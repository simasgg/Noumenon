using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Reverb;

/// <summary>
/// A fixed-length circular delay line with random tap access, used to build the Dattorro plate
/// tank. The tank reads each delay at its full length (<see cref="Process"/>) while the stereo
/// output taps read the same buffers at shorter offsets (<see cref="Tap"/>). Allocation-free once
/// sized.
/// </summary>
internal sealed class PlateDelay
{
    private float[] buffer = [];
    private int length;
    private int pos;

    public void SetSize(int delaySamples, int maxTap)
    {
        length = delaySamples < 1 ? 1 : delaySamples;
        var need = Math.Max(length, maxTap) + 1;
        if (buffer.Length != need)
            buffer = new float[need];

        pos = 0;
    }

    public void Clear()
    {
        Array.Clear(buffer);
        pos = 0;
    }

    public int Length => length;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Tap(int back)
    {
        var i = pos - back;
        if (i < 0)
            i += buffer.Length;

        return buffer[i];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float TapInterpolated(float back)
    {
        var i0 = (int)back;
        var frac = back - i0;

        return Tap(i0) + (Tap(i0 + 1) - Tap(i0)) * frac;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Push(float input)
    {
        if (++pos >= buffer.Length)
            pos = 0;

        buffer[pos] = input;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        Push(input);

        return Tap(length);
    }
}
