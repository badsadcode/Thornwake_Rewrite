using System;
using System.Globalization;

namespace Thornwake.WorldGen;

/// <summary>Integer hashing and seeded streams. Bit-compatible with the original A4 generator's streams.</summary>
public static class Hashing
{
    /// <summary>SplitMix64 finalizer.</summary>
    public static ulong Mix(ulong value)
    {
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL);
        return value ^ (value >> 31);
    }

    /// <summary>Uniform double in [0, 1) from the top 53 bits.</summary>
    public static double Unit(ulong bits) => (bits >> 11) * (1.0 / 9007199254740992.0);

    /// <summary>Hash of a 2D lattice point (same mixing as A4 value noise).</summary>
    public static ulong Lattice(long x, long z, ulong seed) =>
        Mix(unchecked((ulong)x * 0x9E3779B97F4A7C15UL ^ (ulong)z * 0xD1B54A32D192ED03UL) ^ seed);

    /// <summary>
    /// Stable identity string for generated features. Identical text to A4's <c>GenerationIdentity.FeatureId</c>
    /// so seeds derived from it reproduce A4 layouts.
    /// </summary>
    public static string FeatureId(int seed, int algorithm, string revision, string layer, long ownerX, long ownerZ, int ordinal) =>
        string.Create(CultureInfo.InvariantCulture, $"world:{seed}:{algorithm}:{revision}:{layer}:{ownerX}:{ownerZ}:{ordinal}");

    /// <summary>FNV-1a over the identity string (A4's <c>GenerationIdentity.FeatureSeed</c>).</summary>
    public static ulong FeatureSeed(int seed, int algorithm, string revision, string layer, long ownerX = 0, long ownerZ = 0, int ordinal = 0)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in FeatureId(seed, algorithm, revision, layer, ownerX, ownerZ, ordinal))
            hash = unchecked((hash ^ c) * 1099511628211UL);
        return hash;
    }
}

/// <summary>Seeded SplitMix64 stream (A4 <c>RandomStream</c>).</summary>
public sealed class RandomStream
{
    private ulong _state;
    public RandomStream(ulong seed) { _state = seed; }
    public ulong Next() { _state = unchecked(_state + 0x9E3779B97F4A7C15UL); return Hashing.Mix(_state); }
    public double Unit() => Hashing.Unit(Next());
    public double Between(double min, double max) => min + (max - min) * Unit();
    public int Int(int count) => (int)(Unit() * count);
}
