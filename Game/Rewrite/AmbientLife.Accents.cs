using System;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Living air accents (D195, from the ideas backlog: "floating ash and embers in volcanic regions", "occasional insects or
/// small insect swarms"): embers drifting up in volcanic land (day and night); fireflies blinking slowly on calm nights
/// in meadows, forests and swamps (not in winter); a midge swarm hovering in one spot on calm days by swamps and meadows.
/// One small particle system, reconfigured when the kind changes.
/// </summary>
public partial class AmbientLife
{
    private GpuParticles3D? _accent;
    private ParticleProcessMaterial? _accentPm;
    private StandardMaterial3D? _glowMat, _speckMat;
    private Accent _accentKind = (Accent)(-1);
    private Vector3 _swarmAt;
    private float _swarmTimer;

    private enum Accent { None, Embers, Fireflies, Midges }

    private static bool FireflyBiome(string b) => b is "temperate-grassland" or "temperate-forest" or "ancient-forest" or "swamp" or "marsh" or "fen" or "bayou" or "jungle";
    private static bool MidgeBiome(string b) => b is "swamp" or "blighted-swamp" or "marsh" or "fen" or "bayou" or "jungle" or "temperate-grassland";

    private void UpdateAccent(double delta, Vector2 windDir, float windSpeed, float daylight, float rain)
    {
        if (_accent == null) CreateAccent();
        bool on = GameSettings.AmbientLife.Index > 0 && rain < .1f;
        bool calm = windSpeed < 4.5f;
        bool notWinter = GameSettings.Season.Index != 3;
        var kind = !on ? Accent.None
            : _biome == "volcanic" ? Accent.Embers
            : daylight < .15f && calm && notWinter && FireflyBiome(_biome) ? Accent.Fireflies
            : daylight > .4f && calm && notWinter && MidgeBiome(_biome) ? Accent.Midges
            : Accent.None;
        if (kind != _accentKind) Configure(kind);
        _accent!.Emitting = kind != Accent.None;
        if (kind == Accent.None) return;
        _accent.AmountRatio = GameSettings.AmbientLife.Index == 2 ? 1 : .6f;
        var cam = GetParent<Node3D>().GlobalTransform;
        var dir = new Vector3(windDir.X, 0, windDir.Y).Normalized();
        switch (kind)
        {
            case Accent.Embers:
                _accentPm!.Gravity = new Vector3(0, .35f, 0) + dir * windSpeed * .25f;
                _accent.GlobalPosition = cam.Origin + new Vector3(0, -1.2f, 0) - dir * Math.Min(windSpeed, 6) * .6f;
                break;
            case Accent.Fireflies:
                _accent.GlobalPosition = cam.Origin + new Vector3(0, -.6f, 0);
                break;
            case Accent.Midges:
                // a swarm hovering in one spot a few metres off; another spot now and then (or when left behind)
                _swarmTimer -= (float)delta;
                if (_swarmTimer <= 0 || _swarmAt.DistanceTo(cam.Origin) > 25)
                {
                    var fwd = -cam.Basis.Z; fwd.Y = 0;
                    fwd = fwd.LengthSquared() > 1e-4f ? fwd.Normalized() : Vector3.Forward;
                    _swarmAt = cam.Origin + fwd.Rotated(Vector3.Up, (float)(_rng.NextDouble() - .5) * 1.6f) * (3.5f + 4 * (float)_rng.NextDouble());
                    if (GroundY != null) _swarmAt.Y = Math.Max(GroundY(_swarmAt.X, _swarmAt.Z), cam.Origin.Y - 4) + 1.3f + (float)_rng.NextDouble();
                    _swarmTimer = 20 + 25 * (float)_rng.NextDouble();
                }
                _accent.GlobalPosition = _swarmAt;
                break;
        }
    }

    private void CreateAccent()
    {
        _glowMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BlendMode = BaseMaterial3D.BlendModeEnum.Add, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true, AlbedoTexture = DotTexture(),
        };
        _speckMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true, AlbedoTexture = DotTexture(),
            AlbedoColor = new Color(.08f, .08f, .07f, .85f),
        };
        _accentPm = new ParticleProcessMaterial { TurbulenceEnabled = true, Spread = 180 };
        _accent = new GpuParticles3D
        {
            Amount = 120, Lifetime = 5, LocalCoords = false, Emitting = false, TopLevel = true,
            VisibilityAabb = new Aabb(new Vector3(-30, -10, -30), new Vector3(60, 30, 60)),
            DrawPass1 = new QuadMesh { Size = new Vector2(.05f, .05f), Material = _glowMat },
            ProcessMaterial = _accentPm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_accent);
    }

    private void Configure(Accent kind)
    {
        _accentKind = kind;
        var pm = _accentPm!;
        var mesh = (QuadMesh)_accent!.DrawPass1;
        switch (kind)
        {
            case Accent.Embers:
                _accent.Amount = 160; _accent.Lifetime = 6;
                pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box; pm.EmissionBoxExtents = new Vector3(10, 1, 10);
                pm.InitialVelocityMin = .2f; pm.InitialVelocityMax = .8f; pm.Direction = Vector3.Up;
                pm.TurbulenceNoiseStrength = 1.4f; pm.TurbulenceNoiseScale = 2.5f;
                pm.ColorRamp = Flicker(new Color(1f, .55f, .15f), new Color(1f, .25f, .05f), 6, false);
                mesh.Size = new Vector2(.09f, .09f); mesh.Material = _glowMat;
                break;
            case Accent.Fireflies:
                _accent.Amount = 70; _accent.Lifetime = 9;
                pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box; pm.EmissionBoxExtents = new Vector3(11, 1.2f, 11);
                pm.Gravity = Vector3.Zero; pm.InitialVelocityMin = .1f; pm.InitialVelocityMax = .4f; pm.Direction = Vector3.Up;
                pm.TurbulenceNoiseStrength = .8f; pm.TurbulenceNoiseScale = 4;
                pm.ColorRamp = Flicker(new Color(.75f, 1f, .3f), new Color(.75f, 1f, .3f), 3, true);
                mesh.Size = new Vector2(.13f, .13f); mesh.Material = _glowMat;
                break;
            case Accent.Midges:
                _accent.Amount = 140; _accent.Lifetime = 2.5;
                pm.EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere; pm.EmissionSphereRadius = .9f;
                pm.Gravity = Vector3.Zero; pm.InitialVelocityMin = .2f; pm.InitialVelocityMax = .6f; pm.Direction = Vector3.Up;
                pm.TurbulenceNoiseStrength = 3; pm.TurbulenceNoiseScale = .6f;
                pm.ColorRamp = Ramp(new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0));
                mesh.Size = new Vector2(.04f, .04f); mesh.Material = _speckMat;
                _swarmTimer = 0;
                break;
        }
    }

    /// <summary>A colour over a particle's life that flickers (embers) or blinks slowly on and off (fireflies).</summary>
    private static GradientTexture1D Flicker(Color a, Color b, int pulses, bool blink)
    {
        var g = new Gradient();
        g.SetColor(0, new Color(a.R, a.G, a.B, 0));
        g.SetColor(1, new Color(b.R, b.G, b.B, 0));
        for (int i = 0; i < pulses; i++)
        {
            float t0 = (i + .2f) / pulses, t1 = (i + .45f) / pulses, t2 = (i + .75f) / pulses;
            g.AddPoint(t0, new Color(a.R, a.G, a.B, blink ? 0f : .6f));
            g.AddPoint(t1, new Color(a.R, a.G, a.B, 1));
            g.AddPoint(t2, new Color(b.R, b.G, b.B, blink ? 0f : .5f));
        }
        return new GradientTexture1D { Gradient = g };
    }
}
