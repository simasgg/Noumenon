using System.Runtime.CompilerServices;
using Noumenon.Dsp.Reverb;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Effects;

/// <summary>
/// MF's Spin: the signal passes <i>through</i> a stereo delay — T1 on the left, T2 (plus the Fine
/// offset) on the right — with feedback, so unequal times spread the image and moving times shift
/// the pitch of everything, tape-style (the times glide and are read with interpolation). Link
/// makes the right time follow T1, leaving Fine as a pure stereo offset. A high-pass in the
/// feedback loop keeps repeats from piling up mud. Mix is a Noumenon addition (MF is fully wet);
/// idle when off or at mix 0.
/// </summary>
public sealed class Spin
{
    public const float MinTimeMs = 0.1f;
    public const float MaxTimeMs = 500f;
    public const float MaxFineMs = 1f;
    public const float MaxFeedback = 0.95f;

    private const float TimeSmoothSeconds = 0.02f;
    private const float LoopHighPassHz = 80f;

    private readonly InterpolatedDelay left = new();
    private readonly InterpolatedDelay right = new();
    private readonly OnePoleHighpass loopLeft = new();
    private readonly OnePoleHighpass loopRight = new();
    private readonly Smoother feedback = new();
    private readonly BypassMix mix = new(1f);

    private double sampleRate = 48000.0;
    private bool wasIdle = true;

    public Spin() => feedback.Snap(0.35f);

    public void SetEnabled(bool on) => mix.SetEnabled(on);

    public void SetMix(float value) => mix.SetMix(value);

    public void SetFeedback(float value) => feedback.Target = DspHelper.Clamp(value, 0f, MaxFeedback);

    public void SetTimes(float time1Ms, float time2Ms, float fineMs, bool link)
    {
        var t1 = DspHelper.Clamp(time1Ms, MinTimeMs, MaxTimeMs);
        var t2 = DspHelper.Clamp((link ? t1 : DspHelper.Clamp(time2Ms, MinTimeMs, MaxTimeMs)) + DspHelper.Clamp(fineMs, -MaxFineMs, MaxFineMs), MinTimeMs, MaxTimeMs + MaxFineMs);
        left.SetDelaySamples((float)(t1 * sampleRate / 1000.0));
        right.SetDelaySamples((float)(t2 * sampleRate / 1000.0));
    }

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;
        left.Prepare(this.sampleRate, (MaxTimeMs + MaxFineMs) / 1000f, TimeSmoothSeconds);
        right.Prepare(this.sampleRate, (MaxTimeMs + MaxFineMs) / 1000f, TimeSmoothSeconds);
        var hp = DspHelper.OnePoleCoeff(this.sampleRate, LoopHighPassHz);
        loopLeft.Coeff = hp;
        loopRight.Coeff = hp;
        feedback.SetTime(this.sampleRate, TimeSmoothSeconds);
        mix.Prepare(this.sampleRate);
    }

    public void Reset()
    {
        left.Clear();
        right.Clear();
        loopLeft.Clear();
        loopRight.Clear();
        feedback.Snap();
        mix.Snap();
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
            loopLeft.Clear();
            loopRight.Clear();
            wasIdle = false;
        }

        var fb = feedback.Next();
        var w = mix.Next();
        var delayedL = left.Read();
        var delayedR = right.Read();
        left.Write(l + loopLeft.Process(delayedL) * fb);
        right.Write(r + loopRight.Process(delayedR) * fb);
        l += (delayedL - l) * w;
        r += (delayedR - r) * w;
    }
}
