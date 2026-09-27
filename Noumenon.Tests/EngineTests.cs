using Noumenon.Dsp.Oscillators;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>
/// The real-time and determinism guarantees of the whole engine: allocation-free rendering, a
/// render that does not depend on how it is cut into blocks, seed-exact reproducibility, and the
/// Phase 1 graph doing what the panel says (P routing, Slave, Mute).
/// </summary>
public class EngineTests
{
    private const int SampleRate = 48000;

    private static NoumenonEngine Make(ulong seed = 1, int sampleRate = SampleRate)
    {
        var engine = new NoumenonEngine();
        engine.Prepare(sampleRate, 512);
        engine.Reset(seed);

        return engine;
    }

    private static (float[] left, float[] right) Render(NoumenonEngine engine, int n, int block = 512)
    {
        var l = new float[n];
        var r = new float[n];
        for (var pos = 0; pos < n; pos += block)
            engine.Process(l, r, pos, Math.Min(block, n - pos));

        return (l, r);
    }

    private static double Rms(float[] x, int start = 0)
    {
        var sum = 0.0;
        for (var i = start; i < x.Length; i++)
            sum += (double)x[i] * x[i];

        return Math.Sqrt(sum / Math.Max(1, x.Length - start));
    }

    /// <summary>Turns every effect block off so a test sees the sources alone (the distortion's constant latency stays).</summary>
    public static void BypassEffects(ParameterBank bank)
    {
        bank.Set(ParamId.MasterFilterOn, false);
        bank.Set(ParamId.DistortionOn, false);
        bank.Set(ParamId.SpinOn, false);
        bank.Set(ParamId.ResochordOn, false);
        bank.Set(ParamId.ReverbOn, false);
        bank.Set(ParamId.PostFilterOn, false);
        bank.Set(ParamId.PostLimiterOn, false);
    }

    private static void SoloA2Sine(ParameterBank bank)
    {
        BypassEffects(bank);
        for (var s = 0; s < ParameterTable.SectionCount; s++)
        {
            for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
                bank.Set(ParameterTable.Osc((SectionId)s, slot, OscParam.On), false);
        }

        bank.Set(ParamId.OscA2On, true);
        bank.Set(ParamId.OscA2Wave, (int)Waveform.Sine);
        bank.Set(ParamId.OscA2Semitone, 0);
        bank.Set(ParamId.OscA2Fine, 0f);
        bank.Set(ParamId.OscA2Level, 1f);
        bank.Set(ParamId.SectionACutoff, 20000f);
        bank.Set(ParamId.SectionAReso, 0f);
        bank.Set(ParamId.SectionALevel, 1f);
        bank.Set(ParamId.MixCrossfade, 0f);
    }

    private static int PositiveZeroCrossings(float[] x)
    {
        var crossings = 0;
        for (var i = 1; i < x.Length; i++)
        {
            if (x[i - 1] < 0f && x[i] >= 0f)
                crossings++;
        }

        return crossings;
    }

    [Fact]
    public void Process_Does_Not_Allocate()
    {
        var engine = Make();
        var l = new float[512];
        var r = new float[512];
        for (var k = 0; k < 50; k++)
            engine.Process(l, r, l.Length);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 1000; k++)
        {
            engine.Parameters.Set(ParamId.OscA1Level, (k & 1) == 0 ? 0.3f : 0.6f);
            engine.Parameters.Set(ParamId.SectionBCutoff, (k & 1) == 0 ? 800f : 6000f);
            engine.Process(l, r, l.Length);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Render_Is_BlockSize_Invariant()
    {
        const int n = SampleRate * 2;
        var (oneL, oneR) = Render(Make(7), n, n);

        var engine = Make(7);
        var blkL = new float[n];
        var blkR = new float[n];
        int[] blocks = [1, 32, 512, 7, 333, 1024, 17, 31, 64];
        int pos = 0, bi = 0;
        while (pos < n)
        {
            var len = Math.Min(blocks[bi++ % blocks.Length], n - pos);
            engine.Process(blkL, blkR, pos, len);
            pos += len;
        }

        for (var i = 0; i < n; i++)
        {
            Assert.True(oneL[i] == blkL[i], $"L differs at {i}: one-shot={oneL[i]} blocked={blkL[i]}");
            Assert.True(oneR[i] == blkR[i], $"R differs at {i}: one-shot={oneR[i]} blocked={blkR[i]}");
        }
    }

    [Fact]
    public void Same_Seed_Renders_Identically_And_Another_Seed_Differs()
    {
        var a = Make(7);
        var b = Make(7);
        var c = Make(8);
        Randomizer.Randomize(a.Parameters, 7);
        Randomizer.Randomize(b.Parameters, 7);
        Randomizer.Randomize(c.Parameters, 8);
        a.Reset(7);
        b.Reset(7);
        c.Reset(8);

        var (al, _) = Render(a, SampleRate);
        var (bl, _) = Render(b, SampleRate);
        var (cl, _) = Render(c, SampleRate);

        Assert.Equal(al, bl);
        Assert.NotEqual(al, cl);
    }

    [Fact]
    public void Init_Patch_Sounds_From_The_First_Block_And_Stays_In_Range()
    {
        var (l, r) = Render(Make(), SampleRate * 3);
        for (var i = 0; i < l.Length; i++)
            Assert.True(float.IsFinite(l[i]) && float.IsFinite(r[i]), $"non-finite at {i}");

        Assert.True(Rms(l, 0) > 0.02, "the init patch should make sound immediately");
        Assert.True(Rms(l, SampleRate * 2) > 0.02, "the init patch should keep sounding");
        Assert.True(l.Max(MathF.Abs) <= 0.981f, "the safety limiter caps the output");
        Assert.NotEqual(l, r);   // Spin's unequal delay times make the init patch stereo
    }

    [Fact]
    public void Randomized_Patches_Are_Finite_And_Audible()
    {
        for (ulong seed = 1; seed <= 12; seed++)
        {
            var engine = Make(seed);
            Randomizer.Randomize(engine.Parameters, seed);
            engine.Reset(seed);
            var (l, _) = Render(engine, SampleRate * 2);
            for (var i = 0; i < l.Length; i++)
                Assert.True(float.IsFinite(l[i]), $"seed {seed}: non-finite at {i}");

            Assert.True(Rms(l, SampleRate) > 0.005, $"seed {seed} rendered near silence");
        }
    }

    [Fact]
    public void P1_Routing_Transposes_A_Routed_Oscillator()
    {
        var engine = Make();
        SoloA2Sine(engine.Parameters);
        engine.Reset(1);
        var (baseL, _) = Render(engine, SampleRate * 2);

        engine.P1 = 12f;
        engine.Reset(1);
        var (upL, _) = Render(engine, SampleRate * 2);

        engine.Parameters.Set(ParamId.OscA2P1Route, false);
        engine.Reset(1);
        var (unroutedL, _) = Render(engine, SampleRate * 2);

        var baseCrossings = PositiveZeroCrossings(baseL);
        Assert.InRange(baseCrossings, 520, 526);   // 261.6 Hz × 2 s
        Assert.InRange(PositiveZeroCrossings(upL), 2 * baseCrossings - 4, 2 * baseCrossings + 4);
        Assert.InRange(PositiveZeroCrossings(unroutedL), baseCrossings - 2, baseCrossings + 2);
    }

    [Fact]
    public void Fine_Range_Turns_A_P_Fader_Into_Cents()
    {
        var engine = Make();
        SoloA2Sine(engine.Parameters);
        engine.Parameters.Set(ParamId.SectionAP1FineRange, true);
        engine.P1 = 48f;   // 48 cents, not 48 semitones
        engine.Reset(1);
        var (l, _) = Render(engine, SampleRate * 2);

        Assert.InRange(PositiveZeroCrossings(l), 528, 542);   // 261.6 × 2^(0.48/12) ≈ 269 Hz × 2 s
    }

    [Fact]
    public void Slave_Follows_The_Master_Filter_Cutoff()
    {
        var engine = Make();
        var bank = engine.Parameters;
        BypassEffects(bank);   // the master filter itself stays off: only its cutoff value feeds the Slave
        bank.Set(ParamId.MasterFilterCutoff, 100f);
        engine.Reset(1);
        var (openL, _) = Render(engine, SampleRate);

        bank.Set(ParamId.SectionASlave, true);
        bank.Set(ParamId.SectionBSlave, true);
        engine.Reset(1);
        var (slavedL, _) = Render(engine, SampleRate);

        Assert.True(Rms(slavedL, SampleRate / 2) < Rms(openL, SampleRate / 2) * 0.5, "slaving both sections to a 100 Hz master cutoff should darken and quieten the drone");
    }

    [Fact]
    public void Mute_Silences_Without_A_Click_And_Volume_Glides()
    {
        var engine = Make();
        var (l, _) = Render(engine, SampleRate / 2);
        engine.Parameters.Set(ParamId.Mute, true);
        var (muted, _) = Render(engine, SampleRate / 2);

        var maxStep = 0f;
        var prev = l[^1];
        for (var i = 0; i < muted.Length; i++)
        {
            maxStep = MathF.Max(maxStep, MathF.Abs(muted[i] - prev));
            prev = muted[i];
        }

        var baselineStep = 0f;
        for (var i = 1; i < l.Length; i++)
            baselineStep = MathF.Max(baselineStep, MathF.Abs(l[i] - l[i - 1]));

        Assert.Equal(0f, muted[^1]);
        Assert.True(maxStep <= baselineStep * 1.5f + 1e-3f, $"mute clicked: step {maxStep} vs baseline {baselineStep}");
    }

    [Fact]
    public void Sample_Rate_Change_Reprepares_Cleanly()
    {
        var engine = Make();
        Render(engine, 1000);
        engine.Prepare(96000, 256);
        var (l, _) = Render(engine, 96000, 256);
        for (var i = 0; i < l.Length; i++)
            Assert.True(float.IsFinite(l[i]), $"non-finite at {i} after the rate change");

        Assert.True(Rms(l) > 0.02);
    }
}
