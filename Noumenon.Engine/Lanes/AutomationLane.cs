using Noumenon.Dsp.Shared;
using Noumenon.Engine.Parameters;

namespace Noumenon.Engine.Lanes;

public enum LaneMode
{
    Static = 0,
    Automated = 1,
}

/// <summary>A lane's recording and settings as one immutable bundle, for presets, the randomizer and the tick to copy in.</summary>
public sealed class LaneSnapshot(float[] points, LaneMode mode, float speedMultiplier)
{
    public float[] Points { get; } = points;
    public LaneMode Mode { get; } = mode;
    public float SpeedMultiplier { get; } = speedMultiplier;
}

/// <summary>
/// One of MF's "faders that play themselves": a loop of values recorded at 50 Hz that replaces
/// the parameter's static value while the lane is Automated. The MF workflow is reproduced
/// through two flags the panel sets: <see cref="Armed"/> (the position display was clicked) and
/// <see cref="Pressed"/> (the fader is being dragged). While both are set the tick records the
/// parameter's live value into a second buffer; on release the buffers swap, the lane stays
/// armed, and switching it to Automated plays the new loop. Loops have their own lengths, so
/// lanes drift in and out of phase with each other. Everything that touches the point arrays
/// runs on the audio thread: recording, playback, and the copying-in of a
/// <see cref="LaneSnapshot"/> handed over by a preset or the randomizer. The panel only reads
/// (a torn read shows one stale point on a display, nothing worse).
/// </summary>
public sealed class AutomationLane
{
    public const int PointsPerSecond = 50;
    public const float MinSpeedMultiplier = 0.25f;
    public const float MaxSpeedMultiplier = 4f;

    private const double PointInterval = 1.0 / PointsPerSecond;

    private readonly ParamInfo info;
    private float[] points;
    private float[] recordBuffer;
    private int count;
    private int recordCount;
    private double phase;
    private double recordAccumulator;
    private float lastValue;
    private float syncStretch = 1f;
    private float speedMultiplier = 1f;
    private int mode;
    private int armed;
    private int pressed;
    private int recording;
    private bool holdOff;   // a take filled the buffer while the fader was still held: wait for a release
    private LaneSnapshot? pending;

    public AutomationLane(ParamInfo info, float maxSeconds)
    {
        this.info = info;
        var capacity = Math.Max(2, (int)(maxSeconds * PointsPerSecond));
        points = new float[capacity];
        recordBuffer = new float[capacity];
        lastValue = info.Default;
    }

    public ParamId Id => info.Id;

    public ParamInfo Info => info;

    public int Capacity => points.Length;

    public float MaxSeconds => Capacity / (float)PointsPerSecond;

    public LaneMode Mode
    {
        get => (LaneMode)Volatile.Read(ref mode);
        set => Volatile.Write(ref mode, (int)value);
    }

    /// <summary>The position display was clicked: the next fader drag records.</summary>
    public bool Armed
    {
        get => Volatile.Read(ref armed) != 0;
        set => Volatile.Write(ref armed, value ? 1 : 0);
    }

    /// <summary>The fader is being dragged (mouse down); with <see cref="Armed"/> this records.</summary>
    public bool Pressed
    {
        get => Volatile.Read(ref pressed) != 0;
        set => Volatile.Write(ref pressed, value ? 1 : 0);
    }

    public bool IsRecording => Volatile.Read(ref recording) != 0;

    public int PointCount => Volatile.Read(ref count);

    public bool HasRecording => PointCount >= 2;

    public float LengthSeconds => PointCount / (float)PointsPerSecond;

    public float RecordedSeconds => Volatile.Read(ref recordCount) / (float)PointsPerSecond;

    /// <summary>Playback position in seconds into the loop.</summary>
    public double Phase => Volatile.Read(ref phase);

    /// <summary>The value the lane last produced (or passed through), for the fader display.</summary>
    public float Value => Volatile.Read(ref lastValue);

    public float SpeedMultiplier
    {
        get => Volatile.Read(ref speedMultiplier);
        set => Volatile.Write(ref speedMultiplier, DspHelper.Clamp(value, MinSpeedMultiplier, MaxSpeedMultiplier));
    }

    /// <summary>Playback-rate factor that makes the loop span a whole number of host bars (1 when unsynced).</summary>
    public float SyncStretch => syncStretch;

    public bool IsPlaying => Mode == LaneMode.Automated && HasRecording && !IsRecording;

    /// <summary>Hands a recording to the lane from any thread; the next tick copies it in and rewinds.</summary>
    public void Load(LaneSnapshot snapshot) => Volatile.Write(ref pending, snapshot);

    public void Clear() => Load(new LaneSnapshot([], LaneMode.Static, SpeedMultiplier));

    public LaneSnapshot Capture()
    {
        var n = PointCount;
        var copy = new float[n];
        Array.Copy(points, copy, n);

        return new LaneSnapshot(copy, Mode, SpeedMultiplier);
    }

    /// <summary>Copies up to <paramref name="destination"/>.Length recorded points for a display; returns how many.</summary>
    public int CopyPoints(float[] destination)
    {
        var n = Math.Min(PointCount, destination.Length);
        Array.Copy(points, destination, n);

        return n;
    }

    internal void Restart() => phase = 0.0;

    /// <summary>
    /// One control tick on the audio thread. Returns true when a recording started or ended (the
    /// clock's Restart mode acts on that); <paramref name="overrides"/> says whether
    /// <paramref name="value"/> replaces the parameter's static value this tick.
    /// </summary>
    internal bool Tick(double seconds, float clockSpeed, float barSeconds, float sourceValue, out bool overrides, out float value)
    {
        ApplyPending();

        var edge = false;
        var isArmed = armed != 0;
        var isPressed = pressed != 0;
        var isRecording = recording != 0;
        if (!isPressed)
            holdOff = false;

        if (!isRecording && isArmed && isPressed && !holdOff)
        {
            BeginRecording();
            isRecording = true;
            edge = true;
        }
        else if (isRecording && (!isPressed || !isArmed))
        {
            EndRecording(barSeconds);
            isRecording = false;
            edge = true;
        }

        if (isRecording)
        {
            recordAccumulator += seconds;
            while (recordAccumulator >= PointInterval)
            {
                recordAccumulator -= PointInterval;
                if (recordCount < recordBuffer.Length)
                {
                    recordBuffer[recordCount] = sourceValue;
                    Volatile.Write(ref recordCount, recordCount + 1);
                }
                else
                {
                    EndRecording(barSeconds);
                    holdOff = true;
                    edge = true;
                    break;
                }
            }

            Volatile.Write(ref lastValue, sourceValue);
            overrides = false;
            value = sourceValue;

            return edge;
        }

        if (mode == (int)LaneMode.Automated && count >= 2)
        {
            // Read first, then advance: the tick after a load or a restart yields point 0 exactly.
            value = Interpolate(phase);
            var length = count * PointInterval;
            phase += seconds * clockSpeed * speedMultiplier * syncStretch;
            if (phase >= length || phase < 0.0)
            {
                phase %= length;
                if (phase < 0.0)
                    phase += length;
            }

            Volatile.Write(ref lastValue, value);
            overrides = true;

            return edge;
        }

        Volatile.Write(ref lastValue, sourceValue);
        overrides = false;
        value = sourceValue;

        return edge;
    }

    private float Interpolate(double seconds)
    {
        var position = seconds * PointsPerSecond;
        var i0 = (int)position;
        if (i0 >= count)
            i0 = count - 1;

        var frac = (float)(position - i0);
        var i1 = i0 + 1 < count ? i0 + 1 : 0;

        return points[i0] + (points[i1] - points[i0]) * frac;
    }

    private void BeginRecording()
    {
        Volatile.Write(ref recordCount, 0);
        recordAccumulator = PointInterval;   // capture the first point at once
        Volatile.Write(ref recording, 1);
    }

    private void EndRecording(float barSeconds)
    {
        (points, recordBuffer) = (recordBuffer, points);
        Volatile.Write(ref count, recordCount);
        Volatile.Write(ref recordCount, 0);
        phase = 0.0;
        syncStretch = SyncStretchFor(count / (float)PointsPerSecond, barSeconds);
        Volatile.Write(ref recording, 0);
    }

    private void ApplyPending()
    {
        var snapshot = Interlocked.Exchange(ref pending, null);
        if (snapshot is null)
            return;

        var n = Math.Min(snapshot.Points.Length, recordBuffer.Length);
        Array.Copy(snapshot.Points, recordBuffer, n);
        (points, recordBuffer) = (recordBuffer, points);
        Volatile.Write(ref count, n);
        Volatile.Write(ref recordCount, 0);
        Volatile.Write(ref recording, 0);
        Volatile.Write(ref mode, (int)snapshot.Mode);
        Volatile.Write(ref speedMultiplier, DspHelper.Clamp(snapshot.SpeedMultiplier, MinSpeedMultiplier, MaxSpeedMultiplier));
        phase = 0.0;
        syncStretch = 1f;
    }

    /// <summary>With host sync the loop is stretched to the nearest whole number of bars (1–64).</summary>
    internal static float SyncStretchFor(float lengthSeconds, float barSeconds)
    {
        if (barSeconds <= 0f || lengthSeconds <= 0f)
            return 1f;

        var bars = Math.Clamp((int)MathF.Round(lengthSeconds / barSeconds), 1, 64);

        return lengthSeconds / (bars * barSeconds);
    }
}
