using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Filters;

/// <summary>
/// MF's Master Filter (and the Fabrications post filter): a stereo 12 dB state-variable filter
/// whose LP–HP knob morphs continuously from the low-pass through a notch (the sum of the
/// low-pass and high-pass outputs, unity in both passbands) to the high-pass, with Cut, Res, Mix
/// (dry/wet) and an On switch. Cutoff, morph and mix glide per sample; the coefficients are
/// refreshed only while the cutoff moves. Idle when off or at mix 0, and cleared when it wakes up.
/// </summary>
public sealed class MorphFilter
{
    private const float SmoothSeconds = 0.02f;

    private readonly StateVariableFilter left = new();
    private readonly StateVariableFilter right = new();
    private readonly Smoother cutoff = new();
    private readonly Smoother morph = new();
    private readonly BypassMix mix = new(1f);

    private double sampleRate = 48000.0;
    private float resonance = 0.1f;
    private float lastCutoff = -1f;
    private float lastResonance = -1f;
    private bool wasIdle = true;

    public MorphFilter()
    {
        cutoff.Snap(12000f);
        morph.Snap(0f);
    }

    public void SetEnabled(bool on) => mix.SetEnabled(on);

    public void SetMix(float value) => mix.SetMix(value);

    public void SetCutoff(float hz) => cutoff.Target = hz;

    public void SetResonance(float value) => resonance = DspHelper.Clamp01(value);

    public void SetMorph(float value) => morph.Target = DspHelper.Clamp01(value);

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;
        cutoff.SetTime(this.sampleRate, SmoothSeconds);
        morph.SetTime(this.sampleRate, SmoothSeconds);
        mix.Prepare(this.sampleRate);
        lastCutoff = -1f;
    }

    public void Reset()
    {
        cutoff.Snap();
        morph.Snap();
        mix.Snap();
        left.Clear();
        right.Clear();
        lastCutoff = -1f;
        wasIdle = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(ref float l, ref float r)
    {
        if (mix.IsIdle)
        {
            wasIdle = true;
            return;
        }

        if (wasIdle)
        {
            left.Clear();
            right.Clear();
            wasIdle = false;
        }

        var fc = cutoff.Next();
        if (fc != lastCutoff || resonance != lastResonance)
        {
            left.SetCoefficients(fc, resonance, sampleRate);
            right.CopyCoefficientsFrom(left);
            lastCutoff = fc;
            lastResonance = resonance;
        }

        var m = morph.Next();
        var w = mix.Next();

        left.Process(l, out var lowL, out _, out var highL);
        right.Process(r, out var lowR, out _, out var highR);

        // Low-pass → notch → high-pass: the other response is faded in up to the midpoint, where
        // low + high is a true notch (v0 − k·band) with unity passbands, then the first is faded out.
        float wetL, wetR;
        if (m <= 0.5f)
        {
            var blend = 2f * m;
            wetL = lowL + highL * blend;
            wetR = lowR + highR * blend;
        }
        else
        {
            var blend = 2f - 2f * m;
            wetL = highL + lowL * blend;
            wetR = highR + lowR * blend;
        }

        l += (wetL - l) * w;
        r += (wetR - r) * w;
    }
}
