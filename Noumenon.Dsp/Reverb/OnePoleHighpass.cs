using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// A one-pole high-pass: a one-pole low-pass tracks the low band and the result is subtracted from
/// the input. <see cref="Coeff"/> is the low-pass pole coefficient (<c>1 − e^(−2π·fc/SR)</c>);
/// 0 means Off. At Off the residual low-band state is not frozen — a frozen state would keep
/// subtracting a constant (DC) from the output — it fades multiplicatively until
/// <see cref="DspHelper.Undenormalize"/> flushes it to exactly zero, so sweeping the control to
/// Off converges to a bit-exact pass-through with no click. Used for the reverbs' low cut and
/// Spin's feedback-loop high-pass.
/// </summary>
internal sealed class OnePoleHighpass
{
    private const float BypassFade = 0.999f;

    private float state;

    public float Coeff;

    public void Clear() => state = 0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        if (Coeff <= 0f)
        {
            state = DspHelper.Undenormalize(state * BypassFade);

            return input - state;
        }

        state = DspHelper.Undenormalize(state + Coeff * (input - state));

        return input - state;
    }
}
