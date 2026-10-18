using System.Collections.Generic;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// The gameplay models (D120, D121): weapons and building pieces from <c>Assets/Gameplay</c> (made by
/// <c>tools/build_gameplay_models.py</c>), animals from <c>Assets/Animals</c>. Their materials are replaced by name
/// (Bark, Wood, Thatch, Stone, Fibre) with the game's own: a colour and a stretched noise grain, so the placeholder
/// shapes read as wood and straw. Blueprints and the placement ghost wear see-through materials; finished pieces get
/// collision built from their meshes (meshes named <c>Col…</c> are collision only, hidden).
/// </summary>
public static class GameplayModels
{
    private static readonly Dictionary<string, PackedScene?> Scenes = new();
    private static readonly Dictionary<string, Material> Named = new();
    private static readonly Dictionary<string, List<(Shape3D Shape, Transform3D At, string Under)>> ShapeCache = new();
    private static StandardMaterial3D? _ghostOk, _ghostBad, _blueprint, _blueprintAimed;

    /// <summary>The model at <paramref name="path"/> (a new instance each call, materials replaced), or null if missing.</summary>
    public static Node3D? Instance(string path)
    {
        if (!Scenes.TryGetValue(path, out var scene))
        {
            scene = ResourceLoader.Exists(path) ? GD.Load<PackedScene>(path) : null;
            if (scene == null) GD.PushWarning($"gameplay model missing: {path}");
            Scenes[path] = scene;
        }
        if (scene?.Instantiate() is not Node3D node) return null;
        Retexture(node);
        return node;
    }

    /// <summary>A weapon (item id): its grip at the origin, pointing +Z.</summary>
    public static Node3D Weapon(string id) => Instance($"res://Assets/Gameplay/Weapons/{id}.glb") ?? new Node3D { Name = id };

    private static void Retexture(Node node)
    {
        foreach (var child in node.FindChildren("*", "MeshInstance3D", true, false))
        {
            if (child is not MeshInstance3D mi || mi.Mesh == null) continue;
            for (int i = 0; i < mi.Mesh.GetSurfaceCount(); i++)
            {
                string name = mi.Mesh.SurfaceGetMaterial(i)?.ResourceName ?? "";
                if (Material(name) is { } m) mi.SetSurfaceOverrideMaterial(i, m);
            }
        }
    }

    /// <summary>The game's material for a model material name (null: keep the model's own).</summary>
    public static Material? Material(string name)
    {
        string key = name.Split('.')[0];
        if (Named.TryGetValue(key, out var known)) return known;
        (Color dark, Color light, Vector2 scale, float rough)? look = key switch
        {
            "Bark" => (new Color("3b2a1c"), new Color("6e5236"), new Vector2(3.5f, .35f), .95f),
            "Wood" => (new Color("8a6a45"), new Color("bf9a68"), new Vector2(2.5f, .4f), .85f),
            "Thatch" => (new Color("7d6634"), new Color("c4a95c"), new Vector2(9f, .55f), 1f),
            "Stone" => (new Color("54524d"), new Color("8d8a83"), new Vector2(2f, 2f), .8f),
            "Fibre" => (new Color("6b5a3a"), new Color("a48d60"), new Vector2(8f, 1.5f), 1f),
            _ => null,
        };
        if (look is not { } l) return null;
        var gradient = new Gradient();
        gradient.SetColor(0, l.dark); gradient.SetColor(1, l.light);
        var noise = new FastNoiseLite { NoiseType = FastNoiseLite.NoiseTypeEnum.SimplexSmooth, Frequency = .045f, FractalOctaves = 4, Seed = key.Length * 31 };
        var albedo = new NoiseTexture2D { Noise = noise, Seamless = true, Width = 256, Height = 256, ColorRamp = gradient };
        var bump = new NoiseTexture2D { Noise = noise, Seamless = true, Width = 256, Height = 256, AsNormalMap = true, BumpStrength = key == "Thatch" ? 10 : 5 };
        var mat = new StandardMaterial3D
        {
            ResourceName = key, AlbedoTexture = albedo, NormalEnabled = true, NormalTexture = bump, NormalScale = .7f,
            Roughness = l.rough, Uv1Scale = new Vector3(l.scale.X, l.scale.Y, 1), CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        Named[key] = mat;
        return mat;
    }

    private static StandardMaterial3D SeeThrough(Color c) => new()
    {
        AlbedoColor = c, Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled, NoDepthTest = false,
    };

    /// <summary>The placement ghost: blue where it can go, red where it cannot.</summary>
    public static Material Ghost(bool ok) => ok ? _ghostOk ??= SeeThrough(new Color(.45f, .78f, 1f, .38f)) : _ghostBad ??= SeeThrough(new Color(1f, .35f, .3f, .38f));
    /// <summary>A placed blueprint waiting for materials (brighter when aimed at).</summary>
    public static Material Blueprint(bool aimed = false) => aimed ? _blueprintAimed ??= SeeThrough(new Color(.75f, .92f, 1f, .42f)) : _blueprint ??= SeeThrough(new Color(.62f, .82f, 1f, .26f));

    /// <summary>Every mesh of the model (not its Col… meshes) wears <paramref name="m"/> (null: its own again).</summary>
    public static void Wear(Node3D model, Material? m)
    {
        foreach (var child in model.FindChildren("*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mi && !mi.Name.ToString().StartsWith("Col"))
            {
                // (its own override — a campfire's flames — is kept to come back to)
                // (Godot drops a null meta value: "own_set" marks that the own one, maybe none, was noted)
                if (!mi.HasMeta("own_set")) { mi.SetMeta("own_set", true); if (mi.MaterialOverride != null) mi.SetMeta("own_material", mi.MaterialOverride); }
                mi.MaterialOverride = m ?? (mi.HasMeta("own_material") ? mi.GetMeta("own_material").As<Material>() : null);
                mi.CastShadow = m == null ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
            }
    }

    /// <summary>Hide the Col… meshes (collision only).</summary>
    public static void HideCollisionMeshes(Node3D model)
    {
        foreach (var child in model.FindChildren("Col*", "MeshInstance3D", true, false))
            if (child is MeshInstance3D mi) mi.Visible = false;
    }

    /// <summary>
    /// Collision shapes of a model (cached per model): its Col… meshes if it has any, else all its meshes, each as a
    /// two-sided triangle mesh; with the transform from the model's root and the name of the node they move with
    /// ("Leaf" for a door's leaf, "" for the root).
    /// </summary>
    public static List<(Shape3D Shape, Transform3D At, string Under)> Shapes(string path, Node3D model)
    {
        if (ShapeCache.TryGetValue(path, out var known)) return known;
        var list = new List<(Shape3D, Transform3D, string)>();
        var meshes = new List<MeshInstance3D>();
        foreach (var child in model.FindChildren("*", "MeshInstance3D", true, false)) if (child is MeshInstance3D mi && mi.Mesh != null) meshes.Add(mi);
        bool hasCol = meshes.Exists(m => m.Name.ToString().StartsWith("Col"));
        foreach (var mi in meshes)
        {
            if (hasCol && !mi.Name.ToString().StartsWith("Col")) continue;
            // From the mesh up to the model's root, or to a moving part ("Leaf").
            var t = Transform3D.Identity;
            string under = "";
            for (Node? n = mi; n != null && n != model; n = n.GetParent())
            {
                if (n.Name == "Leaf") { under = "Leaf"; break; }
                if (n is Node3D n3) t = n3.Transform * t;
            }
            if (mi.Mesh.CreateTrimeshShape() is ConcavePolygonShape3D shape)
            {
                shape.BackfaceCollision = true;
                list.Add((shape, t, under));
            }
        }
        ShapeCache[path] = list;
        return list;
    }

    /// <summary>Free the cached resources (scene exit: Godot resources outliving the engine crash on shutdown).</summary>
    public static void Release()
    {
        Scenes.Clear(); Named.Clear(); ShapeCache.Clear();
        _ghostOk = _ghostBad = _blueprint = _blueprintAimed = null;
    }
}
