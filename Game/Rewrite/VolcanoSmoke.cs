using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// The column of ash and gas over an active volcano's lava lake (D258, D262): puffs of Tom's smoke texture (as
/// SmokePlume's), 70–680 m across, billowing up from the crater for over a minute to about a kilometre and bending off
/// with the wind; ash brown and grey with white steam among it, each puff its own shade, sunlit on top and dark underneath
/// (VolcanoSmoke.gdshader), fading in as it leaves the crater and away before it ends; its underside glowing red from the
/// lava at night. GPU particles in the node's own space, with a visibility box of their own (CPU particles reported an
/// empty box and were culled). Setting <c>fx.volcano_smoke</c>; "on (build 126)" is the earlier thin, white-grey plume.
/// </summary>
public sealed partial class VolcanoSmoke : GpuParticles3D
{
    private const string TexturePath = "res://Assets/Effects/smoke_puff.png";
    private const string ShaderPath = "res://Game/Rewrite/VolcanoSmoke.gdshader", Build126Path = "res://Game/Rewrite/VolcanoSmokeBuild126.gdshader";
    private readonly ParticleProcessMaterial _process;
    private readonly ShaderMaterial _material;
    private readonly CurveTexture _scale = Grow(.45f, 1.4f, 2.6f), _scale126 = Grow(.6f, 1.2f, 1.8f);
    private bool _build126 = true;

    /// <summary>Build 126's plume (setting <c>fx.volcano_smoke</c> → "on (build 126)", D262): fewer, smaller puffs, whole to
    /// their end.</summary>
    public bool Build126
    {
        get => _build126;
        set
        {
            if (value == _build126) return;
            _build126 = value;
            _material.Shader = GD.Load<Shader>(value ? Build126Path : ShaderPath);
            // (D262, Tom: "bigger, denser, and have more color variance"; his pictures: ash columns billowing up)
            Amount = value ? 150 : 260;
            Lifetime = Preprocess = value ? 55 : 75; // (already risen when the player arrives)
            _process.EmissionSphereRadius = value ? 90 : 150;
            _process.Spread = value ? 15 : 22;
            _process.InitialVelocityMin = value ? 7 : 14; _process.InitialVelocityMax = value ? 11 : 22;
            _process.DampingMin = value ? .05f : .1f; _process.DampingMax = value ? .1f : .16f;
            _process.ScaleMin = value ? 60 : 150; _process.ScaleMax = value ? 110 : 260; // (40–65 read as a wisp from 3 km)
            _process.ScaleCurve = value ? _scale126 : _scale;
            if (Emitting) Restart();
        }
    }

    /// <summary>The wind it drifts with (Atmosphere.Wind).</summary>
    public Wind? Wind { get; set; }

    public VolcanoSmoke()
    {
        Name = "VolcanoSmoke";
        // (local: the node is moved over the crater when the scene's origin shifts, and its puffs move with it — in world
        // space they stayed at their old scene positions, the column jumping 1.5 km off the volcano at each shift, D264)
        LocalCoords = true; Randomness = .6f;
        VisibilityAabb = new Aabb(new Vector3(-1500, -150, -1500), new Vector3(3000, 2000, 3000));
        _process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere, Direction = Vector3.Up, Spread = 15,
            AngleMin = 0, AngleMax = 360, AngularVelocityMin = -2, AngularVelocityMax = 2, Gravity = Vector3.Up * .05f,
            ColorRamp = new GradientTexture1D { Gradient = Ramp126() },
        };
        ProcessMaterial = _process;
        _material = new ShaderMaterial();
        _material.SetShaderParameter("puff", GD.Load<Texture2D>(TexturePath));
        DrawPass1 = new QuadMesh { Size = Vector2.One, Material = _material };
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
        Build126 = false;
        Emitting = false;
    }

    /// <summary>Build 126's colour ramp, kept for its shader (it does not reach a shader's COLOR here: the current one draws
    /// its own from the age, D262).</summary>
    private static Gradient Ramp126()
    {
        var ramp = new Gradient();
        ramp.SetColor(0, new Color(1f, .78f, .6f, 0f));
        ramp.AddPoint(.05f, new Color(.95f, .88f, .82f, .85f));
        ramp.AddPoint(.4f, new Color(.9f, .9f, .9f, .7f));
        ramp.SetColor(1, new Color(.88f, .88f, .9f, 0f));
        return ramp;
    }

    private static CurveTexture Grow(float start, float middle, float end)
    {
        var curve = new Curve { MaxValue = 3 };
        curve.AddPoint(new Vector2(0, start)); curve.AddPoint(new Vector2(.4f, middle)); curve.AddPoint(new Vector2(1, end));
        return new CurveTexture { Curve = curve };
    }

    public override void _Process(double delta)
    {
        if (!Emitting) return;
        // Up, slowing as it rises, and off with the wind (gusts included).
        var w = Wind is { } wind ? new Vector3(wind.Direction.X, 0, wind.Direction.Y) * Mathf.Min(wind.Speed, 18) : Vector3.Zero;
        _process.Gravity = Vector3.Up * .05f + w * .06f;
    }
}
