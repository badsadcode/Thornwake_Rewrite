using System;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Alden's torch (round 32, Tom: "nights are looking really nice … can we give Alden a torch? Nice and flickering on the
/// wind, with a little bit of smoke"). A wooden stick with a wrapped head; a flame of glowing particles that streams
/// downwind and trails behind when walking (particles live in the world); a little grey smoke rising and drifting with
/// the wind; a warm light that flickers in brightness and position, so its shadows tremble. Held in the right hand
/// (third person) or at the lower right of the view (first person). R lights and puts it out.
/// </summary>
public sealed partial class Torch : Node3D
{
    private readonly OmniLight3D _light;
    private readonly CpuParticles3D _flame, _smoke;
    private readonly FastNoiseLite _noise = new() { Frequency = 1, Seed = 9 };
    private readonly Vector3 _lightHome = new(0, .66f, 0);
    // The smoke sits on its own render layer, which the torch's light skips: lit from a few cm away it burned to a white
    // ball (first render); it takes the moon and the sky instead.
    private const uint SmokeLayer = 1u << 19;
    private Wind? _wind;
    private double _t;

    public Torch()
    {
        Name = "Torch";
        var wood = new StandardMaterial3D { AlbedoColor = new Color("5a3d25"), Roughness = .9f };
        var wrap = new StandardMaterial3D { AlbedoColor = new Color("2b231c"), Roughness = 1, EmissionEnabled = true, Emission = new Color("ff6a1e"), EmissionEnergyMultiplier = .35f };
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .022f, BottomRadius = .018f, Height = .62f, RadialSegments = 6 }, MaterialOverride = wood, Position = new Vector3(0, .25f, 0) });
        AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .042f, BottomRadius = .032f, Height = .13f, RadialSegments = 7 }, MaterialOverride = wrap, Position = new Vector3(0, .56f, 0) });

        // Flame: additive glowing quads, yellow-white at the root to deep orange and gone within half a second.
        var flameRamp = new Gradient();
        flameRamp.SetColor(0, new Color(1f, .85f, .45f, .5f));
        flameRamp.AddPoint(.3f, new Color(1f, .45f, .1f, .38f));
        flameRamp.SetColor(1, new Color(.5f, .1f, .02f, 0f));
        _flame = new CpuParticles3D
        {
            Name = "Flame", Amount = 72, Lifetime = .38, Randomness = .5f, LocalCoords = false, Position = new Vector3(0, .6f, 0),
            Mesh = new QuadMesh { Size = Vector2.One, Material = Billboard(additive: true, shaded: false) },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = .035f,
            Direction = Vector3.Up, Spread = 10, InitialVelocityMin = .2f, InitialVelocityMax = .45f, DampingMin = .5f, DampingMax = 1.5f,
            ScaleAmountMin = .05f, ScaleAmountMax = .09f, ScaleAmountCurve = Curve(1, .8f, .35f), ColorRamp = flameRamp,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_flame);

        // Smoke: lit grey puffs, growing and thinning as they rise and drift away with the wind.
        var smokeRamp = new Gradient();
        smokeRamp.SetColor(0, new Color(.3f, .16f, .07f, 0f));
        smokeRamp.AddPoint(.12f, new Color(.16f, .12f, .1f, .16f));
        smokeRamp.AddPoint(.5f, new Color(.09f, .09f, .09f, .1f));
        smokeRamp.SetColor(1, new Color(.08f, .08f, .08f, 0f));
        _smoke = new CpuParticles3D
        {
            Name = "Smoke", Amount = 26, Lifetime = 3.2, Randomness = .5f, LocalCoords = false, Position = new Vector3(0, .78f, 0),
            // (unshaded: warm just above the flame, then dim grey — shaded, the torch's light burned it to white balls)
            Mesh = new QuadMesh { Size = Vector2.One, Material = Billboard(additive: false, shaded: false) },
            EmissionShape = CpuParticles3D.EmissionShapeEnum.Sphere, EmissionSphereRadius = .05f,
            Direction = Vector3.Up, Spread = 18, InitialVelocityMin = .25f, InitialVelocityMax = .55f, DampingMin = .2f, DampingMax = .5f,
            ScaleAmountMin = .16f, ScaleAmountMax = .28f, ScaleAmountCurve = Curve(.35f, 1.2f, 2.6f), ColorRamp = smokeRamp,
            AngleMin = 0, AngleMax = 360, AngularVelocityMin = -25, AngularVelocityMax = 25,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = SmokeLayer,
        };
        AddChild(_smoke);

        _light = new OmniLight3D
        {
            Name = "Light", Position = _lightHome, LightColor = new Color(1f, .62f, .3f), LightEnergy = 2.2f, OmniRange = 16,
            OmniAttenuation = 1.3f, ShadowEnabled = true, LightSize = .06f, LightSpecular = .3f, LightCullMask = ~SmokeLayer,
        };
        AddChild(_light);
    }

    /// <summary>The wind the flame and smoke follow (Atmosphere.Wind).</summary>
    public void Follow(Wind wind) => _wind = wind;

    /// <summary>Lit (visible, burning) or out.</summary>
    public bool Lit
    {
        get => Visible;
        set { Visible = value; _flame.Emitting = value; _smoke.Emitting = value; }
    }

    public override void _Process(double delta)
    {
        if (!Visible) return;
        _t += delta;
        float t = (float)_t;
        // Flicker: a slow breathing and quick licks of the flame; the light moves a few cm with them (trembling shadows).
        float f = .82f + .12f * _noise.GetNoise1D(t * 2.2f) + .08f * _noise.GetNoise1D(t * 9 + 40) + .05f * _noise.GetNoise1D(t * 23 + 80);
        _light.LightEnergy = 2.2f * f;
        _light.Position = _lightHome + new Vector3(_noise.GetNoise1D(t * 7 + 10), _noise.GetNoise1D(t * 6 + 20) * .6f, _noise.GetNoise1D(t * 7 + 30)) * .04f;
        // The flame leans and streams downwind, the smoke drifts off with it (gusts included).
        var w = _wind is { } wind ? new Vector3(wind.Direction.X, 0, wind.Direction.Y) * Mathf.Min(wind.Speed, 18) : Vector3.Zero;
        _flame.Gravity = Vector3.Up * 1.2f + w * .2f;
        _smoke.Gravity = Vector3.Up * .35f + w * .16f;
    }

    private static Curve Curve(float a, float b, float c)
    {
        var curve = new Curve { MaxValue = 3 };
        curve.AddPoint(new Vector2(0, a)); curve.AddPoint(new Vector2(.5f, b)); curve.AddPoint(new Vector2(1, c));
        return curve;
    }

    /// <summary>A soft round particle: additive and unlit for the flame, alpha-blended and lit for the smoke.</summary>
    private static StandardMaterial3D Billboard(bool additive, bool shaded)
    {
        var g = new Gradient();
        g.SetColor(0, Colors.White);
        g.SetColor(1, new Color(1, 1, 1, 0));
        var tex = new GradientTexture2D { Gradient = g, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(.5f, 0), Width = 64, Height = 64 };
        return new StandardMaterial3D
        {
            AlbedoColor = additive ? new Color(1.5f, 1.5f, 1.5f) : Colors.White, // (the flame a little brighter than white: it glows)
            AlbedoTexture = tex, VertexColorUseAsAlbedo = true, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            BillboardKeepScale = true, // (without it every particle is drawn 1 m wide: a white ball)
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BlendMode = additive ? BaseMaterial3D.BlendModeEnum.Add : BaseMaterial3D.BlendModeEnum.Mix,
            ShadingMode = shaded ? BaseMaterial3D.ShadingModeEnum.PerPixel : BaseMaterial3D.ShadingModeEnum.Unshaded,
            DepthDrawMode = BaseMaterial3D.DepthDrawModeEnum.Disabled, Roughness = 1,
        };
    }
}
