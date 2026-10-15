namespace Noumenon.Engine.Scales;

/// <summary>A generated chord's shape, as scale steps stacked on its degree; the order is the parameter table's list.</summary>
public enum ChordType
{
    Triad,
    Seventh,
    Ninth,
    Sus2,
    Sus4,
    Add9,
    Quartal,
    Fifths,
    Cluster,
}

/// <summary>How the six voices spread a generated chord; the order is the parameter table's list.</summary>
public enum ChordVoicing
{
    /// <summary>The chord's tones in order, upward from the root, repeating an octave up.</summary>
    Close,

    /// <summary>A root an octave below, then every other tone of the close sequence.</summary>
    Open,

    /// <summary>Root and fifth-place tone an octave below, then every other tone from two places up: two and a half octaves wide.</summary>
    Wide,
}

/// <summary>
/// The scale-aware chord generator behind the chord sequencer and the panel's "fill user chords":
/// six Resochord notes for a chord built on a degree of the active scale. Tones are stacked in
/// scale steps — a triad is degrees 1-3-5 of whatever scale is active, so a major scale gives
/// major and minor triads where they belong, a pentatonic one its own open chords, a Scala scale
/// its own harmony — and the voicing spreads them over the six voices. 12-TET has no degrees of
/// its own, so it borrows the major scale; the root note sets the key. Notes come back as MIDI
/// note numbers (fractional for just and Scala scales). Allocation-free, so the sequencer calls it
/// on the audio thread.
/// </summary>
public static class ChordGenerator
{
    public const int VoiceCount = 6;

    private static readonly Scale Major = Scale.BuiltIn[1];

    // Each type's tones in scale steps above its degree.
    private static readonly int[][] Tones =
    [
        [0, 2, 4],
        [0, 2, 4, 6],
        [0, 2, 4, 6, 8],
        [0, 1, 4],
        [0, 3, 4],
        [0, 2, 4, 8],
        [0, 3, 6, 9],
        [0, 4, 8],
        [0, 1, 2, 3, 4, 5],
    ];

    // Each voicing as positions in the endless close sequence of the chord's tones; -1 marks a voice one octave (period) down.
    private static readonly int[][] Voicings =
    [
        [0, 1, 2, 3, 4, 5],
        [-1, 0, 2, 4, 6, 8],
        [-1, -3, 4, 6, 8, 10],
    ];

    /// <summary>
    /// Writes six notes into <paramref name="notes"/>: the chord on <paramref name="degree"/> (0 = the
    /// root's degree, wrapping into the next period beyond the scale's size) of <paramref name="scale"/>
    /// rooted at pitch class <paramref name="root"/> (0 = C), with its root in <paramref name="octave"/>
    /// (MIDI octave numbering: 4 is the octave of middle C).
    /// </summary>
    public static void Generate(Scale scale, int root, int degree, ChordType type, ChordVoicing voicing, int octave, Span<float> notes)
    {
        var source = scale.IsIdentity ? Major : scale;
        var degrees = source.DegreesCents;
        var count = degrees.Count;
        var period = source.PeriodCents;
        var tones = Tones[Math.Clamp((int)type, 0, Tones.Length - 1)];
        var positions = Voicings[Math.Clamp((int)voicing, 0, Voicings.Length - 1)];
        var baseNote = (octave + 1) * 12 + ((root % 12) + 12) % 12;

        for (var v = 0; v < VoiceCount && v < notes.Length; v++)
        {
            var position = positions[v];
            var down = 0;
            if (position == -1)
            {
                position = 0;
                down = 1;
            }
            else if (position == -3)
            {
                position = Math.Min(2, tones.Length - 1);
                down = 1;
            }

            // The close sequence: tone i of the chord, raised by a period every time the tones run out.
            var steps = tones[position % tones.Length] + position / tones.Length * count + degree;
            var periods = FloorDiv(steps, count) - down;
            var within = steps - FloorDiv(steps, count) * count;
            var cents = degrees[within] + periods * period;
            notes[v] = baseNote + cents / 100f;
        }
    }

    /// <summary>The root of the chord on <paramref name="degree"/> as a MIDI note, with the same conventions as <see cref="Generate"/>.</summary>
    public static float RootNote(Scale scale, int root, int degree, int octave)
    {
        var source = scale.IsIdentity ? Major : scale;
        var count = source.DegreesCents.Count;
        var periods = FloorDiv(degree, count);
        var within = degree - periods * count;

        return (octave + 1) * 12 + ((root % 12) + 12) % 12 + (source.DegreesCents[within] + periods * source.PeriodCents) / 100f;
    }

    private static int FloorDiv(int a, int b) => a >= 0 ? a / b : -((-a + b - 1) / b);
}
