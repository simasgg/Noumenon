using NAudio.Midi;
using Noumenon.Engine.Modulation;

namespace Noumenon.Standalone.Audio;

/// <summary>
/// One MIDI input port routed into the engine's queue: notes, bend, controllers and channel
/// aftertouch, translated from NAudio's event objects on winmm's callback thread (the queue's
/// producer lock is short and never taken by the audio thread). Closing the port releases every
/// note so nothing stays stuck in the gate envelope. The last few values are kept for the monitor.
/// </summary>
internal sealed class MidiHost : IDisposable
{
    private readonly MidiInput midi;
    private MidiIn? port;
    private long messages;
    private int lastNote = -1;
    private int lastVelocity;
    private int lastController = -1;
    private int lastControllerValue;
    private string? error;

    public MidiHost(MidiInput midi) => this.midi = midi;

    public string? DeviceName { get; private set; }

    public bool IsOpen => port is not null;

    public long Messages => Volatile.Read(ref messages);

    public int LastNote => Volatile.Read(ref lastNote);

    public int LastVelocity => Volatile.Read(ref lastVelocity);

    public int LastController => Volatile.Read(ref lastController);

    public int LastControllerValue => Volatile.Read(ref lastControllerValue);

    public string? Error => Volatile.Read(ref error);

    public bool Open(string? productName)
    {
        Close();
        Volatile.Write(ref error, null);
        if (productName is null)
            return true;

        var index = AudioDevices.MidiIndex(productName);
        if (index < 0)
        {
            Volatile.Write(ref error, $"MIDI input '{productName}' is not connected.");

            return false;
        }

        try
        {
            port = new MidiIn(index);
            port.MessageReceived += OnMessage;
            port.ErrorReceived += OnError;
            port.Start();
            DeviceName = productName;

            return true;
        }
        catch (Exception ex) when (AudioDevices.IsDeviceFailure(ex))
        {
            Volatile.Write(ref error, ex.Message);
            port?.Dispose();
            port = null;

            return false;
        }
    }

    public void Close()
    {
        if (port is null)
            return;

        port.MessageReceived -= OnMessage;
        port.ErrorReceived -= OnError;
        try
        {
            port.Stop();
            port.Dispose();
        }
        catch (Exception ex) when (AudioDevices.IsDeviceFailure(ex))
        {
        }

        port = null;
        DeviceName = null;
        midi.AllNotesOff();
    }

    public void Dispose() => Close();

    /// <summary>Feeds one NAudio event to the engine; returns false for kinds the engine ignores.</summary>
    public static bool Route(MidiEvent midiEvent, MidiInput midi)
    {
        switch (midiEvent)
        {
            case NoteOnEvent { Velocity: > 0 } on:
                midi.NoteOn(on.NoteNumber, on.Velocity);
                return true;
            case NoteEvent off when off.CommandCode == MidiCommandCode.NoteOff || off is NoteOnEvent:
                midi.NoteOff(off.NoteNumber);
                return true;
            case PitchWheelChangeEvent bend:
                midi.PitchBend(bend.Pitch);
                return true;
            case ControlChangeEvent cc:
                midi.ControlChange((int)cc.Controller, cc.ControllerValue);
                return true;
            case ChannelAfterTouchEvent pressure:
                midi.ChannelAftertouch(pressure.AfterTouchPressure);
                return true;
            default:
                return false;
        }
    }

    private void OnMessage(object? sender, MidiInMessageEventArgs e)
    {
        MidiEvent? midiEvent;
        try
        {
            midiEvent = e.MidiEvent;
        }
        catch (FormatException)
        {
            return;
        }

        if (midiEvent is null || !Route(midiEvent, midi))
            return;

        Interlocked.Increment(ref messages);
        switch (midiEvent)
        {
            case NoteOnEvent on:
                Volatile.Write(ref lastNote, on.NoteNumber);
                Volatile.Write(ref lastVelocity, on.Velocity);
                break;
            case ControlChangeEvent cc:
                Volatile.Write(ref lastController, (int)cc.Controller);
                Volatile.Write(ref lastControllerValue, cc.ControllerValue);
                break;
        }
    }

    private void OnError(object? sender, MidiInMessageEventArgs e) => Volatile.Write(ref error, $"MIDI error 0x{e.RawMessage:X}");
}
