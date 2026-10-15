using Noumenon.Dsp.Spectral;

namespace Noumenon.Tests;

/// <summary>
/// The engine's own FFT: a round trip returns the input, a cosine lands in its bin with the
/// expected magnitude, smaller sizes read the shared twiddles correctly, and a transform run in
/// small slices gives exactly what one call gives.
/// </summary>
public class FftTests
{
    private static (float[] re, float[] im) Noise(int n, int seed)
    {
        var random = new Random(seed);
        var re = new float[n];
        var im = new float[n];
        for (var i = 0; i < n; i++)
        {
            re[i] = (float)(random.NextDouble() * 2 - 1);
            im[i] = (float)(random.NextDouble() * 2 - 1);
        }

        return (re, im);
    }

    [Theory]
    [InlineData(16)]
    [InlineData(1024)]
    [InlineData(16384)]
    public void Forward_Then_Inverse_Returns_The_Input(int n)
    {
        var fft = new Fft(32768);
        var (re, im) = Noise(n, n);
        var originalRe = (float[])re.Clone();
        var originalIm = (float[])im.Clone();

        fft.Forward(re, im, n);
        fft.Inverse(re, im, n);

        for (var i = 0; i < n; i++)
        {
            Assert.True(MathF.Abs(re[i] - originalRe[i]) < 1e-4f, $"re[{i}] {re[i]} vs {originalRe[i]}");
            Assert.True(MathF.Abs(im[i] - originalIm[i]) < 1e-4f, $"im[{i}] {im[i]} vs {originalIm[i]}");
        }
    }

    [Fact]
    public void A_Cosine_Lands_In_Its_Bin()
    {
        const int n = 2048;
        const int bin = 37;
        var fft = new Fft(n);
        var re = new float[n];
        var im = new float[n];
        for (var i = 0; i < n; i++)
            re[i] = MathF.Cos(2f * MathF.PI * bin * i / n);

        fft.Forward(re, im, n);

        for (var k = 0; k < n; k++)
        {
            var magnitude = MathF.Sqrt(re[k] * re[k] + im[k] * im[k]);
            if (k == bin || k == n - bin)
                Assert.Equal(n / 2f, magnitude, 1);
            else
                Assert.True(magnitude < 0.05f, $"bin {k}: {magnitude}");
        }
    }

    [Fact]
    public void A_Transform_In_Slices_Equals_One_Call()
    {
        const int n = 8192;
        var fft = new Fft(n);
        var (re, im) = Noise(n, 3);
        var sliceRe = (float[])re.Clone();
        var sliceIm = (float[])im.Clone();

        fft.Forward(re, im, n);
        var run = fft.Begin(n, inverse: false);
        var calls = 0;
        while (true)
        {
            var budget = 37;
            calls++;
            if (fft.Step(ref run, sliceRe, sliceIm, ref budget))
                break;

            Assert.True(budget <= 0, "a slice that is not done uses its whole budget");
        }

        Assert.True(calls > 100, $"the transform should have taken many slices ({calls})");
        Assert.True(run.IsDone);
        Assert.Equal(re, sliceRe);
        Assert.Equal(im, sliceIm);
    }
}
