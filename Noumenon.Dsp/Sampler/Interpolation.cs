using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Sampler;

internal static class Interpolation
{
    /// <summary>
    /// Four-point, third-order Hermite (Catmull-Rom) read at a fractional frame position. Exact at
    /// integer positions and for straight lines, smooth between samples; indices past either end
    /// clamp to the edge frame. The position must be non-negative.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Hermite(float[] data, double position)
    {
        var n = data.Length;
        var i = (int)position;
        var f = (float)(position - i);
        if (i >= n)
        {
            i = n - 1;
            f = 0f;
        }

        var im1 = i > 0 ? i - 1 : 0;
        var i1 = i + 1 < n ? i + 1 : n - 1;
        var i2 = i + 2 < n ? i + 2 : n - 1;
        var ym1 = data[im1];
        var y0 = data[i];
        var y1 = data[i1];
        var y2 = data[i2];

        var c1 = 0.5f * (y1 - ym1);
        var c2 = ym1 - 2.5f * y0 + 2f * y1 - 0.5f * y2;
        var c3 = 0.5f * (y2 - ym1) + 1.5f * (y0 - y1);

        return ((c3 * f + c2) * f + c1) * f + y0;
    }
}
