using System;
using System.Collections.Generic;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Placeholder plants for biomes whose art does not exist yet (Tom, round 6): palms, cacti, dead trees, acacia, jungle
/// giants, swamp cypress and dry shrubs, built in code in the woodland pack's style — low-poly shapes with crisp
/// 64 px pixel textures (Assets/Vegetation/Placeholder, made by Tests/WorldGen --plant-textures). Each model is fixed
/// (seeded), so the same species always looks the same; variety comes from instance scale and rotation. Replace a
/// species with a real GLB later by removing it from <see cref="Names"/> and adding the GLB to the woodland catalogue.
/// </summary>
public static class PlaceholderPlants
{
    public static readonly string[] Names =
        { "Palm_A", "Palm_B", "Cactus_A", "Cactus_B", "Dead_A", "Dead_B", "Acacia_A", "Jungle_A", "Cypress_A", "Shrub_Dry" };

    public static bool Has(string name) => Array.IndexOf(Names, name) >= 0;

    private const string Folder = "res://Assets/Vegetation/Placeholder/";
    private static Shader? _shader;
    private static readonly Dictionary<string, ShaderMaterial> Materials = new();

    /// <summary>Material per texture: pixel map with mipmaps (no shimmer far away), leaf translucency and sway.</summary>
    private static ShaderMaterial Material(string texture, bool leaves, float sway)
    {
        string key = $"{texture}|{leaves}|{sway}";
        if (Materials.TryGetValue(key, out var m)) return m;
        _shader ??= GD.Load<Shader>("res://Game/Rewrite/Plant.gdshader");
        var image = GD.Load<Texture2D>(Folder + texture + ".png").GetImage();
        image.Decompress();
        image.GenerateMipmaps();
        m = new ShaderMaterial { Shader = _shader };
        m.SetShaderParameter("pixel_map", ImageTexture.CreateFromImage(image));
        m.SetShaderParameter("leaves", leaves ? 1f : 0f);
        m.SetShaderParameter("sway", sway);
        Materials[key] = m;
        return m;
    }

    /// <summary>The model as mesh parts (one mesh, a surface per material), like the woodland catalogue's.</summary>
    public static List<(Mesh Mesh, Transform3D Transform)> Build(string name, int lod)
    {
        var b = new Builder(lod);
        switch (name)
        {
            case "Palm_A": Palm(b, 11, 9.5f, 2.2f, 9); break;
            case "Palm_B": Palm(b, 12, 6f, 2.6f, 11); break;
            case "Cactus_A": Saguaro(b, 21); break;
            case "Cactus_B": Barrels(b, 22); break;
            case "Dead_A": DeadTree(b, 31); break;
            case "Dead_B": Snag(b, 32); break;
            case "Acacia_A": Acacia(b, 41); break;
            case "Jungle_A": JungleGiant(b, 51); break;
            case "Cypress_A": Cypress(b, 61); break;
            case "Shrub_Dry": Shrub(b, 71); break;
            default: throw new ArgumentException("Unknown placeholder plant " + name);
        }
        return new List<(Mesh, Transform3D)> { (b.Commit(), Transform3D.Identity) };
    }

    private static readonly Dictionary<int, Mesh> Drapes = new();

    /// <summary>Cliff dressing card (kind 0 hanging grass, 1 roots): hangs from local origin (the lip) down the face;
    /// local +Z points out from the cliff, 1 unit = 1 m (instances scale it).</summary>
    public static Mesh Drape(int kind)
    {
        if (Drapes.TryGetValue(kind, out var mesh)) return mesh;
        var b = new Builder(1);
        b.Drape(kind == 0 ? "T_Drape_Grass_64" : "T_Roots_64", kind == 0, kind == 0 ? .06f : .015f);
        return Drapes[kind] = b.Commit();
    }

    // ---------------------------------------------------------------- species

    private static void Palm(Builder b, int seed, float height, float lean, int fronds)
    {
        var rng = new Random(seed);
        var dir = new Vector3(1, 0, .3f).Normalized();
        var pts = new Vector3[9]; var radii = new float[9];
        for (int i = 0; i < 9; i++)
        {
            float t = i / 8f;
            pts[i] = dir * lean * t * t + Vector3.Up * height * t + new Vector3(Mathf.Sin(t * 5 + seed) * .08f, 0, 0);
            radii[i] = i == 0 ? .42f : .3f * (1 - .35f * t);
        }
        b.Tube("T_Palm_Bark_64", false, 0, pts, radii, b.Sides(7), 1.2f, 1);
        var top = pts[^1];
        int count = b.Lod == 1 ? fronds : fronds - 3;
        for (int f = 0; f < count; f++)
        {
            float az = f * Mathf.Tau / count + (float)rng.NextDouble() * .4f;
            float pitch0 = Mathf.DegToRad(25 + 25 * (float)rng.NextDouble()), len = 3.6f + .9f * (float)rng.NextDouble();
            var horizontal = new Vector3(Mathf.Cos(az), 0, Mathf.Sin(az));
            var centre = new Vector3[7];
            centre[0] = top + horizontal * .15f;
            for (int s = 1; s < 7; s++)
            {
                float k = s / 6f, pitch = pitch0 - (pitch0 + Mathf.DegToRad(65)) * Mathf.Pow(k, 1.4f);
                centre[s] = centre[s - 1] + (horizontal * Mathf.Cos(pitch) + Vector3.Up * Mathf.Sin(pitch)) * len / 6;
            }
            b.Frond("T_Palm_Frond_64", centre, horizontal.Cross(Vector3.Up).Normalized() * .75f, .2f);
        }
    }

    private static void Saguaro(Builder b, int seed)
    {
        var rng = new Random(seed);
        int sides = b.Lod == 1 ? 12 : 8;
        Column(b, Vector3.Zero, 4.6f, .34f, sides, Vector3.Zero);
        int arms = 3;
        for (int a = 0; a < arms; a++)
        {
            float az = a * 2.1f + (float)rng.NextDouble() * .5f, h0 = 1.6f + 1.1f * (float)rng.NextDouble(), rise = 1f + .8f * (float)rng.NextDouble();
            var outward = new Vector3(Mathf.Cos(az), 0, Mathf.Sin(az));
            var pts = new[]
            {
                Vector3.Up * h0, Vector3.Up * h0 + outward * .45f, Vector3.Up * (h0 + .12f) + outward * .7f,
                Vector3.Up * (h0 + .4f) + outward * .8f, Vector3.Up * (h0 + rise) + outward * .82f,
                Vector3.Up * (h0 + rise + .1f) + outward * .82f, Vector3.Up * (h0 + rise + .17f) + outward * .82f,
            };
            b.Tube("T_Cactus_Skin_64", false, 0, pts, new[] { .19f, .2f, .2f, .21f, .2f, .14f, .02f }, sides, .9f, .75f, ribs: .07f);
        }
    }

    private static void Barrels(Builder b, int seed)
    {
        var rng = new Random(seed);
        int sides = b.Lod == 1 ? 10 : 8;
        for (int i = 0; i < 4; i++)
        {
            float a = i * 1.9f, d = i == 0 ? 0 : .35f + .15f * (float)rng.NextDouble();
            var at = new Vector3(Mathf.Cos(a) * d, 0, Mathf.Sin(a) * d);
            float h = i == 0 ? 1.3f : .5f + .6f * (float)rng.NextDouble(), r = i == 0 ? .3f : .2f + .08f * (float)rng.NextDouble();
            Column(b, at, h, r, sides, new Vector3((float)rng.NextDouble() - .5f, 0, (float)rng.NextDouble() - .5f) * .15f);
        }
    }

    /// <summary>A ribbed cactus column with a domed top.</summary>
    private static void Column(Builder b, Vector3 at, float height, float radius, int sides, Vector3 lean)
    {
        var pts = new List<Vector3>(); var radii = new List<float>();
        for (int i = 0; i <= 5; i++) { float t = i / 5f; pts.Add(at + lean * t * height + Vector3.Up * height * t); radii.Add(radius * (1 + .08f * Mathf.Sin(t * 3f))); }
        var top = pts[^1];
        foreach (var (dy, r) in new[] { (.1f, .8f), (.17f, .5f), (.2f, .05f) }) { pts.Add(top + Vector3.Up * dy * radius * 3); radii.Add(radius * r); }
        b.Tube("T_Cactus_Skin_64", false, 0, pts.ToArray(), radii.ToArray(), sides, .9f, sides / 16f, ribs: .07f);
    }

    private static void DeadTree(Builder b, int seed)
    {
        var rng = new Random(seed);
        var trunk = Crooked(rng, Vector3.Zero, Vector3.Up, 6.5f, 7, .25f);
        b.Tube("T_Dead_Bark_64", false, 0, trunk, Taper(7, .34f, .09f, flare: .5f), b.Sides(6), 1.6f, 1);
        for (int i = 0; i < 5; i++)
        {
            float t = .4f + .5f * i / 4f, az = i * 2.4f + (float)rng.NextDouble();
            var start = Along(trunk, t);
            var dir = (new Vector3(Mathf.Cos(az), .7f + .6f * (float)rng.NextDouble(), Mathf.Sin(az))).Normalized();
            float len = (2.8f - 1.6f * t) * (.8f + .4f * (float)rng.NextDouble());
            var branch = Crooked(rng, start, dir, len, 4, .18f);
            b.Tube("T_Dead_Bark_64", false, 0, branch, Taper(4, .15f - .06f * t, .035f), b.Sides(4), 1.6f, .5f);
            if (b.Lod > 1) continue;
            for (int k = 0; k < 2; k++)
            {
                var twigDir = (dir + new Vector3((float)rng.NextDouble() - .5f, .5f, (float)rng.NextDouble() - .5f)).Normalized();
                var twig = Crooked(rng, Along(branch, .5f + .3f * k), twigDir, len * .45f, 3, .15f);
                b.Tube("T_Dead_Bark_64", false, 0, twig, Taper(3, .045f, .012f), 3, 1.6f, .3f);
            }
        }
    }

    private static void Snag(Builder b, int seed)
    {
        var rng = new Random(seed);
        var lean = new Vector3(.22f, 1, .08f).Normalized();
        var trunk = Crooked(rng, Vector3.Zero, lean, 3.6f, 5, .12f);
        var radii = Taper(5, .32f, .2f, flare: .45f);
        radii[^1] = .08f; // broken, splintered top
        b.Tube("T_Dead_Bark_64", false, 0, trunk, radii, b.Sides(7), 1.6f, 1);
        for (int i = 0; i < 2; i++)
        {
            float az = 1 + i * 2.8f;
            var stub = Crooked(rng, Along(trunk, .45f + .25f * i), new Vector3(Mathf.Cos(az), .5f, Mathf.Sin(az)).Normalized(), .9f, 3, .1f);
            b.Tube("T_Dead_Bark_64", false, 0, stub, Taper(3, .1f, .04f), b.Sides(5), 1.6f, .5f);
        }
        for (int i = 0; i < 4; i++)
        {
            float az = i * 1.6f + .3f;
            var root = new[] { new Vector3(Mathf.Cos(az) * .15f, .5f, Mathf.Sin(az) * .15f), new Vector3(Mathf.Cos(az) * .7f, .12f, Mathf.Sin(az) * .7f), new Vector3(Mathf.Cos(az) * 1.1f, -.1f, Mathf.Sin(az) * 1.1f) };
            b.Tube("T_Dead_Bark_64", false, 0, root, new[] { .16f, .09f, .03f }, 4, 1.6f, .4f);
        }
    }

    private static void Acacia(Builder b, int seed)
    {
        var rng = new Random(seed);
        var trunk = Crooked(rng, Vector3.Zero, new Vector3(.1f, 1, 0).Normalized(), 2.3f, 4, .08f);
        b.Tube("T_Acacia_Bark_64", false, 0, trunk, Taper(4, .23f, .17f, flare: .32f), b.Sides(6), 1.4f, 1);
        var fork = trunk[^1];
        for (int i = 0; i < 4; i++)
        {
            float az = i * 1.57f + .4f + (float)rng.NextDouble() * .5f, reach = 1.7f + 1f * (float)rng.NextDouble();
            var end = fork + new Vector3(Mathf.Cos(az) * reach, 2f + .9f * (float)rng.NextDouble(), Mathf.Sin(az) * reach);
            var mid = fork.Lerp(end, .5f) + new Vector3(0, -.2f, 0);
            b.Tube("T_Acacia_Bark_64", false, 0, new[] { fork, mid, end }, new[] { .14f, .09f, .05f }, b.Sides(5), 1.4f, .6f);
            b.Blob("T_Acacia_Foliage_64", end + Vector3.Up * .25f, new Vector3(1.7f, .42f, 1.6f), seed * 10 + i, .08f);
        }
        b.Blob("T_Acacia_Foliage_64", fork + new Vector3(0, 3.05f, 0), new Vector3(2.2f, .45f, 2.1f), seed * 10 + 9, .08f);
    }

    private static void JungleGiant(Builder b, int seed)
    {
        var rng = new Random(seed);
        float h = 17;
        var trunk = Crooked(rng, Vector3.Zero, Vector3.Up, h, 8, .05f);
        b.Tube("T_Jungle_Bark_64", false, 0, trunk, Taper(8, .62f, .32f, flare: .9f), b.Sides(8), 2.2f, 1);
        // Buttress roots: thin bark plates from the trunk out across the ground.
        for (int i = 0; i < 5; i++)
        {
            float az = i * 1.257f + .2f;
            var o = new Vector3(Mathf.Cos(az), 0, Mathf.Sin(az));
            b.Plate("T_Jungle_Bark_64", o * .3f, o * .3f + Vector3.Up * 2.6f, o * 2.1f - Vector3.Up * .1f);
        }
        var ends = new List<Vector3>();
        for (int i = 0; i < 4; i++)
        {
            float az = i * 1.57f + (float)rng.NextDouble() * .6f, reach = 3.4f + 1.2f * (float)rng.NextDouble();
            var start = Along(trunk, .72f + .08f * i);
            var end = new Vector3(Mathf.Cos(az) * reach, h - .5f + 1.5f * (float)rng.NextDouble(), Mathf.Sin(az) * reach);
            b.Tube("T_Jungle_Bark_64", false, 0, new[] { start, start.Lerp(end, .5f) + Vector3.Up * .6f, end }, new[] { .24f, .15f, .08f }, b.Sides(5), 2.2f, .6f);
            b.Blob("T_Jungle_Foliage_64", end + Vector3.Up * .5f, new Vector3(3f, 1.35f, 2.8f), seed * 10 + i, .12f);
            ends.Add(end);
        }
        b.Blob("T_Jungle_Foliage_64", trunk[^1] + Vector3.Up * 1.1f, new Vector3(3.4f, 1.6f, 3.4f), seed * 10 + 7, .12f);
        if (b.Lod > 1) return;
        // Hanging vines from under the crown.
        for (int v = 0; v < 9; v++)
        {
            var anchor = ends[v % ends.Count] + new Vector3((float)rng.NextDouble() * 2.4f - 1.2f, -.6f, (float)rng.NextDouble() * 2.4f - 1.2f);
            b.Hanging("T_Vine_32", anchor, 4 + 5 * (float)rng.NextDouble(), .55f, v * .7f, tileMetres: 1.6f, sway: .18f);
        }
    }

    private static void Cypress(Builder b, int seed)
    {
        var rng = new Random(seed);
        float h = 12;
        var trunk = Crooked(rng, Vector3.Zero, Vector3.Up, h, 8, .04f);
        var radii = Taper(8, .36f, .1f);
        radii[0] = 1.0f; radii[1] = .5f; // flared, buttressed swamp base
        b.Tube("T_Cypress_Bark_64", false, 0, trunk, radii, b.Sides(8), 1.5f, 1);
        int tiers = 5;
        for (int i = 0; i < tiers; i++)
        {
            float t = .42f + .5f * i / (tiers - 1), size = 2.1f - .25f * i, az = i * 2.2f;
            var stem = Along(trunk, t);
            var at = stem + new Vector3(Mathf.Cos(az) * .7f, .2f, Mathf.Sin(az) * .7f);
            b.Tube("T_Cypress_Bark_64", false, 0, new[] { stem, at }, new[] { .09f, .05f }, b.Sides(4), 1.5f, .4f);
            b.Blob("T_Cypress_Foliage_64", at, new Vector3(size, .62f, size * .9f), seed * 10 + i, .06f);
            if (b.Lod > 1) continue;
            for (int m = 0; m < 3; m++)
            {
                float ma = m * 2.1f + i;
                b.Hanging("T_Moss_32", at + new Vector3(Mathf.Cos(ma) * size * .55f, -.35f, Mathf.Sin(ma) * size * .55f), 1.4f + 1.4f * (float)rng.NextDouble(), .7f, ma, tileMetres: 0, sway: .1f);
            }
        }
        b.Blob("T_Cypress_Foliage_64", trunk[^1] + Vector3.Up * .2f, new Vector3(1.1f, .8f, 1.1f), seed * 10 + 9, .06f);
    }

    private static void Shrub(Builder b, int seed)
    {
        for (int i = 0; i < 3; i++)
        {
            float a = i * Mathf.Pi / 3 + .2f;
            var right = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * .85f;
            b.Card("T_Shrub_Dry_64", Vector3.Zero, right, Vector3.Up * 1.45f, sway: .05f);
        }
    }

    // ---------------------------------------------------------------- shape helpers

    private static Vector3[] Crooked(Random rng, Vector3 start, Vector3 dir, float length, int segments, float wobble)
    {
        var pts = new Vector3[segments + 1];
        pts[0] = start;
        var d = dir.Normalized();
        for (int i = 1; i <= segments; i++)
        {
            d = (d + new Vector3((float)rng.NextDouble() - .5f, (float)rng.NextDouble() * .2f - .05f, (float)rng.NextDouble() - .5f) * wobble).Normalized();
            pts[i] = pts[i - 1] + d * length / segments;
        }
        return pts;
    }

    private static float[] Taper(int segments, float r0, float r1, float flare = 0)
    {
        var r = new float[segments + 1];
        for (int i = 0; i <= segments; i++) r[i] = Mathf.Lerp(r0, r1, i / (float)segments);
        if (flare > 0) r[0] = flare;
        return r;
    }

    private static Vector3 Along(Vector3[] pts, float t)
    {
        float f = Mathf.Clamp(t, 0, 1) * (pts.Length - 1);
        int i = Math.Min((int)f, pts.Length - 2);
        return pts[i].Lerp(pts[i + 1], f - i);
    }

    // ---------------------------------------------------------------- mesh builder

    private sealed class Builder
    {
        public readonly int Lod;
        private readonly Dictionary<string, Surface> _surfaces = new();
        public Builder(int lod) { Lod = lod; }
        public int Sides(int near) => Lod == 1 ? near : Math.Max(3, near - 2);

        private sealed class Surface
        {
            public readonly List<Vector3> V = new(), N = new(); public readonly List<Vector2> Uv = new(); public readonly List<Color> C = new(); public readonly List<int> I = new();
            public bool Leaves; public float Sway;
            public int Add(Vector3 v, Vector3 n, Vector2 uv, float sway) { V.Add(v); N.Add(n); Uv.Add(uv); C.Add(new Color(sway, 0, 0)); return V.Count - 1; }
        }

        private Surface S(string texture, bool leaves, float sway)
        {
            if (!_surfaces.TryGetValue(texture, out var s)) _surfaces[texture] = s = new Surface { Leaves = leaves, Sway = sway };
            return s;
        }

        /// <summary>A tube along a path (rings with parallel-transported frames); optional ribs (cacti).</summary>
        public void Tube(string texture, bool leaves, float sway, Vector3[] pts, float[] radii, int sides, float metresPerTile, float uRepeat, float ribs = 0)
        {
            var s = S(texture, leaves, sway);
            int baseIndex = s.V.Count;
            Vector3 normal = Vector3.Zero;
            float along = 0;
            for (int i = 0; i < pts.Length; i++)
            {
                var tangent = (i == 0 ? pts[1] - pts[0] : i == pts.Length - 1 ? pts[i] - pts[i - 1] : pts[i + 1] - pts[i - 1]).Normalized();
                if (i == 0) normal = (Mathf.Abs(tangent.Y) < .9f ? tangent.Cross(Vector3.Up) : tangent.Cross(Vector3.Right)).Normalized();
                else { normal = (normal - tangent * normal.Dot(tangent)).Normalized(); along += pts[i].DistanceTo(pts[i - 1]); }
                var binormal = tangent.Cross(normal).Normalized();
                for (int k = 0; k <= sides; k++)
                {
                    float a = k * Mathf.Tau / sides;
                    var radial = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    float r = radii[i] * (ribs > 0 ? 1 + ribs * ((k % 2 == 0) ? 1 : -1) : 1);
                    s.Add(pts[i] + radial * r, radial, new Vector2(k / (float)sides * uRepeat, along / metresPerTile), pts[i].Y / 12f);
                }
            }
            for (int i = 0; i < pts.Length - 1; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = baseIndex + i * (sides + 1) + k, b = a + 1, c = a + sides + 1, d = c + 1;
                    s.I.AddRange(new[] { a, c, b, b, c, d });
                }
        }

        /// <summary>A lumpy ellipsoid of foliage (subdivided icosahedron, noise-displaced).</summary>
        public void Blob(string texture, Vector3 centre, Vector3 radii, int seed, float sway)
        {
            var s = S(texture, true, sway);
            var (verts, tris) = Ico(Lod == 1 ? 2 : 1);
            int baseIndex = s.V.Count;
            foreach (var d in verts)
            {
                float bump = 1 + .16f * (Hash(d, seed) - .5f) * 2;
                var p = centre + new Vector3(d.X * radii.X, d.Y * radii.Y, d.Z * radii.Z) * bump;
                var n = new Vector3(d.X / radii.X, d.Y / radii.Y, d.Z / radii.Z).Normalized();
                // Planar UVs at ~1.6 m per tile (texture density close to the woodland pack's crowns).
                var uv = new Vector2((p.X + p.Z * .6f) / 1.6f, (p.Y + p.Z * .4f) / 1.6f);
                s.Add(p, n, uv, Mathf.Clamp(.4f + d.Y * .3f, 0, 1));
            }
            foreach (int t in tris) s.I.Add(baseIndex + t);
        }

        /// <summary>A palm frond: a strip along <paramref name="centre"/>, folded into a shallow V.</summary>
        public void Frond(string texture, Vector3[] centre, Vector3 side, float fold)
        {
            var s = S(texture, true, .22f);
            int baseIndex = s.V.Count;
            for (int i = 0; i < centre.Length; i++)
            {
                float v = i / (float)(centre.Length - 1), w = 1;
                var tangent = (i == 0 ? centre[1] - centre[0] : centre[i] - centre[i - 1]).Normalized();
                var up = side.Cross(tangent).Normalized();
                if (up.Y < 0) up = -up;
                var mid = centre[i] + up * fold * w;
                s.Add(centre[i] - side * w, up, new Vector2(0, v), v);
                s.Add(mid, up, new Vector2(.5f, v), v);
                s.Add(centre[i] + side * w, up, new Vector2(1, v), v);
            }
            for (int i = 0; i < centre.Length - 1; i++)
                for (int k = 0; k < 2; k++)
                {
                    int a = baseIndex + i * 3 + k, b = a + 1, c = a + 3, d = c + 1;
                    s.I.AddRange(new[] { a, c, b, b, c, d });
                }
        }

        /// <summary>A flat triangular plate (buttress root).</summary>
        public void Plate(string texture, Vector3 a, Vector3 b, Vector3 c)
        {
            var s = S(texture, false, 0);
            var n = (b - a).Cross(c - a).Normalized();
            int i0 = s.Add(a, n, new Vector2(0, 0), 0), i1 = s.Add(b, n, new Vector2(0, (b - a).Length() / 2.2f), 0), i2 = s.Add(c, n, new Vector2((c - a).Length() / 2.2f, 0), 0);
            s.I.AddRange(new[] { i0, i1, i2 });
        }

        /// <summary>An upright cut-out card (shrubs): texture v = 0 at the top.</summary>
        public void Card(string texture, Vector3 bottom, Vector3 halfRight, Vector3 up, float sway)
        {
            var s = S(texture, true, sway);
            var n = halfRight.Cross(up).Normalized();
            int a = s.Add(bottom - halfRight, n, new Vector2(0, 1), 0), b = s.Add(bottom + halfRight, n, new Vector2(1, 1), 0);
            int c = s.Add(bottom - halfRight + up, n, new Vector2(0, 0), 1), d = s.Add(bottom + halfRight + up, n, new Vector2(1, 0), 1);
            s.I.AddRange(new[] { a, c, b, b, c, d });
        }

        /// <summary>A curved card over a cliff lip: starts a little back on the top, bulges out, hangs ~2 m down.</summary>
        public void Drape(string texture, bool leaves, float sway)
        {
            var s = S(texture, leaves, sway);
            var profile = new[] { (0f, -.25f, .02f), (.22f, .25f, -.12f), (.55f, .3f, -.75f), (1f, .14f, -2.1f) }; // (v, z out, y)
            int baseIndex = s.V.Count;
            foreach (var (v, z, y) in profile)
                for (int k = 0; k <= 2; k++)
                {
                    float x = (k - 1) * .9f;
                    s.Add(new Vector3(x, y, z + (k == 1 ? .05f : 0)), Vector3.Back, new Vector2(k / 2f, v), v);
                }
            for (int i = 0; i < profile.Length - 1; i++)
                for (int k = 0; k < 2; k++)
                {
                    int a = baseIndex + i * 3 + k, b = a + 1, c = a + 3, d = c + 1;
                    s.I.AddRange(new[] { a, c, b, b, c, d });
                }
        }

        /// <summary>Two crossed hanging cards (moss, vines) from <paramref name="top"/>; v = 0 at the top.</summary>
        public void Hanging(string texture, Vector3 top, float length, float width, float angle, float tileMetres, float sway)
        {
            var s = S(texture, true, sway);
            for (int k = 0; k < 2; k++)
            {
                float a = angle + k * Mathf.Pi / 2;
                var half = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * width * .5f;
                var n = half.Cross(Vector3.Down).Normalized();
                float vEnd = tileMetres > 0 ? length / tileMetres : 1;
                int i0 = s.Add(top - half, n, new Vector2(0, 0), 0), i1 = s.Add(top + half, n, new Vector2(1, 0), 0);
                int i2 = s.Add(top - half + Vector3.Down * length, n, new Vector2(0, vEnd), 1), i3 = s.Add(top + half + Vector3.Down * length, n, new Vector2(1, vEnd), 1);
                s.I.AddRange(new[] { i0, i1, i2, i1, i3, i2 });
            }
        }

        public ArrayMesh Commit()
        {
            var mesh = new ArrayMesh();
            foreach (var (texture, s) in _surfaces)
            {
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = s.V.ToArray();
                arrays[(int)Mesh.ArrayType.Normal] = s.N.ToArray();
                arrays[(int)Mesh.ArrayType.TexUV] = s.Uv.ToArray();
                arrays[(int)Mesh.ArrayType.Color] = s.C.ToArray();
                arrays[(int)Mesh.ArrayType.Index] = s.I.ToArray();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                mesh.SurfaceSetMaterial(mesh.GetSurfaceCount() - 1, Material(texture, s.Leaves, s.Sway));
            }
            return mesh;
        }

        private static float Hash(Vector3 d, int seed)
        {
            uint h = unchecked((uint)(Mathf.RoundToInt(d.X * 1000) * 73856093 ^ Mathf.RoundToInt(d.Y * 1000) * 19349663 ^ Mathf.RoundToInt(d.Z * 1000) * 83492791 ^ seed * 2654435761u));
            h ^= h >> 13; h *= 1274126177u; h ^= h >> 16;
            return (h & 0xFFFF) / 65535f;
        }

        private static readonly Dictionary<int, (List<Vector3>, List<int>)> IcoCache = new();

        /// <summary>Unit icosphere (vertices, triangle indices) with the given subdivision level.</summary>
        private static (List<Vector3>, List<int>) Ico(int level)
        {
            if (IcoCache.TryGetValue(level, out var cached)) return cached;
            float t = (1 + Mathf.Sqrt(5)) / 2;
            var v = new List<Vector3>
            {
                new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
                new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
            };
            for (int i = 0; i < v.Count; i++) v[i] = v[i].Normalized();
            var f = new List<int> { 0, 11, 5, 0, 5, 1, 0, 1, 7, 0, 7, 10, 0, 10, 11, 1, 5, 9, 5, 11, 4, 11, 10, 2, 10, 7, 6, 7, 1, 8,
                3, 9, 4, 3, 4, 2, 3, 2, 6, 3, 6, 8, 3, 8, 9, 4, 9, 5, 2, 4, 11, 6, 2, 10, 8, 6, 7, 9, 8, 1 };
            for (int l = 0; l < level; l++)
            {
                var mids = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = Math.Min(a, b) * 100000L + Math.Max(a, b);
                    if (mids.TryGetValue(key, out int m)) return m;
                    v.Add(((v[a] + v[b]) * .5f).Normalized());
                    return mids[key] = v.Count - 1;
                }
                var next = new List<int>();
                for (int i = 0; i < f.Count; i += 3)
                {
                    int a = f[i], b = f[i + 1], c = f[i + 2], ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                    next.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
                }
                f = next;
            }
            return IcoCache[level] = (v, f);
        }
    }
}
