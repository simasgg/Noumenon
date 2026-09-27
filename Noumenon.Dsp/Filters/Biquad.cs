using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Filters;

/// <summary>
/// A second-order IIR section (transposed direct form II) with the RBJ cookbook designs the chain
/// needs: shelves for the EQs and Butterworth low/high-pass pairs for the limiter's Linkwitz-Riley
/// crossover. Coefficients are set off the per-sample path (or only while a control is moving).
/// </summary>
internal sealed class Biquad
{
    private float b0 = 1f, b1, b2, a1, a2;
    private float z1, z2;

    public void SetLowShelf(double sampleRate, float frequencyHz, float gainDb, float slope = 1f)
    {
        var a = MathF.Pow(10f, gainDb / 40f);
        var w0 = DspHelper.TwoPi * frequencyHz / (float)sampleRate;
        var cos = MathF.Cos(w0);
        var alpha = MathF.Sin(w0) / 2f * MathF.Sqrt((a + 1f / a) * (1f / slope - 1f) + 2f);
        var sqrtA2 = 2f * MathF.Sqrt(a) * alpha;
        var a0 = (a + 1f) + (a - 1f) * cos + sqrtA2;
        Set(
            a * ((a + 1f) - (a - 1f) * cos + sqrtA2) / a0,
            2f * a * ((a - 1f) - (a + 1f) * cos) / a0,
            a * ((a + 1f) - (a - 1f) * cos - sqrtA2) / a0,
            -2f * ((a - 1f) + (a + 1f) * cos) / a0,
            ((a + 1f) + (a - 1f) * cos - sqrtA2) / a0);
    }

    public void SetHighShelf(double sampleRate, float frequencyHz, float gainDb, float slope = 1f)
    {
        var a = MathF.Pow(10f, gainDb / 40f);
        var w0 = DspHelper.TwoPi * frequencyHz / (float)sampleRate;
        var cos = MathF.Cos(w0);
        var alpha = MathF.Sin(w0) / 2f * MathF.Sqrt((a + 1f / a) * (1f / slope - 1f) + 2f);
        var sqrtA2 = 2f * MathF.Sqrt(a) * alpha;
        var a0 = (a + 1f) - (a - 1f) * cos + sqrtA2;
        Set(
            a * ((a + 1f) + (a - 1f) * cos + sqrtA2) / a0,
            -2f * a * ((a - 1f) + (a + 1f) * cos) / a0,
            a * ((a + 1f) + (a - 1f) * cos - sqrtA2) / a0,
            2f * ((a - 1f) - (a + 1f) * cos) / a0,
            ((a + 1f) - (a - 1f) * cos - sqrtA2) / a0);
    }

    public void SetLowPass(double sampleRate, float frequencyHz, float q)
    {
        var w0 = DspHelper.TwoPi * frequencyHz / (float)sampleRate;
        var cos = MathF.Cos(w0);
        var alpha = MathF.Sin(w0) / (2f * q);
        var a0 = 1f + alpha;
        Set((1f - cos) / 2f / a0, (1f - cos) / a0, (1f - cos) / 2f / a0, -2f * cos / a0, (1f - alpha) / a0);
    }

    public void SetHighPass(double sampleRate, float frequencyHz, float q)
    {
        var w0 = DspHelper.TwoPi * frequencyHz / (float)sampleRate;
        var cos = MathF.Cos(w0);
        var alpha = MathF.Sin(w0) / (2f * q);
        var a0 = 1f + alpha;
        Set((1f + cos) / 2f / a0, -(1f + cos) / a0, (1f + cos) / 2f / a0, -2f * cos / a0, (1f - alpha) / a0);
    }

    public void CopyCoefficientsFrom(Biquad other)
    {
        b0 = other.b0;
        b1 = other.b1;
        b2 = other.b2;
        a1 = other.a1;
        a2 = other.a2;
    }

    public void Clear()
    {
        z1 = 0f;
        z2 = 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float x)
    {
        var y = b0 * x + z1;
        // Both states are flushed: flushing only one leaves the other feeding it back through a1
        // (|a1| ≈ 2 for a narrow section), so silence would hover near 1e-17 instead of reaching zero.
        z1 = DspHelper.Undenormalize(b1 * x - a1 * y + z2);
        z2 = DspHelper.Undenormalize(b2 * x - a2 * y);

        return y;
    }

    private void Set(float b0, float b1, float b2, float a1, float a2)
    {
        this.b0 = b0;
        this.b1 = b1;
        this.b2 = b2;
        this.a1 = a1;
        this.a2 = a2;
    }
}
