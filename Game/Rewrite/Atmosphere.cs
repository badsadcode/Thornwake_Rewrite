using System.Linq;
using System;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Sky, sun, clouds, cloud shadows, sun shafts and the post-processing stack of the rewrite world, all driven by
/// <see cref="GameSettings"/> (applied live when a setting changes).
/// <list type="bullet">
/// <item>Clouds: a tileable noise texture on a plane above the world, drawn by <c>Sky.gdshader</c>.</item>
/// <item>Cloud shadows: a huge decal around the camera carrying the same texture (3×3 tiles, so it can jump by one
/// tile without a visible change), offset along the sun direction so each shadow lies under its cloud.</item>
/// <item>Sun shafts: a full-screen additive pass (<c>SunShafts.gdshader</c>).</item>
/// <item>Ambient occlusion, bounce light, reflections, glow, volumetric fog, haze, exposure, colour: Godot's
/// environment effects.</item>
/// </list>
/// </summary>
public partial class Atmosphere : Node3D
{
    public DirectionalLight3D Sun { get; private set; } = null!;
    public Godot.Environment Environment { get; private set; } = null!;

    private const double CloudPeriod = 6000, CloudHeight = 1600;
    // Cloud texture size (setting "Cloud resolution"). The cloud shadow decal stays at 256 (it is blurry anyway).
    private int _cloudPixels;
    private const int Tile = 256;
    private ShaderMaterial _cloudsNear = null!;
    private MeshInstance3D? _cloudsNearQuad;
    private ShaderMaterial _sky = null!, _shafts = null!, _beams = null!, _curtains = null!;
    private GpuParticles3D? _splashes;
    private ShaderMaterial? _splashMaterial;
    private float _splashLevel = float.NaN;
    private Vector3 _splashCamera;

    /// <summary>The water surface (world height) near the camera, or NaN: raindrop splashes stand on it.</summary>
    public void SetSplashSurface(float level, Vector3 camera) { _splashLevel = level; _splashCamera = camera; }
    private Decal _shadow = null!;
    private Image _noise = null!;
    private ImageTexture _shadowTexture = null!;
    private double _windX, _windZ;
    private float _shadowCoverage = -1;
    private MeshInstance3D? _shaftQuad, _beamQuad;
    private readonly ShaderMaterial _underwater = new() { Shader = GD.Load<Shader>("res://Game/Rewrite/Underwater.gdshader"), RenderPriority = 127 };
    private float _daylight = 1;
    private GpuParticles3D? _rainParticles;
    private byte[] _noiseBase = Array.Empty<byte>();
    /// <summary>Rain on (the water simulation's rain): the sky clouds over, light dims, haze thickens, rain falls.</summary>
    public bool Raining { get; set; }
    /// <summary>Precipitation 0..1 from the weather (light … storm); <see cref="Raining"/> (manual, H) forces heavy rain.</summary>
    public float Precipitation { get; set; }
    /// <summary>Falls as snow (cold, snowy ground).</summary>
    public bool Snowing { get; set; }
    /// <summary>Cloud cover from the weather (NaN = the Cloud cover setting).</summary>
    public float WeatherClouds { get; set; } = float.NaN;
    /// <summary>Thunderstorm: lightning flashes.</summary>
    public bool Thunder { get; set; }
    private float Precip => Raining ? Math.Max(Precipitation, .7f) : Precipitation;
    /// <summary>Cloud types from the weather (round 17, <see cref="Weather"/>): afternoon towers 0..1.</summary>
    public float Cumulus { get; set; }
    /// <summary>A thunderstorm on the horizon 0..1; upwind when it is coming, downwind when it has passed.</summary>
    public float DistantStorm { get; set; }
    public bool StormComing { get; set; } = true;
    /// <summary>Mackerel sky (a front on its way) 0..1.</summary>
    public float Mackerel { get; set; }
    /// <summary>The highest peak around the player (world x, height, z; NaN x: none) and how far it rises above the land around (m).</summary>
    public Vector3 Peak { get; set; } = new(float.NaN, 0, 0);
    public float PeakRelief { get; set; }
    /// <summary>The world's sea level (m): flat cloud bases sit at a fixed height above it.</summary>
    public float SeaLevel { get; set; }
    /// <summary>Screenshots: cloud types forced to (cumulus, distant storm, mackerel, lens); null = from the weather.</summary>
    public Vector4? ForceClouds { get; set; }

    /// <summary>Where the clouds have drifted to (main deck x/z, high layer x/z, low layer x/z, cloud time): written into
    /// debug dumps and set back by --cloud-drift, which also holds them still, so a dump's clouds come back exactly
    /// (round 32: without it a rebuilt dump showed other clouds).</summary>
    public double[] CloudDrift
    {
        get => new[] { _windX, _windZ, _highX, _highZ, _lowX, _lowZ, _cloudTime };
        set { (_windX, _windZ, _highX, _highZ, _lowX, _lowZ, _cloudTime) = (value[0], value[1], value[2], value[3], value[4], value[5], value[6]); _driftHeld = true; }
    }
    private bool _driftHeld;

    /// <summary>The host's cloud drift (multiplayer): taken as it is but, unlike --cloud-drift, not held: the clouds go
    /// on drifting here until the next update (once a second, gusts in step through Wind.Time).</summary>
    public void FollowCloudDrift(double[] drift)
    {
        if (drift.Length < 7) return;
        (_windX, _windZ, _highX, _highZ, _lowX, _lowZ, _cloudTime) = (drift[0], drift[1], drift[2], drift[3], drift[4], drift[5], drift[6]);
    }
    /// <summary>The next rain front (Weather.FrontHours / FrontStrength): hours until it arrives (NaN none), strength.</summary>
    public double FrontHours { get; set; } = double.NaN;
    public float FrontStrength { get; set; }
    /// <summary>Screenshots: the front forced to (distance km, strength); null = from the weather.</summary>
    public Vector2? ForceFront { get; set; }
    /// <summary>Screenshots: the distant storms' bearing (rad); NaN = from the wind.</summary>
    public float ForceStormAzimuth { get; set; } = float.NaN;
    private float _storm; // 0 fair … 1 full storm, eased
    /// <summary>The waves the water shader draws (same parameters and clock), for swimming and boats.</summary>
    public OceanWaves Waves { get; } = new();
    /// <summary>The spectrum sea of the "experimental 2" water (same sea state and wind as <see cref="Waves"/>).</summary>
    public SpectrumWaves Spectrum { get; } = new();
    /// <summary>Wave clock (s), shared with the shader through the global uniform <c>ocean_time</c>.</summary>
    public double OceanTime { get; private set; }

    /// <summary>Follow the host's wave clock (multiplayer): snap when far off, else ease so the waves never jump.</summary>
    public void FollowOceanTime(double hostTime, double delta, bool snap) =>
        OceanTime = snap || Math.Abs(hostTime - OceanTime) > 2 ? hostTime : OceanTime + (hostTime - OceanTime) * Math.Min(1, delta * .5);
    private double _wavesState = -1, _wavesAngle = Math.Atan2(.6, .8);
    /// <summary>The simulated wind (direction, speed, gusts).</summary>
    public Wind Wind { get; } = new();
    private float _wetness, _puddles; // cosmetic rain wetness of the ground and puddles (0..1)
    private ShaderMaterial? _water;
    private ImageTexture? _foamNet;
    /// <summary>The GPU FFT sea of the "experimental 3" water (null with other styles, or without compute shaders).</summary>
    public OceanFft? Ocean { get; private set; }

    public override void _Ready()
    {
        _noise = CloudNoise(Tile, 4242);
        var raw = _noise.GetData();
        _noiseBase = new byte[Tile * Tile];
        for (int i = 0; i < _noiseBase.Length; i++) _noiseBase[i] = raw[i * 4];
        _cloudPixels = Tile;
        _sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/Sky.gdshader") };
        // Cloud renderer v2: the clouds in front of the scene share the sky's code and its parameters (SkyParam).
        _cloudsNear = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/CloudsNear.gdshader"), RenderPriority = 99 };
        SkyParam("cloud_tex", ImageTexture.CreateFromImage(WithMipmaps(_noise)));
        SkyParam("cloud_period", CloudPeriod);
        SkyParam("cloud_height", CloudHeight);
        _cloudNoise = CloudNoise3D.Create();
        SkyParam("cloud_noise", _cloudNoise); // volumetric clouds: 3D billows and detail (and the mist's)
        Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = _sky, RadianceSize = Sky.RadianceSizeEnum.Size256, ProcessMode = Sky.ProcessModeEnum.Automatic },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky, AmbientLightEnergy = .55f,
            ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            FogEnabled = true, FogLightColor = new Color("aebfc2"), FogSunScatter = .15f, FogSkyAffect = .25f,
            SsaoRadius = 1.5f, SsaoIntensity = 1.2f,
            SsilRadius = 6, SsilIntensity = .8f,
            SsrMaxSteps = 48, SsrFadeIn = .15f, SsrFadeOut = 2, SsrDepthTolerance = .3f,
            GlowBloom = .04f, GlowHdrThreshold = 1.05f, GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Softlight,
            VolumetricFogDensity = .0035f, VolumetricFogLength = 180, VolumetricFogAnisotropy = .65f, VolumetricFogAlbedo = new Color("dfe6ea"),
            // Fog also scatters the sky's ambient light (Godot's default 0 left mist in shadow black: Tom, build 23, desert at dawn).
            VolumetricFogAmbientInject = 1,
            AdjustmentEnabled = true,
        };
        AddChild(new WorldEnvironment { Environment = Environment });
        Sun = new DirectionalLight3D { LightColor = new Color("fff3df"), ShadowEnabled = true, DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits };
        AddChild(Sun);

        _shadowTexture = new ImageTexture();
        _shadow = new Decal
        {
            Size = new Vector3((float)CloudPeriod * 3, 8000, (float)CloudPeriod * 3),
            AlbedoMix = 1, Modulate = new Color(1, 1, 1, .5f), UpperFade = .02f, LowerFade = .02f,
            NormalFade = 0, CullMask = 3, // terrain and objects
        };
        AddChild(_shadow);
        _shafts = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/SunShafts.gdshader"), RenderPriority = 100 };
        _beams = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/LightBeams.gdshader"), RenderPriority = 99 };
        _beams.SetShaderParameter("cloud_tex", _sky.GetShaderParameter("cloud_tex"));
        _beams.SetShaderParameter("cloud_period", CloudPeriod);
        _beams.SetShaderParameter("cloud_height", CloudHeight);
        _curtains = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/RainCurtains.gdshader"), RenderPriority = 98 };

        GameSettings.Changed += OnSetting;
        ApplyAll();
    }

    public override void _ExitTree()
    {
        GameSettings.Changed -= OnSetting;
        Ocean?.Dispose();
        Ocean = null;
    }

    /// <summary>Attach the full-screen sun-shaft pass to the camera.</summary>
    public void AttachCamera(Camera3D camera)
    {
        _shaftQuad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2, 2) }, MaterialOverride = _shafts, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384, Position = new Vector3(0, 0, -1), GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        };
        camera.AddChild(_shaftQuad);
        _beamQuad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2, 2) }, MaterialOverride = _beams, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384, Position = new Vector3(0, 0, -1), GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Visible = GameSettings.LightBeams.Value > 0,
        };
        camera.AddChild(_beamQuad);
        // Cloud renderer v2: the cloud volume in front of the scene (hidden with the other cloud qualities).
        _cloudsNearQuad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2, 2) }, MaterialOverride = _cloudsNear, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384, Position = new Vector3(0, 0, -1), GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            Visible = GameSettings.CloudQuality.Index == 3,
        };
        camera.AddChild(_cloudsNearQuad);
        // Distant rain curtains (simulated weather; the shader discards when the weather map is off).
        camera.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2, 2) }, MaterialOverride = _curtains, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384, Position = new Vector3(0, 0, -1), GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        });
        camera.AddChild(new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2, 2) }, MaterialOverride = _underwater, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384, Position = new Vector3(0, 0, -1), GIMode = GeometryInstance3D.GIModeEnum.Disabled,
        });
        // Rain: streaks falling around the camera (world-space particles, so they do not swing with the view).
        var streak = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            AlbedoColor = new Color(.8f, .85f, .9f, .18f), ProximityFadeEnabled = true, ProximityFadeDistance = 1.5f, BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
        };
        _rainParticles = new GpuParticles3D
        {
            Amount = 9000, Lifetime = 1.4, Emitting = false, LocalCoords = false, Position = new Vector3(0, 14, 0),
            VisibilityAabb = new Aabb(new Vector3(-40, -40, -40), new Vector3(80, 60, 80)),
            DrawPass1 = new QuadMesh { Size = new Vector2(.015f, .6f), Material = streak },
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(28, 3, 28),
                Direction = new Vector3(.08f, -1, .05f), Spread = 3, InitialVelocityMin = 13, InitialVelocityMax = 17, Gravity = new Vector3(0, -9.8f, 0),
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        camera.AddChild(_rainParticles);
        // Raindrop splashes on water (simulated weather): placed at the water surface near the camera each frame.
        _splashMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/RainSplash.gdshader") };
        _splashes = new GpuParticles3D
        {
            Amount = 1800, Lifetime = .32, Emitting = false, LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-16, -2, -16), new Vector3(32, 4, 32)),
            DrawPass1 = new QuadMesh { Size = new Vector2(.22f, .16f), Material = _splashMaterial },
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(12, .01f, 12),
                Gravity = Vector3.Zero, InitialVelocityMin = 0, InitialVelocityMax = 0, Spread = 0,
                ScaleMin = .7f, ScaleMax = 1.4f,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_splashes);
        ApplyAll();
    }

    /// <summary>
    /// Camera under water: <paramref name="depth"/> metres below the surface (≤ 0 = above water). Kind picks the
    /// water's colour and clarity (sea clear blue-green, lakes greener, rivers greener and murkier).
    /// </summary>
    public void SetUnderwater(float depth, WorldGen.WaterKind kind)
    {
        bool under = depth > .02f;
        _underwater.SetShaderParameter("active", under ? 1f : 0f);
        if (!under) return;
        _underwater.SetShaderParameter("surface_depth", depth);
        _underwater.SetShaderParameter("daylight", _daylight);
        var (color, density) = kind switch
        {
            WorldGen.WaterKind.Lake => (new Color(.06f, .24f, .2f), .09f),
            WorldGen.WaterKind.River => (new Color(.08f, .2f, .15f), .16f),
            WorldGen.WaterKind.Pond => (new Color(.07f, .09f, .05f), .45f), // peaty, murky wetland pools
            _ => (new Color(.04f, .26f, .33f), .05f),
        };
        _underwater.SetShaderParameter("water_color", color);
        _underwater.SetShaderParameter("density", density);
        // Light shafts and the caustic net on the bed: from the simulated sea in experimental 3, else drifting noise.
        _underwater.SetShaderParameter("shaft_steps", GameSettings.UnderwaterLight.Index switch { 0 => 0, 1 => 14, _ => 32 });
        _underwater.SetShaderParameter("sun_light", new Vector3(Sun.LightColor.R, Sun.LightColor.G, Sun.LightColor.B) * Sun.LightEnergy);
        bool fft = Ocean != null && kind == WorldGen.WaterKind.Ocean;
        _underwater.SetShaderParameter("use_fft", fft ? 1f : 0f);
        if (fft)
        {
            var (ox, oz) = OceanFft.Offsets(Spectrum.OriginX, Spectrum.OriginZ);
            _underwater.SetShaderParameter("fft_deriv", Ocean!.Derivatives);
            _underwater.SetShaderParameter("fft_size", new Vector3(OceanFft.Sizes[0], OceanFft.Sizes[1], OceanFft.Sizes[2]));
            _underwater.SetShaderParameter("fft_offset_x", ox);
            _underwater.SetShaderParameter("fft_offset_z", oz);
        }
    }

    /// <summary>The terrain water material: reflection quality follows the settings.</summary>
    public void SetWater(ShaderMaterial water) { _water = water; _wavesState = -1; ShareClouds(); ApplyAll(); }

    /// <summary>The water reflects the sky's own clouds (experimental 2): same texture, period and heights.</summary>
    /// <summary>A sky material parameter, mirrored to the clouds drawn in front of the scene (cloud renderer v2).</summary>
    private void SkyParam(string name, Variant value)
    {
        _sky.SetShaderParameter(name, value);
        _cloudsNear.SetShaderParameter(name, value);
    }

    private void ShareClouds()
    {
        if (_water == null) return;
        _water.SetShaderParameter("cloud_tex", _sky.GetShaderParameter("cloud_tex"));
        _water.SetShaderParameter("cloud_period", CloudPeriod);
        _water.SetShaderParameter("cloud_height", CloudHeight);
        _foamNet ??= FoamNet.Create(); // the foam net (foam style "net")
        _water.SetShaderParameter("foam_net", _foamNet);
    }

    private void OnSetting(Setting s)
    {
        if (s == GameSettings.WaterStyle) _wavesState = -1; // resend every wave set to the swapped shader
        ApplyAll();
    }

    private void ApplyAll()
    {
        var e = Environment;
        if (e == null) return;
        var vp = GetViewport();
        int shadows = GameSettings.Shadows.Index;
        Sun.ShadowEnabled = shadows > 0;
        Sun.DirectionalShadowMaxDistance = shadows switch { 1 => 90, 2 => 200, _ => 450 };
        RenderingServer.DirectionalShadowAtlasSetSize(shadows switch { 1 => 2048, 2 => 4096, _ => 8192 }, true);
        RenderingServer.DirectionalSoftShadowFilterSetQuality(shadows >= 3 ? RenderingServer.ShadowQuality.SoftHigh : RenderingServer.ShadowQuality.SoftLow);
        e.SsaoEnabled = GameSettings.Ssao.On;
        e.SsilEnabled = GameSettings.Ssil.On;
        e.SsrEnabled = GameSettings.Ssr.On;
        e.GlowEnabled = GameSettings.Glow.Value > 0;
        e.GlowIntensity = GameSettings.Glow.F;
        e.VolumetricFogEnabled = GameSettings.VolumetricFog.On;
        e.FogDensity = .00009f * GameSettings.Haze.F;
        e.FogAerialPerspective = Math.Clamp(.35f * GameSettings.Haze.F, 0, 1);
        e.FogEnabled = GameSettings.Haze.Value > 0;
        // Look (round 23, Tom: "the game in general sometimes feels very washed out"): classic = build 29.
        bool natural = Natural;
        e.TonemapMode = natural ? Godot.Environment.ToneMapper.Aces : Godot.Environment.ToneMapper.Filmic;
        e.TonemapWhite = natural ? 6 : 1;
        _baseExposure = .95f * GameSettings.Exposure.F * (natural ? 1.15f : 1);
        e.TonemapExposure = _baseExposure * _adapted;
        e.SsilIntensity = natural ? .55f : .8f;
        // Natural: only the sun disc and real highlights glow (round 24, Tom's dumps 174752/174828: at 1.3 the bright sky
        // and sunlit clouds around a low sun all glowed and merged into a white blob over half the screen).
        e.GlowHdrThreshold = natural ? 2.6f : 1.05f;
        e.GlowHdrScale = natural ? 1.2f : 2f;
        e.GlowBloom = natural ? 0 : .04f;
        e.GlowBlendMode = natural ? Godot.Environment.GlowBlendModeEnum.Additive : Godot.Environment.GlowBlendModeEnum.Softlight;
        if (natural) e.GlowIntensity = GameSettings.Glow.F * .55f;
        // Natural: glow from small blur levels only, and no single pixel brighter than 6 feeding it (round 24, Tom's
        // dumps 174752/174828: a hazy low sun bloomed into a white blob over half the screen through the widest level).
        e.GlowHdrLuminanceCap = natural ? 4 : 12;
        for (int level = 0; level < 7; level++)
            e.SetGlowLevel(level, natural ? level switch { 0 => .6f, 1 => .8f, 2 => 1, 3 => .5f, 4 => .15f, _ => 0 } : level is 2 or 4 ? 1 : 0);
        e.AdjustmentContrast = natural ? 1.06f : 1;
        e.FogAerialPerspective = Math.Clamp((natural ? .26f : .35f) * GameSettings.Haze.F, 0, 1);
        e.AdjustmentSaturation = GameSettings.Saturation.F * (natural ? 1.08f : 1);
        RenderingServer.GlobalShaderParameterSet("leaf_translucency", GameSettings.LeafTranslucency.F);
        RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.Medium, true);
        SkyParam("quality", GameSettings.CloudQuality.Index);
        SkyParam("cloud_blend", GameSettings.CloudBlend.Index);
        SkyParam("cloud_look", GameSettings.CloudLook.Index);
        SkyParam("cloud_caps", GameSettings.CloudCaps.Index);
        SkyParam("cloud_turbulence", GameSettings.CloudTurbulence.Index);
        SkyParam("lens_style", GameSettings.LensStyle.Index);
        // Tests (cloud look investigation, round 32): --cloud-fog-off draws CloudsNear without Godot's fog (with
        // --cloud-split-test=4 it draws every cloud, so the sky keeps its veil and the clouds lose theirs);
        // --cloud-dev=x,y sets cloud_dev (SkyCommon.gdshaderinc: aerial perspective scale, extra self-shadowing).
        var args = OS.GetCmdlineUserArgs();
        var nearShader = GD.Load<Shader>(GameSettings.CloudBlend.Index == 0 ? "res://Game/Rewrite/CloudsNear52.gdshader"
            : args.Contains("--cloud-fog-off") ? "res://Game/Rewrite/CloudsNearNoFog.gdshader" : "res://Game/Rewrite/CloudsNear.gdshader");
        if (_cloudsNear.Shader != nearShader) _cloudsNear.Shader = nearShader;
        _cloudsNear.SetShaderParameter("cloud_light_linear", GameSettings.CloudBlend.Index);
        SkyParam("split_test", args.FirstOrDefault(a => a.StartsWith("--cloud-split-test")) is { } split
            ? split.Contains('=') ? int.Parse(split[(split.IndexOf('=') + 1)..]) : 1 : 0);
        if (args.FirstOrDefault(a => a.StartsWith("--cloud-dev=")) is { } dev)
        {
            var given = dev[12..].Split(',').Select(t => float.Parse(t, System.Globalization.CultureInfo.InvariantCulture)).ToArray();
            var v = given.Concat(new[] { 1f, 0, 0, 0 }.Skip(given.Length)).ToArray();
            RenderingServer.GlobalShaderParameterSet("cloud_dev", new Vector4(v[0], v[1], v[2], v[3]));
        }
        bool v2 = GameSettings.CloudQuality.Index == 3;
        // (2: a temporal anti-aliasing averages the clouds' moving ray offsets — TAA, FSR 2)
        bool temporal = GameSettings.AntiAliasing.Index is 2 or 4;
        RenderingServer.GlobalShaderParameterSet("cloud_v2", v2 ? (temporal ? 2f : 1f) : 0f);
        if (_cloudsNearQuad != null) _cloudsNearQuad.Visible = v2;
        int pixels = 256 << GameSettings.CloudResolution.Index;
        if (pixels != _cloudPixels)
        {
            // Same pattern (periodic noise, same seed) with finer detail; the sky material keeps its uniforms.
            _cloudPixels = pixels;
            SkyParam("cloud_tex", ImageTexture.CreateFromImage(WithMipmaps(pixels == Tile ? _noise : CloudNoise(pixels, 4242))));
            _beams?.SetShaderParameter("cloud_tex", _sky.GetShaderParameter("cloud_tex"));
            ShareClouds();
        }
        SkyParam("sun_glare", GameSettings.SunGlare.F);
        SkyParam("sun_style", GameSettings.SunStyle.Index);
        _shafts.SetShaderParameter("intensity", GameSettings.SunShafts.F);
        _shafts.SetShaderParameter("contrast", GameSettings.GodRayContrast.F);
        _beams?.SetShaderParameter("contrast", GameSettings.GodRayContrast.F);
        // God ray contrast also feeds the volumetric fog: brighter, more forward scattering, so shafts through trees and fog stand
        // out (build 32 at 0: Godot's energy 1 and anisotropy .65).
        // (round 29: 1 + 2c and .65 + .2c lit the haze around a low sun into a glare half the screen wide — Tom's "blob")
        Sun.LightVolumetricFogEnergy = FogSunEnergy(1);
        Environment.VolumetricFogAnisotropy = .65f + .1f * GameSettings.GodRayContrast.F;
        if (_shaftQuad != null) _shaftQuad.Visible = GameSettings.SunShafts.Value > 0;
        _beams?.SetShaderParameter("intensity", GameSettings.LightBeams.F);
        if (_beamQuad != null) _beamQuad.Visible = GameSettings.LightBeams.Value > 0 && GameSettings.Clouds.Value > 0;
        _shadow.Visible = GameSettings.CloudShadows.Value > 0 && GameSettings.Clouds.Value > 0;
        _shadowStrength = Math.Clamp(.62f * GameSettings.CloudShadows.F, 0, 1);
        UpdateCoverage();
        // Master volume (Audio tab).
        AudioServer.SetBusVolumeDb(0, Mathf.LinearToDb(Math.Max(.0001f, GameSettings.MasterVolume.F)));
        AudioServer.SetBusMute(0, GameSettings.MasterVolume.F <= .001f);
        _water?.SetShaderParameter("foam_style", GameSettings.FoamStyle.Index);
        _water?.SetShaderParameter("ssr_steps", GameSettings.WaterReflections.Index switch { 0 => 0, 1 => 16, _ => 32 });
        if (vp != null)
        {
            vp.Msaa3D = Viewport.Msaa.Disabled; // the water's screen reads break with MSAA (see WorldLab)
            int aa = GameSettings.AntiAliasing.Index;
            vp.ScreenSpaceAA = aa switch { 1 => Viewport.ScreenSpaceAAEnum.Fxaa, 3 => Viewport.ScreenSpaceAAEnum.Smaa, _ => Viewport.ScreenSpaceAAEnum.Disabled };
            vp.UseTaa = aa == 2;
            float scale = GameSettings.RenderScale.F;
            // FSR 2 is temporal anti-aliasing and upscaling in one (at scale 1 it only smooths); otherwise FSR 1 upscales.
            vp.Scaling3DMode = aa == 4 ? Viewport.Scaling3DModeEnum.Fsr2
                : scale < .999f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
            vp.Scaling3DScale = scale;
            if (vp.GetCamera3D() is { } camera)
            {
                camera.Fov = GameSettings.Fov.F;
                // Depth of field (option): far blur only, starting well beyond the character.
                int dof = GameSettings.DepthOfField.Index;
                var attributes = camera.Attributes as CameraAttributesPractical ?? new CameraAttributesPractical();
                attributes.DofBlurNearEnabled = false;
                attributes.DofBlurFarEnabled = dof > 0;
                attributes.DofBlurFarDistance = dof == 1 ? 160 : 70;
                attributes.DofBlurFarTransition = dof == 1 ? 300 : 140;
                attributes.DofBlurAmount = dof == 1 ? .05f : .1f;
                // (No eye adaptation: tried for the natural look, it lifted overcast scenes and washed them out.)
                attributes.AutoExposureEnabled = false;
                camera.Attributes = attributes;
            }
        }
        DisplayServer.WindowSetVsyncMode(GameSettings.VSync.On ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = (int)GameSettings.MaxFps.Value;
    }

    /// <summary>
    /// Per frame: sun from the time of day, wind drift, cloud uniform, shadow decal placement.
    /// <paramref name="cameraWorldX"/>/<paramref name="cameraWorldZ"/> are absolute world metres; the scene uses a float
    /// origin at (<paramref name="originX"/>, <paramref name="originZ"/>).
    /// </summary>
    /// <summary>Sets the cosmetic rain wetness and puddles directly (screenshots and tests).</summary>
    public void SetWetness(float wetness, float puddles) { _wetness = wetness; _puddles = puddles; }

    public void Update(double delta, SkyState sky, double cameraWorldX, double cameraWorldZ, double originX, double originZ, Vector3 cameraLocal)
    {
        float hour = (float)sky.Hour;
        float elevation = (float)sky.SunElevation;
        // The sun from the clock's astronomy (kept a little above the horizon for the light; night is the moon's).
        var sunDir = GameClock.Direction(Math.Max(elevation, -4), sky.SunAzimuth);
        Sun.Basis = Basis.LookingAt(-sunDir, Mathf.Abs(sunDir.Y) > .99f ? Vector3.Forward : Vector3.Up);
        _daylightHours = (float)sky.DaylightHours;
        _sunElevation = elevation;
        UpdateNight(sky, elevation);
        _storm = Mathf.MoveToward(_storm, Mathf.Clamp(Precip * 1.15f, 0, 1), (float)delta / 8); // ~8 s to cloud over or clear
        OceanTime += delta;
        // Wet look: the ground darkens within ~30 s of rain and dries over ~6 min; puddles fill over ~2 min, last ~15 min.
        float dt = (float)delta;
        bool wetting = Precip > .1f && !Snowing;
        _wetness = wetting ? Mathf.Min(1, _wetness + dt / 30 * Precip * 1.5f) : Mathf.Max(0, _wetness - dt / 360);
        _puddles = wetting && Precip > .3f ? Mathf.Min(1, _puddles + dt / 120 * Precip * 1.5f) : Mathf.Max(0, _puddles - dt / 900);
        RenderingServer.GlobalShaderParameterSet("rain_wetness", new Vector2(_wetness, _puddles));
        RenderingServer.GlobalShaderParameterSet("ocean_time", (float)(OceanTime % 100000));
        double seaState = Math.Clamp(GameSettings.SeaState.Value + _storm * .45, 0, 1);
        if (_steer != null)
        {
            // Simulated: the sea builds with the wind over a minute or two and calms more slowly (no gusts in it);
            // the Sea state setting adds a background swell (0.25 = a light one).
            float target = Mathf.SmoothStep(4, 22, Wind.SteadySpeed);
            if (_windSea < 0 || StillShot) _windSea = target;
            _windSea = Mathf.MoveToward(_windSea, target, (float)delta / (target > _windSea ? 90 : 180));
            seaState = Math.Clamp(GameSettings.SeaState.Value * .6 + _windSea * .9, 0, 1);
        }
        if (originX != Spectrum.OriginX || originZ != Spectrum.OriginZ) { Spectrum.SetOrigin(originX, originZ); SendSpectrumOrigin(); }
        double windAngle = Math.Atan2(Wind.Direction.Y, Wind.Direction.X);
        if (Math.Abs(seaState - _wavesState) > .004 || Math.Abs(Mathf.AngleDifference((float)windAngle, (float)_wavesAngle)) > .14) { _wavesAngle = windAngle; UpdateWaves(seaState); }
        if (Ocean != null)
        {
            // Foam drifts with the wind at a few per cent of its speed (3A carries it; 3 ignores this).
            Ocean.FoamDrift = Wind.Direction * Wind.Speed * .03f;
            Ocean.Update(OceanTime);
        }
        // Overcast without rain also changes the mood (Noor: "proper overcast where the whole lighting mood changes"):
        // weaker, flatter sunlight, greyer and hazier air.
        float overcast = Mathf.SmoothStep(.7f, .95f, Coverage);
        float gloom = Math.Max(_storm, overcast * .7f);
        _gloom = gloom;
        LightProfiles.Update(dt, StillShot || SnapExposure);
        var light = AtmosphereLight.Darken(AtmosphereLight.Compute(elevation, gloom, _storm, Natural), elevation, gloom, Canopy);
        Sun.LightEnergy = light.SunEnergy;
        _daylight = Mathf.Clamp(elevation / 20, .08f, 1) * Mathf.Lerp(1, .45f, gloom);
        UpdateWaterSky(elevation);
        // Rain curtains: lit like the overcast (grey, dimmer at dusk and night), hanging from ~1.5 km above the sea.
        float lit = Mathf.Clamp(_daylight * 1.1f, .06f, 1);
        _curtains.SetShaderParameter("rain_color", new Color(.5f * lit, .53f * lit, .57f * lit));
        _curtains.SetShaderParameter("cloud_base", SeaLevel + 1500);
        _curtains.SetShaderParameter("strength", GameSettings.DistantRain.F);
        Environment.FogDensity = (Natural ? .00007f : .00009f) * GameSettings.Haze.F * Mathf.Lerp(1, 4, Math.Max(_storm, overcast * .45f));
        Environment.FogEnabled = GameSettings.Haze.Value > 0 || _storm > 0;
        // The sky's radiance map is not re-baked for the storm (global uniform), so dim the ambient light directly.
        // (natural look: less sky light in the shade, so lit and shaded ground differ)
        Environment.AmbientLightEnergy = light.AmbientEnergy;
        Environment.AmbientLightSkyContribution = light.SkyShare;
        Environment.AmbientLightColor = light.ShadeTint;
        // Eye adaptation (light model 1): toward the target over ~1.5 s; build 47's model keeps 1.
        float exposure = AtmosphereLight.Exposure(light, elevation, Canopy, gloom);
        _adapted = StillShot || SnapExposure ? exposure : Mathf.Lerp(_adapted, exposure, 1 - Mathf.Exp(-dt / 1.5f));
        SnapExposure = false;
        LightInfo = $"profile {LightProfiles.Dominant} sun {light.SunEnergy:0.00} sky {light.AmbientEnergy:0.00}×{light.SkyShare:0.00} canopy {Canopy:0.00} exposure ×{_adapted:0.00} (target {exposure:0.00}) base {_baseExposure:0.00}";
        Environment.TonemapExposure = _baseExposure * _adapted;
        UpdateCoverage();
        if (_rainParticles != null)
        {
            if (Snowing != _particlesSnow) SetParticleKind(Snowing);
            float p = Precip;
            _rainParticles.Emitting = p > .05f;
            float slant = Snowing ? .12f : .02f;
            if (_steer != null)
            {
                // Simulated weather (round 27, Tom: "rain should be denser… varying density"): up to 24 000 drops,
                // Rain strength scales all of them (1 ≈ 15 000 in a downpour), and the rain comes in sheets with the
                // gusts. (Changing Amount restarts the emitter: only when the model changes.)
                if (_rainParticles.Amount != 24000)
                {
                    _rainParticles.Amount = 24000;
                    SetParticleKind(Snowing);
                }
                float sheets = .7f + .45f * Wind.Gust;
                _rainParticles.AmountRatio = Mathf.Clamp(Mathf.Pow(p, .8f) * sheets, 0, 1) * Math.Clamp(GameSettings.RainStrength.F / 1.6f, .12f, 1);
                if (!Snowing) slant = .04f;
            }
            else
            {
                if (_rainParticles.Amount != 9000) { _rainParticles.Amount = 9000; SetParticleKind(Snowing); }
                _rainParticles.AmountRatio = Mathf.Clamp(p * 1.1f, 0, 1) * Math.Clamp(GameSettings.RainStrength.F, .2f, 1);
            }
            // Rain slants with the wind; snow drifts with it.
            if (_rainParticles.ProcessMaterial is ParticleProcessMaterial pm)
                pm.Direction = new Vector3(Wind.Direction.X * Wind.Speed * slant, -1, Wind.Direction.Y * Wind.Speed * slant);
        }
        // Raindrop splashes (round 28): only in the simulated model, raining, over water close below the camera, and
        // fewer on rough water (a still crown would float over the troughs).
        if (_splashes != null)
        {
            float above = _splashCamera.Y - _splashLevel;
            bool on = _steer != null && !Snowing && Precip > .05f && !float.IsNaN(_splashLevel) && above > -.2f && above < 60;
            _splashes.Emitting = on;
            if (on)
            {
                _splashes.GlobalPosition = new Vector3(_splashCamera.X, _splashLevel + .01f, _splashCamera.Z);
                _splashes.AmountRatio = Mathf.Clamp(Mathf.Pow(Precip, .8f) * GameSettings.RainStrength.F / 1.6f, .05f, 1)
                    * (1 - Mathf.SmoothStep(.5f, .9f, (float)_wavesState)) * (1 - Mathf.SmoothStep(25, 60, above));
                float splashLit = Mathf.Clamp(_daylight * 1.1f, .08f, 1);
                _splashMaterial!.SetShaderParameter("tint", new Color(.8f * splashLit, .84f * splashLit, .88f * splashLit));
            }
        }
        // Raindrops ringing on water (round 27): how hard it rains here (not snow), for the water shader.
        RenderingServer.GlobalShaderParameterSet("rain_now", new Vector4(Snowing ? 0 : Mathf.Clamp(Precip, 0, 1), 0, 0, 0));
        if (_steer == null) UpdateLightning(delta); // classic model; the simulated one has Lightning.cs
        else if (_flash != null) _flash.Visible = false;
        // Warmer light at a low sun (AtmosphereLight: build 47's single warm factor, or the colour curve by phase).
        Sun.LightColor = light.SunColor;
        var toSun = Sun.GlobalTransform.Basis.Z.Normalized(); // a directional light shines along -Z

        if (_steer is { } steer)
        {
            var (outDir, outSpeed) = Outflow();
            Wind.UpdateSimulated(delta, sky.Day * 24 + sky.Hour, steer, _steerSpeed, outDir, outSpeed, _storm, 77);
        }
        else Wind.Update(delta, sky.Day * 24 + sky.Hour, _storm, 77);
        double wind = Wind.Speed * 1.5; // m/s at cloud height
        if (!_driftHeld) { _windX += delta * wind * Wind.Direction.X; _windZ += delta * wind * Wind.Direction.Y; }
        _windX %= CloudPeriod; _windZ %= CloudPeriod;
        double cx = Wrap(cameraWorldX + _windX), cz = Wrap(cameraWorldZ + _windZ);
        RenderingServer.GlobalShaderParameterSet("cloud_state", new Vector4((float)cx, (float)cz, Coverage, _storm));
        // Layers ride different winds: high cirrus slow on a veered heading, low clouds faster than the main deck.
        // High winds veer ~50° from the surface wind; low clouds race along it.
        var high = Wind.Direction.Rotated(.9f);
        if (!_driftHeld)
        {
            _highX += delta * wind * .35 * high.X; _highZ += delta * wind * .35 * high.Y;
            _lowX += delta * wind * 1.9 * Wind.Direction.X; _lowZ += delta * wind * 1.9 * Wind.Direction.Y;
        }
        // Wrap where each layer's pattern repeats exactly (see Sky.gdshader), keeping float precision with no jump.
        double highPeriod = CloudPeriod * 25, lowPeriod = CloudPeriod * .45 * 40;
        _highX %= highPeriod; _highZ %= highPeriod; _lowX %= lowPeriod; _lowZ %= lowPeriod;
        RenderingServer.GlobalShaderParameterSet("cloud_layers", new Vector4(
            (float)WrapTo(cameraWorldX + _highX, highPeriod), (float)WrapTo(cameraWorldZ + _highZ, highPeriod),
            (float)WrapTo(cameraWorldX + _lowX, lowPeriod), (float)WrapTo(cameraWorldZ + _lowZ, lowPeriod)));
        UpdateMood(delta);
        UpdateCloudTypes(delta, sky, cameraWorldX, cameraWorldZ, cameraLocal);
        UpdateMist(delta, hour, originX, originZ);
        UpdateForestRays(delta);
        RenderingServer.GlobalShaderParameterSet("sun_dir_world", toSun);
        // The real sun, also below the horizon (the light stops at −4°): twilight colours follow it (Sky.gdshader).
        var trueSun = GameClock.Direction(sky.SunElevation, sky.SunAzimuth);
        RenderingServer.GlobalShaderParameterSet("sun_true", new Vector4(trueSun.X, trueSun.Y, trueSun.Z, (float)sky.SunElevation));
        // Auroras: high latitudes (toward the pole: sign = hemisphere), dark nights, clear skies; some nights strong,
        // some quiet (activity per night from the day number).
        double absLat = Math.Abs(sky.Latitude);
        double activity = .25 + .75 * Fract(Math.Sin((Math.Floor((double)sky.Day) + 1) * 12.9898) * 43758.5453);
        float aurora = (float)(DetMathSmooth((absLat - 50) / 18) * activity * _night * (1 - _storm) * (1 - Coverage * .6));
        RenderingServer.GlobalShaderParameterSet("aurora", sky.Latitude < 0 ? -aurora : aurora);
        var glowColour = Sun.LightColor * Sun.LightEnergy * Mathf.Clamp(toSun.Y * 6 + .2f, 0, 1);
        RenderingServer.GlobalShaderParameterSet("sun_glow", new Vector4(glowColour.R, glowColour.G, glowColour.B, 0));
        _shafts.SetShaderParameter("sun_direction", toSun);
        _water?.SetShaderParameter("sun_direction", toSun);
        _water?.SetShaderParameter("sun_color", Sun.LightColor * Sun.LightEnergy);
        _shafts.SetShaderParameter("sun_color", Sun.LightColor);
        // Light beams follow the sun by day and the moon at night (fainter, silver-blue, with its phase).
        if (_moon != null && _moon.Visible && Sun.LightEnergy * Mathf.Clamp(toSun.Y * 8, 0, 1) < _moon.LightEnergy * 3)
        {
            var toMoon = _moon.GlobalTransform.Basis.Z.Normalized();
            _beams.SetShaderParameter("light_direction", toMoon);
            _beams.SetShaderParameter("light_color", _moon.LightColor * _moon.LightEnergy * 2.2f);
        }
        else
        {
            _beams.SetShaderParameter("light_direction", toSun);
            _beams.SetShaderParameter("light_color", Sun.LightColor * Sun.LightEnergy);
        }

        // Shadow decal: ground point g is shaded by the cloud at g + toSun.xz · H / toSun.y. With the texture laid as
        // 3×3 tiles of period P, the decal centre must be ≡ P/2 − (origin + shift + wind) (mod P), chosen nearest the camera.
        double lift = Math.Max(toSun.Y, .25);
        double sx = toSun.X / lift * CloudHeight, sz = toSun.Z / lift * CloudHeight;
        double c0x = Wrap(CloudPeriod * .5 - (originX + sx + _windX)), c0z = Wrap(CloudPeriod * .5 - (originZ + sz + _windZ));
        double dx = c0x + CloudPeriod * Math.Round((cameraLocal.X - c0x) / CloudPeriod);
        double dz = c0z + CloudPeriod * Math.Round((cameraLocal.Z - c0z) / CloudPeriod);
        _shadow.Position = new Vector3((float)dx, cameraLocal.Y, (float)dz);
        // The decal darkens the ground's colour: right only while the sun is up and clear. At dawn and dusk the light
        // is weak and from the side (and the projection stretches), and under overcast there is no direct sun left to
        // block: fade the shadows there (Tom, build 23: black smudges at 04:40 in the desert).
        float sunUp = Mathf.SmoothStep(3, 18, _sunElevation);
        _shadow.Modulate = new Color(1, 1, 1, _shadowStrength * sunUp * (1 - _storm * .8f));
    }
    private float _shadowStrength = .31f;

    private void UpdateWaves(double seaState)
    {
        _wavesState = seaState;
        Waves.Configure(seaState, _wavesAngle); // waves run with the wind
        if (_water == null) return;
        var w = new Godot.Collections.Array<Vector4>();
        var q = new float[OceanWaves.Count];
        for (int i = 0; i < OceanWaves.Count; i++)
        {
            var (dx, dz, k, a) = Waves.Waves[i];
            w.Add(new Vector4((float)dx, (float)dz, (float)k, (float)a));
            q[i] = (float)Waves.Q[i];
        }
        _water.SetShaderParameter("waves", w);
        _water.SetShaderParameter("wave_q", q);
        _water.SetShaderParameter("wave_height", (float)Waves.WaveHeight);
        _water.SetShaderParameter("sea_state", (float)seaState);
        // Experimental 2: fixed wavelengths and directions, only amplitudes follow the sea state (smooth); the shader
        // follows the wind itself (regional headings).
        Spectrum.Configure(seaState);
        var w2 = new Godot.Collections.Array<Vector4>();
        for (int i = 0; i < SpectrumWaves.Count; i++)
        {
            var (dx, dz, k, a) = Spectrum.Waves[i];
            w2.Add(new Vector4((float)dx, (float)dz, (float)k, (float)a));
        }
        _water.SetShaderParameter("waves2", w2);
        _water.SetShaderParameter("wave2_hs", (float)Spectrum.Hs);
        _water.SetShaderParameter("wave2_chop", (float)Spectrum.Chop);
        // Experimental 3: the FFT sea, made on first use and freed when another style is chosen.
        bool fft = GameSettings.WaterStyle.Index >= 3 && RenderingServer.GetRenderingDevice() != null;
        if (fft && Ocean == null)
        {
            Ocean = OceanFft.Create(77);
            if (Ocean != null)
            {
                _water.SetShaderParameter("fft_disp", Ocean.Displacement);
                _water.SetShaderParameter("fft_deriv", Ocean.Derivatives);
                _water.SetShaderParameter("fft_size", new Vector3(OceanFft.Sizes[0], OceanFft.Sizes[1], OceanFft.Sizes[2]));
            }
        }
        else if (!fft && Ocean != null) { Ocean.Dispose(); Ocean = null; }
        if (Ocean != null) Ocean.CarriedFoam = GameSettings.WaterStyle.Index >= 4; // 3A and 4
        Ocean?.Configure(seaState, _wavesAngle);
        _water.SetShaderParameter("fft_crest_scale", (float)(Spectrum.Hs / Math.Sqrt(Spectrum.Hs * Spectrum.Hs + Spectrum.SwellHs * Spectrum.SwellHs)));
        SendSpectrumOrigin();
    }

    private static double Fract(double v) => v - Math.Floor(v);
    private static double DetMathSmooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * (3 - 2 * t); }

    /// <summary>
    /// The sky the water reflects (all water styles): the sky shader's horizon and zenith colours with its day factor,
    /// sunset warmth near the horizon, the biome mood, and storms greying and darkening it. Fixed daylight colours made
    /// lakes glow bright blue-white at night.
    /// </summary>
    private void UpdateWaterSky(float elevation)
    {
        if (_water == null) return;
        float day = Mathf.Clamp(Mathf.Sin(Mathf.DegToRad(Mathf.Max(elevation, -4))) * 4 + .35f, .08f, 1) * Mathf.Lerp(1, .5f, _storm);
        var mood = new Color(_mood.X, _mood.Y, _mood.Z);
        var horizon = new Color(.68f, .75f, .78f) * new Color(1, 1, 1).Lerp(mood, .7f);
        var zenith = new Color(.29f, .45f, .62f) * mood;
        float low = 1 - Mathf.SmoothStep(3, 25, elevation);
        horizon = horizon.Lerp(horizon * new Color(1.25f, .95f, .75f), low * (1 - _night));
        float grey = _storm * .5f;
        Color Grey(Color c) { float l = (c.R + c.G + c.B) / 3; return c.Lerp(new Color(l, l, l), grey); }
        _water.SetShaderParameter("sky_horizon", Grey(horizon) * day);
        _water.SetShaderParameter("sky_zenith", Grey(zenith) * day);
    }

    /// <summary>Experimental 2 waves are world-anchored: re-send the phases when the float origin moves.</summary>
    private void SendSpectrumOrigin()
    {
        if (_water == null) return;
        var phases = new float[SpectrumWaves.Count];
        for (int i = 0; i < phases.Length; i++) phases[i] = (float)Spectrum.ShiftedPhase[i];
        _water.SetShaderParameter("wave2_phase", phases);
        _water.SetShaderParameter("world_origin", new Vector2((float)Spectrum.OriginX, (float)Spectrum.OriginZ));
        var (ox, oz) = OceanFft.Offsets(Spectrum.OriginX, Spectrum.OriginZ);
        _water.SetShaderParameter("fft_offset_x", ox);
        _water.SetShaderParameter("fft_offset_z", oz);
    }

    /// <summary>Cloud cover in effect: the setting, closing in toward overcast while it rains.</summary>
    private float Coverage => Mathf.Lerp(float.IsNaN(WeatherClouds) ? GameSettings.Clouds.F : WeatherClouds, .97f, _storm);

    private void UpdateCoverage()
    {
        if (Math.Abs(_shadowCoverage - Coverage) > .03f || (_storm == 0 && Math.Abs(_shadowCoverage - Coverage) > 1e-4)) RebuildShadowTexture(Coverage);
    }

    private static double Wrap(double v) => v - Math.Floor(v / CloudPeriod) * CloudPeriod;
    // ---- precipitation kind and lightning
    private bool _particlesSnow;
    private DirectionalLight3D? _flash;
    private double _nextFlash = 4, _flashTime = -1;
    private readonly Random _flashRandom = new(1234);

    private void SetParticleKind(bool snow)
    {
        _particlesSnow = snow;
        if (_rainParticles == null) return;
        var pm = (ParticleProcessMaterial)_rainParticles.ProcessMaterial;
        if (snow)
        {
            _rainParticles.DrawPass1 = new QuadMesh { Size = new Vector2(.07f, .07f), Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(1, 1, 1, .85f), BillboardMode = BaseMaterial3D.BillboardModeEnum.Enabled,
            } };
            _rainParticles.Lifetime = 9;
            pm.InitialVelocityMin = 1.2f; pm.InitialVelocityMax = 2f; pm.Gravity = new Vector3(0, -.4f, 0); pm.Spread = 25;
            pm.TurbulenceEnabled = true; pm.TurbulenceNoiseStrength = 1.5f; pm.TurbulenceNoiseScale = 4;
        }
        else
        {
            _rainParticles.DrawPass1 = _steer != null
                // Simulated weather: thinner streaks that fade out within a few metres of the camera (close ones drew
                // white poles through the view).
                ? new QuadMesh { Size = new Vector2(.011f, .55f), Material = new StandardMaterial3D
                {
                    ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                    AlbedoColor = new Color(.78f, .83f, .88f, .2f), ProximityFadeEnabled = true, ProximityFadeDistance = 1.5f,
                    BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
                    DistanceFadeMode = BaseMaterial3D.DistanceFadeModeEnum.PixelAlpha, DistanceFadeMinDistance = 1.2f, DistanceFadeMaxDistance = 5f,
                } }
                : new QuadMesh { Size = new Vector2(.015f, .6f), Material = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                AlbedoColor = new Color(.8f, .85f, .9f, .18f), ProximityFadeEnabled = true, ProximityFadeDistance = 1.5f, BillboardMode = BaseMaterial3D.BillboardModeEnum.FixedY,
            } };
            _rainParticles.Lifetime = 1.4;
            pm.InitialVelocityMin = 13; pm.InitialVelocityMax = 17; pm.Gravity = new Vector3(0, -9.8f, 0); pm.Spread = 3;
            pm.TurbulenceEnabled = false;
        }
    }

    /// <summary>Thunderstorms: every 5–25 s a flash lights the land for a moment (a double flicker).</summary>
    private void UpdateLightning(double delta)
    {
        if (!Thunder || Snowing) { if (_flash != null) _flash.Visible = false; return; }
        _flash ??= CreateFlash();
        _nextFlash -= delta;
        if (_nextFlash <= 0) { _flashTime = 0; _nextFlash = 5 + _flashRandom.NextDouble() * 20; _flash.Basis = Basis.LookingAt(new Vector3((float)_flashRandom.NextDouble() - .5f, -1, (float)_flashRandom.NextDouble() - .5f).Normalized(), Vector3.Forward); }
        if (_flashTime >= 0)
        {
            _flashTime += delta;
            double t = _flashTime;
            float e = t < .07 ? 2.5f : t < .12 ? .2f : t < .2 ? 1.8f : t < .45 ? (float)(1.8 * (1 - (t - .2) / .25)) : 0;
            _flash.LightEnergy = e;
            _flash.Visible = e > 0;
            if (t > .5) _flashTime = -1;
        }
    }

    // ---- cloud types (round 17): globals for CloudShapes.gdshaderinc
    private double _cloudTime;
    private float _stormAzimuth = float.NaN, _lens, _farFlashTime = -1;
    private double _nextFarFlash = 3;
    private int _farFlashCell;

    private void UpdateCloudTypes(double delta, SkyState sky, double cameraWorldX, double cameraWorldZ, Vector3 cameraLocal)
    {
        if (!_driftHeld) _cloudTime = (_cloudTime + delta) % 100000;
        var force = ForceClouds;
        float cumulus = force?.X ?? Cumulus * (1 - _storm);
        float storm = force?.Y ?? DistantStorm * (1 - _storm * .9f);
        float mackerel = force?.Z ?? Mackerel * (1 - _storm);
        // Storms come from upwind and leave downwind; the bearing follows the wind slowly.
        var toward = StormComing ? -Wind.Direction : Wind.Direction;
        float az = Mathf.Atan2(toward.Y, toward.X);
        // Simulated weather: the towers stand over the strongest real thunderstorm 8–70 km away.
        if (_cells != null)
        {
            double best = 0;
            foreach (var c in _cells)
            {
                double ox = c.X - _cellsX, oz = c.Z - _cellsZ, d = Math.Sqrt(ox * ox + oz * oz);
                double w = c.Intensity * DetMathSmooth((d - c.Radius - 4000) / 5000) * DetMathSmooth((70000 - d) / 35000);
                if (w > best) { best = w; az = (float)Math.Atan2(oz, ox); }
            }
        }
        _stormAzimuth = float.IsNaN(_stormAzimuth) || force != null ? az : Mathf.LerpAngle(_stormAzimuth, az, (float)Math.Min(1, delta / 120));
        if (!float.IsNaN(ForceStormAzimuth)) _stormAzimuth = ForceStormAzimuth;
        // Lightning inside the far cells: a flicker every few seconds in one of the three (visible mostly at night).
        float flash = 0;
        if (storm > .3f)
        {
            _nextFarFlash -= delta;
            if (_nextFarFlash <= 0) { _farFlashTime = 0; _nextFarFlash = 1.5 + _flashRandom.NextDouble() * 7 / storm; _farFlashCell = _flashRandom.Next(3); }
            if (_farFlashTime >= 0)
            {
                _farFlashTime += (float)delta;
                float t = _farFlashTime;
                flash = t < .08f ? .95f : t < .14f ? .15f : t < .24f ? .8f : t < .5f ? .8f * (1 - (t - .24f) / .26f) : 0;
                if (t > .5f) _farFlashTime = -1;
            }
        }
        if (force != null && force.Value.Y > .5f && _shotFlash) flash = .9f; // screenshots: show one
        RenderingServer.GlobalShaderParameterSet("cloud_types", new Vector4(cumulus, storm, _stormAzimuth, _farFlashCell + Math.Min(flash, .99f)));
        // Lenticulars: a peak well above the land around, a strong steady wind, dry stable air; not every day.
        float lensTarget = 0;
        if (!float.IsNaN(Peak.X))
        {
            double dayGate = Fract(Math.Sin(Math.Floor((double)sky.Day) * 45.164 + 3.1) * 43758.5453) < .65 ? 1 : 0;
            lensTarget = (float)(DetMathSmooth((PeakRelief - 450) / 900) * DetMathSmooth((Wind.Speed - 6.5) / 4.5) * dayGate)
                * Mathf.Clamp(1 - Precip * 2.5f, 0, 1) * (1 - _storm);
        }
        if (force != null) lensTarget = float.IsNaN(Peak.X) ? 0 : force.Value.W;
        _lens = force != null ? lensTarget : Mathf.MoveToward(_lens, lensTarget, (float)delta / 40);
        var lens = Vector4.Zero;
        if (_lens > 0 && !float.IsNaN(Peak.X))
        {
            // Over the peak, a little downwind, ~1.6 km above its top.
            double lx = Peak.X + Wind.Direction.X * 1500 - cameraWorldX, lz = Peak.Z + Wind.Direction.Y * 1500 - cameraWorldZ;
            lens = new Vector4((float)lx, Peak.Y + 1600 - cameraLocal.Y, (float)lz, _lens);
        }
        RenderingServer.GlobalShaderParameterSet("cloud_lens", lens);
        // The next rain front: its edge upwind, closing in as its rain approaches (~9 km per game hour); seen from
        // ~70 km away as a dark deck on the horizon with a wall of rain under it (Sky.gdshader).
        double frontKm = ForceFront?.X ?? (double.IsNaN(FrontHours) ? double.NaN : Math.Max(2.5, FrontHours * 9));
        float frontStrength = ForceFront?.Y ?? FrontStrength;
        var upwind = -Wind.Direction;
        _frontDir = _frontDir == Vector2.Zero || ForceFront != null ? upwind : _frontDir.Lerp(upwind, (float)Math.Min(1, delta / 60)).Normalized();
        if (ForceFront != null && !float.IsNaN(ForceStormAzimuth)) _frontDir = new Vector2(Mathf.Cos(ForceStormAzimuth), Mathf.Sin(ForceStormAzimuth));
        float frontVisible = double.IsNaN(frontKm) ? 0 : frontStrength * Mathf.SmoothStep(75, 55, (float)frontKm);
        RenderingServer.GlobalShaderParameterSet("cloud_front", new Vector4(_frontDir.X, _frontDir.Y, double.IsNaN(frontKm) ? 1e6f : (float)(frontKm * 1000), frontVisible));
        RenderingServer.GlobalShaderParameterSet("relief_frame", _reliefSize > 0
            ? new Vector4((float)(cameraWorldX - _reliefX), (float)(cameraWorldZ - _reliefZ), _reliefSize, cameraLocal.Y - SeaLevel)
            : Vector4.Zero);
        // Flat cloud bases ~1.5 km above the sea.
        // (never closer than ~1 km: the sky is drawn behind the terrain, so a cloud base just over the player's head on
        // a mountain made a low grey ceiling and a wall of cloud on the horizon — Tom, build 25)
        // (cloud renderer v2 draws clouds in front of the scene too: the base stays at its height, also below the camera)
        float baseAbove = GameSettings.CloudQuality.Index == 3
            ? Mathf.Min(SeaLevel + 1500 - cameraLocal.Y, 2200)
            : Mathf.Clamp(SeaLevel + 1500 - cameraLocal.Y, 1000, 2200);
        float windAngle = Mathf.Atan2(Wind.Direction.Y, Wind.Direction.X);
        RenderingServer.GlobalShaderParameterSet("cloud_more", new Vector4(mackerel, windAngle, (float)_cloudTime, baseAbove));
        // The sun as the sky sees it, for the clouds drawn in front of the scene (CloudsNear.gdshader).
        var toLight = Sun.GlobalTransform.Basis.Z.Normalized();
        RenderingServer.GlobalShaderParameterSet("cloud_light", new Vector4(Sun.LightColor.R, Sun.LightColor.G, Sun.LightColor.B, Sun.LightEnergy));
        RenderingServer.GlobalShaderParameterSet("cloud_light_dir", new Vector4(toLight.X, toLight.Y, toLight.Z, 0));
        // Godot's fog as it falls on the sky (sky.glsl), so CloudsNear can veil its clouds the same way.
        var env = Environment;
        var fogLight = env.FogLightColor.SrgbToLinear() * env.FogLightEnergy;
        RenderingServer.GlobalShaderParameterSet("cloud_fog", new Vector4(fogLight.R, fogLight.G, fogLight.B, env.FogEnabled ? env.FogSkyAffect : 0));
        RenderingServer.GlobalShaderParameterSet("cloud_fog2", new Vector4(env.FogSunScatter, env.FogAerialPerspective, 0, 0));
        _cloudsNear.SetShaderParameter("fog_depth", env.VolumetricFogLength * 1.02f + 1);
    }
    // ---- simulated weather (round 27): steering wind, nearby thunderstorm cells, wind-built sea
    private Vector2? _steer;
    private float _steerSpeed;
    private System.Collections.Generic.IReadOnlyList<WeatherField.Cell>? _cells;
    private double _cellsX, _cellsZ;
    private float _windSea = -1;
    /// <summary>Thunderstorm cells near the player (simulated model) and the player position they are relative to.</summary>
    public System.Collections.Generic.IReadOnlyList<WeatherField.Cell>? Cells => _cells;
    public (double X, double Z) CellsOrigin => (_cellsX, _cellsZ);

    /// <summary>From the weather model: null steering = classic (build 35) wind and sea.</summary>
    public void SetSimulatedWeather(Vector2? steer, float steerSpeed, System.Collections.Generic.IReadOnlyList<WeatherField.Cell>? cells, double x, double z)
    {
        _steer = steer; _steerSpeed = steerSpeed; _cells = cells; _cellsX = x; _cellsZ = z;
    }

    /// <summary>Storm outflow at the player: gust fronts blow out of nearby thunderstorm cells (direction, extra m/s).</summary>
    private (Vector2 Dir, float Speed) Outflow()
    {
        if (_cells == null) return (Vector2.Zero, 0);
        Vector2 sum = Vector2.Zero; float best = 0;
        foreach (var c in _cells)
        {
            double ox = _cellsX - c.X, oz = _cellsZ - c.Z, d = Math.Sqrt(ox * ox + oz * oz) + 1;
            // Strongest under and just outside the core (the downdraft spreading out), fading by ~3 radii.
            float s = (float)(c.Intensity * 14 * Math.Exp(-Math.Pow(d / (c.Radius * 2.2), 2)));
            if (s <= .05f) continue;
            sum += new Vector2((float)(ox / d), (float)(oz / d)) * s;
            best = Math.Max(best, s);
        }
        return (sum.LengthSquared() > 1e-6f ? sum.Normalized() : Vector2.Zero, best);
    }

    /// <summary>Everything the sky, weather and sea are doing now (F9 debug dump).</summary>
    public string DebugInfo()
    {
        var c = System.Globalization.CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.###", c);
        return $"Weather input: precipitation {F(Precipitation)}{(Raining ? " (H rain on)" : "")}, snow {Snowing}, thunder {Thunder}, clouds {F(WeatherClouds)}\n" +
            $"Storm (eased) {F(_storm)}, cloud coverage {F(Coverage)}, wetness {F(_wetness)}, puddles {F(_puddles)}, mist {F(_mistAmount)}\n" +
            $"Cloud types: cumulus {F(Cumulus)}, distant storm {F(DistantStorm)} ({(StormComing ? "coming" : "passed")}), mackerel {F(Mackerel)}, lens {F(_lens)}" +
            (float.IsNaN(Peak.X) ? "" : $" (peak {F(Peak.X)}, {F(Peak.Y)}, {F(Peak.Z)}, relief {F(PeakRelief)} m)") + "\n" +
            $"Wind: from {Wind.Compass()}, direction ({F(Wind.Direction.X)}, {F(Wind.Direction.Y)}), {F(Wind.Speed)} m/s, gust {F(Wind.Gust)}\n" +
            $"Sun elevation {F(_sunElevation)}°, night {F(_night)}, daylight {F(_daylight)}, ocean time {F(OceanTime)} s\n" +
            $"Sea: effective sea state {F(_wavesState)} (setting {F(GameSettings.SeaState.Value)} + storm), wave heading {F(_wavesAngle)} rad, " +
            $"spectrum origin ({F(Spectrum.OriginX)}, {F(Spectrum.OriginZ)})";
    }

    private Vector2 _frontDir;
    private double _reliefX, _reliefZ;
    private float _reliefSize;

    /// <summary>
    /// The land around the camera for the clouds (round 19: banks on mountains): a square <paramref name="size"/> m
    /// wide centred on (<paramref name="x"/>, <paramref name="z"/>); r = height above the sea / 4000 m, g = how much the
    /// wind is pushed up the slope (windward faces).
    /// </summary>
    public void SetRelief(Image image, double x, double z, float size)
    {
        SkyParam("relief_tex", ImageTexture.CreateFromImage(image));
        _reliefX = x; _reliefZ = z; _reliefSize = size;
    }

    /// <summary>Screenshots with forced storms: light one far cell.</summary>
    public bool ShotFlash { set => _shotFlash = value; }
    private bool _shotFlash;

    private DirectionalLight3D CreateFlash()
    {
        var light = new DirectionalLight3D { LightColor = new Color("dfe6ff"), ShadowEnabled = false, Visible = false, SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly };
        AddChild(light);
        return light;
    }

    // ---- night: moonlight, stars (sky shader globals)
    private DirectionalLight3D? _moon;
    private float _night, _daylightHours = 14, _sunElevation = 30;

    private void UpdateNight(SkyState sky, float sunElevation)
    {
        _moon ??= CreateMoon();
        var moonDir = GameClock.Direction(sky.MoonElevation, sky.MoonAzimuth);
        // Illuminated fraction 0 (new) … 1 (full).
        float lit = (float)(.5 - .5 * Math.Cos(sky.MoonPhase * Math.Tau));
        _night = 1 - Mathf.SmoothStep(-10, 4, sunElevation);
        float moonUp = Mathf.SmoothStep(-2, 8, (float)sky.MoonElevation);
        _moon.Basis = Basis.LookingAt(-moonDir, Mathf.Abs(moonDir.Y) > .99f ? Vector3.Forward : Vector3.Up);
        // (light model 1: brighter moonlight, so a night is dark but readable)
        _moon.LightEnergy = .14f * lit * moonUp * _night * Mathf.Lerp(1, .25f, _storm) * (AtmosphereLight.Modern ? 1.5f : 1)
            * AtmosphereLight.NightLight; // (night darkness, D109)
        _moon.Visible = _moon.LightEnergy > .002f;
        RenderingServer.GlobalShaderParameterSet("night_sky", new Vector4(moonDir.X, moonDir.Y, moonDir.Z, (float)sky.MoonPhase));
        RenderingServer.GlobalShaderParameterSet("star_rotation", sky.Stars);
        RenderingServer.GlobalShaderParameterSet("sky_extra", new Vector4((float)(sky.Day + sky.Hour / 24), GameSettings.Planets.F, GameSettings.Stars.F, lit));
    }

    private DirectionalLight3D CreateMoon()
    {
        var moon = new DirectionalLight3D { LightColor = new Color("aebfe8"), ShadowEnabled = false, SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly };
        AddChild(moon);
        return moon;
    }

    private static double WrapTo(double v, double period) => v - Math.Floor(v / period) * period;

    // ---- ground fog and mist (option): height fog pooling below the player's ground, patches of volumetric wisps.
    private float _mistGround = float.NaN, _mistGroundTarget, _mistBiome, _mistWater, _mistAmount;
    private double _mistDriftX, _mistDriftZ;
    private FogVolume? _mistVolume;
    private static bool Natural => GameSettings.Look.Index == 1;
    /// <summary>Scripted screenshots (a few frames at ~1 FPS): no temporal fog smoothing, which would still be fading in.</summary>
    public bool StillShot { get; set; }

    /// <summary>0 open … 1 closed forest around the camera (WorldLab, for the exposure of light model 1).</summary>
    public float Canopy { get; set; }

    /// <summary>Jump to the exposure target on the next frame (teleports, A/B stills).</summary>
    public bool SnapExposure { get; set; }

    private float _baseExposure = 1, _adapted = 1, _gloom;

    /// <summary>The light state of the last frame, for logs (benchmark A/B stills).</summary>
    public string LightInfo { get; private set; } = "";
    private ImageTexture3D? _cloudNoise;
    private ShaderMaterial? _mistMaterial;

    /// <summary>From the player's surroundings: ground height (scene y), biome mist (0..1), water nearby (0..1).</summary>
    public void SetMist(float groundY, float biomeMist, float nearWater)
    {
        _mistGroundTarget = groundY; _mistBiome = biomeMist; _mistWater = nearWater;
        if (float.IsNaN(_mistGround)) _mistGround = groundY;
    }

    /// <summary>Mist amount 0..1 now: dawn (and a little at dusk), misty biomes, wet ground after rain, water nearby.</summary>
    public float MistAmount => _mistAmount;

    /// <summary>Colour of lit mist now: the haze colour, dimmed at night, warmed by a low sun.</summary>
    public Color MistColour => (new Color("dde3e6") * new Color(_mood.X, _mood.Y, _mood.Z)).Lerp(Sun.LightColor, .25f) * Mathf.Clamp(_daylight * 1.05f, .1f, 1);

    private void UpdateMist(double delta, float hour, double originX, double originZ)
    {
        int mode = GameSettings.GroundFog.Index;
        // Dawn mist peaks just after sunrise, a little at dusk (sunrise/sunset from the clock's daylight hours).
        float sunrise = 12 - _daylightHours / 2, sunset = 12 + _daylightHours / 2;
        float dawn = Mathf.Exp(-Mathf.Pow((hour - sunrise - .6f) / 1.6f, 2)) + .45f * Mathf.Exp(-Mathf.Pow((hour - sunset - 1) / 1.5f, 2));
        float afterRain = _wetness * (1 - _storm);
        // Mist burns off as the sun climbs: around midday only misty biomes, rain and wet ground keep some.
        float noon = Mathf.Exp(-Mathf.Pow((hour - 13f) / (_daylightHours / 4f), 2));
        float target = mode == 0 || float.IsNaN(_mistGround) ? 0
            : Mathf.Clamp((.7f * dawn + .5f * _mistBiome + .1f * _mistWater) * (1 - .65f * noon) + .35f * afterRain + .3f * _storm, 0, 1) * .75f;
        _mistAmount = Mathf.MoveToward(_mistAmount, target, (float)delta / 6);
        if (float.IsNaN(_mistGround)) return;
        // Follow the ground slowly, so walking down into a valley wades into its mist before it settles around you.
        // (a jump — teleport, map travel — snaps: the mist stayed at the old height, buried or hanging in the air)
        _mistGround = Mathf.Abs(_mistGroundTarget - _mistGround) > 60 ? _mistGroundTarget
            : Mathf.Lerp(_mistGround, _mistGroundTarget, 1 - Mathf.Exp(-(float)delta / 8));
        var e = Environment;
        if (_mistAmount > .005f)
        {
            // Below this height everything takes fog by its height alone (Godot height fog): valleys, rivers, lakes.
            // Godot fogs a point by how far it lies below FogHeight (not by distance), so a valley hundreds of metres
            // below a mountain the player stands on went fully grey (Tom, build 25: "sometimes I see nothing, just a
            // single colour"): gentle density, a depth of ~150 m reaches only ~40 %.
            e.FogEnabled = true;
            e.FogHeight = _mistGround - 4;
            // With mist patches the volume carries the shape (banks, billows); the even height fog only fills in.
            e.FogHeightDensity = .0035f * _mistAmount * _mistAmount * (mode == 2 ? .3f : 1);
        }
        else e.FogHeightDensity = 0;
        // Patches: a volumetric fog box around the player (volumetric fog on for it, no global density).
        bool patches = mode == 2 && _mistAmount > .01f;
        if (patches && _mistVolume == null)
        {
            _mistMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/Mist.gdshader") };
            _mistMaterial.SetShaderParameter("noise3d", _cloudNoise);
            _mistVolume = new FogVolume { Size = new Vector3(420, 70, 420), Material = _mistMaterial, Shape = RenderingServer.FogVolumeShape.Box };
            AddChild(_mistVolume);
        }
        if (_mistVolume != null && _mistMaterial != null)
        {
            _mistVolume.Visible = patches;
            if (patches)
            {
                var cam = GetViewport()?.GetCamera3D();
                if (cam != null) _mistVolume.GlobalPosition = new Vector3(cam.GlobalPosition.X, _mistGround + 15, cam.GlobalPosition.Z);
                double drift = Wind.Speed * .08;
                _mistDriftX += delta * drift * Wind.Direction.X; _mistDriftZ += delta * drift * Wind.Direction.Y;
                _mistDriftX %= 8192; _mistDriftZ %= 8192;
                _mistMaterial.SetShaderParameter("amount", _mistAmount);
                _mistMaterial.SetShaderParameter("ground", _mistGround);
                _mistMaterial.SetShaderParameter("origin", new Vector2((float)(originX % 8192), (float)(originZ % 8192)));
                _mistMaterial.SetShaderParameter("drift", new Vector2((float)_mistDriftX, (float)_mistDriftZ));
            }
            e.VolumetricFogEnabled = GameSettings.VolumetricFog.On || patches;
            e.VolumetricFogTemporalReprojectionEnabled = !StillShot;
            e.VolumetricFogTemporalReprojectionAmount = GameSettings.FogSmoothing.F;
            e.VolumetricFogDensity = GameSettings.VolumetricFog.On ? (Natural ? .002f : .0035f) : 0;
        }
    }

    // ---- forest rays (M2 shoot-out, fx.forest_rays): A = volumetric forest air, B = stronger screen-space shafts.
    private FogVolume? _forestAir;
    private ShaderMaterial? _forestAirMaterial;
    private float _forest;
    private bool _forestAirOn;
    private Vector3 _forestDrift;

    private void UpdateForestRays(double delta)
    {
        int mode = GameSettings.ForestRays.Index;
        // How much "forest by day" there is: the canopy around the player (0 when flying), the sun up, no storm.
        float target = mode == 0 ? 0 : Mathf.Clamp(Canopy, 0, 1) * Mathf.SmoothStep(-1, 8, _sunElevation) * (1 - _storm);
        _forest = StillShot || SnapExposure ? target : Mathf.MoveToward(_forest, target, (float)delta / 3);
        if (mode == 2 && _shaftQuad != null)
        {
            // B: the screen-space shafts, up to 2.5× inside a closed forest (and on in forests even when the setting is 0).
            float baseShafts = Math.Max(GameSettings.SunShafts.F, .6f * _forest);
            _shafts.SetShaderParameter("intensity", baseShafts * (1 + 1.5f * _forest));
            _shaftQuad.Visible = baseShafts > 0;
        }
        bool air = mode == 1 && _forest > .01f && !float.IsNaN(_mistGround);
        if (!air) Sun.LightVolumetricFogEnergy = FogSunEnergy(1); // (every frame: the cloud cover changes)
        if (!air && _forestAirOn)
        {
            // Back to what the settings say (ApplyAll only runs when a setting changes).
            Environment.VolumetricFogEnabled = GameSettings.VolumetricFog.On || (_mistVolume?.Visible ?? false);
        }
        _forestAirOn = air;
        if (air && _forestAir == null)
        {
            _forestAirMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/ForestAir.gdshader") };
            _forestAirMaterial.SetShaderParameter("noise3d", _cloudNoise);
            _forestAir = new FogVolume { Size = new Vector3(160, 60, 160), Material = _forestAirMaterial, Shape = RenderingServer.FogVolumeShape.Box };
            AddChild(_forestAir);
        }
        if (_forestAir == null || _forestAirMaterial == null) return;
        _forestAir.Visible = air;
        if (!air) return;
        // A: a thin haze under the crowns around the camera, lit by the sun through the shadow map; the sun's light into
        // the fog is raised there so the shafts stand out (only near the camera: the box is 160 m wide).
        var cam = GetViewport()?.GetCamera3D();
        if (cam != null) _forestAir.GlobalPosition = new Vector3(cam.GlobalPosition.X, _mistGround + 25, cam.GlobalPosition.Z);
        _forestDrift += new Vector3(Wind.Direction.X, 0, Wind.Direction.Y) * (float)(delta * Wind.Speed * .05);
        _forestAirMaterial.SetShaderParameter("density", .014f * _forest);
        _forestAirMaterial.SetShaderParameter("ground", _mistGround);
        _forestAirMaterial.SetShaderParameter("drift", _forestDrift);
        _forestAirMaterial.SetShaderParameter("albedo", new Color("ece6da").Lerp(Sun.LightColor, .25f));
        Environment.VolumetricFogEnabled = true;
        Sun.LightVolumetricFogEnergy = FogSunEnergy(1 + 1.8f * _forest);
    }

    /// <summary>
    /// The sun's light into the volumetric fog. Godot lights the fog by the shadow map, which the clouds are not in, so
    /// under an overcast or a storm the fog went on scattering full sunlight toward the sun: a bright glow around a
    /// sun hidden by the deck (Tom, storm dump 204727; D107). It now dims with the cloud cover like the sunlight on the
    /// ground does, more strongly (the glow is all direct light): a clear or broken sky is unchanged (gloom 0).
    /// </summary>
    private float FogSunEnergy(float boost) => (1 + .8f * GameSettings.GodRayContrast.F) * boost * (1 - .9f * _gloom);

    // ---- biome moods (option): the sky, haze and fog drift toward the biome around the player over a few seconds.
    private Vector4 _mood = new(1, 1, 1, 0), _moodTarget = new(1, 1, 1, 0);
    private double _highX, _highZ, _lowX, _lowZ;

    /// <summary>The biome blend around the player: sky tint (rgb) and haze (a), from the biome definitions.</summary>
    public void SetBiomeMood(Vector4 target) => _moodTarget = target;

    private void UpdateMood(double delta)
    {
        // Round 29 (Tom: "biome specific skies don't seem to change anything"): the tints in biomes.json are near white
        // (a 5–20 % shift) and were scaled by .6 / 1.4 — invisible. Now amplified (subtle ×1.6, strong ×3.2) and also
        // carried by the sunlight and the fog's colour, so a jungle, a desert and a volcano feel different.
        float strength = GameSettings.BiomeMoods.Index switch { 0 => 0, 1 => 1.5f, _ => 2.6f };
        var t = _moodTarget;
        var target = new Vector4(Mathf.Clamp(1 + strength * (t.X - 1), .3f, 1.3f), Mathf.Clamp(1 + strength * (t.Y - 1), .3f, 1.3f),
            Mathf.Clamp(1 + strength * (t.Z - 1), .3f, 1.3f), Mathf.Clamp(t.W * strength * .55f, 0, 1));
        _mood = _mood.Lerp(target, 1 - Mathf.Exp(-(float)delta / 3)); // ~3 s to settle when crossing into a biome
        RenderingServer.GlobalShaderParameterSet("sky_mood", _mood);
        var tint = new Color(_mood.X, _mood.Y, _mood.Z);
        // Fog takes the light of the hour: grey-blue by day, warm at a low sun, dim moonlit blue at night.
        float light = Mathf.Clamp(_daylight * 1.1f, 0, 1);
        if (AtmosphereLight.Fog(_sunElevation, _gloom, _storm) is var (fog1, scatter, night1))
        {
            // Light model 1: haze cool away from the sun, the sun's own colour scattered toward it, deep teal at night.
            Environment.FogLightColor = (fog1 * tint * light).Lerp(night1, _night * .85f);
            Environment.FogSunScatter = scatter;
        }
        else
        {
            var fog = new Color("aebfc2").Lerp(new Color("e0b090"), (1 - Mathf.Clamp(_sunElevation / 12, 0, 1)) * (1 - _night) * .5f);
            Environment.FogLightColor = (fog * tint * light).Lerp(new Color("0b0f18"), _night * .9f);
            Environment.FogSunScatter = .15f;
        }
        Sun.LightColor *= new Color(1, 1, 1).Lerp(tint, .35f);
        Environment.VolumetricFogAlbedo = new Color("dfe6ea") * new Color(1, 1, 1).Lerp(tint, .6f);
        Environment.FogDensity *= 1 + 2.5f * _mood.W;
        Environment.FogEnabled |= _mood.W > .01f;
    }

    private void RebuildShadowTexture(float coverage)
    {
        _shadowCoverage = coverage;
        // Same density curve as the sky shader's base layer (without its fine detail), tiled 3×3.
        var image = Image.CreateEmpty(Tile * 3, Tile * 3, false, Image.Format.Rgba8);
        float edge = 1 - coverage;
        var alpha = new byte[Tile * Tile];
        for (int y = 0; y < Tile; y++)
            for (int x = 0; x < Tile; x++)
            {
                float n = _noiseBase[y * Tile + x] / 255f * .8f + .5f * .2f;
                float d = Mathf.SmoothStep(edge - .05f, edge + .2f, n);
                alpha[y * Tile + x] = (byte)(Mathf.Clamp(d, 0, 1) * 255);
            }
        var data = new byte[Tile * 3 * Tile * 3 * 4];
        for (int y = 0; y < Tile * 3; y++)
            for (int x = 0; x < Tile * 3; x++)
            {
                int i = (y * Tile * 3 + x) * 4;
                data[i] = 0; data[i + 1] = 0; data[i + 2] = 8; data[i + 3] = alpha[(y % Tile) * Tile + x % Tile];
            }
        image.SetData(Tile * 3, Tile * 3, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        _shadowTexture = ImageTexture.CreateFromImage(image);
        _shadow.TextureAlbedo = _shadowTexture;
    }

    internal static Image WithMipmaps(Image image)
    {
        var copy = (Image)image.Duplicate();
        copy.GenerateMipmaps();
        return copy;
    }

    /// <summary>
    /// Tileable cloud noise: R = billowy base (4 octaves of periodic value noise from 4 to 32 cells), G = finer detail
    /// (8 to 64 cells). Deterministic; ~10 ms.
    /// </summary>
    internal static Image CloudNoise(int size, uint seed)
    {
        var data = new byte[size * size * 4];
        float Lattice(int x, int y, int period, uint salt)
        {
            x = ((x % period) + period) % period; y = ((y % period) + period) % period;
            uint h = (uint)x * 374761393u + (uint)y * 668265263u + salt * 2246822519u + seed;
            h = (h ^ (h >> 13)) * 1274126177u;
            return ((h ^ (h >> 16)) & 0xFFFF) / 65535f;
        }
        float Value(float u, float v, int period, uint salt)
        {
            float x = u * period, y = v * period;
            int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
            float a = Lattice(x0, y0, period, salt), b = Lattice(x0 + 1, y0, period, salt);
            float c = Lattice(x0, y0 + 1, period, salt), d = Lattice(x0 + 1, y0 + 1, period, salt);
            return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
        }
        float Fbm(float u, float v, int period, int octaves, uint salt)
        {
            float sum = 0, amp = 1, norm = 0;
            for (int o = 0; o < octaves; o++) { sum += amp * Value(u, v, period << o, salt + (uint)o); norm += amp; amp *= .5f; }
            return sum / norm;
        }
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                // Billowy base: fold the fbm (|2n-1|) and invert, rounding the cloud tops.
                // Bigger textures add finer octaves (the large shapes stay the same).
                int extra = size > 256 ? (int)Math.Round(Math.Log2(size / 256.0)) : 0;
                float n = Fbm(u, v, 4, 4 + extra, 1);
                float b = 1 - MathF.Abs(2 * Fbm(u, v, 8, 3 + extra, 9) - 1);
                float baseValue = Math.Clamp((n * .75f + b * .25f - .2f) / .6f, 0, 1);
                float detail = Fbm(u, v, 8, 4 + extra, 21);
                int i = (y * size + x) * 4;
                data[i] = (byte)(baseValue * 255); data[i + 1] = (byte)(detail * 255); data[i + 2] = 0; data[i + 3] = 255;
            }
        return Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
    }
}
