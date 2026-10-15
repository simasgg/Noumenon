using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;
using Noumenon.Dsp.Spectral;

namespace Noumenon.Dsp.Sampler;

/// <summary>The analysis window of the Stretch mode, as a length in time; the order is the parameter table's list.</summary>
public enum StretchWindow
{
    Short,
    Medium,
    Long,
}

/// <summary>
/// The sampler's Stretch mode: Paulstretch-style extreme time stretching. A playhead crawls through
/// the loop region at Dir ÷ Stretch Amount of the normal speed (0 freezes it); every hop a window of
/// the sample around it is read — at the pitch ratio, so the Pitch knob transposes without changing
/// the speed — Hann-windowed, transformed, given random phases with every magnitude kept, transformed
/// back and overlap-added at four windows per window length. Random phases make successive windows
/// uncorrelated, so they add in power: the synthesis window is Hann as well and the output is scaled
/// by 4/3 to come out at the input's level. Left and right travel through one complex transform
/// (left real, right imaginary) and share each bin's random phase, so a mono sample stays mono and
/// a stereo one keeps its balance per frequency.
///
/// A window of 16384 points is too much work for one audio callback, so each frame is computed in
/// slices: <see cref="Tick"/>, called every control tick, does a share of the reading, the two
/// transforms and the spectral step, sized so the frame is ready before the hop ends; the hop
/// boundary in <see cref="Process"/> finishes whatever is left (normally nothing), adds the frame and
/// starts the next one. The work happens on the engine's sample grid, so a render stays
/// deterministic and block-size invariant. The overlap-add buffer is linear — shifted by a hop at
/// each boundary — so the window length can change between frames without a gap. Allocation-free
/// after construction; the random phases come from a seeded <see cref="Xoshiro128"/>.
/// </summary>
public sealed class SpectralStretcher
{
    public const int MaxFrameSize = 32768;
    public const int MinFrameSize = 1024;

    private const int Overlap = 4;
    private const int PhaseTableSize = 4096;
    private const int MarginTicks = 4;
    private const float OutputGain = 4f / 3f;

    // The window lengths in seconds: ≈ 4096, 8192 and 16384 points at 48 kHz.
    private static readonly float[] WindowSeconds = [0.085f, 0.17f, 0.34f];

    private enum JobPhase
    {
        Idle,
        Gather,
        Forward,
        Spectrum,
        Inverse,
        Ready,
    }

    private readonly Fft fft = new(MaxFrameSize);
    private readonly float[] re = new float[MaxFrameSize];
    private readonly float[] im = new float[MaxFrameSize];
    private readonly float[] hann = new float[MaxFrameSize];
    private readonly float[] olaLeft = new float[MaxFrameSize];
    private readonly float[] olaRight = new float[MaxFrameSize];
    private readonly float[] phaseCos = new float[PhaseTableSize];
    private readonly float[] phaseSin = new float[PhaseTableSize];
    private readonly Xoshiro128 rng = new(1);

    private double sampleRate = 48000.0;
    private StretchWindow window = StretchWindow.Long;
    private int frameSize = 16384;
    private int hop = 4096;
    private int consumed;
    private int olaUsed;
    private double playhead;
    private float amount = 8f;

    private JobPhase job = JobPhase.Idle;
    private int jobIndex;
    private int jobSize;
    private int jobBudget;
    private FftRun run;
    private SampleData? jobData;
    private double jobCentre;
    private double jobRate;
    private int jobLoopStart;
    private int jobLoopEnd;

    public SpectralStretcher()
    {
        for (var i = 0; i < MaxFrameSize; i++)
        {
            var s = Math.Sin(Math.PI * i / MaxFrameSize);
            hann[i] = (float)(s * s);
        }

        for (var i = 0; i < PhaseTableSize; i++)
        {
            var angle = 2.0 * Math.PI * i / PhaseTableSize;
            phaseCos[i] = (float)Math.Cos(angle);
            phaseSin[i] = (float)Math.Sin(angle);
        }
    }

    /// <summary>The playhead, in frames of the sample.</summary>
    public double Playhead => playhead;

    /// <summary>The window length in points at the current rate.</summary>
    public int FrameSize => frameSize;

    public float Amount => amount;

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;
        frameSize = DesiredFrameSize();
        hop = frameSize / Overlap;
    }

    public void SetWindow(StretchWindow value) => window = value;

    /// <summary>How many times slower than normal the playhead moves: 1 to 100.</summary>
    public void SetAmount(float value) => amount = value < 1f ? 1f : value;

    public void Reseed(ulong seed) => rng.Reseed(seed);

    /// <summary>Starts from silence with the playhead at <paramref name="position"/>; the first frame sounds one hop later.</summary>
    public void Start(double position)
    {
        Array.Clear(olaLeft);
        Array.Clear(olaRight);
        olaUsed = 0;
        consumed = 0;
        playhead = position;
        frameSize = DesiredFrameSize();
        hop = frameSize / Overlap;
        job = JobPhase.Idle;
        jobData = null;
    }

    public void Retrigger(int loopStart, int loopEnd, float direction) => playhead = direction < 0f ? Math.Max(loopStart, loopEnd - 1) : loopStart;

    /// <summary>A control tick's share of the frame being prepared.</summary>
    public void Tick()
    {
        if (job is JobPhase.Idle or JobPhase.Ready)
            return;

        Work(jobBudget);
    }

    /// <summary>
    /// One output frame. <paramref name="rateRatio"/> is file frames per engine sample, <paramref name="pitchRatio"/>
    /// the transposition, <paramref name="direction"/> the signed Dir speed; the loop bounds are in file frames.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(SampleData? data, int loopStart, int loopEnd, double rateRatio, float pitchRatio, float direction, out float left, out float right)
    {
        if (consumed >= hop)
            Boundary(data, loopStart, loopEnd, rateRatio, pitchRatio);
        else if (job == JobPhase.Idle)
            BeginJob(data, loopStart, loopEnd, rateRatio, pitchRatio);

        left = olaLeft[consumed];
        right = olaRight[consumed];
        consumed++;

        if (loopEnd - loopStart >= SamplePlayer.MinLoopFrames)
        {
            playhead += direction * rateRatio / amount;
            playhead = Wrap(playhead, loopStart, loopEnd);
        }
    }

    /// <summary>The window in points for a length choice at a rate: the nearest power of two to its time, within the limits.</summary>
    public static int FrameSizeFor(StretchWindow window, double sampleRate)
    {
        var points = WindowSeconds[Math.Clamp((int)window, 0, WindowSeconds.Length - 1)] * sampleRate;
        var size = 1 << (int)Math.Round(Math.Log2(Math.Max(1.0, points)));

        return Math.Clamp(size, MinFrameSize, MaxFrameSize);
    }

    private int DesiredFrameSize() => FrameSizeFor(window, sampleRate);

    /// <summary>The hop is over: finish the frame, shift the buffer by what was played, add the frame, start the next.</summary>
    private void Boundary(SampleData? data, int loopStart, int loopEnd, double rateRatio, float pitchRatio)
    {
        if (job != JobPhase.Idle && job != JobPhase.Ready)
            Work(int.MaxValue);

        var remaining = olaUsed - consumed;
        if (remaining > 0)
        {
            Array.Copy(olaLeft, consumed, olaLeft, 0, remaining);
            Array.Copy(olaRight, consumed, olaRight, 0, remaining);
            Array.Clear(olaLeft, remaining, consumed);
            Array.Clear(olaRight, remaining, consumed);
        }
        else
        {
            Array.Clear(olaLeft, 0, Math.Max(olaUsed, 0));
            Array.Clear(olaRight, 0, Math.Max(olaUsed, 0));
            remaining = 0;
        }

        olaUsed = remaining;
        consumed = 0;

        if (job == JobPhase.Ready)
        {
            var size = jobSize;
            var stride = MaxFrameSize / size;
            for (var n = 0; n < size; n++)
            {
                var w = hann[n * stride] * OutputGain;
                olaLeft[n] += re[n] * w;
                olaRight[n] += im[n] * w;
            }

            if (size > olaUsed)
                olaUsed = size;
        }

        job = JobPhase.Idle;
        frameSize = DesiredFrameSize();
        hop = frameSize / Overlap;
        BeginJob(data, loopStart, loopEnd, rateRatio, pitchRatio);
    }

    private void BeginJob(SampleData? data, int loopStart, int loopEnd, double rateRatio, float pitchRatio)
    {
        if (data is null || loopEnd - loopStart < SamplePlayer.MinLoopFrames)
        {
            job = JobPhase.Idle;
            jobData = null;
            return;
        }

        jobData = data;
        jobSize = frameSize;
        jobCentre = playhead;
        jobRate = rateRatio * pitchRatio;
        jobLoopStart = loopStart;
        jobLoopEnd = loopEnd;
        jobIndex = 0;
        job = JobPhase.Gather;

        // Gathering a point costs about four butterflies, the spectral step about six per bin pair.
        var log2 = System.Numerics.BitOperations.Log2((uint)jobSize);
        var units = 4L * jobSize + 2L * (jobSize + jobSize / 2 * log2) + 6L * (jobSize / 2) + jobSize;
        var ticks = Math.Max(1, hop / 32 - MarginTicks);
        jobBudget = (int)Math.Max(256, units / ticks + 1);
    }

    private void Work(int budget)
    {
        var data = jobData;
        if (data is null)
        {
            job = JobPhase.Idle;
            return;
        }

        var size = jobSize;
        while (budget > 0)
        {
            switch (job)
            {
                case JobPhase.Gather:
                {
                    var stride = MaxFrameSize / size;
                    var half = size / 2;
                    var end = (int)Math.Min(size, jobIndex + (long)Math.Max(1, budget / 4));
                    var left = data.Left;
                    var right = data.Right;
                    var stereo = data.IsStereo;
                    for (var n = jobIndex; n < end; n++)
                    {
                        var position = Wrap(jobCentre + (n - half) * jobRate, jobLoopStart, jobLoopEnd);
                        var w = hann[n * stride];
                        var l = Interpolation.Hermite(left, position);
                        re[n] = l * w;
                        im[n] = (stereo ? Interpolation.Hermite(right, position) : l) * w;
                    }

                    budget -= (end - jobIndex) * 4;
                    jobIndex = end;
                    if (jobIndex >= size)
                    {
                        run = fft.Begin(size, inverse: false);
                        job = JobPhase.Forward;
                    }

                    break;
                }
                case JobPhase.Forward:
                {
                    if (fft.Step(ref run, re, im, ref budget))
                    {
                        job = JobPhase.Spectrum;
                        jobIndex = 1;
                    }

                    break;
                }
                case JobPhase.Spectrum:
                {
                    var half = size / 2;
                    var end = (int)Math.Min(half, jobIndex + (long)Math.Max(1, budget / 6));
                    for (var k = jobIndex; k < end; k++)
                    {
                        var mirror = size - k;
                        var a = re[k];
                        var b = im[k];
                        var c = re[mirror];
                        var d = im[mirror];
                        var lr = 0.5f * (a + c);
                        var li = 0.5f * (b - d);
                        var rr = 0.5f * (b + d);
                        var ri = 0.5f * (c - a);
                        var magL = MathF.Sqrt(lr * lr + li * li);
                        var magR = MathF.Sqrt(rr * rr + ri * ri);
                        var p = (int)(rng.Next() & (PhaseTableSize - 1));
                        var cs = phaseCos[p];
                        var sn = phaseSin[p];
                        re[k] = magL * cs - magR * sn;
                        im[k] = magL * sn + magR * cs;
                        re[mirror] = magL * cs + magR * sn;
                        im[mirror] = magR * cs - magL * sn;
                    }

                    budget -= (end - jobIndex) * 6;
                    jobIndex = end;
                    if (jobIndex >= half)
                    {
                        re[0] = 0f;
                        im[0] = 0f;
                        re[half] = 0f;
                        im[half] = 0f;
                        run = fft.Begin(size, inverse: true);
                        job = JobPhase.Inverse;
                    }

                    break;
                }
                case JobPhase.Inverse:
                {
                    if (fft.Step(ref run, re, im, ref budget))
                        job = JobPhase.Ready;

                    break;
                }
                default:
                    return;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Wrap(double position, int start, int end)
    {
        var length = end - start;
        if (length <= 0)
            return start;

        if (position >= end || position < start)
        {
            position = (position - start) % length;
            if (position < 0)
                position += length;

            position += start;
        }

        return position;
    }
}
