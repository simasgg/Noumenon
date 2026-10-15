namespace Noumenon.Engine.Scales;

/// <summary>
/// A scale as a sorted list of degrees in cents above the root within one period (normally the
/// 1200-cent octave), used to quantize oscillator semitones, the P faders' MIDI notes and the
/// Resochord's notes. 12-TET is the identity and skips quantization; just and Scala scales carry
/// fractional degrees. Immutable, so the audio thread can hold a reference safely, and
/// <see cref="Quantize"/> walks a plain array so it allocates nothing there.
/// </summary>
public sealed class Scale
{
    private readonly float[] degrees;

    public Scale(string name, IEnumerable<float> degreesCents, float periodCents = 1200f)
    {
        if (periodCents <= 0f)
            throw new ArgumentOutOfRangeException(nameof(periodCents));

        var sorted = degreesCents.Where(c => c >= 0f && c < periodCents).Distinct().OrderBy(c => c).ToArray();
        if (sorted.Length == 0 || sorted[0] != 0f)
            sorted = [0f, ..sorted.Where(c => c != 0f)];

        degrees = sorted;
        Name = name;
        DegreesCents = sorted;
        PeriodCents = periodCents;
        IsIdentity = periodCents == 1200f && sorted.Length == 12 && sorted.Select((c, i) => MathF.Abs(c - 100f * i) < 0.01f).All(x => x);
    }

    public string Name { get; }

    public IReadOnlyList<float> DegreesCents { get; }

    public float PeriodCents { get; }

    /// <summary>True for 12-TET: every semitone is in the scale, so nothing needs quantizing.</summary>
    public bool IsIdentity { get; }

    public static Scale Equal12 { get; } = new("12-TET", Enumerable.Range(0, 12).Select(i => 100f * i));

    /// <summary>The nearest scale pitch to <paramref name="semitones"/> (from middle C), for a scale rooted at <paramref name="root"/> (0 = C).</summary>
    public float Quantize(float semitones, int root)
    {
        if (IsIdentity)
            return semitones;

        var period = PeriodCents / 100f;
        var relative = semitones - root;
        var octave = MathF.Floor(relative / period);
        var withinCents = (relative - octave * period) * 100f;

        var best = 0f;
        var bestDistance = float.MaxValue;
        for (var i = 0; i < degrees.Length; i++)
        {
            var distance = MathF.Abs(degrees[i] - withinCents);
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = degrees[i];
            }
        }

        if (PeriodCents - withinCents < bestDistance)
            best = PeriodCents;

        return root + octave * period + best / 100f;
    }

    /// <summary>The built-in scales in the order of the parameter table's list, minus "User".</summary>
    public static IReadOnlyList<Scale> BuiltIn { get; } =
    [
        Equal12,
        new("Major", [0f, 200f, 400f, 500f, 700f, 900f, 1100f]),
        new("Minor", [0f, 200f, 300f, 500f, 700f, 800f, 1000f]),
        new("Pentatonic Major", [0f, 200f, 400f, 700f, 900f]),
        new("Pentatonic Minor", [0f, 300f, 500f, 700f, 1000f]),
        new("Just Major", [0f, 203.91f, 386.31f, 498.04f, 701.96f, 884.36f, 1088.27f]),
        new("Just Minor", [0f, 203.91f, 315.64f, 498.04f, 701.96f, 813.69f, 1017.6f]),
        new("Whole Tone", [0f, 200f, 400f, 600f, 800f, 1000f]),
    ];
}
