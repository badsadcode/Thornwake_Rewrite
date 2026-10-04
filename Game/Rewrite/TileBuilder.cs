using System;
using System.Collections.Generic;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>Address of a terrain tile in the nested LOD grid. Level 0 tiles are <see cref="TileBuilder.BaseTile"/> metres.</summary>
public readonly record struct TileKey(int Level, long X, long Z)
{
    public double Size => TileBuilder.BaseTile * (double)(1L << Level);
    public double MinX => X * Size;
    public double MinZ => Z * Size;
}

/// <summary>A tree to draw: species index into <see cref="TreeCatalog"/>, local transform.</summary>
public readonly record struct TreeInstance(int Species, Transform3D Transform);

/// <summary>CPU-side tile data, built on a worker thread; engine resources are created on the main thread.</summary>
public sealed class TileData
{
    public required TileKey Key { get; init; }
    public required Vector3[] Vertices { get; init; }
    public required Vector3[] Normals { get; init; }
    public required Color[] Colors { get; init; }
    public required Vector2[] Uv { get; init; }
    public required Vector2[] Uv2 { get; init; }
    public required int[] Indices { get; init; }
    /// <summary>Water surface mesh (null when the tile is dry).</summary>
    public (Vector3[] Vertices, Color[] Colors, int[] Indices)? Water { get; set; }
    /// <summary>Triangle soup for a ConcavePolygonShape3D (level 0 only).</summary>
    public Vector3[]? CollisionFaces { get; init; }
    public List<TreeInstance> Trees { get; init; } = new();
    public double BuildMs { get; set; }
}

/// <summary>
/// Builds one LOD tile from the deterministic <see cref="TerrainSampler"/>: a 33×33 grid with skirts (hides cracks
/// between LOD levels), per-vertex normals from a one-sample border, a water surface for ocean/lake/river cells,
/// collision faces for the nearest level, and tree placements on world-aligned cells (so trees never move when a
/// tile changes LOD). Positions are relative to the tile's minimum corner; heights are absolute.
/// </summary>
public static class TileBuilder
{
    public const double BaseTile = 64;
    public const int Cells = 32, Side = Cells + 1;
    /// <summary>Levels at which trees are drawn (0 = nearest); beyond, the terrain shader paints forest colour.</summary>
    public const int TreeLevels = 3;

    public static TileData Build(TerrainSampler sampler, TileKey key)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        double size = key.Size, spacing = size / Cells, x0 = key.MinX, z0 = key.MinZ;
        double minWavelength = spacing * 2;
        int s2 = Side + 2; // one-sample border for normals
        var heights = new double[s2 * s2];
        var samples = new TerrainSample[Side * Side];
        var forest = new double[Side * Side];
        for (int j = 0; j < s2; j++)
            for (int i = 0; i < s2; i++)
            {
                double x = x0 + (i - 1) * spacing, z = z0 + (j - 1) * spacing;
                var t = sampler.Sample(x, z, minWavelength);
                heights[j * s2 + i] = t.Height;
                if (i >= 1 && j >= 1 && i <= Side && j <= Side) { samples[(j - 1) * Side + i - 1] = t; forest[(j - 1) * Side + i - 1] = Vegetation.ForestDensity(sampler, x, z, t); }
            }

        double sea = sampler.Plan.Profile.SeaLevel;
        int skirt = Side * 4;
        var vertices = new Vector3[Side * Side + skirt];
        var normals = new Vector3[vertices.Length];
        var colors = new Color[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var uv2 = new Vector2[vertices.Length];
        for (int j = 0; j < Side; j++)
            for (int i = 0; i < Side; i++)
            {
                int k = j * Side + i, b = (j + 1) * s2 + i + 1;
                double h = heights[b];
                vertices[k] = new Vector3((float)(i * spacing), (float)h, (float)(j * spacing));
                double dx = heights[b + 1] - heights[b - 1], dz = heights[b + s2] - heights[b - s2];
                normals[k] = new Vector3((float)-dx, (float)(2 * spacing), (float)-dz).Normalized();
                var t = samples[k];
                // B: beach (dry ground just above the sea); A: submerged ground (river/lake/sea bed).
                double beach = t.Water != WaterKind.None ? 0 : DetMath.Clamp(1 - (h - sea) / 1.4, 0, 1);
                colors[k] = new Color((float)forest[k], (float)t.Mountain, (float)beach, t.Water != WaterKind.None ? 1 : 0);
                // World xz folded into a 4 km period keeps float precision for shader noise far from the origin.
                uv[k] = new Vector2((float)Fold(x0 + i * spacing), (float)Fold(z0 + j * spacing));
                uv2[k] = new Vector2((float)(h - sea), 0);
            }
        // Skirts: a copy of each edge pushed down, so a coarser neighbour's edge never shows a gap.
        float drop = (float)(2 + spacing * 1.5);
        int[] edge = new int[skirt];
        for (int i = 0; i < Side; i++)
        {
            edge[i] = i;                               // z = 0
            edge[Side + i] = (Side - 1) * Side + i;    // z = max
            edge[2 * Side + i] = i * Side;             // x = 0
            edge[3 * Side + i] = i * Side + Side - 1;  // x = max
        }
        for (int e = 0; e < skirt; e++)
        {
            int k = Side * Side + e, src = edge[e];
            vertices[k] = vertices[src] - new Vector3(0, drop, 0);
            normals[k] = normals[src]; colors[k] = colors[src]; uv[k] = uv[src]; uv2[k] = uv2[src];
        }
        var indices = new List<int>(Cells * Cells * 6 + skirt * 6);
        for (int j = 0; j < Cells; j++)
            for (int i = 0; i < Cells; i++)
            {
                int a = j * Side + i, b = a + 1, c = a + Side, d = c + 1;
                // Godot front faces are clockwise seen from the front (above).
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(b); indices.Add(d); indices.Add(c);
            }
        for (int side = 0; side < 4; side++)
            for (int i = 0; i < Cells; i++)
            {
                int top0 = edge[side * Side + i], top1 = edge[side * Side + i + 1];
                int bot0 = Side * Side + side * Side + i, bot1 = bot0 + 1;
                // Double-sided strip (skirts are seen from either side depending on the neighbour).
                indices.Add(top0); indices.Add(bot0); indices.Add(top1);
                indices.Add(top1); indices.Add(bot0); indices.Add(bot1);
                indices.Add(top0); indices.Add(top1); indices.Add(bot0);
                indices.Add(top1); indices.Add(bot1); indices.Add(bot0);
            }

        var data = new TileData
        {
            Key = key, Vertices = vertices, Normals = normals, Colors = colors, Uv = uv, Uv2 = uv2, Indices = indices.ToArray(),
            CollisionFaces = key.Level == 0 ? Faces(vertices) : null,
            Trees = key.Level < TreeLevels ? Vegetation.Trees(sampler, key) : new List<TreeInstance>(),
        };
        BuildWater(data, samples, heights, s2, spacing);
        data.BuildMs = timer.Elapsed.TotalMilliseconds;
        return data;

        Vector3[] Faces(Vector3[] v)
        {
            var faces = new Vector3[Cells * Cells * 6];
            int f = 0;
            for (int j = 0; j < Cells; j++)
                for (int i = 0; i < Cells; i++)
                {
                    int a = j * Side + i, b = a + 1, c = a + Side, d = c + 1;
                    faces[f++] = v[a]; faces[f++] = v[b]; faces[f++] = v[c];
                    faces[f++] = v[b]; faces[f++] = v[d]; faces[f++] = v[c];
                }
            return faces;
        }
    }

    private static double Fold(double v) { double p = 4096; return v - Math.Floor(v / p) * p; }

    /// <summary>
    /// Water surface on the same grid: wet vertices sit at their water level; dry vertices next to water take the
    /// neighbouring level, so the plane runs under the bank and the visible shoreline is the exact terrain
    /// intersection. Colour: R = depth / 20 m, G = kind (0 sea, .5 lake, 1 river).
    /// </summary>
    private static void BuildWater(TileData data, TerrainSample[] samples, double[] heights, int s2, double spacing)
    {
        int n = Side;
        var wet = new bool[n * n];
        bool any = false;
        for (int k = 0; k < n * n; k++) { wet[k] = samples[k].Water != WaterKind.None; any |= wet[k]; }
        if (!any) return;
        var vertices = new Vector3[n * n];
        var colors = new Color[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                double level = double.NaN, kind = 0;
                if (wet[k]) { level = samples[k].WaterLevel; kind = Kind(samples[k].Water); }
                else
                {
                    for (int b = -1; b <= 1; b++)
                        for (int a = -1; a <= 1; a++)
                        {
                            int x = i + a, z = j + b;
                            if (x < 0 || z < 0 || x >= n || z >= n || !wet[z * n + x]) continue;
                            double l = samples[z * n + x].WaterLevel;
                            if (double.IsNaN(level) || l > level) { level = l; kind = Kind(samples[z * n + x].Water); }
                        }
                    if (double.IsNaN(level)) level = data.Vertices[k].Y - 1; // unused vertex, keep it under ground
                }
                double ground = heights[(j + 1) * s2 + i + 1];
                vertices[k] = new Vector3(i * (float)spacing, (float)level, j * (float)spacing);
                colors[k] = new Color((float)DetMath.Clamp((level - ground) / 20, 0, 1), (float)kind, 0, 1);
            }
        var indices = new List<int>();
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                if (!(wet[a] || wet[b] || wet[c] || wet[d])) continue;
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(b); indices.Add(d); indices.Add(c);
            }
        data.Water = (vertices, colors, indices.ToArray());
    }

    private static double Kind(WaterKind kind) => kind switch { WaterKind.Lake => .5, WaterKind.River => 1, _ => 0 };
}
