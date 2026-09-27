using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// Fractional pre-delay line. The read tap glides toward its target length (one-pole, ~20 ms) and
/// is read with linear interpolation, so automating the pre-delay time sweeps smoothly instead of
/// clicking when the tap would otherwise jump. Allocation-free in <see cref="Process"/>; the
/// buffer is sized in <see cref="SetSampleRate"/>.
/// </summary>
internal sealed class PreDelayLine
{
    private float[] buffer = [];
    private int write;
    private float delaySamples;
    private float delaySamplesTarget;
    private float smoothCoeff = 1f;

    public void SetSampleRate(double sampleRate, float maxDelayMs, float smoothTimeSeconds)
    {
        var sr = sampleRate <= 0 ? 44100.0 : sampleRate;
        var maxSamples = (int)MathF.Round((float)(maxDelayMs * sr / 1000.0)) + 1;
        if (buffer.Length != maxSamples)
            buffer = new float[maxSamples];

        smoothCoeff = 1f - MathF.Exp(-1f / (smoothTimeSeconds * (float)sr));
        Clear();
    }

    public void SetDelaySamples(float samples)
    {
        var max = buffer.Length - 1;
        if (max < 0)
        {
            delaySamplesTarget = 0f;
            return;
        }

        delaySamplesTarget = samples < 0f ? 0f : samples > max ? max : samples;
    }

    public void Clear()
    {
        Array.Clear(buffer);
        write = 0;
        delaySamples = delaySamplesTarget;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        var len = buffer.Length;
        buffer[write] = input;

        delaySamples += (delaySamplesTarget - delaySamples) * smoothCoeff;
        var readPos = write - delaySamples;
        if (readPos < 0f)
            readPos += len;

        // A delay a hair above zero at write = 0 gives −ε + len, which float rounds to exactly len.
        if (readPos >= len)
            readPos -= len;

        var i0 = (int)readPos;
        var frac = readPos - i0;
        var i1 = i0 + 1;
        if (i1 >= len)
            i1 -= len;

        var output = buffer[i0] + (buffer[i1] - buffer[i0]) * frac;

        if (++write >= len)
            write = 0;

        return output;
    }
}
