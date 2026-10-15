using System.Numerics;

namespace Noumenon.Engine.Modulation;

public enum MidiKind : byte
{
    NoteOn,
    NoteOff,
    PitchBend,
    ControlChange,
    Aftertouch,
}

public readonly record struct MidiMessage(MidiKind Kind, byte Data1, byte Data2);

/// <summary>
/// The engine's MIDI input: producers (a MIDI port thread, the computer keyboard, a host's event
/// list, the harness) enqueue messages behind a short lock; the audio thread drains the ring at
/// its control tick without locking and keeps the tracked state the rest of the engine reads —
/// last note and velocity (for the P faders), held notes (the gate envelope), pitch bend, the
/// mod wheel, channel aftertouch, every controller, and the round-robin note assignment
/// Fabrications' ResoMidi gives the six Resochord voices. Notes are mono, last-note priority.
/// </summary>
public sealed class MidiInput
{
    public const int ResoVoiceCount = 6;
    public const int ModWheelController = 1;

    private const int Capacity = 256;
    private const int Mask = Capacity - 1;

    private readonly MidiMessage[] ring = new MidiMessage[Capacity];
    private readonly Lock producerLock = new();
    private int head;
    private int tail;

    private readonly float[] controllers = new float[128];
    private readonly float[] voiceNotes = new float[ResoVoiceCount];
    private ulong heldLow;
    private ulong heldHigh;
    private ulong arrivedLow;
    private ulong arrivedHigh;
    private int lastNote = -1;
    private float lastVelocity;
    private float bend;
    private float aftertouch;
    private int nextVoice;

    public MidiInput()
    {
        Array.Fill(voiceNotes, float.NaN);
    }

    public int LastNote => Volatile.Read(ref lastNote);

    public float LastVelocity => Volatile.Read(ref lastVelocity);

    public int HeldCount => BitOperations.PopCount(Volatile.Read(ref heldLow)) + BitOperations.PopCount(Volatile.Read(ref heldHigh));

    /// <summary>Pitch bend as −1..1.</summary>
    public float Bend => Volatile.Read(ref bend);

    public float Wheel => Volatile.Read(ref controllers[ModWheelController]);

    public float Aftertouch => Volatile.Read(ref aftertouch);

    public float Controller(int number) => (uint)number < 128 ? Volatile.Read(ref controllers[number]) : 0f;

    /// <summary>The note ResoMidi last assigned to a Resochord voice, or NaN if that voice has not been played.</summary>
    public float VoiceNote(int voice) => Volatile.Read(ref voiceNotes[voice]);

    /// <summary>Audio thread: the controllers 0–63 that sent a value in the last drain, one bit each (the MIDI map reads them).</summary>
    internal ulong ArrivedLow => arrivedLow;

    /// <summary>Controllers 64–127, as <see cref="ArrivedLow"/>.</summary>
    internal ulong ArrivedHigh => arrivedHigh;

    public void NoteOn(int note, int velocity)
    {
        if (velocity <= 0)
        {
            NoteOff(note);
            return;
        }

        Enqueue(new MidiMessage(MidiKind.NoteOn, (byte)Math.Clamp(note, 0, 127), (byte)Math.Clamp(velocity, 1, 127)));
    }

    public void NoteOff(int note) => Enqueue(new MidiMessage(MidiKind.NoteOff, (byte)Math.Clamp(note, 0, 127), 0));

    /// <summary>14-bit pitch bend, 0..16383 with 8192 at centre.</summary>
    public void PitchBend(int value)
    {
        var v = Math.Clamp(value, 0, 16383);
        Enqueue(new MidiMessage(MidiKind.PitchBend, (byte)(v & 0x7F), (byte)(v >> 7)));
    }

    public void ControlChange(int controller, int value) => Enqueue(new MidiMessage(MidiKind.ControlChange, (byte)Math.Clamp(controller, 0, 127), (byte)Math.Clamp(value, 0, 127)));

    public void ChannelAftertouch(int value) => Enqueue(new MidiMessage(MidiKind.Aftertouch, (byte)Math.Clamp(value, 0, 127), 0));

    public void AllNotesOff()
    {
        for (var note = 0; note < 128; note++)
            NoteOff(note);
    }

    /// <summary>Forgets the ResoMidi voice assignments (a new chord takes over all six voices again).</summary>
    public void ClearVoiceNotes()
    {
        for (var i = 0; i < ResoVoiceCount; i++)
            Volatile.Write(ref voiceNotes[i], float.NaN);

        nextVoice = 0;
    }

    /// <summary>Audio thread: applies every queued message; returns true if a note-on (a gate) arrived.</summary>
    internal bool Drain()
    {
        var gate = false;
        arrivedLow = 0UL;
        arrivedHigh = 0UL;
        while (TryDequeue(out var message))
        {
            switch (message.Kind)
            {
                case MidiKind.NoteOn:
                    SetHeld(message.Data1, true);
                    Volatile.Write(ref lastNote, message.Data1);
                    Volatile.Write(ref lastVelocity, message.Data2 / 127f);
                    Volatile.Write(ref voiceNotes[nextVoice], message.Data1);
                    nextVoice = (nextVoice + 1) % ResoVoiceCount;
                    gate = true;
                    break;
                case MidiKind.NoteOff:
                    SetHeld(message.Data1, false);
                    break;
                case MidiKind.PitchBend:
                    Volatile.Write(ref bend, ((message.Data1 | (message.Data2 << 7)) - 8192) / 8192f);
                    break;
                case MidiKind.ControlChange:
                    Volatile.Write(ref controllers[message.Data1], message.Data2 / 127f);
                    if (message.Data1 < 64)
                        arrivedLow |= 1UL << message.Data1;
                    else
                        arrivedHigh |= 1UL << (message.Data1 - 64);

                    break;
                case MidiKind.Aftertouch:
                    Volatile.Write(ref aftertouch, message.Data1 / 127f);
                    break;
            }
        }

        return gate;
    }

    private void Enqueue(MidiMessage message)
    {
        lock (producerLock)
        {
            var t = tail;
            var next = (t + 1) & Mask;
            if (next == Volatile.Read(ref head))
                return;   // full: the message is dropped rather than blocking a MIDI thread

            ring[t] = message;
            Volatile.Write(ref tail, next);
        }
    }

    private bool TryDequeue(out MidiMessage message)
    {
        var h = head;
        if (h == Volatile.Read(ref tail))
        {
            message = default;
            return false;
        }

        message = ring[h];
        Volatile.Write(ref head, (h + 1) & Mask);

        return true;
    }

    private void SetHeld(int note, bool on)
    {
        if (note < 64)
        {
            var bit = 1UL << note;
            Volatile.Write(ref heldLow, on ? heldLow | bit : heldLow & ~bit);
        }
        else
        {
            var bit = 1UL << (note - 64);
            Volatile.Write(ref heldHigh, on ? heldHigh | bit : heldHigh & ~bit);
        }
    }
}
