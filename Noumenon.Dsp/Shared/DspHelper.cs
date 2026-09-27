using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Shared;

public static class DspHelper
{
    public const float TwoPi = 2f * MathF.PI;

    /// <summary>A gain at or below this many decibels is treated as silence (exactly zero).</summary>
    public const float SilenceDb = -60f;

    /// <summary>
    /// Flushes denormal / vanishingly small values to zero. Feedback and smoothing paths decay
    /// through the denormal range when the input goes silent, and denormal arithmetic can spike CPU
    /// by 10-100x. Anything below ~1e-18 is far under -300 dB, so flushing it is inaudible.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Undenormalize(float value) => value is < 1e-18f and > -1e-18f ? 0f : value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Clamp01(float value) => value < 0f ? 0f : value > 1f ? 1f : value;

    public static float DbToLinear(float db) => db <= SilenceDb ? 0f : MathF.Pow(10f, db / 20f);

    public static float LinearToDb(float linear) => linear <= 0f ? SilenceDb : 20f * MathF.Log10(linear);

    /// <summary>One-pole coefficient that reaches ~63 % of a step in <paramref name="seconds"/>; 1 (instant) for a non-positive time.</summary>
    public static float SmoothingCoeff(double sampleRate, float seconds) => seconds <= 0f ? 1f : 1f - MathF.Exp(-1f / (seconds * (float)sampleRate));

    public static float OnePoleCoeff(double sampleRate, float cutoffHz) => 1f - MathF.Exp(-TwoPi * cutoffHz / (float)sampleRate);
}
