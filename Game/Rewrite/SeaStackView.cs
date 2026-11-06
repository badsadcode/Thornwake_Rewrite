using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Sea stacks and arches (landform no. 9, D266): the rock the generator places off steep coasts
/// (<see cref="SeaStacks"/>), built here as procedural placeholder models until the art comes — a stack a column of
/// jagged rock rings, undercut at the waterline, stepped in strata, tapering to a grassy top; an arch a block of cliff
/// with a hole worn through it, a solid shape meshed on a worker (D267; a tube swept over a half circle looked man-made).
/// Flat-shaded in the rocks' material (the biome's rock colour and
/// texture). Drawn within 12 km of the camera, solid (their own faces as collision) within 700 m. Each is placed from
/// its world position against the scene's origin every time that moves (world-space objects left behind at a shift
/// jumped away, D264).
/// </summary>
public sealed class SeaStackView
{
    private const double DrawRange = 12000, SolidRange = 700;
    private readonly Node3D _parent;
    private readonly Dictionary<ulong, (MeshInstance3D Mesh, StaticBody3D? Body, SeaStacks.Rock Rock, Vector3[] Faces)> _shown = new();
    private readonly Dictionary<ulong, (Task<Model> Job, SeaStacks.Rock Rock)> _building = new();
    private TerrainSampler? _for;
    private Task<SeaStacks?>? _placing;
    private SeaStacks? _stacks;
    private double _originX = double.NaN, _originZ, _lastX = double.NaN, _lastZ;

    public SeaStackView(Node3D parent) { _parent = parent; }

    /// <summary>Rocks shown now (diagnostics).</summary>
    public int Shown => _shown.Count;

    public void Update(TerrainSampler terrain, double camX, double camZ, double originX, double originZ)
    {
        if (_for != terrain)
        {
            Clear(); _building.Clear();
            _for = terrain; _stacks = null; _lastX = double.NaN;
            _placing = Task.Run(() => terrain.SeaStacks); // (placed from the terrain: a few thousand samples)
        }
        if (_placing is { IsCompleted: true } done) { _stacks = done.Result; _placing = null; _lastX = double.NaN; }
        if (_stacks == null) return;
        bool moved = double.IsNaN(_lastX) || Math.Abs(camX - _lastX) + Math.Abs(camZ - _lastZ) > 150;
        bool rebased = originX != _originX || originZ != _originZ;
        if (moved)
        {
            _lastX = camX; _lastZ = camZ;
            var near = _stacks.Near(camX, camZ, DrawRange).ToDictionary(r => r.Seed);
            foreach (var key in _shown.Keys.Where(k => !near.ContainsKey(k)).ToList()) Remove(key);
            int built = 0;
            foreach (var (key, rock) in near)
                if (!_shown.ContainsKey(key) && !_building.ContainsKey(key) && built++ < 6)
                    _building[key] = (Task.Run(() => Build(rock, Tint(terrain, rock.X, rock.Z))), rock); // (a few a pass; the rest next time)
            if (built > 6) _lastX = double.NaN;
        }
        // Built models go in one a frame (an arch is thousands of triangles).
        foreach (var (key, b) in _building)
            if (b.Job.IsCompleted)
            {
                _building.Remove(key);
                if (Math.Abs(b.Rock.X - camX) <= DrawRange * 1.1 && Math.Abs(b.Rock.Z - camZ) <= DrawRange * 1.1) Add(terrain, b.Rock, b.Job.Result, originX, originZ);
                break;
            }
        foreach (var (key, s) in _shown.ToList())
        {
            if (rebased) s.Mesh.Position = new Vector3((float)(s.Rock.X - originX), (float)terrain.Plan.Profile.SeaLevel, (float)(s.Rock.Z - originZ));
            // Solid near the camera only (a trimesh per rock).
            bool solid = Math.Abs(s.Rock.X - camX) + Math.Abs(s.Rock.Z - camZ) < SolidRange * 1.4;
            if (solid && s.Body == null)
            {
                var body = new StaticBody3D();
                body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = s.Faces, BackfaceCollision = true } });
                s.Mesh.AddChild(body);
                _shown[key] = s with { Body = body };
            }
            else if (!solid && s.Body != null) { s.Body.QueueFree(); _shown[key] = s with { Body = null }; }
        }
        _originX = originX; _originZ = originZ;
    }

    private void Add(TerrainSampler terrain, SeaStacks.Rock rock, Model model, double originX, double originZ)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = model.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = model.Normals;
        arrays[(int)Mesh.ArrayType.Color] = model.Colors;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, RockMeshes.Material);
        var faces = model.Vertices;
        var node = new MeshInstance3D { Name = "SeaStack", Mesh = mesh };
        // (an arch's legs lie along its way out to sea: local x)
        float dx = (float)rock.DirX, dz = (float)rock.DirZ;
        node.Basis = new Basis(new Vector3(dx, 0, dz), Vector3.Up, new Vector3(-dz, 0, dx));
        node.Position = new Vector3((float)(rock.X - originX), (float)terrain.Plan.Profile.SeaLevel, (float)(rock.Z - originZ));
        _parent.AddChild(node);
        _shown[rock.Seed] = (node, null, rock, faces);
    }

    private void Remove(ulong key)
    {
        var s = _shown[key];
        s.Mesh.QueueFree(); s.Mesh.Mesh?.Dispose();
        _shown.Remove(key);
    }

    private void Clear() { foreach (var key in _shown.Keys.ToList()) Remove(key); }

    /// <summary>The biome's rock colour (relative to the default stone, as GroundCover's rocks) and rock look.</summary>
    private static Color Tint(TerrainSampler terrain, double x, double z)
    {
        if (terrain.Plan.Biomes is not { } biomes) return new Color(1, 1, 1, 1 / 8f);
        var (a, b, share) = biomes.Sample(x, z);
        var r = biomes.Pool[share >= .5 ? a : b];
        return new Color(r.Rock.R / .467f, r.Rock.G / .463f, r.Rock.B / .416f, (r.RockTexture + 1) / 8f);
    }

    // ---- the models

    private static float Noise(ulong seed, float x, float y, float z)
    {
        // Smooth value noise on a lattice (rock bulges a few metres across).
        int ix = Mathf.FloorToInt(x), iy = Mathf.FloorToInt(y), iz = Mathf.FloorToInt(z);
        float fx = x - ix, fy = y - iy, fz = z - iz;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        float H(int a, int b, int c) => (float)Hashing.Unit(Hashing.Mix(seed ^ (ulong)(a * 73856093L) ^ (ulong)(b * 19349663L) ^ (ulong)(c * 83492791L)));
        float x00 = Mathf.Lerp(H(ix, iy, iz), H(ix + 1, iy, iz), fx), x10 = Mathf.Lerp(H(ix, iy + 1, iz), H(ix + 1, iy + 1, iz), fx);
        float x01 = Mathf.Lerp(H(ix, iy, iz + 1), H(ix + 1, iy, iz + 1), fx), x11 = Mathf.Lerp(H(ix, iy + 1, iz + 1), H(ix + 1, iy + 1, iz + 1), fx);
        return Mathf.Lerp(Mathf.Lerp(x00, x10, fy), Mathf.Lerp(x01, x11, fy), fz) * 2 - 1;
    }

    /// <summary>A rock's triangles (flat-shaded: three vertices each), built on a worker thread.</summary>
    private sealed record Model(Vector3[] Vertices, Vector3[] Normals, Color[] Colors);

    private static Model Build(SeaStacks.Rock rock, Color tint)
    {
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var colours = new List<Color>();
        if (rock.Kind == SeaStacks.Kind.Arch) ArchSurface(rock, tint, vertices, normals, colours);
        else StackSurface(rock, tint, vertices, normals, colours);
        return new Model(vertices.ToArray(), normals.ToArray(), colours.ToArray());
    }

    private static readonly Color Stone = new("777a72"), Dark = new("5d605a"), Moss = new("66744f"), Grass = new("5f7a45");

    private static void StackSurface(SeaStacks.Rock rock, Color tint, List<Vector3> vertices, List<Vector3> normals, List<Color> colours)
    {
        var rings = StackRings(rock);
        var rng = new RandomStream(rock.Seed ^ 0x5EA5UL);
        void Tri(Vector3 a, Vector3 b, Vector3 c, bool top)
        {
            var normal = (b - a).Cross(c - a).Normalized();
            float shade = (float)rng.Unit();
            var colour = normal.Y > .75f ? (top ? Grass : Moss).Lerp(Stone, .25f + .3f * shade) : Stone.Lerp(Dark, shade * .6f);
            var tinted = new Color(colour.R * tint.R, colour.G * tint.G, colour.B * tint.B, tint.A);
            // (Godot front faces are clockwise seen from outside)
            vertices.Add(a); vertices.Add(c); vertices.Add(b);
            for (int k = 0; k < 3; k++) { normals.Add(normal); colours.Add(tinted); }
        }
        int n = rings[0].Length;
        for (int k = 0; k + 1 < rings.Count; k++)
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 a = rings[k][i], b = rings[k][j], c = rings[k + 1][j], d = rings[k + 1][i];
                Tri(a, b, c, false); Tri(a, c, d, false);
            }
        // The top: a low dome over the last ring, grassy.
        var last = rings[^1];
        var centre = last.Aggregate(Vector3.Zero, (s, v) => s + v) / n + Vector3.Up * (float)(rock.Radius * .12);
        for (int i = 0; i < n; i++) Tri(last[i], last[(i + 1) % n], centre, true);
    }

    /// <summary>A stack: rings of 16 points every ~3 m from 3 m under the seabed to its top, counter-clockwise seen from
    /// above, the radius jagged by noise, undercut at the waterline, stepped every 5–9 m, tapering over its top quarter.</summary>
    private static List<Vector3[]> StackRings(SeaStacks.Rock rock)
    {
        const int N = 16;
        var rng = new RandomStream(rock.Seed);
        float h = (float)rock.Height, r0 = (float)rock.Radius, bottom = (float)rock.Seabed - 3;
        float strata = (float)rng.Between(5, 9), squash = (float)rng.Between(.7, 1);
        int levels = Math.Clamp((int)Math.Ceiling((h - bottom) / 3), 4, 48);
        var rings = new List<Vector3[]>();
        for (int k = 0; k <= levels; k++)
        {
            float y = bottom + (h - bottom) * k / levels;
            float r = r0 * (y < 0 ? 1 + .25f * Mathf.Clamp(-y / 15, 0, 1) : 1);       // wider down in the water
            r *= 1 - .18f * Mathf.Clamp(1 - Mathf.Abs(y - 1.2f) / 2.5f, 0, 1);        // the waves' notch
            float layer = Mathf.PosMod(y, strata) / strata;
            r *= 1 + .07f * (layer < .3f ? 1 : 0);                                    // strata ledges
            float u = Mathf.Clamp((y - .72f * h) / (.28f * h), 0, 1);
            r *= 1 - .5f * Mathf.Pow(u, 1.5f);                                        // the top tapers
            float lx = (float)rock.LeanX * Math.Max(0, y), lz = (float)rock.LeanZ * Math.Max(0, y);
            var ring = new Vector3[N];
            for (int i = 0; i < N; i++)
            {
                float a = -i * Mathf.Tau / N, ca = Mathf.Cos(a), sa = Mathf.Sin(a);
                float jag = 1 + .2f * Noise(rock.Seed, ca * 1.6f + 3, y / 7, sa * 1.6f + 3) + .08f * Noise(rock.Seed + 1, ca * 4, y / 2.5f, sa * 4);
                ring[i] = new Vector3(ca * r * jag + lx, y, sa * r * jag * squash + lz);
            }
            rings.Add(ring);
        }
        return rings;
    }

    private static float SmoothMax(float a, float b, float k)
    {
        float h = Math.Max(k - Math.Abs(a - b), 0) / k;
        return Math.Max(a, b) + h * h * k * .25f;
    }

    /// <summary>
    /// An arch (D267, Tom: "they don't look natural"; a tube of even thickness swept over a half circle looked man-made): what
    /// is left of a headland — a block of cliff, one leg stout and one slight, its top sloping off and narrowing upward,
    /// a lopsided hole worn through with a flatter roof, in tilted beds and vertical joints, undercut at the waterline,
    /// fallen blocks at its feet. A solid shape (signed distance, metres) on a grid of 1.5–2.5 m cells, its surface taken
    /// with surface nets and flat-shaded; the bottom left open under the seabed. x along the legs, z across.
    /// </summary>
    private static void ArchSurface(SeaStacks.Rock rock, Color tint, List<Vector3> vertices, List<Vector3> normals, List<Color> colours)
    {
        var rng = new RandomStream(rock.Seed);
        ulong seed = rock.Seed;
        float span = (float)rock.Span, half = (float)rock.Radius, h = (float)rock.Height, bottom = (float)rock.Seabed - 3;
        float side = rng.Unit() < .5 ? -1 : 1; // (which leg is the stout one)
        float stout = half * (float)rng.Between(1.5, 1.9), slight = half * (float)rng.Between(.9, 1.25);
        float x0 = -(span * .5f + slight), x1 = span * .5f + stout;
        float thick = half * (float)rng.Between(1.45, 1.8);
        float roof = h - Math.Max(.3f * h, half * 1.3f);                 // the hole's top, under the bridge of rock
        float tilt = (float)rng.Between(-.16, .16), bed = (float)rng.Between(2.6, 4.5);
        float bend = half * (float)rng.Between(-.45, .45);
        // Blocks fallen from it, lying in the water by its legs.
        var blocks = new List<(Vector3 At, float R)>();
        for (int b = 2 + rng.Int(4); b > 0; b--)
        {
            bool byStout = rng.Unit() < .6;
            float bx = (byStout ? x1 : x0) + (float)rng.Between(-half, half * .8) * (byStout ? 1 : -1);
            float bz = (rng.Unit() < .5 ? -1 : 1) * (thick + (float)rng.Between(0, half * .9));
            blocks.Add((new Vector3(bx, (float)rng.Between(-2, 1.5), bz), half * (float)rng.Between(.3, .6)));
        }
        float Solid(float wx, float y, float z)
        {
            float x = wx * side;
            // Everything bent a little by slow noise: no straight line or true curve anywhere.
            float warp = Noise(seed + 2, x / 22, y / 22, z / 22), warp2 = Noise(seed + 3, x / 22 + 7, y / 22, z / 22);
            float px = x + 4 * warp, pz = z + 3.5f * warp2 - bend * Mathf.Sin(x / (x1 - x0) * 3.1f);
            float u = Mathf.Clamp((x - x0) / (x1 - x0), 0, 1), up = Mathf.Clamp(y / h, 0, 1);
            // The block: narrowing upward, its top sloping off to the slight leg, higher and lower by noise.
            float top = h * (.74f + .26f * u) + .06f * h * Noise(seed + 4, x / 14, 0, z / 14);
            float d = SmoothMax(SmoothMax(x0 + half * .5f * up - px, px - x1 + half * .35f * up, 4), Math.Abs(pz) - thick * (1.2f - .45f * up), 4);
            d = SmoothMax(d, y - top, 3);
            // The hole: off the middle, leaning, flatter under its roof, straight legs below the water.
            float a = span * .5f * (1 + .14f * Noise(seed + 5, y / 11, 0, z / 11)), lean = tilt * 1.6f * Math.Max(y, 0);
            float hx = Math.Abs(px + lean), hy = y + 3;
            float hole;
            if (hy <= 0) hole = hx - a;
            else
            {
                float nx = hx / a, ny = hy / (roof + 3 + 2.5f * Noise(seed + 6, x / 9, 0, z / 9));
                hole = (Mathf.Pow(Mathf.Pow(nx, 2.6f) + Mathf.Pow(ny, 2.6f), 1 / 2.6f) - 1) * Math.Min(a, roof + 3);
            }
            d = SmoothMax(d, -hole, 2.5f);
            // Worn rock near the surface only (the noise is the cost): vertical joints cut in, tilted beds, the waves' notch.
            if (Math.Abs(d) < 9)
            {
                float joints = Math.Abs(Noise(seed + 7, x / 6, y / 32, z / 6)), s = y + tilt * x;
                d += 1.6f * (1 - Math.Min(joints * 5, 1)) - .3f + 1.6f * Noise(seed + 8, x / 8, y / 8, z / 8) + .6f * Noise(seed + 9, x / 3, y / 3, z / 3);
                d += .7f * (Mathf.PosMod(s, bed) / bed < .32f ? -1 : .25f);
                d += 1.5f * Mathf.Clamp(1 - Math.Abs(y - 1.2f) / 2.8f, 0, 1);
            }
            foreach (var (at, r) in blocks)
            {
                float dx = x - at.X, dy = (y - at.Y) * 1.35f, dz = z - at.Z;
                float block = Mathf.Sqrt(dx * dx + dy * dy + dz * dz) - r;
                if (block < 3) block += r * .3f * Noise(seed + 10, x / 2.5f, y / 2.5f, z / 2.5f);
                d = Math.Min(d, block);
            }
            return d;
        }
        // The grid, past the shape all round but its open bottom.
        float reach = Math.Max(Math.Abs(x0), x1) + half + 8, across = thick * 1.3f + half * 1.6f + 8;
        float cell = Mathf.Clamp(Math.Max(reach * 2, h - bottom) / 50, 1.5f, 2.5f); // (at 1 m a small arch was 20,000 triangles)
        int nx2 = (int)Math.Ceiling(reach * 2 / cell) + 1, ny2 = (int)Math.Ceiling((h * 1.12f + 6 - bottom) / cell) + 1, nz2 = (int)Math.Ceiling(across * 2 / cell) + 1;
        var field = new float[nx2 * ny2 * nz2];
        int At(int i, int j, int k) => (k * ny2 + j) * nx2 + i;
        Vector3 Corner(int i, int j, int k) => new(-reach + i * cell, bottom + j * cell, -across + k * cell);
        for (int k = 0; k < nz2; k++)
            for (int j = 0; j < ny2; j++)
                for (int i = 0; i < nx2; i++) { var p = Corner(i, j, k); field[At(i, j, k)] = Solid(p.X, p.Y, p.Z); }
        // Surface nets: a point in each cell the surface crosses (the mean of its edges' crossings), a quad across each crossed edge.
        var point = new int[field.Length];
        Array.Fill(point, -1);
        var points = new List<Vector3>();
        for (int k = 0; k + 1 < nz2; k++)
            for (int j = 0; j + 1 < ny2; j++)
                for (int i = 0; i + 1 < nx2; i++)
                {
                    var sum = Vector3.Zero; int crossings = 0;
                    for (int e = 0; e < 8; e++)
                        for (int axis = 0; axis < 3; axis++)
                        {
                            if ((e >> axis & 1) != 0) continue;
                            int ai = i + (e & 1), aj = j + (e >> 1 & 1), ak = k + (e >> 2 & 1);
                            int bi = ai + (axis == 0 ? 1 : 0), bj = aj + (axis == 1 ? 1 : 0), bk = ak + (axis == 2 ? 1 : 0);
                            float da = field[At(ai, aj, ak)], db = field[At(bi, bj, bk)];
                            if (da < 0 == db < 0) continue;
                            sum += Corner(ai, aj, ak).Lerp(Corner(bi, bj, bk), da / (da - db)); crossings++;
                        }
                    if (crossings == 0) continue;
                    point[At(i, j, k)] = points.Count;
                    points.Add(sum / crossings);
                }
        var shadeRng = new RandomStream(seed ^ 0x5EA5UL);
        void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward)
        {
            var normal = (b - a).Cross(c - a);
            if (normal.LengthSquared() < 1e-6f) return;
            normal = normal.Normalized();
            if (normal.Dot(outward) < 0) { (b, c) = (c, b); normal = -normal; }
            var mid = (a + b + c) / 3;
            float shade = (float)shadeRng.Unit();
            // The beds show as bands of paler and darker rock; dark and wet just over the sea; green only on the open top.
            float band = .5f + .5f * Noise(seed + 11, 0, (mid.Y + tilt * mid.X * side) / 2.4f, 0);
            var colour = Stone.Lerp(Dark, .05f + .45f * band + .1f * shade);
            if (normal.Y > .72f) colour = mid.Y > roof ? Grass.Lerp(Stone, .15f + .3f * shade) : Moss.Lerp(colour, .5f);
            float wet = 1 - Mathf.Clamp((mid.Y - .5f) / 3, 0, 1);
            colour = colour.Lerp(new Color(colour.R * .5f, colour.G * .5f, colour.B * .48f), wet);
            var tinted = new Color(colour.R * tint.R, colour.G * tint.G, colour.B * tint.B, tint.A);
            vertices.Add(a); vertices.Add(c); vertices.Add(b);
            for (int v = 0; v < 3; v++) { normals.Add(normal); colours.Add(tinted); }
        }
        void Quad(int a, int b, int c, int d, Vector3 outward)
        {
            if (a < 0 || b < 0 || c < 0 || d < 0) return;
            Tri(points[a], points[b], points[c], outward); Tri(points[a], points[c], points[d], outward);
        }
        for (int k = 1; k + 1 < nz2; k++)
            for (int j = 1; j + 1 < ny2; j++)
                for (int i = 1; i + 1 < nx2; i++)
                {
                    float here = field[At(i, j, k)];
                    float x = field[At(i + 1, j, k)], y = field[At(i, j + 1, k)], z = field[At(i, j, k + 1)];
                    if (here < 0 != x < 0) Quad(point[At(i, j - 1, k - 1)], point[At(i, j, k - 1)], point[At(i, j, k)], point[At(i, j - 1, k)], x > here ? Vector3.Right : Vector3.Left);
                    if (here < 0 != y < 0) Quad(point[At(i - 1, j, k - 1)], point[At(i, j, k - 1)], point[At(i, j, k)], point[At(i - 1, j, k)], y > here ? Vector3.Up : Vector3.Down);
                    if (here < 0 != z < 0) Quad(point[At(i - 1, j - 1, k)], point[At(i, j - 1, k)], point[At(i, j, k)], point[At(i - 1, j, k)], z > here ? Vector3.Back : Vector3.Forward);
                }
    }
}
