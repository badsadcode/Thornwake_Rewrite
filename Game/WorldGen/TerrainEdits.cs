using System;
using System.Collections.Concurrent;

namespace Thornwake.WorldGen;

/// <summary>
/// Player changes to the generated ground (milestone M8, first step): a sparse height offset on a 1 m vertex grid,
/// stored in 32 m chunks and interpolated bilinearly. Unedited ground costs one empty-dictionary check per sample.
/// Thread-safe for one writer (the game) and many readers (tile builders): a chunk is never modified in place; an
/// edit builds a new array and swaps it in, so a reader always sees one consistent version of every chunk.
/// </summary>
public sealed class TerrainEdits
{
    public const int ChunkVertices = 32;
    private readonly ConcurrentDictionary<(long, long), float[]> _chunks = new();
    /// <summary>Incremented by every edit.</summary>
    public long Version { get; private set; }
    public int ChunkCount => _chunks.Count;

    private float Vertex(long x, long z)
    {
        long cx = Math.DivRem(x, ChunkVertices, out long ix), cz = Math.DivRem(z, ChunkVertices, out long iz);
        if (ix < 0) { ix += ChunkVertices; cx--; }
        if (iz < 0) { iz += ChunkVertices; cz--; }
        return _chunks.TryGetValue((cx, cz), out var chunk) ? chunk[iz * ChunkVertices + ix] : 0;
    }

    /// <summary>Height offset (m) at a world position.</summary>
    public double Delta(double x, double z)
    {
        if (_chunks.IsEmpty) return 0;
        double fx = Math.Floor(x), fz = Math.Floor(z);
        long ix = (long)fx, iz = (long)fz;
        double tx = x - fx, tz = z - fz;
        float a = Vertex(ix, iz), b = Vertex(ix + 1, iz), c = Vertex(ix, iz + 1), d = Vertex(ix + 1, iz + 1);
        if (a == 0 && b == 0 && c == 0 && d == 0) return 0;
        return a * (1 - tx) * (1 - tz) + b * tx * (1 - tz) + c * (1 - tx) * tz + d * tx * tz;
    }

    /// <summary>
    /// Raise (positive) or lower (negative) the ground around (x, z) by up to <paramref name="amount"/> metres with a
    /// smooth round brush of <paramref name="radius"/> metres. Offsets are limited to ±<paramref name="limit"/>.
    /// Returns the touched world rectangle.
    /// </summary>
    public (double MinX, double MinZ, double MaxX, double MaxZ) Apply(double x, double z, double radius, double amount, double limit = 30)
    {
        long x0 = (long)Math.Floor(x - radius), x1 = (long)Math.Ceiling(x + radius);
        long z0 = (long)Math.Floor(z - radius), z1 = (long)Math.Ceiling(z + radius);
        for (long cz = Chunk(z0); cz <= Chunk(z1); cz++)
            for (long cx = Chunk(x0); cx <= Chunk(x1); cx++)
            {
                _chunks.TryGetValue((cx, cz), out var old);
                var next = old != null ? (float[])old.Clone() : new float[ChunkVertices * ChunkVertices];
                bool changed = false;
                for (int j = 0; j < ChunkVertices; j++)
                    for (int i = 0; i < ChunkVertices; i++)
                    {
                        double vx = cx * ChunkVertices + i, vz = cz * ChunkVertices + j;
                        double r = Math.Sqrt((vx - x) * (vx - x) + (vz - z) * (vz - z)) / radius;
                        if (r >= 1) continue;
                        double falloff = DetMath.Smooth(1 - r);
                        int k = j * ChunkVertices + i;
                        next[k] = (float)Math.Clamp(next[k] + amount * falloff, -limit, limit);
                        changed = true;
                    }
                if (changed) _chunks[(cx, cz)] = next;
            }
        Version++;
        return (x0, z0, x1, z1);
    }

    public void Clear() { _chunks.Clear(); Version++; }

    private static long Chunk(long vertex) => (long)Math.Floor(vertex / (double)ChunkVertices);
}
