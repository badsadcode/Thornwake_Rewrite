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
    private ShaderMaterial _sky = null!, _shafts = null!, _beams = null!;
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
    /// <summary>Screenshots: the distant storms' bearing (rad); NaN = from the wind.</summary>
    public float ForceStormAzimuth { get; set; } = float.NaN;
    private float _storm; // 0 fair … 1 full storm, eased
    /// <summary>The waves the water shader draws (same parameters and clock), for swimming and boats.</summary>
    public OceanWaves Waves { get; } = new();
    /// <summary>The spectrum sea of the "experimental 2" water (same sea state and wind as <see cref="Waves"/>).</summary>
    public SpectrumWaves Spectrum { get; } = new();
    /// <summary>Wave clock (s), shared with the shader through the global uniform <c>ocean_time</c>.</summary>
    public double OceanTime { get; private set; }
    private double _wavesState = -1, _wavesAngle = Math.Atan2(.6, .8);
    /// <summary>The simulated wind (direction, speed, gusts).</summary>
    public Wind Wind { get; } = new();
    private float _wetness, _puddles; // cosmetic rain wetness of the ground and puddles (0..1)
    private ShaderMaterial? _water;

    public override void _Ready()
    {
        _noise = CloudNoise(Tile, 4242);
        var raw = _noise.GetData();
        _noiseBase = new byte[Tile * Tile];
        for (int i = 0; i < _noiseBase.Length; i++) _noiseBase[i] = raw[i * 4];
        _cloudPixels = Tile;
        _sky = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/Sky.gdshader") };
        _sky.SetShaderParameter("cloud_tex", ImageTexture.CreateFromImage(WithMipmaps(_noise)));
        _sky.SetShaderParameter("cloud_period", CloudPeriod);
        _sky.SetShaderParameter("cloud_height", CloudHeight);
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

        GameSettings.Changed += OnSetting;
        ApplyAll();
    }

    public override void _ExitTree() => GameSettings.Changed -= OnSetting;

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
    }

    /// <summary>The terrain water material: reflection quality follows the settings.</summary>
    public void SetWater(ShaderMaterial water) { _water = water; _wavesState = -1; ShareClouds(); ApplyAll(); }

    /// <summary>The water reflects the sky's own clouds (experimental 2): same texture, period and heights.</summary>
    private void ShareClouds()
    {
        if (_water == null) return;
        _water.SetShaderParameter("cloud_tex", _sky.GetShaderParameter("cloud_tex"));
        _water.SetShaderParameter("cloud_period", CloudPeriod);
        _water.SetShaderParameter("cloud_height", CloudHeight);
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
        e.TonemapExposure = .95f * GameSettings.Exposure.F;
        e.AdjustmentSaturation = GameSettings.Saturation.F;
        RenderingServer.GlobalShaderParameterSet("leaf_translucency", GameSettings.LeafTranslucency.F);
        RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.Medium, true);
        _sky.SetShaderParameter("quality", GameSettings.CloudQuality.Index);
        int pixels = 256 << GameSettings.CloudResolution.Index;
        if (pixels != _cloudPixels)
        {
            // Same pattern (periodic noise, same seed) with finer detail; the sky material keeps its uniforms.
            _cloudPixels = pixels;
            _sky.SetShaderParameter("cloud_tex", ImageTexture.CreateFromImage(WithMipmaps(pixels == Tile ? _noise : CloudNoise(pixels, 4242))));
            _beams?.SetShaderParameter("cloud_tex", _sky.GetShaderParameter("cloud_tex"));
            ShareClouds();
        }
        _sky.SetShaderParameter("sun_glare", GameSettings.SunGlare.F);
        _sky.SetShaderParameter("sun_style", GameSettings.SunStyle.Index);
        _shafts.SetShaderParameter("intensity", GameSettings.SunShafts.F);
        if (_shaftQuad != null) _shaftQuad.Visible = GameSettings.SunShafts.Value > 0;
        _beams?.SetShaderParameter("intensity", GameSettings.LightBeams.F);
        if (_beamQuad != null) _beamQuad.Visible = GameSettings.LightBeams.Value > 0 && GameSettings.Clouds.Value > 0;
        _shadow.Visible = GameSettings.CloudShadows.Value > 0 && GameSettings.Clouds.Value > 0;
        _shadow.Modulate = new Color(1, 1, 1, Math.Clamp(.62f * GameSettings.CloudShadows.F, 0, 1));
        UpdateCoverage();
        _water?.SetShaderParameter("ssr_steps", GameSettings.WaterReflections.Index switch { 0 => 0, 1 => 16, _ => 32 });
        if (vp != null)
        {
            vp.Msaa3D = Viewport.Msaa.Disabled; // the water's screen reads break with MSAA (see WorldLab)
            vp.ScreenSpaceAA = GameSettings.AntiAliasing.Index == 1 ? Viewport.ScreenSpaceAAEnum.Fxaa : Viewport.ScreenSpaceAAEnum.Disabled;
            vp.UseTaa = GameSettings.AntiAliasing.Index == 2;
            float scale = GameSettings.RenderScale.F;
            vp.Scaling3DMode = scale < .999f ? Viewport.Scaling3DModeEnum.Fsr : Viewport.Scaling3DModeEnum.Bilinear;
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
        if (originX != Spectrum.OriginX || originZ != Spectrum.OriginZ) { Spectrum.SetOrigin(originX, originZ); SendSpectrumOrigin(); }
        double windAngle = Math.Atan2(Wind.Direction.Y, Wind.Direction.X);
        if (Math.Abs(seaState - _wavesState) > .004 || Math.Abs(Mathf.AngleDifference((float)windAngle, (float)_wavesAngle)) > .14) { _wavesAngle = windAngle; UpdateWaves(seaState); }
        Sun.LightEnergy = Mathf.Clamp(elevation / 25, 0.05f, 1.1f) * Mathf.Lerp(1, .3f, _storm);
        _daylight = Mathf.Clamp(elevation / 20, .08f, 1) * Mathf.Lerp(1, .45f, _storm);
        UpdateWaterSky(elevation);
        Environment.FogDensity = .00009f * GameSettings.Haze.F * Mathf.Lerp(1, 4, _storm);
        Environment.FogEnabled = GameSettings.Haze.Value > 0 || _storm > 0;
        // The sky's radiance map is not re-baked for the storm (global uniform), so dim the ambient light directly.
        Environment.AmbientLightEnergy = Mathf.Lerp(.55f, .38f, _storm);
        UpdateCoverage();
        if (_rainParticles != null)
        {
            if (Snowing != _particlesSnow) SetParticleKind(Snowing);
            float p = Precip;
            _rainParticles.Emitting = p > .05f;
            _rainParticles.AmountRatio = Mathf.Clamp(p * 1.1f, 0, 1) * Math.Clamp(GameSettings.RainStrength.F, .2f, 1);
            // Rain slants with the wind; snow drifts with it.
            if (_rainParticles.ProcessMaterial is ParticleProcessMaterial pm)
                pm.Direction = new Vector3(Wind.Direction.X * Wind.Speed * (Snowing ? .12f : .02f), -1, Wind.Direction.Y * Wind.Speed * (Snowing ? .12f : .02f));
        }
        UpdateLightning(delta);
        // Warmer light at a low sun.
        float warm = 1 - Mathf.Clamp(elevation / 30, 0, 1);
        Sun.LightColor = new Color("fff3df").Lerp(new Color("ffc58a"), warm * .8f);
        var toSun = Sun.GlobalTransform.Basis.Z.Normalized(); // a directional light shines along -Z

        Wind.Update(delta, sky.Day * 24 + sky.Hour, _storm, 77);
        double wind = Wind.Speed * 1.5; // m/s at cloud height
        _windX += delta * wind * Wind.Direction.X; _windZ += delta * wind * Wind.Direction.Y;
        _windX %= CloudPeriod; _windZ %= CloudPeriod;
        double cx = Wrap(cameraWorldX + _windX), cz = Wrap(cameraWorldZ + _windZ);
        RenderingServer.GlobalShaderParameterSet("cloud_state", new Vector4((float)cx, (float)cz, Coverage, _storm));
        // Layers ride different winds: high cirrus slow on a veered heading, low clouds faster than the main deck.
        // High winds veer ~50° from the surface wind; low clouds race along it.
        var high = Wind.Direction.Rotated(.9f);
        _highX += delta * wind * .35 * high.X; _highZ += delta * wind * .35 * high.Y;
        _lowX += delta * wind * 1.9 * Wind.Direction.X; _lowZ += delta * wind * 1.9 * Wind.Direction.Y;
        // Wrap where each layer's pattern repeats exactly (see Sky.gdshader), keeping float precision with no jump.
        double highPeriod = CloudPeriod * 25, lowPeriod = CloudPeriod * .45 * 40;
        _highX %= highPeriod; _highZ %= highPeriod; _lowX %= lowPeriod; _lowZ %= lowPeriod;
        RenderingServer.GlobalShaderParameterSet("cloud_layers", new Vector4(
            (float)WrapTo(cameraWorldX + _highX, highPeriod), (float)WrapTo(cameraWorldZ + _highZ, highPeriod),
            (float)WrapTo(cameraWorldX + _lowX, lowPeriod), (float)WrapTo(cameraWorldZ + _lowZ, lowPeriod)));
        UpdateMood(delta);
        UpdateCloudTypes(delta, sky, cameraWorldX, cameraWorldZ, cameraLocal);
        UpdateMist(delta, hour, originX, originZ);
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
    }

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
            _rainParticles.DrawPass1 = new QuadMesh { Size = new Vector2(.015f, .6f), Material = new StandardMaterial3D
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
        _cloudTime = (_cloudTime + delta) % 100000;
        var force = ForceClouds;
        float cumulus = force?.X ?? Cumulus * (1 - _storm);
        float storm = force?.Y ?? DistantStorm * (1 - _storm * .9f);
        float mackerel = force?.Z ?? Mackerel * (1 - _storm);
        // Storms come from upwind and leave downwind; the bearing follows the wind slowly.
        var toward = StormComing ? -Wind.Direction : Wind.Direction;
        float az = Mathf.Atan2(toward.Y, toward.X);
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
        // Flat cloud bases ~1.5 km above the sea (never closer than 300 m above the camera on high ground).
        float baseAbove = Mathf.Clamp(SeaLevel + 1500 - cameraLocal.Y, 300, 2200);
        float windAngle = Mathf.Atan2(Wind.Direction.Y, Wind.Direction.X);
        RenderingServer.GlobalShaderParameterSet("cloud_more", new Vector4(mackerel, windAngle, (float)_cloudTime, baseAbove));
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
        _moon.LightEnergy = .14f * lit * moonUp * _night * Mathf.Lerp(1, .25f, _storm);
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
            : Mathf.Clamp((.7f * dawn + .5f * _mistBiome + .1f * _mistWater) * (1 - .65f * noon) + .35f * afterRain + .3f * _storm, 0, 1);
        _mistAmount = Mathf.MoveToward(_mistAmount, target, (float)delta / 6);
        if (float.IsNaN(_mistGround)) return;
        // Follow the ground slowly, so walking down into a valley wades into its mist before it settles around you.
        _mistGround = Mathf.Lerp(_mistGround, _mistGroundTarget, 1 - Mathf.Exp(-(float)delta / 8));
        var e = Environment;
        if (_mistAmount > .005f)
        {
            // Below this height everything takes fog by its height alone (Godot height fog): valleys, rivers, lakes.
            e.FogEnabled = true;
            e.FogHeight = _mistGround - 2;
            e.FogHeightDensity = .03f * _mistAmount * _mistAmount;
        }
        else e.FogHeightDensity = 0;
        // Patches: a volumetric fog box around the player (volumetric fog on for it, no global density).
        bool patches = mode == 2 && _mistAmount > .01f;
        if (patches && _mistVolume == null)
        {
            _mistMaterial = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/Mist.gdshader") };
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
            e.VolumetricFogDensity = GameSettings.VolumetricFog.On ? .0035f : 0;
        }
    }

    // ---- biome moods (option): the sky, haze and fog drift toward the biome around the player over a few seconds.
    private Vector4 _mood = new(1, 1, 1, 0), _moodTarget = new(1, 1, 1, 0);
    private double _highX, _highZ, _lowX, _lowZ;

    /// <summary>The biome blend around the player: sky tint (rgb) and haze (a), from the biome definitions.</summary>
    public void SetBiomeMood(Vector4 target) => _moodTarget = target;

    private void UpdateMood(double delta)
    {
        float strength = GameSettings.BiomeMoods.Index switch { 0 => 0, 1 => .6f, _ => 1.4f };
        var neutral = new Vector4(1, 1, 1, 0);
        var target = neutral.Lerp(_moodTarget, strength);
        _mood = _mood.Lerp(target, 1 - Mathf.Exp(-(float)delta / 3)); // ~3 s to settle when crossing into a biome
        RenderingServer.GlobalShaderParameterSet("sky_mood", _mood);
        var tint = new Color(_mood.X, _mood.Y, _mood.Z);
        // Fog takes the light of the hour: grey-blue by day, warm at a low sun, dim moonlit blue at night.
        float light = Mathf.Clamp(_daylight * 1.1f, 0, 1);
        var fog = new Color("aebfc2").Lerp(new Color("e0b090"), (1 - Mathf.Clamp(_sunElevation / 12, 0, 1)) * (1 - _night) * .5f);
        Environment.FogLightColor = (fog * tint * light).Lerp(new Color("0b0f18"), _night * .9f);
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

    private static Image WithMipmaps(Image image)
    {
        var copy = (Image)image.Duplicate();
        copy.GenerateMipmaps();
        return copy;
    }

    /// <summary>
    /// Tileable cloud noise: R = billowy base (4 octaves of periodic value noise from 4 to 32 cells), G = finer detail
    /// (8 to 64 cells). Deterministic; ~10 ms.
    /// </summary>
    private static Image CloudNoise(int size, uint seed)
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
