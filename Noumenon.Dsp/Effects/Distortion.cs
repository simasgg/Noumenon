using System.Runtime.CompilerServices;
using Noumenon.Dsp.Filters;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Effects;

/// <summary>The waveshaping curves; the order is the parameter table's choice list.</summary>
public enum DistortionCurve
{
    Tanh,
    Fold,
    Bit,
}

/// <summary>
/// MF's Distortion (Mix, Drive) with a choice of curve, run at 2× the engine rate through the
/// half-band resamplers so the curve's harmonics above Nyquist are filtered off before they fold
/// back. Drive maps to 0–40 dB of input gain. The wet path is 63 samples late through the
/// resamplers, so the dry path is delayed by the same amount and the block has a constant latency
/// whether it is on or off — a toggle never shifts the signal in time. Idle when off or at mix 0.
/// </summary>
public sealed class Distortion
{
    public const int Latency = HalfBand.RoundTripLatency;

    private const float SmoothSeconds = 0.02f;
    private const float MaxDriveDb = 40f;
    private const float MinBits = 3f;
    private const float MaxBits = 16f;

    private readonly HalfBandUpsampler upLeft = new();
    private readonly HalfBandUpsampler upRight = new();
    private readonly HalfBandDownsampler downLeft = new();
    private readonly HalfBandDownsampler downRight = new();
    private readonly FixedDelay dryLeft = new();
    private readonly FixedDelay dryRight = new();
    private readonly Smoother gain = new();
    private readonly BypassMix mix = new(0f);

    private bool wasIdle = true;

    public Distortion()
    {
        gain.Snap(DriveToGain(0.2f));
        dryLeft.SetLength(Latency);
        dryRight.SetLength(Latency);
    }

    public DistortionCurve Curve { get; set; }

    public void SetEnabled(bool on) => mix.SetEnabled(on);

    public void SetMix(float value) => mix.SetMix(value);

    public void SetDrive(float drive) => gain.Target = DriveToGain(drive);

    public void Prepare(double sampleRate)
    {
        gain.SetTime(sampleRate, SmoothSeconds);
        mix.Prepare(sampleRate);
    }

    public void Reset()
    {
        gain.Snap();
        mix.Snap();
        dryLeft.Clear();
        dryRight.Clear();
        upLeft.Clear();
        upRight.Clear();
        downLeft.Clear();
        downRight.Clear();
        wasIdle = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(ref float l, ref float r)
    {
        var dryL = dryLeft.Process(l);
        var dryR = dryRight.Process(r);
        if (mix.IsIdle)
        {
            wasIdle = true;
            l = dryL;
            r = dryR;
            return;
        }

        if (wasIdle)
        {
            upLeft.Clear();
            upRight.Clear();
            downLeft.Clear();
            downRight.Clear();
            wasIdle = false;
        }

        var g = gain.Next();
        var w = mix.Next();

        upLeft.Process(l, out var la, out var lb);
        upRight.Process(r, out var ra, out var rb);
        var wetL = downLeft.Process(Shape(la * g), Shape(lb * g));
        var wetR = downRight.Process(Shape(ra * g), Shape(rb * g));

        l = dryL + (wetL - dryL) * w;
        r = dryR + (wetR - dryR) * w;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private float Shape(float x)
    {
        switch (Curve)
        {
            case DistortionCurve.Fold:
            {
                // Triangle fold: anything past ±1 reflects back, again and again.
                var t = (x + 1f) % 4f;
                if (t < 0f)
                    t += 4f;

                return MathF.Abs(t - 2f) - 1f;
            }
            case DistortionCurve.Bit:
            {
                // Drive sets the word length (16 → 3 bits); the step is a hard quantizer.
                var bits = MaxBits - (MaxBits - MinBits) * DspHelper.Clamp01(MathF.Log10(gain.Current) / (MaxDriveDb / 20f));
                var steps = MathF.Pow(2f, bits - 1f);
                var clipped = DspHelper.Clamp(x / gain.Current, -1f, 1f);

                return MathF.Round(clipped * steps) / steps;
            }
            default:
                return MathF.Tanh(x);
        }
    }

    private static float DriveToGain(float drive) => MathF.Pow(10f, DspHelper.Clamp01(drive) * MaxDriveDb / 20f);
}
