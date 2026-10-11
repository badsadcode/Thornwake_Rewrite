using System;
using System.Collections.Generic;
using System.Linq;
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
    int TreeLevels, int FarTreeLevels, bool SimulatedWater = true, int GrassStyle = 1, int MeshDetail = 1, int CardLevels = -1)
{

    public static DetailConfig For(TerrainSampler sampler)
    {
        var style = sampler.Plan.Style;
        return new(GameSettings.GrassDensity.Value, style.ForestCover, style.ForestDensity, style.RockAmount, style.PropAmount,
            TreeField.Enabled ? 1 : 2 + GameSettings.TreeDistance.Index, 4 + GameSettings.FarTreeDistance.Index, GameSettings.WaterSimulation.On, GameSettings.GrassStyle.Index,
            1 << GameSettings.TerrainMesh.Index, CardLevel());
    }

    /// <summary>
    /// With the tree field (round 30) the tiles carry trees only on level 0 (for collision; the field draws them), and
    /// tree cards from the first level whose ring reaches inside the impostor range (the cards fade in beyond it).
    /// Without it (build 41): 3D trees on the nearest levels, then cards.
    /// </summary>
    private static int CardLevel()
    {
        if (!TreeField.Enabled) return 2 + GameSettings.TreeDistance.Index;
        double impostors = Math.Max(400.0 * (1 << GameSettings.TreeDistance.Index), 800.0 * (1 << GameSettings.TreeImpostors.Index));
        int k = Math.Clamp((int)Math.Round(GameSettings.TerrainDetail.Value), 1, 4);
        int level = 1;
        // A level's ring starts (k + 0.5) tiles from the player along the axes, √2 farther at its corners.
        while ((k + .5) * TileBuilder.BaseTile * (1 << (level + 1)) * Math.Sqrt(2) <= impostors) level++;
        return level;
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
    /// <summary>Plant sprites (grass style "plant populations"): land and water plants, tile-local.</summary>
    public List<FloraInstance> Flora { get; set; } = new();
    /// <summary>Per terrain vertex: biome water tint (packed rgb, strength), copied onto the water sheet.</summary>
    public Vector2[]? WaterTint { get; set; }
    /// <summary>Per water vertex (CUSTOM0 of the water sheet, used by the experimental water): uphill bed gradient
    /// x, z (toward the shore; length = slope), exposure 0 sheltered … 1 open coast, fetch (m: lake size; sea 1e5).</summary>
    public float[]? WaterShore { get; set; }
    /// <summary>Cliff dressing cards: kind 0 hanging grass, 1 roots (GroundCover.CliffDressing).</summary>
    public List<RockInstance> Drapes { get; set; } = new();
    /// <summary>Wetland plants (PlaceholderPlants.WetPlant kinds), tile-local.</summary>
    public List<RockInstance> WetPlants { get; set; } = new();
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
    /// <summary>
    /// Grid cells per tile side: 1 m at level 0; coarser levels 32 cells (4 m, 8 m … — build 32) or, with the Terrain
    /// mesh detail option, 64 or 128 (the first ring 2 m or 1 m: the step from 1 m to 4 m showed as a near loss of
    /// detail). Skirts hide the cracks between neighbours of any resolution.
    /// </summary>
    public static int CellsFor(int level, int meshDetail = 1) => level == 0 ? 64 : Math.Min(128, Cells * meshDetail);

    private static (float R, float G, float B) Mix((float R, float G, float B) a, (float R, float G, float B) b, float sa, float sb, float scale) =>
        ((a.R * sa + b.R * sb) * scale, (a.G * sa + b.G * sb) * scale, (a.B * sa + b.B * sb) * scale);

    private static void Put(float[] target, int k, (float R, float G, float B) c, double a)
    {
        target[k * 4] = c.R; target[k * 4 + 1] = c.G; target[k * 4 + 2] = c.B; target[k * 4 + 3] = (float)a;
    }
    public static TileData Build(TerrainSampler sampler, TileKey key, DetailConfig config)
    {
        int Cells = CellsFor(key.Level, config.MeshDetail), Side = Cells + 1;
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
        ulong floraSeed = GroundCover.FloraSeed(Hashing.Mix((ulong)sampler.Plan.Manifest.Seed + 0xC0FFEEUL));
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
        var props = new float[Side * Side * 3]; // biome rock and bush/log multipliers, rock look (ground cover only, no skirts)
        var waterTint = new Vector2[Side * Side]; // biome water tint, packed rgb (x) and strength (y), for the water sheet
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
                // Rocky shore patches: bare rock instead of sand (and blocky rocks, GroundCover.Rocks).
                float rocky = GroundCover.RockyShore(x0 + i * spacing, z0 + j * spacing, h - sea);
                beach *= 1 - rocky;
                colors[k] = new Color((float)forest[k], (float)t.Mountain, (float)beach, submerged[k]);
                // World xz folded into a 4 km period keeps float precision for shader noise far from the origin.
                uv[k] = new Vector2((float)Fold(x0 + i * spacing), (float)Fold(z0 + j * spacing));
                // UV2.y: how much the player has dug or raised here (bare soil in the shader).
                double edited = Math.Abs(sampler.Edits.Delta(x0 + i * spacing, z0 + j * spacing));
                // UV2.x: grass cover from the plant fields (the shader takes the height above sea from the position).
                uv2[k] = new Vector2((float)GroundCover.FloraCover(x0 + i * spacing, z0 + j * spacing, floraSeed, spacing * 2, out _, out _, out _, out _),
                    (float)Math.Min(1, edited * 2));
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
                // With biomes the grass line follows the cold too (alpine meadows reach above the trees, thinning out
                // from 0 °C to −6 °C; round 25, Tom: "alpine feels a bit empty" — was +1 to −3), carried in the plant
                // cover; GroundCover then drops its fixed 1.7 km line.
                if (climate.Settings.Biomes) plants *= DetMath.Smooth((temperature + 6) / 6);
                Put(biome1, k, Mix(A.Dry, B.Dry, sa, sb, dark), Math.Min(1, plants));
                // CUSTOM2: r = map colour packed (debug view), g = the two rock looks (lo + 8·hi), b = weight of hi,
                // a = rock exposure. Looks are ordered so neighbouring vertices across a border agree on the pair.
                var mapColour = Mix(A.Map, B.Map, sa, sb, 1);
                int rlo = Math.Min(A.RockTexture, B.RockTexture), rhi = Math.Max(A.RockTexture, B.RockTexture);
                float whi = rlo == rhi ? 0 : A.RockTexture == rhi ? sa : sb;
                biome2[k * 4] = (int)(Math.Clamp(mapColour.R, 0, 1) * 255) << 16 | (int)(Math.Clamp(mapColour.G, 0, 1) * 255) << 8 | (int)(Math.Clamp(mapColour.B, 0, 1) * 255);
                biome2[k * 4 + 1] = rlo + 8 * rhi;
                biome2[k * 4 + 2] = whi;
                biome2[k * 4 + 3] = (float)Math.Max(A.Def.Appearance.RockExposure * share + B.Def.Appearance.RockExposure * (1 - share), rocky * 1.6);
                Put(biome3, k, Mix(A.Rock, B.Rock, sa, sb, 1), A.Def.Water.Wetness * share + B.Def.Water.Wetness * (1 - share));
                props[k * 3] = (float)(A.Def.Props.Rocks * share + B.Def.Props.Rocks * (1 - share));
                props[k * 3 + 1] = (float)(A.Def.Props.Logs * share + B.Def.Props.Logs * (1 - share));
                props[k * 3 + 2] = share >= .5 ? A.RockTexture : B.RockTexture;
                waterTint[k] = WaterTint(A, B, share);
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
            Biome0 = biome0, Biome1 = biome1, Biome2 = biome2, Biome3 = biome3, WaterTint = waterTint,
            CollisionFaces = key.Level == 0 ? Faces(vertices) : null,
            // Full trees on the nearest levels; beyond, tree cards, then the terrain shader's forest tint.
            Trees = key.Level < config.TreeLevels ? Vegetation.Trees(sampler, key, config) : new List<TreeInstance>(),
        };
        BuildWater(data, samples, heights, s2, spacing, Side, config.SimulatedWater, sea);
        if (data.Water is { } builtWater) data.WaterShore = ShoreData(sampler, builtWater.Vertices, builtWater.Colors, heights, s2, samples, x0, z0, spacing, Side);
        var grid = new GroundCover.Grid(vertices, normals, forest, samples, spacing, x0, z0, Side, submerged, uv2, biome1, props, biome3, biome0, climate.Settings.Biomes);
        ulong coverSeed = Hashing.Mix((ulong)sampler.Plan.Manifest.Seed + 0xC0FFEEUL);
        bool populations = config.GrassStyle == 1;
        if (key.Level == 0 && config.Grass > 0 && populations)
        {
            data.Flora = GroundCover.LandFlora(key, grid, sea, coverSeed, config.Grass);
            data.Flora.AddRange(GroundCover.WaterFlora(key, grid, sea, coverSeed, biomes, Math.Min(1.5, config.Grass)));
        }
        if (key.Level == 0 && config.Grass > 0 && !populations) data.Grass = GroundCover.Grass(key, grid, sea, coverSeed, config.Grass);
        if (key.Level == 0 && config.Grass > 0 && !populations) data.Reeds = GroundCover.Reeds(key, grid, sea, coverSeed, config.Grass);
        if (key.Level == 0 && config.Props > 0) data.Drapes = GroundCover.CliffDressing(key, grid, sea, coverSeed);
        if (key.Level <= 2 && config.Rocks > 0) data.Rocks = GroundCover.Rocks(key, grid, sea, coverSeed, config.Rocks);
        if (data.Water is { } riverWater && data.WaterShore is { } shore && data.Rocks is { Count: > 0 } rocks)
            ObstacleWakes(riverWater.Vertices, riverWater.Colors, riverWater.Flow, shore, rocks);
        if (key.Level <= 1 && config.Props > 0) data.Props = GroundCover.Props(key, grid, sea, coverSeed, config.Props);
        if (key.Level <= 1 && config.Grass > 0) data.WetPlants = GroundCover.WetPlants(key, grid, sea, coverSeed, biomes, Math.Min(1.5, config.Grass), floating: !populations || key.Level > 0);
        if (key.Level >= (config.CardLevels >= 0 ? config.CardLevels : config.TreeLevels) && key.Level <= config.FarTreeLevels)
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
    /// <summary>The blended biome water tint at a vertex: rgb packed into one float (exact up to 2^24), strength 0..1.</summary>
    /// <summary>The biome water tint at a world point (the simulated water's sheets match the generated water).</summary>
    public static Vector2 WaterTintAt(TerrainSampler sampler, double x, double z)
    {
        var biomes = sampler.Plan.Biomes;
        var (ba, bb, share) = biomes.Sample(x, z);
        return WaterTint(biomes.Pool[ba], biomes.Pool[bb], share);
    }

    private static Vector2 WaterTint(ResolvedBiome a, ResolvedBiome b, double share)
    {
        (float R, float G, float B, float S) Of(ResolvedBiome r) => r.Def.Water.WaterTint is { } hex ? (BiomeRegistry.Hex(hex).Item1, BiomeRegistry.Hex(hex).Item2, BiomeRegistry.Hex(hex).Item3, (float)r.Def.Water.WaterTintStrength) : (1f, 1f, 1f, 0f);
        var (ar, ag, ab, asg) = Of(a); var (br, bg, bb, bs) = Of(b);
        float sa = (float)share, sb = 1 - sa, strength = asg * sa + bs * sb;
        if (strength <= 0) return Vector2.Zero;
        // Colour from the tinted biomes only (an untinted neighbour fades the strength, not the colour).
        float wa = asg * sa, wb = bs * sb, w = wa + wb;
        float r = (ar * wa + br * wb) / w, g = (ag * wa + bg * wb) / w, bl = (ab * wa + bb * wb) / w;
        int packed = (int)(r * 255) << 16 | (int)(g * 255) << 8 | (int)(bl * 255);
        return new Vector2(packed, strength);
    }

    private static void BuildWater(TileData data, TerrainSample[] samples, double[] heights, int s2, double spacing, int n, bool simulated, double seaLevel)
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
        // The world's sea level, not the level of an ocean sample in this tile: a tile holding only the inner part of
        // an estuary had none, kept the river code and drew a seam along its edge (Tom's dumps, round 20).
        double sea = seaLevel;
        // A river's channel continuing out to sea at sea level is the sea there, carrying the river's water (plume: its
        // colour and current) — no river surface with rapids standing in the sea. Bank vertices take the same.
        var bodyKind = new double[n * n];
        var bodyPlume = new double[n * n];
        for (int k = 0; k < n * n; k++)
        {
            if (!wet[k]) continue;
            bool estuary = samples[k].Water == WaterKind.River && samples[k].Height < sea && samples[k].WaterLevel < sea + .15;
            bodyKind[k] = estuary ? SeaCode : Kind(samples[k].Water);
            bodyPlume[k] = estuary ? 1 : samples[k].Plume;
        }
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                double level = double.NaN, kind = 0, plume = 0;
                if (wet[k])
                {
                    level = samples[k].WaterLevel; kind = bodyKind[k]; plume = bodyPlume[k];
                }
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
                            kind = Math.Max(kind, bodyKind[z * n + x]); // (bank vertices: pond, lake, river, then sea)
                            plume = Math.Max(plume, bodyPlume[z * n + x]);
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
                // Alpha: 1 on water vertices, fading over the sheet's extension under the beach (the experimental water
                // fades its swash film out there instead of ending on the sheet's triangle edges; classic ignores it).
                // (Corners of the last squares that are neither: 0 on the sea, 1 at lake and river banks.)
                float edge = wet[k] ? 1 : under[k] != int.MaxValue ? Math.Max(0, 1 - under[k] / 3f) : kind < .25 ? 0 : 1;
                colors[k] = new Color((float)DetMath.Clamp((level - ground) / 20, 0, 1), (float)kind, (float)plume, edge);
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

    /// <summary>
    /// Shore data for the experimental water (a pure function of world position, so neighbouring tiles agree):
    /// near the coast (sea under ~25 m deep) the bed's uphill gradient over ~8 m+ and how open the sea is around
    /// (macro land in 8 directions at 0.6, 2 and 6 km: a straight coast is fully exposed, a bay sheltered); for lakes,
    /// their size (fetch).
    /// </summary>
    private static float[] ShoreData(TerrainSampler sampler, Vector3[] water, Color[] colors, double[] heights, int s2, TerrainSample[] samples,
        double x0, double z0, double spacing, int n)
    {
        var result = new float[n * n * 4];
        var plan = sampler.Plan;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                float kind = colors[k].G;
                double x = x0 + i * spacing, z = z0 + j * spacing;
                double depth = water[k].Y - heights[(j + 1) * s2 + i + 1];
                float gx = 0, gz = 0, exposure = 1, fetch = 0;
                if (kind < .25f)
                {
                    fetch = 1e5f;
                    (gx, gz, exposure) = WaterShore.CoastAt(sampler, x, z, depth, spacing);
                }
                else if (IsLakeCode(kind) && samples[k].WaterBody >= 0 && samples[k].WaterBody < plan.Hydrology.Lakes.Count)
                    fetch = (float)Math.Sqrt(plan.Hydrology.Lakes[samples[k].WaterBody].Area);
                // Rivers: their width (the wind's fetch across them): wide rivers get wind waves too.
                else if (IsRiverCode(kind) && samples[k].WaterBody >= 0 && samples[k].WaterBody < plan.Hydrology.Reaches.Count)
                    fetch = RiverWidth(plan.Hydrology.Reaches[samples[k].WaterBody]);
                result[k * 4] = gx; result[k * 4 + 1] = gz; result[k * 4 + 2] = exposure; result[k * 4 + 3] = fetch;
            }
        return result;
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<RiverReach, System.Runtime.CompilerServices.StrongBox<float>> Widths = new();

    /// <summary>A reach's typical width (m): its mean.</summary>
    private static float RiverWidth(RiverReach reach) => Widths.GetValue(reach, r =>
    {
        double sum = 0;
        foreach (var w in r.Width) sum += w;
        return new System.Runtime.CompilerServices.StrongBox<float>(r.Width.Length > 0 ? (float)(sum / r.Width.Length) : 0);
    }).Value;

    /// <summary>
    /// Obstacle wakes (round 15, RIVERS_PLAN step 5): rocks and boulders standing in a river's current. For river
    /// vertices around one (a little upstream, a long way downstream), the shore-data slots the sea uses are reused:
    /// x = downstream of the rock along the local current, y = across it (both in rock radii), z = weight (0 = no
    /// obstacle; rivers have no exposure). The shader draws the pressure bulge upstream, a V-wake and eddies behind.
    /// </summary>
    private static void ObstacleWakes(Vector3[] water, Color[] colors, Vector2[] flow, float[] shore, List<RockInstance> rocks)
    {
        // Per river vertex: the nearest rock's frame (downstream, across, both in rock radii) and a weight 0..1. The
        // weight fades to 0 toward the wake's ends and where another rock is nearly as close: the shader blends these
        // values across each triangle, and a triangle whose corners belong to different rocks (or none) used to draw
        // frozen rings and arcs of nonsense (Tom, build 25: "circles on the rivers … they look weird").
        var best = new float[water.Length];
        var second = new float[water.Length];
        var data = new Vector3[water.Length];
        for (int k = 0; k < water.Length; k++)
        {
            best[k] = second[k] = float.MaxValue;
            if (IsRiverCode(colors[k].G)) { shore[k * 4] = 0; shore[k * 4 + 1] = 0; shore[k * 4 + 2] = 0; }
        }
        foreach (var rock in rocks)
        {
            if (rock.Kind == GroundCover.Stone) continue;
            var b = rock.Transform.Basis;
            float radius = (b.X.Length() + b.Z.Length()) * .25f; // the meshes span ±0.5 per unit scale
            if (radius < .3f) continue;
            var at = rock.Transform.Origin;
            float reach = 12 * radius + 4;
            for (int k = 0; k < water.Length; k++)
            {
                if (!IsRiverCode(colors[k].G) || flow[k] == Vector2.Zero) continue;
                float dx = water[k].X - at.X, dz = water[k].Z - at.Z;
                if (Math.Abs(dx) > reach || Math.Abs(dz) > reach) continue;
                // Only rocks that break the surface (a rock just under it shows no wake you could tell apart).
                float top = at.Y + b.Y.Length() * .6f;
                if (at.Y > water[k].Y || top < water[k].Y + .05f) continue;
                var f = flow[k].Normalized();
                float a = (dx * f.X + dz * f.Y) / radius, c = (-dx * f.Y + dz * f.X) / radius;
                if (a < -3 || a > 11 || Math.Abs(c) > 2.5f + Math.Max(0, a) * .5f) continue;
                float w = Smooth01((a + 3) / 1.2f) * Smooth01((11 - a) / 4) * Smooth01((2.5f + Math.Max(0, a) * .5f - Math.Abs(c)) / 1.2f);
                float d = MathF.Sqrt(dx * dx + dz * dz);
                if (d < best[k]) { second[k] = best[k]; best[k] = d; data[k] = new Vector3(a, c, w); }
                else if (d < second[k]) second[k] = d;
            }
        }
        for (int k = 0; k < water.Length; k++)
        {
            if (best[k] == float.MaxValue) continue;
            float apart = second[k] == float.MaxValue ? 1 : Smooth01((second[k] - best[k]) / 3);
            shore[k * 4] = data[k].X; shore[k * 4 + 1] = data[k].Y; shore[k * 4 + 2] = data[k].Z * apart;
        }
    }

    private static float Smooth01(float t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

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
        // River mouths (round 15, RIVERS_PLAN step 3): the current continues into the lake or sea as a fanning plume,
        // down the slope of the river-water share (which falls away from the channel's line and toward the plume's
        // end), weakening with it; the smoothing below joins it to the river's own current.
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                if (!wet[k] || samples[k].Water == WaterKind.River || samples[k].Plume < .03) continue;
                double P(int x, int z) => samples[Math.Clamp(z, 0, n - 1) * n + Math.Clamp(x, 0, n - 1)].Plume;
                double gx = (P(i + 1, j) - P(i - 1, j)) / (2 * spacing), gz = (P(i, j + 1) - P(i, j - 1)) / (2 * spacing);
                double g = Math.Sqrt(gx * gx + gz * gz);
                if (g < 1e-6) continue;
                double speed = 1.1 * Math.Pow(samples[k].Plume, .7);
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
        // Across the channel (round 14, Noor: "a river is water with a destination"): the speed follows the depth
        // relative to the deepest water nearby (Manning, v ∝ depth^⅔) — slow, lazy water over the shallow margins,
        // the fastest current over the deep line; a narrow, deep reach keeps its speed across its width.
        int r = Math.Max(2, (int)Math.Ceiling(6 / spacing));
        var depth = new float[n * n];
        for (int k = 0; k < n * n; k++) depth[k] = river[k] ? Math.Max(0, water[k].Y - (float)samples[k].Height) : 0;
        var shaped = (Vector2[])flow.Clone();
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i;
                if (!river[k] || samples[k].Water != WaterKind.River) continue; // mouth plumes keep their spread
                float deepest = .3f;
                for (int b = -r; b <= r; b++)
                    for (int a = -r; a <= r; a++)
                    {
                        int x = i + a, z = j + b;
                        if (x >= 0 && z >= 0 && x < n && z < n) deepest = Math.Max(deepest, depth[z * n + x]);
                    }
                shaped[k] = flow[k] * Math.Clamp(MathF.Pow(depth[k] / deepest, 2f / 3), .2f, 1);
            }
        return shaped;
    }

    /// <summary>
    /// Water body code in the mesh colour (G): sea 0, river ⅓, lake ⅔, pond 1. Ordered by what meets what — river mouths
    /// open into seas and lakes — so values blended across a triangle between neighbours never pass through a third
    /// kind (with sea 0 … river 1, a mouth's triangles showed a strip of "lake" between them: Tom, build 25).
    /// </summary>
    public const float SeaCode = 0, RiverCode = 1 / 3f, LakeCode = 2 / 3f, PondCode = 1;
    public static bool IsRiverCode(float g) => Math.Abs(g - RiverCode) < .1f;
    public static bool IsLakeCode(float g) => Math.Abs(g - LakeCode) < .1f;
    private static double Kind(WaterKind kind) => kind switch { WaterKind.Pond => PondCode, WaterKind.Lake => LakeCode, WaterKind.River => RiverCode, _ => SeaCode };
}
