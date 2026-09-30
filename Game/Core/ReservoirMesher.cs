using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Thornwake.Core;

public sealed class ReservoirMeshData
{
    public List<Vector3> Vertices { get; } = new();
    public List<float> Depths { get; } = new();
    public List<float> ShoreDistances { get; } = new();
}

/// <summary>Water/terrain intersections share the rendered terrain's tetrahedra and interpolation.</summary>
public static class ReservoirMesher
{
    public static float Clearance(CaveSpikeField field, LocalReservoir pool, Vector3 p) =>
        Math.Min(-field.Density(p), pool.Radius - Vector2.Distance(new(p.X,p.Z), new(pool.X,pool.Z)));
    private readonly record struct Point(Vector3 Position, float Density);
    public static ReservoirMeshData Build(CaveSpikeField field, LocalReservoir pool, CancellationToken cancellation = default, float spacing = 1)
    {
        var sampler = new CaveTerrainSampler(field, spacing);
        var mesh = new ReservoirMeshData();
        int x0 = (int)MathF.Floor((pool.X-pool.Radius)/spacing), x1 = (int)MathF.Ceiling((pool.X+pool.Radius)/spacing);
        int z0 = (int)MathF.Floor((pool.Z-pool.Radius)/spacing), z1 = (int)MathF.Ceiling((pool.Z+pool.Radius)/spacing);
        for (int z = z0; z < z1; z++) for (int x = x0; x < x1; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            AppendCell(mesh, sampler, x, z, pool.Level, pool.Bottom, p => pool.Radius - Vector2.Distance(new(p.X,p.Z), new(pool.X,pool.Z)));
        }
        return mesh;
    }
    // This can also skin independently evolving water cells, without changing the terrain representation.
    public static void AppendCell(ReservoirMeshData mesh, CaveTerrainSampler sampler, int x, int z, float level, float bottom, Func<Vector3,float>? boundary = null)
    {
        float step = sampler.Spacing;
        int y = (int)MathF.Floor(level/step);
        var corners = CaveTerrainSampler.Corners; var tetra = CaveTerrainSampler.Tetrahedra;
        var points = new Point[8];
        for (int i = 0; i < 8; i++)
        {
            var c = corners[i];
            points[i] = new(new((x+c.X)*step, (y+c.Y)*step, (z+c.Z)*step), sampler.Grid(x+c.X,y+c.Y,z+c.Z));
        }
        for (int t = 0; t < 6; t++)
        {
            var polygon = new List<Point>(4);
            for (int a = 0; a < 4; a++) for (int b = a + 1; b < 4; b++)
            {
                var p = points[tetra[t,a]]; var q = points[tetra[t,b]];
                if (p.Position.Y > q.Position.Y) (p,q) = (q,p);
                // Half-open Y interval avoids emitting a horizontal face twice at exact grid heights.
                if (p.Position.Y == q.Position.Y || level < p.Position.Y || level >= q.Position.Y) continue;
                float ratio = (level-p.Position.Y)/(q.Position.Y-p.Position.Y);
                var position = p.Position + (q.Position-p.Position)*ratio; position.Y = level;
                var hit = new Point(position, p.Density + (q.Density-p.Density)*ratio);
                if (!polygon.Any(v => v.Position == hit.Position)) polygon.Add(hit);
            }
            if (polygon.Count < 3) continue;
            var center = polygon.Aggregate(Vector3.Zero, (sum,p) => sum+p.Position)/polygon.Count;
            polygon.Sort((a,b) => MathF.Atan2(a.Position.Z-center.Z,a.Position.X-center.X).CompareTo(MathF.Atan2(b.Position.Z-center.Z,b.Position.X-center.X)));
            polygon = Clip(polygon, p => -p.Density);
            if (boundary != null) polygon = Clip(polygon, p => boundary(p.Position));
            for (int i = 1; i < polygon.Count-1; i++)
            {
                var a = polygon[0]; var b = polygon[i]; var c = polygon[i+1];
                if (Vector3.Cross(b.Position-a.Position,c.Position-a.Position).LengthSquared() == 0) continue;
                Vertex(a); Vertex(b); Vertex(c);
            }
        }
        void Vertex(Point p)
        {
            float depth = 0;
            for (float d = .1f; d <= level-bottom; d += .1f)
            {
                if (sampler.Density(p.Position-Vector3.UnitY*d) >= 0) break;
                depth = d;
            }
            mesh.Vertices.Add(p.Position); mesh.Depths.Add(depth);
            mesh.ShoreDistances.Add(Math.Max(0, Math.Min(-p.Density,boundary?.Invoke(p.Position) ?? float.MaxValue)));
        }
    }
    private static List<Point> Clip(List<Point> input, Func<Point,float> distance)
    {
        var result = new List<Point>(6);
        for (int i = 0; i < input.Count; i++)
        {
            var a = input[i]; var b = input[(i+1)%input.Count]; float da = distance(a), db = distance(b);
            if (da >= 0) result.Add(a);
            if ((da >= 0) == (db >= 0)) continue;
            // Canonical interpolation across adjacent tetrahedra.
            if (Compare(a.Position,b.Position) > 0) { (a,b)=(b,a); (da,db)=(db,da); }
            float t = da/(da-db);
            result.Add(new(a.Position+(b.Position-a.Position)*t, a.Density+(b.Density-a.Density)*t));
        }
        return result;
    }
    private static int Compare(Vector3 a, Vector3 b) => a.X != b.X ? a.X.CompareTo(b.X) : a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z.CompareTo(b.Z);
}

