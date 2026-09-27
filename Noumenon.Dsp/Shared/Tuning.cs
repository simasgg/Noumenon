namespace Noumenon.Dsp.Shared;

/// <summary>Pitch arithmetic shared by the oscillators, the sampler and the Resochord.</summary>
public static class Tuning
{
    /// <summary>MIDI note number of middle C; an oscillator at semitone 0 with no P routing plays it.</summary>
    public const int MiddleC = 60;

    public const int A4 = 69;
    public const float DefaultA4Hz = 440f;

    public static float NoteToHz(float midiNote, float a4Hz = DefaultA4Hz) => a4Hz * MathF.Pow(2f, (midiNote - A4) / 12f);

    public static float HzToNote(float hz, float a4Hz = DefaultA4Hz) => A4 + 12f * MathF.Log2(hz / a4Hz);

    /// <summary>
    /// An oscillator's frequency: middle C offset by its semitone and fine knobs, plus whatever P1
    /// and P2 contribute through the routing rows. A routed P fader adds its value in semitones, or
    /// in cents when the row's FINE button is on — the manual never explains FINE, so that is the
    /// working assumption (see the plan's "still undocumented" list).
    /// </summary>
    public static float OscillatorHz(float semitone, float fineCents, float p1, float p2, bool route1, bool route2, bool fineRange1, bool fineRange2, float a4Hz)
    {
        var note = MiddleC + semitone + fineCents / 100f;
        if (route1)
            note += fineRange1 ? p1 / 100f : p1;

        if (route2)
            note += fineRange2 ? p2 / 100f : p2;

        return NoteToHz(note, a4Hz);
    }
}
