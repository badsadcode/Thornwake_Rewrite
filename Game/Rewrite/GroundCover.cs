using System;
using System.Collections.Generic;
using Godot;
using Thornwake.WorldGen;
using Noise = Thornwake.WorldGen.Noise;

namespace Thornwake.Rewrite;

/// <summary>A distant tree drawn as a camera-facing card: position (tile-local), height, crown colour, conifer or not.</summary>
public readonly record struct FarTree(Vector3 Position, float Height, Color Crown, bool Conifer);

/// <summary>Ground details: rock kinds index into <see cref="GroundCover.RockKinds"/>.</summary>
/// <summary>A rock or prop instance; <paramref name="Tint"/> (rocks): the biome rock colour relative to the default stone (alpha 0 = none).</summary>
public readonly record struct RockInstance(int Kind, Transform3D Transform, Color Tint = default);

/// <summary>
/// Cheap, deterministic scatter that reuses a tile's own sample grid (heights, normals, forest cover, water) instead
/// of sampling the terrain again: grass tufts, stones, rocks, boulders and outcrops near the player, and distant tree
/// cards far away. Every item sits on a world-aligned cell, so it does not move when a tile changes level.
/// Heights are interpolated exactly like the rendered triangles, so nothing floats or sinks.
/// </summary>
public static class GroundCover
{
    public const int Stone = 0, Rock = 1, Boulder = 2, CliffSlab = 3, BigBoulder = 4;
    public const int Bush = 0, BerryBush = 1, Stump = 2, Log = 3;
    public static readonly string[] RockKinds = { "stone", "rock", "boulder" };

    /// <summary>Grid view of a built tile (33×33 vertices) used for interpolation.</summary>
    public readonly struct Grid
    {
        public readonly Vector3[] Vertices, Normals; public readonly double[] Forest; public readonly TerrainSample[] Samples;
        public readonly double Spacing, X0, Z0;
        public readonly int Side;
        /// <summary>Per vertex 0.5 + depth below the nearest water surface / 2 m (0.5 = waterline), as in the tile colours.</summary>
        public readonly float[] Shore;
        public Grid(Vector3[] vertices, Vector3[] normals, double[] forest, TerrainSample[] samples, double spacing, double x0, double z0, int side, float[] shore,
            Vector2[]? uv2 = null, float[]? biome1 = null, float[]? props = null, float[]? biome3 = null)
        { Vertices = vertices; Normals = normals; Forest = forest; Samples = samples; Spacing = spacing; X0 = x0; Z0 = z0; Side = side; Shore = shore; EditedAmount = uv2; Biome1 = biome1; PropScale = props; Biome3 = biome3; }

        /// <summary>Per vertex biome data (rgb rock colour, a = wetness), as on the terrain mesh.</summary>
        public readonly float[]? Biome3;

        /// <summary>Biome rock colour at tile-local (x, z) relative to the default stone (multiplier for rock meshes).</summary>
        public Color RockTint(double x, double z)
        {
            if (Biome3 == null) return Colors.White;
            int i = Math.Clamp((int)Math.Round(x / Spacing), 0, Side - 1), j = Math.Clamp((int)Math.Round(z / Spacing), 0, Side - 1);
            int k = (j * Side + i) * 4;
            // Default biome rock 77766a ≈ (0.47, 0.46, 0.42); vertex stone colour carries that already.
            // Alpha carries the rock look (index + 1) / 8 for Rock.gdshader.
            float look = PropScale == null ? 0 : PropScale[(j * Side + i) * 3 + 2];
            return new Color(Biome3[k] / .467f, Biome3[k + 1] / .463f, Biome3[k + 2] / .416f, (look + 1) / 8f);
        }

        /// <summary>Biome wetness (0 … 1; swamps ~0.8) at tile-local (x, z).</summary>
        public float Wetness(double x, double z)
        {
            if (Biome3 == null) return 0;
            int i = Math.Clamp((int)Math.Round(x / Spacing), 0, Side - 1), j = Math.Clamp((int)Math.Round(z / Spacing), 0, Side - 1);
            return Biome3[(j * Side + i) * 4 + 3];
        }

        /// <summary>Per vertex biome data (rgb dry colour, a = plant cover), as on the terrain mesh.</summary>
        public readonly float[]? Biome1;

        /// <summary>Biome plant cover (0 bare … 1 normal) at tile-local (x, z), nearest vertex.</summary>
        public float Plants(double x, double z)
        {
            if (Biome1 == null) return 1;
            int i = Math.Clamp((int)Math.Round(x / Spacing), 0, Side - 1), j = Math.Clamp((int)Math.Round(z / Spacing), 0, Side - 1);
            return Biome1[(j * Side + i) * 4 + 3];
        }

        /// <summary>Per vertex biome prop multipliers (rocks, bushes/logs).</summary>
        public readonly float[]? PropScale;

        /// <summary>Biome multiplier for rocks (kind 0) or bushes, stumps and logs (kind 1) at tile-local (x, z) (kind 2: rock look).</summary>
        public float Props(double x, double z, int kind)
        {
            if (PropScale == null) return 1;
            int i = Math.Clamp((int)Math.Round(x / Spacing), 0, Side - 1), j = Math.Clamp((int)Math.Round(z / Spacing), 0, Side - 1);
            return PropScale[(j * Side + i) * 3 + kind];
        }

        /// <summary>Bilinear <see cref="Shore"/> at tile-local (x, z).</summary>
        /// <summary>True where the player has dug or raised the ground noticeably (bare soil: no grass).</summary>
        public bool Edited(double x, double z)
        {
            int i = Math.Clamp((int)Math.Round(x / Spacing), 0, Side - 1), j = Math.Clamp((int)Math.Round(z / Spacing), 0, Side - 1);
            return EditedAmount != null && EditedAmount[j * Side + i].Y > .15f;
        }
        public readonly Vector2[]? EditedAmount;

        public float ShoreAt(double x, double z)
        {
            double gx = Math.Clamp(x / Spacing, 0, Side - 1.001), gz = Math.Clamp(z / Spacing, 0, Side - 1.001);
            int i = (int)gx, j = (int)gz; float fx = (float)(gx - i), fz = (float)(gz - j);
            int a = j * Side + i;
            return Shore[a] * (1 - fx) * (1 - fz) + Shore[a + 1] * fx * (1 - fz) + Shore[a + Side] * (1 - fx) * fz + Shore[a + Side + 1] * fx * fz;
        }

        /// <summary>Height, normal Y, forest and wetness at tile-local (x, z), matching the mesh triangles (a,b,c / b,d,c).</summary>
        public bool At(double x, double z, out float height, out float up, out float forest, out bool wet)
        {
            height = 0; up = 0; forest = 0; wet = false;
            double gx = x / Spacing, gz = z / Spacing;
            int i = (int)Math.Floor(gx), j = (int)Math.Floor(gz);
            if (i < 0 || j < 0 || i >= Side - 1 || j >= Side - 1) return false;
            double fx = gx - i, fz = gz - j;
            int n = Side, a = j * n + i, b = a + 1, c = a + n, d = c + 1;
            double ha = Vertices[a].Y, hb = Vertices[b].Y, hc = Vertices[c].Y, hd = Vertices[d].Y;
            height = (float)(fx + fz <= 1 ? ha + (hb - ha) * fx + (hc - ha) * fz : hd + (hc - hd) * (1 - fx) + (hb - hd) * (1 - fz));
            int nearest = (fz < .5 ? j : j + 1) * n + (fx < .5 ? i : i + 1);
            up = Normals[nearest].Y;
            forest = (float)(Forest[a] * (1 - fx) * (1 - fz) + Forest[b] * fx * (1 - fz) + Forest[c] * (1 - fx) * fz + Forest[d] * fx * fz);
            wet = Samples[a].Water != WaterKind.None || Samples[b].Water != WaterKind.None || Samples[c].Water != WaterKind.None || Samples[d].Water != WaterKind.None;
            return true;
        }

        public Vector3 NormalAt(double x, double z)
        {
            int i = Math.Clamp((int)Math.Round(x / Spacing), 0, Side - 1), j = Math.Clamp((int)Math.Round(z / Spacing), 0, Side - 1);
            return Normals[j * Side + i];
        }

        public float Mountain(double x, double z)
        {
            int i = Math.Clamp((int)Math.Round(x / Spacing), 0, Side - 1), j = Math.Clamp((int)Math.Round(z / Spacing), 0, Side - 1);
            return (float)Samples[j * Side + i].Mountain;
        }
    }

    /// <summary>Calls <paramref name="emit"/> for each world-aligned cell of size <paramref name="cell"/> inside the tile.</summary>
    private static void Cells(TileKey key, double cell, ulong salt, Action<double, double, ulong> emit)
    {
        long c0x = (long)Math.Floor(key.MinX / cell), c0z = (long)Math.Floor(key.MinZ / cell);
        long c1x = (long)Math.Ceiling((key.MinX + key.Size) / cell), c1z = (long)Math.Ceiling((key.MinZ + key.Size) / cell);
        for (long cz = c0z; cz < c1z; cz++)
            for (long cx = c0x; cx < c1x; cx++)
            {
                ulong h = Hashing.Lattice(cx, cz, salt);
                double x = (cx + Hashing.Unit(h)) * cell - key.MinX, z = (cz + Hashing.Unit(Hashing.Mix(h))) * cell - key.MinZ;
                if (x < 0 || z < 0 || x >= key.Size || z >= key.Size) continue;
                emit(x, z, Hashing.Mix(h ^ 0x5DEECE66DUL));
            }
    }

    private static float Unit(ulong h, int shift) => (float)Hashing.Unit(Hashing.Mix(h + (ulong)shift));

    /// <summary>Grass tufts (level 0 only): dense in meadows, thin in forest, none on rock, sand or in water.</summary>
    public static List<Transform3D> Grass(TileKey key, in Grid grid, double sea, ulong seed, double amount = 1)
    {
        var result = new List<Transform3D>();
        var g = grid;
        // Default amount 1: ~1.3 tufts per m² in meadows (cell 0.88 m); the cell shrinks as the amount grows.
        Cells(key, 1.25 / Math.Sqrt(2 * amount), seed ^ 0x6A55UL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out float forest, out bool wet) || wet || g.Edited(x, z)) return;
            if (up < .82f || height - sea < 1.6) return;
            float alpine = (float)DetMath.Smooth((height - sea - 1700) / 500);
            float plants = g.Plants(x, z); // biome plant cover, squared: sparse biomes (desert, ash, tundra) get only odd tufts
            float density = (1 - .65f * forest) * (1 - alpine) * (float)DetMath.Smooth((up - .82) / .08) * plants * plants;
            if (Unit(h, 1) > density * .9f) return;
            float scale = .7f + .8f * Unit(h, 2);
            result.Add(new Transform3D(new Basis(Vector3.Up, Unit(h, 3) * Mathf.Tau).Scaled(Vector3.One * scale), new Vector3((float)x, height - .02f, (float)z)));
        });
        return result;
    }

    /// <summary>
    /// Reeds along river, lake and wetland pool banks (level 0): tall tufts in clumps from ~0.5 m above the waterline
    /// into ~0.3 m deep water, densest at the waterline. Not on sea shores (beaches).
    /// </summary>
    public static List<Transform3D> Reeds(TileKey key, in Grid grid, double sea, ulong seed, double amount = 1)
    {
        var result = new List<Transform3D>();
        var g = grid;
        Cells(key, .55 / Math.Sqrt(Math.Max(.2, amount)), seed ^ 0x2EEDUL, (x, z, h) =>
        {
            float shore = g.ShoreAt(x, z);
            if (shore < .22f || shore > .66f || g.Edited(x, z)) return;
            if (!g.At(x, z, out float height, out float up, out _, out _) || up < .8f || height - sea < 1.2) return;
            // Clumps: ~45 % of 5 m patches carry reeds; wetland pools (swamp, marsh, fen) are lined with them.
            ulong patch = Hashing.Lattice((long)Math.Floor((x + key.MinX) / 5), (long)Math.Floor((z + key.MinZ) / 5), seed ^ 0x9A7CUL);
            if (Hashing.Unit(patch) > .45 + .5 * Math.Min(1, g.Wetness(x, z))) return;
            float closeness = 1 - Math.Abs(shore - .47f) / .22f;
            if (Unit(h, 1) > closeness * .85f) return;
            float tall = 2.1f + 1.3f * Unit(h, 2), wide = .55f + .35f * Unit(h, 4);
            var basis = new Basis(Vector3.Up, Unit(h, 3) * Mathf.Tau) * new Basis(Vector3.Right, (Unit(h, 5) - .5f) * .25f);
            result.Add(new Transform3D(basis.Scaled(new Vector3(wide, tall, wide)), new Vector3((float)x, height - .05f, (float)z)));
        });
        return result;
    }

    /// <summary>Stones (level 0), rocks (levels 0–1), boulders and outcrop clusters (levels 0–2).</summary>
    public static List<RockInstance> Rocks(TileKey key, in Grid grid, double sea, ulong seed, double amount = 1)
    {
        float a = (float)amount;
        float a0() => Math.Min(1, a);
        var result = new List<RockInstance>();
        var g = grid;
        void Place(int kind, double x, double z, ulong h, float baseSize, float sizeRange, float sink)
        {
            if (!g.At(x, z, out float height, out float up, out _, out bool wet)) return;
            if (wet && kind == Stone) return;
            float size = baseSize + sizeRange * Unit(h, 5) * Unit(h, 6);
            // Random tilt, partly buried so rocks sit in the ground rather than on it.
            var basis = new Basis(Vector3.Up, Unit(h, 7) * Mathf.Tau) * new Basis(Vector3.Right, (Unit(h, 8) - .5f) * .6f);
            basis = basis.Scaled(new Vector3(size * (.8f + .5f * Unit(h, 9)), size * (.55f + .5f * Unit(h, 10)), size * (.8f + .5f * Unit(h, 11))));
            result.Add(new RockInstance(kind, new Transform3D(basis, new Vector3((float)x, height - size * sink, (float)z)), g.RockTint(x, z)));
        }
        if (key.Level == 0)
            Cells(key, 3.5, seed ^ 0x570EUL, (x, z, h) =>
            {
                if (!g.At(x, z, out float height, out float up, out _, out _) || height - sea < .3) return;
                float steep = 1 - up;
                if (Unit(h, 1) > (.1f + 1.2f * steep) * a * g.Props(x, z, 0)) return; // more stones on slopes and banks
                Place(Stone, x, z, h, .12f, .45f, .35f);
            });
        if (key.Level <= 1)
            Cells(key, 14, seed ^ 0x70C4UL, (x, z, h) =>
            {
                if (!g.At(x, z, out float height, out float up, out _, out _) || height - sea < .5) return;
                float steep = 1 - up, mountain = g.Mountain(x, z);
                if (Unit(h, 1) > (.05f + 1.5f * steep + .3f * mountain) * a * g.Props(x, z, 0)) return;
                Place(Rock, x, z, h, .5f, 1.2f, .3f);
            });
        Cells(key, 48, seed ^ 0xB0D1UL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out _, out _) || height - sea < 1) return;
            float steep = 1 - up, mountain = g.Mountain(x, z);
            float chance = (.04f + 2.2f * Math.Max(0, steep - .1f) + .5f * mountain) * a * g.Props(x, z, 0);
            if (Unit(h, 1) > chance) return;
            // Steep or mountainous ground grows a small outcrop: one big boulder with smaller ones around it.
            int extra = steep > .3f || mountain > .3f ? 2 + (int)(Unit(h, 2) * 4) : 0;
            Place(Boulder, x, z, h, 1.8f, 3.5f + 3 * mountain, .35f);
            for (int i = 0; i < extra; i++)
            {
                ulong hh = Hashing.Mix(h + (ulong)(i + 1) * 0x9E37UL);
                double ox = x + (Unit(hh, 1) - .5f) * 9, oz = z + (Unit(hh, 2) - .5f) * 9;
                Place(Boulder, ox, oz, hh, 1f, 2.5f, .4f);
            }
        });
        // Rock walls on cliff faces: big flat-faced slabs set into the face, hiding the stretched ground (nearest tiles
        // only: coarser tiles' ground is too far from the real face to seat them).
        if (key.Level == 0)
        Cells(key, 6.5, seed ^ 0xC1FFUL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out _, out bool wet) || up > .55f || height - sea < -1) return;
            var n = g.NormalAt(x, z);
            var outward = new Vector3(n.X, 0, n.Z);
            if (outward.LengthSquared() < .05f) return;
            outward = outward.Normalized();
            // A real face: the ground keeps dropping in front of it.
            if (!g.At(x + outward.X * 4, z + outward.Z * 4, out float below, out _, out _, out _) || height - below < 2.5f) return;
            if (Unit(h, 1) > .75f * a0()) return;
            var along = Vector3.Up.Cross(outward).Normalized();
            float wide = 3 + 4 * Unit(h, 2), tall = 2.5f + 4 * Unit(h, 3), thick = wide * (.55f + .25f * Unit(h, 4)); // chunky blocks, not plates
            var tilt = new Basis(along, (Unit(h, 5) - .5f) * .3f) * new Basis(outward, (Unit(h, 6) - .5f) * .25f);
            var basis = tilt * new Basis(along * wide, Vector3.Up * tall, outward * thick);
            var at = new Vector3((float)x, height - tall * .2f, (float)z) - outward * thick * .45f;
            result.Add(new RockInstance(CliffSlab, new Transform3D(basis, at), g.RockTint(x, z)));
        });
        // Huge boulders, rare, on fairly open ground.
        if (key.Level <= 2)
            Cells(key, 140, seed ^ 0xB16BUL, (x, z, h) =>
            {
                if (!g.At(x, z, out float height, out float up, out _, out bool wet) || wet || up < .8f || height - sea < 1) return;
                if (Unit(h, 1) > .22f * a0()) return;
                float size = 5 + 7 * Unit(h, 2) * Unit(h, 3);
                var basis = new Basis(Vector3.Up, Unit(h, 4) * Mathf.Tau) * new Basis(Vector3.Right, (Unit(h, 5) - .5f) * .3f);
                basis = basis.Scaled(new Vector3(size * (.9f + .4f * Unit(h, 6)), size * (.6f + .35f * Unit(h, 7)), size * (.9f + .4f * Unit(h, 8))));
                result.Add(new RockInstance(BigBoulder, new Transform3D(basis, new Vector3((float)x, height - size * .25f, (float)z)), g.RockTint(x, z)));
            });
        // Rocky shores: in patches along the sea and lakes, blocky rocks at and just above the waterline.
        if (key.Level <= 1)
            Cells(key, 3.2, seed ^ 0x5407EUL, (x, z, h) =>
            {
                if (!g.At(x, z, out float height, out _, out _, out _)) return;
                if (RockyShore(g.X0 + x, g.Z0 + z, height - sea) < .3f || Unit(h, 1) > .45f * a0()) return;
                float size = .8f + 2.6f * Unit(h, 2) * Unit(h, 2);
                var basis = new Basis(Vector3.Up, Unit(h, 3) * Mathf.Tau) * new Basis(Vector3.Right, (Unit(h, 4) - .5f) * .5f);
                basis = basis.Scaled(new Vector3(size * (.8f + .5f * Unit(h, 5)), size * (.5f + .4f * Unit(h, 6)), size * (.8f + .5f * Unit(h, 7))));
                result.Add(new RockInstance(BigBoulder, new Transform3D(basis, new Vector3((float)x, height - size * .3f, (float)z)), g.RockTint(x, z)));
            });
        // Scree at the foot of cliffs: flat ground with a face rising a few metres right next to it.
        if (key.Level <= 1)
            Cells(key, 5, seed ^ 0x5C2EEUL, (x, z, h) =>
            {
                if (!g.At(x, z, out float height, out float up, out _, out bool wet) || wet || up < .75f || height - sea < -.2) return;
                for (int d = 0; d < 8; d++)
                {
                    float a = d * Mathf.Pi / 4, dx = Mathf.Cos(a), dz = Mathf.Sin(a);
                    if (!g.At(x + dx * 3.5, z + dz * 3.5, out float above, out _, out _, out _) || above - height < 2.8f) continue;
                    if (Unit(h, 1) > .7f * a0()) return;
                    int count = 1 + (int)(Unit(h, 2) * 3);
                    for (int i = 0; i < count; i++)
                    {
                        ulong hh = Hashing.Mix(h + (ulong)(i + 3) * 0x51EDUL);
                        double bx = x + dx * (.8 + Unit(hh, 1) * 1.2) + (Unit(hh, 2) - .5f) * 3, bz = z + dz * (.8 + Unit(hh, 1) * 1.2) + (Unit(hh, 3) - .5f) * 3;
                        Place(i == 0 ? Boulder : Rock, bx, bz, hh, i == 0 ? .9f : .5f, i == 0 ? 1.6f : 1f, .35f);
                    }
                    return;
                }
            });
        return result;
    }

    /// <summary>
    /// Rocky shore strength 0..1 at a world point a given height above the sea or lake surface: shore patches (from a
    /// world noise, so neighbouring tiles agree) between ~1 m under and ~2.5 m above the water. Also bares the ground
    /// to rock in the terrain colours (TileBuilder).
    /// </summary>
    public static float RockyShore(double worldX, double worldZ, double aboveWater)
    {
        if (aboveWater < -1.2 || aboveWater > 3) return 0;
        double patch = Noise.Value(worldX / 160, worldZ / 160, 0x5407EUL) * .7 + Noise.Value(worldX / 45, worldZ / 45, 0x5408UL) * .3;
        float band = (float)(DetMath.Smooth((aboveWater + 1.2) / .6) * (1 - DetMath.Smooth((aboveWater - 1.8) / 1.2)));
        return (float)DetMath.Smooth((patch - .15) / .25) * band;
    }

    /// <summary>
    /// Cliff dressing (level 0): where flat ground ends at a drop of a few metres (sea cliffs, river bluffs, crags),
    /// grass hangs over the lip (kind 0, where plants grow) and roots poke out of the face under it (kind 1, near trees),
    /// breaking up the stretched-looking vertical faces.
    /// </summary>
    public static List<RockInstance> CliffDressing(TileKey key, in Grid grid, double sea, ulong seed)
    {
        var result = new List<RockInstance>();
        var g = grid;
        Cells(key, 2.3, seed ^ 0xC11FUL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out float forest, out bool wet) || wet || up < .8f || height - sea < .5) return;
            if (Unit(h, 9) < .35f) return; // irregular, never a continuous fringe
            if (!Drop(g, x, z, height, up, out float ox, out float oz, out float depth)) return;
            float plants = g.Plants(x, z);
            bool roots = Unit(h, 1) < .25f + .5f * forest;
            if (!roots && Unit(h, 2) > plants * 1.1f) return;
            // Hang just over the edge, facing out; longer over taller faces.
            float length = Math.Clamp(depth / 2.6f, .45f, 1.3f) * (.75f + .5f * Unit(h, 3));
            float width = .8f + .5f * Unit(h, 4);
            var outward = new Vector3(ox, 0, oz);
            var right = Vector3.Up.Cross(outward).Normalized();
            var basis = new Basis(right * width, Vector3.Up * length, outward * (.8f + .4f * Unit(h, 5)));
            var lip = new Vector3((float)x, height + .04f, (float)z) + outward * (.35f + .3f * Unit(h, 6));
            if (roots) lip += Vector3.Down * (.15f + .3f * Unit(h, 7));
            result.Add(new RockInstance(roots ? 1 : 0, new Transform3D(basis, lip)));
        });
        return result;
    }

    /// <summary>
    /// A real cliff next to (x, z): the ground drops at ≥ ~60° right past the point and keeps dropping (≥ 3.5 m within
    /// 3.2 m), and the face is much steeper than the ground on top (plain steep slopes are not cliffs).
    /// </summary>
    private static bool Drop(in Grid g, double x, double z, float height, float up, out float ox, out float oz, out float depth)
    {
        ox = oz = depth = 0;
        float topSlope = Mathf.Sqrt(Math.Max(0, 1 - up * up)) / Math.Max(.2f, up);
        for (int d = 0; d < 8; d++)
        {
            float a = d * Mathf.Pi / 4, dx = Mathf.Cos(a), dz = Mathf.Sin(a);
            if (!g.At(x + dx * 1.6, z + dz * 1.6, out float near, out _, out _, out _) || !g.At(x + dx * 3.2, z + dz * 3.2, out float far, out _, out _, out _)) continue;
            float faceSlope = (height - near) / 1.6f, drop = height - far;
            if (faceSlope > 1.7f && faceSlope > topSlope + 1.2f && drop > 3.5f && drop > depth) { depth = drop; ox = dx; oz = dz; }
        }
        return depth > 0;
    }

    /// <summary>
    /// Wetland plants (levels 0–1; Tom, round 8): low bog shrubs and sedges (fens) or pale tussocks (marshes) on the
    /// ground between the pools, lily pads or water hyacinth mats floating on them. Amounts and kinds come from the
    /// biome (Vegetation.Shrubs, ShrubKind, FloatingPlants, AquaticPlants). Kinds are <see cref="PlaceholderPlants"/>
    /// wetland plant kinds.
    /// </summary>
    public static List<RockInstance> WetPlants(TileKey key, in Grid grid, double sea, ulong seed, BiomeMap biomes, double amount = 1)
    {
        var result = new List<RockInstance>();
        var g = grid;
        float a = (float)amount;
        // Shrubs and grass clumps on dry ground.
        Cells(key, 2.4, seed ^ 0xB06UL, (x, z, h) =>
        {
            if (g.Wetness(x, z) < .4f || g.Edited(x, z)) return;
            if (!g.At(x, z, out float height, out float up, out float forest, out bool wet) || wet || up < .85f || height - sea < .8) return;
            var (ba, bb, share) = biomes.Sample(g.X0 + x, g.Z0 + z);
            var v = (Unit(h, 6) < share ? biomes.Pool[ba] : biomes.Pool[bb]).Def.Vegetation;
            if (v.Shrubs <= 0 || Unit(h, 1) > v.Shrubs * .55f * a * (1 - .6f * forest)) return;
            int kind = v.ShrubKind == "tussock" ? PlaceholderPlants.Tussock : Unit(h, 2) < .55f ? PlaceholderPlants.BogShrub : PlaceholderPlants.Sedge;
            float scale = .7f + .6f * Unit(h, 3);
            result.Add(new RockInstance(kind, new Transform3D(new Basis(Vector3.Up, Unit(h, 4) * Mathf.Tau).Scaled(Vector3.One * scale), new Vector3((float)x, height - .04f, (float)z))));
        });
        // Floating plants on wetland pools (not too shallow), in loose rafts.
        Cells(key, 3.2, seed ^ 0xF10A7UL, (x, z, h) =>
        {
            int i = Math.Clamp((int)Math.Round(x / g.Spacing), 0, g.Side - 1), j = Math.Clamp((int)Math.Round(z / g.Spacing), 0, g.Side - 1);
            var t = g.Samples[j * g.Side + i];
            if (t.Water != WaterKind.Pond || t.WaterLevel - t.Height < .3) return;
            var (ba, bb, share) = biomes.Sample(g.X0 + x, g.Z0 + z);
            var v = (Unit(h, 6) < share ? biomes.Pool[ba] : biomes.Pool[bb]).Def.Vegetation;
            if (v.FloatingPlants <= 0) return;
            ulong raft = Hashing.Lattice((long)Math.Floor((x + key.MinX) / 11), (long)Math.Floor((z + key.MinZ) / 11), seed ^ 0x4AF7UL);
            if (Hashing.Unit(raft) > .3 + .5 * v.FloatingPlants || Unit(h, 1) > v.FloatingPlants * a) return;
            int kind = v.AquaticPlants == "Hyacinth" ? PlaceholderPlants.Hyacinth : PlaceholderPlants.LilyPads;
            float scale = .8f + .5f * Unit(h, 3);
            result.Add(new RockInstance(kind, new Transform3D(new Basis(Vector3.Up, Unit(h, 4) * Mathf.Tau).Scaled(Vector3.One * scale), new Vector3((float)x, (float)t.WaterLevel, (float)z))));
        });
        return result;
    }

    /// <summary>
    /// Bushes along forest edges and in meadows, stumps and fallen logs inside forests (levels 0–1).
    /// Placeholders from the valley game's models until Tom's scatter assets arrive.
    /// </summary>
    public static List<RockInstance> Props(TileKey key, in Grid grid, double sea, ulong seed, double amount = 1)
    {
        float a = (float)amount;
        var result = new List<RockInstance>();
        var g = grid;
        Cells(key, 9, seed ^ 0xB054UL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out float forest, out bool wet) || wet || up < .8f || height - sea < 2) return;
            // Most bushes at forest edges (cover around 0.3–0.7), a few out in the meadows.
            float edge = 1 - Math.Abs(forest - .5f) * 2;
            // Biome: bush amount × plant cover (few bushes in deserts, on ash or ice).
            if (Unit(h, 1) > (.06f + .4f * Math.Max(0, edge)) * a * g.Props(x, z, 1) * Math.Min(1, g.Plants(x, z) * 1.5f)) return;
            float scale = .7f + .7f * Unit(h, 2);
            int kind = Unit(h, 3) < .25f ? BerryBush : Bush;
            result.Add(new RockInstance(kind, new Transform3D(new Basis(Vector3.Up, Unit(h, 4) * Mathf.Tau).Scaled(Vector3.One * scale), new Vector3((float)x, height - .05f, (float)z))));
        });
        Cells(key, 24, seed ^ 0x1065UL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out float forest, out bool wet) || wet || up < .85f || forest < .45f) return;
            if (Unit(h, 1) > .45f * forest * a * g.Props(x, z, 1)) return;
            int kind = Unit(h, 2) < .5f ? Stump : Log;
            float scale = .8f + .5f * Unit(h, 3);
            var basis = new Basis(Vector3.Up, Unit(h, 4) * Mathf.Tau).Scaled(Vector3.One * scale);
            result.Add(new RockInstance(kind, new Transform3D(basis, new Vector3((float)x, height - (kind == Log ? .08f : .03f), (float)z))));
        });
        return result;
    }

    // Crown colours per species (linear, approximating each Blender tree's foliage), and conifer flags.
    private static readonly Color[] Crown =
    {
        new(.10f, .17f, .09f), new(.11f, .18f, .09f), new(.10f, .16f, .08f), new(.08f, .14f, .08f), new(.09f, .15f, .08f), // pines, spruces
        new(.16f, .25f, .09f), new(.15f, .24f, .08f), new(.17f, .26f, .09f),                                                  // oaks
        new(.24f, .34f, .12f), new(.23f, .33f, .11f), new(.25f, .35f, .12f),                                                  // green birches
        new(.42f, .14f, .06f), new(.18f, .28f, .10f), new(.22f, .32f, .12f), new(.17f, .23f, .10f),                         // maple, poplar, willow, sparse
        new(.50f, .36f, .09f), new(.48f, .33f, .08f),                                                                         // autumn birches
        new(.15f, .23f, .08f), new(.16f, .24f, .08f), new(.10f, .19f, .09f), new(.10f, .18f, .09f),                         // palms, cacti
        new(.20f, .18f, .15f), new(.18f, .16f, .13f), new(.19f, .22f, .08f), new(.07f, .16f, .06f), new(.09f, .14f, .08f),  // dead, acacia, jungle, cypress
        new(.22f, .18f, .12f),                                                                                                // dry shrub
        new(.12f, .17f, .08f), new(.12f, .17f, .08f),                                                                         // bald cypress
    };
    private static readonly bool[] ConiferSpecies = { true, true, true, true, true, false, false, false, false, false, false, false, false, false, true, false, false,
        false, false, true, true, true, true, false, false, false, false, false, false };
    private static readonly float[] HeightOf = { 16, 15, 17, 19, 18, 13, 14, 12, 14, 13, 15, 12, 18, 11, 9, 13, 13, 11, 7, 5, 1.3f, 7, 4, 6, 20, 13, 1.5f, 17, 13 };

    /// <summary>Distant tree cards (beyond the full trees, levels 3–5 by default): sparser but larger with distance;
    /// the terrain tint fills in beyond.</summary>
    public static List<FarTree> FarTrees(TileKey key, in Grid grid, double sea, ulong seed, double density = 1)
    {
        var result = new List<FarTree>();
        var g = grid;
        // Cell and size per level so cards cover the ground about as much as the full trees they replace
        // (trees: one per ~6.4 m cell).
        double cell = key.Level switch { <= 2 => 7, 3 => 8, 4 => 13, 5 => 21, _ => 34 };
        float grow = key.Level switch { <= 2 => 1.05f, 3 => 1.2f, 4 => 1.6f, 5 => 2.2f, _ => 3f };
        float keep = (float)Math.Min(1.15, density);
        Cells(key, cell, seed ^ 0xFA77EEUL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out float forest, out bool wet) || wet || up < .65f) return;
            if (Unit(h, 1) > forest * .92f * keep) return;
            double elevation = height - sea;
            int[] list = elevation < 550 ? new[] { 5, 6, 7, 8, 9, 10, 11, 12, 15, 16, 0, 1 } : elevation < 1250 ? new[] { 0, 1, 2, 3, 4, 8, 9, 5, 6, 15 } : new[] { 0, 1, 2, 3, 4, 3, 4, 14 };
            int species = list[(int)(Hashing.Mix(h + 99) % (ulong)list.Length)];
            float scale = (.8f + .45f * Unit(h, 2)) * grow;
            result.Add(new FarTree(new Vector3((float)x, height - .3f, (float)z), HeightOf[species] * scale, Crown[species], ConiferSpecies[species]));
        });
        return result;
    }
}
