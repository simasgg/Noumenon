using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Output;

/// <summary>
/// MF's output controls: DC blocking, the Width control (mid/side: 0 = mono, 1 = as is, 2 = the
/// side signal doubled), the Volume fader through a glided gain, and Mute as a fade: a smoothstep
/// ramp of the same gain over a set number of seconds out and back in, so the drone can leave and
/// return gently. At zero seconds the ramp still takes the gain's 20 ms, so a mute never clicks.
/// Pause is a second switch into the same fade, for hosts that automate it apart from Mute. Sits
/// after the Resochord/Reverb pair and before the Fabrications post section; the engine's safety
/// <see cref="PeakLimiter"/> comes last of all.
/// </summary>
public sealed class OutputStage
{
    private const float DcCutoffHz = 5f;
    private const float GainSmoothSeconds = 0.02f;

    private readonly DcBlocker dcLeft = new();
    private readonly DcBlocker dcRight = new();
    private readonly Smoother gain = new();
    private readonly Smoother width = new();

    private double sampleRate = 48000.0;
    private bool muted;
    private bool paused;
    private float fadeSeconds;
    private double fadeStep;
    private double fadeTarget = 1.0;
    private double fadeLevel = 1.0;

    public OutputStage()
    {
        gain.Snap(1f);
        width.Snap(1f);
        UpdateFadeStep();
    }

    public void SetVolumeDb(float db) => gain.Target = DspHelper.DbToLinear(db);

    public void SetMute(bool mute)
    {
        muted = mute;
        UpdateFadeTarget();
    }

    public void SetPause(bool pause)
    {
        paused = pause;
        UpdateFadeTarget();
    }

    public void SetWidth(float value) => width.Target = DspHelper.Clamp(value, 0f, 2f);

    /// <summary>1 while running, 0 once the mute fade has reached silence, in between while fading either way.</summary>
    public float FadeLevel => (float)fadeLevel;

    /// <summary>The length of the fade out and of the fade in; zero leaves the 20 ms glide.</summary>
    public void SetFade(float seconds)
    {
        if (seconds == fadeSeconds)
            return;

        fadeSeconds = seconds;
        UpdateFadeStep();
    }

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate;
        dcLeft.SetCutoff(sampleRate, DcCutoffHz);
        dcRight.SetCutoff(sampleRate, DcCutoffHz);
        gain.SetTime(sampleRate, GainSmoothSeconds);
        width.SetTime(sampleRate, GainSmoothSeconds);
        UpdateFadeStep();
    }

    public void Reset()
    {
        dcLeft.Clear();
        dcRight.Clear();
        gain.Snap();
        width.Snap();
        fadeLevel = fadeTarget;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(ref float left, ref float right)
    {
        var l = dcLeft.Process(left);
        var r = dcRight.Process(right);
        var mid = 0.5f * (l + r);
        var side = 0.5f * (l - r) * width.Next();
        var g = gain.Next();
        if (fadeLevel != fadeTarget)
            fadeLevel = fadeTarget > fadeLevel ? Math.Min(fadeTarget, fadeLevel + fadeStep) : Math.Max(fadeTarget, fadeLevel - fadeStep);

        if (fadeLevel < 1.0)
            g *= (float)(fadeLevel * fadeLevel * (3.0 - 2.0 * fadeLevel));   // smoothstep: no corner at either end of the fade

        left = (mid + side) * g;
        right = (mid - side) * g;
    }

    private void UpdateFadeTarget() => fadeTarget = muted || paused ? 0.0 : 1.0;

    private void UpdateFadeStep() => fadeStep = 1.0 / (sampleRate * Math.Max(fadeSeconds, GainSmoothSeconds));
}
