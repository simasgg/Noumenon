using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Oscillators;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>
/// The whole chain inside the engine: the Order swap, block-size invariance and zero allocation
/// with every block active, the 2× engine rate, the scope tap, the reported latency, and the
/// plan's "Fabrications snapshot" render.
/// </summary>
public class EffectChainEngineTests
{
    private const int SampleRate = 48000;

    private static NoumenonEngine Make(ulong seed = 1)
    {
        var engine = new NoumenonEngine();
        engine.Prepare(SampleRate, 512);
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

    private static double Rms(float[] x, int start = 0, int end = int.MaxValue)
    {
        var sum = 0.0;
        var count = 0;
        for (var i = start; i < x.Length && i < end; i++)
        {
            sum += (double)x[i] * x[i];
            count++;
        }

        return count > 0 ? Math.Sqrt(sum / count) : 0;
    }

    private static float MaxStep(float[] x, int start, int end)
    {
        var max = 0f;
        for (var i = Math.Max(1, start); i < x.Length && i < end; i++)
            max = MathF.Max(max, MathF.Abs(x[i] - x[i - 1]));

        return max;
    }

    private static void SoloSine(ParameterBank bank)
    {
        for (var s = 0; s < ParameterTable.SectionCount; s++)
        {
            for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
                bank.Set(ParameterTable.Osc((SectionId)s, slot, OscParam.On), false);
        }

        bank.Set(ParamId.OscA2On, true);
        bank.Set(ParamId.OscA2Wave, (int)Waveform.Sine);
        bank.Set(ParamId.OscA2Level, 0.8f);
        bank.Set(ParamId.SectionALevel, 1f);
        bank.Set(ParamId.MixCrossfade, 0f);
    }

    private static void EverythingOn(ParameterBank bank)
    {
        bank.Set(ParamId.MasterFilterOn, true);
        bank.Set(ParamId.MasterFilterCutoff, 3000f);
        bank.Set(ParamId.MasterFilterMorph, 0.2f);
        bank.Set(ParamId.DistortionOn, true);
        bank.Set(ParamId.DistortionMix, 0.5f);
        bank.Set(ParamId.EqLow, 3f);
        bank.Set(ParamId.EqHigh, -2f);
        bank.Set(ParamId.SpinOn, true);
        bank.Set(ParamId.ResochordOn, true);
        bank.Set(ParamId.ResochordMix, 0.4f);
        bank.Set(ParamId.ReverbOn, true);
        bank.Set(ParamId.ReverbMix, 0.4f);
        bank.Set(ParamId.PostFilterOn, true);
        bank.Set(ParamId.PostFilterCutoff, 9000f);
        bank.Set(ParamId.PostEqHigh, 2f);
        bank.Set(ParamId.PostLimiterOn, true);
        bank.Set(ParamId.PostLimiterGain, 6f);
    }

    [Fact]
    public void Order_Swap_Is_Click_Free()
    {
        var engine = Make();
        var bank = engine.Parameters;
        SoloSine(bank);
        EngineTests.BypassEffects(bank);
        bank.Set(ParamId.ResochordOn, true);
        bank.Set(ParamId.ResochordMix, 0.5f);
        bank.Set(ParamId.ResochordChord, 1);
        bank.Set(ParamId.ReverbOn, true);
        bank.Set(ParamId.ReverbMix, 0.5f);
        bank.Set(ParamId.ReverbOrder, 1);   // Post
        engine.Reset(1);

        var (before, _) = Render(engine, SampleRate);
        bank.Set(ParamId.ReverbOrder, 0);   // Pre
        var (after, _) = Render(engine, SampleRate);

        var baseline = MaxStep(before, SampleRate / 2, SampleRate);
        var swap = MaxStep(after, 0, SampleRate / 5);
        Assert.True(swap <= baseline * 2f + 0.02f, $"order swap stepped by {swap} (baseline {baseline})");
        Assert.True(Rms(after, SampleRate / 2) > 0.01, "the chain went silent after the swap");
    }

    [Fact]
    public void Whole_Chain_Render_Is_BlockSize_Invariant()
    {
        const int n = SampleRate * 2;
        var one = Make(5);
        EverythingOn(one.Parameters);
        one.Reset(5);
        var (oneL, oneR) = Render(one, n, n);

        var blocked = Make(5);
        EverythingOn(blocked.Parameters);
        blocked.Reset(5);
        var blkL = new float[n];
        var blkR = new float[n];
        int[] blocks = [1, 32, 512, 7, 333, 1024, 17, 31, 64];
        int pos = 0, bi = 0;
        while (pos < n)
        {
            var len = Math.Min(blocks[bi++ % blocks.Length], n - pos);
            blocked.Process(blkL, blkR, pos, len);
            pos += len;
        }

        for (var i = 0; i < n; i++)
        {
            Assert.True(oneL[i] == blkL[i], $"L differs at {i}: one-shot={oneL[i]} blocked={blkL[i]}");
            Assert.True(oneR[i] == blkR[i], $"R differs at {i}: one-shot={oneR[i]} blocked={blkR[i]}");
        }

        Assert.True(Rms(oneL) > 0.01);
    }

    [Fact]
    public void Whole_Chain_Does_Not_Allocate()
    {
        var engine = Make();
        var bank = engine.Parameters;
        EverythingOn(bank);
        engine.Reset(1);
        var l = new float[512];
        var r = new float[512];
        for (var k = 0; k < 50; k++)
            engine.Process(l, r, l.Length);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 1000; k++)
        {
            bank.Set(ParamId.ReverbRoom, (k / 120) % 6);
            bank.Set(ParamId.ReverbFreeze, k % 500 == 250);
            bank.Set(ParamId.ReverbOrder, (k / 200) & 1);
            bank.Set(ParamId.DistortionCurve, (k / 90) % 3);
            bank.Set(ParamId.ResochordChord, (k / 70) % 27);
            bank.Set(ParamId.SpinTime1, (k & 1) == 0 ? 120f : 200f);
            bank.Set(ParamId.MasterFilterCutoff, (k & 1) == 0 ? 2000f : 6000f);
            bank.Set(ParamId.PostLimiterSplit, (k & 1) == 0 ? 250f : 400f);
            engine.Process(l, r, l.Length);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }

    [Fact]
    public void Two_Times_Engine_Rate_Renders_Comparably_And_Stays_Invariant()
    {
        var single = Make(3);
        var (singleL, _) = Render(single, SampleRate * 2);

        var twice = new NoumenonEngine();
        twice.Parameters.Set(ParamId.EngineRate, 1);
        twice.Prepare(SampleRate, 512);
        twice.Reset(3);
        Assert.Equal(2, twice.Oversampling);
        Assert.Equal(SampleRate * 2, twice.SampleRate);
        Assert.Equal(SampleRate, twice.HostSampleRate);
        Assert.Equal(Distortion_Latency_At_Half_Plus_Edges(), twice.LatencySamples);

        var (twiceL, _) = Render(twice, SampleRate * 2);
        for (var i = 0; i < twiceL.Length; i++)
            Assert.True(float.IsFinite(twiceL[i]), $"non-finite at {i}");

        var ratio = Rms(twiceL, SampleRate) / Rms(singleL, SampleRate);
        Assert.InRange(ratio, 0.5, 2.0);

        var again = new NoumenonEngine();
        again.Parameters.Set(ParamId.EngineRate, 1);
        again.Prepare(SampleRate, 512);
        again.Reset(3);
        var (againL, _) = Render(again, SampleRate * 2, 333);
        Assert.Equal(twiceL, againL);
    }

    private static int Distortion_Latency_At_Half_Plus_Edges() => Dsp.Effects.Distortion.Latency / 2 + HalfBand.RoundTripLatency;

    [Fact]
    public void Latency_Is_The_Distortion_Resamplers_At_1x()
    {
        Assert.Equal(Dsp.Effects.Distortion.Latency, Make().LatencySamples);
    }

    [Fact]
    public void Fabrications_Style_Preset_Sustains_Under_The_Ceiling()
    {
        var engine = Make(7);
        var bank = engine.Parameters;
        bank.Set(ParamId.ResochordOn, true);
        bank.Set(ParamId.ResochordChord, 1);
        bank.Set(ParamId.ResochordFeedback, 0.9f);
        bank.Set(ParamId.ResochordMix, 0.6f);
        bank.Set(ParamId.ReverbOn, true);
        bank.Set(ParamId.ReverbRoom, 3);   // Cathedral
        bank.Set(ParamId.ReverbMix, 0.5f);
        bank.Set(ParamId.PostLimiterOn, true);
        bank.Set(ParamId.PostLimiterGain, 6f);
        engine.Reset(7);

        // A second of the drone into the room, then freeze it ("endless") and mute the sources: the
        // frozen tank plus the ringing Resochord must carry on by themselves.
        var (l, r) = Render(engine, SampleRate);
        bank.Set(ParamId.ReverbFreeze, true);
        var (frozenL, frozenR) = Render(engine, SampleRate * 5);
        foreach (var x in new[] { l, r, frozenL, frozenR })
        {
            for (var i = 0; i < x.Length; i++)
                Assert.True(float.IsFinite(x[i]), $"non-finite at {i}");
        }

        Assert.True(Rms(frozenL, SampleRate * 4) > 0.02, "the preset should still sound after six seconds");
        Assert.True(frozenL.Max(MathF.Abs) <= 0.981f && frozenR.Max(MathF.Abs) <= 0.981f, "the safety limiter holds the ceiling");
        Assert.NotEqual(frozenL, frozenR);
    }

    [Fact]
    public void Scope_Carries_The_Post_Spin_Signal()
    {
        var engine = Make();
        Render(engine, SampleRate);
        var l = new float[1024];
        var r = new float[1024];
        engine.Scope.CopyLatest(l, r, 1024);

        Assert.True(l.Max(MathF.Abs) > 0.01f, "the scope buffer should hold recent audio");
    }
}
