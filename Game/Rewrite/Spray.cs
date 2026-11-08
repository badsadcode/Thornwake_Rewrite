using System;
using System.Collections.Generic;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Billowing spray and mist (D292; Spray.gdshader): particle emitters for the foot and face of waterfalls and for fast river
/// water breaking round stones. Setting *Spray and mist*: billowing spray, or the simple mist of build 138.
/// </summary>
public static class Spray
{
    private static ShaderMaterial? _material;

    public static bool On => GameSettings.Spray.Index == 1;

    // (drawn after the water and the falling water: they are see-through too, and drawn later they covered it)
    public static ShaderMaterial Material => _material ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/Spray.gdshader"), RenderPriority = 3 };

    /// <summary>
    /// An emitter: puffs born in a box (half-extents, local to the node), sent off along <paramref name="direction"/> (local),
    /// slowed by the air, lifted by <paramref name="rise"/> m/s², growing from their size to <paramref name="grow"/> times it,
    /// their density rising quickly to <paramref name="peak"/> and fading out over their life.
    /// </summary>
    public static GpuParticles3D Emitter(Vector3 box, Vector3 direction, float spreadDegrees, float speedMin, float speedMax, float sizeMin, float sizeMax,
        float grow, float lifetime, int amount, float rise, float peak, Color tint)
    {
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box,
            EmissionBoxExtents = box,
            Direction = direction.Normalized(),
            Spread = spreadDegrees,
            InitialVelocityMin = speedMin, InitialVelocityMax = speedMax,
            Gravity = new Vector3(0, rise, 0),
            DampingMin = .8f, DampingMax = 1.6f,
            ScaleMin = sizeMin, ScaleMax = sizeMax,
            AngleMin = 0, AngleMax = 360,
            AngularVelocityMin = -12, AngularVelocityMax = 12,
        };
        var fade = new Gradient();
        fade.SetColor(0, tint with { A = 0 }); fade.SetColor(1, tint with { A = 0 });
        fade.AddPoint(.12f, tint with { A = peak }); fade.AddPoint(.55f, tint with { A = peak * .6f });
        process.ColorRamp = new GradientTexture1D { Gradient = fade };
        var growth = new Curve(); growth.AddPoint(new Vector2(0, 1 / grow)); growth.AddPoint(new Vector2(1, 1));
        process.ScaleCurve = new CurveTexture { Curve = growth };
        float reach = Math.Max(box.Length() + speedMax * lifetime * .6f + sizeMax * 2, 4);
        return new GpuParticles3D
        {
            Name = "Spray",
            Amount = Math.Max(1, amount), Lifetime = lifetime, Preprocess = lifetime, Randomness = .5f,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = Material },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            DrawOrder = GpuParticles3D.DrawOrderEnum.ViewDepth,
            VisibilityAabb = new Aabb(-Vector3.One * reach, Vector3.One * reach * 2 + Vector3.Up * reach),
        };
    }

    /// <summary>Near white with a little of the water's green-blue (spray carries the water's colour faintly).</summary>
    public static readonly Color Tint = new(.9f, .95f, .96f);
}

/// <summary>
/// Spray where fast river water breaks round stones (D292): small billowing puffs on the upstream side of the stones standing
/// in rivers near the camera (WorldStreamer.NearRiverRocks), where the current runs over ~0.9 m/s, more and bigger the
/// faster it runs. A pool of emitters, reassigned every half second.
/// </summary>
public sealed class RiverSpray
{
    private const int Pool = 16;
    private readonly Node3D _parent;
    private readonly List<GpuParticles3D> _emitters = new();
    private readonly Vector4[] _near = new Vector4[32];
    private double _timer, _originX = double.NaN, _originZ;

    public RiverSpray(Node3D parent) { _parent = parent; }

    public void Update(double dt, TerrainSampler terrain, WorldStreamer streamer, double camX, double camZ, double originX, double originZ)
    {
        bool on = Spray.On && GameSettings.RiverWater.Index == 1;
        if (!on) { foreach (var e in _emitters) e.Visible = false; return; }
        _timer -= dt;
        if (_timer > 0 && originX == _originX && originZ == _originZ) return;
        _timer = .5; _originX = originX; _originZ = originZ;
        int n = streamer.NearRiverRocks(camX, camZ, _near, 90);
        int used = 0;
        for (int i = 0; i < n && used < Pool; i++)
        {
            var r = _near[i];
            double wx = r.X + originX, wz = r.Y + originZ;
            var current = Wakes.CurrentAt(terrain, wx, wz);
            float speed = current.Length();
            if (speed < .9f) continue;
            var t = terrain.Sample(wx, wz);
            if (double.IsNaN(t.WaterLevel)) continue;
            var dir = current / speed;
            float radius = r.Z, strength = Math.Clamp((speed - .9f) / 1.8f, 0, 1);
            if (used >= _emitters.Count)
            {
                var e = Spray.Emitter(new Vector3(.6f, .15f, .3f), new Vector3(0, 1, .8f), 35, .4f, 1.2f, .5f, 1f, 2.2f, 1.8f, 10, .15f, .5f, Spray.Tint);
                _parent.AddChild(e);
                _emitters.Add(e);
            }
            var em = _emitters[used++];
            // (the node's local z downstream, x across: the box lies against the stone's upstream face)
            var fwd = new Vector3(dir.X, 0, dir.Y);
            em.Basis = new Basis(Vector3.Up.Cross(fwd).Normalized(), Vector3.Up, fwd) * Basis.FromScale(Vector3.One * Math.Clamp(radius, .5f, 2.5f));
            em.Position = new Vector3(r.X - fwd.X * radius * .7f, (float)t.WaterLevel + .1f, r.Y - fwd.Z * radius * .7f);
            em.AmountRatio = .35f + .65f * strength;
            em.Visible = true;
            em.Emitting = true;
        }
        for (int i = used; i < _emitters.Count; i++) { _emitters[i].Emitting = false; _emitters[i].Visible = false; }
    }
}
