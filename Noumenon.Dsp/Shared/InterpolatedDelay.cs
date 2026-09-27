using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Shared;

/// <summary>
/// A fractional delay line whose length glides toward its target (one-pole, ~20 ms) and is read
/// with linear interpolation, so automating the time sweeps smoothly — like a tape delay, which
/// is exactly MF's Spin "pitch shift" — instead of clicking when the tap would jump. Read the
/// output first, then write the input: the read is measured from the slot about to be written, so
/// a length of D returns the sample written D samples ago. Allocation-free once
/// <see cref="Prepare"/> has sized the buffer.
/// </summary>
internal sealed class InterpolatedDelay
{
    private const float MinDelaySamples = 1f;

    private float[] buffer = [];
    private int write;
    private float delaySamples = MinDelaySamples;
    private float delaySamplesTarget = MinDelaySamples;
    private float smoothCoeff = 1f;

    public float MaxDelaySamples => buffer.Length - 2;

    public void Prepare(double sampleRate, float maxSeconds, float smoothSeconds)
    {
        var sr = sampleRate <= 0 ? 48000.0 : sampleRate;
        var size = (int)Math.Ceiling(maxSeconds * sr) + 3;
        if (buffer.Length != size)
            buffer = new float[size];

        smoothCoeff = DspHelper.SmoothingCoeff(sr, smoothSeconds);
        SetDelaySamples(delaySamplesTarget);
        Clear();
    }

    public void SetDelaySamples(float samples)
    {
        var max = MaxDelaySamples;
        delaySamplesTarget = samples < MinDelaySamples ? MinDelaySamples : samples > max ? max : samples;
    }

    public void SnapDelay() => delaySamples = delaySamplesTarget;

    public void Clear()
    {
        Array.Clear(buffer);
        write = 0;
        delaySamples = delaySamplesTarget;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Read()
    {
        var length = buffer.Length;
        delaySamples += (delaySamplesTarget - delaySamples) * smoothCoeff;
        var readPos = write - delaySamples;
        if (readPos < 0f)
            readPos += length;

        // Guard against float rounding landing exactly on the buffer length.
        if (readPos >= length)
            readPos -= length;

        var i0 = (int)readPos;
        var frac = readPos - i0;
        var i1 = i0 + 1;
        if (i1 >= length)
            i1 -= length;

        return buffer[i0] + (buffer[i1] - buffer[i0]) * frac;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(float input)
    {
        buffer[write] = input;
        if (++write >= buffer.Length)
            write = 0;
    }
}
