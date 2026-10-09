namespace Noumenon.Engine.Recording;

/// <summary>
/// A single-producer, single-consumer ring of floats for handing audio between the real-time
/// thread and a slower one (the WAV writer, a capture device's thread). <see cref="Write"/> and
/// <see cref="Read"/> never lock, allocate or block: a full ring makes the producer drop what does
/// not fit and report it, so a stalled consumer can cost recorded samples but never a glitch on
/// the audio thread. The capacity is rounded up to a power of two and the two cursors count
/// samples ever written and read, so the whole capacity is usable and the indices never reset.
/// Each side owns its cursor and publishes it with a volatile write after touching the data, which
/// is all the ordering the other side needs.
/// </summary>
public sealed class SampleRing
{
    private readonly float[] buffer;
    private readonly int mask;
    private long written;
    private long read;

    public SampleRing(int minimumCapacity)
    {
        var capacity = 2;
        while (capacity < minimumCapacity)
            capacity <<= 1;

        buffer = new float[capacity];
        mask = capacity - 1;
    }

    public int Capacity => buffer.Length;

    public int Count => (int)(Volatile.Read(ref written) - Volatile.Read(ref read));

    public int Free => Capacity - Count;

    /// <summary>Producer side: copies as much of <paramref name="source"/> as fits and returns how many samples that was.</summary>
    public int Write(ReadOnlySpan<float> source)
    {
        var w = written;
        var free = Capacity - (int)(w - Volatile.Read(ref read));
        var n = Math.Min(free, source.Length);
        if (n <= 0)
            return 0;

        var index = (int)(w & mask);
        var first = Math.Min(n, Capacity - index);
        source[..first].CopyTo(buffer.AsSpan(index, first));
        if (n > first)
            source.Slice(first, n - first).CopyTo(buffer.AsSpan(0, n - first));

        Volatile.Write(ref written, w + n);

        return n;
    }

    /// <summary>Consumer side: copies up to <paramref name="destination"/>.Length samples, oldest first, and returns how many.</summary>
    public int Read(Span<float> destination)
    {
        var r = read;
        var available = (int)(Volatile.Read(ref written) - r);
        var n = Math.Min(available, destination.Length);
        if (n <= 0)
            return 0;

        var index = (int)(r & mask);
        var first = Math.Min(n, Capacity - index);
        buffer.AsSpan(index, first).CopyTo(destination[..first]);
        if (n > first)
            buffer.AsSpan(0, n - first).CopyTo(destination.Slice(first, n - first));

        Volatile.Write(ref read, r + n);

        return n;
    }

    /// <summary>Consumer side: discards everything queued.</summary>
    public void Clear() => Volatile.Write(ref read, Volatile.Read(ref written));
}
