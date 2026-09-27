using Noumenon.Dsp.Effects;
using Noumenon.Dsp.Shared;

namespace Noumenon.Tests;

public class PostSectionTests
{
    private const int SampleRate = 48000;

    private static PostSection Make(bool limiterOn = true, float gainDb = 0f, float lowThresholdDb = -6f, float highThresholdDb = -6f, float splitHz = 250f, float volumeDb = 0f)
    {
        var p = new PostSection();
        p.Prepare(SampleRate);
        p.Filter.SetEnabled(false);
        p.Eq.SetLow(0f);
        p.Eq.SetHigh(0f);
        p.Limiter.SetEnabled(limiterOn);
        p.Limiter.SetGainDb(gainDb);
        p.Limiter.SetThresholdsDb(lowThresholdDb, highThresholdDb);
        p.Limiter.SetSplit(splitHz);
        p.Limiter.SetSpeed(LimiterSpeed.Medium);
        p.SetVolumeDb(volumeDb);
        p.Reset();

        return p;
    }

    private static float PeakOfSine(PostSection p, float hz, float amplitude)
    {
        var peak = 0f;
        for (var i = 0; i < SampleRate; i++)
        {
            var l = amplitude * MathF.Sin(DspHelper.TwoPi * hz * i / SampleRate);
            var r = l;
            p.Process(ref l, ref r);
            if (i >= SampleRate / 2)
                peak = MathF.Max(peak, MathF.Abs(l));
        }

        return peak;
    }

    [Fact]
    public void Each_Band_Is_Held_Under_Its_Threshold()
    {
        var low = PeakOfSine(Make(gainDb: 12f, lowThresholdDb: -6f, highThresholdDb: 0f, splitHz: 1000f), 100f, 0.5f);
        Assert.InRange(low, 0.45f, 0.51f);   // 0.5 × 3.98 driven into a −6 dB (0.501) ceiling

        var high = PeakOfSine(Make(gainDb: 12f, lowThresholdDb: 0f, highThresholdDb: -12f, splitHz: 1000f), 5000f, 0.5f);
        Assert.InRange(high, 0.22f, 0.26f);
    }

    [Fact]
    public void Below_Threshold_The_Limiter_Is_Transparent()
    {
        var peak = PeakOfSine(Make(gainDb: 0f, lowThresholdDb: 0f, highThresholdDb: 0f), 440f, 0.3f);
        Assert.InRange(peak, 0.29f, 0.31f);
    }

    [Fact]
    public void Post_Volume_Scales_The_Output()
    {
        Assert.InRange(PeakOfSine(Make(limiterOn: false, volumeDb: -6f), 440f, 0.5f), 0.245f, 0.256f);
    }

    [Fact]
    public void Everything_Idle_Is_A_Bit_Exact_Passthrough()
    {
        var p = Make(limiterOn: false);
        var rng = new Xoshiro128(2);
        for (var i = 0; i < 2048; i++)
        {
            var l = rng.NextBipolar();
            var r = rng.NextBipolar();
            var inL = l;
            var inR = r;
            p.Process(ref l, ref r);
            Assert.Equal(inL, l);
            Assert.Equal(inR, r);
        }
    }

    [Fact]
    public void Turning_The_Limiter_On_Is_Click_Free()
    {
        var p = Make(limiterOn: false, gainDb: 12f, lowThresholdDb: -6f, highThresholdDb: -6f);
        var prev = 0f;
        var maxStep = 0f;
        for (var i = 0; i < SampleRate / 2; i++)
        {
            if (i == 8000)
                p.Limiter.SetEnabled(true);

            var l = 0.3f * MathF.Sin(DspHelper.TwoPi * 300f * i / SampleRate);
            var r = l;
            p.Process(ref l, ref r);
            if (i > 0)
                maxStep = MathF.Max(maxStep, MathF.Abs(l - prev));

            prev = l;
        }

        Assert.True(maxStep < 0.08f, $"stepped by {maxStep}");
    }
}
