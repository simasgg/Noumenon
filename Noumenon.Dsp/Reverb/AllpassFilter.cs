using System.Runtime.CompilerServices;
using Noumenon.Dsp.Shared;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// Schroeder all-pass filter used for diffusion — it smears the comb outputs into a smooth,
/// echo-free tail without colouring the magnitude response. Classic Freeverb hard-wires the
/// feedback at 0.5; here the engine drives it from the Diffusion control (0.5 at the default).
/// </summary>
internal sealed class AllpassFilter
{
    private float[] buffer = [];
    private int index;

    public float Feedback = 0.5f;

    public void SetSize(int size)
    {
        if (buffer.Length != size)
            buffer = new float[size];

        index = 0;
    }

    public void Clear()
    {
        Array.Clear(buffer);
        index = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        var buffered = DspHelper.Undenormalize(buffer[index]);
        var output = -input + buffered;

        buffer[index] = input + buffered * Feedback;
        if (++index >= buffer.Length)
            index = 0;

        return output;
    }
}
