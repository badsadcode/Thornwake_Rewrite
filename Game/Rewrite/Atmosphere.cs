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
    private ShaderMaterial _sky = null!, _shafts = null!, _beams = null!, _curtains = null!, _air = null!;
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
    private MeshInstance3D? _shaftQuad, _beamQuad, _airQuad;
    // ---- raindrops on the lens (D217)
    private readonly ShaderMaterial _lensDrops = new() { Shader = GD.Load<Shader>("res://Game/Rewrite/LensDrops.gdshader"), RenderPriority = 126 };
    private MeshInstance3D? _lensQuad;
    private float _lensAmount, _lensClock;
    private bool _cameraUnder;
    private Vector3 _rainDir = Vector3.Down;
    /// <summary>Tests only (<c>--lens-test</c>): drops on the lens whatever the setting.</summary>
    private static readonly bool LensTest = Array.IndexOf(OS.GetCmdlineUserArgs(), "--lens-test") >= 0;

    /// <summary>
    /// Raindrops on the lens (setting Lens drops, off by default): more the harder it rains and the more you look into it
    /// (upwind and up), none under cover and fewer under trees, none under water; they build over a few seconds and dry
    /// away over ~8 s.
    /// </summary>
    private void UpdateLens(float dt)
    {
        if (_lensQuad == null) return;
        bool on = (GameSettings.LensDrops.Index == 1 || LensTest) && !Snowing && !_cameraUnder;
        float target = 0;
        if (on && Precip > .05f)
        {
            var cam = GetViewport()?.GetCamera3D();
            var fwd = cam != null ? -cam.GlobalBasis.Z : Vector3.Forward;
            float facing = Mathf.Clamp(.35f + .9f * fwd.Dot(-_rainDir), .15f, 1.2f);
            target = Mathf.Clamp(Mathf.SmoothStep(.05f, .8f, Precip) * facing * (1 - Sheltered) * (1 - .7f * Mathf.Clamp(Canopy, 0, 1)), 0, 1);
        }
        _lensAmount = target > _lensAmount ? Mathf.MoveToward(_lensAmount, target, dt / 3) : Mathf.MoveToward(_lensAmount, target, dt / 8);
        _lensQuad.Visible = _lensAmount > .005f;
        if (!_lensQuad.Visible) return;
        _lensClock += dt;
        _lensDrops.SetShaderParameter("amount", _lensAmount);
        _lensDrops.SetShaderParameter("clock", _lensClock);
    }

    private readonly ShaderMaterial _underwater = new() { Shader = GD.Load<Shader>("res://Game/Rewrite/Underwater.gdshader"), RenderPriority = 127 };
    private float _daylight = 1;
    private GpuParticles3D? _rainParticles;
    // Rain look by strength (D207): the streak shader, and splashes on the surfaces around the camera — spawned on a pool of
    // points on the ground, rocks, roofs and logs (SurfaceY), a few of them moved each frame.
    private ShaderMaterial? _rainStreakMat;
    private GpuParticles3D? _groundSplashes;
    private Image? _splashPointsImage;
    private ImageTexture? _splashPoints;
    private Image? _splashColorImage;
    private ImageTexture? _splashColors; // (r: how hard the surface is, D213)
    private int _splashPointNext;
    private readonly Random _splashRandom = new(77);
    private const int SplashPointCount = 1024;
    // Shelter (D211): the cover map — the first surface rain lands on, 1 m cells over 40 m around the camera, held
    // toroidally (cell x, z mod 40) with the cell it holds; 100 cells refreshed a frame (all within ~16 frames).
    private const int CoverN = 40;
    private Image? _coverImage;
    private ImageTexture? _coverTex;
    private int _coverNext;
    private (int, int)[]? _coverOrder;
    private int? _coverInner;
    private int _coverFar;
    private readonly float[] _coverData = new float[CoverN * CoverN * 4];
    private readonly byte[] _coverBytes = new byte[CoverN * CoverN * 16];
    private readonly int[] _coverX = new int[CoverN * CoverN], _coverZ = new int[CoverN * CoverN];
    private readonly bool[] _coverOk = new bool[CoverN * CoverN];
    private readonly System.Collections.Generic.List<Vector3> _edgeCells = new(); // roof and ledge edges (D216)
    private float _edgeTimer;
    /// <summary>0 in the open … 1 under cover at the camera (eased): the rain sound muffles (Lightning.Shelter).</summary>
    public float Sheltered { get; private set; }
    private float _dripSound;
    /// <summary>Under wet leaves (D213): 0 … 1, for the rain sound — patter on the leaves, drips after the rain.</summary>
    public float LeafDrip => _dripSound;
    private int _particlesLook = -1;
    // Trees in the rain (D213): the crown map (1 m cells, 48 x 48 around the camera: the underside of the crown over each
    // cell), the leaves' own wetness, and the drips falling from the crowns' undersides.
    private const int CanopyN = 48;
    private Image? _canopyImage;
    private ImageTexture? _canopyTex;
    private Vector2 _canopyOrigin;
    private readonly System.Collections.Generic.List<Vector3> _crownCells = new();
    private float _leafWet;
    private GpuParticles3D? _drips;
    private ShaderMaterial? _dripMat;
    private Image? _dripPointsImage;
    private ImageTexture? _dripPoints;
    private int _dripNext;
    private const int DripPointCount = 512;

    /// <summary>
    /// The crowns around the camera (D213; WorldLab, every half second): scene x, z, radius and the height of the crown's
    /// underside. Drawn into the crown map — the lowest underside where crowns overlap.
    /// </summary>
    public void SetCrowns(System.Collections.Generic.List<(float X, float Z, float R, float Low)> crowns, Vector3 camera)
    {
        _canopyImage ??= Image.CreateEmpty(CanopyN, CanopyN, false, Image.Format.Rgf);
        _canopyImage.Fill(new Color(0, 0, 0));
        _canopyOrigin = new Vector2(Mathf.Floor(camera.X) - CanopyN / 2, Mathf.Floor(camera.Z) - CanopyN / 2);
        _crownCells.Clear();
        foreach (var (x, z, r, low) in crowns)
        {
            int x0 = Mathf.FloorToInt(x - r - _canopyOrigin.X), x1 = Mathf.FloorToInt(x + r - _canopyOrigin.X);
            int z0 = Mathf.FloorToInt(z - r - _canopyOrigin.Y), z1 = Mathf.FloorToInt(z + r - _canopyOrigin.Y);
            for (int cz = Math.Max(z0, 0); cz <= Math.Min(z1, CanopyN - 1); cz++)
                for (int cx = Math.Max(x0, 0); cx <= Math.Min(x1, CanopyN - 1); cx++)
                {
                    float dx = cx + .5f + _canopyOrigin.X - x, dz = cz + .5f + _canopyOrigin.Y - z;
                    if (dx * dx + dz * dz > r * r) continue;
                    var old = _canopyImage.GetPixel(cx, cz);
                    if (old.G > .5f && old.R <= low) continue;
                    _canopyImage.SetPixel(cx, cz, new Color(low, 1, 0));
                }
        }
        for (int cz = 0; cz < CanopyN; cz++)
            for (int cx = 0; cx < CanopyN; cx++)
                if (_canopyImage.GetPixel(cx, cz) is var c && c.G > .5f) _crownCells.Add(new Vector3(cx + _canopyOrigin.X, c.R, cz + _canopyOrigin.Y));
        if (_canopyTex == null) _canopyTex = ImageTexture.CreateFromImage(_canopyImage);
        else _canopyTex.Update(_canopyImage);
    }

    /// <summary>Whether the crown map is wanted (WorldLab asks before gathering the trees).</summary>
    public bool WantsCrowns => GameSettings.RainTrees.Index == 1 && GameSettings.RainLook.Index >= 1 && (Precip > .02f || _leafWet > .01f) && !Snowing;
    /// <summary>The first surface straight down at a scene x, z (WorldLab: terrain, rocks, objects, buildings), for splashes.</summary>
    public Func<float, float, float>? SurfaceY { get; set; }
    /// <summary>The terrain's own height at a scene x, z (WorldLab): a surface above it is a roof, a rock, a log (D213:
    /// hard surfaces splash harder).</summary>
    public Func<float, float, float>? GroundY { get; set; }
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
    /// <summary>The land's mean height above the sea around the player (m, ~20 km across; WorldLab), for the cloud base.</summary>
    public float RegionalGround { get; set; }
    private float _regionalGround = float.NaN;

    /// <summary>
    /// The cloud base above the sea (m). Build 84: 1,500 m whatever the land, so in mountain worlds the deck sat at valley
    /// height or below (Tom: from a 3,060 m ridge the camera stood inside the cumulus). Above the land (D161): about
    /// 1,500 m over the region's mean ground — the condensation level rides on the land — eased over ~20 s as the region
    /// changes, so mountains of the region still reach into the deck while its valleys stay below it.
    /// </summary>
    public float CloudBaseAboveSea
    {
        get
        {
            float land = 1500 + (GameSettings.CloudBase.Index == 1 && !float.IsNaN(_regionalGround) ? _regionalGround * 0.9f : 0);
            float sea = CloudSea; // (0 when the region is unknown)
            return sea > 0 ? Mathf.Lerp(land, Math.Max(1500, _regionalGround + 350), sea) : land;
        }
    }

    /// <summary>
    /// Cloud sea (D169, draft; Tom: "the cloud sea seen from above: gaps, lit tops and light through it"): 0 = off, 1 = in
    /// high country (region ≥ ~1.5 km) the layer sits ~350 m over the region's ground and is a few hundred metres thick
    /// (SkyCommon.gdshaderinc), under the ridges and summits. Build 88's base over the land put it ~1.35 km over the
    /// region and 1–2 km thick: on Tom's 3,060 m ridge the camera stood in the bottom of the layer, every view down was
    /// the clouds' blue-grey underside.
    /// </summary>
    private float CloudSea => GameSettings.CloudSea.Index >= 1 && GameSettings.CloudBase.Index == 1 && GameSettings.CloudQuality.Index == 3
        && !float.IsNaN(_regionalGround) ? Mathf.SmoothStep(800, 1500, _regionalGround) : 0;

    private void UpdateRegionalGround(double delta)
    {
        float target = float.IsFinite(RegionalGround) ? Math.Max(RegionalGround, 0) : 0;
        _regionalGround = float.IsNaN(_regionalGround) || StillShot || SnapExposure ? target
            : Mathf.Lerp(_regionalGround, target, 1 - Mathf.Exp(-(float)delta / 20));
    }

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
    /// <summary>Wind wisps and biome motes around the camera (D190).</summary>
    public AmbientLife? Life { get; private set; }
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
        // (after the clouds drawn in front of the land, 99: in the mountains the deck sits at valley height and covered
        // the pass; the shadows of mountains and cloud gaps fall across those clouds too)
        _air = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/AirRays.gdshader"), RenderPriority = 101 };
        _air.SetShaderParameter("cloud_tex", _sky.GetShaderParameter("cloud_tex"));
        _air.SetShaderParameter("cloud_period", CloudPeriod);
        _air.SetShaderParameter("cloud_noise", _cloudNoise); // (the broken cloud sea's holes, D173)
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
        _airQuad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2, 2) }, MaterialOverride = _air, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384, Position = new Vector3(0, 0, -1), GIMode = GeometryInstance3D.GIModeEnum.Disabled, Visible = false,
        };
        camera.AddChild(_airQuad);
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
        // Raindrops on the lens (D217, optional): a full-screen pass, drawn only while drops are on.
        _lensQuad = new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(2, 2) }, MaterialOverride = _lensDrops, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            ExtraCullMargin = 16384, Position = new Vector3(0, 0, -1), GIMode = GeometryInstance3D.GIModeEnum.Disabled, Visible = false,
        };
        camera.AddChild(_lensQuad);
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
        // Living air (D190): wind wisps and biome motes around the camera.
        Life = new AmbientLife();
        camera.AddChild(Life);
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
        _rainStreakMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/RainStreak.gdshader") };
        var groundSplash = (ShaderMaterial)_splashMaterial.Duplicate();
        groundSplash.SetShaderParameter("ring_k", .45f);
        // (a first try let the drops collide with a height field and spawn splashes as a sub-emitter: none appeared)
        _splashPointsImage = Image.CreateEmpty(SplashPointCount, 1, false, Image.Format.Rgbf);
        _splashPoints = ImageTexture.CreateFromImage(_splashPointsImage);
        _splashColorImage = Image.CreateEmpty(SplashPointCount, 1, false, Image.Format.Rgba8);
        _splashColorImage.Fill(new Color(.35f, 1, 1, 1));
        _splashColors = ImageTexture.CreateFromImage(_splashColorImage);
        _groundSplashes = new GpuParticles3D
        {
            Amount = 3000, Lifetime = .3, Emitting = false, LocalCoords = false,
            VisibilityAabb = new Aabb(new Vector3(-1e5f, -1e4f, -1e5f), new Vector3(2e5f, 2e4f, 2e5f)), // (points in scene space)
            DrawPass1 = new QuadMesh { Size = new Vector2(.26f, .2f), Material = groundSplash },
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Points, EmissionPointTexture = _splashPoints, EmissionPointCount = SplashPointCount,
                EmissionColorTexture = _splashColors,
                Gravity = Vector3.Zero, InitialVelocityMin = 0, InitialVelocityMax = 0, Spread = 0, ScaleMin = .6f, ScaleMax = 1.2f,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_groundSplashes);
        // Rain in depth (D221): two cylinders of procedural rain around the camera, ~22 m and ~45 m out.
        var layerShader = GD.Load<Shader>("res://Game/Rewrite/RainLayers.gdshader");
        for (int i = 0; i < 2; i++)
        {
            float r = i == 0 ? 16 : 40; // (D222: the near one just past the drops' fade, ~16–22 m: no gap)
            var m = new ShaderMaterial { Shader = layerShader };
            m.SetShaderParameter("radius", r);
            m.SetShaderParameter("cell", i == 0 ? .1f : .085f); // (D221: denser — at .16 / .12 the layers hardly showed)
            m.SetShaderParameter("seed", 3.7f + i * 5.1f);
            m.SetShaderParameter("depth_scale", i == 0 ? 20f : 60f);
            _rainLayers[i] = new MeshInstance3D
            {
                // (tall: looking down a slope the 50 m one's bottom showed as an edge)
                Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r, Height = 160, RadialSegments = 64, Rings = 1, CapTop = false, CapBottom = false },
                MaterialOverride = m, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, TopLevel = true, Visible = false,
                ExtraCullMargin = 100, GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            };
            AddChild(_rainLayers[i]);
        }
        ApplyAll();
    }

    /// <summary>
    /// Camera under water: <paramref name="depth"/> metres below the surface (≤ 0 = above water). Kind picks the
    /// water's colour and clarity (sea clear blue-green, lakes greener, rivers greener and murkier).
    /// </summary>
    public void SetUnderwater(float depth, WorldGen.WaterKind kind)
    {
        bool under = depth > .02f;
        _cameraUnder = under; // (no lens drops under water, D217)
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
        e.VolumetricFogEnabled = GameSettings.VolumetricFog.On && FogAllowed;
        ApplyFogQuality();
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
        e.AdjustmentContrast = _baseContrast = natural ? 1.06f : 1;
        e.FogAerialPerspective = _baseAerial = Math.Clamp((natural ? .26f : .35f) * GameSettings.Haze.F, 0, 1);
        e.AdjustmentSaturation = _baseSaturation = GameSettings.Saturation.F * (natural ? 1.08f : 1);
        // Colour grading presets (D192): a lookup table after tone mapping; Natural = none (as before).
        e.AdjustmentColorCorrection = ColorGrading.Lut(GameSettings.Grading.Index, GameSettings.GradingStrength.F);
        RenderingServer.GlobalShaderParameterSet("leaf_translucency", GameSettings.LeafTranslucency.F);
        RenderingServer.CameraAttributesSetDofBlurQuality(RenderingServer.DofBlurQuality.Medium, true);
        SkyParam("quality", GameSettings.CloudQuality.Index);
        SkyParam("cloud_blend", GameSettings.CloudBlend.Index);
        SkyParam("cloud_look", GameSettings.CloudLook.Index);
        SkyParam("cloud_caps", GameSettings.CloudCaps.Index);
        SkyParam("cloud_shape", GameSettings.CloudShape.Index);
        SkyParam("terrain_shade", GameSettings.CloudTerrainShadow.Index);
        SkyParam("sea_broken", GameSettings.CloudSea.Index == 2 ? 1 : 0);
        SkyParam("cloud_body", GameSettings.CloudBody.Index);
        SkyParam("cloud_one_pass", GameSettings.CloudOnePass.Index);
        SkyParam("cloud_smooth", GameSettings.CloudSmooth.Index);
        SkyParam("layers_mode", GameSettings.CloudLayers.Index);
        SkyParam("cloud_cost", GameSettings.CloudCost.Index);
        SkyParam("cloud_soft", GameSettings.CloudSoft.Index);
        SkyParam("cloud_shading", GameSettings.CloudShading.Index);
        SkyParam("cloud_bases", GameSettings.CloudBases.Index);
        if (GameSettings.CloudFine.Index == 1 && _cloudFineNoise == null) { _cloudFineNoise = CloudNoise3D.CreateDetail(); SkyParam("cloud_fine_noise", _cloudFineNoise); }
        SkyParam("cloud_fine", _cloudFineNoise != null ? GameSettings.CloudFine.Index : 0);
        SkyParam("sea_tune", new Vector4(Dev("seahole", .5f), 0, 0, 0));
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
        RenderingServer.GlobalShaderParameterSet("ray_temporal", temporal ? 1f : 0f); // (the ray passes' moving dither, D162)
        RenderingServer.GlobalShaderParameterSet("wind_waves", GameSettings.WindWaves.F);
        if (_cloudsNearQuad != null) _cloudsNearQuad.Visible = v2;
        int pixels = 256 << GameSettings.CloudResolution.Index;
        if (pixels != _cloudPixels)
        {
            // Same pattern (periodic noise, same seed) with finer detail; the sky material keeps its uniforms.
            _cloudPixels = pixels;
            SkyParam("cloud_tex", ImageTexture.CreateFromImage(WithMipmaps(pixels == Tile ? _noise : CloudNoise(pixels, 4242))));
            _beams?.SetShaderParameter("cloud_tex", _sky.GetShaderParameter("cloud_tex"));
            _air?.SetShaderParameter("cloud_tex", _sky.GetShaderParameter("cloud_tex"));
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
        Environment.VolumetricFogAnisotropy = .65f + .1f * GameSettings.GodRayContrast.F; // (and every frame: UpdateForestRays)
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
    public void SetWetness(float wetness, float puddles) { _wetness = wetness; _puddles = puddles; _leafWet = wetness; } // (the leaves too, D213)
    public (float Wetness, float Puddles) Wetness => (_wetness, _puddles);

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
        // Sun showers (D216; Noor's notes §6: "warm sunlit terrain alongside rain"): rain from a sky that is mostly open — the
        // weather's own cover below ~0.6 in light or moderate rain, or the Weather test's switch: the storm's darkening,
        // the rain light's grey sheet and most of its veil stay away, the sun keeps shining; the rainbow comes (Sky).
        float showerTarget = ForceSunShower ? 1 : GameSettings.RainLight.Index == 1 && !float.IsNaN(WeatherClouds) && Precip > .02f && Precip < .65f
            ? Mathf.SmoothStep(.62f, .45f, WeatherClouds) : 0;
        _sunShower = Mathf.MoveToward(_sunShower, Snowing ? 0 : showerTarget, (float)delta / 6);
        _storm = Mathf.MoveToward(_storm, Mathf.Clamp(Precip * 1.15f, 0, 1) * (1 - .85f * _sunShower), (float)delta / 8); // ~8 s to cloud over or clear
        _rainK = Mathf.MoveToward(_rainK, Snowing ? 0 : Mathf.Clamp(Precip, 0, 1), (float)delta / 8); // (rain light, D210)
        OceanTime += delta;
        // Wet look: the ground darkens within ~30 s of rain and dries over ~6 min; puddles fill over ~2 min, last ~15 min.
        float dt = (float)delta;
        if (GameSettings.WetLook.Index == 1)
        {
            // Wet look by material (D212, Noor's Weather 2.0 notes §2, §10: wetness builds up over time and dries by sun,
            // wind and shade): drizzle wets too, slowly (full in ~4 min of drizzle, ~30 s of a downpour); drying ~3 min in
            // a sunny breeze, ~6 under cloud, ~15 at night (the materials then dry in turn: TerrainBody, Rock, ...).
            bool wets = Precip > .02f && !Snowing;
            float sunOnGround = Mathf.Clamp(_daylight, 0, 1) * (1 - _gloom);
            float dryK = .4f + 1.3f * sunOnGround + .05f * Math.Min(Wind.Speed, 20);
            _wetness = wets ? Mathf.Min(1, _wetness + dt / 40 * Precip * 1.6f) : Mathf.Max(0, _wetness - dt / 360 * dryK);
            _puddles = wets && Precip > .3f ? Mathf.Min(1, _puddles + dt / 120 * Precip * 1.5f) : Mathf.Max(0, _puddles - dt / 900 * dryK);
        }
        else
        {
            bool wetting = Precip > .1f && !Snowing;
            _wetness = wetting ? Mathf.Min(1, _wetness + dt / 30 * Precip * 1.5f) : Mathf.Max(0, _wetness - dt / 360);
            _puddles = wetting && Precip > .3f ? Mathf.Min(1, _puddles + dt / 120 * Precip * 1.5f) : Mathf.Max(0, _puddles - dt / 900);
        }
        _dripSound = GameSettings.RainTrees.Index == 1 ? _leafWet * Mathf.Clamp(Canopy * 1.5f, 0, 1) : 0;
        // (figures out in the weather: soak with the rain, dry like the leaves, D215)
        Character.AmbientWet = GameSettings.WetLook.Index == 1 ? _leafWet : 0;
        GameplayModels.SetWetness(GameSettings.WetLook.Index == 1 ? Mathf.SmoothStep(.15f, .45f, _wetness) : 0);
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
        float gloom = Math.Max(Math.Max(_storm, overcast * .7f), RainOvercast * .9f);
        // (rain light, D210: under drizzle's grey sheet the sun is mostly gone too — the sky closed but the land stayed sunlit)
        float stormLight = Math.Max(_storm, RainOvercast * .6f);
        _gloom = gloom;
        LightProfiles.Update(dt, StillShot || SnapExposure);
        var light = AtmosphereLight.Darken(AtmosphereLight.Compute(elevation, gloom, stormLight, Natural), elevation, gloom, Canopy);
        Sun.LightEnergy = light.SunEnergy;
        _daylight = Mathf.Clamp(elevation / 20, .08f, 1) * Mathf.Lerp(1, .45f, gloom);
        UpdateWaterSky(elevation);
        // Rain curtains: lit like the overcast (grey, dimmer at dusk and night), hanging from ~1.5 km above the sea.
        float lit = Mathf.Clamp(_daylight * 1.1f, .06f, 1);
        _curtains.SetShaderParameter("rain_color", new Color(.5f * lit, .53f * lit, .57f * lit));
        _curtains.SetShaderParameter("cloud_base", SeaLevel + CloudBaseAboveSea);
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
            if (Snowing != _particlesSnow || GameSettings.RainLook.Index != _particlesLook) SetParticleKind(Snowing);
            float p = Precip;
            _rainParticles.Emitting = p > .05f;
            // (D186: the streaks are unshaded — at night they stood out bright white; they take the scene's light)
            if ((_rainParticles.DrawPass1 as QuadMesh)?.Material is StandardMaterial3D rm)
            {
                _rainTint ??= rm.AlbedoColor;
                float l = Mathf.Clamp(_daylight * 1.1f, .06f, 1) * Mathf.Lerp(1, .35f, _night);
                rm.AlbedoColor = new Color(_rainTint.Value.R * l, _rainTint.Value.G * l, _rainTint.Value.B * l, _rainTint.Value.A);
            }
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
                int want = GameSettings.RainLook.Index >= 1 && !Snowing ? 24000 : 9000; // (D207: by strength, as many as the simulated model)
                if (_rainParticles.Amount != want) { _rainParticles.Amount = want; SetParticleKind(Snowing); }
                _rainParticles.AmountRatio = Mathf.Clamp(p * 1.1f, 0, 1) * Math.Clamp(GameSettings.RainStrength.F, .2f, 1);
            }
            // Rain slants with the wind; snow drifts with it.
            if (_rainParticles.ProcessMaterial is ParticleProcessMaterial pm)
                pm.Direction = new Vector3(Wind.Direction.X * Wind.Speed * slant, -1, Wind.Direction.Y * Wind.Speed * slant);
            if (!Snowing && _particlesLook >= 1) RainByStrength(p, cameraLocal);
            else if (_groundSplashes != null) _groundSplashes.Emitting = false;
            if (Snowing || _particlesLook < 1) foreach (var layer in _rainLayers) if (layer != null) layer.Visible = false;
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
        // (y: the wet look by material, D212)
        // (z: the ground stays dry under cover — the cover map is valid and Rain under cover is on, D214)
        RenderingServer.GlobalShaderParameterSet("rain_now", new Vector4(Snowing ? 0 : Mathf.Clamp(Precip, 0, 1), GameSettings.WetLook.Index,
            GameSettings.RainShelter.Index == 1 && _coverTex != null ? 1 : 0, 0));
        if (_steer == null) UpdateLightning(delta); // classic model; the simulated one has Lightning.cs
        else if (_flash != null) _flash.Visible = false;
        // Warmer light at a low sun (AtmosphereLight: build 47's single warm factor, or the colour curve by phase).
        Sun.LightColor = light.SunColor;
        UpdateMoonRays(sunDir);
        if (Life != null) Life.Tint = Sun.LightColor.Lerp(Environment.FogLightColor, .4f); // (wisps take the light's colour)
        UpdateLens((float)delta);
        Life?.Tick(delta, Wind.Direction, Wind.Speed, Wind.Gust, _daylight, Mathf.Clamp(Precip, 0, 1), Canopy);
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
        UpdateSunCover(delta, cx, cz, cameraLocal.Y - SeaLevel);
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
        RainLight();
        UpdateCloudTypes(delta, sky, cameraWorldX, cameraWorldZ, cameraLocal);
        UpdateRegionalGround(delta);
        RenderingServer.GlobalShaderParameterSet("cloud_region", GameSettings.CloudBase.Index == 1 && !float.IsNaN(_regionalGround) ? _regionalGround : 0f);
        RenderingServer.GlobalShaderParameterSet("cloud_sea", CloudSea);
        UpdateMist(delta, hour, originX, originZ);
        UpdateRainMist((float)delta, originX, originZ);
        UpdateAirLoad(delta, hour);
        UpdateForestRays(delta);
        RenderingServer.GlobalShaderParameterSet("sun_dir_world", toSun);
        // The real sun, also below the horizon (the light stops at −4°): twilight colours follow it (Sky.gdshader).
        var trueSun = GameClock.Direction(sky.SunElevation, sky.SunAzimuth);
        RenderingServer.GlobalShaderParameterSet("sun_true", new Vector4(trueSun.X, trueSun.Y, trueSun.Z, (float)sky.SunElevation));
        // Caustics are the sun's light focused by the waves: none at night, faint at dawn, weak under an overcast (Tom,
        // dump 214459: bright white nets on a pond bed with the sun 4° up). Build 74: always at full strength.
        float causticLight = GameSettings.CausticLight.Index == 0 ? 1
            : Mathf.SmoothStep(2, 30, (float)sky.SunElevation) * (1 - .85f * _storm) * (1 - .6f * _gloom);
        RenderingServer.GlobalShaderParameterSet("caustic_light", causticLight);
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
        _camera3DHeight = cameraLocal.Y;
        // The scene origin's world xz folded to 65,536 m: the terrain folds each pixel's own world position (D181).
        RenderingServer.GlobalShaderParameterSet("terrain_fold", new Vector2((float)(originX - Math.Floor(originX / 65536) * 65536), (float)(originZ - Math.Floor(originZ / 65536) * 65536)));
        UpdateAirRays(toSun, cameraWorldX, cameraWorldZ, originX, originZ);
        UpdateHeightHaze(cameraLocal.Y);
        Sun.LightVolumetricFogEnergy *= MoonRayBoost; // (D182: the haze's moonlight, after the forest and air passes set it)
        Sun.LightVolumetricFogEnergy *= 1 - .85f * _sunCover; // (D201: no glare in the haze with the sun behind a cloud)

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
    private Color _skyHorizon = new(.68f, .75f, .78f);

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
        _skyHorizon = Grey(horizon) * day;
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
    private float Coverage => Mathf.Lerp(float.IsNaN(WeatherClouds) ? GameSettings.Clouds.F : WeatherClouds, .97f, Math.Max(_storm, RainOvercast * .85f));

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

    private Color? _rainTint;

    // ---- rain light (D210)
    private float _rainK; // the rain here, eased like the storm (~8 s)
    private float _baseContrast = 1, _baseSaturation = 1, _baseAerial = .35f;
    /// <summary>How closed and soft the sky is from the rain alone (draft Rain light): already in drizzle.</summary>
    // (drizzle falls from a low grey sheet: closed already at ~0.15; a first try closing toward 0.3 left drizzle under a
    // broken, sunny sky)
    private float RainOvercast => GameSettings.RainLight.Index == 1 ? Mathf.SmoothStep(.01f, .15f, _rainK) * (1 - _sunShower) : 0;
    private float _sunShower;
    /// <summary>Tests (Esc → Weather test): the rain falls as a sun shower whatever the clouds (D216).</summary>
    public bool ForceSunShower { get; set; }

    /// <summary>
    /// Rain light by strength (D210; Tom: "when it's raining, especially during the downpour, the lighting conditions
    /// should change"; Noor's Weather 2.0 notes §6: drizzle a soft veil with visibility kept, heavy rain a strong loss of
    /// contrast at middle and long range, a downpour a substantial veil). On top of the storm's darkening (which starts only
    /// from moderate rain): drizzle closes the sky and softens the light (RainOvercast); a grey veil of depth fog grows
    /// with the rain — ~25 km of visibility in drizzle, ~4 km in moderate rain, ~1.3 km in a downpour; colours and
    /// contrast mute and the fog loses its sun glow and its blue aerial tint.
    /// </summary>
    private void RainLight()
    {
        var e = Environment;
        bool on = GameSettings.RainLight.Index == 1;
        float r = on ? _rainK * (1 - .65f * _sunShower) : 0; // (a sun shower: a light veil, the sun's glow kept)
        e.AdjustmentSaturation = _baseSaturation * (1 - .35f * r);
        e.AdjustmentContrast = _baseContrast * (1 - .1f * r);
        e.FogAerialPerspective = _baseAerial * (1 - .8f * r);
        if (r <= .001f) return;
        e.FogDensity += .0023f * Mathf.Pow(r, 1.5f);
        e.FogEnabled = true;
        e.FogSunScatter *= 1 - .8f * r * (1 - _sunShower);
    }

    /// <summary>
    /// Rain look by strength (D207, Tom: "the direction of raindrops should be determined by the wind … raindrops splashing
    /// on surfaces during heavy rain … small rain should look different"). From drizzle to a downpour: the fall speed
    /// (~3 m/s for drizzle drops, ~9.5 m/s for big ones), the drift with the wind (small drops follow it more), the streak's
    /// length (its movement in ~1/18 s), width and opacity, and how many; from moderate rain on, drops that hit the ground,
    /// rocks, roofs or leaves within ~28 m splash (a sub-emitter at the collision with a height field around the camera).
    /// </summary>
    private void RainByStrength(float p, Vector3 camera)
    {
        if (_rainParticles?.ProcessMaterial is not ParticleProcessMaterial pm || _rainStreakMat == null) return;
        float k = Mathf.SmoothStep(.05f, .75f, p); // 0 drizzle … 1 downpour
        // (with TAA or FSR 2 the hashed streak, which writes motion vectors: the transparent one faded to ~10 % — RainStreakTaa)
        bool temporal = GameSettings.AntiAliasing.Index is 2 or 4;
        // (D221: smooth — thin solid lines cut by a scissor, no dither grain; build 110 the hashed one)
        // (D223: refractive — a transparent water lens, with any anti-aliasing)
        var want = GD.Load<Shader>(GameSettings.RainLook.Index == 3 ? "res://Game/Rewrite/RainStreakRefract.gdshader" : !temporal ? "res://Game/Rewrite/RainStreak.gdshader"
            : GameSettings.RainLook.Index == 2 ? "res://Game/Rewrite/RainStreakSolid.gdshader" : "res://Game/Rewrite/RainStreakTaa.gdshader");
        if (_rainStreakMat.Shader != want) _rainStreakMat.Shader = want;
        float fall = Mathf.Lerp(3.2f, 9.5f, k);
        var drift = Wind.Direction * Wind.Speed * Mathf.Lerp(.85f, .65f, k);
        var v = new Vector3(drift.X, -fall, drift.Y);
        float speed = v.Length();
        var dir = v / speed;
        _rainDir = dir;
        // (each drop drawn along its own velocity, D208: a little spread in direction and speed so the lines are not parallel)
        pm.ParticleFlagAlignY = true;
        pm.Direction = dir; pm.Spread = 5; pm.Gravity = Vector3.Zero;
        pm.InitialVelocityMin = speed * .85f; pm.InitialVelocityMax = speed * 1.15f;
        // (drops are born through the whole volume, the volume upwind so the drifting drops fill the view; only ~16 m around
        // the camera — farther a drop is under a pixel, and spread over 52 m the near rain was too sparse to read)
        pm.EmissionBoxExtents = new Vector3(16, 7, 16);
        // (D209, Tom: "the rain's direction shouldn't matter to where the camera is looking": the emitter hangs on the camera,
        // and a particle's direction is in its emitter's frame — the drops' fall turned with the view. Now top level: it
        // follows the camera's position only, never its rotation.)
        _rainParticles.TopLevel = true;
        _rainParticles.GlobalTransform = new Transform3D(Basis.Identity, camera + new Vector3(-dir.X, 0, -dir.Z) * 4 + new Vector3(0, 2, 0));
        float sheets = .7f + .45f * Wind.Gust;
        _rainParticles.AmountRatio = Mathf.Clamp(Mathf.Pow(p, .45f) * sheets, 0, 1) * Math.Clamp(GameSettings.RainStrength.F / 1.6f, .12f, 1);
        float l = Mathf.Clamp(_daylight * 1.1f, .06f, 1) * Mathf.Lerp(1, .35f, _night);
        _rainStreakMat.SetShaderParameter("fall_dir", dir);
        _rainStreakMat.SetShaderParameter("streak_len", speed * .055f);
        // (wide and opaque enough to survive TAA, which smeared build 104's 1 cm, 20 % streaks to almost nothing)
        _rainStreakMat.SetShaderParameter("width", Mathf.Lerp(.008f, .022f, k));
        _rainStreakMat.SetShaderParameter("tint", new Color(.82f * l, .86f * l, .92f * l, Mathf.Lerp(.28f, .55f, k)));
        // (only while it rains or the leaves still drip: in dry weather the cover map cost ~0.2 ms a frame for nothing)
        if (p > .02f || _leafWet > .01f) UpdateCover(camera, dir);
        UpdateTrees(p, camera);
        bool splash = p > .3f && _groundSplashes != null && SurfaceY != null;
        UpdateRainLayers(p, k, fall, drift, l, camera);
        if (_groundSplashes == null) return;
        _groundSplashes.Emitting = splash;
        if (!splash) { _splashPointNext = 0; return; }
        // Splash points: 2–16 m around the camera (even over the area), on the surface rain lands on (the cover map, D211:
        // no rays of their own); 96 of the 1,024 moved per frame (all within ~0.2 s), the first time all at once. A splash
        // every ~0.6 s per point in a downpour.
        int n = _splashPointNext == 0 ? SplashPointCount : 96;
        for (int i = 0; i < n; i++)
        {
            int at = _splashPointNext++ % SplashPointCount;
            double r = Math.Sqrt(4 + _splashRandom.NextDouble() * (16 * 16 - 4)), a = _splashRandom.NextDouble() * Math.Tau;
            float x = camera.X + (float)(Math.Cos(a) * r), z = camera.Z + (float)(Math.Sin(a) * r);
            float y = SurfaceAt(x, z);
            if (float.IsNaN(y)) continue;
            _splashPointsImage!.SetPixel(at, 0, new Color(x, y + .01f, z));
            // Splash kinds by surface (D213, Noor's notes §3): hard surfaces (roofs, rocks, logs) throw a bright crown with
            // many droplets; on the ground the drop mostly sinks into grass and soil (a small, dull splash); in the woods
            // the leaf litter swallows more.
            int t = CoverSlot(Mathf.FloorToInt(x), Mathf.FloorToInt(z));
            float hard = _coverOk[t] && _coverData[t * 4 + 3] > 1.5f ? 1 : Canopy > .4f ? .1f : .35f;
            _splashColorImage!.SetPixel(at, 0, new Color(hard, 1, 1, 1));
        }
        if (_splashPointNext >= SplashPointCount * 1000) _splashPointNext = SplashPointCount;
        _splashPoints!.Update(_splashPointsImage);
        _splashColors!.Update(_splashColorImage);
        _groundSplashes.AmountRatio = Mathf.Clamp(Mathf.SmoothStep(.3f, 1f, p) * Math.Clamp(GameSettings.RainStrength.F / 1.6f, .3f, 1), .05f, 1);
        if (_groundSplashes.DrawPass1 is QuadMesh { Material: ShaderMaterial sm })
            sm.SetShaderParameter("tint", new Color(.8f * l, .84f * l, .88f * l));
    }

    /// <summary>
    /// The cover map (D211): each cell's first surface rain lands on, cast once when the cell comes into range (nearest
    /// first, up to 40 a frame while moving) and refreshed in the background (new pieces, felled trees), near you first.
    /// A first version cast 100 rays a frame and the splash points 96 more: ~3 ms of CPU a frame in a downpour.
    /// Also feeds the splash points (<see cref="SurfaceAt"/>), the streaks' shelter test and <see cref="Sheltered"/>.
    /// </summary>
    // ---- rain-mist gusts (D221)
    private FogVolume? _rainMist;
    private ShaderMaterial? _rainMistMat;
    private Vector2 _rainMistDrift;
    private float _rainMistAmount;

    /// <summary>
    /// Rain-mist gusts (setting, draft): a fog volume around the player (80 × 24 × 80 m) whose sheets of wet air move with
    /// the wind; from heavy rain on (≥ ~0.4, full in a downpour), stronger in gusts, halved under cover; eased over ~4 s.
    /// </summary>
    private void UpdateRainMist(float dt, double originX, double originZ)
    {
        float target = 0;
        if (GameSettings.RainMist.Index == 1 && Environment.VolumetricFogEnabled && !Snowing)
            target = Mathf.SmoothStep(.4f, .95f, Mathf.Clamp(Precip, 0, 1)) * (.45f + .55f * Wind.Gust) * (1 - .5f * Sheltered);
        _rainMistAmount = Mathf.MoveToward(_rainMistAmount, target, dt / 4);
        if (_rainMistAmount <= .002f) { if (_rainMist != null) _rainMist.Visible = false; return; }
        if (_rainMist == null)
        {
            _rainMistMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/RainMist.gdshader") };
            _rainMistMat.SetShaderParameter("noise3d", _cloudNoise);
            _rainMist = new FogVolume { Size = new Vector3(80, 24, 80), Material = _rainMistMat, Shape = RenderingServer.FogVolumeShape.Box };
            AddChild(_rainMist);
        }
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null) return;
        _rainMist.Visible = true;
        _rainMistDrift += Wind.Direction * Wind.Speed * dt;
        if (_rainMistDrift.LengthSquared() > 8192f * 8192f) _rainMistDrift = Vector2.Zero;
        var c = cam.GlobalPosition;
        float ground = GroundY?.Invoke(c.X, c.Z) ?? c.Y - 1.7f;
        _rainMist.GlobalPosition = new Vector3(c.X, ground + 10, c.Z);
        float l = Mathf.Clamp(_daylight * 1.1f, .06f, 1);
        _rainMistMat!.SetShaderParameter("amount", _rainMistAmount);
        _rainMistMat.SetShaderParameter("ground", ground);
        _rainMistMat.SetShaderParameter("origin", new Vector2((float)(originX % 8192), (float)(originZ % 8192)));
        _rainMistMat.SetShaderParameter("drift", _rainMistDrift);
        _rainMistMat.SetShaderParameter("wind_dir", Wind.Direction.Normalized());
        _rainMistMat.SetShaderParameter("tint", new Color(.86f * l + .1f, .88f * l + .1f, .9f * l + .1f));
        // (D222: in grey rain the haze light is dim and the mist vanished — Tom saw it only in sun showers; the overcast
        // lifts it to the brightness of the grey veil around it)
        _rainMistMat.SetShaderParameter("glow", Environment.FogLightColor * (1 + 3.5f * _gloom * (1 - _sunShower)));
    }

    // ---- rain in depth (D221)
    private readonly MeshInstance3D?[] _rainLayers = new MeshInstance3D?[2];
    private float _rainClock;

    /// <summary>
    /// The two rain layers (setting Rain in depth): at the camera, world-fixed (no rotation), their streaks falling at
    /// the rain's speed and leaning with the wind; the near one shows ~45 % of a downpour's columns, the far one finer and
    /// denser (~70 %), both fainter in drizzle and none below ~0.08 of rain.
    /// </summary>
    private void UpdateRainLayers(float p, float k, float fall, Vector2 drift, float light, Vector3 camera)
    {
        bool on = GameSettings.RainDepth.Index == 1 && p > .08f;
        _rainClock += (float)GetProcessDeltaTime();
        for (int i = 0; i < 2; i++)
        {
            if (_rainLayers[i] is not { } layer) continue;
            layer.Visible = on;
            if (!on) continue;
            layer.GlobalTransform = new Transform3D(Basis.Identity, camera);
            var m = (ShaderMaterial)layer.MaterialOverride;
            float amount = Mathf.SmoothStep(.08f, .9f, p) * (i == 0 ? .7f : .9f);
            m.SetShaderParameter("amount", amount);
            m.SetShaderParameter("fall", fall);
            m.SetShaderParameter("drift", drift);
            m.SetShaderParameter("clock", _rainClock);
            m.SetShaderParameter("streak_len", Mathf.Lerp(.5f, 1.4f, k) * (i == 0 ? 1 : 1.3f));
            m.SetShaderParameter("tint", new Color(.85f * light, .88f * light, .94f * light, Mathf.Lerp(.12f, .34f, k) * (i == 0 ? 1 : .8f)));
        }
    }

    private void UpdateCover(Vector3 camera, Vector3 dir)
    {
        bool on = GameSettings.RainShelter.Index == 1 && SurfaceY != null;
        _rainStreakMat!.SetShaderParameter("cover_on", on ? 1 : 0);
        if (SurfaceY == null) { Sheltered = 0; return; }
        if (_coverOrder == null)
        {
            var order = new System.Collections.Generic.List<(int, int)>();
            for (int dz = -CoverN / 2; dz < CoverN / 2; dz++)
                for (int dx = -CoverN / 2; dx < CoverN / 2; dx++) order.Add((dx, dz));
            _coverOrder = order.OrderBy(o => o.Item1 * o.Item1 + o.Item2 * o.Item2).ToArray();
            _coverImage = Image.CreateEmpty(CoverN, CoverN, false, Image.Format.Rgbaf);
            _coverTex = ImageTexture.CreateFromImage(_coverImage);
        }
        // (centred a little upwind, like the rain's volume)
        int cx = Mathf.FloorToInt(camera.X - dir.X * 4), cz = Mathf.FloorToInt(camera.Z - dir.Z * 4);
        bool changed = false;
        int rays = 0;
        foreach (var (dx, dz) in _coverOrder)
        {
            if (rays >= 40) break;
            int wx = cx + dx, wz = cz + dz, t = CoverSlot(wx, wz);
            if (_coverOk[t] && _coverX[t] == wx && _coverZ[t] == wz) continue;
            CoverWrite(t, wx, wz); rays++; changed = true;
        }
        // (background: 8 a frame within ~8 m — all within ~0.4 s at 60 FPS, so a roof just built shelters at once; 2 a frame
        // over the rest. With 6 a frame over all 1,600 a hut built beside you stayed rainy inside for seconds.)
        int inner = _coverInner ??= _coverOrder.Count(o => o.Item1 * o.Item1 + o.Item2 * o.Item2 <= 64);
        for (int k = 0; k < 10; k++)
        {
            var (dx, dz) = k < 8 ? _coverOrder[_coverNext++ % inner] : _coverOrder[_coverFar++ % _coverOrder.Length];
            CoverWrite(CoverSlot(cx + dx, cz + dz), cx + dx, cz + dz); changed = true;
        }
        if (_coverNext >= inner * 1000) _coverNext = 0;
        // Edges of roofs and ledges (D216, Noor's notes §5: "water drips at the edge of the rock", roof-edge drips): twice a
        // second, the hard covered cells whose neighbour is open or much lower; the drips fall just past their edge.
        if ((_edgeTimer -= (float)GetProcessDeltaTime()) <= 0)
        {
            _edgeTimer = .5f;
            _edgeCells.Clear();
            foreach (var (dx, dz) in _coverOrder)
            {
                if (dx * dx + dz * dz > 18 * 18) continue;
                int wx = cx + dx, wz = cz + dz, t = CoverSlot(wx, wz);
                if (!_coverOk[t] || _coverX[t] != wx || _coverZ[t] != wz || _coverData[t * 4 + 3] < 1.5f) continue;
                float h = _coverData[t * 4];
                foreach (var (nx, nz) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
                {
                    float hn = CoverCell(wx + nx, wz + nz);
                    if (float.IsNaN(hn) || hn > h - .5f) continue;
                    _edgeCells.Add(new Vector3(wx + .5f + nx * .55f, h - .05f, wz + .5f + nz * .55f));
                }
            }
        }
        if (_coverFar >= _coverOrder.Length * 1000) _coverFar = 0;
        if (changed)
        {
            Buffer.BlockCopy(_coverData, 0, _coverBytes, 0, _coverBytes.Length);
            _coverImage!.SetData(CoverN, CoverN, false, Image.Format.Rgbaf, _coverBytes);
            _coverTex!.Update(_coverImage);
            RenderingServer.GlobalShaderParameterSet("rain_cover", _coverTex); // (the terrain: dry under cover, D214)
        }
        _rainStreakMat.SetShaderParameter("cover_tex", _coverTex);
        // Under cover at the camera: a surface over it, here or a step around (a roof's edge does not snap the sound).
        int covered = 0;
        for (int dz = -1; dz <= 1; dz++)
            for (int dx = -1; dx <= 1; dx++)
                if (CoverCell(Mathf.FloorToInt(camera.X) + dx, Mathf.FloorToInt(camera.Z) + dz) is var h && !float.IsNaN(h) && h > camera.Y + .3f) covered++;
        Sheltered = on ? Mathf.MoveToward(Sheltered, covered / 9f, (float)GetProcessDeltaTime() * 2) : 0;
    }

    /// <summary>
    /// Trees in the rain (D213; Noor's Weather 2.0 notes §4: canopies intercept rainfall, water drips from branches and
    /// foliage, gusts shake loose clusters of droplets, dripping continues after rainfall stops and fades as the foliage
    /// dries). The leaves' wetness soaks up in ~1 min of rain and dries over ~2-6 min after it (sun and wind faster); the
    /// drips: larger, slower drops from random points under the crowns, more in gusts.
    /// </summary>
    // ---- footstep splashes (D215)
    private GpuParticles3D[]? _footSplashes;
    private int _footNext;

    /// <summary>
    /// A footfall at <paramref name="foot"/> (scene) (D215; Noor's notes §9: small splashes when moving through wet or
    /// shallow-water surfaces): on wet ground a little spray, in puddles and rain-soaked earth a proper crown; none on
    /// dry ground, under cover or in snow. <paramref name="puddleHere"/>: standing water under the foot (0..1).
    /// </summary>
    public void FootSplash(Vector3 foot, float puddleHere)
    {
        if (GameSettings.WetLook.Index != 1 || Snowing || _splashMaterial == null) return;
        float k = Math.Max(Mathf.SmoothStep(.35f, .8f, _wetness) * .55f, puddleHere);
        if (k < .1f) return;
        // (two one-shot emitters, one per foot, each restarted at its step: a manual emit from an idle emitter drew nothing)
        if (_footSplashes == null)
        {
            var mat = (ShaderMaterial)_splashMaterial.Duplicate();
            mat.SetShaderParameter("ring_k", .6f);
            _footSplashes = new GpuParticles3D[2];
            for (int f = 0; f < 2; f++)
            {
                _footSplashes[f] = new GpuParticles3D
                {
                    Amount = 1, Lifetime = .4, OneShot = true, Explosiveness = 1, Emitting = false, LocalCoords = false, TopLevel = true,
                    VisibilityAabb = new Aabb(new Vector3(-2, -1, -2), new Vector3(4, 2, 4)),
                    DrawPass1 = new QuadMesh { Size = new Vector2(.75f, .55f), Material = mat }, // (at 0.42 m they did not read)
                    ProcessMaterial = new ParticleProcessMaterial { Gravity = Vector3.Zero, InitialVelocityMin = 0, InitialVelocityMax = 0, Spread = 0 },
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                AddChild(_footSplashes[f]);
            }
        }
        float l = Mathf.Clamp(_daylight * 1.1f, .08f, 1);
        var e = _footSplashes[_footNext++ & 1];
        ((ShaderMaterial)((QuadMesh)e.DrawPass1).Material).SetShaderParameter("tint", new Color(.8f * l, .84f * l, .88f * l));
        var pm = (ParticleProcessMaterial)e.ProcessMaterial;
        float s = .6f + .6f * k;
        pm.ScaleMin = pm.ScaleMax = s;
        pm.Color = new Color(k > .5f ? 1 : .75f, 1, 1, 1); // (r: how hard the splash, RainSplash)
        e.GlobalTransform = new Transform3D(Basis.Identity, foot);
        e.Restart();
    }

    /// <summary>Standing water at a scene x, z on the land (0..1), approximate: the rain's puddles on flat ground.</summary>
    public float PuddleAt => _puddles;

    private void UpdateTrees(float p, Vector3 camera)
    {
        float dt = (float)GetProcessDeltaTime();
        bool on = GameSettings.RainTrees.Index == 1 && !Snowing;
        float sunOnGround = Mathf.Clamp(_daylight, 0, 1) * (1 - _gloom);
        float dryK = .4f + 1.3f * sunOnGround + .05f * Math.Min(Wind.Speed, 20);
        _leafWet = p > .02f ? Mathf.Min(1, _leafWet + dt / 60 * (.3f + p * 1.5f)) : Mathf.Max(0, _leafWet - dt / 240 * dryK);
        bool crowns = on && _canopyTex != null && _crownCells.Count > 0;
        bool edges = GameSettings.WetLook.Index == 1 && GameSettings.RainShelter.Index == 1 && !Snowing && _edgeCells.Count > 0;
        _rainStreakMat!.SetShaderParameter("canopy_on", crowns ? 1 : 0);
        if (crowns)
        {
            _rainStreakMat.SetShaderParameter("canopy_tex", _canopyTex);
            _rainStreakMat.SetShaderParameter("canopy_origin", _canopyOrigin);
        }
        if (_drips == null)
        {
            _dripMat = (ShaderMaterial)_rainStreakMat.Duplicate();
            _dripPointsImage = Image.CreateEmpty(DripPointCount, 1, false, Image.Format.Rgbf);
            _dripPoints = ImageTexture.CreateFromImage(_dripPointsImage);
            _drips = new GpuParticles3D
            {
                Amount = 700, Lifetime = 1.8, Emitting = false, LocalCoords = false, TopLevel = true,
                VisibilityAabb = new Aabb(new Vector3(-1e5f, -1e4f, -1e5f), new Vector3(2e5f, 2e4f, 2e5f)), // (points in scene space)
                DrawPass1 = new QuadMesh { Size = Vector2.One }, MaterialOverride = _dripMat,
                ProcessMaterial = new ParticleProcessMaterial
                {
                    EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Points, EmissionPointTexture = _dripPoints, EmissionPointCount = DripPointCount,
                    Direction = Vector3.Down, Spread = 4, InitialVelocityMin = .3f, InitialVelocityMax = 1.2f, Gravity = new Vector3(0, -9.8f, 0),
                    ParticleFlagAlignY = true,
                },
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_drips);
        }
        int sources = (crowns ? _crownCells.Count : 0) + (edges ? _edgeCells.Count * 6 : 0);
        float rate = sources > 0 ? _leafWet * (.55f + .45f * Wind.Gust) * Mathf.Clamp(sources / 400f, .25f, 1) : 0;
        _drips.Emitting = rate > .02f;
        if (!_drips.Emitting) return;
        _drips.GlobalTransform = Transform3D.Identity;
        _drips.AmountRatio = Mathf.Clamp(rate, 0, 1);
        // Drip points: under the crowns, at the underside or a little higher in the leaves; 64 moved a frame.
        int n = _dripNext == 0 ? DripPointCount : 64;
        for (int i = 0; i < n; i++)
        {
            float x, y, z;
            // (half the drips from the edges when there are both: by count the crowns took nearly all)
            if (crowns && (!edges || _splashRandom.NextDouble() < .5))
            {
                var cell = _crownCells[_splashRandom.Next(_crownCells.Count)];
                x = cell.X + (float)_splashRandom.NextDouble(); z = cell.Z + (float)_splashRandom.NextDouble();
                y = cell.Y + (float)_splashRandom.NextDouble() * 1.2f;
            }
            else
            {
                // (a roof's or a ledge's edge: along the cell's side, just below it)
                var edge = _edgeCells[_splashRandom.Next(_edgeCells.Count)];
                float along = (float)_splashRandom.NextDouble() - .5f;
                bool alongX = Mathf.Abs(edge.X - Mathf.Floor(edge.X) - .5f) < .1f;
                x = edge.X + (alongX ? along : 0); z = edge.Z + (alongX ? 0 : along); y = edge.Y;
            }
            _dripPointsImage!.SetPixel(_dripNext++ % DripPointCount, 0, new Color(x, y, z));
        }
        if (_dripNext >= DripPointCount * 1000) _dripNext = DripPointCount;
        _dripPoints!.Update(_dripPointsImage);
        // (the rain's own streak, shorter and a little heavier; landing on the cover map like the rain; not caught by crowns)
        _dripMat!.Shader = _rainStreakMat.Shader;
        // (a first try at 22 cm x 1.6 cm read as specks in the leaves)
        _dripMat.SetShaderParameter("streak_len", .34f);
        _dripMat.SetShaderParameter("width", .024f);
        _dripMat.SetShaderParameter("tint", (Color)_rainStreakMat.GetShaderParameter("tint") with { A = .5f });
        _dripMat.SetShaderParameter("cover_on", _rainStreakMat.GetShaderParameter("cover_on"));
        _dripMat.SetShaderParameter("cover_tex", _coverTex);
        _dripMat.SetShaderParameter("canopy_on", 0);
    }

    private static int CoverSlot(int wx, int wz) => ((wx % CoverN) + CoverN) % CoverN + (((wz % CoverN) + CoverN) % CoverN) * CoverN;

    private void CoverWrite(int t, int wx, int wz)
    {
        float h = SurfaceY!(wx + .5f, wz + .5f);
        bool hard = GroundY != null && h > GroundY(wx + .5f, wz + .5f) + .15f;
        _coverOk[t] = true; _coverX[t] = wx; _coverZ[t] = wz;
        // (a: 1 written on the ground, 2 on a hard surface — roof, rock, log; the shader only tests a > 0.5)
        _coverData[t * 4] = h; _coverData[t * 4 + 1] = wx; _coverData[t * 4 + 2] = wz; _coverData[t * 4 + 3] = hard ? 2 : 1;
    }

    /// <summary>The cover map's height of cell (wx, wz), NaN if it does not hold that cell yet.</summary>
    private float CoverCell(int wx, int wz)
    {
        int t = CoverSlot(wx, wz);
        return _coverOk[t] && _coverX[t] == wx && _coverZ[t] == wz ? _coverData[t * 4] : float.NaN;
    }

    /// <summary>The surface rain lands on at a scene x, z from the cover map: smooth between the four nearest cell centres
    /// where they agree (ground, a roof's slope), else the cell's own (a roof's edge); NaN where the map has no data yet.</summary>
    private float SurfaceAt(float x, float z)
    {
        float own = CoverCell(Mathf.FloorToInt(x), Mathf.FloorToInt(z));
        if (float.IsNaN(own)) return own;
        float fx = x - .5f, fz = z - .5f;
        int x0 = Mathf.FloorToInt(fx), z0 = Mathf.FloorToInt(fz);
        float tx = fx - x0, tz = fz - z0;
        float a = CoverCell(x0, z0), b = CoverCell(x0 + 1, z0), c = CoverCell(x0, z0 + 1), d = CoverCell(x0 + 1, z0 + 1);
        if (float.IsNaN(a + b + c + d)) return own;
        float lo = Math.Min(Math.Min(a, b), Math.Min(c, d)), hi = Math.Max(Math.Max(a, b), Math.Max(c, d));
        if (hi - lo > .6f) return own;
        return Mathf.Lerp(Mathf.Lerp(a, b, tx), Mathf.Lerp(c, d, tx), tz);
    }

    private void SetParticleKind(bool snow)
    {
        _particlesSnow = snow;
        _particlesLook = GameSettings.RainLook.Index;
        _rainTint = null;
        if (_rainParticles == null) return;
        var pm = (ParticleProcessMaterial)_rainParticles.ProcessMaterial;
        // (back to build 104's emitter; the rain look by strength sets its own each frame, D207)
        _rainParticles.MaterialOverride = null;
        pm.ParticleFlagAlignY = false;
        _rainParticles.TopLevel = false; // (build 104's emitter turns with the camera, as it did)
        _rainParticles.Transform = Transform3D.Identity;
        _rainParticles.Position = new Vector3(0, 14, 0);
        pm.EmissionBoxExtents = new Vector3(28, 3, 28);
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
            if (_particlesLook >= 1 && _rainStreakMat != null)
            {
                _rainParticles.DrawPass1 = new QuadMesh { Size = Vector2.One };
                _rainParticles.MaterialOverride = _rainStreakMat;
            }
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
            ? Mathf.Min(SeaLevel + CloudBaseAboveSea - cameraLocal.Y, 2200)
            : Mathf.Clamp(SeaLevel + CloudBaseAboveSea - cameraLocal.Y, 1000, 2200);
        float windAngle = Mathf.Atan2(Wind.Direction.Y, Wind.Direction.X);
        RenderingServer.GlobalShaderParameterSet("cloud_more", new Vector4(mackerel, windAngle, (float)_cloudTime, baseAbove));
        // The sun as the sky sees it, for the clouds drawn in front of the scene (CloudsNear.gdshader).
        // (the real sun, also while the moon drives the light, D182: the clouds handle the night themselves)
        var toLight = _trueSunDir;
        RenderingServer.GlobalShaderParameterSet("cloud_light", new Vector4(_trueSunColor.R, _trueSunColor.G, _trueSunColor.B, _trueSunEnergy));
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
            $"Sun behind clouds {F(_sunCover)}\n" +
            $"Air load {F(_airLoad)} (moisture {F(_airMoisture)}, dust {F(_airDust)}; climate {F(_airRain)} mm/yr; rays {(GameSettings.RayWeather.Index == 1 ? "by the weather" : "always")}; cloud base {F(CloudBaseAboveSea)} m above the sea (cloud sea {F(CloudSea)}), region {F(_regionalGround)} m)\n" +
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

    // ---- rays in open air (M2, D148): AirRays.gdshader over a sun-shadow height map built by WorldLab.
    private double _airX, _airZ;
    private float _airSize;

    /// <summary>1 = the camera sees the sun over the terrain, 0 = it stands in a mountain's shadow (WorldLab, from the
    /// sun-shadow map; 1 without it).</summary>
    public float TerrainSunAtCamera { get; set; } = 1;
    private float _camera3DHeight;

    /// <summary>Toward the sun (world directions), for the sun-shadow height map.</summary>
    public Vector3 SunDirection => Sun.GlobalTransform.Basis.Z.Normalized();

    /// <summary>The air map around (<paramref name="x"/>, <paramref name="z"/>), <paramref name="size"/> m wide:
    /// r = ground height, g = the height above which the air sees the sun over the terrain (RGF).</summary>
    public void SetAirMap(Image image, double x, double z, float size)
    {
        var texture = ImageTexture.CreateFromImage(image);
        _air.SetShaderParameter("air_map", texture);
        RenderingServer.GlobalShaderParameterSet("air_map", texture); // (the terrain's far mountain shadows, D154)
        _airX = x; _airZ = z; _airSize = size;
    }

    private void UpdateAirRays(Vector3 toSun, double cameraWorldX, double cameraWorldZ, double originX, double originZ)
    {
        if (_airQuad == null) return;
        bool contrast = GameSettings.AtmoContrast.Index == 1;
        RenderingServer.GlobalShaderParameterSet("air_frame", contrast && _airSize > 0
            ? new Vector4((float)(_airX - originX), (float)(_airZ - originZ), _airSize, Dev("farshadow", .7f)) : Vector4.Zero);
        // Mountains shade the clouds (D167): the cloud march reads the same sun-shadow map.
        // (D172: the broken cloud sea reads the map's ground heights too, to keep its clouds clear of the valley floors)
        bool cloudShadow = (GameSettings.CloudTerrainShadow.Index == 1 || GameSettings.CloudSea.Index == 2) && GameSettings.CloudQuality.Index == 3 && _airSize > 0;
        RenderingServer.GlobalShaderParameterSet("cloud_air", cloudShadow
            ? new Vector4((float)(cameraWorldX - _airX), (float)(cameraWorldZ - _airZ), _airSize, _camera3DHeight) : Vector4.Zero);
        int q = GameSettings.AirRays.Index;
        bool on = q > 0 && _airSize > 0 && toSun.Y > -.01f;
        // (D166, Tom: "draw the air rays before the clouds so clouds hide what is behind them"; 101 = after the clouds in
        // front of the land (99), build 87, which painted the hidden land's haze over a cloud sea)
        int priority = GameSettings.RaysBehindClouds.Index == 1 ? 98 : 101;
        if (_air.RenderPriority != priority) _air.RenderPriority = priority;
        _airQuad.Visible = on;
        // (the light beams under clouds are part of the air rays while they are on)
        if (_beamQuad != null) _beamQuad.Visible = !on && GameSettings.LightBeams.Value > 0 && GameSettings.Clouds.Value > 0;
        if (!on) return;
        _air.SetShaderParameter("light_direction", toSun);
        _air.SetShaderParameter("light_color", Sun.LightColor * Sun.LightEnergy * MoonAirBoost);
        _air.SetShaderParameter("map_frame", new Vector4((float)(cameraWorldX - _airX), (float)(cameraWorldZ - _airZ), _airSize, 0));
        // The pass works on Godot's distance haze as it is this frame (its density: how much of each view is haze).
        var e = Environment;
        // (by the weather: the haze comes from the air load, whatever the Distance haze setting, which only fades the distance)
        _air.SetShaderParameter("density", (RaysByWeather ? .00009f * Mathf.Lerp(.15f, 3.5f, _airLoad) : e.FogEnabled ? e.FogDensity : 0) * Dev("airdens", 1));
        // Environment contrast (D154): the pass is the haze, Godot's even fog keeps a thin remainder (its colour and
        // aerial perspective still blend the farthest land into the sky).
        _air.SetShaderParameter("own_haze", contrast && Dev("airown", 1) > 0 ? 1 : 0);
        // (haze in a shadow is lit by the whole sky: Godot's fog light mixed toward the sky's horizon colour by the
        // aerial perspective, as Godot's fog looks; the plain fog light made shaded far ridges dark violet)
        _air.SetShaderParameter("fog_color", (e.FogLightColor.SrgbToLinear() * e.FogLightEnergy).Lerp(_skyHorizon, Mathf.Clamp(e.FogAerialPerspective * Dev("airsky", 1), 0, 1)));
        _air.SetShaderParameter("own_far", Dev("airfar", 10000));
        if (contrast) e.FogDensity *= Dev("airkeep", .6f);
        // Godot's fog glows toward the sun on every pixel, a mountain in front of the sun too (Tom: "the light is going
        // through the mountain"): that glow moves into the pass, where only sunlit air gets it.
        _air.SetShaderParameter("sun_scatter", e.FogSunScatter * Dev("airsun", 1));
        _air.SetShaderParameter("sky_affect", e.FogSkyAffect * Dev("airskyglow", 1));
        e.FogSunScatter = 0;
        // The screen-space shafts smear the bright sky round the sun across whatever stands before it: off (the pass
        // draws the rays now). The near volumetric fog only knows the shadow map (450 m): dimmed while the camera stands
        // in a mountain's shadow by the sun-shadow map.
        if (_shaftQuad != null) _shaftQuad.Visible = false;
        Sun.LightVolumetricFogEnergy *= TerrainSunAtCamera;
        // Noor's notes (D154): haze in a shadow loses all of its sunlight (shaded slopes stay darker, light falls in
        // distinct shafts); valleys hold more haze, ridge tops less (the layers separate).
        _air.SetShaderParameter("shade", Dev("airshade", contrast ? 1f : .85f));
        _air.SetShaderParameter("valley_haze", Dev("airvalley", contrast ? 6f : 3f));
        // Sharper, stronger shafts (D150): lanes along ridge shadows, a crisper edge, a broad glow toward the sun.
        _air.SetShaderParameter("ray_noise", Dev("airnoise", 150));
        _air.SetShaderParameter("ray_size", Dev("airlane", 70));
        _air.SetShaderParameter("shadow_edge", Dev("airedge", 8));
        _air.SetShaderParameter("shaft_glow", Dev("airglow", .6f));
        // (D202: the sky's glow toward the sun by the air's haze; build 102 = full, broad)
        bool hazeGlow = GameSettings.SkyGlow.Index == 1;
        _air.SetShaderParameter("sky_glow", hazeGlow ? Mathf.Lerp(.12f, 1, Mathf.SmoothStep(.08f, .6f, _airLoad)) : 1f);
        _air.SetShaderParameter("sky_glow_pow", hazeGlow ? 10f : 3f);
        _air.SetShaderParameter("world_origin", new Vector2((float)originX, (float)originZ));
        UpdateLanePivot(toSun, cameraWorldX, cameraWorldZ, originX, originZ);
        _air.SetShaderParameter("cloud_base", SeaLevel + CloudBaseAboveSea);
        _air.SetShaderParameter("cloud_shade", Dev("aircloud", _shadow.Visible ? _shadowStrength : .62f));
        // (D172: under the broken cloud sea the clouds take nearly all the sun; light comes down through the gaps)
        _air.SetShaderParameter("sea_shade", GameSettings.CloudSea.Index == 2 ? CloudSea * Dev("airsea", .95f) : 0f);
        _air.SetShaderParameter("sea_hole", GameSettings.CloudSea.Index == 2 ? CloudSea * Dev("seahole", .5f) : 0f);
        _air.SetShaderParameter("sea_level", SeaLevel);
        _air.SetShaderParameter("steps", q switch { 1 => 24, 2 => 40, _ => 64 });
        // (developer tool, --ray-dev: airgain, airside, airsun, airshade, airdens, airhaze (haze height), airvalley, aircloud, airdebug)
        // (no dimming by the cloud cover as the light beams have: the clouds' shadows in the pass do that, and shafts
        // belong under broken cloud — Tom's reference 1)
        _air.SetShaderParameter("gain", Dev("airgain", 2.2f) * (1f - .9f * _storm) * GameSettings.RayStrength.F);
        _air.SetShaderParameter("side_scatter", Dev("airside", .8f));
        _air.SetShaderParameter("haze_height", Dev("airhaze", contrast ? 1150 : 1400));
        _air.SetShaderParameter("debug", (int)Dev("airdebug", 0));
    }

    // The air rays' lanes turn about a point near the player (D170): the air map's centre (moves after ~2.5 km), or the
    // camera before the first map. When it moves, the offset takes up the difference so the lanes stay where they are.
    private double _lanePivotX = double.NaN, _lanePivotZ;
    private Vector2 _laneOffset;

    private void UpdateLanePivot(Vector3 toSun, double cameraWorldX, double cameraWorldZ, double originX, double originZ)
    {
        double px = _airSize > 0 ? _airX : cameraWorldX, pz = _airSize > 0 ? _airZ : cameraWorldZ;
        var l = toSun.Normalized();
        var a = (Mathf.Abs(l.Y) > .99f ? Vector3.Right : l.Cross(Vector3.Up)).Normalized(); // (as AirRays.gdshader)
        var b = a.Cross(l);
        if (double.IsNaN(_lanePivotX)) { _lanePivotX = px; _lanePivotZ = pz; }
        else if (px != _lanePivotX || pz != _lanePivotZ)
        {
            var d = new Vector3((float)(px - _lanePivotX), 0, (float)(pz - _lanePivotZ));
            _laneOffset += new Vector2(d.Dot(a), d.Dot(b)); // (grows with the distance travelled: 1,000 km is 14,000 lanes, fine in float)
            _lanePivotX = px; _lanePivotZ = pz;
        }
        _air.SetShaderParameter("lane_mode", GameSettings.RayLanes.Index);
        _air.SetShaderParameter("lane_pivot", new Vector2((float)(_lanePivotX - originX), (float)(_lanePivotZ - originZ)));
        _air.SetShaderParameter("lane_offset", _laneOffset);
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
        _moonElevation = (float)sky.MoonElevation;
        _moon.Basis = Basis.LookingAt(-moonDir, Mathf.Abs(moonDir.Y) > .99f ? Vector3.Forward : Vector3.Up);
        // (light model 1: brighter moonlight, so a night is dark but readable)
        _moon.LightEnergy = .14f * lit * moonUp * _night * Mathf.Lerp(1, .25f, _storm) * (AtmosphereLight.Modern ? 1.5f : 1)
            * AtmosphereLight.NightLight; // (night darkness, D109)
        _moon.Visible = _moon.LightEnergy > .002f;
        RenderingServer.GlobalShaderParameterSet("night_sky", new Vector4(moonDir.X, moonDir.Y, moonDir.Z, (float)sky.MoonPhase));
        RenderingServer.GlobalShaderParameterSet("star_rotation", sky.Stars);
        RenderingServer.GlobalShaderParameterSet("sky_extra", new Vector4((float)(sky.Day + sky.Hour / 24), GameSettings.Planets.F, GameSettings.Stars.F, lit));
    }

    // ---- moonlit rays and shadows (D182, Tom: "for the volumetric lighting we need to do a similar thing at night,
    // when the moon is shining"). The moon light had no shadows, so nothing could cut shafts out of it. While the moon
    // is the main light the shadow-casting sun light takes its direction, colour and strength (forest rays, rays in
    // open air, tree and mountain shadows by moonlight) and the moon light is off. The sky reads its sun from its first
    // light: a sky-only copy of the real sun stands in for it, and the clouds get the real sun too.
    private DirectionalLight3D? _skySun;
    private Vector3 _trueSunDir = Vector3.Up;
    private Color _trueSunColor = Colors.White;
    private float _trueSunEnergy, _moonElevation;
    /// <summary>The haze's light while the moon is the light (D182): moonlight is ~6× weaker than daylight but the night
    /// exposure barely brightens, so its shafts were there but invisible; the haze takes this much more of it (the
    /// ground keeps the plain moonlight). Like a film's moonlight: shafts readable, not realistic.</summary>
    // (×6 made a blue wall with Tom's own ray settings; more under a canopy, where the shafts are narrow, D183. D188: tuned
    // at air load ~0.4; in denser air — after rain, Tom's dump 135336 at 0.79 — it glowed white round the moon, so the boost
    // backs off as the air thickens, and the canopy adds ×1 instead of ×2.)
    private float MoonRayBoost => MoonLit ? 1 + (Dev("moonfog", 2) * (1 + Dev("moonforest", 1) * _forest) - 1) * MoonDensityK : 1;
    private float MoonDensityK => Mathf.Clamp(Dev("moonref", .4f) / Math.Max(_airLoad, .01f), .25f, 1);
    /// <summary>The same for the rays in open air (D182): ×3 lit the whole night valley like dusk, ×8 blew it out.</summary>
    private float MoonAirBoost => MoonLit ? 1 + (Dev("moonair", 1.5f) - 1) * MoonDensityK : 1;
    /// <summary>How high the main light stands: the moon's elevation while it is the light (D182), else the sun's.</summary>
    private float LightElevation => MoonLit ? _moonElevation : _sunElevation;
    public string MoonInfo => $"moon {(_moon?.LightEnergy ?? 0):0.000} visible {_moon?.Visible} · main light {Sun.LightEnergy:0.000} moonlit {MoonLit} · toward {Sun.GlobalTransform.Basis.Z.Normalized()} · fog energy {Sun.LightVolumetricFogEnergy:0.00} density {Environment.VolumetricFogDensity:0.0000} forest {_forest:0.00} canopy {Canopy:0.00} exposure {Environment.TonemapExposure:0.00} vol fog {Environment.VolumetricFogEnabled}";
    /// <summary>True while the moon drives the main light (D182).</summary>
    public bool MoonLit { get; private set; }

    private void UpdateMoonRays(Vector3 sunDir)
    {
        _trueSunDir = sunDir; _trueSunColor = Sun.LightColor; _trueSunEnergy = Sun.LightEnergy;
        MoonLit = GameSettings.MoonRays.Index == 1 && _moon != null && _moon.Visible
            && Sun.LightEnergy * Mathf.Clamp(sunDir.Y * 8, 0, 1) < _moon.LightEnergy * 3;
        if (_skySun == null)
        {
            _skySun = new DirectionalLight3D { ShadowEnabled = false, Visible = false, SkyMode = DirectionalLight3D.SkyModeEnum.SkyOnly };
            AddChild(_skySun);
        }
        if (!MoonLit)
        {
            Sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightAndSky;
            _skySun.Visible = false;
            return;
        }
        _skySun.Basis = Sun.Basis; _skySun.LightColor = Sun.LightColor; _skySun.LightEnergy = Sun.LightEnergy;
        _skySun.Visible = true;
        Sun.SkyMode = DirectionalLight3D.SkyModeEnum.LightOnly;
        Sun.Basis = _moon!.Basis;
        Sun.LightColor = _moon.LightColor;
        Sun.LightEnergy = _moon.LightEnergy * Dev("moonk", 1);
        _moon.Visible = false;
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
    /// <summary>A still shot that keeps the fog's smoothing over frames, as in play (<c>--shot-live</c>): the torch's
    /// haze in Tom's dump 221438 only builds up with it.</summary>
    public bool LiveFog { get; set; }
    /// <summary>The mist banks' wind drift so far (dumps carry it: --mist-drift, so a Reproduce line has the same banks).</summary>
    public (double X, double Z) MistDrift { get => (_mistDriftX, _mistDriftZ); set { _mistDriftX = value.X; _mistDriftZ = value.Z; } }
    /// <summary>Mist amount forced by a test (<c>--mist=0..1</c>; never a setting).</summary>
    public float? MistOverride { get; set; }
    /// <summary>Tests: a mist bank everywhere (<c>--mist=amount,1</c>).</summary>
    public bool MistBankEverywhere { get; set; }

    /// <summary>0 open … 1 closed forest around the camera (WorldLab, for the exposure of light model 1).</summary>
    public float Canopy { get; set; }

    /// <summary>Jump to the exposure target on the next frame (teleports, A/B stills).</summary>
    public bool SnapExposure { get; set; }

    private float _baseExposure = 1, _adapted = 1, _gloom;

    /// <summary>The light state of the last frame, for logs (benchmark A/B stills).</summary>
    public string LightInfo { get; private set; } = "";
    private ImageTexture3D? _cloudNoise, _cloudFineNoise;
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

    private float _mistLift; // m the mist has lifted off the drying ground (D214)

    private void UpdateMist(double delta, float hour, double originX, double originZ)
    {
        int mode = GameSettings.GroundFog.Index;
        // Dawn mist peaks just after sunrise, a little at dusk (sunrise/sunset from the clock's daylight hours).
        float sunrise = 12 - _daylightHours / 2, sunset = 12 + _daylightHours / 2;
        float dawn = Mathf.Exp(-Mathf.Pow((hour - sunrise - .6f) / 1.6f, 2)) + .45f * Mathf.Exp(-Mathf.Pow((hour - sunset - 1) / 1.5f, 2));
        float afterRain = _wetness * (1 - _storm);
        // Mist burns off as the sun climbs: around midday only misty biomes, rain and wet ground keep some.
        float noon = Mathf.Exp(-Mathf.Pow((hour - 13f) / (_daylightHours / 4f), 2));
        _mistLift = 0;
        if (GameSettings.AfterRainMist.Index == 1)
        {
            // Mist after rain by conditions (D214; Noor's Weather 2.0 notes §10: "mist or low haze may linger where weather
            // conditions support it", her mood E: golden hour after rain, mist lifting). Wet ground breathes mist into calm,
            // cool air: wind carries it off (gone by ~12 m/s), the midday sun thins it, the woods hold it; as the sun dries
            // the ground (the last third of the wetness) the mist lifts off it — up to ~25 m — and thins.
            float calm = 1 - Mathf.SmoothStep(3, 12, Wind.Speed);
            float cool = 1 - .7f * noon * (1 - _gloom);
            float woods = 1 + .4f * Mathf.Clamp(Canopy, 0, 1);
            afterRain = Mathf.SmoothStep(.1f, .6f, _wetness) * (1 - _storm) * calm * cool * woods * 1.45f;
            float sunOnGround = Mathf.Clamp(_daylight, 0, 1) * (1 - _gloom);
            // (only once the rain has stopped: at the start of a rain the ground is barely wet too, and the mist hung as a
            // bank at tree height — a band across the view, D218)
            _mistLift = 25 * sunOnGround * (1 - Mathf.SmoothStep(.15f, .55f, _wetness)) * (_wetness > .02f && Precip < .02f ? 1 : 0);
        }
        float target = mode == 0 || float.IsNaN(_mistGround) ? 0
            : Mathf.Clamp((.7f * dawn + .5f * _mistBiome + .1f * _mistWater) * (1 - .65f * noon) + .35f * afterRain + .3f * _storm, 0, 1) * .75f;
        _mistAmount = MistOverride ?? Mathf.MoveToward(_mistAmount, target, (float)delta / 6);
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
            e.FogHeight = _mistGround - 4 + _mistLift; // (D214: after rain the mist lifts as the ground dries)
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
                if (cam != null) _mistVolume.GlobalPosition = new Vector3(cam.GlobalPosition.X, _mistGround + 15 + _mistLift * .6f, cam.GlobalPosition.Z);
                double drift = Wind.Speed * .08;
                _mistDriftX += delta * drift * Wind.Direction.X; _mistDriftZ += delta * drift * Wind.Direction.Y;
                _mistDriftX %= 8192; _mistDriftZ %= 8192;
                _mistMaterial.SetShaderParameter("amount", _mistAmount);
                _mistMaterial.SetShaderParameter("force_bank", MistBankEverywhere ? 1f : 0f);
                _mistMaterial.SetShaderParameter("ground", _mistGround);
                _mistMaterial.SetShaderParameter("origin", new Vector2((float)(originX % 8192), (float)(originZ % 8192)));
                _mistMaterial.SetShaderParameter("drift", new Vector2((float)_mistDriftX, (float)_mistDriftZ));
            }
            e.VolumetricFogEnabled = (GameSettings.VolumetricFog.On || patches) && FogAllowed;
            e.VolumetricFogTemporalReprojectionEnabled = !StillShot || LiveFog;
            e.VolumetricFogTemporalReprojectionAmount = GameSettings.FogSmoothing.F;
            e.VolumetricFogDensity = GameSettings.VolumetricFog.On ? (Natural ? .002f : .0035f) : 0;
        }
    }

    // ---- height-aware distance haze (D168, Tom: "clear up high, hazy in the valleys"). Godot's distance fog counts
    // only distance, so looking down from a 3 km ridge at land 8 km away fogged as if along the valley floor (a flat
    // blue-grey, D167). Real haze thins with height: above the region's ground the camera looks through thinner air,
    // so the distance fog thins with the camera's height over the land (to a quarter, falling to ~37 % per 1.4 km above
    // the first 200 m). Within ~10 km the air rays' own haze (environment contrast) is shaped per ray; this is the rest.
    // ---- sun behind clouds (D201; Tom: "the sun is always so bright … no shadows from the clouds, like the sun just
    // doesn't care about the clouds"). The cloud shadows were only a decal darkening the ground's colour; the sun shone on
    // at full strength on the player, plants, the haze and the glare. Now the cloud map is sampled where the camera's ray
    // toward the sun crosses the cloud layer: covered, the direct sunlight drops (shadows fade), and the haze takes no
    // glare. Eased over ~1.5 s, as a cloud edge passes.
    private float _sunCover;

    private void UpdateSunCover(double delta, double cx, double cz, float cameraAboveSea)
    {
        float target = 0;
        var toSun = _trueSunDir;
        if (GameSettings.SunClouds.Index == 1 && !MoonLit && toSun.Y > .02f && Coverage > .01f)
        {
            float layer = CloudBaseAboveSea + 700 - cameraAboveSea; // (mid deck above the camera)
            if (layer > 0)
            {
                double t = layer / Math.Max(toSun.Y, .08);
                double x = cx + toSun.X * t, z = cz + toSun.Z * t;
                float n = SampleNoise(x, z) * .8f + .5f * .2f, edge = 1 - Coverage;
                target = Mathf.SmoothStep(edge - .05f, edge + .2f, n) * (1 - .5f * _storm);
            }
        }
        _sunCover = StillShot || SnapExposure ? target : Mathf.MoveToward(_sunCover, target, (float)delta / 1.5f);
        if (!MoonLit) Sun.LightEnergy *= 1 - Dev("suncover", .78f) * _sunCover;
    }

    /// <summary>The cloud map's base channel at world xz (bilinear, wrapped over the cloud period).</summary>
    private float SampleNoise(double x, double z)
    {
        double u = x / CloudPeriod * Tile, v = z / CloudPeriod * Tile;
        int x0 = (int)Math.Floor(u), y0 = (int)Math.Floor(v);
        float fx = (float)(u - x0), fy = (float)(v - y0);
        float P(int a, int b) => _noiseBase[(((b % Tile) + Tile) % Tile) * Tile + ((a % Tile) + Tile) % Tile] / 255f;
        return Mathf.Lerp(Mathf.Lerp(P(x0, y0), P(x0 + 1, y0), fx), Mathf.Lerp(P(x0, y0 + 1), P(x0 + 1, y0 + 1), fx), fy);
    }

    private void UpdateHeightHaze(float cameraY)
    {
        if (GameSettings.HeightHaze.Index == 0 || float.IsNaN(_regionalGround)) return;
        float above = cameraY - (SeaLevel + _regionalGround);
        Environment.FogDensity *= Mathf.Max(Mathf.Exp(-Mathf.Max(above - Dev("hazefloor", 200), 0) / Dev("hazeheight", 1400)), .25f);
    }

    // ---- air load (D152, Tom: shafts "only where weather effects are met for them to naturally show up"): how much
    // moisture and dust the air holds here and now. Rays need a blocked sun and something in the air to catch the light;
    // with *Rays and haze: by the weather* the forest rays, the rays in open air and the near volumetric fog all scale
    // with this, so dry, windy noon air is crisp and a calm wet dawn fills with shafts.
    private float _airRain = 800, _airLoad = .5f, _airMoisture, _airDust;
    /// <summary>The climate's rain where the player is (mm a year): dry climates hold little moisture, raise dust.</summary>
    public void SetClimateRain(float mmPerYear) => _airRain = mmPerYear;
    /// <summary>0 crisp to 1 heavy haze (moisture and dust), for the ray effects.</summary>
    public float AirLoad => _airLoad;
    private static bool RaysByWeather => GameSettings.RayWeather.Index == 1;

    private void UpdateAirLoad(double delta, float hour)
    {
        float sunrise = 12 - _daylightHours / 2, sunset = 12 + _daylightHours / 2;
        // Morning haze after a cool night, burning off toward midday; a little again before sunset.
        float morning = Mathf.Exp(-Mathf.Pow((hour - sunrise - .8f) / 2.2f, 2)) + .45f * Mathf.Exp(-Mathf.Pow((hour - sunset + .5f) / 1.8f, 2));
        float calm = 1 - Mathf.SmoothStep(5, 13, Wind.SteadySpeed);
        float dry = Mathf.SmoothStep(900, 250, _airRain);
        float humid = Mathf.Clamp(.2f + .5f * _mistBiome + .3f * _mistWater + .25f * Mathf.Clamp(Canopy, 0, 1), 0, 1) * (1 - .75f * dry);
        // After rain the wet ground steams whatever the climate (Tom, build 82: "shafts show up after rain but they are very
        // weak"; dump 100445, wetness 0.42, gave a load of 0.31): half-dried ground still fills the air (square root).
        float afterRain = Dev("airwetk", .95f) * Mathf.Pow(Mathf.Clamp(_wetness, 0, 1), Dev("airwetp", .5f)) * (1 - _storm);
        _airMoisture = Mathf.Clamp(humid * (.2f + .9f * Mathf.Min(morning, 1)) + afterRain, 0, 1) * (.35f + .65f * calm);
        _airDust = dry * Mathf.SmoothStep(4, 11, Wind.SteadySpeed) * (1 - _wetness);
        // Moonlight draft (D183): the air grows damp through a calm night (the ground cools, dew and haze form), so the
        // moon has haze to light; at midnight the day's model had dry air and forest shafts by moonlight did not show.
        if (GameSettings.MoonRays.Index == 1)
            _airMoisture = Math.Max(_airMoisture, Dev("nightdamp", .4f) * _night * (.35f + .65f * calm) * (1 - .6f * dry) * (1 - _storm));
        // Under the broken cloud sea (D173) the valley air is damp (the sea is the top of a moist layer): more haze for
        // the light coming down through its gaps.
        if (GameSettings.CloudSea.Index == 2 && CloudSea > 0)
            _airMoisture = Math.Max(_airMoisture, Dev("seamoist", .35f) * CloudSea * Mathf.SmoothStep(-50, 150, SeaLevel + CloudBaseAboveSea - _camera3DHeight) * (1 - _storm));
        float target = Mathf.Clamp(_airMoisture + .8f * _airDust, 0, 1);
        _airLoad = StillShot || SnapExposure ? target : Mathf.MoveToward(_airLoad, target, (float)delta / 20);
    }

    // ---- forest rays (M2 shoot-out, fx.forest_rays): A = volumetric forest air, B = stronger screen-space shafts.
    private FogVolume? _forestAir;
    private ShaderMaterial? _forestAirMaterial;
    private float _forest;
    private bool _forestAirOn;
    private Vector3 _forestDrift;

    /// <summary>M2 tuning (developer tool, <c>--ray-dev=key=value,...</c>), over A and A2: size, depth (cells across and
    /// into the view), filter (0/1), length, spread, boost (sun energy into the forest air), aniso, density, ambient
    /// (ambient inject), gdens (global density).</summary>
    public System.Collections.Generic.Dictionary<string, float> RayDev { get; } = new();
    private float Dev(string key, float value) => RayDev.TryGetValue(key, out float v) ? v : value;

    /// <summary>Volumetric fog allowed by <c>perf.fog_quality</c> (D142: "off" for weak GPUs drops all of it).</summary>
    private static bool FogAllowed => GameSettings.FogQuality.Index > 0;

    /// <summary>The volumetric fog's cell grid from <c>perf.fog_quality</c> (build 75: project.godot's 96 × 96 × 96).</summary>
    private (int, int, bool) _fogGrid = (96, 96, true); // (project.godot)

    public void ApplyFogQuality()
    {
        var (size, depth) = GameSettings.FogQuality.Index switch { 1 => (64, 64), 3 => (160, 128), 4 => (256, 160), _ => (96, 96) };
        var grid = ((int)Dev("size", size), (int)Dev("depth", depth), Dev("filter", 1) > 0);
        if (grid == _fogGrid) return; // (ApplyAll runs on every setting change: only a real change reaches the renderer)
        _fogGrid = grid;
        RenderingServer.EnvironmentSetVolumetricFogVolumeSize(grid.Item1, grid.Item2);
        RenderingServer.EnvironmentSetVolumetricFogFilterActive(grid.Item3);
    }

    // A2 (M2 draft, D143): the forest air of A, tuned toward Unity HDRP's rays.
    private const float A2Density = .03f, A2Ambient = .15f, A2Length = 80, A2Spread = 3;
    // Forward scattering (Tom, build 76 dumps 232115/232214: the shaft vanished once the sun was off screen) is a setting;
    // less of it scatters less light toward the camera overall, so the sun's energy into the air rises as it falls
    // (0.75 → ×8 as build 76, 0.3 → ×20).
    private static float A2Aniso => GameSettings.RayScatter.F;
    // A high sun lets far more light through the crowns: at full boost the noon air went milky white (rays3 captures,
    // D146), so the boost eases from a 30° to a 65° sun, down to 35 %.
    private float A2Boost => (8 + (.75f - A2Aniso) * 26.7f) * Mathf.Lerp(1, .35f, Mathf.SmoothStep(30, 65, LightElevation));

    private void UpdateForestRays(double delta)
    {
        int mode = GameSettings.ForestRays.Index;
        bool a2 = mode == 3;
        if (RaysByWeather && GameSettings.VolumetricFog.On)
        {
            // The near air everywhere: thin when crisp, thick in a loaded air, so a pillar, a single tree or a building
            // cuts shafts too; dust warms its colour.
            Environment.VolumetricFogDensity = (Natural ? .002f : .0035f) * Mathf.Lerp(.25f, 3.5f, _airLoad);
            Environment.VolumetricFogAlbedo = Environment.VolumetricFogAlbedo.Lerp(new Color("d9b88c"), Mathf.Clamp(_airDust / Math.Max(_airLoad, .01f), 0, 1) * .6f);
        }
        // How much "forest by day" there is: the canopy around the player (0 when flying), the sun up, no storm.
        float target = mode == 0 ? 0 : Mathf.Clamp(Canopy, 0, 1) * Mathf.SmoothStep(-1, 8, LightElevation) * (1 - _storm);
        _forest = StillShot || SnapExposure ? target : Mathf.MoveToward(_forest, target, (float)delta / 3);
        if (mode == 2 && _shaftQuad != null)
        {
            // B: the screen-space shafts, up to 2.5× inside a closed forest (and on in forests even when the setting is 0).
            float baseShafts = Math.Max(GameSettings.SunShafts.F, .6f * _forest);
            _shafts.SetShaderParameter("intensity", baseShafts * (1 + 1.5f * _forest));
            _shaftQuad.Visible = baseShafts > 0;
        }
        bool air = mode is 1 or 3 && _forest > .01f && !float.IsNaN(_mistGround) && FogAllowed;
        // A2 changes the whole volumetric fog (its length, spread, scattering, sky light), so it fades in and out with the
        // forest; outside it (and with A) these are as in build 75. Set every frame: leaving the forest brings them back.
        float w = air && a2 ? _forest : 0;
        var e = Environment;
        e.VolumetricFogLength = Dev("length", Mathf.Lerp(180, A2Length, w));
        e.VolumetricFogDetailSpread = Dev("spread", Mathf.Lerp(2, A2Spread, w));
        e.VolumetricFogAnisotropy = Dev("aniso", Mathf.Lerp(.65f + .1f * GameSettings.GodRayContrast.F, A2Aniso, w));
        e.VolumetricFogAmbientInject = Dev("ambient", Mathf.Lerp(1, A2Ambient, w));
        if (air && RayDev.TryGetValue("gdens", out float g)) e.VolumetricFogDensity = g;
        if (!air) Sun.LightVolumetricFogEnergy = FogSunEnergy(1); // (every frame: the cloud cover changes)
        if (!air && _forestAirOn)
        {
            // Back to what the settings say (ApplyAll only runs when a setting changes).
            e.VolumetricFogEnabled = (GameSettings.VolumetricFog.On || (_mistVolume?.Visible ?? false)) && FogAllowed;
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
        float weather = RaysByWeather ? Mathf.Lerp(.1f, 1.4f, _airLoad) : 1;
        _forestAirMaterial.SetShaderParameter("density", Dev("density", a2 ? A2Density : .014f) * _forest * weather);
        _forestAirMaterial.SetShaderParameter("ground", _mistGround);
        _forestAirMaterial.SetShaderParameter("drift", _forestDrift);
        _forestAirMaterial.SetShaderParameter("albedo", new Color("ece6da").Lerp(Sun.LightColor, .25f));
        e.VolumetricFogEnabled = true;
        // Sun glare (Tom, build 80: "in the forest the sun is so bright, blinding; above the canopy it is normal"): the
        // extra sunlight in the forest air scatters forward, so straight toward the sun it piled up into a white glare
        // over the sun and the crowns. Softened: it eases off as the view turns toward the sun (to 20 % looking right
        // at it), so the shafts beside and across the view keep their strength. Build 80: as before.
        float glare = 1;
        if (GameSettings.ForestSunGlare.Index == 1 && cam != null)
        {
            float facing = (-cam.GlobalBasis.Z).Normalized().Dot(Sun.GlobalTransform.Basis.Z.Normalized());
            glare = 1 - Dev("glarecut", .8f) * Mathf.SmoothStep(.7f, .97f, facing);
        }
        Sun.LightVolumetricFogEnergy = FogSunEnergy(1 + Dev("boost", a2 ? A2Boost : 1.8f) * _forest * glare * (RaysByWeather ? Mathf.Lerp(.3f, 1.2f, _airLoad) : 1) * GameSettings.RayStrength.F);
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
