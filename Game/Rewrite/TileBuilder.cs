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

/// <summary>
/// What a tile build places and draws, captured when the build is scheduled (worker threads never read live
/// settings). Forest, rocks and props come from the world itself (TerrainStyle, fixed at generation: trees, rocks and
/// logs are world objects, not decoration); grass and the draw distances come from the player's settings.
/// </summary>
public readonly record struct DetailConfig(double Grass, double ForestCover, double ForestDensity, double Rocks, double Props,
    int TreeLevels, int FarTreeLevels, bool SimulatedWater = true)
{
    public static DetailConfig For(TerrainSampler sampler)
    {
        var style = sampler.Plan.Style;
        return new(GameSettings.GrassDensity.Value, style.ForestCover, style.ForestDensity, style.RockAmount, style.PropAmount,
            2 + GameSettings.TreeDistance.Index, 4 + GameSettings.FarTreeDistance.Index, GameSettings.WaterSimulation.On);
    }
}

/// <summary>CPU-side tile data, built on a worker thread; engine resources are created on the main thread.</summary>
public sealed class TileData
{
    public required TileKey Key { get; init; }
    /// <summary>Vertices per tile side (the nearest level is finer).</summary>
    public required int Side { get; init; }
    public required Vector3[] Vertices { get; init; }
    public required Vector3[] Normals { get; init; }
    public required Color[] Colors { get; init; }
    public required Vector2[] Uv { get; init; }
    public required Vector2[] Uv2 { get; init; }
    public required int[] Indices { get; init; }
    /// <summary>Per vertex biome data (D42), CUSTOM0..3: grass rgb + snow; dry rgb + plant cover; map colour rgb
    /// (debug view) + rock exposure; rock rgb + wetness.</summary>
    public required float[] Biome0 { get; init; }
    public required float[] Biome1 { get; init; }
    public required float[] Biome2 { get; init; }
    public required float[] Biome3 { get; init; }
    /// <summary>Water surface mesh (null when the tile is dry).</summary>
    public (Vector3[] Vertices, Color[] Colors, Vector2[] Flow, int[] Indices)? Water { get; set; }
    /// <summary>Triangle soup for a ConcavePolygonShape3D (level 0 only).</summary>
    public Vector3[]? CollisionFaces { get; init; }
    public List<TreeInstance> Trees { get; init; } = new();
    public List<Transform3D> Grass { get; set; } = new();
    public List<Transform3D> Reeds { get; set; } = new();
    public List<RockInstance> Rocks { get; set; } = new();
    public List<RockInstance> Props { get; set; } = new();
    public List<FarTree> FarTrees { get; set; } = new();
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
    /// <summary>Cells across a tile: the nearest level is twice as fine (1 m vertices: smoother shorelines, small creeks).</summary>
    public static int CellsFor(int level) => level == 0 ? 64 : Cells;

    private static (float R, float G, float B) Mix((float R, float G, float B) a, (float R, float G, float B) b, float sa, float sb, float scale) =>
        ((a.R * sa + b.R * sb) * scale, (a.G * sa + b.G * sb) * scale, (a.B * sa + b.B * sb) * scale);

    private static void Put(float[] target, int k, (float R, float G, float B) c, double a)
    {
        target[k * 4] = c.R; target[k * 4 + 1] = c.G; target[k * 4 + 2] = c.B; target[k * 4 + 3] = (float)a;
    }
    public static TileData Build(TerrainSampler sampler, TileKey key, DetailConfig config)
    {
        int Cells = CellsFor(key.Level), Side = Cells + 1;
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
                if (i >= 1 && j >= 1 && i <= Side && j <= Side) { samples[(j - 1) * Side + i - 1] = t; forest[(j - 1) * Side + i - 1] = Vegetation.ForestDensity(sampler, x, z, t, config.ForestCover); }
            }

        double sea = sampler.Plan.Profile.SeaLevel;
        // Submerged-bed tint from the signed depth below the nearest water surface (dry vertices beside water use the
        // neighbouring level): interpolates to exactly 0.5 at the waterline, so the tint follows the real shoreline
        // instead of colouring whole triangles.
        var submerged = new float[Side * Side];
        for (int j = 0; j < Side; j++)
            for (int i = 0; i < Side; i++)
            {
                int k = j * Side + i;
                double level = samples[k].Water != WaterKind.None ? samples[k].WaterLevel : double.NaN;
                if (double.IsNaN(level))
                    for (int b = -1; b <= 1; b++)
                        for (int a = -1; a <= 1; a++)
                        {
                            int x = i + a, z = j + b;
                            if (x < 0 || z < 0 || x >= Side || z >= Side || samples[z * Side + x].Water == WaterKind.None) continue;
                            double l = samples[z * Side + x].WaterLevel;
                            if (double.IsNaN(level) || l > level) level = l;
                        }
                submerged[k] = double.IsNaN(level) ? 0 : (float)DetMath.Clamp(.5 + (level - samples[k].Height) * .5, 0, 1);
            }
        int skirt = Side * 4;
        var vertices = new Vector3[Side * Side + skirt];
        var normals = new Vector3[vertices.Length];
        var colors = new Color[vertices.Length];
        var uv = new Vector2[vertices.Length];
        var uv2 = new Vector2[vertices.Length];
        var biome0 = new float[vertices.Length * 4];
        var biome1 = new float[vertices.Length * 4];
        var biome2 = new float[vertices.Length * 4];
        var biome3 = new float[vertices.Length * 4];
        var props = new float[Side * Side * 2]; // biome rock and bush/log multipliers (ground cover only, no skirts)
        var climate = sampler.Plan.Climate;
        var biomes = sampler.Plan.Biomes;
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
                // Beach: a band along the sea (low land influence) and only a few metres above it.
                double beach = t.Water != WaterKind.None ? 0 : (1 - DetMath.Smooth(t.Land / .0025)) * (1 - DetMath.Smooth((h - sea) / 2.4));
                colors[k] = new Color((float)forest[k], (float)t.Mountain, (float)beach, submerged[k]);
                // World xz folded into a 4 km period keeps float precision for shader noise far from the origin.
                uv[k] = new Vector2((float)Fold(x0 + i * spacing), (float)Fold(z0 + j * spacing));
                // UV2.y: how much the player has dug or raised here (bare soil in the shader).
                double edited = Math.Abs(sampler.Edits.Delta(x0 + i * spacing, z0 + j * spacing));
                uv2[k] = new Vector2((float)(h - sea), (float)Math.Min(1, edited * 2));
                // Biome colours, blended between the two best-fitting biomes; snow from the biome and the cold.
                double wx = x0 + i * spacing, wz = z0 + j * spacing;
                var (ba, bb, share) = biomes.Sample(wx, wz);
                ResolvedBiome A = biomes.Pool[ba], B = biomes.Pool[bb];
                float sa = (float)share, sb = 1 - sa;
                float dark = (float)(1 - .35 * (A.Def.Appearance.Darkness * share + B.Def.Appearance.Darkness * (1 - share)));
                double temperature = climate.At(wx, wz, h - sea).Temperature;
                double snow = climate.Snow(A.Snow * share + B.Snow * (1 - share), temperature, h - sea);
                double plants = A.Def.Vegetation.Grass * share + B.Def.Vegetation.Grass * (1 - share);
                Put(biome0, k, Mix(A.Grass, B.Grass, sa, sb, dark), snow);
                Put(biome1, k, Mix(A.Dry, B.Dry, sa, sb, dark), Math.Min(1, plants));
                Put(biome2, k, Mix(A.Map, B.Map, sa, sb, 1), A.Def.Appearance.RockExposure * share + B.Def.Appearance.RockExposure * (1 - share));
                Put(biome3, k, Mix(A.Rock, B.Rock, sa, sb, 1), A.Def.Water.Wetness * share + B.Def.Water.Wetness * (1 - share));
                props[k * 2] = (float)(A.Def.Props.Rocks * share + B.Def.Props.Rocks * (1 - share));
                props[k * 2 + 1] = (float)(A.Def.Props.Logs * share + B.Def.Props.Logs * (1 - share));
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
            for (int c = 0; c < 4; c++)
            {
                biome0[k * 4 + c] = biome0[src * 4 + c]; biome1[k * 4 + c] = biome1[src * 4 + c];
                biome2[k * 4 + c] = biome2[src * 4 + c]; biome3[k * 4 + c] = biome3[src * 4 + c];
            }
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
            Key = key, Side = Side, Vertices = vertices, Normals = normals, Colors = colors, Uv = uv, Uv2 = uv2, Indices = indices.ToArray(),
            Biome0 = biome0, Biome1 = biome1, Biome2 = biome2, Biome3 = biome3,
            CollisionFaces = key.Level == 0 ? Faces(vertices) : null,
            // Full trees on the nearest levels; beyond, tree cards, then the terrain shader's forest tint.
            Trees = key.Level < config.TreeLevels ? Vegetation.Trees(sampler, key, config) : new List<TreeInstance>(),
        };
        BuildWater(data, samples, heights, s2, spacing, Side, config.SimulatedWater);
        var grid = new GroundCover.Grid(vertices, normals, forest, samples, spacing, x0, z0, Side, submerged, uv2, biome1, props);
        ulong coverSeed = Hashing.Mix((ulong)sampler.Plan.Manifest.Seed + 0xC0FFEEUL);
        if (key.Level == 0 && config.Grass > 0) data.Grass = GroundCover.Grass(key, grid, sea, coverSeed, config.Grass);
        if (key.Level == 0 && config.Grass > 0) data.Reeds = GroundCover.Reeds(key, grid, sea, coverSeed, config.Grass);
        if (key.Level <= 2 && config.Rocks > 0) data.Rocks = GroundCover.Rocks(key, grid, sea, coverSeed, config.Rocks);
        if (key.Level <= 1 && config.Props > 0) data.Props = GroundCover.Props(key, grid, sea, coverSeed, config.Props);
        if (key.Level >= config.TreeLevels && key.Level <= config.FarTreeLevels)
            data.FarTrees = GroundCover.FarTrees(key, grid, sea, coverSeed, config.ForestDensity);
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
    private static void BuildWater(TileData data, TerrainSample[] samples, double[] heights, int s2, double spacing, int n, bool simulated)
    {
        var wet = new bool[n * n];
        bool any = false;
        for (int k = 0; k < n * n; k++) { wet[k] = samples[k].Water != WaterKind.None; any |= wet[k]; }
        if (!any) return;
        var vertices = new Vector3[n * n];
        var colors = new Color[n * n];
        // Ground the player dug (below its generated height) is left to the local water simulation: the river's
        // sheet must not be stretched over it (that drew gaps and overlapping sheets in dug river banks).
        var dug = new bool[n * n];
        // Without the simulation, dug ground next to water simply fills at the water's level (the sheet covers it).
        for (int k = 0; k < n * n; k++) dug[k] = simulated && !wet[k] && data.Uv2[k].Y > .15f;
        // The sea's sheet also reaches ~14 m under the beach, so the swash can run up and down it.
        var under = new int[n * n];
        Array.Fill(under, int.MaxValue);
        var front = new List<int>();
        for (int k = 0; k < n * n; k++) if (wet[k] && samples[k].Water == WaterKind.Ocean) { under[k] = 0; front.Add(k); }
        int reach = Math.Max(1, (int)Math.Ceiling(14 / spacing));
        for (int step = 1; step <= reach && front.Count > 0; step++)
        {
            var next = new List<int>();
            foreach (int k in front)
            {
                int i = k % n, j = k / n;
                for (int b = -1; b <= 1; b++)
                    for (int a = -1; a <= 1; a++)
                    {
                        int x = i + a, z = j + b;
                        if (x < 0 || z < 0 || x >= n || z >= n) continue;
                        int m = z * n + x;
                        if (wet[m] || dug[m] || under[m] <= step) continue;
                        under[m] = step; next.Add(m);
                    }
            }
            front = next;
        }
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                double level = double.NaN, kind = 0, plume = 0;
                if (wet[k]) { level = samples[k].WaterLevel; kind = Kind(samples[k].Water); plume = samples[k].Plume; }
                else
                {
                    // Average of the wet neighbours (the maximum lifted the sheet over the downstream bank of steep
                    // creeks, where the upstream neighbour is higher).
                    double sum = 0; int count = 0;
                    for (int b = -1; b <= 1; b++)
                        for (int a = -1; a <= 1; a++)
                        {
                            int x = i + a, z = j + b;
                            if (x < 0 || z < 0 || x >= n || z >= n || !wet[z * n + x]) continue;
                            sum += samples[z * n + x].WaterLevel; count++;
                            kind = Math.Max(kind, Kind(samples[z * n + x].Water));
                            plume = Math.Max(plume, samples[z * n + x].Plume);
                        }
                    if (count > 0) level = sum / count;
                    else if (under[k] != int.MaxValue && !dug[k]) level = SeaLevel(samples);
                    // Dug ground still above the water: the sheet runs under it as under any bank. Dug ground below
                    // the water is the local simulation's (its squares are left out below).
                    if (dug[k] && !double.IsNaN(level))
                    {
                        if (heights[(j + 1) * s2 + i + 1] >= level) dug[k] = false;
                        else level = double.NaN;
                    }
                }
                if (double.IsNaN(level)) level = data.Vertices[k].Y - 1; // unused vertex: keep it under ground
                double ground = heights[(j + 1) * s2 + i + 1];
                vertices[k] = new Vector3(i * (float)spacing, (float)level, j * (float)spacing);
                colors[k] = new Color((float)DetMath.Clamp((level - ground) / 20, 0, 1), (float)kind, (float)plume, 1);
            }
        var indices = new List<int>();
        bool Covered(int k) => wet[k] || under[k] != int.MaxValue;
        for (int j = 0; j < n - 1; j++)
            for (int i = 0; i < n - 1; i++)
            {
                int a = j * n + i, b = a + 1, c = a + n, d = c + 1;
                if (!(Covered(a) || Covered(b) || Covered(c) || Covered(d))) continue;
                // A square touching dug ground below the water would stretch the sheet down into the hole (walls of
                // water around a dug river bed); the local simulation draws the water there.
                if (dug[a] || dug[b] || dug[c] || dug[d]) continue;
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(b); indices.Add(d); indices.Add(c);
            }
        data.Water = (vertices, colors, Flow(samples, vertices, wet, spacing, n), indices.ToArray());
    }

    private static double SeaLevel(TerrainSample[] samples)
    {
        foreach (var t in samples) if (t.Water == WaterKind.Ocean) return t.WaterLevel;
        return 0;
    }

    /// <summary>
    /// River current per water vertex (world xz, m/s): downhill along the water surface's gradient (central
    /// differences between wet neighbours), speed growing with the square root of the slope. Still water elsewhere.
    /// </summary>
    private static Vector2[] Flow(TerrainSample[] samples, Vector3[] water, bool[] wet, double spacing, int n)
    {
        var flow = new Vector2[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                if (!wet[k] || samples[k].Water != WaterKind.River) continue;
                double Slope(int a, int b)
                {
                    bool fa = a >= 0 && wet[a] && samples[a].Water == WaterKind.River, fb = b >= 0 && wet[b] && samples[b].Water == WaterKind.River;
                    if (fa && fb) return (water[b].Y - water[a].Y) / (2 * spacing);
                    if (fb) return (water[b].Y - water[k].Y) / spacing;
                    if (fa) return (water[k].Y - water[a].Y) / spacing;
                    return 0;
                }
                double gx = Slope(i > 0 ? k - 1 : -1, i < n - 1 ? k + 1 : -1);
                double gz = Slope(j > 0 ? k - n : -1, j < n - 1 ? k + n : -1);
                double g = Math.Sqrt(gx * gx + gz * gz);
                if (g < 1e-6) continue;
                double speed = Math.Clamp(Math.Sqrt(g) * 6, .3, 3.5);
                flow[k] = new Vector2((float)(-gx / g * speed), (float)(-gz / g * speed));
            }
        // Bank vertices (under the dry ground beside the water) take their wet neighbours' current, then two passes
        // of 3×3 averaging over river vertices: smooth directions, no slow-down toward the banks.
        var river = new bool[n * n];
        for (int k = 0; k < n * n; k++) river[k] = flow[k] != Vector2.Zero;
        for (int pass = 0; pass < 3; pass++)
        {
            var next = (Vector2[])flow.Clone();
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    if (pass == 0 && river[k]) continue;
                    if (pass > 0 && !river[k]) continue;
                    var sum = Vector2.Zero; int count = 0;
                    for (int b = -1; b <= 1; b++)
                        for (int a = -1; a <= 1; a++)
                        {
                            int x = i + a, z = j + b;
                            if (x < 0 || z < 0 || x >= n || z >= n || !river[z * n + x]) continue;
                            sum += flow[z * n + x]; count++;
                        }
                    if (count > 0) next[k] = sum / count;
                }
            flow = next;
            if (pass == 0) for (int k = 0; k < n * n; k++) river[k] |= flow[k] != Vector2.Zero;
        }
        return flow;
    }

    private static double Kind(WaterKind kind) => kind switch { WaterKind.Lake => .5, WaterKind.River => 1, _ => 0 };
}
