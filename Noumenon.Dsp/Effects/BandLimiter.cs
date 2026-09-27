using System.Runtime.CompilerServices;
using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Effects;

/// <summary>Release speed of the two-band limiter; the order is the parameter table's choice list.</summary>
public enum LimiterSpeed
{
    Fast,
    Medium,
    Slow,
}

/// <summary>
/// Fabrications' two-band limiter: a 4th-order Linkwitz-Riley crossover at Split, then a
/// stereo-linked peak limiter per band with its own threshold (LO / HI), an input gain in front
/// and three release speeds. Each band is held under its threshold with an instantaneous attack;
/// the recombined bands are allpass-flat but can sum above either threshold, which is why the
/// engine keeps its safety limiter after the post section. The On switch crossfades to the dry
/// signal over ~20 ms and the block goes idle, crossover included.
/// </summary>
public sealed class BandLimiter
{
    private const float SmoothSeconds = 0.02f;
    private const float ButterworthQ = 0.70710678f;

    private static readonly float[] ReleaseSeconds = [0.03f, 0.15f, 0.6f];

    private readonly Biquad lowL1 = new(), lowL2 = new(), lowR1 = new(), lowR2 = new();
    private readonly Biquad highL1 = new(), highL2 = new(), highR1 = new(), highR2 = new();
    private readonly Smoother gain = new();
    private readonly Smoother lowThreshold = new();
    private readonly Smoother highThreshold = new();
    private readonly BypassMix on = new(1f);

    private double sampleRate = 48000.0;
    private float splitHz = 250f;
    private float lowEnvelope;
    private float highEnvelope;
    private float releaseCoeff;
    private LimiterSpeed speed = LimiterSpeed.Medium;
    private bool wasIdle = true;

    public BandLimiter()
    {
        gain.Snap(1f);
        lowThreshold.Snap(DspHelper.DbToLinear(-6f));
        highThreshold.Snap(DspHelper.DbToLinear(-6f));
    }

    public void SetEnabled(bool enabled) => on.SetEnabled(enabled);

    public void SetGainDb(float db) => gain.Target = DspHelper.DbToLinear(DspHelper.Clamp(db, 0f, 24f));

    public void SetThresholdsDb(float lowDb, float highDb)
    {
        lowThreshold.Target = DspHelper.DbToLinear(DspHelper.Clamp(lowDb, -24f, 0f));
        highThreshold.Target = DspHelper.DbToLinear(DspHelper.Clamp(highDb, -24f, 0f));
    }

    public void SetSplit(float hz)
    {
        var clamped = DspHelper.Clamp(hz, 50f, 8000f);
        if (clamped == splitHz)
            return;

        splitHz = clamped;
        DesignCrossover();
    }

    public void SetSpeed(LimiterSpeed value)
    {
        speed = value;
        releaseCoeff = MathF.Exp(-1f / (ReleaseSeconds[(int)speed] * (float)sampleRate));
    }

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;
        gain.SetTime(this.sampleRate, SmoothSeconds);
        lowThreshold.SetTime(this.sampleRate, SmoothSeconds);
        highThreshold.SetTime(this.sampleRate, SmoothSeconds);
        on.Prepare(this.sampleRate);
        SetSpeed(speed);
        DesignCrossover();
    }

    public void Reset()
    {
        gain.Snap();
        lowThreshold.Snap();
        highThreshold.Snap();
        on.Snap();
        ClearState();
        wasIdle = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(ref float l, ref float r)
    {
        if (on.IsIdle)
        {
            wasIdle = true;
            return;
        }

        if (wasIdle)
        {
            ClearState();
            wasIdle = false;
        }

        var g = gain.Next();
        var inL = l * g;
        var inR = r * g;

        var lowLeft = lowL2.Process(lowL1.Process(inL));
        var lowRight = lowR2.Process(lowR1.Process(inR));
        var highLeft = highL2.Process(highL1.Process(inL));
        var highRight = highR2.Process(highR1.Process(inR));

        var lowThr = lowThreshold.Next();
        var highThr = highThreshold.Next();

        var lowPeak = MathF.Max(MathF.Abs(lowLeft), MathF.Abs(lowRight));
        lowEnvelope = lowPeak > lowEnvelope ? lowPeak : DspHelper.Undenormalize(lowEnvelope * releaseCoeff);
        if (lowEnvelope > lowThr)
        {
            var reduce = lowThr / lowEnvelope;
            lowLeft *= reduce;
            lowRight *= reduce;
        }

        var highPeak = MathF.Max(MathF.Abs(highLeft), MathF.Abs(highRight));
        highEnvelope = highPeak > highEnvelope ? highPeak : DspHelper.Undenormalize(highEnvelope * releaseCoeff);
        if (highEnvelope > highThr)
        {
            var reduce = highThr / highEnvelope;
            highLeft *= reduce;
            highRight *= reduce;
        }

        var w = on.Next();
        l += (lowLeft + highLeft - l) * w;
        r += (lowRight + highRight - r) * w;
    }

    private void DesignCrossover()
    {
        lowL1.SetLowPass(sampleRate, splitHz, ButterworthQ);
        lowL2.CopyCoefficientsFrom(lowL1);
        lowR1.CopyCoefficientsFrom(lowL1);
        lowR2.CopyCoefficientsFrom(lowL1);
        highL1.SetHighPass(sampleRate, splitHz, ButterworthQ);
        highL2.CopyCoefficientsFrom(highL1);
        highR1.CopyCoefficientsFrom(highL1);
        highR2.CopyCoefficientsFrom(highL1);
    }

    private void ClearState()
    {
        lowL1.Clear();
        lowL2.Clear();
        lowR1.Clear();
        lowR2.Clear();
        highL1.Clear();
        highL2.Clear();
        highR1.Clear();
        highR2.Clear();
        lowEnvelope = 0f;
        highEnvelope = 0f;
    }
}
