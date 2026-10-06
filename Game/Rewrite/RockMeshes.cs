using System;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Procedural low-poly rocks (until Tom's rock assets arrive): a jittered, flat-shaded icosphere per kind, in the
/// valley game's stone palette with a hint of moss on top. Built once on the main thread.
/// </summary>
public static class RockMeshes
{
    private static ArrayMesh[]? _meshes;
    private static ShaderMaterial? _material;

    private static ShaderMaterial CreateMaterial()
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/Rock.gdshader") };
        m.SetShaderParameter("rock_tex", RockTextureArray.Get());
        return m;
    }

    /// <summary>Free the cached meshes (scene exit): static Godot resources outliving the engine crash on shutdown.</summary>
    public static void Release()
    {
        if (_meshes != null) foreach (var mesh in _meshes) mesh.Dispose();
        _meshes = null; _material = null;
    }

    public static ArrayMesh Get(int kind)
    {
        // 3: cliff slab, 4: huge/shore boulder — chunky, flat-faced (few facets), like blocky rocks.
        _meshes ??= new[] { Build(Kinds[0]), Build(Kinds[1]), Build(Kinds[2]), Build(Kinds[3]), Build(Kinds[4]) };
        return _meshes[Math.Clamp(kind, 0, _meshes.Length - 1)];
    }

    private static readonly (int Subdivisions, uint Seed, float Roughness)[] Kinds =
        { (1, 0x51u, .22f), (1, 0x77u, .28f), (2, 0x93u, .3f), (0, 0xC1u, .32f), (0, 0xB7u, .26f) };
    private static readonly Vector3[]?[] Hulls = new Vector3[]?[5];

    /// <summary>
    /// The vertices of a rock kind's mesh (unit size, before the instance transform) for its collision hull, so the
    /// player stands on the visible stone (a sphere stood well above flat rocks). Thread-safe; no rendering objects.
    /// </summary>
    public static Vector3[] HullPoints(int kind)
    {
        kind = Math.Clamp(kind, 0, Kinds.Length - 1);
        lock (Hulls) return Hulls[kind] ??= Shape(Kinds[kind], out _, out _).ToArray();
    }

    private static ArrayMesh Build((int Subdivisions, uint Seed, float Roughness) kind)
    {
        var v = Shape(kind, out var f, out var rng);
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var stone = new Color("777a72"); var dark = new Color("5d605a"); var moss = new Color("66744f");
        foreach (var (ia, ib0, ic0) in f)
        {
            int a = ia, b = ib0, c = ic0;
            var normal = (v[b] - v[a]).Cross(v[c] - v[a]).Normalized();
            var centroid = (v[a] + v[b] + v[c]) / 3 - new Vector3(0, .12f, 0);
            if (normal.Dot(centroid) < 0) { (b, c) = (c, b); normal = -normal; } // outward
            float shade = rng.Randf();
            var colour = normal.Y > .75f ? moss.Lerp(stone, .4f + .3f * shade) : stone.Lerp(dark, shade * .6f);
            st.SetColor(colour);
            // Godot front faces are clockwise seen from outside: a, c, b for a counter-clockwise outward face.
            st.SetNormal(normal); st.AddVertex(v[a]); st.SetNormal(normal); st.AddVertex(v[c]); st.SetNormal(normal); st.AddVertex(v[b]);
        }
        var mesh = st.Commit();
        _material ??= CreateMaterial();
        mesh.SurfaceSetMaterial(0, _material);
        return mesh;
    }

    /// <summary>A jittered icosphere with a flattened bottom; the generator continues into the mesh's face shading.</summary>
    private static System.Collections.Generic.List<Vector3> Shape((int Subdivisions, uint Seed, float Roughness) kind,
        out System.Collections.Generic.List<(int, int, int)> faces, out RandomNumberGenerator rng)
    {
        var (subdivisions, seed, roughness) = kind;
        // Icosahedron.
        float t = (1 + Mathf.Sqrt(5)) / 2;
        var v = new System.Collections.Generic.List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0), new(0, -1, t), new(0, 1, t),
            new(0, -1, -t), new(0, 1, -t), new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        var f = new System.Collections.Generic.List<(int, int, int)>
        {
            (0,11,5),(0,5,1),(0,1,7),(0,7,10),(0,10,11),(1,5,9),(5,11,4),(11,10,2),(10,7,6),(7,1,8),
            (3,9,4),(3,4,2),(3,2,6),(3,6,8),(3,8,9),(4,9,5),(2,4,11),(6,2,10),(8,6,7),(9,8,1),
        };
        for (int s = 0; s < subdivisions; s++)
        {
            var next = new System.Collections.Generic.List<(int, int, int)>();
            var cache = new System.Collections.Generic.Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = Math.Min(a, b) * 100000L + Math.Max(a, b);
                if (cache.TryGetValue(key, out int m)) return m;
                v.Add((v[a] + v[b]) * .5f); cache[key] = v.Count - 1; return v.Count - 1;
            }
            foreach (var (a, b, c) in f)
            {
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                next.Add((a, ab, ca)); next.Add((b, bc, ab)); next.Add((c, ca, bc)); next.Add((ab, bc, ca));
            }
            f = next;
        }
        // Jitter radially (deterministic), flatten the bottom half so rocks sit in the ground.
        rng = new RandomNumberGenerator { Seed = seed };
        for (int i = 0; i < v.Count; i++)
        {
            var n = v[i].Normalized();
            float r = 1 + (rng.Randf() - .5f) * 2 * roughness;
            var p = n * r * .5f;
            if (p.Y < 0) p.Y *= .45f;
            v[i] = p + new Vector3(0, .12f, 0);
        }
        faces = f;
        return v;
    }
}
