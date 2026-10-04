using System.Collections.Generic;
using Godot;
using Thornwake.World;

namespace Thornwake.Rewrite;

/// <summary>
/// Placeholder props built from the valley game's existing procedural models, flattened into (mesh, transform)
/// parts for MultiMesh drawing. Replace an entry with an imported asset when Tom's scatter assets arrive
/// (see docs/ASSET_SPEC.md); placement rules in <see cref="GroundCover.Props"/> stay the same.
/// </summary>
public sealed class PropCatalog
{
    private readonly Dictionary<int, List<(Mesh Mesh, Transform3D Transform)>> _parts = new();

    public List<(Mesh Mesh, Transform3D Transform)> Parts(int kind)
    {
        if (_parts.TryGetValue(kind, out var parts)) return parts;
        var root = kind switch
        {
            GroundCover.Bush => Bush(false),
            GroundCover.BerryBush => Bush(true),
            GroundCover.Stump => Visuals.Stump(),
            _ => Log(),
        };
        parts = Flatten(root);
        root.Free();
        _parts[kind] = parts;
        return parts;
    }

    private static List<(Mesh, Transform3D)> Flatten(Node3D root)
    {
        var parts = new List<(Mesh, Transform3D)>();
        foreach (var source in WoodlandAssets.Meshes(root))
        {
            var mesh = (Mesh)source.Mesh.Duplicate();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++) mesh.SurfaceSetMaterial(s, source.GetActiveMaterial(s));
            Transform3D transform = source.Transform; Node? parent = source.GetParent();
            while (parent is Node3D p && p != root) { transform = p.Transform * transform; parent = p.GetParent(); }
            parts.Add((mesh, root.Transform * transform));
        }
        return parts;
    }

    // The valley game's berry bush (Valley.BerryBush), as a prop.
    private static Node3D Bush(bool fruit)
    {
        var n = new Node3D();
        var leaf = Visuals.Material(new Color("526943"));
        for (int i = 0; i < 3; i++)
            n.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .5f, Height = .9f, RadialSegments = 7, Rings = 4 }, Position = new Vector3((i - 1) * .3f, .55f, 0), MaterialOverride = leaf });
        if (fruit)
        {
            var berry = Visuals.Material(new Color("a45f56"));
            for (int i = 0; i < 8; i++)
            {
                float a = i * 2.4f;
                n.AddChild(new MeshInstance3D { Mesh = new SphereMesh { Radius = .085f, Height = .17f, RadialSegments = 5, Rings = 3 }, Position = new Vector3(Mathf.Cos(a) * .55f, .5f + i * .035f, Mathf.Sin(a) * .35f), MaterialOverride = berry });
            }
        }
        return n;
    }

    // A fallen log in the valley palette: bark cylinder lying on its side with lighter end grain.
    private static Node3D Log()
    {
        var n = new Node3D();
        var bark = Visuals.Material(new Color("5c4632"));
        var grain = Visuals.Material(new Color("b39362"));
        var body = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .2f, BottomRadius = .24f, Height = 3.4f, RadialSegments = 8, Rings = 1 }, MaterialOverride = bark };
        body.Rotation = new Vector3(0, 0, Mathf.Pi / 2); body.Position = new Vector3(0, .2f, 0);
        n.AddChild(body);
        foreach (float side in new[] { -1f, 1f })
        {
            var cap = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = side < 0 ? .2f : .23f, BottomRadius = side < 0 ? .2f : .23f, Height = .02f, RadialSegments = 8 }, MaterialOverride = grain };
            cap.Rotation = new Vector3(0, 0, Mathf.Pi / 2); cap.Position = new Vector3(side * 1.7f, .2f, 0);
            n.AddChild(cap);
        }
        return n;
    }
}
