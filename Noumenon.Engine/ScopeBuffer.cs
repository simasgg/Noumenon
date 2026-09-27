using System.Runtime.CompilerServices;

namespace Noumenon.Engine;

/// <summary>
/// The XY scope's feed: a lock-free ring of the post-Spin stereo signal. The audio thread writes
/// one frame per sample; the UI thread copies the latest frames whenever it redraws. Single
/// producer, single consumer, no locks — a frame the UI reads while it is being written is at
/// worst one sample stale, which a scope cannot show.
/// </summary>
public sealed class ScopeBuffer
{
    public const int Capacity = 4096;

    private const int Mask = Capacity - 1;

    private readonly float[] left = new float[Capacity];
    private readonly float[] right = new float[Capacity];
    private int writeIndex;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(float l, float r)
    {
        var i = writeIndex;
        left[i] = l;
        right[i] = r;
        Volatile.Write(ref writeIndex, (i + 1) & Mask);
    }

    /// <summary>Copies the most recent <paramref name="count"/> frames (oldest first) into the destination arrays.</summary>
    public void CopyLatest(float[] destinationLeft, float[] destinationRight, int count)
    {
        count = Math.Clamp(count, 0, Math.Min(Capacity, Math.Min(destinationLeft.Length, destinationRight.Length)));
        var end = Volatile.Read(ref writeIndex);
        var start = (end - count) & Mask;
        for (var k = 0; k < count; k++)
        {
            var i = (start + k) & Mask;
            destinationLeft[k] = left[i];
            destinationRight[k] = right[i];
        }
    }

    public void Clear()
    {
        Array.Clear(left);
        Array.Clear(right);
        Volatile.Write(ref writeIndex, 0);
    }
}
