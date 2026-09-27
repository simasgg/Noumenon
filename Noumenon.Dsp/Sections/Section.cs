using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Oscillators;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Sections;

/// <summary>
/// One of MF's two oscillator sections: six slots, each scaled by its own gain (level × trim, or 0
/// when the slot is off), summed, run through the section's 12 dB filter and scaled by the section
/// level. The sum is scaled by <see cref="SumGain"/> so six slots at half level land around full
/// scale. Pitch is not computed here — the engine sets each slot's frequency from the knobs and the
/// P routing at control rate — but every gain and the cutoff glide per sample, and the filter
/// coefficients are refreshed only while the cutoff is actually moving. A slot whose gain has
/// settled at zero is skipped entirely, so an unused slot costs nothing.
/// </summary>
public sealed class Section
{
    public const int SlotCount = 6;

    /// <summary>Headroom applied to the six-slot sum: 6 × 0.5 × ⅓ = 1.</summary>
    public const float SumGain = 1f / 3f;

    private const float GainSmoothSeconds = 0.02f;
    private const float CutoffSmoothSeconds = 0.02f;

    private readonly Oscillator[] oscillators = new Oscillator[SlotCount];
    private readonly Smoother[] gains = new Smoother[SlotCount];
    private readonly Smoother cutoff = new();
    private readonly Smoother level = new();
    private readonly StateVariableFilter filter = new();

    private double sampleRate = 48000.0;
    private float resonance = 0.1f;
    private float lastCutoff = -1f;
    private float lastResonance = -1f;

    public Section()
    {
        for (var i = 0; i < SlotCount; i++)
        {
            oscillators[i] = new Oscillator();
            gains[i] = new Smoother();
        }

        cutoff.Snap(8000f);
        level.Snap(0.7f);
    }

    public Oscillator Slot(int index) => oscillators[index];

    public void SetSlotGain(int slot, float gain) => gains[slot].Target = gain < 0f ? 0f : gain;

    public void SetFilter(float cutoffHz, float reso, FilterType type)
    {
        cutoff.Target = cutoffHz;
        resonance = reso;
        filter.Type = type;
    }

    public void SetLevel(float value) => level.Target = value < 0f ? 0f : value;

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;
        for (var i = 0; i < SlotCount; i++)
        {
            oscillators[i].Prepare(this.sampleRate);
            gains[i].SetTime(this.sampleRate, GainSmoothSeconds);
        }

        cutoff.SetTime(this.sampleRate, CutoffSmoothSeconds);
        level.SetTime(this.sampleRate, GainSmoothSeconds);
        lastCutoff = -1f;
    }

    /// <summary>Snaps every glide to its target, rewinds the oscillators and reseeds each slot's noise from <paramref name="seed"/>.</summary>
    public void Reset(ulong seed)
    {
        for (var i = 0; i < SlotCount; i++)
        {
            oscillators[i].Reset(seed + (ulong)i * 0x9E3779B97F4A7C15UL);
            gains[i].Snap();
        }

        cutoff.Snap();
        level.Snap();
        filter.Clear();
        lastCutoff = -1f;
    }

    public float Process()
    {
        var sum = 0f;
        for (var i = 0; i < SlotCount; i++)
        {
            var gain = gains[i];
            if (gain.Current == 0f && gain.Target == 0f)
                continue;

            sum += oscillators[i].Process() * gain.Next();
        }

        var fc = cutoff.Next();
        if (fc != lastCutoff || resonance != lastResonance)
        {
            filter.SetCoefficients(fc, resonance, sampleRate);
            lastCutoff = fc;
            lastResonance = resonance;
        }

        return filter.Process(sum * SumGain) * level.Next();
    }
}
