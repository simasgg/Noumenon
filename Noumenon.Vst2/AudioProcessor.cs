using Jacobi.Vst.Core;
using Jacobi.Vst.Plugin.Framework;
using Jacobi.Vst.Plugin.Framework.Plugin;
using Noumenon.Engine;
using Noumenon.Engine.Parameters;
using Noumenon.Vst2.Parameters;

namespace Noumenon.Vst2;

/// <summary>
/// The audio thread. Each block pulls the host's parameter changes into the bank, refreshes the
/// lane clock from the host's time info every few blocks, copies the host's buffers into float
/// arrays (the instrument identity has no inputs and feeds the engine silence) and renders in
/// sub-blocks cut at the MIDI events the host delivered for this block, so a note lands within
/// the engine's control interval of where the host put it. Steady state is allocation-free: the
/// scratch arrays are sized by the announced block size and grow only if a host sends a bigger
/// one; the only allocation left is the framework's time-info object, which is why it is asked
/// for every few blocks rather than every block. An Engine Rate change re-prepares the engine
/// here, between blocks, because nothing else may touch the engine while this thread renders.
/// Host bypass passes the input through (effect) or goes silent (instrument).
/// </summary>
internal sealed class AudioProcessor : VstPluginAudioProcessor, IVstPluginBypass, IVstMidiProcessor
{
    public const int MaxPendingEvents = 512;

    private const int TimeInfoEveryBlocks = 8;
    private const int InitialFrames = 4096;
    private const int TailSeconds = 600;

    private readonly PluginSession session;
    private readonly PluginParameters parameters;
    private readonly NoumenonEngine engine;
    private readonly PendingEvent[] pending = new PendingEvent[MaxPendingEvents];
    private float[] inputLeft = new float[InitialFrames];
    private float[] inputRight = new float[InitialFrames];
    private float[] outputLeft = new float[InitialFrames];
    private float[] outputRight = new float[InitialFrames];
    private IVstHostSequencer? sequencer;
    private bool sequencerLooked;
    private bool timeInfoBroken;
    private int pendingCount;
    private int blocksUntilTimeInfo;
    private double preparedRate;
    private int preparedFrames;
    private bool bypass;

    public AudioProcessor(PluginSession session, PluginParameters parameters)
        : base(PluginIdentity.InputCount, PluginIdentity.OutputCount, TailSeconds * (int)NoumenonEngine.DefaultSampleRate, noSoundInStop: false)
    {
        this.session = session;
        this.parameters = parameters;
        engine = session.Engine;
        preparedRate = engine.HostSampleRate;
        preparedFrames = InitialFrames;
    }

    private readonly record struct PendingEvent(int Delta, byte Status, byte Data1, byte Data2);

    public bool Bypass
    {
        get => Volatile.Read(ref bypass);
        set => Volatile.Write(ref bypass, value);
    }

    public int ChannelCount => 16;

    public int PendingEventCount => pendingCount;

    public override float SampleRate
    {
        get => base.SampleRate;
        set
        {
            base.SampleRate = value;
            if (value > 0f)
                PrepareIfChanged(value, Math.Max(BlockSize, 0));
        }
    }

    public override int BlockSize
    {
        get => base.BlockSize;
        set
        {
            base.BlockSize = value;
            if (value > 0)
                PrepareIfChanged(SampleRate > 0f ? SampleRate : preparedRate, value);
        }
    }

    /// <summary>After the host turns the plugin on: the sequencer is looked up again and the transport read on the first block.</summary>
    public void Resumed()
    {
        sequencerLooked = false;
        timeInfoBroken = false;
        blocksUntilTimeInfo = 0;
    }

    /// <summary>
    /// The host's MIDI for the coming block, on the host's thread just before <see cref="Process(VstAudioBuffer[], VstAudioBuffer[])"/>.
    /// Events are kept with their frame offsets; a batch no block consumed is applied first.
    /// </summary>
    public void Process(VstEventCollection events)
    {
        if (pendingCount > 0)
            FlushPending();

        for (var i = 0; i < events.Count && pendingCount < MaxPendingEvents; i++)
        {
            if (events[i] is not VstMidiEvent midi || midi.Data is null || midi.Data.Length == 0)
                continue;

            var data = midi.Data;
            pending[pendingCount++] = new PendingEvent(midi.DeltaFrames < 0 ? 0 : midi.DeltaFrames, data[0], data.Length > 1 ? data[1] : (byte)0, data.Length > 2 ? data[2] : (byte)0);
        }
    }

    public override void Process(VstAudioBuffer[] inChannels, VstAudioBuffer[] outChannels)
    {
        if (outChannels.Length == 0)
            return;

        var outL = outChannels[0];
        var outR = outChannels.Length > 1 ? outChannels[1] : outChannels[0];
        var count = outL.SampleCount;
        if (count <= 0)
            return;

        EnsureCapacity(count);
        if (Bypass)
        {
            PassThrough(inChannels, outL, outR, count);
            pendingCount = 0;
            return;
        }

        var wantsDouble = engine.Parameters.GetInt(ParamId.EngineRate) == 1;
        if (wantsDouble != (engine.Oversampling == 2))
            engine.Prepare(preparedRate, preparedFrames);

        parameters.PullFromHost();
        if (--blocksUntilTimeInfo <= 0)
        {
            blocksUntilTimeInfo = TimeInfoEveryBlocks;
            ReadTimeInfo();
        }

        if (InputCount > 0)
            CopyInput(inChannels, count);

        var rendered = 0;
        for (var e = 0; e < pendingCount; e++)
        {
            var at = pending[e].Delta;
            if (at > count)
                at = count;

            if (at > rendered)
            {
                engine.Process(inputLeft, inputRight, outputLeft, outputRight, rendered, at - rendered);
                rendered = at;
            }

            Dispatch(pending[e]);
        }

        pendingCount = 0;
        if (rendered < count)
            engine.Process(inputLeft, inputRight, outputLeft, outputRight, rendered, count - rendered);

        outputLeft.AsSpan(0, count).CopyTo(outL.AsSpan());
        if (!ReferenceEquals(outR, outL))
            outputRight.AsSpan(0, count).CopyTo(outR.AsSpan());
    }

    private void CopyInput(VstAudioBuffer[] inChannels, int count)
    {
        if (inChannels.Length == 0 || inChannels[0].SampleCount < count)
        {
            Array.Clear(inputLeft, 0, count);
            Array.Clear(inputRight, 0, count);
            return;
        }

        inChannels[0].AsReadOnlySpan()[..count].CopyTo(inputLeft);
        var right = inChannels.Length > 1 && inChannels[1].SampleCount >= count ? inChannels[1] : inChannels[0];
        right.AsReadOnlySpan()[..count].CopyTo(inputRight);
    }

    private static void PassThrough(VstAudioBuffer[] inChannels, VstAudioBuffer outL, VstAudioBuffer outR, int count)
    {
        if (PluginIdentity.IsEffect && inChannels.Length > 0 && inChannels[0].SampleCount >= count)
        {
            var inL = inChannels[0];
            var inR = inChannels.Length > 1 && inChannels[1].SampleCount >= count ? inChannels[1] : inChannels[0];
            if (!ReferenceEquals(inL, outL))
                inL.AsReadOnlySpan()[..count].CopyTo(outL.AsSpan());

            if (!ReferenceEquals(inR, outR))
                inR.AsReadOnlySpan()[..count].CopyTo(outR.AsSpan());

            return;
        }

        outL.AsSpan()[..count].Clear();
        if (!ReferenceEquals(outR, outL))
            outR.AsSpan()[..count].Clear();
    }

    private void Dispatch(in PendingEvent e)
    {
        var midi = engine.Midi;
        switch (e.Status & 0xF0)
        {
            case 0x90:
                midi.NoteOn(e.Data1, e.Data2);
                break;
            case 0x80:
                midi.NoteOff(e.Data1);
                break;
            case 0xB0:
                midi.ControlChange(e.Data1, e.Data2);
                break;
            case 0xD0:
                midi.ChannelAftertouch(e.Data1);
                break;
            case 0xE0:
                midi.PitchBend(e.Data1 | (e.Data2 << 7));
                break;
        }
    }

    private void FlushPending()
    {
        for (var e = 0; e < pendingCount; e++)
            Dispatch(pending[e]);

        pendingCount = 0;
    }

    private void ReadTimeInfo()
    {
        if (timeInfoBroken)
            return;

        if (!sequencerLooked)
        {
            sequencerLooked = true;
            sequencer = session.Host?.GetInstance<IVstHostSequencer>();
        }

        if (sequencer is null)
            return;

        // GetTime allocates one VstTimeInfo, which is why this is read every few blocks and not
        // every one. A host that does not supply time info returns null — VST.NET's interop then
        // dereferences it and throws, rather than returning null — so a failure here means the
        // host has no transport: the lanes fall back to their free clock and it is never retried.
        VstTimeInfo? time;
        try
        {
            time = sequencer.GetTime(VstTimeInfoFlags.TempoValid);
        }
        catch (Exception ex) when (ex is NullReferenceException or NotSupportedException or InvalidOperationException)
        {
            timeInfoBroken = true;
            return;
        }

        if (time is null)
            return;

        engine.Clock.HostTempo = (time.Flags & VstTimeInfoFlags.TempoValid) != 0 ? time.Tempo : 0;
        engine.Clock.HostPlaying = (time.Flags & VstTimeInfoFlags.TransportPlaying) != 0;
    }

    private void PrepareIfChanged(double rate, int frames)
    {
        var maxFrames = Math.Max(frames, InitialFrames);
        if (Math.Abs(rate - preparedRate) < 0.5 && maxFrames <= preparedFrames)
            return;

        preparedRate = rate;
        preparedFrames = Math.Max(maxFrames, preparedFrames);
        EnsureCapacity(preparedFrames);
        engine.Prepare(rate, preparedFrames);
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
