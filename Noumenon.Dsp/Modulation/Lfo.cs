using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Modulation;

/// <summary>LFO shapes; the order is the parameter table's choice list.</summary>
public enum LfoShape
{
    Sine,
    Triangle,
    Saw,
    Square,
    SampleHold,
    SmoothRandom,
    Drift,
}

/// <summary>
/// A control-rate LFO for the modulation matrix, advanced once per control tick and returning
/// −1..1. The random shapes draw from a seeded <see cref="Xoshiro128"/> reseeded in
/// <see cref="Reset"/>, so a render with LFOs is reproducible: Sample &amp; Hold steps to a new
/// value each cycle, Smooth Random eases between them, and Drift is a bounded random walk whose
/// speed follows the rate.
/// </summary>
public sealed class Lfo
{
    private readonly Xoshiro128 rng = new(1);
    private double phase;
    private float held;
    private float previous;
    private float target;
    private float drift;

    public LfoShape Shape { get; set; }

    public float RateHz { get; set; } = 0.1f;

    public void Reset(ulong seed)
    {
        rng.Reseed(seed);
        phase = 0.0;
        held = rng.NextBipolar();
        previous = held;
        target = rng.NextBipolar();
        drift = 0f;
    }

    public float Advance(double seconds)
    {
        phase += RateHz * seconds;
        if (phase >= 1.0)
        {
            phase -= Math.Floor(phase);
            held = rng.NextBipolar();
            previous = target;
            target = rng.NextBipolar();
        }

        var p = (float)phase;
        switch (Shape)
        {
            case LfoShape.Triangle:
                return 1f - 4f * MathF.Abs(p - 0.5f);
            case LfoShape.Saw:
                return 2f * p - 1f;
            case LfoShape.Square:
                return p < 0.5f ? 1f : -1f;
            case LfoShape.SampleHold:
                return held;
            case LfoShape.SmoothRandom:
            {
                var t = 0.5f - 0.5f * MathF.Cos(MathF.PI * p);
                return previous + (target - previous) * t;
            }
            case LfoShape.Drift:
            {
                // A random walk pulled gently back to the centre; the step scales with the rate.
                drift += rng.NextBipolar() * (float)(RateHz * seconds) * 4f - drift * (float)(RateHz * seconds) * 0.5f;
                drift = DspHelper.Clamp(drift, -1f, 1f);
                return drift;
            }
            default:
                return MathF.Sin(DspHelper.TwoPi * p);
        }
    }
}
