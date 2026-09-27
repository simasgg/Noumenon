using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Oscillators;

/// <summary>
/// One oscillator slot: a phase accumulator producing any <see cref="Waveform"/>. The discontinuous
/// waves are anti-aliased with two-sample PolyBLEP (steps) and PolyBLAMP (the triangle's corners)
/// — cheap and click-free while the pitch moves, with residual aliasing around −50 dB for a 1 kHz
/// saw at 48 kHz and far lower in the drone register; the optional 2× engine rate lowers it further.
/// The frequency glides over ~20 ms so a stepped semitone knob or a jumping P fader never clicks.
/// Noise comes from a seeded <see cref="Xoshiro128"/>, so a render is reproducible.
/// <see cref="Process"/> runs on the audio thread and does not allocate.
/// </summary>
public sealed class Oscillator
{
    private const float MinShape = 0.05f;
    private const float MaxShape = 0.95f;
    private const float PitchSmoothSeconds = 0.02f;

    /// <summary>Highest frequency as a fraction of the sample rate; keeps the PolyBLEP window (2·dt) inside one cycle.</summary>
    private const double MaxNormalizedFrequency = 0.45;

    private readonly Xoshiro128 rng = new(1);

    private double sampleRate = 48000.0;
    private double phase;
    private double increment;
    private double incrementTarget;
    private float incrementCoeff = 1f;
    private float frequencyHz = 261.6256f;
    private float shape = 0.5f;
    private float noiseCutoffHz = 2000f;
    private float noiseCoeff = 0.1f;
    private float noiseState;
    private double noisePhase;
    private double noiseIncrement;

    public Waveform Waveform { get; set; } = Waveform.Sine;

    public double Phase => phase;

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;
        incrementCoeff = DspHelper.SmoothingCoeff(this.sampleRate, PitchSmoothSeconds);
        SetFrequency(frequencyHz);
        SetNoiseCutoff(noiseCutoffHz);
        increment = incrementTarget;
    }

    public void Reset(ulong seed)
    {
        phase = 0.0;
        increment = incrementTarget;
        noiseState = 0f;
        noisePhase = 0.0;
        rng.Reseed(seed);
    }

    public void SetFrequency(float hz)
    {
        frequencyHz = hz;
        var normalized = hz / sampleRate;
        incrementTarget = normalized < 0.0 ? 0.0 : normalized > MaxNormalizedFrequency ? MaxNormalizedFrequency : normalized;
    }

    public void SetShape(float value) => shape = DspHelper.Clamp(value, MinShape, 1f);

    public void SetNoiseCutoff(float hz)
    {
        noiseCutoffHz = DspHelper.Clamp(hz, 1f, (float)(MaxNormalizedFrequency * sampleRate));
        noiseCoeff = DspHelper.OnePoleCoeff(sampleRate, noiseCutoffHz);
        noiseIncrement = noiseCutoffHz / sampleRate;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process()
    {
        increment += (incrementTarget - increment) * incrementCoeff;
        var dt = increment;
        var p = phase;
        float y;
        switch (Waveform)
        {
            case Waveform.Sine:
                y = MathF.Sin((float)(p * DspHelper.TwoPi));
                break;
            case Waveform.Triangle:
                y = Triangle(p, dt, shape > MaxShape ? MaxShape : shape);
                break;
            case Waveform.Saw:
                y = (float)(2.0 * p - 1.0) - Blep(p, dt);
                break;
            case Waveform.Square:
                y = Pulse(p, dt, 0.5);
                break;
            case Waveform.Pulse:
                y = Pulse(p, dt, shape > MaxShape ? MaxShape : shape);
                break;
            case Waveform.BipolarPulse:
                y = BipolarPulse(p, dt, shape);
                break;
            case Waveform.NoiseLp:
                noiseState += noiseCoeff * (rng.NextBipolar() - noiseState);
                y = noiseState;
                break;
            default:
                noisePhase += noiseIncrement;
                if (noisePhase >= 1.0)
                {
                    noisePhase -= 1.0;
                    noiseState = rng.NextBipolar();
                }

                y = noiseState;
                break;
        }

        p += dt;
        if (p >= 1.0)
            p -= 1.0;

        phase = p;

        return y;
    }

    /// <summary>Naive pulse plus a BLEP at each edge (a +2 step at phase 0, a −2 step at the width).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Pulse(double p, double dt, double width)
    {
        var naive = p < width ? 1f : -1f;

        return naive + Blep(p, dt) - Blep(Wrap(p - width), dt);
    }

    /// <summary>+1 for the first half-width, −1 for a half-width from mid-cycle, 0 elsewhere: four ±1 steps, each half a BLEP.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float BipolarPulse(double p, double dt, double width)
    {
        var halfWidth = 0.5 * width;
        var naive = p < halfWidth ? 1f : p >= 0.5 && p < 0.5 + halfWidth ? -1f : 0f;
        var corr = Blep(p, dt) - Blep(Wrap(p - halfWidth), dt) - Blep(Wrap(p - 0.5), dt) + Blep(Wrap(p - 0.5 - halfWidth), dt);

        return naive + 0.5f * corr;
    }

    /// <summary>
    /// Naive triangle with its peak at <paramref name="width"/>, plus a BLAMP at each corner. The slope
    /// changes by <c>2·dt/(w·(1−w))</c> per sample — upward at the trough (phase 0), downward at the peak.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Triangle(double p, double dt, double width)
    {
        var naive = p < width ? -1.0 + 2.0 * p / width : 1.0 - 2.0 * (p - width) / (1.0 - width);
        var slopeStep = 2.0 * dt / (width * (1.0 - width));
        var corr = slopeStep * (Blamp(p, dt) - Blamp(Wrap(p - width), dt));

        return (float)(naive + corr);
    }

    /// <summary>
    /// Two-sample polynomial band-limited step residual for a discontinuity of height 2 at phase 0
    /// (the saw's drop): <c>−(1−s)²</c> on the sample after it, <c>(1+s)²</c> on the sample before,
    /// where s is the distance in samples. A step of height h adds <c>h/2 · Blep</c>.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Blep(double t, double dt)
    {
        if (t < dt)
        {
            var s = t / dt;

            return (float)(s + s - s * s - 1.0);
        }

        if (t > 1.0 - dt)
        {
            var s = (t - 1.0) / dt;

            return (float)(s * s + s + s + 1.0);
        }

        return 0f;
    }

    /// <summary>
    /// The integral of <see cref="Blep"/>: the residual for a slope change at phase 0, <c>(1−|s|)³/6</c>
    /// per unit of slope change (amplitude per sample) on the two samples around it.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Blamp(double t, double dt)
    {
        if (t < dt)
        {
            var s = 1.0 - t / dt;

            return (float)(s * s * s * (1.0 / 6.0));
        }

        if (t > 1.0 - dt)
        {
            var s = (t - 1.0) / dt + 1.0;

            return (float)(s * s * s * (1.0 / 6.0));
        }

        return 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static double Wrap(double t) => t < 0.0 ? t + 1.0 : t;
}
