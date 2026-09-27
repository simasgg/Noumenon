namespace Noumenon.Tests;

/// <summary>
/// Test-side spectrum analysis: a Blackman-Harris windowed radix-2 FFT (sidelobes below −90 dB, so
/// a −80 dB alias floor is measurable next to a harmonic) and an alias-floor measurement for
/// harmonic test tones.
/// </summary>
internal static class Spectrum
{
    /// <summary>Magnitude in dB for bins 0..n/2 of the first <paramref name="n"/> samples (n a power of two).</summary>
    public static double[] MagnitudeDb(float[] signal, int n)
    {
        if ((n & (n - 1)) != 0)
            throw new ArgumentException("n must be a power of two", nameof(n));

        var re = new double[n];
        var im = new double[n];
        for (var i = 0; i < n; i++)
        {
            var x = 2.0 * Math.PI * i / (n - 1);
            var w = 0.35875 - 0.48829 * Math.Cos(x) + 0.14128 * Math.Cos(2 * x) - 0.01168 * Math.Cos(3 * x);
            re[i] = signal[i] * w;
        }

        Fft(re, im);

        var mags = new double[n / 2 + 1];
        for (var k = 0; k < mags.Length; k++)
            mags[k] = 20.0 * Math.Log10(Math.Sqrt(re[k] * re[k] + im[k] * im[k]) + 1e-30);

        return mags;
    }

    /// <summary>
    /// The loudest bin below <paramref name="maxHz"/> that is not within <paramref name="excludeBins"/>
    /// of a harmonic of <paramref name="f0"/> (DC counts as harmonic 0), in dB relative to the
    /// fundamental's peak. For a band-limited harmonic tone this is the aliasing floor.
    /// </summary>
    public static double AliasFloorDb(double[] magsDb, double binHz, double f0, double maxHz, int excludeBins)
    {
        var fundamentalBin = (int)Math.Round(f0 / binHz);
        var fundamental = double.NegativeInfinity;
        for (var k = Math.Max(0, fundamentalBin - excludeBins); k <= fundamentalBin + excludeBins; k++)
            fundamental = Math.Max(fundamental, magsDb[k]);

        var floor = double.NegativeInfinity;
        var maxBin = Math.Min(magsDb.Length - 1, (int)(maxHz / binHz));
        for (var k = 0; k <= maxBin; k++)
        {
            var nearestHarmonic = Math.Round(k * binHz / f0) * f0 / binHz;
            if (Math.Abs(k - nearestHarmonic) <= excludeBins)
                continue;

            floor = Math.Max(floor, magsDb[k]);
        }

        return floor - fundamental;
    }

    private static void Fft(double[] re, double[] im)
    {
        var n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
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

        for (var len = 2; len <= n; len <<= 1)
        {
            var angle = -2.0 * Math.PI / len;
            var wRe = Math.Cos(angle);
            var wIm = Math.Sin(angle);
            for (var i = 0; i < n; i += len)
            {
                var curRe = 1.0;
                var curIm = 0.0;
                for (var j = 0; j < len / 2; j++)
                {
                    var a = i + j;
                    var b = a + len / 2;
                    var tRe = re[b] * curRe - im[b] * curIm;
                    var tIm = re[b] * curIm + im[b] * curRe;
                    re[b] = re[a] - tRe;
                    im[b] = im[a] - tIm;
                    re[a] += tRe;
                    im[a] += tIm;
                    var nextRe = curRe * wRe - curIm * wIm;
                    curIm = curRe * wIm + curIm * wRe;
                    curRe = nextRe;
                }
            }
        }
    }
}
