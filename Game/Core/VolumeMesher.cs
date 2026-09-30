using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Thornwake.Core;

public sealed class VolumeMeshData
{
    public List<Vector3> Vertices { get; } = new();
    public List<Vector3> Normals { get; } = new();
    public int DensitySamples { get; internal set; }
    public int TriangleCount => Vertices.Count / 3;
    public long BufferBytes => Vertices.Count * 24L; // positions + normals, excluding list capacity/engine copies
}

/// <summary>Small reference marching-tetrahedra mesher for backend evaluation. Not a production LOD mesher.</summary>
public static class VolumeMesher
{
    private static readonly (int X, int Y, int Z)[] Corners = CaveTerrainSampler.Corners;
    private static readonly int[,] Tetrahedra = CaveTerrainSampler.Tetrahedra;
    public static VolumeMeshData Build(CaveSpikeField field, TerrainRegion region, float spacing, bool volume, CancellationToken cancellation = default)
    {
        if (spacing is not (1 or .5f) || region.X < -2 || region.X > 1 || region.Z < -2 || region.Z > 1)
            throw new ArgumentOutOfRangeException(nameof(spacing));
        var mesh = new VolumeMeshData();
        int n = (int)(TerrainRegion.Size / spacing);
        var normals = new Dictionary<Vector3, Vector3>();
        var sums = new Dictionary<Vector3, Vector3>();
        bool emit = true;
        Vector3 Normal(Vector3 p)
        {
            if (!normals.TryGetValue(p, out var normal)) normals[p] = normal = field.Normal(p);
            return normal;
        }
        void Accumulate(Vector3 p, Vector3 u, Vector3 v, Vector3 face)
        {
            // Angle weighting keeps sliver triangles from dominating a shared vertex's lighting.
            float angle = MathF.Atan2(Vector3.Cross(u, v).Length(), Vector3.Dot(u, v));
            sums.TryGetValue(p, out var sum); sums[p] = sum + face * angle;
        }
        void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            var cross = Vector3.Cross(b - a, c - a);
            if (cross.LengthSquared() == 0) return;
            // Godot front faces are clockwise. Topology, never shading, determines winding.
            if (Vector3.Dot(cross, outward) > 0) (b, c) = (c, b);
            if (volume)
            {
                var face = -Vector3.Normalize(Vector3.Cross(b - a, c - a));
                Accumulate(a, b-a, c-a, face); Accumulate(b, c-b, a-b, face); Accumulate(c, a-c, b-c, face);
            }
            if (!emit) return;
            mesh.Vertices.Add(a); mesh.Vertices.Add(b); mesh.Vertices.Add(c);
            if (!volume) { mesh.Normals.Add(Normal(a)); mesh.Normals.Add(Normal(b)); mesh.Normals.Add(Normal(c)); }
        }
        if (!volume)
        {
            Vector3 Point(int x, int z)
            {
                float wx = region.MinX + x * spacing, wz = region.MinZ + z * spacing;
                return new(wx, field.Surface(wx, wz), wz);
            }
            for (int x = 0; x < n; x++) for (int z = 0; z < n; z++)
            {
                var a = Point(x, z); var b = Point(x + 1, z); var c = Point(x, z + 1); var d = Point(x + 1, z + 1);
                cancellation.ThrowIfCancellationRequested();
                Triangle(a, b, d, Vector3.UnitY); Triangle(a, d, c, Vector3.UnitY);
            }
            return mesh;
        }
        const float bottom = -12, top = 16;
        int ny = (int)((top - bottom) / spacing), width = n + 3;
        int Index(int x, int y, int z) => (y * width + z + 1) * width + x + 1;
        var samples = new float[width * width * (ny + 1)];
        // Height is invariant along a column; all vertical samples reuse it.
        // A one-cell halo contributes normals across region boundaries without emitting extra geometry.
        for (int z = -1; z <= n + 1; z++) for (int x = -1; x <= n + 1; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            float wx = region.MinX + x * spacing, wz = region.MinZ + z * spacing;
            float surface = field.Surface(wx, wz);
            for (int y = 0; y <= ny; y++)
                samples[Index(x, y, z)] = field.Density(new(wx, bottom + y * spacing, wz), surface);
        }
        mesh.DensitySamples = samples.Length;
        Span<Vector3> points = stackalloc Vector3[8]; Span<float> values = stackalloc float[8];
        Span<int> inside = stackalloc int[4]; Span<int> outside = stackalloc int[4];
        for (int y = 0; y < ny; y++) for (int z = -1; z <= n; z++) for (int x = -1; x <= n; x++)
        {
            if (x == 0 && z == 0) cancellation.ThrowIfCancellationRequested();
            emit = x >= 0 && x < n && z >= 0 && z < n;
            int positive = 0;
            for (int i = 0; i < 8; i++)
            {
                var c = Corners[i]; values[i] = samples[Index(x + c.X, y + c.Y, z + c.Z)];
                points[i] = new(region.MinX + (x + c.X) * spacing, bottom + (y + c.Y) * spacing, region.MinZ + (z + c.Z) * spacing);
                if (values[i] > 0) positive++;
            }
            if (positive == 0 || positive == 8) continue;
            for (int t = 0; t < 6; t++)
            {
                int ni = 0, no = 0;
                for (int j = 0; j < 4; j++) { int i = Tetrahedra[t, j]; if (values[i] > 0) inside[ni++] = i; else outside[no++] = i; }
                if (ni == 0 || ni == 4) continue;
                var outward = points[outside[0]] - points[inside[0]];
                if (ni == 1)
                    Triangle(Cross(points, values, inside[0], outside[0]), Cross(points, values, inside[0], outside[1]), Cross(points, values, inside[0], outside[2]), outward);
                else if (ni == 3)
                    Triangle(Cross(points, values, outside[0], inside[0]), Cross(points, values, outside[0], inside[1]), Cross(points, values, outside[0], inside[2]), outward);
                else
                {
                    var a = Cross(points, values, inside[0], outside[0]); var b = Cross(points, values, inside[0], outside[1]);
                    var c = Cross(points, values, inside[1], outside[0]); var d = Cross(points, values, inside[1], outside[1]);
                    Triangle(a, b, c, outward); Triangle(b, d, c, outward);
                }
            }
        }
        foreach (var p in mesh.Vertices)
        {
            var sum = sums[p];
            // Every copy uses the same normal from the actual mesh, including border contributions.
            mesh.Normals.Add(sum.LengthSquared() > 1e-12f ? Vector3.Normalize(sum) : Normal(p));
        }
        return mesh;
    }
    private static Vector3 Cross(ReadOnlySpan<Vector3> points, ReadOnlySpan<float> values, int a, int b)
    {
        // Sort global endpoints so the same edge is interpolated in the same order in adjacent blocks.
        var pa = points[a]; var pb = points[b];
        if (pa.X > pb.X || pa.X == pb.X && (pa.Y > pb.Y || pa.Y == pb.Y && pa.Z > pb.Z)) (a, b) = (b, a);
        float t = values[a] / (values[a] - values[b]);
        return points[a] + (points[b] - points[a]) * t;
    }
}








