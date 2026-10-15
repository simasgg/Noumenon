using System.Runtime.CompilerServices;
using Noumenon.Dsp.Sampler;
using Noumenon.Dsp.Spectral;

namespace Noumenon.Dsp.Oscillators;

/// <summary>
/// The oscillators' Wavetable wave: up to 32 single-cycle frames cut from a sample, each stored as
/// a stack of band-limited copies (a mip-map) so any pitch plays without aliasing. Level 0 keeps
/// all 1023 harmonics of a 2048-point cycle, each level above keeps half as many, and a slot
/// reads the level whose top harmonic stays below Nyquist at its pitch (one octave of headroom is
/// given up for that guarantee, never more). A level is stored at 8 points per harmonic (256 to
/// 2048 points) and read with Hermite interpolation, whose images sit far below the harmonics.
/// Shape scans the frames, blending neighbours linearly.
///
/// How a sample becomes frames: a file of at most 4096 frames is one cycle (a single-cycle
/// waveform, resampled to 2048 points); a file that is a whole number (up to 256) of 2048-point
/// cycles (the common wavetable format) gives up to 32 of its cycles, evenly picked; anything else gives 32
/// windows of 2048 points evenly spread over the recording, each made cyclic by crossfading the
/// material that follows it into its start. Every frame loses its DC and is normalized to a peak of
/// 1, by at most +24 dB, so scanning a recording keeps an even level without lifting near-silence
/// into noise. Building runs FFTs and allocates (about 1.2 MB): it happens once per sample, off the
/// audio thread (<see cref="SampleData.BuildWavetable"/>); reading is allocation-free.
/// </summary>
public sealed class Wavetable
{
    public const int FrameSize = 2048;
    public const int MaxFrames = 32;
    public const int LevelCount = 11;

    private const int SingleCycleLimit = 4096;
    private const int MaxCycleFile = 256;
    private const int CrossfadeFrames = 256;
    private const float MaxNormalizeGain = 16f;

    private readonly float[][] levels;
    private readonly int[] sizes;

    private Wavetable(int frameCount, float[][] levels, int[] sizes)
    {
        FrameCount = frameCount;
        this.levels = levels;
        this.sizes = sizes;
    }

    public int FrameCount { get; }

    /// <summary>The highest harmonic a level keeps: 1023, 512, 256, … 1.</summary>
    public static int Harmonics(int level) => level == 0 ? FrameSize / 2 - 1 : (FrameSize / 2) >> level;

    /// <summary>The band-limited level for a pitch of <paramref name="increment"/> cycles per sample: the lowest one whose top harmonic stays below Nyquist.</summary>
    public static int LevelFor(double increment)
    {
        if (increment * FrameSize <= 1.0)
            return 0;

        var level = (int)Math.Ceiling(Math.Log2(increment * FrameSize) - 1e-9);

        return Math.Clamp(level, 0, LevelCount - 1);
    }

    /// <param name="phase">Position in the cycle, 0–1.</param>
    /// <param name="position">Position across the frames, 0–1.</param>
    /// <param name="level">From <see cref="LevelFor"/>.</param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Read(double phase, float position, int level)
    {
        var size = sizes[level];
        var table = levels[level];
        var last = FrameCount - 1;
        var fp = position * last;
        var f0 = fp <= 0f ? 0 : fp >= last ? last : (int)fp;
        var f1 = f0 < last ? f0 + 1 : last;
        var t = fp - f0;

        var x = phase * size;
        var i0 = (int)x;
        var frac = (float)(x - i0);
        var mask = size - 1;
        i0 &= mask;
        var im1 = (i0 - 1) & mask;
        var i1 = (i0 + 1) & mask;
        var i2 = (i0 + 2) & mask;
        var a = Hermite(table, f0 * size, im1, i0, i1, i2, frac);
        if (f1 == f0)
            return a;

        var b = Hermite(table, f1 * size, im1, i0, i1, i2, frac);

        return a + (b - a) * t;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Hermite(float[] table, int offset, int im1, int i0, int i1, int i2, float t)
    {
        var ym1 = table[offset + im1];
        var y0 = table[offset + i0];
        var y1 = table[offset + i1];
        var y2 = table[offset + i2];
        var c1 = 0.5f * (y1 - ym1);
        var c2 = ym1 - 2.5f * y0 + 2f * y1 - 0.5f * y2;
        var c3 = 0.5f * (y2 - ym1) + 1.5f * (y0 - y1);

        return ((c3 * t + c2) * t + c1) * t + y0;
    }

    public static Wavetable FromSample(SampleData data)
    {
        var length = data.Length;
        var mono = new float[length];
        for (var i = 0; i < length; i++)
            mono[i] = 0.5f * (data.Left[i] + data.Right[i]);

        if (length < 2)
            return FromCycles([new float[FrameSize]]);

        if (length <= SingleCycleLimit)
            return FromCycles([Resample(mono)]);

        if (length % FrameSize == 0 && length / FrameSize <= MaxCycleFile)
        {
            var cycles = length / FrameSize;
            var count = Math.Min(MaxFrames, cycles);
            var frames = new float[count][];
            for (var f = 0; f < count; f++)
            {
                var cycle = count == 1 ? 0 : (int)((long)(cycles - 1) * f / (count - 1));
                frames[f] = mono.AsSpan(cycle * FrameSize, FrameSize).ToArray();
            }

            return FromCycles(frames);
        }

        var usable = length - FrameSize - CrossfadeFrames;
        var windows = new float[MaxFrames][];
        for (var f = 0; f < MaxFrames; f++)
        {
            var start = (int)((long)usable * f / (MaxFrames - 1));
            var frame = mono.AsSpan(start, FrameSize).ToArray();
            for (var i = 0; i < CrossfadeFrames; i++)
            {
                var t = (i + 0.5f) / CrossfadeFrames;
                frame[i] = frame[i] * t + mono[start + FrameSize + i] * (1f - t);
            }

            windows[f] = frame;
        }

        return FromCycles(windows);
    }

    /// <summary>Builds the table from cycles of exactly <see cref="FrameSize"/> points (at most <see cref="MaxFrames"/>).</summary>
    public static Wavetable FromCycles(IReadOnlyList<float[]> cycles)
    {
        if (cycles.Count == 0 || cycles.Count > MaxFrames)
            throw new ArgumentException($"A wavetable has 1 to {MaxFrames} frames.", nameof(cycles));

        var frameCount = cycles.Count;
        var sizes = new int[LevelCount];
        var levels = new float[LevelCount][];
        for (var l = 0; l < LevelCount; l++)
        {
            sizes[l] = Math.Clamp((int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)(8 * Harmonics(l))), 256, FrameSize);
            levels[l] = new float[frameCount * sizes[l]];
        }

        var fft = new Fft(FrameSize);
        var specRe = new float[FrameSize];
        var specIm = new float[FrameSize];
        var re = new float[FrameSize];
        var im = new float[FrameSize];
        for (var f = 0; f < frameCount; f++)
        {
            var cycle = cycles[f];
            if (cycle.Length != FrameSize)
                throw new ArgumentException($"Every cycle has {FrameSize} points.", nameof(cycles));

            Array.Copy(cycle, specRe, FrameSize);
            Array.Clear(specIm);
            fft.Forward(specRe, specIm, FrameSize);

            for (var l = 0; l < LevelCount; l++)
            {
                var size = sizes[l];
                var keep = Math.Min(Harmonics(l), size / 2 - 1);
                Array.Clear(re, 0, size);
                Array.Clear(im, 0, size);

                // The 2048-point spectrum played back through a smaller inverse keeps its amplitudes when scaled by size / 2048.
                var scale = size / (float)FrameSize;
                for (var k = 1; k <= keep; k++)
                {
                    re[k] = specRe[k] * scale;
                    im[k] = specIm[k] * scale;
                    re[size - k] = specRe[k] * scale;
                    im[size - k] = -specIm[k] * scale;
                }

                fft.Inverse(re, im, size);
                Array.Copy(re, 0, levels[l], f * size, size);
            }

            // Normalize the frame by its full-band peak, the same factor at every level.
            var peak = 0f;
            var full = levels[0];
            for (var i = 0; i < sizes[0]; i++)
                peak = MathF.Max(peak, MathF.Abs(full[f * sizes[0] + i]));

            var gain = peak > 0f ? MathF.Min(MaxNormalizeGain, 1f / peak) : 0f;
            for (var l = 0; l < LevelCount; l++)
            {
                var table = levels[l];
                var size = sizes[l];
                for (var i = 0; i < size; i++)
                    table[f * size + i] *= gain;
            }
        }

        return new Wavetable(frameCount, levels, sizes);
    }

    /// <summary>One cycle of any length, read periodically with Hermite interpolation onto <see cref="FrameSize"/> points.</summary>
    private static float[] Resample(float[] cycle)
    {
        var n = cycle.Length;
        var frame = new float[FrameSize];
        for (var i = 0; i < FrameSize; i++)
        {
            var x = i * (double)n / FrameSize;
            var i0 = (int)x;
            var t = (float)(x - i0);
            var ym1 = cycle[(i0 - 1 + n) % n];
            var y0 = cycle[i0 % n];
            var y1 = cycle[(i0 + 1) % n];
            var y2 = cycle[(i0 + 2) % n];
            var c1 = 0.5f * (y1 - ym1);
            var c2 = ym1 - 2.5f * y0 + 2f * y1 - 0.5f * y2;
            var c3 = 0.5f * (y2 - ym1) + 1.5f * (y0 - y1);
            frame[i] = ((c3 * t + c2) * t + c1) * t + y0;
        }

        return frame;
    }
}
