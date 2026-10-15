using System.Runtime;
using NPlug;
using NPlug.IO;
using Noumenon.Ui.Hosting;
using Noumenon.Vst3.Parameters;

namespace Noumenon.Vst3.Processing;

/// <summary>
/// The VST3 component of one plugin instance: it owns the session (the engine) and is what the
/// host processes and asks for state. The two identities differ only in their buses — the effect
/// has a stereo main input feeding Samp↔In — so they are two sealed subclasses of this one. Both
/// have a stereo auxiliary "Sidechain" input, off until the host routes something to it, which only
/// the envelope follower hears (its Sidechain source). Each block,
/// every parameter point and note goes to the <see cref="BlockRenderer"/> with its sample offset,
/// the host's tempo and transport go to the lane clock, and the renderer cuts the block at those
/// offsets; a block without audio (the host flushing parameters) applies them at once. The output
/// bus's silence flags follow what was actually rendered.
///
/// The latency the host compensates is the engine's, which changes with Engine Rate, so it is
/// answered live (re-implementing the one interface member NPlug fixes at construction); the
/// controller asks the host to re-read it. The tail is infinite — the instrument drones without
/// input, so a host must never put it to sleep. State is the session's snapshot JSON, the same as
/// a <c>.noumenon</c> file and the VST2 chunk. On connection the processor sends its session key
/// to the controller, and sends it again whenever the controller asks.
/// </summary>
internal abstract class NoumenonProcessor : AudioProcessor<NoumenonModel>, IAudioProcessor
{
    private const uint InfiniteTail = uint.MaxValue;

    private volatile bool sidechainActive;

    protected NoumenonProcessor(bool isEffect)
        : base(AudioSampleSizeSupport.Float32, 0, InfiniteTail, AudioProcessContextRequirementFlags.NeedTempo | AudioProcessContextRequirementFlags.NeedTransportState)
    {
        GCSettings.LatencyMode = GCLatencyMode.SustainedLowLatency;
        MediaFoundationDecoding.Register();
        Session = new PluginSession(isEffect);
        Renderer = new BlockRenderer(Session);
    }

    public PluginSession Session { get; }

    public BlockRenderer Renderer { get; }

    public override Guid ControllerClassId => PluginIdentity.ControllerClassId;

    uint IAudioProcessor.LatencySamples => (uint)Session.Engine.LatencySamples;

    /// <summary>The sidechain's place among the input buses: after the effect's main input, first for the instrument.</summary>
    public int SidechainBusIndex => Session.IsEffect ? 1 : 0;

    protected override bool Initialize(AudioHostApplication host)
    {
        if (Session.IsEffect)
            AddAudioInput("Input", SpeakerArrangement.SpeakerStereo);

        AddAudioInput("Sidechain", SpeakerArrangement.SpeakerStereo, BusType.Auxiliary, default);
        AddAudioOutput("Output", SpeakerArrangement.SpeakerStereo);
        AddEventInput("MIDI Input", 16);
        NPlugWorkarounds.SeedStrings(host, SessionMessages.Ids);
        Session.Register();

        return true;
    }

    protected override void Terminate() => Session.Unregister();

    protected override void OnSetupProcessing(in AudioProcessSetupData processSetupData) => Renderer.Prepare(processSetupData.SampleRate, processSetupData.MaxSamplesPerBlock);

    protected override void OnActivate(bool isActive)
    {
        if (!isActive)
            Session.Engine.Midi.AllNotesOff();
    }

    protected override bool OnBusActivate(BusInfo busInfo, bool newActiveState)
    {
        if (busInfo.MediaType == BusMediaType.Audio && busInfo.Direction == BusDirection.Input && busInfo.BusType == BusType.Auxiliary)
            sidechainActive = newActiveState;

        return newActiveState;
    }

    protected override void SaveState(PortableBinaryWriter writer) => writer.Stream.Write(Session.SaveState());

    protected override void RestoreState(PortableBinaryReader reader)
    {
        using var buffer = new MemoryStream();
        reader.Stream.CopyTo(buffer);
        Session.LoadState(buffer.GetBuffer().AsSpan(0, (int)buffer.Length));
    }

    protected override void OnConnect(IAudioConnectionPoint connectionPoint) => SendSessionKey(connectionPoint);

    protected override void OnMessage(AudioMessage message)
    {
        if (message.Id == SessionMessages.Hello)
            SendSessionKey(ConnectionPoint);
    }

    protected override void Process(in AudioProcessData data)
    {
        var renderer = Renderer;
        var changes = data.Input.ParameterChanges;
        var changeCount = changes.Count;
        for (var i = 0; i < changeCount; i++)
        {
            var queue = changes.GetParameterData(i);
            var id = queue.ParameterId.Value;
            var points = queue.PointCount;
            for (var p = 0; p < points; p++)
            {
                var value = queue.GetPoint(p, out var offset);
                renderer.AddParameter(offset, id, value);
            }
        }

        var events = data.Input.Events;
        var eventCount = events.Count;
        for (var i = 0; i < eventCount; i++)
        {
            if (!events.TryGetEvent(i, out var e))
                continue;

            if (e.Kind == AudioEventKind.NoteOn)
                renderer.AddNoteOn(e.SampleOffset, e.Value.NoteOn.Pitch, e.Value.NoteOn.Velocity);
            else if (e.Kind == AudioEventKind.NoteOff)
                renderer.AddNoteOff(e.SampleOffset, e.Value.NoteOff.Pitch);
        }

        if (data.HasContext)
        {
            ref var context = ref data.GetContext();
            renderer.SetTransport((context.Flags & AudioProcessContextFlags.TempoValid) != 0 ? context.Tempo : 0.0, (context.Flags & AudioProcessContextFlags.Playing) != 0);
        }

        var frames = data.SampleCount;
        if (frames <= 0 || data.Output.BusCount == 0 || data.Output[0].ChannelCount == 0)
        {
            renderer.Flush();
            return;
        }

        ref readonly var setup = ref ProcessSetupData;
        ref var output = ref data.Output[0];
        var left = output.GetChannelSpanAsFloat32(setup, data, 0);
        var right = output.ChannelCount > 1 ? output.GetChannelSpanAsFloat32(setup, data, 1) : Span<float>.Empty;
        scoped ReadOnlySpan<float> inLeft = default;
        scoped ReadOnlySpan<float> inRight = default;
        if (Session.IsEffect && data.Input.BusCount > 0 && data.Input[0].ChannelCount > 0)
        {
            ref var input = ref data.Input[0];
            inLeft = input.GetChannelSpanAsFloat32(setup, data, 0);
            inRight = input.ChannelCount > 1 ? input.GetChannelSpanAsFloat32(setup, data, 1) : inLeft;
        }

        // An inactive bus may come without buffers, so the sidechain is read only while the host has it on.
        scoped ReadOnlySpan<float> sideLeft = default;
        scoped ReadOnlySpan<float> sideRight = default;
        var side = SidechainBusIndex;
        if (sidechainActive && data.Input.BusCount > side && data.Input[side].ChannelCount > 0)
        {
            ref var sidechain = ref data.Input[side];
            sideLeft = sidechain.GetChannelSpanAsFloat32(setup, data, 0);
            sideRight = sidechain.ChannelCount > 1 ? sidechain.GetChannelSpanAsFloat32(setup, data, 1) : sideLeft;
        }

        renderer.Render(inLeft, inRight, sideLeft, sideRight, left, right, frames);
        output.SilenceFlags = 0;
        output.SetChannelSilence(0, BlockRenderer.IsSilent(left));
        if (output.ChannelCount > 1)
            output.SetChannelSilence(1, BlockRenderer.IsSilent(right));
    }

    private void SendSessionKey(IAudioConnectionPoint? peer)
    {
        if (peer is null || Host is not { } host || !host.TryCreateMessage(SessionMessages.Session, out var message))
            return;

        // A failure here must not fail the host's connect: without the key the controller has no
        // editor, but the plugin still plays.
        try
        {
            message.Id = SessionMessages.Session;
            message.Attributes.TrySetInt64(SessionMessages.KeyAttribute, Session.Key);
            peer.Notify(message);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
        }
        finally
        {
            NPlugWorkarounds.Release(ref message);
        }
    }
}

/// <summary>The instrument: a stereo output and a MIDI input, category Instrument|Synth.</summary>
internal sealed class InstrumentProcessor : NoumenonProcessor
{
    public InstrumentProcessor()
        : base(isEffect: false)
    {
    }
}

/// <summary>The effect: the instrument plus a stereo input feeding Samp↔In, category Fx, so a host routes a track into it.</summary>
internal sealed class EffectProcessor : NoumenonProcessor
{
    public EffectProcessor()
        : base(isEffect: true)
    {
    }
}
