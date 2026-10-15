using Noumenon.Engine.Parameters;
using Noumenon.Vst3.Processing;

namespace Noumenon.Vst3.Tests;

/// <summary>
/// The audio thread's block work without NPlug around it: notes and parameter points cut the
/// block where the host put them, the panel's own edits coming back from the host are not applied
/// twice, the hidden MIDI-controller parameters become MIDI, the effect hears its input, and a
/// steady stream of events allocates nothing.
/// </summary>
public class RendererTests
{
    private const int SampleRate = 48000;
    private const int Block = 512;

    private readonly float[] left = new float[Block];
    private readonly float[] right = new float[Block];

    private static (PluginSession Session, BlockRenderer Renderer) Open(bool effect = false)
    {
        var session = new PluginSession(effect);
        var renderer = new BlockRenderer(session);
        renderer.Prepare(SampleRate, Block);

        return (session, renderer);
    }

    private float Render(BlockRenderer renderer, int blocks, float[]? input = null)
    {
        var peak = 0f;
        for (var i = 0; i < blocks; i++)
        {
            renderer.Render(input ?? [], input ?? [], left, right, Block);
            for (var s = 0; s < Block; s++)
                peak = MathF.Max(peak, MathF.Max(MathF.Abs(left[s]), MathF.Abs(right[s])));
        }

        return peak;
    }

    private static double Normalized(ParamId id, float value) => ParameterTable.Get(id).ToNormalized(value);

    [Fact]
    public void Renders_Sound_And_Cuts_Blocks_At_Notes()
    {
        var (session, renderer) = Open();
        var engine = session.Engine;
        engine.Parameters.Set(ParamId.P1Midi, true);
        engine.Parameters.Set(ParamId.P1Glide, 0f);

        // The init patch's Drift is a through-delay at Mix 1: ~120 ms of the delay filling first.
        Assert.True(Render(renderer, 20) > 0.01f, "the init patch should sound from the first blocks");

        renderer.AddNoteOn(256, 72, 0.8f);
        Assert.Equal(1, renderer.PendingEventCount);
        Render(renderer, 1);
        Assert.Equal(0, renderer.PendingEventCount);
        Render(renderer, 10);
        Assert.Equal(72, engine.Midi.LastNote);
        Assert.InRange(engine.P1, 11.5f, 12.5f);   // note 72 is twelve semitones above middle C

        renderer.AddNoteOff(0, 72);
        Render(renderer, 2);
        Assert.Equal(0, engine.Midi.HeldCount);
    }

    [Fact]
    public void Parameter_Points_Reach_The_Bank_In_Offset_Order()
    {
        var (session, renderer) = Open();
        var id = PluginIdentity.ToVst3Id(ParamId.OutputVolume);

        renderer.AddParameter(400, id, Normalized(ParamId.OutputVolume, -6f));
        renderer.AddParameter(100, id, Normalized(ParamId.OutputVolume, -12f));
        renderer.AddParameter(0, 999_999, 0.5);   // not a parameter of ours
        Assert.Equal(2, renderer.PendingEventCount);
        Render(renderer, 1);

        Assert.Equal(-6f, session.Engine.Parameters.Get(ParamId.OutputVolume), 3);
        Assert.Equal(Normalized(ParamId.OutputVolume, -6f), session.HostValues.Agreed(ParamId.OutputVolume), 6);
    }

    [Fact]
    public void Echoes_Of_Panel_Edits_Are_Ignored_But_Host_Values_Are_Applied()
    {
        var (session, renderer) = Open();
        var bank = session.Engine.Parameters;
        var info = ParameterTable.Get(ParamId.OutputVolume);
        var id = PluginIdentity.ToVst3Id(ParamId.OutputVolume);
        var now = Environment.TickCount64;

        bank.Set(ParamId.OutputVolume, -12f);
        Assert.True(session.HostValues.TryPush(info, -12f, now, out var first));
        bank.Set(ParamId.OutputVolume, -15f);   // the panel moved on before the host echoed the first edit
        Assert.True(session.HostValues.TryPush(info, -15f, now, out _));

        renderer.AddParameter(0, id, first);
        Render(renderer, 1);
        Assert.Equal(-15f, bank.Get(ParamId.OutputVolume), 3);

        renderer.AddParameter(0, id, Normalized(ParamId.OutputVolume, -3f));   // automation
        Render(renderer, 1);
        Assert.Equal(-3f, bank.Get(ParamId.OutputVolume), 3);
        Assert.False(session.HostValues.TryPush(info, bank.Get(ParamId.OutputVolume), now, out _));
    }

    [Fact]
    public void Midi_Controller_Parameters_Become_Bend_Wheel_And_Aftertouch()
    {
        var (session, renderer) = Open();
        var midi = session.Engine.Midi;

        renderer.AddParameter(0, PluginIdentity.MidiControllerIdBase + PluginIdentity.PitchBendController, 1.0);
        renderer.AddParameter(0, PluginIdentity.MidiControllerIdBase + 1, 1.0);
        renderer.AddParameter(0, PluginIdentity.MidiControllerIdBase + PluginIdentity.AftertouchController, 0.5);
        Render(renderer, 2);

        Assert.InRange(midi.Bend, 0.99f, 1f);
        Assert.Equal(1f, midi.Wheel);
        Assert.InRange(midi.Aftertouch, 0.49f, 0.51f);
    }

    [Fact]
    public void A_Block_Without_Audio_Applies_Its_Events_At_Once()
    {
        var (session, renderer) = Open();
        renderer.AddParameter(10, PluginIdentity.ToVst3Id(ParamId.ReverbRoom), Normalized(ParamId.ReverbRoom, 3f));
        renderer.AddNoteOn(0, 64, 1f);

        renderer.Flush();
        Assert.Equal(0, renderer.PendingEventCount);
        Assert.Equal(3, session.Engine.Parameters.GetInt(ParamId.ReverbRoom));

        Render(renderer, 1);   // the note is drained at the next control tick
        Assert.Equal(64, session.Engine.Midi.LastNote);
    }

    [Fact]
    public void Engine_Rate_Change_Re_Prepares_Between_Blocks()
    {
        var (session, renderer) = Open();
        var engine = session.Engine;
        Render(renderer, 2);
        var latency = engine.LatencySamples;

        engine.Parameters.Set(ParamId.EngineRate, 1);
        Render(renderer, 2);

        Assert.Equal(2, engine.Oversampling);
        Assert.NotEqual(latency, engine.LatencySamples);
        Assert.True(Render(renderer, 20) > 0.01f);
    }

    [Fact]
    public void The_Effect_Hears_Its_Input_Through_Samp_In()
    {
        var input = new float[Block];
        for (var s = 0; s < Block; s++)
            input[s] = 0.5f * MathF.Sin(2f * MathF.PI * 375f * s / SampleRate);   // a whole number of cycles per block

        float RenderEffect(float[]? signal)
        {
            var (session, renderer) = Open(effect: true);
            var bank = session.Engine.Parameters;
            bank.Set(ParamId.SectionALevel, 0f);
            bank.Set(ParamId.SectionBLevel, 0f);
            bank.Set(ParamId.SamplerInputMix, 1f);
            bank.Set(ParamId.SamplerAmp, 1f);
            renderer.Prepare(SampleRate, Block);   // start from this patch, not from a glide away from the init drone
            Render(renderer, 10, signal);

            return Render(renderer, 30, signal);
        }

        Assert.True(RenderEffect(input) > 0.05f, "the input should reach the output through Samp↔In");
        Assert.True(RenderEffect(null) < 0.005f, "with the oscillators down and no input there is nothing to hear");
    }

    [Fact]
    public void Steady_State_Render_Does_Not_Allocate()
    {
        var (_, renderer) = Open();
        var volume = PluginIdentity.ToVst3Id(ParamId.OutputVolume);
        var bend = PluginIdentity.MidiControllerIdBase + PluginIdentity.PitchBendController;

        void RunBlock()
        {
            renderer.AddParameter(64, volume, 0.6);
            renderer.AddParameter(200, bend, 0.75);
            renderer.AddNoteOn(100, 60, 0.7f);
            renderer.AddNoteOff(300, 60);
            renderer.Render([], [], left, right, Block);
        }

        for (var i = 0; i < 20; i++)
            RunBlock();

        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < 50; i++)
            RunBlock();

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
