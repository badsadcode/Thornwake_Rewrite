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
    private const int Tile = 256;
    private ShaderMaterial _sky = null!, _shafts = null!;
    private Decal _shadow = null!;
    private Image _noise = null!;
    private ImageTexture _shadowTexture = null!;
    private double _windX, _windZ;
    private float _shadowCoverage = -1;
    private MeshInstance3D? _shaftQuad;
    private GpuParticles3D? _rainParticles;
    private byte[] _noiseBase = Array.Empty<byte>();
    /// <summary>Rain on (the water simulation's rain): the sky clouds over, light dims, haze thickens, rain falls.</summary>
    public bool Raining { get; set; }
    private float _storm; // 0 fair … 1 full storm, eased
    /// <summary>The waves the water shader draws (same parameters and clock), for swimming and boats.</summary>
    public OceanWaves Waves { get; } = new();
    /// <summary>Wave clock (s), shared with the shader through the global uniform <c>ocean_time</c>.</summary>
    public double OceanTime { get; private set; }
    private double _wavesState = -1;
    private float _wetness, _puddles; // cosmetic rain wetness of the ground and puddles (0..1)
    private ShaderMaterial? _water;

    public override void _Ready()
    {
        _noise = CloudNoise(Tile, 4242);
        var raw = _noise.GetData();
        _noiseBase = new byte[Tile * Tile];
        for (int i = 0; i < _noiseBase.Length; i++) _noiseBase[i] = raw[i * 4];
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

    /// <summary>The terrain water material: reflection quality follows the settings.</summary>
    public void SetWater(ShaderMaterial water) { _water = water; _wavesState = -1; ApplyAll(); }

    private void OnSetting(Setting s) => ApplyAll();

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
        _shafts.SetShaderParameter("intensity", GameSettings.SunShafts.F);
        if (_shaftQuad != null) _shaftQuad.Visible = GameSettings.SunShafts.Value > 0;
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
            if (vp.GetCamera3D() is { } camera) camera.Fov = GameSettings.Fov.F;
        }
        DisplayServer.WindowSetVsyncMode(GameSettings.VSync.On ? DisplayServer.VSyncMode.Enabled : DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = (int)GameSettings.MaxFps.Value;
    }

    /// <summary>
    /// Per frame: sun from the time of day, wind drift, cloud uniform, shadow decal placement.
    /// <paramref name="cameraWorldX"/>/<paramref name="cameraWorldZ"/> are absolute world metres; the scene uses a float
    /// origin at (<paramref name="originX"/>, <paramref name="originZ"/>).
    /// </summary>
    public void Update(double delta, float hour, double cameraWorldX, double cameraWorldZ, double originX, double originZ, Vector3 cameraLocal)
    {
        float elevation = Mathf.Sin((hour - 6) / 12 * Mathf.Pi) * 62;
        Sun.RotationDegrees = new Vector3(-Mathf.Max(elevation, -5), -35 + (hour - 12) * 12, 0);
        _storm = Mathf.MoveToward(_storm, Raining ? 1 : 0, (float)delta / 8); // ~8 s to cloud over or clear
        OceanTime += delta;
        // Wet look: the ground darkens within ~30 s of rain and dries over ~6 min; puddles fill over ~2 min, last ~15 min.
        float dt = (float)delta;
        _wetness = Raining ? Mathf.Min(1, _wetness + dt / 30) : Mathf.Max(0, _wetness - dt / 360);
        _puddles = Raining ? Mathf.Min(1, _puddles + dt / 120) : Mathf.Max(0, _puddles - dt / 900);
        RenderingServer.GlobalShaderParameterSet("rain_wetness", new Vector2(_wetness, _puddles));
        RenderingServer.GlobalShaderParameterSet("ocean_time", (float)(OceanTime % 100000));
        double seaState = Math.Clamp(GameSettings.SeaState.Value + _storm * .45, 0, 1);
        if (Math.Abs(seaState - _wavesState) > .01) UpdateWaves(seaState);
        Sun.LightEnergy = Mathf.Clamp(elevation / 25, 0.05f, 1.1f) * Mathf.Lerp(1, .3f, _storm);
        Environment.FogDensity = .00009f * GameSettings.Haze.F * Mathf.Lerp(1, 4, _storm);
        Environment.FogEnabled = GameSettings.Haze.Value > 0 || _storm > 0;
        // The sky's radiance map is not re-baked for the storm (global uniform), so dim the ambient light directly.
        Environment.AmbientLightEnergy = Mathf.Lerp(.55f, .38f, _storm);
        UpdateCoverage();
        if (_rainParticles != null)
        {
            _rainParticles.Emitting = _storm > .35f;
            _rainParticles.AmountRatio = Mathf.Clamp((_storm - .3f) / .7f, 0, 1) * Math.Clamp(GameSettings.RainStrength.F, .2f, 1);
        }
        // Warmer light at a low sun.
        float warm = 1 - Mathf.Clamp(elevation / 30, 0, 1);
        Sun.LightColor = new Color("fff3df").Lerp(new Color("ffc58a"), warm * .8f);
        var toSun = Sun.GlobalTransform.Basis.Z.Normalized(); // a directional light shines along -Z

        double wind = 9 * GameSettings.CloudSpeed.Value; // m/s
        _windX += delta * wind * .8; _windZ += delta * wind * .6;
        _windX %= CloudPeriod; _windZ %= CloudPeriod;
        double cx = Wrap(cameraWorldX + _windX), cz = Wrap(cameraWorldZ + _windZ);
        RenderingServer.GlobalShaderParameterSet("cloud_state", new Vector4((float)cx, (float)cz, Coverage, _storm));
        _shafts.SetShaderParameter("sun_direction", toSun);
        _shafts.SetShaderParameter("sun_color", Sun.LightColor);

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
        Waves.Configure(seaState, Math.Atan2(.6, .8)); // same wind as the clouds
        if (_water == null) return;
        var w = new Godot.Collections.Array<Vector4>();
        var q = new float[OceanWaves.Count];
        for (int i = 0; i < OceanWaves.Count; i++)
        {
            var (dx, dz, k, a) = Waves.Waves[i];
            w.Add(new Vector4((float)dx, (float)dz, (float)k, (float)a));
            q[i] = (float)(Waves.Choppiness / (k * Waves.WaveHeight * OceanWaves.Count + 1e-9));
        }
        _water.SetShaderParameter("waves", w);
        _water.SetShaderParameter("wave_q", q);
        _water.SetShaderParameter("wave_height", (float)Waves.WaveHeight);
        _water.SetShaderParameter("sea_state", (float)seaState);
    }

    /// <summary>Cloud cover in effect: the setting, closing in toward overcast while it rains.</summary>
    private float Coverage => Mathf.Lerp(GameSettings.Clouds.F, .97f, _storm);

    private void UpdateCoverage()
    {
        if (Math.Abs(_shadowCoverage - Coverage) > .03f || (_storm == 0 && Math.Abs(_shadowCoverage - Coverage) > 1e-4)) RebuildShadowTexture(Coverage);
    }

    private static double Wrap(double v) => v - Math.Floor(v / CloudPeriod) * CloudPeriod;

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
                float n = Fbm(u, v, 4, 4, 1);
                float b = 1 - MathF.Abs(2 * Fbm(u, v, 8, 3, 9) - 1);
                float baseValue = Math.Clamp((n * .75f + b * .25f - .2f) / .6f, 0, 1);
                float detail = Fbm(u, v, 8, 4, 21);
                int i = (y * size + x) * 4;
                data[i] = (byte)(baseValue * 255); data[i + 1] = (byte)(detail * 255); data[i + 2] = 0; data[i + 3] = 255;
            }
        return Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
    }
}
