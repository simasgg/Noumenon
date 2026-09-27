namespace Noumenon.Dsp.Shared;

/// <summary>
/// Pitch arithmetic shared by the oscillators, the sampler and the Resochord. An oscillator at
/// semitone 0 with no P routing plays middle C (MIDI 60); a routed P fader adds its value in
/// semitones, or in cents when the row's FINE button is on — the manual never explains FINE, so
/// that is the working assumption (see the plan's "still undocumented" list).
/// </summary>
public static class Tuning
{
    public const int MiddleC = 60;
    public const int A4 = 69;
    public const float DefaultA4Hz = 440f;

    public static float NoteToHz(float midiNote, float a4Hz = DefaultA4Hz) => a4Hz * MathF.Pow(2f, (midiNote - A4) / 12f);

    public static float HzToNote(float hz, float a4Hz = DefaultA4Hz) => A4 + 12f * MathF.Log2(hz / a4Hz);

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
