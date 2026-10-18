using System;
using System.Collections.Generic;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Tracking (Tom, build 67: "scan the nearby area by pressing a key, like Soulmask: highlight interactable things, show
/// animal tracks that stay; a foundation for a tracking skill"; D132), and blood where an animal is hit.
/// * Every animal this PC sees leaves **tracks** as it goes (a pair of prints every 1.2 m, kept 5 minutes): hidden until
///   you scan.
/// * **Z scans**: a ring sweeps out from Alden to <see cref="ScanRadius"/>; what it passes is marked for
///   <see cref="ScanSeconds"/> (fading at the end) — loose stones, berry bushes, arrows, carcasses, piles and packs,
///   blueprints, animals (with their distance) — and the tracks around light up. Then a short rest before the next scan.
///   The radius and how long tracks last are where a tracking skill will grow.
/// * A hit animal **bleeds**: a burst of red drops wherever its health falls (every PC sees it: the health comes with the
///   animals' positions).
/// </summary>
public partial class WorldLab
{
    public const double ScanRadius = 45, ScanSeconds = 20, ScanRest = 6, TrackSeconds = 300;
    private readonly List<(double X, double Y, double Z, float Yaw, string Kind, double T)> _tracks = new();
    private readonly Dictionary<int, (double X, double Z)> _trackLast = new();
    private double _scanAt = -1e9;
    private Node3D? _scanRoot;
    private MeshInstance3D? _scanRing;
    private MultiMeshInstance3D? _trackPrints;
    private readonly List<(Label3D Label, double Distance)> _scanMarks = new();
    private StandardMaterial3D? _printMat;
    private bool _crouch, _winded;
    private float _stamina = 100, _shoulderSide = 1, _shoulderSideSmooth = 1;

    /// <summary>Per frame: tracks left by the animals seen, the scan's ring and marks.</summary>
    private void UpdateTracking()
    {
        double now = Now;
        foreach (var v in _animalViews.Values)
        {
            if (v.State == AnimalState.Dead) continue;
            if (!_trackLast.TryGetValue(v.Id, out var last)) { _trackLast[v.Id] = (v.X, v.Z); continue; }
            double dx = v.X - last.X, dz = v.Z - last.Z;
            if (dx * dx + dz * dz < 1.2 * 1.2) continue;
            _trackLast[v.Id] = (v.X, v.Z);
            _tracks.Add((v.X, v.Y, v.Z, (float)Math.Atan2(dx, dz), v.Def.Id, now));
        }
        if (_tracks.Count > 0 && now - _tracks[0].T > TrackSeconds) _tracks.RemoveAll(t => now - t.T > TrackSeconds);
        if (_tracks.Count > 4000) _tracks.RemoveRange(0, _tracks.Count - 4000);
        UpdateScan(now);
    }

    private void Scan()
    {
        if (_sampler == null) return;
        double now = Now;
        if (now - _scanAt < ScanRest) { _status = $"Catching your breath before looking around again ({ScanRest - (now - _scanAt):0} s)."; return; }
        _scanAt = now;
        if (_scanRoot == null) { _scanRoot = new Node3D { Name = "Scan" }; AddChild(_scanRoot); }
        foreach (var (l, _) in _scanMarks) l.QueueFree();
        _scanMarks.Clear();
        var marks = new List<(double X, double Y, double Z, string Text, Color Colour)>();
        // (the 30 nearest stones and bushes; named within 15 m, a mark only farther out: less clutter)
        foreach (var g in _streamer.GatherablesNear(PlayerX, PlayerZ, ScanRadius).Take(30))
        {
            bool close = (g.X - PlayerX) * (g.X - PlayerX) + (g.Z - PlayerZ) * (g.Z - PlayerZ) < 15 * 15;
            marks.Add((g.X, g.Y + .35, g.Z, close ? g.Layer == ObjectRemovals.Stones ? "stone" : "berries" : "", g.Layer == ObjectRemovals.Stones ? new Color(.85f, .85f, .8f) : new Color(.9f, .4f, .6f)));
        }
        foreach (var o in _seenObjects.Near(PlayerX, PlayerZ, ScanRadius))
        {
            if (o.Kind == "carcass") marks.Add((o.X, o.Y + .8, o.Z, $"{(_catalog.Animals.TryGetValue(o.Ref, out var d) ? d.Name.ToLowerInvariant() : "carcass")} (butcher)", new Color(1, .45f, .35f)));
            else if (o.Blueprint && _catalog.Piece(o.Kind) is { } p) marks.Add((o.X, o.Y + 1.2, o.Z, $"{p.Name.ToLowerInvariant()} (needs materials)", new Color(.55f, .8f, 1f)));
            else if (o.Contents.Count > 0) marks.Add((o.X, o.Y + .4, o.Z, string.Join(", ", o.Contents.Select(c => _catalog.Item(c.Item).Name.ToLowerInvariant())), new Color(1, .9f, .5f)));
        }
        foreach (var v in _animalViews.Values.Where(v => v.State != AnimalState.Dead))
        {
            double d = Math.Sqrt((v.X - PlayerX) * (v.X - PlayerX) + (v.Z - PlayerZ) * (v.Z - PlayerZ));
            if (d <= ScanRadius * 1.6) marks.Add((v.X, v.Y + v.Def.Height + .4, v.Z, $"{v.Def.Name.ToLowerInvariant()} · {d:0} m", new Color(1, .7f, .3f)));
        }
        foreach (var (x, y, z, text, colour) in marks)
        {
            var label = new Label3D { Text = text.Length > 0 ? "◆ " + text : "◆", Billboard = BaseMaterial3D.BillboardModeEnum.Enabled, NoDepthTest = true, FixedSize = true,
                PixelSize = .0009f, FontSize = 22, OutlineSize = 6, Modulate = colour with { A = 0 }, RenderPriority = 5 };
            label.SetMeta("at", new Vector3((float)x, (float)y, (float)z));
            _scanRoot.AddChild(label);
            _scanMarks.Add((label, Math.Sqrt((x - PlayerX) * (x - PlayerX) + (z - PlayerZ) * (z - PlayerZ))));
        }
        int prints = _tracks.Count(t => (t.X - PlayerX) * (t.X - PlayerX) + (t.Z - PlayerZ) * (t.Z - PlayerZ) <= ScanRadius * ScanRadius);
        _status = $"You look around: {marks.Count} things of note{(prints > 0 ? $", {prints} tracks" : ", no tracks")}.";
    }

    private void UpdateScan(double now)
    {
        double age = now - _scanAt;
        bool on = age < ScanSeconds;
        if (_scanRoot == null) return;
        _scanRoot.Visible = on;
        if (!on) return;
        // The ring: out to the radius in a second, fading.
        float reach = (float)(ScanRadius * Math.Min(1, age / 1.0));
        if (_scanRing == null)
        {
            var mat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(.6f, .9f, 1f, .5f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            _scanRing = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = .97f, OuterRadius = 1f, Rings = 64, RingSegments = 4 }, MaterialOverride = mat,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
            _scanRoot.AddChild(_scanRing);
        }
        _scanRing.Visible = age < 1.2;
        _scanRing.Position = _player.Position + Vector3.Up * .3f;
        _scanRing.Scale = new Vector3(Math.Max(.1f, reach), 1 + reach * .04f, Math.Max(.1f, reach));
        ((StandardMaterial3D)_scanRing.MaterialOverride).AlbedoColor = new Color(.6f, .9f, 1f, .5f * (1 - (float)Math.Min(1, age / 1.2)));
        float fade = (float)Math.Clamp((ScanSeconds - age) / 5, 0, 1);
        foreach (var (label, dist) in _scanMarks)
        {
            var at = label.GetMeta("at").AsVector3();
            label.Position = new Vector3((float)(at.X - _originX), at.Y, (float)(at.Z - _originZ));
            float shown = dist <= reach ? fade : 0; // (marked as the ring reaches it)
            label.Modulate = label.Modulate with { A = shown };
        }
        // The tracks the ring has passed: hoof and paw prints, a pair every 1.2 m along the way the animal went.
        if (_trackPrints == null)
        {
            _printMat = new StandardMaterial3D { ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(1f, .75f, .35f, .85f), CullMode = BaseMaterial3D.CullModeEnum.Disabled };
            _trackPrints = new MultiMeshInstance3D { CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = new CylinderMesh { TopRadius = .06f, BottomRadius = .06f, Height = .02f, RadialSegments = 8, Material = _printMat },
            } };
            _scanRoot.AddChild(_trackPrints);
        }
        _printMat!.AlbedoColor = new Color(1f, .75f, .35f, .85f * fade);
        var near = _tracks.Where(t => Math.Sqrt((t.X - PlayerX) * (t.X - PlayerX) + (t.Z - PlayerZ) * (t.Z - PlayerZ)) <= reach).ToList();
        var mm = _trackPrints.Multimesh;
        mm.InstanceCount = near.Count * 2;
        for (int i = 0; i < near.Count; i++)
        {
            var t = near[i];
            float small = t.Kind is "fox" ? .6f : t.Kind is "wolf" ? .8f : 1;
            var side = new Vector3(Mathf.Cos(t.Yaw), 0, -Mathf.Sin(t.Yaw)) * .13f * small;
            var basis = new Basis(Vector3.Up, t.Yaw).Scaled(new Vector3(small, 1, 1.5f * small)); // (longer along the way it went)
            for (int k = 0; k < 2; k++)
            {
                var p = new Vector3((float)(t.X - _originX), (float)_sampler!.Height(t.X, t.Z) + .03f, (float)(t.Z - _originZ)) + (k == 0 ? side : -side)
                    + new Vector3(Mathf.Sin(t.Yaw), 0, Mathf.Cos(t.Yaw)) * (k == 0 ? .2f : -.2f) * small;
                mm.SetInstanceTransform(i * 2 + k, new Transform3D(basis, p));
            }
        }
    }

    /// <summary>A burst of red drops at an animal that was just hurt.</summary>
    private void Blood(AnimalView v)
    {
        var drops = new CpuParticles3D
        {
            Amount = 36, Lifetime = .9f, OneShot = true, Explosiveness = .95f, Emitting = true,
            Direction = Vector3.Up, Spread = 70, InitialVelocityMin = 1.5f, InitialVelocityMax = 4f, Gravity = new Vector3(0, -9.8f, 0),
            ScaleAmountMin = .5f, ScaleAmountMax = 1.4f,
            Mesh = new SphereMesh { Radius = .025f, Height = .05f, RadialSegments = 6, Rings = 3,
                Material = new StandardMaterial3D { AlbedoColor = new Color(.45f, .02f, .02f), Roughness = .3f, Metallic = .1f } },
            Position = v.Position + Vector3.Up * (float)(v.Def.Height * .55),
        };
        AddChild(drops);
        GetTree().CreateTimer(2).Timeout += () => drops.QueueFree();
    }

    private void TrackingWorldReady()
    {
        _tracks.Clear(); _trackLast.Clear(); _scanAt = -1e9;
        foreach (var (l, _) in _scanMarks) l.QueueFree();
        _scanMarks.Clear();
    }
}
