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
/// jagged rock rings, undercut at the waterline, stepped in strata, tapering to a grassy top; an arch a thick rock tube
/// swept up from the seabed, over and down again. Flat-shaded in the rocks' material (the biome's rock colour and
/// texture). Drawn within 12 km of the camera, solid (their own faces as collision) within 700 m. Each is placed from
/// its world position against the scene's origin every time that moves (world-space objects left behind at a shift
/// jumped away, D264).
/// </summary>
public sealed class SeaStackView
{
    private const double DrawRange = 12000, SolidRange = 700;
    private readonly Node3D _parent;
    private readonly Dictionary<ulong, (MeshInstance3D Mesh, StaticBody3D? Body, SeaStacks.Rock Rock, Vector3[] Faces)> _shown = new();
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
            Clear();
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
                if (!_shown.ContainsKey(key) && built++ < 6) Add(terrain, rock, originX, originZ); // (a few a frame; the rest next time)
            if (built > 6) _lastX = double.NaN;
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

    private void Add(TerrainSampler terrain, SeaStacks.Rock rock, double originX, double originZ)
    {
        var (mesh, faces) = Build(rock, Tint(terrain, rock.X, rock.Z));
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

    private static (ArrayMesh, Vector3[]) Build(SeaStacks.Rock rock, Color tint)
    {
        var rings = rock.Kind == SeaStacks.Kind.Arch ? ArchRings(rock) : StackRings(rock);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var faces = new List<Vector3>();
        var stone = new Color("777a72"); var dark = new Color("5d605a"); var moss = new Color("66744f"); var grass = new Color("5f7a45");
        var rng = new RandomStream(rock.Seed ^ 0x5EA5UL);
        void Tri(Vector3 a, Vector3 b, Vector3 c, bool top)
        {
            var normal = (b - a).Cross(c - a).Normalized();
            float shade = (float)rng.Unit();
            var colour = normal.Y > .75f ? (top ? grass : moss).Lerp(stone, .25f + .3f * shade) : stone.Lerp(dark, shade * .6f);
            st.SetColor(new Color(colour.R * tint.R, colour.G * tint.G, colour.B * tint.B, tint.A));
            // (Godot front faces are clockwise seen from outside)
            st.SetNormal(normal); st.AddVertex(a); st.SetNormal(normal); st.AddVertex(c); st.SetNormal(normal); st.AddVertex(b);
            faces.Add(a); faces.Add(c); faces.Add(b);
        }
        int n = rings[0].Length;
        for (int k = 0; k + 1 < rings.Count; k++)
            for (int i = 0; i < n; i++)
            {
                int j = (i + 1) % n;
                Vector3 a = rings[k][i], b = rings[k][j], c = rings[k + 1][j], d = rings[k + 1][i];
                Tri(a, b, c, false); Tri(a, c, d, false);
            }
        if (rock.Kind == SeaStacks.Kind.Stack)
        {
            // The top: a low dome over the last ring, grassy.
            var last = rings[^1];
            var centre = last.Aggregate(Vector3.Zero, (s, v) => s + v) / n + Vector3.Up * (float)(rock.Radius * .12);
            for (int i = 0; i < n; i++) Tri(last[i], last[(i + 1) % n], centre, true);
        }
        var mesh = st.Commit();
        mesh.SurfaceSetMaterial(0, RockMeshes.Material);
        return (mesh, faces.ToArray());
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

    /// <summary>An arch: a rock tube swept along a path in its plane (local x, y) — up a leg from under the seabed, over a
    /// stretched half-circle, down the other leg — 12 points round, thicker toward the bottom, jagged by noise.</summary>
    private static List<Vector3[]> ArchRings(SeaStacks.Rock rock)
    {
        const int N = 12;
        float span = (float)rock.Span, half = (float)rock.Radius, h = (float)rock.Height, bottom = (float)rock.Seabed - 3;
        float a = span * .5f + half, spring = Math.Max(2, .3f * (h - half)), rise = h - half - spring;
        var path = new List<Vector3>();
        for (float y = bottom; y < spring; y += 3) path.Add(new Vector3(-a, y, 0));
        for (int s = 0; s <= 14; s++) { float phi = Mathf.Pi - Mathf.Pi * s / 14; path.Add(new Vector3(a * Mathf.Cos(phi), spring + rise * Mathf.Sin(phi), 0)); }
        for (float y = spring - 3; y >= bottom; y -= 3) path.Add(new Vector3(a, y, 0));
        if (path[^1].Y > bottom) path.Add(new Vector3(a, bottom, 0));
        var rings = new List<Vector3[]>();
        for (int k = 0; k < path.Count; k++)
        {
            var t = (path[Math.Min(k + 1, path.Count - 1)] - path[Math.Max(k - 1, 0)]).Normalized();
            var inPlane = new Vector3(-t.Y, t.X, 0); // (perpendicular to the path in the arch's plane)
            var p = path[k];
            float thick = 1 + .5f * Mathf.Clamp(1 - (p.Y - bottom) / (h - bottom), 0, 1); // (stouter legs)
            var ring = new Vector3[N];
            for (int i = 0; i < N; i++)
            {
                float ang = i * Mathf.Tau / N, c = Mathf.Cos(ang), s2 = Mathf.Sin(ang);
                float jag = 1 + .18f * Noise(rock.Seed, p.X / 6 + c, p.Y / 6, s2) + .07f * Noise(rock.Seed + 1, p.X / 2, p.Y / 2, s2 * 3);
                ring[i] = p + inPlane * (c * half * thick * jag) + Vector3.Back * (s2 * half * 1.5f * thick * jag);
            }
            rings.Add(ring);
        }
        return rings;
    }
}
