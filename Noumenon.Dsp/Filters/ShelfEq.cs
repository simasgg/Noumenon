using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Filters;

/// <summary>
/// MF's two-knob EQ (and the post section's): a low shelf at <see cref="LowShelfHz"/> and a high
/// shelf at <see cref="HighShelfHz"/>, ±12 dB each, centre = flat. The gains glide per sample and
/// the biquads are redesigned only while a gain moves; a shelf at exactly 0 dB is skipped, so the
/// default is a bit-exact pass-through.
/// </summary>
public sealed class ShelfEq
{
    public const float LowShelfHz = 200f;
    public const float HighShelfHz = 4000f;

    private const float SmoothSeconds = 0.02f;

    private readonly Biquad lowL = new();
    private readonly Biquad lowR = new();
    private readonly Biquad highL = new();
    private readonly Biquad highR = new();
    private readonly Smoother lowGain = new();
    private readonly Smoother highGain = new();

    private double sampleRate = 48000.0;
    private float lastLow = float.NaN;
    private float lastHigh = float.NaN;
    private bool lowIdle = true;
    private bool highIdle = true;

    public void SetLow(float db) => lowGain.Target = DspHelper.Clamp(db, -24f, 24f);

    public void SetHigh(float db) => highGain.Target = DspHelper.Clamp(db, -24f, 24f);

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;
        lowGain.SetTime(this.sampleRate, SmoothSeconds);
        highGain.SetTime(this.sampleRate, SmoothSeconds);
        lastLow = float.NaN;
        lastHigh = float.NaN;
    }

    public void Reset()
    {
        lowGain.Snap();
        highGain.Snap();
        lowL.Clear();
        lowR.Clear();
        highL.Clear();
        highR.Clear();
        lastLow = float.NaN;
        lastHigh = float.NaN;
        lowIdle = true;
        highIdle = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(ref float l, ref float r)
    {
        var low = lowGain.Next();
        if (low == 0f)
        {
            lowIdle = true;
        }
        else
        {
            if (lowIdle)
            {
                lowL.Clear();
                lowR.Clear();
                lowIdle = false;
            }

            if (low != lastLow)
            {
                lowL.SetLowShelf(sampleRate, LowShelfHz, low);
                lowR.CopyCoefficientsFrom(lowL);
                lastLow = low;
            }

            l = lowL.Process(l);
            r = lowR.Process(r);
        }

        var high = highGain.Next();
        if (high == 0f)
        {
            highIdle = true;
        }
        else
        {
            if (highIdle)
            {
                highL.Clear();
                highR.Clear();
                highIdle = false;
            }

            if (high != lastHigh)
            {
                highL.SetHighShelf(sampleRate, HighShelfHz, high);
                highR.CopyCoefficientsFrom(highL);
                lastHigh = high;
            }

            l = highL.Process(l);
            r = highR.Process(r);
        }
    }
}
