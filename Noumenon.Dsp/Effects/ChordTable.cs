namespace Noumenon.Dsp.Effects;

/// <summary>One Resochord chord: six MIDI notes (fractional for just-intonation voicings).</summary>
public sealed record Chord(string Name, float[] Notes);

/// <summary>
/// The Resochord's chord presets — our own list, not MF's names. Index 0 ("Voices") means the six
/// voice Note parameters as set; the rest are voicings around C3–C5 that the Chord parameter, the
/// P1/P2 chord-select input and later the MIDI layer pick from. Append only: the index is the
/// parameter value.
/// </summary>
public static class ChordTable
{
    public const int VoiceCount = 6;

    public static readonly IReadOnlyList<Chord> All =
    [
        new("Voices", [48f, 55f, 60f, 64f, 67f, 72f]),
        new("Major", [48f, 52f, 55f, 60f, 64f, 67f]),
        new("Minor", [48f, 51f, 55f, 60f, 63f, 67f]),
        new("Sus2", [48f, 50f, 55f, 60f, 62f, 67f]),
        new("Sus4", [48f, 53f, 55f, 60f, 65f, 67f]),
        new("Fifths", [36f, 43f, 48f, 55f, 60f, 67f]),
        new("Octaves", [36f, 48f, 60f, 72f, 84f, 96f]),
        new("Major 7", [48f, 52f, 55f, 59f, 64f, 67f]),
        new("Minor 7", [48f, 51f, 55f, 58f, 63f, 67f]),
        new("Dominant 7", [48f, 52f, 55f, 58f, 64f, 70f]),
        new("Major 9", [48f, 52f, 55f, 59f, 62f, 67f]),
        new("Minor 9", [48f, 51f, 55f, 58f, 62f, 67f]),
        new("Add 9", [48f, 52f, 55f, 62f, 64f, 67f]),
        new("Quartal", [48f, 53f, 58f, 63f, 68f, 73f]),
        new("Cluster", [60f, 61f, 62f, 63f, 64f, 65f]),
        new("Whole Tone", [48f, 50f, 52f, 54f, 56f, 58f]),
        new("Pentatonic", [48f, 50f, 52f, 55f, 57f, 60f]),
        new("Lydian", [48f, 52f, 54f, 55f, 59f, 64f]),
        new("Dorian", [48f, 51f, 55f, 57f, 62f, 63f]),
        new("Diminished", [48f, 51f, 54f, 57f, 60f, 63f]),
        new("Augmented", [48f, 52f, 56f, 60f, 64f, 68f]),
        new("Deep Major", [36f, 43f, 48f, 52f, 55f, 60f]),
        new("Deep Minor", [36f, 43f, 48f, 51f, 55f, 60f]),
        new("Wide", [36f, 48f, 55f, 64f, 71f, 79f]),
        new("Bell", [48f, 55f, 62f, 66f, 71f, 74f]),
        new("Just Major", [48f, 51.86f, 55.02f, 60f, 63.86f, 67.02f]),
        new("Just Minor", [48f, 51.16f, 55.02f, 60f, 63.16f, 67.02f]),
    ];

    public static readonly IReadOnlyList<string> Names = All.Select(c => c.Name).ToArray();

    public static int Count => All.Count;

    /// <summary>
    /// Fabrications' chord-select mapping for a P fader: C#3 and up (P = −11 …) pick chords 1, 2, …;
    /// C3 and below mean the voices as set.
    /// </summary>
    public static int IndexFromPitch(float semitonesFromMiddleC)
    {
        var index = (int)MathF.Round(semitonesFromMiddleC) + 12;

        return Math.Clamp(index, 0, All.Count - 1);
    }
}
