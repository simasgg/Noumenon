using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// A one-pole low-shelf, built as <c>x + (Gain − 1)·lowpass(x)</c>: frequencies below the crossover
/// are scaled by <see cref="Gain"/>, those above pass at unity. Sits in the plate tank feedback so
/// the low band can decay longer (Gain &gt; 1) or shorter (Gain &lt; 1) than the mids — the Bass
/// control. At Gain = 1 it is an exact pass-through. The low-pass state is denormal-flushed because
/// it recirculates in the tank.
/// </summary>
internal sealed class LowShelf
{
    private float state;

    public float Cutoff = 0.07f;
    public float Gain = 1f;

    public void Clear() => state = 0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        state = DspHelper.Undenormalize(state + Cutoff * (input - state));

        return input + (Gain - 1f) * state;
    }
}
