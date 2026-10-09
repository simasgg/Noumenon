using Noumenon.Engine.Modulation;

namespace Noumenon.Standalone.Input;

/// <summary>
/// The computer keyboard as a MIDI keyboard for when no port is connected: Z–M is one octave
/// with its sharps on the row above (S, D, G, H, J), Q–P the next octave up with the digits as
/// its sharps; minus and equals shift the base note an octave. Held keys are tracked so
/// autorepeat never retriggers a gate, and <see cref="ReleaseAll"/> clears them when the window
/// loses focus so no note can stick.
/// </summary>
internal sealed class KeyboardMidi
{
    private const int Velocity = 100;
    private const int LowestBase = 24;
    private const int HighestBase = 96;

    private static readonly Dictionary<Keys, int> Offsets = new()
    {
        [Keys.Z] = 0, [Keys.S] = 1, [Keys.X] = 2, [Keys.D] = 3, [Keys.C] = 4, [Keys.V] = 5, [Keys.G] = 6, [Keys.B] = 7, [Keys.H] = 8, [Keys.N] = 9, [Keys.J] = 10, [Keys.M] = 11,
        [Keys.Oemcomma] = 12, [Keys.L] = 13, [Keys.OemPeriod] = 14,
        [Keys.Q] = 12, [Keys.D2] = 13, [Keys.W] = 14, [Keys.D3] = 15, [Keys.E] = 16, [Keys.R] = 17, [Keys.D5] = 18, [Keys.T] = 19, [Keys.D6] = 20, [Keys.Y] = 21, [Keys.D7] = 22, [Keys.U] = 23,
        [Keys.I] = 24, [Keys.D9] = 25, [Keys.O] = 26, [Keys.D0] = 27, [Keys.P] = 28,
    };

    private readonly MidiInput midi;
    private readonly Dictionary<Keys, int> held = new();

    public KeyboardMidi(MidiInput midi) => this.midi = midi;

    /// <summary>The MIDI note Z plays; 48 is C3.</summary>
    public int BaseNote { get; private set; } = 48;

    public int HeldCount => held.Count;

    public bool KeyDown(Keys key)
    {
        switch (key)
        {
            case Keys.OemMinus:
                ReleaseAll();
                BaseNote = Math.Max(LowestBase, BaseNote - 12);
                return true;
            case Keys.Oemplus:
                ReleaseAll();
                BaseNote = Math.Min(HighestBase, BaseNote + 12);
                return true;
        }

        if (!Offsets.TryGetValue(key, out var offset))
            return false;

        if (held.ContainsKey(key))
            return true;

        var note = Math.Clamp(BaseNote + offset, 0, 127);
        held[key] = note;
        midi.NoteOn(note, Velocity);

        return true;
    }

    public bool KeyUp(Keys key)
    {
        if (held.Remove(key, out var note))
        {
            midi.NoteOff(note);

            return true;
        }

        return Offsets.ContainsKey(key) || key is Keys.OemMinus or Keys.Oemplus;
    }

    public void ReleaseAll()
    {
        foreach (var note in held.Values)
            midi.NoteOff(note);

        held.Clear();
    }
}
