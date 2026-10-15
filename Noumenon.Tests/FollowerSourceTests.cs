using Noumenon.Dsp.Sampler;
using Noumenon.Engine;
using Noumenon.Engine.Modulation;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>
/// The sidechain envelope follower: each source hears what it says — the sampler mix, the sample
/// alone, the live input, the separate sidechain, the sections, the output — the gain scales what
/// it hears, and the matrix carries it to any target; the sidechain overload stays allocation-free
/// and changes nothing else in the sound.
/// </summary>
public class FollowerSourceTests
{
    private const int SampleRate = 48000;
    private const int Frames = SampleRate / 2;

    private static float[] Tone(int frames, float amplitude) => Enumerable.Range(0, frames).Select(i => amplitude * MathF.Sin(2f * MathF.PI * 300f * i / SampleRate)).ToArray();

    private static NoumenonEngine Make(FollowerSource source, bool sectionsOn = false)
    {
        var engine = new NoumenonEngine();
        var bank = engine.Parameters;
        EngineTests.BypassEffects(bank);
        if (!sectionsOn)
        {
            for (var s = 0; s < ParameterTable.SectionCount; s++)
            {
                for (var slot = 0; slot < ParameterTable.SlotsPerSection; slot++)
                    bank.Set(ParameterTable.Osc((SectionId)s, slot, OscParam.On), false);
            }
        }

        bank.Set(ParamId.EnvFollowerSource, (int)source);
        bank.Set(ParamId.EnvFollowerAttack, 1f);
        bank.Set(ParamId.EnvFollowerRelease, 20f);
        engine.Prepare(SampleRate, 512);
        engine.Reset(1);

        return engine;
    }

    private static float Follow(NoumenonEngine engine, float[]? input = null, float[]? sidechain = null)
    {
        var silence = new float[Frames];
        var l = new float[Frames];
        var r = new float[Frames];
        var max = 0f;
        for (var pos = 0; pos < Frames; pos += 512)
        {
            var count = Math.Min(512, Frames - pos);
            engine.Process(input ?? silence, input ?? silence, sidechain ?? silence, sidechain ?? silence, l, r, pos, count);
            if (pos > Frames / 2)
                max = MathF.Max(max, engine.EnvelopeFollowerValue);
        }

        return max;
    }

    [Fact]
    public void Input_Hears_The_Input_Even_When_Samp_In_Is_At_Zero()
    {
        var input = Tone(Frames, 0.5f);
        Assert.InRange(Follow(Make(FollowerSource.Input), input), 0.4f, 0.55f);
        Assert.True(Follow(Make(FollowerSource.SampleAndInput), input) < 0.01f, "Samp↔In at 0: the mix holds no input");
    }

    [Fact]
    public void Sidechain_Hears_Only_The_Sidechain()
    {
        var sidechain = Tone(Frames, 0.25f);
        Assert.InRange(Follow(Make(FollowerSource.Sidechain), sidechain: sidechain), 0.2f, 0.3f);
        Assert.True(Follow(Make(FollowerSource.Input), sidechain: sidechain) < 0.01f);
        Assert.True(Follow(Make(FollowerSource.SampleAndInput), sidechain: sidechain) < 0.01f);
    }

    [Fact]
    public void Sample_Hears_The_Sample_Whatever_Samp_In_Does()
    {
        var engine = Make(FollowerSource.Sample);
        engine.Samples.Set(0, new SampleData("tone", SampleRate, Tone(SampleRate, 0.5f)));
        engine.Parameters.Set(ParamId.SamplerInputMix, 1f);
        engine.Parameters.Set(ParamId.SamplerLoopCrossfade, 0f);
        Assert.InRange(Follow(engine), 0.4f, 0.55f);
    }

    [Fact]
    public void Sections_And_Output_Hear_The_Instrument()
    {
        Assert.True(Follow(Make(FollowerSource.Sections, sectionsOn: true)) > 0.05f);
        Assert.True(Follow(Make(FollowerSource.Output, sectionsOn: true)) > 0.05f);
        Assert.True(Follow(Make(FollowerSource.Output)) < 1e-4f, "silent instrument, silent follower");
    }

    [Fact]
    public void The_Gain_Scales_What_It_Hears()
    {
        var input = Tone(Frames, 0.1f);
        var engine = Make(FollowerSource.Input);
        engine.Parameters.Set(ParamId.EnvFollowerGain, 12f);
        Assert.InRange(Follow(engine, input), 0.3f, 0.42f);
    }

    [Fact]
    public void The_Sidechain_Reaches_Any_Target_Through_The_Matrix()
    {
        var engine = Make(FollowerSource.Sidechain);
        var bank = engine.Parameters;
        bank.Set(ParamId.ModSlot1Source, 6);   // Env Follower
        bank.Set(ParamId.ModSlot1Target, (int)ParamId.OutputWidth + 1);
        bank.Set(ParamId.ModSlot1Depth, 1f);
        bank.Set(ParamId.OutputWidth, 0f);
        Follow(engine, sidechain: Tone(Frames, 0.5f));

        Assert.True(engine.Resolved.Get(ParamId.OutputWidth) > 0.8f, $"width {engine.Resolved.Get(ParamId.OutputWidth)}");
    }

    [Fact]
    public void The_Sidechain_Overload_Does_Not_Allocate_Or_Change_The_Sound()
    {
        var a = Make(FollowerSource.Sidechain, sectionsOn: true);
        var b = Make(FollowerSource.Sidechain, sectionsOn: true);
        var sidechain = Tone(512, 0.5f);
        var silence = new float[512];
        var la = new float[512];
        var ra = new float[512];
        var lb = new float[512];
        var rb = new float[512];
        for (var k = 0; k < 20; k++)
        {
            a.Process(silence, silence, la, ra, 0, 512);
            b.Process(silence, silence, sidechain, sidechain, lb, rb, 0, 512);
            Assert.Equal(la, lb);
        }

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var k = 0; k < 500; k++)
            b.Process(silence, silence, sidechain, sidechain, lb, rb, 0, 512);

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
