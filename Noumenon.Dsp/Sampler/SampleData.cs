using Noumenon.Dsp.Oscillators;

namespace Noumenon.Dsp.Sampler;

/// <summary>
/// A decoded sample: float frames at the file's own rate, one array per channel (a mono file shares
/// one array for both). Immutable once built, so the loader can hand a finished instance to the
/// audio thread with a single reference write and the player never sees a half-filled buffer.
/// Samples are not resampled to the engine rate — the player reads them at
/// <see cref="SampleRate"/> / engine rate, so a host rate change costs nothing. The oscillators'
/// <see cref="Oscillators.Wavetable"/> made from it is derived data, built once by
/// <see cref="BuildWavetable"/> on a loading thread and published like the sample itself.
/// </summary>
public sealed class SampleData
{
    private Wavetable? wavetable;

    public SampleData(string name, int sampleRate, float[] left, float[]? right = null)
    {
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate));

        if (right is not null && right.Length != left.Length)
            throw new ArgumentException("Left and right channels must have the same length.", nameof(right));

        Name = name;
        SampleRate = sampleRate;
        Left = left;
        Right = right ?? left;
    }

    public string Name { get; }
    public int SampleRate { get; }
    public float[] Left { get; }
    public float[] Right { get; }
    public int Length => Left.Length;
    public bool IsStereo => !ReferenceEquals(Left, Right);
    public double Seconds => Length / (double)SampleRate;

    /// <summary>The wavetable once <see cref="BuildWavetable"/> has run, else null; safe from the audio thread.</summary>
    public Wavetable? Wavetable => Volatile.Read(ref wavetable);

    /// <summary>Builds the wavetable the first time (allocates and runs FFTs: never on the audio thread) and returns it.</summary>
    public Wavetable BuildWavetable()
    {
        var built = Volatile.Read(ref wavetable);
        if (built is not null)
            return built;

        built = Oscillators.Wavetable.FromSample(this);

        return Interlocked.CompareExchange(ref wavetable, built, null) ?? built;
    }
}
