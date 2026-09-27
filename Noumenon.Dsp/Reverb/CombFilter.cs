using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// Lowpass-feedback comb filter — the resonant "tank" element of Freeverb. The one-pole lowpass in
/// the feedback path is the damping control: more damping = faster high-frequency decay. A one-pole
/// low-shelf on the same feedback is the Bass control: <see cref="BassGain"/> &gt; 1 lets the low
/// band ring longer than the mids, &lt; 1 tightens it; at 1 the shelf is a pass-through.
/// </summary>
internal sealed class CombFilter
{
    private float[] buffer = [];
    private int index;
    private float filterStore;
    private float bassStore;

    public float Feedback;
    public float Damp1;
    public float Damp2;
    public float BassCutoff;
    public float BassGain = 1f;

    public void SetSize(int size)
    {
        if (buffer.Length != size)
            buffer = new float[size];

        index = 0;
    }

    public void Clear()
    {
        Array.Clear(buffer);
        filterStore = 0f;
        bassStore = 0f;
        index = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        var output = DspHelper.Undenormalize(buffer[index]);
        filterStore = DspHelper.Undenormalize(output * Damp2 + filterStore * Damp1);
        bassStore = DspHelper.Undenormalize(bassStore + BassCutoff * (filterStore - bassStore));
        var shaped = filterStore + (BassGain - 1f) * bassStore;
        buffer[index] = input + shaped * Feedback;
        if (++index >= buffer.Length)
            index = 0;

        return output;
    }
}
