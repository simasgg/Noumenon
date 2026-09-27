using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Modulation;

/// <summary>
/// A rectify-and-smooth envelope follower with separate attack and release times. It tracks the
/// sampler / live-input signal and is exposed as a modulation source (Phase 4's matrix) and as the
/// control of the Sample → AM route, so a field recording can make the oscillators breathe.
/// </summary>
public sealed class EnvelopeFollower
{
    private float envelope;
    private float attackCoeff = 1f;
    private float releaseCoeff = 1f;
    private float lastAttackMs = -1f;
    private float lastReleaseMs = -1f;
    private double sampleRate = 48000.0;

    public float Value => envelope;

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;
        lastAttackMs = -1f;
        lastReleaseMs = -1f;
    }

    public void SetTimes(float attackMs, float releaseMs)
    {
        if (attackMs == lastAttackMs && releaseMs == lastReleaseMs)
            return;

        attackCoeff = DspHelper.SmoothingCoeff(sampleRate, attackMs / 1000f);
        releaseCoeff = DspHelper.SmoothingCoeff(sampleRate, releaseMs / 1000f);
        lastAttackMs = attackMs;
        lastReleaseMs = releaseMs;
    }

    public void Clear() => envelope = 0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        var level = MathF.Abs(input);
        envelope = DspHelper.Undenormalize(envelope + (level > envelope ? attackCoeff : releaseCoeff) * (level - envelope));

        return envelope;
    }
}
