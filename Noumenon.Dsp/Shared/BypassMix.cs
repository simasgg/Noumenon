using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Shared;

/// <summary>
/// The effective wet amount of a bypassable block: its Mix knob times its On switch, glided per
/// sample (~20 ms) so toggling the block or moving the mix never clicks. A block whose wet amount
/// has settled at zero is <see cref="IsIdle"/> and skips its processing entirely — that is the
/// "an unused block costs nothing" rule — and clears its state when it comes back, so no stale
/// tail from before the bypass leaks out.
/// </summary>
public sealed class BypassMix
{
    private const float SmoothSeconds = 0.02f;

    private readonly Smoother wet = new();
    private bool enabled = true;
    private float mix;

    public BypassMix(float initialMix = 1f)
    {
        mix = DspHelper.Clamp01(initialMix);
        wet.Snap(mix);
    }

    public bool IsIdle => wet.Current == 0f && wet.Target == 0f;

    public float Current => wet.Current;

    public void Prepare(double sampleRate) => wet.SetTime(sampleRate, SmoothSeconds);

    public void SetEnabled(bool on)
    {
        enabled = on;
        wet.Target = on ? mix : 0f;
    }

    public void SetMix(float value)
    {
        mix = DspHelper.Clamp01(value);
        wet.Target = enabled ? mix : 0f;
    }

    public void Snap() => wet.Snap();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Next() => wet.Next();
}
