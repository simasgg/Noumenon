using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Mix;

/// <summary>
/// MF's section combine: the two filtered sections are both summed and ring-modulated, and the
/// central fader crossfades between the two paths. The A+B balance knob tilts the summed path
/// towards A (−1) or B (+1) by attenuating the other; the A×B knob scales the ring-mod path, which
/// also gets a fixed <see cref="RingMakeup"/> because a product of two signals well below full
/// scale is much quieter than their sum. All four controls glide per sample.
/// </summary>
public sealed class Combiner
{
    public const float RingMakeup = 2f;

    private const float SmoothSeconds = 0.02f;

    private readonly Smoother crossfade = new();
    private readonly Smoother gainA = new();
    private readonly Smoother gainB = new();
    private readonly Smoother ring = new();

    public Combiner()
    {
        crossfade.Snap(0.5f);
        gainA.Snap(1f);
        gainB.Snap(1f);
        ring.Snap(RingMakeup);
    }

    public void SetCrossfade(float value) => crossfade.Target = DspHelper.Clamp01(value);

    public void SetBalance(float balance)
    {
        var b = DspHelper.Clamp(balance, -1f, 1f);
        gainA.Target = b > 0f ? 1f - b : 1f;
        gainB.Target = b < 0f ? 1f + b : 1f;
    }

    public void SetRingLevel(float level) => ring.Target = (level < 0f ? 0f : level) * RingMakeup;

    public void Prepare(double sampleRate)
    {
        crossfade.SetTime(sampleRate, SmoothSeconds);
        gainA.SetTime(sampleRate, SmoothSeconds);
        gainB.SetTime(sampleRate, SmoothSeconds);
        ring.SetTime(sampleRate, SmoothSeconds);
    }

    public void Reset()
    {
        crossfade.Snap();
        gainA.Snap();
        gainB.Snap();
        ring.Snap();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float a, float b)
    {
        var sum = a * gainA.Next() + b * gainB.Next();
        var ringMod = a * b * ring.Next();

        return sum + (ringMod - sum) * crossfade.Next();
    }
}
