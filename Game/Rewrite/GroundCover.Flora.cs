using System;
using System.Collections.Generic;
using Godot;
using Thornwake.WorldGen;
using Noise = Thornwake.WorldGen.Noise;

namespace Thornwake.Rewrite;

/// <summary>
/// Vegetation as populations (round 22; Noor: docs/design-inbox/NOOR_VEGETATION_POPULATIONS.md). Plants are not
/// independent random dots: after the usual hard rules (no grass in water, on rock, on dug ground, on steep slopes),
/// a candidate's chance comes from three scales of world-space noise —
///   macro (~170 m): regions that are generally sparse or lush;
///   patch (7 m or 24 m clumps, the scale itself varying by region; domain-warped so patches are blobs, crescents,
///          broken islands, never circles): the actual clumps, with soft edges and small holes inside;
///   micro: the candidate's own random position, rotation, size, lean and sprite.
/// Each patch also has an identity: a community drawn from a weighted table by habitat (meadow, forest edge, wet
/// ground, water's edge), e.g. "short grass with odd tall tufts", "thick lush grass", "grass with wild wheat", "a stand
/// of tall grass", "wet meadow". Water plants follow the same idea with fuzzy depth bands. Nothing is binary except
/// the hard rules. All noise runs at fixed wavelengths on world coordinates (a varying scale or direction multiplied by a
/// large position turns into stripes: round 20).
/// </summary>
public static partial class GroundCover
{
    private readonly record struct Community(float Density, float Scale, (int Species, float Weight)[] Mix);

    // Land communities (indices used by the habitat weights below).
    private static readonly Community[] LandCommunities =
    {
        new(.5f, .9f, new[] { (Flora.ShortA, 3f), (Flora.ShortB, 3f), (Flora.MediumA, 1f) }),                              // sparse short grass
        new(.85f, 1f, new[] { (Flora.ShortA, 3f), (Flora.ShortB, 2f), (Flora.MediumA, 2f), (Flora.MediumB, 1f), (Flora.TallA, .5f), (Flora.TallB, .5f) }), // short, odd tall tufts
        new(1.15f, 1.05f, new[] { (Flora.Lush, 3f), (Flora.MediumB, 3f), (Flora.MediumA, 2f), (Flora.TallC, 1f) }),          // thick lush grass
        new(1f, 1f, new[] { (Flora.ShortA, 2f), (Flora.MediumA, 2f), (-1, 2.5f), (Flora.Seeded, 1f) }),                     // grass with wild wheat (-1: wheat)
        new(1.05f, 1.05f, new[] { (Flora.TallA, 2f), (Flora.TallB, 2f), (Flora.TallC, 2f), (Flora.TallD, 2f), (Flora.Seeded, 1.5f), (Flora.Lush, 1f) }), // tall stand
        new(1.15f, 1.05f, new[] { (Flora.Lush, 3f), (Flora.TallC, 2f), (Flora.TallD, 1.5f), (Flora.ReedsRush, .5f), (Flora.Flowering, .25f) }), // wet meadow
    };

    private static readonly Community[] ShoreCommunities =
    {
        new(1f, 1f, new[] { (Flora.ReedsCommon, 4f), (Flora.ReedsRush, 1f) }),                                              // reed bed
        new(1f, 1f, new[] { (Flora.Cattails, 4f), (Flora.ReedsCommon, 1f) }),                                               // cattail stand
        new(.9f, 1f, new[] { (Flora.ReedsRush, 3f), (Flora.Flowering, 1.5f), (Flora.Lush, 1f) }),                           // rushes with flowers
        new(.45f, .9f, new[] { (Flora.ReedsRush, 1f), (Flora.Lush, 2f), (Flora.TallC, 1f) }),                               // sparse bank
    };

    private static readonly (float R, float G, float B) ReferenceGrass = (0x56 / 255f, 0x6b / 255f, 0x3f / 255f); // temperate biome grass

    /// <summary>Seed of the land plant fields (the terrain's ground tint uses the same fields as the sprites).</summary>
    public static ulong FloraSeed(ulong coverSeed) => coverSeed ^ 0xF10AUL;

    /// <summary>
    /// Grass cover 0..1 at a world point: macro lushness and the patch field (shared by the sprites and, on every
    /// terrain level, the ground's tint, so the patch pattern carries on beyond the grass draw distance). Octaves
    /// shorter than <paramref name="minWavelength"/> fade out (coarse tiles: no aliasing).
    /// </summary>
    public static double FloraCover(double wx, double wz, ulong seed, double minWavelength, out double lush, out double dx, out double dz, out double sel)
    {
        double macro = Noise.Fbm(wx, wz, 170, 3, minWavelength, seed + 1);
        lush = DetMath.Smooth((macro + .4) / .8);
        dx = 7 * Noise.Fbm(wx, wz, 30, 2, minWavelength, seed + 2); dz = 7 * Noise.Fbm(wx + 51.3, wz - 17.1, 30, 2, minWavelength, seed + 3);
        double fine = Noise.Fbm(wx + dx, wz + dz, 7, 2, minWavelength, seed + 4), coarse = Noise.Fbm(wx + dx, wz + dz, 24, 3, minWavelength, seed + 5);
        sel = DetMath.Smooth((Noise.Fbm(wx, wz, 90, 2, minWavelength, seed + 6) + .25) / .5);
        double p = fine + (coarse - fine) * sel;
        // Lush regions lower the threshold: bigger, joined-up patches; sparse ones leave islands.
        double thr = .15 - .45 * (lush - .5);
        double d = DetMath.Smooth((p - thr + .12) / .3);
        // Between patches: sparse to normal grass, never a bare lawn (Noor: bare … very dense, blended).
        return Math.Max(d, .2 + .2 * lush);
    }

    /// <summary>Per-tile fields on the vertex grid (bilinear between vertices; ~2 m apart on level 0).</summary>
    private sealed class FloraField
    {
        public readonly int Side; public readonly double Spacing;
        public readonly float[] Density, Lush, WarpX, WarpZ, Coarse, NearWater, Reeds;
        public FloraField(in Grid g, ulong seed)
        {
            Side = g.Side; Spacing = g.Spacing;
            int n = Side * Side;
            Density = new float[n]; Lush = new float[n]; WarpX = new float[n]; WarpZ = new float[n]; Coarse = new float[n]; NearWater = new float[n]; Reeds = new float[n];
            for (int j = 0; j < Side; j++)
                for (int i = 0; i < Side; i++)
                {
                    int k = j * Side + i;
                    double wx = g.X0 + i * Spacing, wz = g.Z0 + j * Spacing;
                    Density[k] = (float)FloraCover(wx, wz, seed, 0, out double lush, out double dx, out double dz, out double sel);
                    Lush[k] = (float)lush; WarpX[k] = (float)dx; WarpZ[k] = (float)dz; Coarse[k] = (float)sel;
                    // Reed and floating-plant patches along the water (their own clumps, ~12 m).
                    Reeds[k] = (float)DetMath.Smooth((Noise.Fbm(wx + dx, wz + dz, 12, 2, 0, seed + 7) + .1) / .4);
                }
            // Nearness to water (within ~8 m), from the tile's own samples.
            int r = Math.Max(1, (int)Math.Round(8 / Spacing));
            for (int j = 0; j < Side; j++)
                for (int i = 0; i < Side; i++)
                {
                    double best = double.MaxValue;
                    for (int b = Math.Max(0, j - r); b <= Math.Min(Side - 1, j + r); b++)
                        for (int a = Math.Max(0, i - r); a <= Math.Min(Side - 1, i + r); a++)
                        {
                            var t = g.Samples[b * Side + a];
                            if (t.Water is WaterKind.None or WaterKind.Ocean) continue;
                            best = Math.Min(best, (a - i) * (a - i) + (b - j) * (b - j));
                        }
                    NearWater[j * Side + i] = best == double.MaxValue ? 0 : (float)Math.Max(0, 1 - Math.Sqrt(best) * Spacing / 8);
                }
        }

        public float At(float[] f, double x, double z)
        {
            double gx = Math.Clamp(x / Spacing, 0, Side - 1.001), gz = Math.Clamp(z / Spacing, 0, Side - 1.001);
            int i = (int)gx, j = (int)gz; float fx = (float)(gx - i), fz = (float)(gz - j);
            int a = j * Side + i;
            return f[a] * (1 - fx) * (1 - fz) + f[a + 1] * fx * (1 - fz) + f[a + Side] * (1 - fx) * fz + f[a + Side + 1] * fx * fz;
        }
    }

    /// <summary>Picks a community: weights[i] × a hash of the patch cell (deterministic per cell).</summary>
    private static int Pick(ReadOnlySpan<float> weights, double u)
    {
        double total = 0;
        foreach (var w in weights) total += Math.Max(0, w);
        double t = u * total;
        for (int i = 0; i < weights.Length; i++) { t -= Math.Max(0, weights[i]); if (t <= 0) return i; }
        return weights.Length - 1;
    }

    private static int PickSpecies(in Community c, double u, bool golden)
    {
        double total = 0;
        foreach (var (_, w) in c.Mix) total += w;
        double t = u * total;
        foreach (var (s, w) in c.Mix) { t -= w; if (t <= 0) return s < 0 ? (golden ? Flora.WheatGolden : Flora.WheatYoung) : s; }
        var last = c.Mix[^1].Species;
        return last < 0 ? Flora.WheatYoung : last;
    }

    /// <summary>The patch cell a point belongs to: a warped grid of 8 m or 22 m cells (the scale varies by region).</summary>
    private static ulong PatchCell(FloraField f, double x, double z, double wx, double wz, ulong salt)
    {
        double px = wx + f.At(f.WarpX, x, z) * .8, pz = wz + f.At(f.WarpZ, x, z) * .8;
        ulong coarse = Hashing.Lattice((long)Math.Floor(px / 22), (long)Math.Floor(pz / 22), salt ^ 0xC0A5EUL);
        if (Hashing.Unit(coarse) < f.At(f.Coarse, x, z)) return coarse;
        return Hashing.Lattice((long)Math.Floor(px / 8), (long)Math.Floor(pz / 8), salt ^ 0xF1AEUL);
    }

    private static Color GrassTint(in Grid g, double x, double z, double dry, float jitter)
    {
        float r = 1, gr = 1, b = 1;
        if (g.Biome0 != null)
        {
            int i = Math.Clamp((int)Math.Round(x / g.Spacing), 0, g.Side - 1), j = Math.Clamp((int)Math.Round(z / g.Spacing), 0, g.Side - 1);
            int k = (j * g.Side + i) * 4;
            r = Math.Clamp(1 + (g.Biome0[k] / ReferenceGrass.R - 1) * .6f, .6f, 1.5f);
            gr = Math.Clamp(1 + (g.Biome0[k + 1] / ReferenceGrass.G - 1) * .6f, .6f, 1.5f);
            b = Math.Clamp(1 + (g.Biome0[k + 2] / ReferenceGrass.B - 1) * .6f, .6f, 1.5f);
        }
        // Fresh … dry patches (sun-bleached, yellower).
        float d = (float)Math.Clamp(dry, 0, 1);
        r *= 1 + .08f * d; gr *= 1 + .01f * d; b *= 1 - .18f * d;
        return new Color(r * jitter, gr * jitter, b * jitter);
    }

    private static Transform3D PlantTransform(ulong h, float height, Vector3 at, float maxTilt = .12f)
    {
        var basis = new Basis(Vector3.Up, Unit(h, 13) * Mathf.Tau) * new Basis(Vector3.Right, (Unit(h, 14) - .5f) * 2 * maxTilt);
        return new Transform3D(basis.Scaled(Vector3.One * height), at);
    }

    /// <summary>Land plants (level 0): grass, tall grass, wild wheat, wet-meadow plants, in patches and communities.</summary>
    public static List<FloraInstance> LandFlora(TileKey key, in Grid grid, double sea, ulong seed, double amount = 1)
    {
        var result = new List<FloraInstance>();
        var g = grid;
        var field = new FloraField(g, FloraSeed(seed));
        var weights = new float[LandCommunities.Length];
        // Candidates: a little denser than the classic tufts at the same setting; the fields thin them.
        Cells(key, 1.25 / Math.Sqrt(2 * amount) * .78, seed ^ 0x9A55UL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out float forest, out bool wet) || wet || g.Edited(x, z)) return;
            if (up < .82f || height - sea < 1.4) return;
            float alpine = (float)DetMath.Smooth((height - sea - 1700) / 500);
            float plants = g.Plants(x, z);
            float habitat = (1 - .7f * forest) * (1 - alpine) * (float)DetMath.Smooth((up - .82) / .08) * plants * plants;
            if (habitat <= .01f) return;
            float density = field.At(field.Density, x, z), lush = field.At(field.Lush, x, z), near = field.At(field.NearWater, x, z);
            float wetness = g.Wetness(x, z);
            ulong cell = PatchCell(field, x, z, g.X0 + x, g.Z0 + z, seed);
            weights[0] = 2 + 4 * (1 - plants) + 2 * forest + 2 * (1 - lush);
            weights[1] = 4;
            weights[2] = 1 + 3 * lush;
            weights[3] = .8f * (1 - forest) * (1 - wetness);
            weights[4] = .8f + 2 * lush + 3 * near;
            weights[5] = 4 * wetness + 4 * near;
            var c = LandCommunities[Pick(weights, Hashing.Unit(cell))];
            if (Unit(h, 1) > density * c.Density * habitat) return;
            ulong cellH = Hashing.Mix(cell);
            int species = PickSpecies(c, Unit(h, 2), Hashing.Unit(cellH) < .3);
            var sp = Flora.All[species];
            float size = (sp.MinHeight + (sp.MaxHeight - sp.MinHeight) * Unit(h, 3)) * c.Scale * (.85f + .3f * density) * (1 - .25f * forest);
            double dry = Hashing.Unit(Hashing.Mix(cellH)) * .45 + (1 - plants) * .4 - near * .3;
            var tint = GrassTint(g, x, z, dry, .94f + .12f * Unit(h, 4));
            result.Add(new FloraInstance(species, PlantTransform(h, size, new Vector3((float)x, height - .03f, (float)z)), tint));
        });
        return result;
    }

    /// <summary>
    /// Water plants (level 0): reeds, cattails and rushes along still and slow water (in clumps, standing into the
    /// shallows), floating leaves on ponds and lakes, submerged plants on lake, pond and river beds, seagrass meadows,
    /// kelp and seaweed in the sea. Fuzzy depth bands: depth makes a plant likelier, it never cuts it off at a line.
    /// </summary>
    public static List<FloraInstance> WaterFlora(TileKey key, in Grid grid, double sea, ulong seed, BiomeMap biomes, double amount = 1)
    {
        var result = new List<FloraInstance>();
        var g = grid;
        var field = new FloraField(g, seed ^ 0xA0A7UL);
        var weights = new float[ShoreCommunities.Length];
        TerrainSample SampleAt(double x, double z)
        {
            int i = Math.Clamp((int)Math.Round(x / g.Spacing), 0, g.Side - 1), j = Math.Clamp((int)Math.Round(z / g.Spacing), 0, g.Side - 1);
            return g.Samples[j * g.Side + i];
        }
        // Reeds, cattails, rushes: the bank just above the waterline and the shallows (not sea beaches, not fast water).
        Cells(key, .7 / Math.Sqrt(Math.Max(.2, amount)), seed ^ 0x2EEDUL, (x, z, h) =>
        {
            if (g.Edited(x, z) || !g.At(x, z, out float height, out float up, out _, out _) || up < .8f) return;
            var t = SampleAt(x, z);
            float shore = g.ShoreAt(x, z);
            float place;
            if (t.Water == WaterKind.None)
            {
                if (shore < .22f || shore > .66f || height - sea < 1.2) return;
                place = 1 - Math.Abs(shore - .47f) / .25f;
            }
            else
            {
                double depth = t.WaterLevel - t.Height;
                if (t.Water == WaterKind.Ocean || depth > (t.Water == WaterKind.River ? .25 : .55)) return;
                place = (float)(1 - DetMath.Smooth((depth - .15) / .4));
            }
            float wetness = g.Wetness(x, z), clump = field.At(field.Reeds, x, z);
            // Clumps along the bank, not a continuous hedge around every pool.
            if (Unit(h, 1) > place * clump * clump * (.45f + .55f * wetness) * 1.2f) return;
            ulong cell = PatchCell(field, x, z, g.X0 + x, g.Z0 + z, seed ^ 0x5EEDUL);
            weights[0] = 3; weights[1] = 1 + 1.5f * wetness + (t.Water == WaterKind.Pond ? 1 : 0); weights[2] = 2.5f; weights[3] = 2.5f;
            var c = ShoreCommunities[Pick(weights, Hashing.Unit(cell))];
            if (Unit(h, 5) > c.Density) return;
            int species = PickSpecies(c, Unit(h, 2), false);
            var sp = Flora.All[species];
            float size = (sp.MinHeight + (sp.MaxHeight - sp.MinHeight) * Unit(h, 3)) * c.Scale * (.8f + .3f * clump);
            var tint = species is Flora.ShortA or Flora.Lush or Flora.TallC ? GrassTint(g, x, z, .1, .95f + .1f * Unit(h, 4)) : new Color(1, 1, 1) * (.92f + .14f * Unit(h, 4));
            result.Add(new FloraInstance(species, PlantTransform(h, size, new Vector3((float)x, height - .05f, (float)z), .08f), tint));
        });
        // Floating leaves on still water (ponds, lakes), in rafts.
        Cells(key, 1.7 / Math.Sqrt(Math.Max(.2, amount)), seed ^ 0xF10A7UL, (x, z, h) =>
        {
            var t = SampleAt(x, z);
            if (t.Water is not (WaterKind.Pond or WaterKind.Lake)) return;
            double depth = t.WaterLevel - t.Height;
            float band = (float)(DetMath.Smooth((depth - .2) / .4) * (1 - DetMath.Smooth((depth - 1.8) / 1.4)));
            var (ba, bb, share) = biomes.Sample(g.X0 + x, g.Z0 + z);
            var v = (Unit(h, 6) < share ? biomes.Pool[ba] : biomes.Pool[bb]).Def.Vegetation;
            if (v.AquaticPlants == "Hyacinth") return; // bayou: water hyacinth (WetPlants)
            float abundance = Math.Max((float)v.FloatingPlants, t.Water == WaterKind.Pond ? .35f : .2f);
            double raft = DetMath.Smooth((Noise.Fbm(g.X0 + x, g.Z0 + z, 18, 2, 0, seed ^ 0x4AF7UL) - .02) / .3);
            if (Unit(h, 1) > band * raft * abundance * 1.6f) return;
            ulong cell = PatchCell(field, x, z, g.X0 + x, g.Z0 + z, seed ^ 0x11E5UL);
            bool duck = Hashing.Unit(cell) < (t.Water == WaterKind.Pond ? .4 : .15);
            int species = duck ? Flora.Duckweed : Unit(h, 2) < .22f ? Flora.WaterLilies : Flora.LilyPads;
            var sp = Flora.All[species];
            float size = sp.MinHeight + (sp.MaxHeight - sp.MinHeight) * Unit(h, 3);
            var basis = new Basis(Vector3.Up, Unit(h, 4) * Mathf.Tau).Scaled(Vector3.One * size);
            result.Add(new FloraInstance(species, new Transform3D(basis, new Vector3((float)x, (float)t.WaterLevel + .015f + .004f * Unit(h, 5), (float)z)),
                new Color(1, 1, 1) * (.92f + .14f * Unit(h, 7))));
        });
        // Submerged plants: lake, pond and river beds; seagrass meadows, kelp and seaweed in the sea.
        Cells(key, 1.3 / Math.Sqrt(Math.Max(.2, amount)), seed ^ 0x5EA6UL, (x, z, h) =>
        {
            var t = SampleAt(x, z);
            if (t.Water == WaterKind.None) return;
            double depth = t.WaterLevel - t.Height;
            if (depth < .35) return;
            if (!g.At(x, z, out float bed, out float up, out _, out _)) return;
            bed = (float)Math.Min(bed, t.Height);
            bool rocky = up < .93f;
            double wx = g.X0 + x, wz = g.Z0 + z;
            double meadow = Noise.Fbm(wx, wz, 30, 2, 0, seed ^ 0x5EA9UL);
            int species; float chance;
            if (t.Water == WaterKind.Ocean)
            {
                float grassBand = (float)(DetMath.Smooth((depth - .5) / .6) * (1 - DetMath.Smooth((depth - 3.5) / 2.5)));
                float kelpBand = (float)(DetMath.Smooth((depth - 2.5) / 2) * (1 - DetMath.Smooth((depth - 14) / 6)));
                double forestPatch = DetMath.Smooth((Noise.Fbm(wx, wz, 45, 2, 0, seed ^ 0x6E1BUL) - .05) / .3);
                float seagrass = grassBand * (float)DetMath.Smooth((meadow + .05) / .35) * (rocky ? .3f : 1);
                float kelp = kelpBand * (float)forestPatch * (rocky ? 1.2f : .6f);
                float weed = (rocky ? .7f : .1f) * (float)(1 - DetMath.Smooth((depth - 6) / 4));
                float total = seagrass + kelp + weed;
                if (total <= .01f) return;
                double u = Unit(h, 2) * total;
                species = u < seagrass ? (Unit(h, 3) < .7f ? Flora.Seagrass : Flora.EelgrassTall)
                    : u < seagrass + kelp ? Flora.Kelp
                    : Unit(h, 3) < .5f ? Flora.RedAlgae : Flora.GreenAlgae;
                chance = Math.Min(1, total) * .8f;
            }
            else
            {
                float shallow = (float)(DetMath.Smooth((depth - .35) / .4) * (1 - DetMath.Smooth((depth - 2) / 1.5)));
                float deep = (float)(DetMath.Smooth((depth - 1.5) / 1) * (1 - DetMath.Smooth((depth - 5) / 2.5)));
                float patch = (float)DetMath.Smooth((meadow + .1) / .4);
                float total = (shallow + deep) * patch;
                if (total <= .01f) return;
                double u = Unit(h, 2) * (shallow + deep);
                if (t.Water == WaterKind.River)
                    species = rocky ? Flora.GreenAlgae : Unit(h, 3) < .75f ? Flora.EelgrassTall : Flora.Submerged;
                else if (u < shallow)
                {
                    float s = Unit(h, 3);
                    species = s < .4f ? Flora.Submerged : s < .7f ? Flora.EelgrassTall : Flora.Feathery;
                }
                else
                    species = rocky && Unit(h, 3) < .4f ? Flora.GreenAlgae : Unit(h, 3) < .45f ? Flora.Broadleaf : Unit(h, 3) < .75f ? Flora.Mixed : Flora.Feathery;
                chance = Math.Min(1, total) * .7f;
            }
            if (Unit(h, 1) > chance) return;
            var sp = Flora.All[species];
            float size = sp.MinHeight + (sp.MaxHeight - sp.MinHeight) * Unit(h, 4);
            // Below the surface with room to spare: at sea the wave troughs dip ~0.5 m (tips stuck out of the water).
            double room = t.Water == WaterKind.Ocean ? (depth - .7) * .75 : depth - .25;
            if (room < .3) return;
            size = (float)Math.Min(size, room);
            result.Add(new FloraInstance(species, PlantTransform(h, size, new Vector3((float)x, bed - .05f, (float)z), .1f), new Color(1, 1, 1) * (.9f + .15f * Unit(h, 5))));
        });
        return result;
    }
}
