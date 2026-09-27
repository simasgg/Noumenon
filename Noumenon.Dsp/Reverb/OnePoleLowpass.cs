using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// A one-pole low-pass used for the plate's input bandwidth limit and in-tank HF damping, for the
/// wet-output EQ's high cut in both engines, and for the Resochord's per-voice damping.
/// <see cref="Coeff"/> is the pole "openness": 1 passes the signal through unchanged (bright),
/// smaller values roll more high frequency off. Coeff = 1 short-circuits to a bit-exact
/// pass-through, which is what makes the high cut's Off position exactly transparent. The state is
/// denormal-flushed because it sits in recirculating / decaying paths.
/// </summary>
internal sealed class OnePoleLowpass
{
    private float state;

    public float Coeff = 1f;

    public void Clear() => state = 0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        if (Coeff >= 1f)
        {
            state = input;

            return input;
        }

        state = DspHelper.Undenormalize(state + Coeff * (input - state));

        return state;
    }
}
