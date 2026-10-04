using System;
using System.Collections.Generic;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>A distant tree drawn as a camera-facing card: position (tile-local), height, crown colour, conifer or not.</summary>
public readonly record struct FarTree(Vector3 Position, float Height, Color Crown, bool Conifer);

/// <summary>Ground details: rock kinds index into <see cref="GroundCover.RockKinds"/>.</summary>
public readonly record struct RockInstance(int Kind, Transform3D Transform);

/// <summary>
/// Cheap, deterministic scatter that reuses a tile's own sample grid (heights, normals, forest cover, water) instead
/// of sampling the terrain again: grass tufts, stones, rocks, boulders and outcrops near the player, and distant tree
/// cards far away. Every item sits on a world-aligned cell, so it does not move when a tile changes level.
/// Heights are interpolated exactly like the rendered triangles, so nothing floats or sinks.
/// </summary>
public static class GroundCover
{
    public const int Stone = 0, Rock = 1, Boulder = 2;
    public const int Bush = 0, BerryBush = 1, Stump = 2, Log = 3;
    public static readonly string[] RockKinds = { "stone", "rock", "boulder" };

    /// <summary>Grid view of a built tile (33×33 vertices) used for interpolation.</summary>
    public readonly struct Grid
    {
        public readonly Vector3[] Vertices, Normals; public readonly double[] Forest; public readonly TerrainSample[] Samples;
        public readonly double Spacing, X0, Z0;
        public Grid(Vector3[] vertices, Vector3[] normals, double[] forest, TerrainSample[] samples, double spacing, double x0, double z0)
        { Vertices = vertices; Normals = normals; Forest = forest; Samples = samples; Spacing = spacing; X0 = x0; Z0 = z0; }

        /// <summary>Height, normal Y, forest and wetness at tile-local (x, z), matching the mesh triangles (a,b,c / b,d,c).</summary>
        public bool At(double x, double z, out float height, out float up, out float forest, out bool wet)
        {
            height = 0; up = 0; forest = 0; wet = false;
            double gx = x / Spacing, gz = z / Spacing;
            int i = (int)Math.Floor(gx), j = (int)Math.Floor(gz);
            if (i < 0 || j < 0 || i >= TileBuilder.Cells || j >= TileBuilder.Cells) return false;
            double fx = gx - i, fz = gz - j;
            int n = TileBuilder.Side, a = j * n + i, b = a + 1, c = a + n, d = c + 1;
            double ha = Vertices[a].Y, hb = Vertices[b].Y, hc = Vertices[c].Y, hd = Vertices[d].Y;
            height = (float)(fx + fz <= 1 ? ha + (hb - ha) * fx + (hc - ha) * fz : hd + (hc - hd) * (1 - fx) + (hb - hd) * (1 - fz));
            int nearest = (fz < .5 ? j : j + 1) * n + (fx < .5 ? i : i + 1);
            up = Normals[nearest].Y;
            forest = (float)(Forest[a] * (1 - fx) * (1 - fz) + Forest[b] * fx * (1 - fz) + Forest[c] * (1 - fx) * fz + Forest[d] * fx * fz);
            wet = Samples[a].Water != WaterKind.None || Samples[b].Water != WaterKind.None || Samples[c].Water != WaterKind.None || Samples[d].Water != WaterKind.None;
            return true;
        }

        public float Mountain(double x, double z)
        {
            int i = Math.Clamp((int)Math.Round(x / Spacing), 0, TileBuilder.Side - 1), j = Math.Clamp((int)Math.Round(z / Spacing), 0, TileBuilder.Side - 1);
            return (float)Samples[j * TileBuilder.Side + i].Mountain;
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
    public static List<Transform3D> Grass(TileKey key, in Grid grid, double sea, ulong seed)
    {
        var result = new List<Transform3D>();
        var g = grid;
        Cells(key, 1.25, seed ^ 0x6A55UL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out float forest, out bool wet) || wet) return;
            if (up < .82f || height - sea < 1.6) return;
            float alpine = (float)DetMath.Smooth((height - sea - 1700) / 500);
            float density = (1 - .65f * forest) * (1 - alpine) * (float)DetMath.Smooth((up - .82) / .08);
            if (Unit(h, 1) > density * .9f) return;
            float scale = .7f + .8f * Unit(h, 2);
            result.Add(new Transform3D(new Basis(Vector3.Up, Unit(h, 3) * Mathf.Tau).Scaled(Vector3.One * scale), new Vector3((float)x, height - .02f, (float)z)));
        });
        return result;
    }

    /// <summary>Stones (level 0), rocks (levels 0–1), boulders and outcrop clusters (levels 0–2).</summary>
    public static List<RockInstance> Rocks(TileKey key, in Grid grid, double sea, ulong seed)
    {
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
            result.Add(new RockInstance(kind, new Transform3D(basis, new Vector3((float)x, height - size * sink, (float)z))));
        }
        if (key.Level == 0)
            Cells(key, 3.5, seed ^ 0x570EUL, (x, z, h) =>
            {
                if (!g.At(x, z, out float height, out float up, out _, out _) || height - sea < .3) return;
                float steep = 1 - up;
                if (Unit(h, 1) > .1f + 1.2f * steep) return; // more stones on slopes and banks
                Place(Stone, x, z, h, .12f, .45f, .35f);
            });
        if (key.Level <= 1)
            Cells(key, 14, seed ^ 0x70C4UL, (x, z, h) =>
            {
                if (!g.At(x, z, out float height, out float up, out _, out _) || height - sea < .5) return;
                float steep = 1 - up, mountain = g.Mountain(x, z);
                if (Unit(h, 1) > .05f + 1.5f * steep + .3f * mountain) return;
                Place(Rock, x, z, h, .5f, 1.2f, .3f);
            });
        Cells(key, 48, seed ^ 0xB0D1UL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out _, out _) || height - sea < 1) return;
            float steep = 1 - up, mountain = g.Mountain(x, z);
            float chance = .04f + 2.2f * Math.Max(0, steep - .1f) + .5f * mountain;
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
        return result;
    }

    /// <summary>
    /// Bushes along forest edges and in meadows, stumps and fallen logs inside forests (levels 0–1).
    /// Placeholders from the valley game's models until Tom's scatter assets arrive.
    /// </summary>
    public static List<RockInstance> Props(TileKey key, in Grid grid, double sea, ulong seed)
    {
        var result = new List<RockInstance>();
        var g = grid;
        Cells(key, 9, seed ^ 0xB054UL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out float forest, out bool wet) || wet || up < .8f || height - sea < 2) return;
            // Most bushes at forest edges (cover around 0.3–0.7), a few out in the meadows.
            float edge = 1 - Math.Abs(forest - .5f) * 2;
            if (Unit(h, 1) > .06f + .4f * Math.Max(0, edge)) return;
            float scale = .7f + .7f * Unit(h, 2);
            int kind = Unit(h, 3) < .25f ? BerryBush : Bush;
            result.Add(new RockInstance(kind, new Transform3D(new Basis(Vector3.Up, Unit(h, 4) * Mathf.Tau).Scaled(Vector3.One * scale), new Vector3((float)x, height - .05f, (float)z))));
        });
        Cells(key, 24, seed ^ 0x1065UL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out float forest, out bool wet) || wet || up < .85f || forest < .45f) return;
            if (Unit(h, 1) > .45f * forest) return;
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
    };
    private static readonly bool[] ConiferSpecies = { true, true, true, true, true, false, false, false, false, false, false, false, false, false, true, false, false };
    private static readonly float[] HeightOf = { 16, 15, 17, 19, 18, 13, 14, 12, 14, 13, 15, 12, 18, 11, 9, 13, 13 };

    /// <summary>Distant tree cards (levels 3–5): sparser but larger with distance; the terrain tint fills in beyond.</summary>
    public static List<FarTree> FarTrees(TileKey key, in Grid grid, double sea, ulong seed)
    {
        var result = new List<FarTree>();
        var g = grid;
        double cell = key.Level switch { 3 => 13, 4 => 22, _ => 36 };
        float grow = key.Level switch { 3 => 1.1f, 4 => 1.35f, _ => 1.7f };
        Cells(key, cell, seed ^ 0xFA77EEUL, (x, z, h) =>
        {
            if (!g.At(x, z, out float height, out float up, out float forest, out bool wet) || wet || up < .65f) return;
            if (Unit(h, 1) > forest * .85f) return;
            double elevation = height - sea;
            int[] list = elevation < 550 ? new[] { 5, 6, 7, 8, 9, 10, 11, 12, 15, 16, 0, 1 } : elevation < 1250 ? new[] { 0, 1, 2, 3, 4, 8, 9, 5, 6, 15 } : new[] { 0, 1, 2, 3, 4, 3, 4, 14 };
            int species = list[(int)(Hashing.Mix(h + 99) % (ulong)list.Length)];
            float scale = (.8f + .45f * Unit(h, 2)) * grow;
            result.Add(new FarTree(new Vector3((float)x, height - .3f, (float)z), HeightOf[species] * scale, Crown[species], ConiferSpecies[species]));
        });
        return result;
    }
}
