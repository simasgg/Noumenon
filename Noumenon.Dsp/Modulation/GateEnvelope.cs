using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Modulation;

/// <summary>
/// The Play Mode envelope (a Noumenon addition — MF has no note on/off): in Free-run it is a
/// constant 1, in Gated it opens on a MIDI gate with the Attack time and closes with the Release
/// time, so the same patch works as a drone or as a playable pad. It gates the sources before the
/// effect chain, so delays and reverb ring out after the key is released. One-pole segments
/// scaled so a stage is ~95 % complete at its nominal time.
/// </summary>
public sealed class GateEnvelope
{
    private const float Completion = 3f;

    private float value = 1f;
    private float attackCoeff = 1f;
    private float releaseCoeff = 1f;
    private float lastAttack = -1f;
    private float lastRelease = -1f;
    private double sampleRate = 48000.0;
    private bool freeRun = true;
    private bool gate;

    public float Value => value;

    public bool IsOpen => freeRun || gate || value > 0f;

    public void Prepare(double sampleRate)
    {
        this.sampleRate = sampleRate <= 0 ? 48000.0 : sampleRate;
        lastAttack = -1f;
        lastRelease = -1f;
    }

    public void SetTimes(float attackSeconds, float releaseSeconds)
    {
        if (attackSeconds == lastAttack && releaseSeconds == lastRelease)
            return;

        lastAttack = attackSeconds;
        lastRelease = releaseSeconds;
        attackCoeff = DspHelper.SmoothingCoeff(sampleRate, attackSeconds / Completion);
        releaseCoeff = DspHelper.SmoothingCoeff(sampleRate, releaseSeconds / Completion);
    }

    public void SetFreeRun(bool on) => freeRun = on;

    public void SetGate(bool on) => gate = on;

    public void Reset() => value = freeRun ? 1f : 0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Next()
    {
        if (freeRun)
        {
            value = 1f;
            return 1f;
        }

        var target = gate ? 1f : 0f;
        if (value == target)
            return value;

        value += (target - value) * (gate ? attackCoeff : releaseCoeff);
        if (MathF.Abs(target - value) <= 1e-5f)
            value = target;

        return value;
    }
}
