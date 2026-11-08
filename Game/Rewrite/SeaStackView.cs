using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Sea stacks and arches (landform no. 9, D266): the rock the generator places off steep coasts
/// (<see cref="SeaStacks"/>), built here as procedural placeholder models until the art comes: solid shapes of worn rock
/// (signed distance), meshed on a worker — an arch a block of cliff with a hole worn through it (D267; a tube swept over a
/// half circle looked man-made), a stack a tower, a spire, a broken crown or a low stump (D268; columns of rings looked like
/// thimbles). Flat-shaded in the rocks' material (the biome's rock colour and
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
        mesh.SurfaceSetMaterial(0, RockMeshes.BigMaterial);
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
        // (paler than the boulders' stone: at the same colour they stood nearly black before the cliffs, D268)
        tint = new Color(tint.R * RockLight, tint.G * RockLight, tint.B * RockLight, tint.A);
        return rock.Kind == SeaStacks.Kind.Arch ? ArchModel(rock, tint) : StackModel(rock, tint);
    }

    private const float RockLight = 1.45f;
    private static readonly Color Stone = new("777a72"), Dark = new("5d605a"), Moss = new("66744f"), Grass = new("5f7a45");

    private static float SmoothMax(float a, float b, float k)
    {
        float h = Math.Max(k - Math.Abs(a - b), 0) / k;
        return Math.Max(a, b) + h * h * k * .25f;
    }

    /// <summary>Worn rock, near the surface only (the noise is the cost): vertical joints cut in, tilted beds as ledges, the
    /// waves' notch. <paramref name="size"/> scales it to the rock (a stump's joints are not a tower's).</summary>
    private static float Worn(float d, ulong seed, float x, float y, float z, float tilt, float bed, float size)
    {
        if (Math.Abs(d) >= 9 * size) return d;
        float joints = Math.Abs(Noise(seed + 7, x / (6 * size), y / 32, z / (6 * size))), s = y + tilt * x;
        d += size * (1.6f * (1 - Math.Min(joints * 5, 1)) - .3f + 1.6f * Noise(seed + 8, x / (8 * size), y / (8 * size), z / (8 * size)) + .6f * Noise(seed + 9, x / (3 * size), y / 3, z / (3 * size)));
        // (smooth steps, and beds three cells or more tall, set by the callers: sharp ones as tall as a cell drew a knitted zigzag)
        float f = Mathf.PosMod(s, bed) / bed;
        d += .7f * size * (-1 + 1.25f * Mathf.SmoothStep(.2f, .42f, f) - 1.25f * Mathf.SmoothStep(.84f, 1, f));
        d += 1.5f * Mathf.Clamp(1 - Math.Abs(y - 1.2f) / 2.8f, 0, 1);
        return d;
    }

    /// <summary>Blocks fallen from a rock, lying in the water: added to its solid.</summary>
    private static float Fallen(float d, List<(Vector3 At, float R)> blocks, ulong seed, float x, float y, float z)
    {
        foreach (var (at, r) in blocks)
        {
            float dx = x - at.X, dy = (y - at.Y) * 1.35f, dz = z - at.Z;
            float block = Mathf.Sqrt(dx * dx + dy * dy + dz * dz) - r;
            if (block < 3) block += r * .3f * Noise(seed + 10, x / 2.5f, y / 2.5f, z / 2.5f);
            d = Math.Min(d, block);
        }
        return d;
    }

    /// <summary>
    /// A stack (D268, Tom's call after the arches; columns of jagged rings looked like thimbles, all alike): one of four
    /// kinds — a flat-topped tower with sheer sides, a spire narrowing in steps, a broken crown split by a cleft and
    /// lower on one side, a low stump barely over the waves, wider than it is tall. Blocky in plan, stepped where beds end,
    /// worn as the arches are, fallen blocks at its feet; green only on an open top well above the spray. As high as
    /// the generator says, but for the stumps.
    /// </summary>
    private static Model StackModel(SeaStacks.Rock rock, Color tint)
    {
        var rng = new RandomStream(rock.Seed);
        ulong seed = rock.Seed;
        float r = (float)rock.Radius, bottom = (float)rock.Seabed - 3, kind = (float)rng.Unit();
        bool spire = kind >= .4f && kind < .62f, broken = kind >= .62f && kind < .82f, stump = kind >= .82f;
        float h = (float)rock.Height * (stump ? (float)rng.Between(.14, .34) : 1);
        float a = r * (float)rng.Between(.85, 1.25) * (stump ? 1.15f : 1), b = r * (float)rng.Between(.6, 1) * (stump ? 1.15f : 1);
        float taper = spire ? .62f : stump ? .2f : (float)rng.Between(.08, .22);
        int ledges = 2 + rng.Int(4);
        float turn = (float)rng.Unit() * Mathf.Tau, ca = Mathf.Cos(turn), sa = Mathf.Sin(turn);
        float tilt = (float)rng.Between(-.18, .18), bed = (float)rng.Between(2.6, 4.5), size = Mathf.Clamp(r / 10, .5f, 1.2f);
        float cleft = a * (float)rng.Between(-.35, .35), leanX = (float)rock.LeanX, leanZ = (float)rock.LeanZ;
        float reach = r * 2.2f + 8;
        float cell = Mathf.Clamp(Math.Max(reach * 2, h - bottom) / 30, 1.5f, 3);
        bed = Math.Max(bed, cell * 3);
        var blocks = new List<(Vector3 At, float R)>();
        for (int n = 1 + rng.Int(4); n > 0; n--)
        {
            float at = (float)rng.Unit() * Mathf.Tau, far = r * (float)rng.Between(1.05, 1.6);
            blocks.Add((new Vector3(far * Mathf.Cos(at), (float)rng.Between(-2, 1), far * Mathf.Sin(at)), r * (float)rng.Between(.15, .38)));
        }
        float Solid(float x, float y, float z)
        {
            float warpAt = 1.6f * r + 6;
            float px = x + r * .28f * Noise(seed + 2, x / warpAt, y / warpAt, z / warpAt) - leanX * Math.Max(y, 0);
            float pz = z + r * .28f * Noise(seed + 3, x / warpAt + 7, y / warpAt, z / warpAt) - leanZ * Math.Max(y, 0);
            float rx = ca * px + sa * pz, rz = ca * pz - sa * px;
            // Narrowing in steps, each a ledge where a bed ends; wider down in the water.
            float v = Mathf.Clamp(y / h, 0, 1) * ledges, step = Mathf.Floor(v), rise = v - step;
            float t = (step + Mathf.Clamp((rise - .72f) / .28f, 0, 1)) / ledges;
            float scale = (1 - taper * t) * (y < 0 ? 1 + .25f * Mathf.Clamp(-y / 15, 0, 1) : 1);
            float ax = a * scale, az = b * scale;
            // Blocky in plan: between a circle and a square.
            float plan = (Mathf.Pow(Mathf.Pow(Math.Abs(rx) / ax, 3.2f) + Mathf.Pow(Math.Abs(rz) / az, 3.2f), 1 / 3.2f) - 1) * Math.Min(ax, az);
            // The top: a tilted bed, rough; a spire's falls away from its peak; a broken crown's is split and lower on one side.
            float top = h + tilt * rx * .7f + .05f * h * Noise(seed + 4, x / (r + 4), 0, z / (r + 4));
            if (spire) top -= .55f * h * Mathf.Clamp(Mathf.Sqrt(rx * rx + rz * rz) / (r * 1.1f), 0, 1);
            if (broken)
            {
                top -= .22f * h * Mathf.Clamp(rx / a + .3f, 0, 1);
                top -= .4f * h * Mathf.Clamp(1 - Math.Abs(rx - cleft) / (a * .32f), 0, 1);
            }
            float d = SmoothMax(plan, y - top, 2.5f);
            d = Worn(d, seed, x, y, z, tilt, bed, size);
            return Fallen(d, blocks, seed, x, y, z);
        }
        return Surface(Solid, new Vector3(-reach, bottom, -reach), new Vector3(reach, h * 1.15f + 6, reach), cell, seed, tint, tilt, spire || stump ? 1e9f : Math.Max(.6f * h, 12));
    }

    /// <summary>
    /// A block of worn cliff rock for the rocks set into tall cliff faces (D272; RockMeshes kinds 5–10, the last three coarse
    /// for the middle distance): built 20 m across as the
    /// stacks are (blocky in plan, narrowing in a ledge or two, a rough tilted top, beds and vertical joints), closed below,
    /// then made unit size and centred, to be scaled per rock. Vertex colours as the stacks' (the instance gives the biome's
    /// tint and rock look), no sea's wet band.
    /// </summary>
    public static (Vector3[] Vertices, Vector3[] Normals, Color[] Colors) CliffBlock(ulong seed, float cell = 2.2f)
    {
        var rng = new RandomStream(seed);
        float a = (float)rng.Between(8, 10), b = (float)rng.Between(6, 9), h = 20;
        float tilt = (float)rng.Between(-.2, .2), bed = (float)rng.Between(4.5, 6.5), taper = (float)rng.Between(.05, .2);
        float turn = (float)rng.Unit() * Mathf.Tau, ca = Mathf.Cos(turn), sa = Mathf.Sin(turn);
        float Solid(float x, float y, float z)
        {
            float px = x + 2.4f * Noise(seed + 2, x / 14, y / 14, z / 14), pz = z + 2.4f * Noise(seed + 3, x / 14 + 7, y / 14, z / 14);
            float rx = ca * px + sa * pz, rz = ca * pz - sa * px;
            float t = Mathf.Clamp(y / h, 0, 1), scale = 1 - taper * (t > .55f ? 1 : t / .55f * .3f);
            float plan = (Mathf.Pow(Mathf.Pow(Math.Abs(rx) / (a * scale), 3.2f) + Mathf.Pow(Math.Abs(rz) / (b * scale), 3.2f), 1 / 3.2f) - 1) * Math.Min(a, b) * scale;
            float top = h * .92f + tilt * rx * .6f + 1.2f * Noise(seed + 4, x / 8, 0, z / 8);
            float d = SmoothMax(SmoothMax(plan, y - top, 2), .5f - y, 2);
            float joints = Math.Abs(Noise(seed + 7, x / 5, y / 25, z / 5)), s = y + tilt * x;
            d += 1.3f * (1 - Math.Min(joints * 5, 1)) - .25f + 1.1f * Noise(seed + 8, x / 6, y / 6, z / 6) + .4f * Noise(seed + 9, x / 2.5f, y / 2.5f, z / 2.5f);
            float f = Mathf.PosMod(s, bed) / bed;
            d += .6f * (-1 + 1.25f * Mathf.SmoothStep(.2f, .42f, f) - 1.25f * Mathf.SmoothStep(.84f, 1, f));
            return d;
        }
        // (2.2 m cells: at 1.3 m a wall's hundreds of blocks took the frame rate from 60 to 16)
        var model = Surface(Solid, new Vector3(-15, -1.5f, -15), new Vector3(15, 24, 15), cell, seed, new Color(RockLight, RockLight, RockLight, 1), tilt, 1e9f, sea: false);
        var vertices = new Vector3[model.Vertices.Length];
        for (int i = 0; i < vertices.Length; i++) vertices[i] = (model.Vertices[i] - new Vector3(0, h * .5f, 0)) / h;
        return (vertices, model.Normals, model.Colors);
    }

    /// <summary>
    /// An arch (D267, Tom: "they don't look natural"; a tube of even thickness swept over a half circle looked man-made): what
    /// is left of a headland — a block of cliff, one leg stout and one slight, its top sloping off and narrowing upward,
    /// a lopsided hole worn through with a flatter roof, in tilted beds and vertical joints, undercut at the waterline,
    /// fallen blocks at its feet. x along the legs, z across.
    /// </summary>
    private static Model ArchModel(SeaStacks.Rock rock, Color tint)
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
        // The grid, past the shape all round but its open bottom.
        float reach = Math.Max(Math.Abs(x0), x1) + half + 8, across = thick * 1.3f + half * 1.6f + 8;
        float cell = Mathf.Clamp(Math.Max(reach * 2, h - bottom) / 50, 1.5f, 2.5f); // (at 1 m a small arch was 20,000 triangles)
        bed = Math.Max(bed, cell * 3);
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
            d = Worn(d, seed, x, y, z, tilt, bed, 1);
            return Fallen(d, blocks, seed, x, y, z);
        }
        return Surface(Solid, new Vector3(-reach, bottom, -across), new Vector3(reach, h * 1.12f + 6, across), cell, seed, tint, tilt * side, roof);
    }

    /// <summary>
    /// A solid's surface (<paramref name="solid"/>: signed distance in metres, below 0 inside), taken with surface nets on
    /// a grid of <paramref name="cell"/> metres and flat-shaded, its bottom left open under the seabed: a point in each cell
    /// the surface crosses (the mean of its edges' crossings), a quad across each crossed edge. The beds show as bands of
    /// paler and darker rock; dark and wet just over the sea; green only on open tops above <paramref name="grassAbove"/>.
    /// </summary>
    private static Model Surface(Func<float, float, float, float> solid, Vector3 min, Vector3 max, float cell, ulong seed, Color tint, float tilt, float grassAbove, bool sea = true)
    {
        int nx = (int)Math.Ceiling((max.X - min.X) / cell) + 1, ny = (int)Math.Ceiling((max.Y - min.Y) / cell) + 1, nz = (int)Math.Ceiling((max.Z - min.Z) / cell) + 1;
        var field = new float[nx * ny * nz];
        int At(int i, int j, int k) => (k * ny + j) * nx + i;
        Vector3 Corner(int i, int j, int k) => min + new Vector3(i, j, k) * cell;
        for (int k = 0; k < nz; k++)
            for (int j = 0; j < ny; j++)
                for (int i = 0; i < nx; i++) { var p = Corner(i, j, k); field[At(i, j, k)] = solid(p.X, p.Y, p.Z); }
        var point = new int[field.Length];
        Array.Fill(point, -1);
        var points = new List<Vector3>();
        for (int k = 0; k + 1 < nz; k++)
            for (int j = 0; j + 1 < ny; j++)
                for (int i = 0; i + 1 < nx; i++)
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
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var colours = new List<Color>();
        var shadeRng = new RandomStream(seed ^ 0x5EA5UL);
        // (bands wider than the cells, one colour across a quad: bands as narrow as the cells, per triangle, drew a knitted zigzag)
        float bandHeight = Math.Max(3, cell * 2.6f);
        void Tri(Vector3 a, Vector3 b, Vector3 c, float band, float shade)
        {
            var normal = (b - a).Cross(c - a);
            if (normal.LengthSquared() < 1e-6f) return;
            normal = normal.Normalized();
            var mid = (a + b + c) / 3;
            var colour = Stone.Lerp(Dark, .05f + .4f * band + .1f * shade);
            if (normal.Y > .72f) colour = mid.Y > grassAbove ? Grass.Lerp(Stone, .15f + .3f * shade) : Moss.Lerp(colour, .5f);
            float wet = sea ? 1 - Mathf.Clamp((mid.Y - .5f) / 3, 0, 1) : 0;
            colour = colour.Lerp(new Color(colour.R * .5f, colour.G * .5f, colour.B * .48f), wet);
            var tinted = new Color(colour.R * tint.R, colour.G * tint.G, colour.B * tint.B, tint.A);
            // (Godot front faces are clockwise seen from outside)
            vertices.Add(a); vertices.Add(c); vertices.Add(b);
            for (int v = 0; v < 3; v++) { normals.Add(normal); colours.Add(tinted); }
        }
        // (facing from the grid: flipping a triangle by its own normal turned folded ones inside out, a hole right through the rock)
        void Quad(int a, int b, int c, int d, bool flip)
        {
            if (a < 0 || b < 0 || c < 0 || d < 0) return;
            if (flip) (b, d) = (d, b);
            var mid = (points[a] + points[b] + points[c] + points[d]) / 4;
            float band = .5f + .5f * Noise(seed + 11, 0, (mid.Y + tilt * mid.X) / bandHeight, 0), shade = (float)shadeRng.Unit();
            Tri(points[a], points[b], points[c], band, shade); Tri(points[a], points[c], points[d], band, shade);
        }
        for (int k = 1; k + 1 < nz; k++)
            for (int j = 1; j + 1 < ny; j++)
                for (int i = 1; i + 1 < nx; i++)
                {
                    float here = field[At(i, j, k)];
                    float x = field[At(i + 1, j, k)], y = field[At(i, j + 1, k)], z = field[At(i, j, k + 1)];
                    if (here < 0 != x < 0) Quad(point[At(i, j - 1, k - 1)], point[At(i, j, k - 1)], point[At(i, j, k)], point[At(i, j - 1, k)], x < here);
                    if (here < 0 != y < 0) Quad(point[At(i - 1, j, k - 1)], point[At(i, j, k - 1)], point[At(i, j, k)], point[At(i - 1, j, k)], y > here);
                    if (here < 0 != z < 0) Quad(point[At(i - 1, j - 1, k)], point[At(i, j - 1, k)], point[At(i, j, k)], point[At(i - 1, j, k)], z < here);
                }
        return new Model(vertices.ToArray(), normals.ToArray(), colours.ToArray());
    }
}
