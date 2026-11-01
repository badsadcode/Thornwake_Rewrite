using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// The gas plume over an active volcano's lava lake (D258): puffs of Tom's smoke texture (as SmokePlume's), 60–200 m
/// across, rising slowly for about a minute to a few hundred metres and bending off with the wind, white-grey and thinning
/// as they go; lit by the scene, its base glowing orange from the lava below (VolcanoSmoke.gdshader). World-space GPU
/// particles with a visibility box of their own (CPU particles reported an empty box and were culled). Setting
/// <c>fx.volcano_smoke</c>.
/// </summary>
public sealed partial class VolcanoSmoke : GpuParticles3D
{
    private const string TexturePath = "res://Assets/Effects/smoke_puff.png";
    private readonly ParticleProcessMaterial _process;

    /// <summary>The wind it drifts with (Atmosphere.Wind).</summary>
    public Wind? Wind { get; set; }

    public VolcanoSmoke()
    {
        Name = "VolcanoSmoke";
        LocalCoords = false; Randomness = .6f;
        Amount = 150; Lifetime = 55; Preprocess = 55; // (already risen when the player arrives)
        VisibilityAabb = new Aabb(new Vector3(-900, -150, -900), new Vector3(1800, 1300, 1800));
        var scale = new Curve { MaxValue = 3 };
        scale.AddPoint(new Vector2(0, .6f)); scale.AddPoint(new Vector2(.4f, 1.2f)); scale.AddPoint(new Vector2(1, 1.8f));
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1f, .78f, .6f, 0f));
        ramp.AddPoint(.05f, new Color(.95f, .88f, .82f, .85f)); // (warm where it leaves the lava)
        ramp.AddPoint(.4f, new Color(.9f, .9f, .9f, .7f));
        ramp.SetColor(1, new Color(.88f, .88f, .9f, 0f));
        _process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, EmissionSphereRadius = 90,
            Direction = Vector3.Up, Spread = 15, InitialVelocityMin = 7, InitialVelocityMax = 11, DampingMin = .05f, DampingMax = .1f,
            AngleMin = 0, AngleMax = 360, AngularVelocityMin = -2, AngularVelocityMax = 2,
            ScaleMin = 60, ScaleMax = 110, ScaleCurve = new CurveTexture { Curve = scale }, // (40–65 read as a wisp from 3 km)
            ColorRamp = new GradientTexture1D { Gradient = ramp }, Gravity = Vector3.Up * .05f,
        };
        ProcessMaterial = _process;
        var material = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/VolcanoSmoke.gdshader") };
        material.SetShaderParameter("puff", GD.Load<Texture2D>(TexturePath));
        DrawPass1 = new QuadMesh { Size = Vector2.One, Material = material };
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        Emitting = false;
    }

    public override void _Process(double delta)
    {
        if (!Emitting) return;
        // Up, slowing as it rises, and off with the wind (gusts included).
        var w = Wind is { } wind ? new Vector3(wind.Direction.X, 0, wind.Direction.Y) * Mathf.Min(wind.Speed, 18) : Vector3.Zero;
        _process.Gravity = Vector3.Up * .05f + w * .06f;
    }
}
