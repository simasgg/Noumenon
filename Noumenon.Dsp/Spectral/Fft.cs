namespace Noumenon.Dsp.Spectral;

/// <summary>
/// Where a transform started with <see cref="Fft.Begin"/> has got to: the stage it is in and the
/// element within it. A plain value, so whoever runs a transform in slices keeps it in a field.
/// </summary>
public struct FftRun
{
    internal int Size;
    internal int Log2;
    internal bool Inverse;
    internal int Phase;
    internal int Index;
    internal int Reversed;
    internal int Stage;

    public readonly bool IsDone => Phase == Fft.PhaseDone;
}

/// <summary>
/// An in-place radix-2 complex FFT on separate real and imaginary float arrays, for the sampler's
/// Stretch mode and the wavetable builder. The twiddles are computed once (in double) for
/// <see cref="MaxSize"/>, and any smaller power of two reads them with a stride, so one instance
/// serves every size up to it. The forward transform is unscaled; the inverse scales by 1/N, so a
/// round trip returns the input. A transform runs in one call, or in slices of a bounded number of
/// operations (<see cref="Begin"/> / <see cref="Step"/>) — a bit-reversal swap, a butterfly or a
/// scaling multiply each count as one — which is how the stretch mode spreads a 16384-point
/// transform over many control ticks instead of spending it inside one audio callback. Nothing
/// allocates after construction.
/// </summary>
public sealed class Fft
{
    internal const int PhaseBitReverse = 0;
    internal const int PhaseButterflies = 1;
    internal const int PhaseScale = 2;
    internal const int PhaseDone = 3;

    private readonly float[] cos;
    private readonly float[] sin;

    public Fft(int maxSize)
    {
        if (maxSize < 2 || (maxSize & (maxSize - 1)) != 0)
            throw new ArgumentException("The size must be a power of two of at least 2.", nameof(maxSize));

        MaxSize = maxSize;
        cos = new float[maxSize / 2];
        sin = new float[maxSize / 2];
        for (var t = 0; t < cos.Length; t++)
        {
            var angle = 2.0 * Math.PI * t / maxSize;
            cos[t] = (float)Math.Cos(angle);
            sin[t] = (float)Math.Sin(angle);
        }
    }

    public int MaxSize { get; }

    /// <summary>cos(2π·t/<see cref="MaxSize"/>) for t below half the size: the table the twiddles come from, for windows built at the same sizes.</summary>
    public float CosTable(int t) => cos[t];

    public void Forward(float[] re, float[] im, int size)
    {
        var run = Begin(size, inverse: false);
        var budget = int.MaxValue;
        Step(ref run, re, im, ref budget);
    }

    public void Inverse(float[] re, float[] im, int size)
    {
        var run = Begin(size, inverse: true);
        var budget = int.MaxValue;
        Step(ref run, re, im, ref budget);
    }

    public FftRun Begin(int size, bool inverse)
    {
        if (size < 2 || size > MaxSize || (size & (size - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(size), size, $"A transform size is a power of two between 2 and {MaxSize}.");

        return new FftRun { Size = size, Log2 = System.Numerics.BitOperations.Log2((uint)size), Inverse = inverse, Phase = PhaseBitReverse, Index = 1 };
    }

    /// <summary>Runs up to <paramref name="budget"/> operations of the transform, taking what it used off it; returns true once the transform is complete.</summary>
    public bool Step(ref FftRun run, float[] re, float[] im, ref int budget)
    {
        var n = run.Size;
        while (budget > 0 && run.Phase != PhaseDone)
        {
            switch (run.Phase)
            {
                case PhaseBitReverse:
                {
                    // The bit-reversed partner j of i is carried along incrementally (no table).
                    var i = run.Index;
                    var j = run.Reversed;
                    var end = (int)Math.Min(n, (long)i + budget);
                    for (; i < end; i++)
                    {
                        var bit = n >> 1;
                        for (; (j & bit) != 0; bit >>= 1)
                            j ^= bit;

                        j ^= bit;
                        if (i < j)
                        {
                            (re[i], re[j]) = (re[j], re[i]);
                            (im[i], im[j]) = (im[j], im[i]);
                        }
                    }

                    budget -= end - run.Index;
                    run.Index = i;
                    run.Reversed = j;
                    if (i >= n)
                    {
                        run.Phase = PhaseButterflies;
                        run.Index = 0;
                        run.Stage = 0;
                    }

                    break;
                }
                case PhaseButterflies:
                {
                    // Stage s joins groups of 2^(s+1): butterfly b is element j = b mod 2^s of group b / 2^s.
                    var stage = run.Stage;
                    var half = 1 << stage;
                    var twiddleStep = MaxSize >> (stage + 1);
                    var sign = run.Inverse ? 1f : -1f;
                    var total = n >> 1;
                    var b = run.Index;
                    var end = (int)Math.Min(total, (long)b + budget);
                    for (; b < end; b++)
                    {
                        var j = b & (half - 1);
                        var i = ((b >> stage) << (stage + 1)) + j;
                        var k = i + half;
                        var t = j * twiddleStep;
                        var wr = cos[t];
                        var wi = sign * sin[t];
                        var xr = re[k] * wr - im[k] * wi;
                        var xi = re[k] * wi + im[k] * wr;
                        re[k] = re[i] - xr;
                        im[k] = im[i] - xi;
                        re[i] += xr;
                        im[i] += xi;
                    }

                    budget -= end - run.Index;
                    run.Index = b;
                    if (b >= total)
                    {
                        run.Index = 0;
                        run.Stage++;
                        if (run.Stage >= run.Log2)
                            run.Phase = run.Inverse ? PhaseScale : PhaseDone;
                    }

                    break;
                }
                default:
                {
                    var scale = 1f / n;
                    var i = run.Index;
                    var end = (int)Math.Min(n, (long)i + budget);
                    for (; i < end; i++)
                    {
                        re[i] *= scale;
                        im[i] *= scale;
                    }

                    budget -= end - run.Index;
                    run.Index = i;
                    if (i >= n)
                        run.Phase = PhaseDone;

                    break;
                }
            }
        }

        return run.Phase == PhaseDone;
    }
}
