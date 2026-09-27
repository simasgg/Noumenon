using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Output;

/// <summary>
/// MF's output controls: DC blocking, the Width control (mid/side: 0 = mono, 1 = as is, 2 = the
/// side signal doubled), the Volume fader and Mute (both through one glided gain, so muting never
/// clicks). Sits after the Resochord/Reverb pair
/// and before the Fabrications post section; the engine's safety <see cref="PeakLimiter"/> comes
/// last of all.
/// </summary>
public sealed class OutputStage
{
    private const float DcCutoffHz = 5f;
    private const float GainSmoothSeconds = 0.02f;

    private readonly DcBlocker dcLeft = new();
    private readonly DcBlocker dcRight = new();
    private readonly Smoother gain = new();
    private readonly Smoother width = new();

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

    public void SetWidth(float value) => width.Target = DspHelper.Clamp(value, 0f, 2f);

    public void Prepare(double sampleRate)
    {
        dcLeft.SetCutoff(sampleRate, DcCutoffHz);
        dcRight.SetCutoff(sampleRate, DcCutoffHz);
        gain.SetTime(sampleRate, GainSmoothSeconds);
        width.SetTime(sampleRate, GainSmoothSeconds);
    }

    public void Reset()
    {
        dcLeft.Clear();
        dcRight.Clear();
        gain.Snap();
        width.Snap();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Process(ref float left, ref float right)
    {
        var l = dcLeft.Process(left);
        var r = dcRight.Process(right);
        var mid = 0.5f * (l + r);
        var side = 0.5f * (l - r) * width.Next();
        var g = gain.Next();
        left = (mid + side) * g;
        right = (mid - side) * g;
    }

    private void UpdateGain() => gain.Target = muted ? 0f : volumeLinear;
}
