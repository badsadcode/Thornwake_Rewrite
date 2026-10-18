using System.Collections.Generic;
using System.Linq;
using Godot;
using Thornwake.Gameplay;

namespace Thornwake.Rewrite;

/// <summary>
/// Snap points shown while building (Tom, build 66: "could we make the snapping points toggleable, visible or invisible";
/// D130): small dots at the points of the pieces near the ghost (pale blue), the ghost's own points (yellow) and those of
/// its points that meet a piece's (green) — drawn through walls. P while building, or Interface → Show snap points.
/// </summary>
public partial class WorldLab
{
    private MultiMeshInstance3D? _snapDots;

    private static readonly Color DotTarget = new(.55f, .8f, 1f, .85f), DotGhost = new(1f, .85f, .3f, .95f), DotJoined = new(.35f, 1f, .45f, 1f);

    private void UpdateSnapMarkers(PieceDef? def)
    {
        bool show = _building && def != null && GameSettings.ShowSnapPoints.On && _ghostPose != null;
        if (!show) { if (_snapDots != null) _snapDots.Visible = false; return; }
        if (_snapDots == null)
        {
            var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, VertexColorUseAsAlbedo = true,
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha, NoDepthTest = true, RenderPriority = 10 };
            var mesh = new SphereMesh { Radius = .05f, Height = .1f, RadialSegments = 8, Rings = 4, Material = mat };
            _snapDots = new MultiMeshInstance3D { Name = "SnapDots", CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh } };
            AddChild(_snapDots);
        }
        var g = _ghostPose!.Value;
        var dots = new List<(V3 At, Color Colour)>();
        var targets = new List<V3>();
        foreach (var p in GameRules.PiecesNear(_catalog, _seenObjects, g.X, g.Z, 10))
            foreach (var q in Building.SnapPoints(p.Def, p.Pose)) targets.Add(q);
        var mine = Building.SnapPoints(def!, g).ToList();
        foreach (var t in targets)
            if (!mine.Any(m => m.DistanceTo(t) < Building.Joint)) dots.Add((t, DotTarget));
        foreach (var m in mine) dots.Add((m, targets.Any(t => t.DistanceTo(m) < Building.Joint) ? DotJoined : DotGhost));
        var mm = _snapDots.Multimesh;
        mm.InstanceCount = dots.Count;
        for (int i = 0; i < dots.Count; i++)
        {
            var (at, colour) = dots[i];
            float size = colour == DotTarget ? 1 : 1.5f;
            mm.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(Vector3.One * size), new Vector3((float)(at.X - _originX), (float)at.Y, (float)(at.Z - _originZ))));
            mm.SetInstanceColor(i, colour);
        }
        _snapDots.Visible = true;
    }
}
