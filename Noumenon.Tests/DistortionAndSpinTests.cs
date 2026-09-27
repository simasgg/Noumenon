using Noumenon.Dsp.Effects;
using Noumenon.Dsp.Shared;

namespace Noumenon.Tests;

public class DistortionAndSpinTests
{
    private const int SampleRate = 48000;

    private static Distortion MakeDistortion(bool enabled = true, float mix = 1f, float drive = 0.2f, DistortionCurve curve = DistortionCurve.Tanh)
    {
        var d = new Distortion { Curve = curve };
        d.Prepare(SampleRate);
        d.SetEnabled(enabled);
        d.SetMix(mix);
        d.SetDrive(drive);
        d.Reset();

        return d;
    }

    private static float[] Mono(Func<int, float> input, int n, Action<Func<float, float>> _ = null!)
    {
        var y = new float[n];
        for (var i = 0; i < n; i++)
            y[i] = input(i);

        return y;
    }

    private static float[] RunDistortion(Distortion d, float[] x)
    {
        var y = new float[x.Length];
        for (var i = 0; i < x.Length; i++)
        {
            var l = x[i];
            var r = x[i];
            d.Process(ref l, ref r);
            y[i] = l;
        }

        return y;
    }

    [Fact]
    public void Off_Is_The_Dry_Signal_Delayed_By_The_Latency()
    {
        var x = Mono(i => i * 1e-4f, 2000);
        var y = RunDistortion(MakeDistortion(enabled: false), x);
        for (var i = 0; i < Distortion.Latency; i++)
            Assert.Equal(0f, y[i]);

        for (var i = Distortion.Latency; i < y.Length; i++)
            Assert.Equal(x[i - Distortion.Latency], y[i]);
    }

    [Fact]
    public void Tanh_At_Full_Drive_Squares_The_Wave_Without_Overs()
    {
        var x = Mono(i => MathF.Sin(DspHelper.TwoPi * 200f * i / SampleRate), SampleRate);
        var y = RunDistortion(MakeDistortion(drive: 1f), x);
        var peak = y.Skip(4096).Max(MathF.Abs);
        var rms = Math.Sqrt(y.Skip(4096).Average(v => (double)v * v));

        Assert.True(peak <= 1.15f, $"peak {peak}");
        Assert.True(rms > 0.85, $"a saturated sine should be nearly square, rms {rms}");
    }

    [Theory]
    [InlineData(DistortionCurve.Tanh)]
    [InlineData(DistortionCurve.Fold)]
    [InlineData(DistortionCurve.Bit)]
    public void Every_Curve_Stays_Bounded_And_Finite(DistortionCurve curve)
    {
        var x = Mono(i => MathF.Sin(DspHelper.TwoPi * 330f * i / SampleRate) * 0.9f, SampleRate / 2);
        var y = RunDistortion(MakeDistortion(drive: 0.7f, curve: curve), x);
        Assert.All(y, v => Assert.True(float.IsFinite(v) && MathF.Abs(v) <= 1.25f, $"{v}"));
        Assert.True(y.Skip(4096).Max(MathF.Abs) > 0.3f, "the curve should produce output");
    }

    [Fact]
    public void Oversampling_Keeps_The_Curves_Aliasing_Down()
    {
        const int n = 16384;
        var f0 = 1707 * ((double)SampleRate / n);   // ≈ 5 kHz, on a bin
        const float drive = 0.25f;
        var gain = MathF.Pow(10f, drive * 2f);   // the block's own drive mapping (0–40 dB)
        var x = Mono(i => MathF.Sin((float)(DspHelper.TwoPi * f0 * i / SampleRate)), n + 4096);

        var naive = x.Skip(4096).Select(v => MathF.Tanh(v * gain)).ToArray();
        var oversampled = RunDistortion(MakeDistortion(drive: drive), x).Skip(4096).ToArray();

        var binHz = (double)SampleRate / n;
        var naiveFloor = Spectrum.AliasFloorDb(Spectrum.MagnitudeDb(naive, n), binHz, f0, 4800, 6);
        var cleanFloor = Spectrum.AliasFloorDb(Spectrum.MagnitudeDb(oversampled, n), binHz, f0, 4800, 6);

        Assert.True(cleanFloor < -50, $"oversampled alias floor {cleanFloor:F1} dB");
        Assert.True(naiveFloor - cleanFloor > 12, $"oversampling should beat a naive curve by >12 dB (naive {naiveFloor:F1}, oversampled {cleanFloor:F1})");
    }

    private static Spin MakeSpin(float t1 = 10f, float t2 = 20f, float fine = 0f, bool link = false, float feedback = 0f, float mix = 1f, bool enabled = true)
    {
        var s = new Spin();
        s.Prepare(SampleRate);
        s.SetEnabled(enabled);
        s.SetMix(mix);
        s.SetFeedback(feedback);
        s.SetTimes(t1, t2, fine, link);
        s.Reset();

        return s;
    }

    private static (float[] left, float[] right) Impulse(Spin s, int n)
    {
        var l = new float[n];
        var r = new float[n];
        for (var i = 0; i < n; i++)
        {
            var a = i == 0 ? 1f : 0f;
            var b = a;
            s.Process(ref a, ref b);
            l[i] = a;
            r[i] = b;
        }

        return (l, r);
    }

    private static int ArgMax(float[] x)
    {
        var best = 0;
        for (var i = 1; i < x.Length; i++)
        {
            if (x[i] > x[best])
                best = i;
        }

        return best;
    }

    [Fact]
    public void Left_Is_Delayed_By_T1_And_Right_By_T2_Plus_Fine()
    {
        var (l, r) = Impulse(MakeSpin(), 2000);
        Assert.Equal(480, ArgMax(l));
        Assert.Equal(960, ArgMax(r));
        Assert.Equal(1f, l[480], 5);

        var (_, fineR) = Impulse(MakeSpin(fine: 1f), 2000);
        Assert.Equal(1008, ArgMax(fineR));

        var (_, linkedR) = Impulse(MakeSpin(fine: 1f, link: true), 2000);
        Assert.Equal(528, ArgMax(linkedR));
    }

    [Fact]
    public void Feedback_Repeats_And_Decays()
    {
        var (l, _) = Impulse(MakeSpin(feedback: 0.5f), 2000);
        Assert.InRange(l[480], 0.99f, 1.01f);
        Assert.InRange(l[960], 0.45f, 0.51f);
        Assert.InRange(l[1440], 0.2f, 0.26f);
    }

    [Fact]
    public void A_Time_Sweep_Shifts_Pitch_Without_Clicking()
    {
        var s = MakeSpin(t1: 10f, t2: 10f, feedback: 0.3f);
        var y = new float[SampleRate];
        var maxStep = 0f;
        for (var i = 0; i < y.Length; i++)
        {
            if (i % 32 == 0)
                s.SetTimes(10f + 90f * i / y.Length, 10f, 0f, true);

            var l = 0.5f * MathF.Sin(DspHelper.TwoPi * 200f * i / SampleRate);
            var r = l;
            s.Process(ref l, ref r);
            y[i] = l;
            if (i > 0)
                maxStep = MathF.Max(maxStep, MathF.Abs(y[i] - y[i - 1]));
        }

        Assert.True(maxStep < 0.05f, $"sweep stepped by {maxStep}");
        Assert.True(y.Skip(SampleRate / 2).Max(MathF.Abs) > 0.2f);
    }

    [Fact]
    public void Off_Is_Exact_Dry()
    {
        var s = MakeSpin(enabled: false);
        var rng = new Xoshiro128(4);
        for (var i = 0; i < 2048; i++)
        {
            var l = rng.NextBipolar();
            var r = rng.NextBipolar();
            var inL = l;
            var inR = r;
            s.Process(ref l, ref r);
            Assert.Equal(inL, l);
            Assert.Equal(inR, r);
        }
    }
}
