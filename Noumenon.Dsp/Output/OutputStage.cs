using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Output;

/// <summary>
/// MF's output controls: DC blocking, the Width control (mid/side: 0 = mono, 1 = as is, 2 = the
/// side signal doubled), the Volume fader and Mute (both through one glided gain, so muting never
/// clicks), and Pause: a mute that takes its time, a smoothstep fade of the same gain over a set
/// number of seconds out and back in, so the drone can leave and return gently. Sits after the
/// Resochord/Reverb pair and before the Fabrications post section; the engine's safety
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
    private float volumeLinear = 1f;
    private bool muted;
    private float fadeSeconds = 0f;
    private double fadeStep = 1.0;
    private double fadeTarget = 1.0;
    private double fadeLevel = 1.0;

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

    public void SetWidth(float value) => width.Target = DspHelper.Clamp(value, 0f, 2f);

    /// <summary>1 while running, 0 once the pause fade has reached silence, in between while fading either way.</summary>
    public float PauseLevel => (float)fadeLevel;

    public void SetPause(bool paused) => fadeTarget = paused ? 0.0 : 1.0;

    /// <summary>The length of the fade out and of the fade in; zero makes Pause as abrupt as Mute.</summary>
    public void SetPauseFade(float seconds)
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

    private void UpdateGain() => gain.Target = muted ? 0f : volumeLinear;

    private void UpdateFadeStep() => fadeStep = fadeSeconds > 0f ? 1.0 / (sampleRate * fadeSeconds) : 1.0;
}
