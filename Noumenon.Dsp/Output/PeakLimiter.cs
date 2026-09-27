using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Output;

/// <summary>
/// A stereo-linked safety limiter with instantaneous attack and an exponential release: the gain
/// is computed from a peak envelope that is never below the current sample, so the output can never
/// exceed <see cref="Ceiling"/>. It is the last thing in the chain — a guard against resonance,
/// ring-mod and post-gain overs, not a mastering tool; the Fabrications two-band limiter in the
/// post section does the musical work in front of it.
/// </summary>
public sealed class PeakLimiter
{
    private float envelope;
    private float releaseCoeff;

    public float Ceiling { get; set; } = 0.98f;

    public void Prepare(double sampleRate, float releaseSeconds) => releaseCoeff = MathF.Exp(-1f / (releaseSeconds * (float)sampleRate));

    public void Clear() => envelope = 0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(ref float left, ref float right)
    {
        var peak = MathF.Max(MathF.Abs(left), MathF.Abs(right));
        envelope = peak > envelope ? peak : DspHelper.Undenormalize(envelope * releaseCoeff);
        if (envelope > Ceiling)
        {
            var gain = Ceiling / envelope;
            left *= gain;
            right *= gain;
        }
    }
}
