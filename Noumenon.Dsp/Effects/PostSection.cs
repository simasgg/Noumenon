using System.Runtime.CompilerServices;
using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Effects;

/// <summary>
/// Fabrications' post section, after MF's Volume and Width: the same filter and EQ again, the
/// two-band limiter and a post gain — for taming the output without changing what feeds the
/// Resochord. Each stage is its own block with its own idle rule.
/// </summary>
public sealed class PostSection
{
    private const float SmoothSeconds = 0.02f;

    private readonly Smoother gain = new();

    public PostSection() => gain.Snap(1f);

    public MorphFilter Filter { get; } = new();

    public ShelfEq Eq { get; } = new();

    public BandLimiter Limiter { get; } = new();

    public void SetVolumeDb(float db) => gain.Target = DspHelper.DbToLinear(db);

    public void Prepare(double sampleRate)
    {
        Filter.Prepare(sampleRate);
        Eq.Prepare(sampleRate);
        Limiter.Prepare(sampleRate);
        gain.SetTime(sampleRate, SmoothSeconds);
    }

    public void Reset()
    {
        Filter.Reset();
        Eq.Reset();
        Limiter.Reset();
        gain.Snap();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(ref float l, ref float r)
    {
        Filter.Process(ref l, ref r);
        Eq.Process(ref l, ref r);
        Limiter.Process(ref l, ref r);
        var g = gain.Next();
        l *= g;
        r *= g;
    }
}
