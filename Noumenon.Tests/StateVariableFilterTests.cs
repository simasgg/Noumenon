using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Shared;

namespace Noumenon.Tests;

public class StateVariableFilterTests
{
    private const int SampleRate = 48000;

    private static double SteadyRms(StateVariableFilter filter, float hz)
    {
        var n = SampleRate;
        var sum = 0.0;
        var count = 0;
        for (var i = 0; i < n; i++)
        {
            var y = filter.Process(MathF.Sin(DspHelper.TwoPi * hz * i / SampleRate));
            if (i >= n / 2)
            {
                sum += (double)y * y;
                count++;
            }
        }

        return Math.Sqrt(sum / count) * MathF.Sqrt(2f);   // amplitude, since the input has amplitude 1
    }

    private static StateVariableFilter Make(FilterType type, float cutoff, float resonance)
    {
        var f = new StateVariableFilter { Type = type };
        f.SetCoefficients(cutoff, resonance, SampleRate);

        return f;
    }

    [Fact]
    public void LowPass_Keeps_Lows_And_Rolls_Off_Highs_At_12dB_Per_Octave()
    {
        Assert.InRange(SteadyRms(Make(FilterType.LowPass, 1000f, 0f), 50f), 0.97, 1.03);
        var twoOctavesUp = SteadyRms(Make(FilterType.LowPass, 1000f, 0f), 4000f);
        Assert.InRange(twoOctavesUp, 0.04, 0.09);   // −24 dB nominal ≈ 0.063
        Assert.True(SteadyRms(Make(FilterType.LowPass, 1000f, 0f), 12000f) < 0.012);
    }

    [Fact]
    public void HighPass_Is_The_Mirror_Image()
    {
        Assert.InRange(SteadyRms(Make(FilterType.HighPass, 1000f, 0f), 12000f), 0.95, 1.05);
        Assert.True(SteadyRms(Make(FilterType.HighPass, 1000f, 0f), 50f) < 0.005);
    }

    [Fact]
    public void BandPass_Peaks_At_Cutoff()
    {
        var atCutoff = SteadyRms(Make(FilterType.BandPass, 1000f, 0f), 1000f);
        Assert.True(atCutoff > SteadyRms(Make(FilterType.BandPass, 1000f, 0f), 100f) * 4);
        Assert.True(atCutoff > SteadyRms(Make(FilterType.BandPass, 1000f, 0f), 10000f) * 4);
    }

    [Fact]
    public void Resonance_Boosts_The_Cutoff_Region()
    {
        var flat = SteadyRms(Make(FilterType.LowPass, 1000f, 0f), 1000f);
        var resonant = SteadyRms(Make(FilterType.LowPass, 1000f, 0.9f), 1000f);
        Assert.InRange(flat, 0.4, 0.6);   // k = 2 → −6 dB at cutoff
        Assert.True(resonant > 3.0, $"resonance 0.9 should peak well above unity, got {resonant}");
    }

    [Theory]
    [InlineData(FilterType.LowPass, 5f, 1f)]
    [InlineData(FilterType.HighPass, 5f, 1f)]
    [InlineData(FilterType.BandPass, 20000f, 1f)]
    [InlineData(FilterType.LowPass, 30000f, 1f)]
    [InlineData(FilterType.HighPass, 20000f, 0f)]
    public void Stays_Finite_At_Extreme_Settings(FilterType type, float cutoff, float resonance)
    {
        var f = Make(type, cutoff, resonance);
        var rng = new Xoshiro128(5);
        for (var i = 0; i < SampleRate; i++)
        {
            var y = f.Process(rng.NextBipolar());
            Assert.True(float.IsFinite(y) && MathF.Abs(y) < 200f, $"sample {i} = {y}");
        }
    }
}
