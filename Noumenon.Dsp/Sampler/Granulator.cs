using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Sampler;

/// <summary>Where grains come from; the order is the parameter table's list.</summary>
public enum GrainScan
{
    /// <summary>From the Position parameter: the cloud stays where it is put (a lane or an LFO moves it).</summary>
    Free,

    /// <summary>From a playhead that moves through the loop at Dir speed, with Position as an offset: the sample keeps its timing while pitch stays free.</summary>
    Locked,
}

/// <summary>
/// The sampler's Granular mode: a pool of 32 grains, each a short windowed read of the sample at
/// its own position, rate and pan. A new grain starts every 1/Density seconds (Spray also jitters
/// the timing by up to half an interval); its start is Position across the loop region — or, when
/// locked, a playhead crossing the loop at Dir speed — plus a random offset of up to Spray × the
/// loop length; its rate is the pitch ratio times a random spread of up to ±Pitch Spread semitones,
/// reversed when Dir is negative; its pan is random within Width, equal-power. Grains read the
/// whole sample (they start inside the loop but may run past its end) and wrap at the sample's
/// edges. The window runs from Hann (Window 0) to a flat-topped Tukey that tapers only its last
/// 5 % (Window 1). Each grain is scaled by 1/√overlap so the cloud's level stays near the
/// sample's whatever Size and Density do. A grain keeps the sample it started with, so a new sample
/// fades in grain by grain. When all 32 are busy a new grain is skipped. Allocation-free; the
/// randomness comes from a seeded <see cref="Xoshiro128"/>.
/// </summary>
public sealed class Granulator
{
    public const int MaxGrains = 32;

    private const int WindowTableSize = 512;
    private const float MinTaper = 0.05f;
    private const int MinGrainSamples = 16;
    private const float Sqrt2 = 1.41421356f;

    private struct Grain
    {
        public SampleData? Data;
        public double Position;
        public double Increment;
        public float Phase;
        public float PhaseStep;
        public float GainLeft;
        public float GainRight;
        public float HalfTaper;
    }

    private readonly Grain[] grains = new Grain[MaxGrains];
    private readonly float[] rise = new float[WindowTableSize + 2];
    private readonly Xoshiro128 rng = new(1);

    private double sampleRate = 48000.0;
    private double untilNext;
    private double scan;
    private int active;
    private float sizeSeconds = 0.08f;
    private float density = 8f;
    private float position;
    private float spray = 0.1f;
    private float pitchSpread;
    private float halfTaper = 0.5f;
    private float width = 0.5f;
    private GrainScan scanMode;

    public Granulator()
    {
        for (var i = 0; i < rise.Length; i++)
        {
            var x = Math.Min(1.0, i / (double)WindowTableSize);
            rise[i] = (float)(0.5 - 0.5 * Math.Cos(Math.PI * x));
        }
    }

    public int ActiveGrains => active;

    /// <summary>The locked playhead, in frames of the sample.</summary>
    public double ScanPosition => scan;

    public GrainScan Scan => scanMode;

    public float Position => position;

    public void Prepare(double sampleRate) => this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;

    public void Reseed(ulong seed) => rng.Reseed(seed);

    /// <param name="sizeMilliseconds">Grain length.</param>
    /// <param name="grainsPerSecond">Density.</param>
    /// <param name="positionFraction">0–1 across the loop region.</param>
    /// <param name="sprayFraction">0–1 of the loop length, the random scatter of start positions.</param>
    /// <param name="spreadSemitones">The random transposition of each grain, ±.</param>
    /// <param name="windowShape">0 = Hann, 1 = flat-topped Tukey.</param>
    /// <param name="stereoWidth">0 = every grain centred, 1 = anywhere from left to right.</param>
    public void Set(float sizeMilliseconds, float grainsPerSecond, float positionFraction, float sprayFraction, float spreadSemitones, float windowShape, float stereoWidth, GrainScan scanMode)
    {
        sizeSeconds = MathF.Max(0.001f, sizeMilliseconds / 1000f);
        density = MathF.Max(0.01f, grainsPerSecond);
        position = DspHelper.Clamp01(positionFraction);
        spray = DspHelper.Clamp01(sprayFraction);
        pitchSpread = MathF.Max(0f, spreadSemitones);
        halfTaper = 0.5f * (1f - (1f - MinTaper) * DspHelper.Clamp01(windowShape));
        width = DspHelper.Clamp01(stereoWidth);
        this.scanMode = scanMode;
    }

    /// <summary>Silences every grain and puts the locked playhead at <paramref name="playhead"/>; the next sample starts a grain.</summary>
    public void Start(double playhead)
    {
        for (var i = 0; i < grains.Length; i++)
            grains[i].Data = null;

        active = 0;
        scan = playhead;
        untilNext = 0.0;
    }

    public void Retrigger(int loopStart, int loopEnd, float direction)
    {
        scan = direction < 0f ? Math.Max(loopStart, loopEnd - 1) : loopStart;
        untilNext = 0.0;
    }

    /// <summary>
    /// One output frame. <paramref name="rateRatio"/> is file frames per engine sample,
    /// <paramref name="pitchRatio"/> the transposition, <paramref name="direction"/> the signed Dir
    /// speed (it moves the locked playhead and its sign reverses new grains); loop bounds in file frames.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(SampleData? data, int loopStart, int loopEnd, double rateRatio, float pitchRatio, float direction, out float left, out float right)
    {
        var loopLength = loopEnd - loopStart;
        if (data is not null && loopLength >= SamplePlayer.MinLoopFrames)
        {
            if (scanMode == GrainScan.Locked)
                scan = Wrap(scan + direction * rateRatio, loopStart, loopEnd);

            untilNext -= 1.0;
            if (untilNext <= 0.0)
            {
                Spawn(data, loopStart, loopLength, rateRatio, pitchRatio, direction);
                var interval = sampleRate / density;
                untilNext += interval * (1.0 + 0.5 * spray * rng.NextBipolar());
                if (untilNext <= 0.0)
                    untilNext = interval;
            }
        }

        var l = 0f;
        var r = 0f;
        if (active > 0)
        {
            for (var i = 0; i < grains.Length; i++)
            {
                ref var grain = ref grains[i];
                var source = grain.Data;
                if (source is null)
                    continue;

                var w = Window(grain.Phase, grain.HalfTaper);
                var p = grain.Position;
                var sl = Interpolation.Hermite(source.Left, p);
                var sr = source.IsStereo ? Interpolation.Hermite(source.Right, p) : sl;
                l += sl * w * grain.GainLeft;
                r += sr * w * grain.GainRight;

                p += grain.Increment;
                var length = source.Length;
                if (p >= length)
                    p -= length;
                else if (p < 0.0)
                    p += length;

                grain.Position = p;
                grain.Phase += grain.PhaseStep;
                if (grain.Phase >= 1f)
                {
                    grain.Data = null;
                    active--;
                }
            }
        }

        left = l;
        right = r;
    }

    /// <summary>Copies where the sounding grains are (fractions of their sample) and their window levels, for a display; returns how many.</summary>
    public int CopyGrains(float[] positions, float[] levels)
    {
        var n = 0;
        for (var i = 0; i < grains.Length && n < positions.Length && n < levels.Length; i++)
        {
            var source = grains[i].Data;
            if (source is null || source.Length == 0)
                continue;

            positions[n] = (float)(grains[i].Position / source.Length);
            levels[n] = Window(grains[i].Phase, grains[i].HalfTaper);
            n++;
        }

        return n;
    }

    private void Spawn(SampleData data, int loopStart, int loopLength, double rateRatio, float pitchRatio, float direction)
    {
        var slot = -1;
        for (var i = 0; i < grains.Length; i++)
        {
            if (grains[i].Data is null)
            {
                slot = i;
                break;
            }
        }

        if (slot < 0)
            return;

        var centre = scanMode == GrainScan.Locked ? scan + position * loopLength : loopStart + position * loopLength;
        var start = Wrap(centre + spray * loopLength * (rng.NextFloat() - 0.5f), loopStart, loopStart + loopLength);
        var semitones = pitchSpread > 0f ? pitchSpread * rng.NextBipolar() : 0f;
        var increment = rateRatio * pitchRatio * MathF.Pow(2f, semitones / 12f);
        if (direction < 0f)
            increment = -increment;

        var samples = Math.Max(MinGrainSamples, sizeSeconds * sampleRate);
        var pan = width > 0f ? width * rng.NextBipolar() : 0f;
        var angle = (pan + 1f) * (MathF.PI * 0.25f);
        var overlap = density * sizeSeconds * (1f - halfTaper);
        var norm = 1f / MathF.Sqrt(MathF.Max(1f, overlap));

        ref var grain = ref grains[slot];
        grain.Data = data;
        grain.Position = start;
        grain.Increment = increment;
        grain.Phase = 0f;
        grain.PhaseStep = (float)(1.0 / samples);
        grain.GainLeft = MathF.Cos(angle) * Sqrt2 * norm;
        grain.GainRight = MathF.Sin(angle) * Sqrt2 * norm;
        grain.HalfTaper = halfTaper;
        active++;
    }

    /// <summary>A Tukey window over phase 0–1 whose cosine tapers each take <paramref name="half"/> of it (0.5 = Hann).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float Window(float phase, float half)
    {
        float x;
        if (phase < half)
            x = phase / half;
        else if (phase > 1f - half)
            x = (1f - phase) / half;
        else
            return 1f;

        if (x <= 0f)
            return 0f;

        var f = x * WindowTableSize;
        var i = (int)f;
        if (i >= WindowTableSize)
            return 1f;

        var frac = f - i;

        return rise[i] + (rise[i + 1] - rise[i]) * frac;
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
