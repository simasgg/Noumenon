using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Shared;

namespace Noumenon.Tests;

/// <summary>The chain's filter building blocks: RBJ biquads, the shelf EQ, the LP-HP morph filter and the half-band resamplers.</summary>
public class FilterBlockTests
{
    private const int SampleRate = 48000;

    private static double Amplitude(Func<float, float> process, float hz)
    {
        var sum = 0.0;
        var count = 0;
        for (var i = 0; i < SampleRate; i++)
        {
            var y = process(MathF.Sin(DspHelper.TwoPi * hz * i / SampleRate));
            if (i >= SampleRate / 2)
            {
                sum += (double)y * y;
                count++;
            }
        }

        return Math.Sqrt(sum / count) * Math.Sqrt(2.0);
    }

    private static float MaxStep(float[] x)
    {
        var max = 0f;
        for (var i = 1; i < x.Length; i++)
            max = MathF.Max(max, MathF.Abs(x[i] - x[i - 1]));

        return max;
    }

    [Fact]
    public void Low_Shelf_Boosts_The_Lows_And_Leaves_The_Highs()
    {
        var shelf = new Biquad();
        shelf.SetLowShelf(SampleRate, 200f, 12f);
        Assert.InRange(Amplitude(shelf.Process, 30f), 3.7, 4.1);   // +12 dB = 3.98×

        shelf.Clear();
        Assert.InRange(Amplitude(shelf.Process, 5000f), 0.97, 1.03);
    }

    [Fact]
    public void High_Shelf_Cuts_The_Highs_And_Leaves_The_Lows()
    {
        var shelf = new Biquad();
        shelf.SetHighShelf(SampleRate, 4000f, -12f);
        Assert.InRange(Amplitude(shelf.Process, 15000f), 0.24, 0.32);

        shelf.Clear();
        Assert.InRange(Amplitude(shelf.Process, 100f), 0.97, 1.03);
    }

    [Theory]
    [InlineData(100f)]
    [InlineData(1000f)]
    [InlineData(10000f)]
    public void Linkwitz_Riley_Bands_Sum_Flat(float hz)
    {
        var lp1 = new Biquad();
        var lp2 = new Biquad();
        var hp1 = new Biquad();
        var hp2 = new Biquad();
        lp1.SetLowPass(SampleRate, 1000f, 0.70710678f);
        lp2.CopyCoefficientsFrom(lp1);
        hp1.SetHighPass(SampleRate, 1000f, 0.70710678f);
        hp2.CopyCoefficientsFrom(hp1);

        Assert.InRange(Amplitude(x => lp2.Process(lp1.Process(x)) + hp2.Process(hp1.Process(x)), hz), 0.98, 1.02);
    }

    [Fact]
    public void Shelf_Eq_At_Zero_Is_A_Bit_Exact_Passthrough()
    {
        var eq = new ShelfEq();
        eq.Prepare(SampleRate);
        eq.SetLow(0f);
        eq.SetHigh(0f);
        eq.Reset();
        var rng = new Xoshiro128(3);
        for (var i = 0; i < 4096; i++)
        {
            var l = rng.NextBipolar();
            var r = rng.NextBipolar();
            var inL = l;
            var inR = r;
            eq.Process(ref l, ref r);
            Assert.Equal(inL, l);
            Assert.Equal(inR, r);
        }
    }

    [Fact]
    public void Shelf_Eq_Boosts_And_Cuts()
    {
        var eq = new ShelfEq();
        eq.Prepare(SampleRate);
        eq.SetLow(12f);
        eq.SetHigh(-12f);
        eq.Reset();

        Assert.InRange(Amplitude(x =>
        {
            var r = x;
            eq.Process(ref x, ref r);
            return x;
        }, 30f), 3.6, 4.1);

        eq.Reset();
        Assert.InRange(Amplitude(x =>
        {
            var r = x;
            eq.Process(ref x, ref r);
            return x;
        }, 15000f), 0.22, 0.33);
    }

    private static MorphFilter MakeMorph(float morph, float cutoff = 1000f, float mix = 1f, bool enabled = true)
    {
        var f = new MorphFilter();
        f.Prepare(SampleRate);
        f.SetEnabled(enabled);
        f.SetMix(mix);
        f.SetCutoff(cutoff);
        f.SetResonance(0f);
        f.SetMorph(morph);
        f.Reset();

        return f;
    }

    private static Func<float, float> Mono(MorphFilter f) => x =>
    {
        var r = x;
        f.Process(ref x, ref r);
        return x;
    };

    [Fact]
    public void Morph_Zero_Is_A_Low_Pass_And_One_Is_A_High_Pass()
    {
        Assert.InRange(Amplitude(Mono(MakeMorph(0f)), 100f), 0.95, 1.05);
        Assert.True(Amplitude(Mono(MakeMorph(0f)), 10000f) < 0.05);
        Assert.True(Amplitude(Mono(MakeMorph(1f)), 100f) < 0.05);
        Assert.InRange(Amplitude(Mono(MakeMorph(1f)), 10000f), 0.9, 1.1);
    }

    [Fact]
    public void Morph_Half_Is_A_Notch_At_The_Cutoff()
    {
        Assert.True(Amplitude(Mono(MakeMorph(0.5f)), 1000f) < 0.3);
        Assert.InRange(Amplitude(Mono(MakeMorph(0.5f)), 50f), 0.9, 1.1);
        Assert.InRange(Amplitude(Mono(MakeMorph(0.5f)), 15000f), 0.9, 1.1);
    }

    [Fact]
    public void Mix_Zero_And_Off_Are_Exact_Dry()
    {
        foreach (var f in new[] { MakeMorph(0f, mix: 0f), MakeMorph(0f, enabled: false) })
        {
            var rng = new Xoshiro128(9);
            for (var i = 0; i < 2048; i++)
            {
                var l = rng.NextBipolar();
                var r = rng.NextBipolar();
                var inL = l;
                var inR = r;
                f.Process(ref l, ref r);
                Assert.Equal(inL, l);
                Assert.Equal(inR, r);
            }
        }
    }

    [Fact]
    public void Switching_The_Filter_On_Is_Click_Free()
    {
        var f = MakeMorph(0f, cutoff: 200f, enabled: false);
        var y = new float[SampleRate / 4];
        for (var i = 0; i < y.Length; i++)
        {
            var l = MathF.Sin(DspHelper.TwoPi * 1000f * i / SampleRate);
            var r = l;
            if (i == 1000)
                f.SetEnabled(true);

            f.Process(ref l, ref r);
            y[i] = l;
        }

        Assert.True(MaxStep(y) < 0.2f, $"enabling the filter stepped by {MaxStep(y)} (a 1 kHz sine itself slews 0.13)");
        Assert.True(MathF.Abs(y[^1]) < 0.1f, "the low-pass should have taken the 1 kHz tone down");
    }

    [Fact]
    public void Half_Band_Round_Trip_Is_A_Delayed_Copy()
    {
        var up = new HalfBandUpsampler();
        var down = new HalfBandDownsampler();
        var x = new float[4096];
        var y = new float[4096];
        for (var i = 0; i < x.Length; i++)
        {
            x[i] = 0.5f * MathF.Sin(DspHelper.TwoPi * 1000f * i / SampleRate);
            up.Process(x[i], out var a, out var b);
            y[i] = down.Process(a, b);
        }

        for (var i = 300; i < y.Length; i++)
            Assert.True(MathF.Abs(y[i] - x[i - HalfBand.RoundTripLatency]) < 2e-3f, $"sample {i}: {y[i]} vs {x[i - HalfBand.RoundTripLatency]}");
    }

    [Fact]
    public void Upsampler_Suppresses_The_Image()
    {
        var up = new HalfBandUpsampler();
        var stream = new float[16384];
        for (var i = 0; i < stream.Length / 2; i++)
        {
            up.Process(0.5f * MathF.Sin(DspHelper.TwoPi * 1000f * i / SampleRate), out stream[2 * i], out stream[2 * i + 1]);
        }

        var mags = Spectrum.MagnitudeDb(stream, stream.Length);
        var binHz = 2.0 * SampleRate / stream.Length;
        var peak = mags.Skip((int)(900 / binHz)).Take((int)(200 / binHz)).Max();
        var image = mags.Skip((int)(40000 / binHz)).Take((int)(7900 / binHz)).Max();

        Assert.True(peak - image > 60, $"image only {peak - image:F1} dB below the tone");
    }

    [Fact]
    public void Downsampler_Passes_The_Band_And_Rejects_Above_Nyquist()
    {
        var down = new HalfBandDownsampler();
        var passed = 0.0;
        var rejected = 0.0;
        const int n = 4096;
        for (var i = 0; i < n; i++)
        {
            var a = MathF.Sin(DspHelper.TwoPi * 1000f * (2 * i) / (2 * SampleRate));
            var b = MathF.Sin(DspHelper.TwoPi * 1000f * (2 * i + 1) / (2 * SampleRate));
            var y = down.Process(a, b);
            if (i >= 300)
                passed += (double)y * y;
        }

        down.Clear();
        for (var i = 0; i < n; i++)
        {
            var a = MathF.Sin(DspHelper.TwoPi * 30000f * (2 * i) / (2 * SampleRate));
            var b = MathF.Sin(DspHelper.TwoPi * 30000f * (2 * i + 1) / (2 * SampleRate));
            var y = down.Process(a, b);
            if (i >= 300)
                rejected += (double)y * y;
        }

        Assert.InRange(Math.Sqrt(passed / (n - 300)), 0.69, 0.72);
        Assert.True(Math.Sqrt(rejected / (n - 300)) < 0.002, $"30 kHz leaked at rms {Math.Sqrt(rejected / (n - 300))}");
    }
}
