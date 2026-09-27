using Noumenon.Dsp.Oscillators;

namespace Noumenon.Tests;

public class OscillatorTests
{
    private const int SampleRate = 48000;
    private const int FftSize = 16384;
    private const double BinHz = (double)SampleRate / FftSize;

    private static Oscillator Make(Waveform wave, float hz, float shape = 0.5f, ulong seed = 1)
    {
        var osc = new Oscillator { Waveform = wave };
        osc.Prepare(SampleRate);
        osc.SetFrequency(hz);
        osc.SetShape(shape);
        osc.SetNoiseCutoff(2000f);
        osc.Reset(seed);

        return osc;
    }

    private static float[] Render(Oscillator osc, int n)
    {
        var y = new float[n];
        for (var i = 0; i < n; i++)
            y[i] = osc.Process();

        return y;
    }

    private static float[] NaiveSaw(double hz, int n)
    {
        var y = new float[n];
        var phase = 0.0;
        var dt = hz / SampleRate;
        for (var i = 0; i < n; i++)
        {
            y[i] = (float)(2.0 * phase - 1.0);
            phase += dt;
            if (phase >= 1.0)
                phase -= 1.0;
        }

        return y;
    }

    [Fact]
    public void Saw_At_1kHz_Aliases_Far_Less_Than_A_Naive_Saw()
    {
        var f0 = 341 * BinHz;   // ≈ 999 Hz, exactly on a bin so every harmonic is too
        var blep = Spectrum.AliasFloorDb(Spectrum.MagnitudeDb(Render(Make(Waveform.Saw, (float)f0), FftSize), FftSize), BinHz, f0, 5000, 6);
        var naive = Spectrum.AliasFloorDb(Spectrum.MagnitudeDb(NaiveSaw(f0, FftSize), FftSize), BinHz, f0, 5000, 6);

        Assert.True(blep < -45, $"PolyBLEP saw alias floor {blep:F1} dB below 5 kHz (expected around -50 dB)");
        Assert.True(naive - blep > 12, $"PolyBLEP should beat the naive saw by >12 dB (naive {naive:F1} dB, blep {blep:F1} dB)");
    }

    [Fact]
    public void Saw_In_The_Drone_Register_Is_Clean()
    {
        var f0 = 34 * BinHz;   // ≈ 100 Hz
        var floor = Spectrum.AliasFloorDb(Spectrum.MagnitudeDb(Render(Make(Waveform.Saw, (float)f0), FftSize), FftSize), BinHz, f0, 4000, 6);

        Assert.True(floor < -65, $"alias floor {floor:F1} dB below 4 kHz for a 100 Hz saw (expected around -74 dB)");
    }

    [Theory]
    [InlineData(Waveform.Pulse, 0.5f)]
    [InlineData(Waveform.Pulse, 0.2f)]
    [InlineData(Waveform.Square, 0.5f)]
    [InlineData(Waveform.BipolarPulse, 0.5f)]
    [InlineData(Waveform.Triangle, 0.5f)]
    [InlineData(Waveform.Triangle, 0.15f)]
    public void Anti_Aliased_Waves_Beat_Their_Naive_Counterparts(Waveform wave, float shape)
    {
        var f0 = 341 * BinHz;
        var floor = Spectrum.AliasFloorDb(Spectrum.MagnitudeDb(Render(Make(wave, (float)f0, shape), FftSize), FftSize), BinHz, f0, 5000, 6);

        // Triangle corners alias far less than steps to begin with; the pulses sit near the saw's floor.
        var limit = wave == Waveform.Triangle ? -70 : -45;
        Assert.True(floor < limit, $"{wave} shape {shape}: alias floor {floor:F1} dB below 5 kHz (limit {limit} dB)");
    }

    [Fact]
    public void Sine_Frequency_Is_Accurate()
    {
        const float hz = 440f;
        var y = Render(Make(Waveform.Sine, hz), SampleRate * 2);
        var crossings = 0;
        for (var i = 1; i < y.Length; i++)
        {
            if (y[i - 1] < 0f && y[i] >= 0f)
                crossings++;
        }

        Assert.InRange(crossings, 878, 882);
    }

    [Theory]
    [InlineData(Waveform.NoiseLp)]
    [InlineData(Waveform.NoiseSh)]
    public void Noise_Is_Deterministic_Per_Seed(Waveform wave)
    {
        var a = Render(Make(wave, 100f, seed: 42), 4096);
        var b = Render(Make(wave, 100f, seed: 42), 4096);
        var c = Render(Make(wave, 100f, seed: 43), 4096);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Contains(a, v => MathF.Abs(v) > 0.01f);
    }

    [Fact]
    public void Pulse_Width_Sets_The_Duty_Cycle()
    {
        var y = Render(Make(Waveform.Pulse, 100f, 0.25f), SampleRate);
        var mean = y.Average();

        Assert.InRange(mean, -0.52f, -0.48f);   // 25 % high, 75 % low → 2w − 1 = −0.5
    }

    [Fact]
    public void Bipolar_Pulse_Has_No_Dc()
    {
        var y = Render(Make(Waveform.BipolarPulse, 100f, 0.4f), SampleRate);

        Assert.InRange(y.Average(), -0.01f, 0.01f);
        Assert.Contains(y, v => v > 0.9f);
        Assert.Contains(y, v => v < -0.9f);
        Assert.Contains(y, v => MathF.Abs(v) < 0.01f);
    }

    [Theory]
    [InlineData(0.5f)]
    [InlineData(0.1f)]
    [InlineData(0.9f)]
    public void Triangle_Spans_Minus_One_To_One(float shape)
    {
        var y = Render(Make(Waveform.Triangle, 200f, shape), SampleRate);

        Assert.InRange(y.Max(), 0.95f, 1.02f);
        Assert.InRange(y.Min(), -1.02f, -0.95f);
    }

    [Theory]
    [InlineData(Waveform.Sine)]
    [InlineData(Waveform.Triangle)]
    [InlineData(Waveform.Saw)]
    [InlineData(Waveform.Square)]
    [InlineData(Waveform.Pulse)]
    [InlineData(Waveform.BipolarPulse)]
    [InlineData(Waveform.NoiseLp)]
    [InlineData(Waveform.NoiseSh)]
    public void Every_Wave_Stays_Finite_And_Bounded_Across_Pitch_And_Shape(Waveform wave)
    {
        foreach (var hz in new[] { 0f, 6.5f, 55f, 1000f, 8000f, 20000f, 30000f })
        {
            foreach (var shape in new[] { 0f, 0.05f, 0.5f, 0.95f, 1f })
            {
                var y = Render(Make(wave, hz, shape), 8192);
                for (var i = 0; i < y.Length; i++)
                    Assert.True(float.IsFinite(y[i]) && MathF.Abs(y[i]) <= 1.35f, $"{wave} at {hz} Hz shape {shape}: sample {i} = {y[i]}");
            }
        }
    }

    [Fact]
    public void Pitch_Changes_Glide_Instead_Of_Jumping()
    {
        var osc = Make(Waveform.Sine, 220f);
        Render(osc, 4096);
        osc.SetFrequency(880f);

        var y = Render(osc, 4096);
        var maxStep = 0f;
        for (var i = 1; i < y.Length; i++)
            maxStep = MathF.Max(maxStep, MathF.Abs(y[i] - y[i - 1]));

        // A sine at 880 Hz slews at most 2π·880/48000 ≈ 0.115 per sample; a phase discontinuity would be up to 2.
        Assert.True(maxStep < 0.13f, $"pitch change caused a step of {maxStep}");
    }
}
