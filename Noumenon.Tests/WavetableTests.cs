using Noumenon.Dsp.Oscillators;
using Noumenon.Dsp.Sampler;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>
/// The two Phase 9 waves: the wavetable keeps a cycle's shape, picks a band-limited level so a
/// bright cycle does not alias high up, scans its frames with Shape, and reads the sample on the
/// Wavetable Key inside the engine; the Sub wave puts its energy at the slot's pitch, an octave
/// below, or both, as Shape says.
/// </summary>
public class WavetableTests
{
    private const int SampleRate = 48000;
    private const int FftSize = 65536;
    private const double BinHz = (double)SampleRate / FftSize;

    private static float[] Cycle(Func<double, double> wave)
    {
        var cycle = new float[Wavetable.FrameSize];
        for (var i = 0; i < cycle.Length; i++)
            cycle[i] = (float)wave(i / (double)Wavetable.FrameSize);

        return cycle;
    }

    private static float[] Play(Oscillator osc, int n)
    {
        var y = new float[n];
        for (var i = 0; i < n; i++)
            y[i] = osc.Process();

        return y;
    }

    private static Oscillator Make(Waveform wave, float hz, float shape, Wavetable? table = null)
    {
        var osc = new Oscillator { Waveform = wave, Table = table };
        osc.Prepare(SampleRate);
        osc.SetFrequency(hz);
        osc.SetShape(shape);
        osc.Reset(1);

        return osc;
    }

    [Fact]
    public void A_Sine_Cycle_Plays_Back_As_A_Sine()
    {
        var table = Wavetable.FromCycles([Cycle(p => Math.Sin(2 * Math.PI * p))]);
        var osc = Make(Waveform.Wavetable, 440f, 0.5f, table);
        var y = Play(osc, FftSize + 4800)[4800..];

        var mags = Spectrum.MagnitudeDb(y, FftSize);
        Assert.Equal(440.0, Spectrum.PeakFrequency(mags, BinHz, 100, 4000), 0.5);
        Assert.InRange(y.Max(MathF.Abs), 0.97f, 1.03f);
        Assert.True(Spectrum.AliasFloorDb(mags, BinHz, 440, 20000, 6) < -70, "a sine cycle has nothing but its fundamental");
    }

    [Fact]
    public void A_Bright_Cycle_Played_High_Does_Not_Alias()
    {
        var table = Wavetable.FromCycles([Cycle(p => 2 * p - 1)]);   // a naive saw: every harmonic up to the frame's limit
        var hz = 2637f;   // E7: only a few harmonics fit under Nyquist
        var osc = Make(Waveform.Wavetable, hz, 0.5f, table);
        var y = Play(osc, FftSize + 4800)[4800..];

        var mags = Spectrum.MagnitudeDb(y, FftSize);
        var floor = Spectrum.AliasFloorDb(mags, BinHz, hz, 20000, 6);
        Assert.True(floor < -55, $"alias floor {floor:0.0} dB");
    }

    [Fact]
    public void The_Level_Keeps_The_Top_Harmonic_Below_Nyquist()
    {
        for (var hz = 20.0; hz < 21000; hz *= 1.13)
        {
            var increment = hz / SampleRate;
            var level = Wavetable.LevelFor(increment);
            Assert.True(Wavetable.Harmonics(level) * increment <= 0.5 + 1e-9, $"{hz:0} Hz: level {level} keeps {Wavetable.Harmonics(level)} harmonics");
            if (level > 0)
                Assert.True(Wavetable.Harmonics(level - 1) * increment > 0.5, $"{hz:0} Hz: level {level} gives up more than it must");
        }
    }

    [Fact]
    public void Shape_Scans_The_Frames()
    {
        var sine = Cycle(p => Math.Sin(2 * Math.PI * p));
        var third = Cycle(p => Math.Sin(2 * Math.PI * 3 * p));
        var table = Wavetable.FromCycles([sine, third]);

        var first = Spectrum.MagnitudeDb(Play(Make(Waveform.Wavetable, 200f, 0f, table), FftSize + 4800)[4800..], FftSize);
        var last = Spectrum.MagnitudeDb(Play(Make(Waveform.Wavetable, 200f, 1f, table), FftSize + 4800)[4800..], FftSize);

        Assert.Equal(200.0, Spectrum.PeakFrequency(first, BinHz, 100, 2000), 0.5);
        Assert.Equal(600.0, Spectrum.PeakFrequency(last, BinHz, 100, 2000), 0.5);
    }

    [Fact]
    public void A_Long_Recording_Becomes_32_Normalized_Frames()
    {
        var n = SampleRate * 3;
        var left = new float[n];
        for (var i = 0; i < n; i++)
            left[i] = 0.05f * MathF.Sin(2f * MathF.PI * 110f * i / SampleRate);   // quiet: normalized up by 24 dB at most

        var table = new SampleData("long", SampleRate, left).BuildWavetable();
        Assert.Equal(Wavetable.MaxFrames, table.FrameCount);

        var peak = 0f;
        for (var i = 0; i < Wavetable.FrameSize; i++)
            peak = MathF.Max(peak, MathF.Abs(table.Read(i / (double)Wavetable.FrameSize, 0f, 0)));

        Assert.InRange(peak, 0.75f, 0.85f);   // 0.05 × 16
    }

    [Fact]
    public void A_File_Of_Whole_Cycles_Keeps_Its_Cycles()
    {
        var cycles = 4;
        var left = new float[cycles * Wavetable.FrameSize];
        for (var c = 0; c < cycles; c++)
        {
            for (var i = 0; i < Wavetable.FrameSize; i++)
                left[c * Wavetable.FrameSize + i] = MathF.Sin(2f * MathF.PI * (c + 1) * i / Wavetable.FrameSize);
        }

        var table = Wavetable.FromSample(new SampleData("cycles", SampleRate, left));

        Assert.Equal(cycles, table.FrameCount);
        Assert.Equal(1f, table.Read(1.0 / 16, 1f, 0), 2);   // frame 4 is sin(4·2πp): its first peak is at p = 1/16
    }

    [Fact]
    public void The_Engine_Plays_The_Wavetable_Of_The_Wavetable_Key()
    {
        var engine = new NoumenonEngine();
        var bank = engine.Parameters;
        EngineTests.BypassEffects(bank);
        for (var s = 0; s < ParameterTable.SectionCount; s++)
        {
            for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
                bank.Set(ParameterTable.Osc((SectionId)s, slot, OscParam.On), false);
        }

        bank.Set(ParamId.OscA1On, true);
        bank.Set(ParamId.OscA1Wave, (int)Waveform.Wavetable);
        bank.Set(ParamId.OscA1Semitone, 0);
        bank.Set(ParamId.OscA1Fine, 0f);
        bank.Set(ParamId.OscA1Level, 1f);
        bank.Set(ParamId.OscA1P1Route, false);
        bank.Set(ParamId.OscA1P2Route, false);
        bank.Set(ParamId.SectionACutoff, 20000f);
        bank.Set(ParamId.SectionAReso, 0f);
        bank.Set(ParamId.SectionALevel, 1f);
        bank.Set(ParamId.MixCrossfade, 0f);
        bank.Set(ParamId.SamplerAmp, 0f);
        bank.Set(ParamId.WavetableKey, 5);
        engine.Prepare(SampleRate, 512);
        engine.Reset(1);

        var l = new float[SampleRate];
        var r = new float[SampleRate];
        engine.Process(l, r, l.Length);
        Assert.True(l.Skip(SampleRate / 2).Max(MathF.Abs) < 1e-4f, "no table on the key: the slot is silent");

        var cycle = Cycle(p => Math.Sin(2 * Math.PI * p));
        engine.Samples.Set(5, new SampleData("one cycle", SampleRate, cycle));
        var y = new float[FftSize + 4800];
        var right = new float[y.Length];
        engine.Process(y, right, y.Length);

        var mags = Spectrum.MagnitudeDb(y[4800..], FftSize);
        Assert.Equal(261.63, Spectrum.PeakFrequency(mags, BinHz, 100, 2000), 0.5);
    }

    [Theory]
    [InlineData(0f, 1.0, 0.0)]
    [InlineData(0.5f, 1.0, 1.0)]
    [InlineData(1f, 0.0, 1.0)]
    public void Sub_Shape_Moves_Energy_To_The_Octave_Below(float shape, double expectFundamental, double expectSub)
    {
        var y = Play(Make(Waveform.Sub, 400f, shape), FftSize + 4800)[4800..];
        var mags = Spectrum.MagnitudeDb(y, FftSize);
        var top = mags[(int)Math.Round(400 / BinHz)];
        var sub = mags[(int)Math.Round(200 / BinHz)];
        var loudest = Math.Max(top, sub);

        Assert.Equal(expectFundamental > 0, top > loudest - 6);
        Assert.Equal(expectSub > 0, sub > loudest - 6);
        Assert.True(y.Max(MathF.Abs) <= 1.5f);
    }
}
