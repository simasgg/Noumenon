using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Shared;

/// <summary>A plain integer delay, used to hold a dry path in step with a latent wet path.</summary>
internal sealed class FixedDelay
{
    private float[] buffer = [];
    private int index;

    public void SetLength(int samples)
    {
        var length = samples < 1 ? 1 : samples;
        if (buffer.Length != length)
            buffer = new float[length];

        Clear();
    }

    public void Clear()
    {
        Array.Clear(buffer);
        index = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float input)
    {
        var output = buffer[index];
        buffer[index] = input;
        if (++index >= buffer.Length)
            index = 0;

        return output;
    }
}
