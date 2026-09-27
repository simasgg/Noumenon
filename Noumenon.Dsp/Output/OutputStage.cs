using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Output;

/// <summary>
/// The end of the chain: DC blocking, the Width control (mid/side — inert while the signal is mono,
/// which it is until Spin and the reverb arrive in Phase 3), the Volume fader and Mute (both through
/// one glided gain, so muting never clicks), then the safety <see cref="PeakLimiter"/>. Phase 3's
/// post section slots in before this and takes over the musical limiting.
/// </summary>
public sealed class OutputStage
{
    private const float DcCutoffHz = 5f;
    private const float GainSmoothSeconds = 0.02f;
    private const float LimiterReleaseSeconds = 0.2f;

    private readonly DcBlocker dcLeft = new();
    private readonly DcBlocker dcRight = new();
    private readonly Smoother gain = new();
    private readonly Smoother width = new();
    private readonly PeakLimiter limiter = new();

    private float volumeLinear = 1f;
    private bool muted;

    public OutputStage()
    {
        gain.Snap(1f);
        width.Snap(1f);
    }

    public void SetVolumeDb(float db)
    {
        volumeLinear = DspHelper.DbToLinear(db);
        UpdateGain();
    }

    public void SetMute(bool mute)
    {
        muted = mute;
        UpdateGain();
    }

    /// <summary>0 = mono, 1 = as is, 2 = the side signal doubled.</summary>
    public void SetWidth(float value) => width.Target = DspHelper.Clamp(value, 0f, 2f);

    public void Prepare(double sampleRate)
    {
        dcLeft.SetCutoff(sampleRate, DcCutoffHz);
        dcRight.SetCutoff(sampleRate, DcCutoffHz);
        gain.SetTime(sampleRate, GainSmoothSeconds);
        width.SetTime(sampleRate, GainSmoothSeconds);
        limiter.Prepare(sampleRate, LimiterReleaseSeconds);
    }

    public void Reset()
    {
        dcLeft.Clear();
        dcRight.Clear();
        gain.Snap();
        width.Snap();
        limiter.Clear();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(ref float left, ref float right)
    {
        var l = dcLeft.Process(left);
        var r = dcRight.Process(right);
        var mid = 0.5f * (l + r);
        var side = 0.5f * (l - r) * width.Next();
        var g = gain.Next();
        l = (mid + side) * g;
        r = (mid - side) * g;
        limiter.Process(ref l, ref r);
        left = l;
        right = r;
    }

    private void UpdateGain() => gain.Target = muted ? 0f : volumeLinear;
}
