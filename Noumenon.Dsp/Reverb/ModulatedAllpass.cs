using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// A true Schroeder all-pass whose delay length is modulated by an LFO (read with linear
/// interpolation). This is the plate tank's first decay diffuser — the modulation continuously
/// detunes the recirculating signal, giving the tail its subtle movement / lush shimmer and
/// breaking up metallic ringing.
/// </summary>
internal sealed class ModulatedAllpass
{
    private readonly PlateDelay delay = new();
    private float baseDelay;

    public float Gain;

    public void SetSize(int delaySamples, int excursion)
    {
        baseDelay = delaySamples < 1 ? 1 : delaySamples;
        delay.SetSize(delaySamples + excursion + 2, delaySamples + excursion + 2);
    }

    public void Clear() => delay.Clear();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input, float modOffset)
    {
        var read = baseDelay + modOffset;
        if (read < 1f)
            read = 1f;

        var delayed = delay.TapInterpolated(read);
        var v = input + Gain * delayed;
        delay.Push(v);

        return delayed - Gain * v;
    }
}
