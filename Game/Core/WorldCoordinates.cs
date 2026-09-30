using System;

namespace Thornwake.Core;

/// <summary>Horizontal stream address, independent of legacy valley keys and future vertical voxel blocks.</summary>
public readonly record struct WorldRegion(long X, long Z)
{
    public const int Size = TerrainRegion.Size;
    // Explicit supported domain, rather than silently overflowing casts or losing local detail.
    public const long MaximumIndex = 1_000_000_000;
    public void Validate()
    {
        if (X < -MaximumIndex || X > MaximumIndex || Z < -MaximumIndex || Z > MaximumIndex)
            throw new ArgumentOutOfRangeException(nameof(WorldRegion));
    }
    public static WorldRegion At(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(x));
        double rx = Math.Floor(x / Size), rz = Math.Floor(z / Size);
        if (rx < -MaximumIndex || rx > MaximumIndex || rz < -MaximumIndex || rz > MaximumIndex)
            throw new ArgumentOutOfRangeException(nameof(x));
        return new((long)rx, (long)rz);
    }
    public WorldPoint Origin(double elevation = 0)
    {
        Validate();
        var point = new WorldPoint(X * (double)Size, elevation, Z * (double)Size);
        point.Validate();
        return point;
    }
}

public readonly record struct WorldPoint(double X, double Y, double Z)
{
    public void Validate()
    {
        _ = WorldRegion.At(X, Z);
        if (!double.IsFinite(Y) || Math.Abs(Y) > 1_000_000) throw new ArgumentOutOfRangeException(nameof(Y));
    }
    /// <summary>Subtract in double precision BEFORE conversion to renderer-local floats.</summary>
    public System.Numerics.Vector3 RelativeTo(WorldPoint origin)
    {
        Validate(); origin.Validate();
        double x = X - origin.X, y = Y - origin.Y, z = Z - origin.Z;
        if (Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z))) > 8192)
            throw new ArgumentOutOfRangeException(nameof(origin), "Rebase before converting distant positions to engine floats.");
        return new((float)x, (float)y, (float)z);
    }
}

/// <summary>Versioned deterministic feature identity. No mutable PRNG or process-dependent string hashes.</summary>
public sealed record GenerationIdentity(int Seed, int AlgorithmVersion, string ProfileRevision)
{
    private static void ValidateToken(string token)
    {
        if (string.IsNullOrEmpty(token) || token.Length > 64) throw new ArgumentException("Expected a stable content token.");
        foreach (char c in token)
            if (!(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.'))
                throw new ArgumentException("Content tokens use lowercase ASCII letters, digits, dash, underscore or dot.");
    }
    public string FeatureId(string layer, WorldRegion owner, int ordinal)
    {
        owner.Validate(); ValidateToken(ProfileRevision); ValidateToken(layer);
        if (AlgorithmVersion < 1 || ordinal < 0) throw new ArgumentOutOfRangeException(nameof(ordinal));
        return FormattableString.Invariant($"world:{Seed}:{AlgorithmVersion}:{ProfileRevision}:{layer}:{owner.X}:{owner.Z}:{ordinal}");
    }
    // Specified FNV-1a over the validated ASCII identity; suitable for independent generation streams.
    // The full string (not this hash) is the persistent identity, so hash collisions cannot merge state.
    public ulong FeatureSeed(string layer, WorldRegion owner, int ordinal)
    {
        ulong hash = 14695981039346656037UL;
        foreach (char c in FeatureId(layer, owner, ordinal)) hash = unchecked((hash ^ c) * 1099511628211UL);
        return hash;
    }
}
