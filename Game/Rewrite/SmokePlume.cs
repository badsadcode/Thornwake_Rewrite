using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Smoke rising from a torch or a campfire, drifting off with the wind. Setting *Smoke* (<c>fx.smoke</c>): build 72's
/// soft round dots (the torch only; campfires had no smoke), or puffs of Tom's smoke texture
/// (<c>Assets/Effects/smoke_puff.png</c>), each turned its own way and slowly spinning, so the one picture never repeats
/// visibly. The particles live in the world (they stay behind when the torch moves) and sit on their own render layer,
/// which the torch's light skips (lit from a few cm away they burned to white, D98).
/// </summary>
public sealed partial class SmokePlume : CpuParticles3D
{
    public enum Kind { Torch, Campfire }
    public const uint SmokeLayer = 1u << 19;
    private const string TexturePath = "res://Assets/Effects/smoke_puff.png";

    private readonly Kind _kind;
    /// <summary>The wind it drifts with (Atmosphere.Wind).</summary>
    public Wind? Wind { get; set; }
    private StandardMaterial3D _material = null!;
    private int _look = -1;
    private bool? _shaded;
    private bool _burning;

    public SmokePlume(Kind kind, Wind? wind)
    {
        _kind = kind; Wind = wind;
        Name = kind == Kind.Torch ? "Smoke" : "FlameSmoke"; // ("Flame…": shown only while the fire burns, like its flames)
        LocalCoords = false; Randomness = .5f;
        EmissionShape = EmissionShapeEnum.Sphere; Direction = Vector3.Up;
        AngleMin = 0; AngleMax = 360;
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off; Layers = SmokeLayer;
        Apply(GameSettings.SmokeLook.Index);
        Emitting = false; // (until it burns)
    }

    /// <summary>Burning: smoke comes (a torch lit, a campfire with fuel and finished).</summary>
    public bool Burning
    {
        get => _burning;
        set { _burning = value; if (IsInsideTree()) Emitting = value && Shows; } // (Godot warns when set before)
    }

    public override void _EnterTree() => Emitting = _burning && Shows;

    /// <summary>Build 72's dots: lit by the scene's lights (moon, sky, sun), or unshaded (as in build 72: white at night).
    /// The textured smoke is always lit.</summary>
    public bool Shaded
    {
        set { _wantShaded = value; UpdateShading(); }
    }
    private bool _wantShaded;

    private void UpdateShading()
    {
        bool shaded = _look == 1 || _wantShaded || _kind == Kind.Campfire;
        if (_shaded == shaded) return;
        _shaded = shaded;
        _material.ShadingMode = shaded ? BaseMaterial3D.ShadingModeEnum.PerPixel : BaseMaterial3D.ShadingModeEnum.Unshaded;
    }

    private bool Shows => _kind == Kind.Torch || _look == 1;

    public override void _Process(double delta)
    {
        if (GameSettings.SmokeLook.Index != _look) { Apply(GameSettings.SmokeLook.Index); Emitting = _burning && Shows; }
        if (!Emitting) return;
        // Up, and off with the wind (gusts included); a campfire's plume rises higher before it bends.
        var w = Wind is { } wind ? new Vector3(wind.Direction.X, 0, wind.Direction.Y) * Mathf.Min(wind.Speed, 18) : Vector3.Zero;
        Gravity = _kind == Kind.Torch ? Vector3.Up * .35f + w * .16f : Vector3.Up * .45f + w * .12f;
    }

    private void Apply(int look)
    {
        _look = look;
        bool textured = look == 1;
        var ramp = new Gradient();
        if (!textured)
        {
            // Build 72's torch smoke, unchanged.
            ramp.SetColor(0, new Color(.3f, .16f, .07f, 0f));
            ramp.AddPoint(.12f, new Color(.16f, .12f, .1f, .16f));
            ramp.AddPoint(.5f, new Color(.09f, .09f, .09f, .1f));
            ramp.SetColor(1, new Color(.08f, .08f, .08f, 0f));
            Amount = 26; Lifetime = 3.2; Position = new Vector3(0, .78f, 0); EmissionSphereRadius = .05f;
            Spread = 18; InitialVelocityMin = .25f; InitialVelocityMax = .55f; DampingMin = .2f; DampingMax = .5f;
            ScaleAmountMin = .16f; ScaleAmountMax = .28f; ScaleAmountCurve = Curve(.35f, 1.2f, 2.6f);
            AngularVelocityMin = -25; AngularVelocityMax = 25;
        }
        else
        {
            // The texture's grey carries the shape; lit by the scene, so the ramp is near white: a warm breath over the
            // flame, then pale grey thinning out (grey × grey under daylight read nearly black).
            if (_kind == Kind.Torch)
            {
                // (Tom: it started too far off the torch — the first puffs were clear and tiny, seen only once the wind
                // had carried them off; now visible from the flame's tip, denser, more of them)
                ramp.SetColor(0, new Color(1f, .82f, .62f, .32f));
                ramp.AddPoint(.05f, new Color(1f, .95f, .9f, .45f));
                ramp.AddPoint(.5f, new Color(.95f, .95f, .95f, .24f));
                ramp.SetColor(1, new Color(.95f, .95f, .95f, 0f));
                Amount = 36; Lifetime = 2.6; Position = new Vector3(0, .68f, 0); EmissionSphereRadius = .03f;
                Spread = 12; InitialVelocityMin = .25f; InitialVelocityMax = .5f; DampingMin = .2f; DampingMax = .5f;
                ScaleAmountMin = .18f; ScaleAmountMax = .28f; ScaleAmountCurve = Curve(.8f, 1.4f, 2.4f);
            }
            else
            {
                ramp.SetColor(0, new Color(1f, .82f, .62f, 0f));
                ramp.AddPoint(.06f, new Color(1f, .96f, .92f, .55f));
                ramp.AddPoint(.45f, new Color(.95f, .95f, .95f, .3f));
                ramp.SetColor(1, new Color(.95f, .95f, .95f, 0f));
                Amount = 32; Lifetime = 6.5; Position = new Vector3(0, .6f, 0); EmissionSphereRadius = .2f;
                Spread = 10; InitialVelocityMin = .5f; InitialVelocityMax = .85f; DampingMin = .1f; DampingMax = .25f;
                ScaleAmountMin = .45f; ScaleAmountMax = .7f; ScaleAmountCurve = Curve(.45f, 1.7f, 3f);
            }
            AngularVelocityMin = -14; AngularVelocityMax = 14;
        }
        ColorRamp = ramp;
        _material = Material(textured);
        _shaded = null;
        Mesh = new QuadMesh { Size = Vector2.One, Material = _material };
        UpdateShading();
    }

    private static Curve Curve(float a, float b, float c)
    {
        var curve = new Curve { MaxValue = 3 };
        curve.AddPoint(new Vector2(0, a)); curve.AddPoint(new Vector2(.5f, b)); curve.AddPoint(new Vector2(1, c));
        return curve;
    }

    private static StandardMaterial3D Material(bool textured)
    {
        Texture2D tex;
        if (textured) tex = GD.Load<Texture2D>(TexturePath);
        else
        {
            var g = new Gradient();
            g.SetColor(0, Colors.White);
            g.SetColor(1, new Color(1, 1, 1, 0));
            tex = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(.5f, 0), Width = 64, Height = 64 };
        }
        return new StandardMaterial3D
        {
            AlbedoColor = Colors.White, AlbedoTexture = tex, VertexColorUseAsAlbedo = true,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true, // (without it every particle is drawn 1 m wide: a white ball)
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = BaseMaterial3D.BlendModeEnum.Mix,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled, Roughness = 1,
            // (no backlight: with it the scan's sonar wave stopped drawing while a torch smoked, D141)
            DiffuseMode = BaseMaterial3D.DiffuseModeEnum.Lambert,
        };
    }
}
