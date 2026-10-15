using Noumenon.Engine;
using Noumenon.Engine.Parameters;

namespace Noumenon.Vst3.Processing;

/// <summary>
/// The audio thread's work for one host block, kept apart from NPlug's types so it can be driven
/// directly. The processor hands over every parameter point and note of the block with its sample
/// offset; the renderer orders them (stably — a block's parameters before its notes at the same
/// offset), cuts the block at each offset and applies the event there, so automation and notes
/// land within the engine's control interval of where the host placed them. A table parameter
/// reaches the bank unless <see cref="Parameters.HostValues"/> says it is the echo of the panel's
/// own edit; the hidden MIDI-controller parameters become controller, aftertouch and pitch-bend
/// messages (CC 120 and 123 also release every note). Everything is preallocated in
/// <see cref="Prepare"/> and steady state allocates nothing; a block larger than announced grows
/// the buffers once. An Engine Rate change re-prepares the engine here, between blocks, because
/// nothing else may touch the engine while this thread renders.
/// </summary>
internal sealed class BlockRenderer
{
    public const int MaxEvents = 2048;

    private const int MinimumFrames = 1024;
    private const float SilenceThreshold = 0.000132184039f;

    private readonly PluginSession session;
    private readonly NoumenonEngine engine;
    private readonly HostEvent[] events = new HostEvent[MaxEvents];
    private float[] inputLeft = new float[MinimumFrames];
    private float[] inputRight = new float[MinimumFrames];
    private float[] outputLeft = new float[MinimumFrames];
    private float[] outputRight = new float[MinimumFrames];
    private int eventCount;
    private double preparedRate = NoumenonEngine.DefaultSampleRate;
    private int preparedFrames = MinimumFrames;
    private long clockMs;

    public BlockRenderer(PluginSession session)
    {
        this.session = session;
        engine = session.Engine;
    }

    private enum HostEventKind : byte
    {
        Parameter,
        MidiController,
        NoteOn,
        NoteOff,
    }

    /// <summary>A point in the block: a table parameter (id = ParamId), a MIDI controller (id = controller number) or a note (id = pitch, value = velocity 0–1).</summary>
    private readonly record struct HostEvent(int Offset, HostEventKind Kind, int Id, double Value);

    public int PendingEventCount => eventCount;

    /// <summary>UI thread, with processing stopped (VST3's setupProcessing).</summary>
    public void Prepare(double sampleRate, int maxFrames)
    {
        preparedRate = sampleRate > 0 ? sampleRate : NoumenonEngine.DefaultSampleRate;
        preparedFrames = Math.Max(maxFrames, MinimumFrames);
        EnsureCapacity(preparedFrames);
        engine.Prepare(preparedRate, preparedFrames);
        session.SampleRate = preparedRate;
        session.MaxBlockSize = maxFrames;
    }

    public void AddParameter(int offset, int vst3Id, double normalized)
    {
        if (PluginIdentity.TryGetParamId(vst3Id, out var id))
        {
            Add(new HostEvent(offset, HostEventKind.Parameter, (int)id, normalized));
            return;
        }

        var controller = PluginIdentity.MidiControllerOf(vst3Id);
        if (controller >= 0)
            Add(new HostEvent(offset, HostEventKind.MidiController, controller, normalized));
    }

    public void AddNoteOn(int offset, int pitch, float velocity) => Add(new HostEvent(offset, HostEventKind.NoteOn, pitch, velocity));

    public void AddNoteOff(int offset, int pitch) => Add(new HostEvent(offset, HostEventKind.NoteOff, pitch, 0.0));

    /// <summary>The host's transport for this block; a tempo of 0 means the host gave none and the lanes keep their free clock.</summary>
    public void SetTransport(double tempo, bool playing)
    {
        engine.Clock.HostTempo = tempo;
        engine.Clock.HostPlaying = playing;
    }

    /// <summary>A block without audio (VST3's flush, or no output bus): every event is applied at once.</summary>
    public void Flush()
    {
        clockMs = Environment.TickCount64;
        Sort();
        for (var e = 0; e < eventCount; e++)
            Apply(events[e]);

        eventCount = 0;
    }

    /// <summary>
    /// Renders <paramref name="count"/> frames. An empty input renders silence in (the instrument,
    /// or an effect whose input bus is off); an empty right output renders a mono bus's left only.
    /// </summary>
    public void Render(ReadOnlySpan<float> inLeft, ReadOnlySpan<float> inRight, Span<float> outLeft, Span<float> outRight, int count)
    {
        clockMs = Environment.TickCount64;
        EnsureCapacity(count);
        var wantsDouble = engine.Parameters.GetInt(ParamId.EngineRate) == 1;
        if (wantsDouble != (engine.Oversampling == 2))
            engine.Prepare(preparedRate, preparedFrames);

        CopyInput(inLeft, inRight, count);
        Sort();
        var rendered = 0;
        for (var e = 0; e < eventCount; e++)
        {
            var at = Math.Clamp(events[e].Offset, 0, count);
            if (at > rendered)
            {
                RenderSpan(rendered, at - rendered);
                rendered = at;
            }

            Apply(events[e]);
        }

        eventCount = 0;
        if (rendered < count)
            RenderSpan(rendered, count - rendered);

        outputLeft.AsSpan(0, count).CopyTo(outLeft);
        if (!outRight.IsEmpty)
            outputRight.AsSpan(0, count).CopyTo(outRight);
    }

    /// <summary>Whether a rendered channel is below the VST3 SDK's silence threshold, for the bus's silence flags.</summary>
    public static bool IsSilent(ReadOnlySpan<float> channel)
    {
        foreach (var sample in channel)
        {
            if (MathF.Abs(sample) > SilenceThreshold)
                return false;
        }

        return true;
    }

    private void Add(in HostEvent e)
    {
        if (eventCount < MaxEvents)
            events[eventCount++] = e;
        else
            Apply(e);
    }

    /// <summary>Insertion sort by offset: stable, allocation-free, and the events arrive nearly ordered.</summary>
    private void Sort()
    {
        for (var i = 1; i < eventCount; i++)
        {
            var e = events[i];
            var j = i - 1;
            while (j >= 0 && events[j].Offset > e.Offset)
            {
                events[j + 1] = events[j];
                j--;
            }

            events[j + 1] = e;
        }
    }

    private void Apply(in HostEvent e)
    {
        var midi = engine.Midi;
        switch (e.Kind)
        {
            case HostEventKind.Parameter:
                var id = (ParamId)e.Id;
                if (session.HostValues.Accept(id, e.Value, clockMs))
                    engine.Parameters.Set(id, ParameterTable.Get(id).FromNormalized((float)e.Value));

                break;
            case HostEventKind.MidiController:
                switch (e.Id)
                {
                    case PluginIdentity.PitchBendController:
                        midi.PitchBend((int)Math.Round(e.Value * 16383.0));
                        break;
                    case PluginIdentity.AftertouchController:
                        midi.ChannelAftertouch((int)Math.Round(e.Value * 127.0));
                        break;
                    default:
                        if (e.Id is 120 or 123)
                            midi.AllNotesOff();

                        midi.ControlChange(e.Id, (int)Math.Round(e.Value * 127.0));
                        break;
                }

                break;
            case HostEventKind.NoteOn:
                midi.NoteOn(e.Id, Math.Max(1, (int)Math.Round(e.Value * 127.0)));
                break;
            case HostEventKind.NoteOff:
                midi.NoteOff(e.Id);
                break;
        }
    }

    private void RenderSpan(int offset, int length)
    {
        if (session.IsEffect)
            engine.Process(inputLeft, inputRight, outputLeft, outputRight, offset, length);
        else
            engine.Process(outputLeft, outputRight, offset, length);
    }

    private void CopyInput(ReadOnlySpan<float> inLeft, ReadOnlySpan<float> inRight, int count)
    {
        if (!session.IsEffect)
            return;

        if (inLeft.Length < count)
        {
            Array.Clear(inputLeft, 0, count);
            Array.Clear(inputRight, 0, count);
            return;
        }

        inLeft[..count].CopyTo(inputLeft);
        (inRight.Length >= count ? inRight : inLeft)[..count].CopyTo(inputRight);
    }

    private void EnsureCapacity(int frames)
    {
        if (outputLeft.Length >= frames)
            return;

        inputLeft = new float[frames];
        inputRight = new float[frames];
        outputLeft = new float[frames];
        outputRight = new float[frames];
    }
}
