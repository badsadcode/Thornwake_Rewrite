using System.Collections.Generic;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Solid scatter objects near the player (level-0 tiles): tree trunks, dead trees and cacti, stumps, logs, bushes,
/// rocks and boulders get collision shapes (rocks: their own hull) on the tile's static body. Small stones, grass, reeds and hanging
/// cliff dressing stay passable.
/// </summary>
public static class ObjectCollision
{
    // Trunk radius (m at scale 1) per species, Vegetation.Species order; 0 = passable.
    private static readonly float[] Trunk =
    {
        .28f, .28f, .26f, .32f, .3f,        // pines, spruces
        .45f, .42f, .4f,                    // oaks
        .2f, .2f, .18f,                     // green birches
        .32f, .28f, .4f, .16f,              // maple, poplar, willow, sparse
        .18f, .18f,                         // autumn birches
        .3f, .26f, .36f, .3f,               // palms, cacti (saguaro, barrel cluster)
        .3f, .3f, .22f, .9f, .7f,           // dead tree, snag, acacia, jungle giant (buttresses), cypress (flared base)
        0f,                                 // dry shrub: passable
        .55f, .5f,                          // bald cypress (fluted base)
    };

    private static readonly Dictionary<float, CylinderShape3D> Cylinders = new();

    private static CylinderShape3D Cylinder(float radius, float height)
    {
        float key = Mathf.Snapped(radius, .02f) * 1000 + Mathf.Snapped(height, .5f);
        if (!Cylinders.TryGetValue(key, out var shape)) Cylinders[key] = shape = new CylinderShape3D { Radius = Mathf.Snapped(radius, .02f), Height = Mathf.Snapped(height, .5f) };
        return shape;
    }

    /// <summary>
    /// Add the tile's object shapes to <paramref name="body"/>. Round 30c: shapes go straight to the physics server
    /// (no CollisionShape3D node per trunk, rock and bush, which made committing a forest tile slow); the shape
    /// resources are kept alive in the body's metadata. The body must have no CollisionShape3D children (their shape
    /// indices would clash with these).
    /// </summary>
    public static void Add(StaticBody3D body, TileData data, List<Shape3D> keep)
    {
        var rid = body.GetRid();
        void Shape(Shape3D shape, Transform3D at) { keep.Add(shape); PhysicsServer3D.BodyAddShape(rid, shape.GetRid(), at); }
        foreach (var tree in data.Trees)
        {
            float radius = tree.Species < Trunk.Length ? Trunk[tree.Species] : .3f;
            if (radius <= 0) continue;
            float scale = tree.Transform.Basis.X.Length();
            var at = tree.Transform.Origin + Vector3.Up * 2f;
            Shape(Cylinder(radius * scale, 4), new Transform3D(Basis.Identity, at));
        }
        foreach (var rock in data.Rocks)
        {
            if (rock.Kind is GroundCover.Stone or GroundCover.CliffSlab) continue; // slabs lie on the (colliding) cliff
            var b = rock.Transform.Basis;
            float size = (b.X.Length() + b.Y.Length() + b.Z.Length()) / 3;
            if (size < .45f) continue;
            // The rock's own shape (its mesh's hull, scaled and tilted like the instance): a sphere around flat rocks
            // stood the player up to a metre above the visible stone.
            var unit = RockMeshes.HullPoints(rock.Kind);
            var points = new Vector3[unit.Length];
            for (int i = 0; i < unit.Length; i++) points[i] = b * unit[i];
            Shape(new ConvexPolygonShape3D { Points = points }, new Transform3D(Basis.Identity, rock.Transform.Origin));
        }
        foreach (var prop in data.Props)
        {
            var t = prop.Transform;
            float scale = t.Basis.X.Length();
            switch (prop.Kind)
            {
                case GroundCover.Log:
                    // Lying along its local X, 3.4 m long.
                    Shape(new CapsuleShape3D { Radius = .24f * scale, Height = 3.4f * scale },
                        new Transform3D(t.Basis.Orthonormalized() * new Basis(Vector3.Back, Mathf.Pi / 2), t.Origin + Vector3.Up * .2f * scale));
                    break;
                case GroundCover.Stump:
                    Shape(Cylinder(.4f * scale, 1), new Transform3D(Basis.Identity, t.Origin + Vector3.Up * .3f));
                    break;
                default: // bushes: a low, soft-edged obstacle
                    Shape(Cylinder(.45f * scale, 1), new Transform3D(Basis.Identity, t.Origin + Vector3.Up * .5f));
                    break;
            }
        }
    }
}
