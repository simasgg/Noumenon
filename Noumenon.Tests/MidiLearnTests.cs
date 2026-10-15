using System.Text;
using Noumenon.Engine;
using Noumenon.Engine.Hosting;
using Noumenon.Engine.Modulation;
using Noumenon.Engine.Parameters;
using Noumenon.Engine.Presets;

namespace Noumenon.Tests;

/// <summary>
/// MIDI learn: the next controller binds to the armed parameter and moves its bank value through
/// the normalized range, one controller per parameter and the other way round, channel-mode
/// controllers are never learned, nothing moves until a message arrives, a trigger fires on a
/// pressed controller, and the map lives in a host's project state but not in a preset pick.
/// The matrix's CC source follows the Matrix CC parameter.
/// </summary>
public class MidiLearnTests
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

    private static void Tick(NoumenonEngine engine, int blocks = 1)
    {
        var l = new float[64];
        var r = new float[64];
        for (var i = 0; i < blocks; i++)
            engine.Process(l, r, l.Length);
    }

    [Fact]
    public void The_Next_Controller_Binds_And_Moves_The_Parameter()
    {
        var engine = Make();
        engine.MidiMap.BeginLearn(ParamId.MasterFilterCutoff);
        Assert.Equal(ParamId.MasterFilterCutoff, engine.MidiMap.Learning);

        engine.Midi.ControlChange(21, 127);
        Tick(engine);
        Assert.Null(engine.MidiMap.Learning);
        Assert.Equal(21, engine.MidiMap.ControllerFor(ParamId.MasterFilterCutoff));
        Assert.Equal(1, engine.MidiMap.LearnedCount);
        Assert.Equal(20000f, engine.Parameters.Get(ParamId.MasterFilterCutoff), 1);

        engine.Midi.ControlChange(21, 0);
        Tick(engine);
        Assert.Equal(20f, engine.Parameters.Get(ParamId.MasterFilterCutoff), 1);

        engine.Midi.ControlChange(22, 64);   // an unbound controller moves nothing
        Tick(engine);
        Assert.Equal(20f, engine.Parameters.Get(ParamId.MasterFilterCutoff), 1);
    }

    [Fact]
    public void A_Binding_Is_One_To_One_And_Learning_Again_Moves_It()
    {
        var map = new MidiMap();
        map.Assign(21, ParamId.OscA1Level);
        map.Assign(22, ParamId.OscA1Level);
        Assert.Equal(22, map.ControllerFor(ParamId.OscA1Level));
        Assert.Null(map.ParameterFor(21));

        map.Assign(22, ParamId.OscA2Level);
        Assert.Equal(-1, map.ControllerFor(ParamId.OscA1Level));
        Assert.Equal(ParamId.OscA2Level, map.ParameterFor(22));

        map.Assign(123, ParamId.OscA3Level);   // all notes off is a channel-mode message, never a knob
        Assert.Equal(-1, map.ControllerFor(ParamId.OscA3Level));

        map.Forget(ParamId.OscA2Level);
        Assert.Empty(map.Capture());
    }

    [Fact]
    public void Channel_Mode_Messages_Are_Not_Learned()
    {
        var engine = Make();
        engine.MidiMap.BeginLearn(ParamId.OscA1Level);
        engine.Midi.ControlChange(121, 0);
        Tick(engine);
        Assert.Equal(ParamId.OscA1Level, engine.MidiMap.Learning);
    }

    [Fact]
    public void A_Loaded_Map_Moves_Nothing_Until_A_Message_Arrives()
    {
        var engine = Make();
        engine.ApplyMidiMap([new MidiMapping { Controller = 30, Parameter = nameof(ParamId.OscA1Level) }, new MidiMapping { Controller = 31, Parameter = "NoSuchParameter" }]);
        Tick(engine, 4);
        Assert.Equal(0.6f, engine.Parameters.Get(ParamId.OscA1Level), 4);
        Assert.Single(engine.MidiMap.Capture());

        engine.Midi.ControlChange(30, 127);
        Tick(engine);
        Assert.Equal(1f, engine.Parameters.Get(ParamId.OscA1Level), 4);
    }

    [Fact]
    public void A_Trigger_Fires_When_Its_Controller_Is_Pressed()
    {
        var engine = Make();
        engine.MidiMap.Assign(64, ParamId.RandomizeNow);
        engine.Midi.ControlChange(64, 0);
        Tick(engine);
        Assert.False(engine.ConsumeRandomizeRequest());

        engine.Midi.ControlChange(64, 127);
        Tick(engine);
        Assert.True(engine.ConsumeRandomizeRequest());
    }

    [Fact]
    public void The_Map_Travels_With_A_Host_Project_But_Not_With_A_Preset_Pick()
    {
        var engine = Make();
        engine.MidiMap.Assign(21, ParamId.SpinTime1);
        var state = engine.CapturePreset("mapped");
        var json = PresetIo.Serialize(state);
        Assert.Contains("\"MidiMap\"", json);

        var other = Make();
        other.MidiMap.Assign(40, ParamId.ReverbMix);
        other.ApplyPreset(PresetIo.Deserialize(json), loadSamples: false);
        Assert.Equal(40, other.MidiMap.ControllerFor(ParamId.ReverbMix));
        Assert.Equal(-1, other.MidiMap.ControllerFor(ParamId.SpinTime1));

        var session = new Session();
        Assert.True(session.LoadState(Encoding.UTF8.GetBytes(json)));
        Assert.Equal(21, session.Engine.MidiMap.ControllerFor(ParamId.SpinTime1));
    }

    [Fact]
    public void Older_Presets_Without_A_Map_Still_Load()
    {
        var json = "{\"Version\":1,\"Name\":\"old\",\"Parameters\":{\"OutputVolume\":-3}}";
        var state = PresetIo.Deserialize(json);
        Assert.Empty(state.MidiMap);
    }

    [Fact]
    public void The_Matrix_Cc_Source_Follows_The_Matrix_Cc_Parameter()
    {
        var engine = Make();
        var bank = engine.Parameters;
        bank.Set(ParamId.MatrixCc, 20);
        bank.Set(ParamId.ModSlot1Source, 10);   // CC
        bank.Set(ParamId.ModSlot1Target, (int)ParamId.OscA2Level + 1);
        bank.Set(ParamId.ModSlot1Depth, 0.5f);

        engine.Midi.ControlChange(74, 127);
        Tick(engine, 2);
        Assert.Equal(0.5f, engine.Resolved.Get(ParamId.OscA2Level), 4);

        engine.Midi.ControlChange(20, 127);
        Tick(engine, 2);
        Assert.Equal(1f, engine.Resolved.Get(ParamId.OscA2Level), 4);
        Assert.Equal(1f, engine.ModulationSource(10), 4);
    }

    private sealed class Session() : PluginSessionBase(null);
}
