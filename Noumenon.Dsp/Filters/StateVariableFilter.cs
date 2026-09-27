using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Filters;

/// <summary>
/// A 12 dB/octave state-variable filter in the trapezoidal (zero-delay feedback) form after
/// Zavalishin / Cytomic: stable at any cutoff up to near Nyquist and at full resonance, with
/// low-, band- and high-pass taken from the same two integrator states. Resonance 0..1 maps to the
/// damping <c>k = 2 → 0.02</c>, so the response at cutoff goes from −6 dB to a +34 dB peak on every
/// output alike. Coefficients cost a <c>tan</c>; callers recompute them only when the (smoothed)
/// cutoff actually changes. The section filters (MF's 12 dB lowpass with our BP/HP modes) and the
/// master filter's LP-HP morph are built on it.
/// </summary>
internal sealed class StateVariableFilter
{
    private const float MinDamping = 0.02f;
    private const float MinCutoffHz = 5f;
    private const float MaxCutoffFraction = 0.45f;

    private float ic1eq, ic2eq;
    private float k = 2f;
    private float a1, a2, a3;

    public FilterType Type { get; set; }

    public void SetCoefficients(float cutoffHz, float resonance, double sampleRate)
    {
        var sr = (float)sampleRate;
        var fc = DspHelper.Clamp(cutoffHz, MinCutoffHz, MaxCutoffFraction * sr);
        var g = MathF.Tan(MathF.PI * fc / sr);
        k = 2f - (2f - MinDamping) * DspHelper.Clamp01(resonance);
        a1 = 1f / (1f + g * (g + k));
        a2 = g * a1;
        a3 = g * a2;
    }

    public void Clear()
    {
        ic1eq = 0f;
        ic2eq = 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float v0)
    {
        var v3 = v0 - ic2eq;
        var v1 = a1 * ic1eq + a2 * v3;
        var v2 = ic2eq + a2 * ic1eq + a3 * v3;
        ic1eq = DspHelper.Undenormalize(2f * v1 - ic1eq);
        ic2eq = DspHelper.Undenormalize(2f * v2 - ic2eq);

        return Type switch
        {
            FilterType.LowPass => v2,
            FilterType.BandPass => v1,
            _ => v0 - k * v1 - v2,
        };
    }
}
