using Noumenon.Engine;
using Noumenon.Engine.Lanes;
using Noumenon.Engine.Parameters;

namespace Noumenon.Tests;

/// <summary>The resolved-value layer: mod-wheel box, lanes and matrix slots all show up in what the engine uses.</summary>
public class ModMatrixTests
{
    private const int SampleRate = 48000;

    private static NoumenonEngine Make()
    {
        var engine = new NoumenonEngine();
        EngineTests.BypassEffects(engine.Parameters);
        engine.Prepare(SampleRate, 512);
        engine.Reset(1);

        return engine;
    }

    private static void RenderBlocks(NoumenonEngine engine, int blocks, Action<int>? each = null)
    {
        var l = new float[512];
        var r = new float[512];
        for (var i = 0; i < blocks; i++)
        {
            engine.Process(l, r, l.Length);
            each?.Invoke(i);
        }
    }

    private static int Target(ParamId id) => (int)id + 1;

    [Fact]
    public void An_Lfo_Sweeps_Its_Target_Within_Range()
    {
        var engine = Make();
        var bank = engine.Parameters;
        bank.Set(ParamId.Lfo1Rate, 2f);
        bank.Set(ParamId.Lfo1Shape, 0);
        bank.Set(ParamId.ModSlot1Source, 3);   // LFO 1
        bank.Set(ParamId.ModSlot1Target, Target(ParamId.MasterFilterCutoff));
        bank.Set(ParamId.ModSlot1Depth, 0.5f);

        var min = float.MaxValue;
        var max = float.MinValue;
        RenderBlocks(engine, 100, _ =>
        {
            var cutoff = engine.Resolved.Get(ParamId.MasterFilterCutoff);
            min = MathF.Min(min, cutoff);
            max = MathF.Max(max, cutoff);
        });

        Assert.True(min < 1000f, $"min {min}");
        Assert.True(max > 15000f, $"max {max}");
        Assert.Equal(12000f, bank.Get(ParamId.MasterFilterCutoff));   // the bank itself is untouched
    }

    [Fact]
    public void The_Wheel_Box_Drives_Its_Target_Between_Min_And_Max()
    {
        var engine = Make();
        var bank = engine.Parameters;
        bank.Set(ParamId.WheelTarget, Target(ParamId.OscA1Level));
        bank.Set(ParamId.WheelMin, 0.2f);
        bank.Set(ParamId.WheelMax, 0.8f);

        engine.Midi.ControlChange(1, 0);
        RenderBlocks(engine, 2);
        Assert.Equal(0.2f, engine.Resolved.Get(ParamId.OscA1Level), 4);

        engine.Midi.ControlChange(1, 127);
        RenderBlocks(engine, 2);
        Assert.Equal(0.8f, engine.Resolved.Get(ParamId.OscA1Level), 4);
    }

    [Fact]
    public void A_Macro_Offsets_Its_Target()
    {
        var engine = Make();
        var bank = engine.Parameters;
        bank.Set(ParamId.ModSlot2Source, 11);   // Macro 1
        bank.Set(ParamId.ModSlot2Target, Target(ParamId.OscA2Level));
        bank.Set(ParamId.ModSlot2Depth, -0.5f);
        bank.Set(ParamId.Macro1, 1f);
        RenderBlocks(engine, 2);

        Assert.Equal(0f, engine.Resolved.Get(ParamId.OscA2Level), 4);   // 0.5 − 0.5, clamped at the range
    }

    [Fact]
    public void A_Playing_Lane_Overrides_Its_Fader_In_The_Resolved_Values()
    {
        var engine = Make();
        engine.Lanes.ForParameter(ParamId.OscA1Level)!.Load(new LaneSnapshot([0.9f, 0.9f], LaneMode.Automated, 1f));
        RenderBlocks(engine, 2);

        Assert.Equal(0.9f, engine.Resolved.Get(ParamId.OscA1Level), 4);
        Assert.Equal(0.6f, engine.Parameters.Get(ParamId.OscA1Level), 4);
        Assert.True(engine.Lanes.AnyPlaying);
    }

    [Fact]
    public void Randomize_Trigger_Is_Handed_To_The_Ui_Thread()
    {
        var engine = Make();
        engine.Parameters.Set(ParamId.RandomizeNow, 1f);
        RenderBlocks(engine, 1);

        Assert.Equal(0f, engine.Parameters.Get(ParamId.RandomizeNow));
        Assert.True(engine.ConsumeRandomizeRequest());
        Assert.False(engine.ConsumeRandomizeRequest());
    }
}
