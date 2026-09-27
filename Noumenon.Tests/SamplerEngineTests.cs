using Noumenon.Dsp.Sampler;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>
/// The sampler inside the engine: mixed in after the combiner, the live input through Samp↔In,
/// the MIDI gate retrigger, P2 as the default pitch source, the AM route, and the real-time
/// guarantees holding with a sample and an input in play.
/// </summary>
public class SamplerEngineTests
{
    private const int SampleRate = 48000;

    private static float[] Tone(int frames, float hz, float amplitude) => Enumerable.Range(0, frames).Select(i => amplitude * MathF.Sin(2f * MathF.PI * hz * i / SampleRate)).ToArray();

    private static void SilenceSections(ParameterBank bank)
    {
        for (var s = 0; s < ParameterTable.SectionCount; s++)
        {
            for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
                bank.Set(ParameterTable.Osc((SectionId)s, slot, OscParam.On), false);
        }
    }

    private static NoumenonEngine Make(SampleData? sample, bool sectionsOn = false, float amp = 1f)
    {
        var engine = new NoumenonEngine();
        var bank = engine.Parameters;
        EngineTests.BypassEffects(bank);
        if (!sectionsOn)
            SilenceSections(bank);

        if (sample is not null)
            engine.Samples.Set(0, sample);

        bank.Set(ParamId.SamplerSelect, 0);
        bank.Set(ParamId.SamplerAmp, amp);
        bank.Set(ParamId.SamplerMaster, 1f);
        bank.Set(ParamId.SamplerLoopCrossfade, 0f);
        engine.Prepare(SampleRate, 512);
        engine.Reset(1);

        return engine;
    }

    private static (float[] left, float[] right) Render(NoumenonEngine engine, int n, float[]? inputLeft = null, float[]? inputRight = null, int block = 512)
    {
        var l = new float[n];
        var r = new float[n];
        for (var pos = 0; pos < n; pos += block)
        {
            var count = Math.Min(block, n - pos);
            if (inputLeft is not null && inputRight is not null)
                engine.Process(inputLeft, inputRight, l, r, pos, count);
            else
                engine.Process(l, r, pos, count);
        }

        return (l, r);
    }

    private static double Rms(float[] x, int start = 0)
    {
        var sum = 0.0;
        for (var i = start; i < x.Length; i++)
            sum += (double)x[i] * x[i];

        return Math.Sqrt(sum / Math.Max(1, x.Length - start));
    }

    [Fact]
    public void Sample_Is_Mixed_In_Stereo_After_The_Combiner()
    {
        var left = Tone(SampleRate, 440f, 0.25f);
        var right = Tone(SampleRate, 660f, 0.25f);
        var engine = Make(new SampleData("stereo", SampleRate, left, right));
        var (l, r) = Render(engine, SampleRate / 2);

        AssertPassesThrough(left, l, engine.LatencySamples);
        AssertPassesThrough(right, r, engine.LatencySamples);
    }

    private static void AssertPassesThrough(float[] input, float[] output, int latency)
    {
        var signal = 0.0;
        var error = 0.0;
        for (var i = 200 + latency; i < output.Length; i++)
        {
            var expected = input[i - latency];
            signal += (double)expected * expected;
            error += (double)(output[i] - expected) * (output[i] - expected);
        }

        Assert.True(signal > 0);
        Assert.True(Math.Sqrt(error / signal) < 0.05, $"relative error {Math.Sqrt(error / signal):P1}");
    }

    [Fact]
    public void Amp_At_Zero_Removes_The_Sample()
    {
        var engine = Make(new SampleData("tone", SampleRate, Tone(SampleRate, 440f, 0.5f)), amp: 0f);
        var (l, _) = Render(engine, SampleRate / 4);
        Assert.True(Rms(l) < 1e-6, $"sample leaked at amp 0: rms {Rms(l)}");
    }

    [Fact]
    public void Live_Input_Passes_Through_The_Fx_Path()
    {
        var input = Tone(SampleRate, 440f, 0.25f);
        var engine = Make(null);
        engine.Parameters.Set(ParamId.SamplerInputMix, 1f);
        engine.Reset(1);
        var (l, r) = Render(engine, SampleRate / 2, input, input);

        AssertPassesThrough(input, l, engine.LatencySamples);
        AssertPassesThrough(input, r, engine.LatencySamples);

        engine.Parameters.Set(ParamId.SamplerInputMix, 0f);
        engine.Reset(1);
        var (silent, _) = Render(engine, SampleRate / 4, input, input);
        Assert.True(Rms(silent) < 1e-6, "with Samp<>In at 0 and no sample, the input must not leak");
    }

    [Fact]
    public void Gate_Retriggers_The_Sample_Only_When_Enabled()
    {
        var sample = new SampleData("long", SampleRate, Tone(SampleRate * 2, 220f, 0.3f));

        var engine = Make(sample);
        engine.Parameters.Set(ParamId.SamplerRetrigger, true);
        engine.Reset(1);
        Render(engine, SampleRate);
        engine.Gate();
        Render(engine, SampleRate / 2);
        Assert.InRange(engine.SamplePosition, SampleRate / 2 - 64, SampleRate / 2 + 64);

        var untriggered = Make(sample);
        untriggered.Parameters.Set(ParamId.SamplerRetrigger, false);
        untriggered.Reset(1);
        Render(untriggered, SampleRate);
        untriggered.Gate();
        Render(untriggered, SampleRate / 2);
        Assert.InRange(untriggered.SamplePosition, SampleRate * 1.5 - 64, SampleRate * 1.5 + 64);
    }

    [Fact]
    public void P2_Drives_The_Sample_Pitch_By_Default()
    {
        var sample = new SampleData("long", SampleRate, Tone(SampleRate * 2, 220f, 0.3f));

        var plain = Make(sample);
        Render(plain, SampleRate / 2);
        Assert.InRange(plain.SamplePosition, SampleRate / 2 - 1, SampleRate / 2 + 1);

        var up = Make(sample);
        up.P2 = 12f;
        up.Reset(1);
        Render(up, SampleRate / 2);
        Assert.InRange(up.SamplePosition, SampleRate - 1, SampleRate + 1);

        var unrouted = Make(sample);
        unrouted.P2 = 12f;
        unrouted.Parameters.Set(ParamId.SamplerModSource, 0);
        unrouted.Reset(1);
        Render(unrouted, SampleRate / 2);
        Assert.InRange(unrouted.SamplePosition, SampleRate / 2 - 1, SampleRate / 2 + 1);
    }

    [Fact]
    public void Root_Note_Corrects_The_Sample_Pitch()
    {
        var sample = new SampleData("a3", SampleRate, Tone(SampleRate * 2, 220f, 0.3f));
        var engine = Make(null);
        engine.Samples.Set(0, sample, rootNote: 48);   // an octave below middle C: pitch 0 plays it an octave up
        engine.Reset(1);
        Render(engine, SampleRate / 2);
        Assert.InRange(engine.SamplePosition, SampleRate - 1, SampleRate + 1);
    }

    [Fact]
    public void Am_Route_Ducks_The_Oscillators_To_The_Sample_Envelope()
    {
        var reference = Make(null, sectionsOn: true);
        var (refL, _) = Render(reference, SampleRate);
        var referenceRms = Rms(refL, SampleRate / 2);
        Assert.True(referenceRms > 0.02);

        var ducked = Make(new SampleData("silence", SampleRate, new float[SampleRate]), sectionsOn: true, amp: 0f);
        ducked.Parameters.Set(ParamId.SamplerAmDepth, 1f);
        ducked.Reset(1);
        var (duckL, _) = Render(ducked, SampleRate);
        Assert.True(Rms(duckL, SampleRate / 2) < 1e-4, $"a silent sample at full AM depth should silence the oscillators, rms {Rms(duckL, SampleRate / 2)}");

        var breathing = Make(new SampleData("loud", SampleRate, Tone(SampleRate, 440f, 0.9f)), sectionsOn: true, amp: 0f);
        breathing.Parameters.Set(ParamId.SamplerAmDepth, 1f);
        breathing.Reset(1);
        var (breathL, _) = Render(breathing, SampleRate);
        var breathRms = Rms(breathL, SampleRate / 2);
        Assert.InRange(breathRms, referenceRms * 0.6, referenceRms * 1.05);
    }

    [Fact]
    public void Render_Is_BlockSize_Invariant_With_A_Sample_And_Input()
    {
        const int n = SampleRate * 2;
        var sample = new SampleData("s", 44100, Tone(30000, 330f, 0.4f));
        var input = Tone(n, 97f, 0.2f);

        var one = Make(sample, sectionsOn: true, amp: 0.7f);
        one.Parameters.Set(ParamId.SamplerInputMix, 0.4f);
        one.Parameters.Set(ParamId.SamplerAmDepth, 0.5f);
        one.Parameters.Set(ParamId.SamplerLoopCrossfade, 30f);
        one.Parameters.Set(ParamId.SamplerDir, -0.7f);
        one.Reset(3);
        var (oneL, oneR) = Render(one, n, input, input, n);

        var blocked = Make(sample, sectionsOn: true, amp: 0.7f);
        blocked.Parameters.Set(ParamId.SamplerInputMix, 0.4f);
        blocked.Parameters.Set(ParamId.SamplerAmDepth, 0.5f);
        blocked.Parameters.Set(ParamId.SamplerLoopCrossfade, 30f);
        blocked.Parameters.Set(ParamId.SamplerDir, -0.7f);
        blocked.Reset(3);
        var blkL = new float[n];
        var blkR = new float[n];
        int[] blocks = [1, 32, 512, 7, 333, 1024, 17, 31, 64];
        int pos = 0, bi = 0;
        while (pos < n)
        {
            var len = Math.Min(blocks[bi++ % blocks.Length], n - pos);
            blocked.Process(input, input, blkL, blkR, pos, len);
            pos += len;
        }

        for (var i = 0; i < n; i++)
        {
            Assert.True(oneL[i] == blkL[i], $"L differs at {i}: one-shot={oneL[i]} blocked={blkL[i]}");
            Assert.True(oneR[i] == blkR[i], $"R differs at {i}: one-shot={oneR[i]} blocked={blkR[i]}");
        }
    }

    [Fact]
    public void Process_Does_Not_Allocate_With_A_Sample()
    {
        var a = new SampleData("a", SampleRate, Tone(9600, 330f, 0.4f));
        var b = new SampleData("b", 44100, Tone(8000, 250f, 0.4f));
        var engine = Make(a, sectionsOn: true, amp: 0.5f);
        engine.Samples.Set(1, b);
        engine.Parameters.Set(ParamId.SamplerRetrigger, true);
        var l = new float[512];
        var r = new float[512];
        for (var k = 0; k < 50; k++)
            engine.Process(l, r, l, r, l.Length);

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 1000; k++)
        {
            engine.Parameters.Set(ParamId.SamplerSelect, (k / 100) & 1);
            engine.Parameters.Set(ParamId.SamplerDir, (k & 1) == 0 ? 1f : -0.5f);
            if (k % 97 == 0)
                engine.Gate();

            engine.Process(l, r, l, r, l.Length);
        }

        var after = GC.GetAllocatedBytesForCurrentThread();

        Assert.Equal(0, after - before);
    }
}
