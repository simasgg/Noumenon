using Noumenon.Dsp.Sampler;
using Noumenon.Dsp.Shared;

namespace Noumenon.Engine.Samples;

/// <summary>
/// The sample map: 128 numbered keys (MF's map keys, Fabrications used 74–103) each holding at most
/// one <see cref="SampleSlot"/>. Loading and decoding happen on whatever thread calls
/// <see cref="Load"/> — never the audio thread, which only ever does a volatile read of a slot
/// reference at its control tick and crossfades to whatever it finds there. The standalone runs
/// loads on a worker; the harness and tests call them inline. Unlike MF, adding a sample never
/// rebuilds anything: a key is simply set or cleared.
/// </summary>
public sealed class SampleMap
{
    public const int KeyCount = 128;

    private readonly SampleSlot?[] slots = new SampleSlot?[KeyCount];

    /// <summary>The slot at a key, or null; safe to call from the audio thread.</summary>
    public SampleSlot? Get(int key) => (uint)key < KeyCount ? Volatile.Read(ref slots[key]) : null;

    /// <summary>Decodes a file and publishes it under <paramref name="key"/>.</summary>
    public SampleSlot Load(int key, string path, float gain = 1f, int rootNote = Tuning.MiddleC)
    {
        var data = SampleDecoder.Decode(path);

        return Set(key, data, gain, rootNote, path);
    }

    /// <summary>Publishes already-decoded data (generated, or decoded by the caller) under <paramref name="key"/>.</summary>
    public SampleSlot Set(int key, SampleData data, float gain = 1f, int rootNote = Tuning.MiddleC, string? path = null)
    {
        ValidateKey(key);
        var slot = new SampleSlot(key, data.Name, path, data, gain < 0f ? 0f : gain, Math.Clamp(rootNote, 0, 127));
        Volatile.Write(ref slots[key], slot);

        return slot;
    }

    /// <summary>Replaces a slot's settings, keeping its audio; returns null if the key is empty.</summary>
    public SampleSlot? Adjust(int key, float? gain = null, int? rootNote = null)
    {
        ValidateKey(key);
        var current = Volatile.Read(ref slots[key]);
        if (current is null)
            return null;

        var slot = current with { Gain = gain is { } g ? (g < 0f ? 0f : g) : current.Gain, RootNote = rootNote is { } n ? Math.Clamp(n, 0, 127) : current.RootNote };
        Volatile.Write(ref slots[key], slot);

        return slot;
    }

    public void Clear(int key)
    {
        ValidateKey(key);
        Volatile.Write(ref slots[key], null);
    }

    public void ClearAll()
    {
        for (var i = 0; i < KeyCount; i++)
            Volatile.Write(ref slots[i], null);
    }

    public int Count
    {
        get
        {
            var count = 0;
            for (var i = 0; i < KeyCount; i++)
            {
                if (Volatile.Read(ref slots[i]) is not null)
                    count++;
            }

            return count;
        }
    }

    public IEnumerable<SampleSlot> Loaded
    {
        get
        {
            for (var i = 0; i < KeyCount; i++)
            {
                if (Volatile.Read(ref slots[i]) is { } slot)
                    yield return slot;
            }
        }
    }

    private static void ValidateKey(int key)
    {
        if ((uint)key >= KeyCount)
            throw new ArgumentOutOfRangeException(nameof(key), key, $"Sample keys run from 0 to {KeyCount - 1}.");
    }
}
