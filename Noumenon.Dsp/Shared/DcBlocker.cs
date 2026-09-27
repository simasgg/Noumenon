using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Shared;

/// <summary>
/// First-order DC blocker (<c>y = x − x₁ + R·y₁</c>), a few hertz wide: the pulse waves carry DC at
/// non-square widths and the ring-mod path squares signals, so the output stage takes the offset
/// out before the width matrix and the limiter see it.
/// </summary>
internal sealed class DcBlocker
{
    private float x1, y1;
    private float r = 0.999f;

    public void SetCutoff(double sampleRate, float cutoffHz) => r = 1f - DspHelper.TwoPi * cutoffHz / (float)sampleRate;

    public void Clear()
    {
        x1 = 0f;
        y1 = 0f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float Process(float x)
    {
        var y = x - x1 + r * y1;
        x1 = x;
        y1 = DspHelper.Undenormalize(y);

        return y;
    }
}
