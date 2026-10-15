using Noumenon.Engine.Parameters;

namespace Noumenon.Vst3.Parameters;

/// <summary>
/// The normalized value of every table parameter that the host and the plugin last agreed on,
/// shared by the audio thread and the editor's poll so the two directions of traffic never fight.
/// The panel writes the engine's bank directly and the poll tells the host; VST3 then forwards each
/// of those edits to the processor a block or two later, by when the panel may have moved on. So
/// the poll remembers its last few pushes per parameter, and the processor recognises a value it
/// receives within half a second of such a push as its own echo and leaves the bank alone. Any
/// other value — automation, the host's own editor — is news: it is applied and becomes the agreed
/// value, so the poll, which only pushes a bank value that no longer means the agreed one, never
/// sends a host's value back to it. Each slot is one aligned 8-byte word, written and read with
/// volatile semantics; nothing here allocates.
/// </summary>
internal sealed class HostValues
{
    private const int History = 4;
    private const long EchoWindowMs = 500;
    private const long NoPush = long.MinValue;

    /// <summary>How close a value must be to a push to be its echo; a host that keeps values as 32-bit floats rounds them.</summary>
    private const double EchoTolerance = 1e-6;

    private const double PushTolerance = 1e-5;

    private readonly double[] agreed;
    private readonly double[] pushes;
    private readonly long[] pushTimes;
    private readonly int[] nextPush;

    public HostValues()
    {
        var all = ParameterTable.All;
        agreed = new double[all.Count];
        pushes = new double[all.Count * History];
        pushTimes = new long[all.Count * History];
        nextPush = new int[all.Count];
        Array.Fill(pushTimes, NoPush);
        foreach (var info in all)
            agreed[info.Index] = info.ToNormalized(info.Default);
    }

    public double Agreed(ParamId id) => Volatile.Read(ref agreed[(int)id]);

    /// <summary>
    /// Audio thread, with the block's clock (<see cref="Environment.TickCount64"/>): false when the
    /// value is the echo of a push made within the window (the echo is consumed), true when it is
    /// news and has become the agreed value.
    /// </summary>
    public bool Accept(ParamId id, double normalized, long nowMs)
    {
        var index = (int)id;
        var first = index * History;
        for (var slot = first; slot < first + History; slot++)
        {
            var time = Volatile.Read(ref pushTimes[slot]);
            if (time == NoPush || nowMs - time > EchoWindowMs || Math.Abs(Volatile.Read(ref pushes[slot]) - normalized) > EchoTolerance)
                continue;

            Interlocked.CompareExchange(ref pushTimes[slot], NoPush, time);
            return false;
        }

        Volatile.Write(ref agreed[index], normalized);

        return true;
    }

    /// <summary>
    /// UI thread: true when the bank value no longer means the agreed one (the same step of a
    /// discrete parameter, the same continuous value within a hair); <paramref name="normalized"/> is
    /// then sent to the host and recorded as agreed and as a recent push.
    /// </summary>
    public bool TryPush(ParamInfo info, float bankValue, long nowMs, out double normalized)
    {
        var index = info.Index;
        var current = Volatile.Read(ref agreed[index]);
        normalized = info.ToNormalized(bankValue);
        if (info.IsDiscrete ? info.FromNormalized((float)current) == info.Clamp(bankValue) : Math.Abs(normalized - current) <= PushTolerance)
            return false;

        Volatile.Write(ref agreed[index], normalized);
        var slot = index * History + nextPush[index];
        nextPush[index] = (nextPush[index] + 1) % History;
        Volatile.Write(ref pushes[slot], normalized);
        Volatile.Write(ref pushTimes[slot], nowMs);

        return true;
    }

    /// <summary>UI thread: makes a value the agreed one without sending it — a snapshot the host reads back by itself.</summary>
    public void Agree(ParamInfo info, float bankValue) => Volatile.Write(ref agreed[info.Index], info.ToNormalized(bankValue));
}
