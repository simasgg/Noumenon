using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// A true Schroeder all-pass (flat magnitude response) with an integer delay, used for the plate's
/// input diffusers and the second tank diffuser. Its internal line is tap-readable so the stereo
/// output taps can read from inside the diffuser, as in Dattorro's design.
/// </summary>
internal sealed class PlateAllpass
{
    private readonly PlateDelay delay = new();

    public float Gain;

    public void SetSize(int delaySamples, int maxTap) => delay.SetSize(delaySamples, maxTap);

    public void Clear() => delay.Clear();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Tap(int back) => delay.Tap(back);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        var delayed = delay.Tap(delay.Length);
        var v = input + Gain * delayed;
        delay.Push(v);

        return delayed - Gain * v;
    }
}
