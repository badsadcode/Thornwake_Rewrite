using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Thornwake.WorldGen;
using Noise = Thornwake.WorldGen.Noise;

namespace Thornwake.Rewrite;

/// <summary>
/// Deterministic forest rules (worker-thread safe). Trees sit on world-aligned cells, so the same tree appears at
/// the same place whatever tile or LOD level asks for it. Species follow elevation, with willows by rivers.
/// </summary>
public static class Vegetation
{
    public const double Cell = 7;
    public static readonly string[] Species =
    {
        "Pine_A", "Pine_B", "Pine_C", "Spruce_A", "Spruce_B", "Oak_A", "Oak_B", "Oak_C", "Birch_Green_A", "Birch_Green_B",
        "Birch_Green_C", "Maple_Autumn_A", "Poplar_A", "Willow_A", "Sparse_A", "Birch_Autumn_A", "Birch_Autumn_B",
        // Placeholder plants (PlaceholderPlants.cs) for biomes without art yet.
        "Palm_A", "Palm_B", "Cactus_A", "Cactus_B", "Dead_A", "Dead_B", "Acacia_A", "Jungle_A", "Cypress_A", "Shrub_Dry",
        "Bald_Cypress_A", "Bald_Cypress_B",
    };
    private static readonly int[] Broadleaf = { 5, 6, 7, 8, 9, 10, 11, 12, 15, 16, 0, 1 };
    private static readonly int[] Mixed = { 0, 1, 2, 3, 4, 8, 9, 5, 6, 15 };
    private static readonly int[] Conifer = { 0, 1, 2, 3, 4, 3, 4, 14 };

    private static readonly int[] Green = { 5, 6, 7, 8, 9, 10, 12 };
    private static readonly int[] Autumn = { 11, 15, 16 };
    private static readonly int[] Needles = { 0, 1, 2, 3, 4, 3, 4 };

    /// <summary>The biome blend at a point (D42): the two strongest biomes and the first one's share.</summary>
    public static (ResolvedBiome A, ResolvedBiome B, double Share) BiomeOf(TerrainSampler sampler, double x, double z)
    {
        var map = sampler.Plan.Biomes;
        var (a, b, share) = map.Sample(x, z);
        return (map.Pool[a], map.Pool[b], share);
    }

    private static readonly Dictionary<string, int> SpeciesIndex = Species.Select((s, i) => (s, i)).ToDictionary(p => p.s, p => p.i);

    /// <summary>0..1 forest cover: broad patches of woodland and meadow, thinning toward the tree line; the biome
    /// sets how much forest there is (none in deserts and on ice, little in steppe and tundra, dense in rainforest).</summary>
    public static double ForestDensity(TerrainSampler sampler, double x, double z, in TerrainSample t, double coverSetting = 1, double biomeForest = -1)
    {
        if (coverSetting <= 0 || t.Water is not (WaterKind.None or WaterKind.Pond)) return 0;
        double inWater = 1;
        if (t.Water == WaterKind.Pond)
        {
            // Bayou cypress stand in the pools (not too deep); other trees keep out of the water.
            var (wa, wb, ws) = BiomeOf(sampler, x, z);
            inWater = (wa.Def.Vegetation.WaterTrees * ws + wb.Def.Vegetation.WaterTrees * (1 - ws)) * (1 - DetMath.Smooth((t.WaterLevel - t.Height - 1.2) / .6));
            if (inWater <= .001) return 0;
        }
        double elevation = (t.Water == WaterKind.Pond ? t.WaterLevel : t.Height) - sampler.Plan.Profile.SeaLevel;
        if (elevation < 2.5) return 0;
        if (biomeForest < 0) { var (ba, bb, share) = BiomeOf(sampler, x, z); biomeForest = ba.Def.Vegetation.TreeDensity * share + bb.Def.Vegetation.TreeDensity * (1 - share); }
        biomeForest *= inWater;
        if (biomeForest <= .001) return 0;
        // Denser biomes grow bigger forests; sparse ones (steppe, savanna, tundra) only scattered trees.
        double sparse = Math.Min(1, biomeForest);
        coverSetting *= Math.Clamp(biomeForest, 1, 1.8);
        ulong seed = Hashing.Mix(unchecked((ulong)sampler.Plan.Manifest.Seed * 0x2545F4914F6CDD1DUL + 77));
        // Steep enough that land is mostly either forest or meadow, with short edges (a wide band of half-wooded land
        // looked like scattered trees everywhere near the player).
        double patches = Noise.Fbm(x, z, 2600, 4, 60, seed, .55) * 3.4 + .45 + (coverSetting - 1) * 1.1;
        double cover = DetMath.Smooth(patches) * Treeline(sampler, x, z, elevation, seed) * DetMath.Smooth((elevation - 2.5) / 4);
        return cover * (1 - .7 * DetMath.Smooth((t.Mountain - .45) / .35)) * sparse;
    }

    /// <summary>
    /// 0..1: below the tree line. With biomes (round 24, Tom: alpine slopes at 2.1 km and 4.6 °C stood bare) it follows
    /// the climate — trees thin out from a mean of +1.5 °C and stop at −2.5 °C (round 25; near the real tree line), so warm mountains
    /// are wooded higher up than cold ones; without biomes the fixed ~1.75 km line of build 9.
    /// </summary>
    public static double Treeline(TerrainSampler sampler, double x, double z, double elevation, ulong seed)
    {
        var climate = sampler.Plan.Climate;
        if (climate.Settings.Biomes)
        {
            double t = climate.At(x, z, elevation).Temperature + .8 * Noise.Gradient(x / 9000, z / 9000, seed + 5);
            // (round 25, Tom: alpine still empty — trees now thin from +1.5 °C and stop at −2.5 °C; round 24: +2 / −1)
            return DetMath.Smooth((t + 2.5) / 4);
        }
        double treeline = 1750 + 350 * Noise.Gradient(x / 9000, z / 9000, seed + 5);
        return DetMath.Smooth((treeline - elevation) / 220);
    }

    /// <summary>A tree of the lattice in world space; (Cx, Cz) is its cell: the tree's identity (felling, D108).</summary>
    public readonly record struct LatticeTree(long Cx, long Cz, double X, double Y, double Z, int Species, float Scale, float Yaw);

    /// <summary>The lattice's cell size for this world (denser forests: smaller cells).</summary>
    public static double LatticeCell(in DetailConfig config) => Cell / Math.Sqrt(Math.Max(1, config.ForestDensity));

    private static ulong TreeSeed(TerrainSampler sampler) => Hashing.Mix((ulong)sampler.Plan.Manifest.Seed + 0x7EE5UL);

    /// <summary>Where the lattice cell's tree would stand (before any test whether it grows there).</summary>
    private static (double X, double Z, ulong Hash) Candidate(long cx, long cz, double cell, ulong seed)
    {
        ulong h = Hashing.Lattice(cx, cz, seed);
        return ((cx + .15 + .7 * Hashing.Unit(h)) * cell, (cz + .15 + .7 * Hashing.Unit(Hashing.Mix(h))) * cell, h);
    }

    public static List<TreeInstance> Trees(TerrainSampler sampler, TileKey key, in DetailConfig config)
    {
        var trees = new List<TreeInstance>();
        // Denser than default: smaller world-aligned cells; sparser: fewer cells kept.
        double cell = LatticeCell(config);
        double x0 = key.MinX, z0 = key.MinZ, size = key.Size;
        long c0x = (long)Math.Floor(x0 / cell), c0z = (long)Math.Floor(z0 / cell);
        long c1x = (long)Math.Floor((x0 + size) / cell), c1z = (long)Math.Floor((z0 + size) / cell);
        ulong seed = TreeSeed(sampler);
        var removals = sampler.Removals;
        // Inclusive: the cell straddling the tile's far edge may put its tree inside this tile (an exclusive loop lost
        // those trees, leaving tree-free lanes along every tile border).
        for (long cz = c0z; cz <= c1z; cz++)
            for (long cx = c0x; cx <= c1x; cx++)
            {
                var (x, z, _) = Candidate(cx, cz, cell, seed);
                if (x < x0 || z < z0 || x >= x0 + size || z >= z0 + size) continue;
                if (removals.Contains(ObjectRemovals.Trees, cx, cz) || sampler.Hidden.Contains(ObjectRemovals.Trees, cx, cz)) continue; // felled, or falling
                if (TreeAt(sampler, cx, cz, config) is not { } t) continue;
                var basis = new Basis(Vector3.Up, t.Yaw).Scaled(Vector3.One * t.Scale);
                trees.Add(new TreeInstance(t.Species, new Transform3D(basis, new Vector3((float)(x - x0), (float)t.Y, (float)(z - z0)))));
            }
        return trees;
    }

    /// <summary>The tree of lattice cell (cx, cz), or null where none grows (felled ones included: see Removals).</summary>
    public static LatticeTree? TreeAt(TerrainSampler sampler, long cx, long cz, in DetailConfig config)
    {
        double cell = LatticeCell(config), keep = Math.Min(1, config.ForestDensity), sea = sampler.Plan.Profile.SeaLevel;
        var (x, z, h) = Candidate(cx, cz, cell, TreeSeed(sampler));
        double chance = Hashing.Unit(Hashing.Mix(h ^ 0xABCDEFUL));
        if (chance > .95) return null; // never a perfectly closed canopy
        var t = sampler.Sample(x, z, 2);
        double density = ForestDensity(sampler, x, z, t, config.ForestCover);
        if (chance > density * .92 * keep) return null;
        // Avoid steep ground: compare with a neighbour sample.
        double slope = Math.Abs(sampler.Height(x + 2, z, 4) - t.Height) / 2;
        if (slope > .7) return null;
        double elevation = t.Height - sea;
        ulong pick = Hashing.Mix(h + 99);
        int species;
        ResolvedBiome? biomeHere = null;
        if (sampler.Plan.Hydrology.NearestRiver(x, z, 0, out var river) && river.Distance < river.Width * .5 + 18 && (pick & 3) != 0) species = 13;
        else
        {
            // A tree from the biome's tree sets (classic look: species by elevation as in build 9).
            var (ba, bb, share) = BiomeOf(sampler, x, z);
            biomeHere = Hashing.Unit(Hashing.Mix(pick + 7)) < share ? ba : bb;
            if (biomeHere.Classic)
            {
                var list = elevation < 550 ? Broadleaf : elevation < 1250 ? Mixed : Conifer;
                species = list[(int)(pick % (ulong)list.Length)];
            }
            else species = SpeciesIndex.GetValueOrDefault(biomeHere.PickTree(Hashing.Unit(Hashing.Mix(pick + 3))), 0);
        }
        var sizes = biomeHere?.Def.Vegetation.TreeSize ?? new[] { .8, 1.25 };
        float scale = (float)(sizes[0] + (sizes[1] - sizes[0]) * Hashing.Unit(Hashing.Mix(pick)));
        float yaw = (float)(Hashing.Unit(Hashing.Mix(pick + 1)) * Math.PI * 2);
        return new LatticeTree(cx, cz, x, (float)t.Height - .1f, z, species, scale, yaw);
    }

    /// <summary>The trees standing within <paramref name="radius"/> m of (x, z) (felled ones left out).</summary>
    public static List<LatticeTree> TreesNear(TerrainSampler sampler, double x, double z, double radius, in DetailConfig config)
    {
        var list = new List<LatticeTree>();
        double cell = LatticeCell(config);
        for (long cz = (long)Math.Floor((z - radius) / cell); cz <= (long)Math.Floor((z + radius) / cell); cz++)
            for (long cx = (long)Math.Floor((x - radius) / cell); cx <= (long)Math.Floor((x + radius) / cell); cx++)
            {
                if (sampler.Removals.Contains(ObjectRemovals.Trees, cx, cz)) continue;
                if (TreeAt(sampler, cx, cz, config) is { } t && (t.X - x) * (t.X - x) + (t.Z - z) * (t.Z - z) <= radius * radius) list.Add(t);
            }
        return list;
    }
}
