using System.Numerics;
using System.Runtime.CompilerServices;

namespace Noumenon.Dsp.Shared;

/// <summary>
/// xoshiro128** — a small, fast, seedable PRNG. It feeds the noise oscillators on the audio thread
/// (a few operations per sample, no allocation) and the randomizer off it, and because every
/// consumer reseeds from a stored value, the same seed always reproduces the same render.
/// <see cref="System.Random"/> is never used in the engine: it is not guaranteed stable across
/// runtimes and cannot be reseeded in place.
/// </summary>
public sealed class Xoshiro128
{
    private uint s0, s1, s2, s3;

    public Xoshiro128(ulong seed) => Reseed(seed);

    /// <summary>Expands a 64-bit seed into the 128-bit state with SplitMix64, so nearby seeds diverge immediately.</summary>
    public void Reseed(ulong seed)
    {
        var x = seed;
        s0 = (uint)SplitMix(ref x);
        s1 = (uint)SplitMix(ref x);
        s2 = (uint)SplitMix(ref x);
        s3 = (uint)SplitMix(ref x);
        if ((s0 | s1 | s2 | s3) == 0)
            s0 = 1;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint Next()
    {
        var result = BitOperations.RotateLeft(s1 * 5, 7) * 9;
        var t = s1 << 9;

        s2 ^= s0;
        s3 ^= s1;
        s1 ^= s2;
        s0 ^= s3;
        s2 ^= t;
        s3 = BitOperations.RotateLeft(s3, 11);

        return result;
    }

    /// <summary>Uniform in [0, 1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextFloat() => (Next() >> 8) * (1f / 16777216f);

    /// <summary>Uniform in [-1, 1).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public float NextBipolar() => (Next() >> 8) * (2f / 16777216f) - 1f;

    /// <summary>Uniform in [<paramref name="min"/>, <paramref name="max"/>).</summary>
    public float NextRange(float min, float max) => min + NextFloat() * (max - min);

    /// <summary>Uniform integer in [0, <paramref name="maxExclusive"/>).</summary>
    public int NextInt(int maxExclusive) => maxExclusive <= 1 ? 0 : (int)(NextFloat() * maxExclusive);

    /// <summary>True with the given probability.</summary>
    public bool Chance(float probability) => NextFloat() < probability;

    private static ulong SplitMix(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        var z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;

        return z ^ (z >> 31);
    }
}
