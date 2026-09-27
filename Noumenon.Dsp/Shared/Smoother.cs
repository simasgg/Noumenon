using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Shared;

/// <summary>
/// A one-pole parameter smoother: the control thread sets <see cref="Target"/>, the audio thread
/// pulls one glided value per sample with <see cref="Next"/>. Every gain, pitch and cutoff that
/// would click on a jump goes through one of these (≈20 ms). The state is a <see cref="double"/>
/// because a float one-pole stalls a few 1e-5 short of its target (the per-sample step falls under
/// half a ULP), which would leave a gain sent to zero faintly on; in double the glide converges,
/// and once it is within float noise of the target it snaps exactly, so a muted output really is
/// zero and a render settles to a bit-stable steady state.
/// </summary>
public sealed class Smoother
{
    private double current;
    private float target;
    private float coeff = 1f;

    public float Target
    {
        get => target;
        set => target = value;
    }

    public float Current => (float)current;

    public void SetTime(double sampleRate, float seconds) => coeff = DspHelper.SmoothingCoeff(sampleRate, seconds);

    public void Snap() => current = target;

    public void Snap(float value)
    {
        target = value;
        current = value;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Next()
    {
        if (current == target)
            return target;

        current += (target - current) * coeff;
        if (Math.Abs(target - current) <= 1e-6 * Math.Max(1.0, Math.Abs(target)))
            current = target;

        return (float)current;
    }
}
