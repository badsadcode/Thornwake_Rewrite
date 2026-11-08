using System;
using System.Collections.Generic;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Procedural low-poly rocks (until Tom's rock assets arrive): a jittered, flat-shaded icosphere per kind, in the
/// valley game's stone palette with a hint of moss on top. Built once on the main thread.
/// </summary>
public static class RockMeshes
{
    private static ArrayMesh[]? _meshes;
    private static ShaderMaterial? _material, _big;

    private static ShaderMaterial CreateMaterial()
    {
        var m = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/Rock.gdshader") };
        m.SetShaderParameter("rock_tex", RockTextureArray.Get());
        return m;
    }

    /// <summary>Free the cached meshes (scene exit): static Godot resources outliving the engine crash on shutdown.</summary>
    public static void Release()
    {
        if (_meshes != null) foreach (var mesh in System.Linq.Enumerable.Distinct(_meshes)) mesh.Dispose();
        _meshes = null; _builders = null; _material = null; _big = null;
    }

    /// <summary>The rocks' material for rocks tens of metres tall (sea stacks and arches, D268; cliff rocks, D272): the texture
    /// every 19 m and, turned, every 5.9 m (repeating every 4 m it drew rows of little triangles over them, like knitting);
    /// Tom's cliff rock on their sides every 14 m (D273).</summary>
    public static ShaderMaterial BigMaterial
    {
        get
        {
            if (_big != null) return _big;
            _big = CreateMaterial();
            _big.SetShaderParameter("tile", 19f);
            _big.SetShaderParameter("second_tile", 5.9f);
            // Tom's cliff rocks on their sides (D273), made seamless (a half-shifted copy blended over their edges): per rock
            // look of the biome (layered, granite, basalt, sandstone, mossy, icy) a first and a second.
            _big.SetShaderParameter("cliff_tex", CliffTextures());
            _big.SetShaderParameter("cliff_tile", 14f);
            // (sandstone country's first is the pale granite, the sandstone second: all sandstone drew hot orange-red cliffs
            // over the whole savanna)
            _big.SetShaderParameter("cliff_first", new[] { 0, 3, 2, 3, 4, 1 });
            _big.SetShaderParameter("cliff_second", new[] { 1, 1, 4, 5, 0, 2 });
            _big.SetShaderParameter("cliff_means", new[] { .275f, .427f, .395f, .514f, .269f, .468f });
            _big.SetShaderParameter("cliff_contrast", new[] { .55f, .85f, .85f, .85f, .7f, .85f });
            _big.SetShaderParameter("big_look", 1f);
            return _big;
        }
    }

    /// <summary>Each mesh and how to build it, so the Rock shading setting can rebuild them in place (the tiles' instances keep them).</summary>
    private static List<(ArrayMesh Mesh, Func<(Vector3[], Vector3[], Color[])> Make, Material Material)>? _builders;

    public static ArrayMesh Get(int kind)
    {
        // 3: cliff slab, 4: huge/shore boulder — chunky, flat-faced (few facets), like blocky rocks; from CliffRock on: rocks of a
        // tall cliff's face (D272), tens of metres: worn blocks like the sea stacks (as lumps they looked stuck on), six shapes
        // (D275), in the big rocks' material (the texture at 19 m and 5.9 m, not 4 m); from CliffCoarse the same coarse (~a
        // quarter of the triangles) for the middle distance; from CliffLip the near ones again, for the rocks across a lip.
        if (_meshes == null)
        {
            _builders = new();
            ArrayMesh Add(Func<(Vector3[], Vector3[], Color[])> make, Material material)
            {
                var mesh = new ArrayMesh();
                Fill(mesh, make(), material);
                _builders.Add((mesh, make, material));
                return mesh;
            }
            _material ??= CreateMaterial();
            var list = new List<ArrayMesh>();
            for (int k = 0; k < 5; k++) { int kk = k; list.Add(Add(() => Icosphere(Kinds[kk]), _material)); }
            var near = new List<ArrayMesh>();
            for (int k = 0; k < GroundCover.CliffShapes; k++) { int kk = k; near.Add(Add(() => SeaStackView.CliffBlock(0xB10C1UL + (ulong)kk, 2.2f, kk), BigMaterial)); }
            list.AddRange(near);
            for (int k = 0; k < GroundCover.CliffShapes; k++) { int kk = k; list.Add(Add(() => SeaStackView.CliffBlock(0xB10C1UL + (ulong)kk, 4.4f, kk), BigMaterial)); }
            list.AddRange(near);
            _meshes = list.ToArray();
        }
        return _meshes[Math.Clamp(kind, 0, _meshes.Length - 1)];
    }

    /// <summary>Rebuild every rock mesh in place (the Rock shading setting changed): the tiles' instances keep the same meshes.</summary>
    public static void Reshade()
    {
        if (_builders == null) return;
        foreach (var (mesh, make, material) in _builders) { mesh.ClearSurfaces(); Fill(mesh, make(), material); }
    }

    private static void Fill(ArrayMesh mesh, (Vector3[] Vertices, Vector3[] Normals, Color[] Colors) data, Material material)
    {
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
        arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
        arrays[(int)Mesh.ArrayType.Color] = data.Colors;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, material);
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

    /// <summary>Tom's six cliff textures (512², made seamless, D273), as one array: his cliff rock, alpine rock, blue-grey
    /// slate, grey granite, mossy stone, warm sandstone.</summary>
    private static Texture2DArray CliffTextures()
    {
        var images = new Godot.Collections.Array<Image>();
        foreach (var name in new[] { "rock_tom", "alpine_rock", "blue_gray_slate", "granite_gray", "mossy_stone", "warm_sandstone" })
            images.Add(GD.Load<Texture2D>($"res://Assets/Terrain/Cliff/cliff_{name}_tiled.png").GetImage());
        var array = new Texture2DArray();
        array.CreateFromImages(images);
        return array;
    }

    private static (Vector3[], Vector3[], Color[]) Icosphere((int Subdivisions, uint Seed, float Roughness) kind)
    {
        var v = Shape(kind, out var f, out var rng);
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var colours = new List<Color>();
        var stone = new Color("777a72"); var dark = new Color("5d605a"); var moss = new Color("66744f");
        foreach (var (ia, ib0, ic0) in f)
        {
            int a = ia, b = ib0, c = ic0;
            var normal = (v[b] - v[a]).Cross(v[c] - v[a]).Normalized();
            var centroid = (v[a] + v[b] + v[c]) / 3 - new Vector3(0, .12f, 0);
            if (normal.Dot(centroid) < 0) { (b, c) = (c, b); normal = -normal; } // outward
            float shade = rng.Randf();
            var colour = normal.Y > .75f ? moss.Lerp(stone, .4f + .3f * shade) : stone.Lerp(dark, shade * .6f);
            // Godot front faces are clockwise seen from outside: a, c, b for a counter-clockwise outward face.
            vertices.Add(v[a]); vertices.Add(v[c]); vertices.Add(v[b]);
            for (int k = 0; k < 3; k++) { normals.Add(normal); colours.Add(colour); }
        }
        var vs = vertices.ToArray();
        return (vs, SeaStackView.Shade(vs, normals.ToArray()), colours.ToArray());
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
