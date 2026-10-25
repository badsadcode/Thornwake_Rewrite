using System;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Small living-world effects around the camera (D190; Tom: "when there's wind, some kind of trails of wind in the
/// direction around the player, not too often, maybe some flying particles, small and cheap … depending on the biome,
/// dust in the desert or the badlands"; Noor's ideas, docs/ATMOSPHERE_IDEAS_BACKLOG.md). Two GPU particle systems that
/// follow the camera, cosmetic and client-side (nothing synced):
/// <list type="bullet">
/// <item>wind wisps: a few long, faint streaks gliding along the wind, more in gusts, none in calm air;</item>
/// <item>biome motes: dust (desert, badlands, savanna), ash and embers (volcanic), snow (cold), pollen and seeds
/// (grassland, forests), spores and midges (swamps), leaves in gusts (broadleaf forests) — carried by the wind.</item>
/// </list>
/// The setting Living air (fx.ambient_life): 0 off, 1 subtle, 2 more.
/// </summary>
public partial class AmbientLife : Node3D
{
    private GpuParticles3D _wisps = null!, _motes = null!, _swooshes = null!;
    private ParticleProcessMaterial _wispPm = null!, _motePm = null!, _swooshPm = null!;
    private ShaderMaterial _swooshMat = null!;
    private StandardMaterial3D _wispMat = null!, _moteMat = null!, _leafMat = null!;
    private bool _leafy;
    private string _biome = "";
    private float _amount;
    private double _time;

    /// <summary>
    /// Leaves come in flurries (D191, Tom: "I don't want it to always be the same amount; sometimes they can not be showing
    /// up, kind of random"): a smooth random value over ~20–60 s — about half the time no leaves at all, then a flurry that
    /// builds and fades, strongest in gusts.
    /// </summary>
    private float Flurry(float gust)
    {
        double t = _time / 37;
        double n = .5 + .35 * Math.Sin(t * 1.0 + 1.3) + .25 * Math.Sin(t * 2.31 + 4.1) + .15 * Math.Sin(t * 5.17 + .7);
        float episode = Mathf.SmoothStep(.55f, .85f, (float)n);
        return episode * (.35f + .65f * gust);
    }

    /// <summary>The ground's height (scene y) at a scene x, z (WorldLab: the terrain sampler), for the dust devils.</summary>
    public Func<float, float, float>? GroundY { get; set; }

    // ---- dust devils (D194): now and then a whirling dust column some way off in dry, sunny country.
    private GpuParticles3D? _devil;
    private ShaderMaterial? _devilPm;
    private float _devilAge = -1, _devilLife, _devilWait = 20;
    private Vector3 _devilPos;
    private readonly Random _rng = new(97);
    /// <summary>Tests only (<c>--dust-devil</c>): one right away, ~70 m ahead.</summary>
    public static bool ForceDevil { get; set; }

    private void UpdateDevil(double delta, Vector2 windDir, float windSpeed, float daylight, float rain, MoteProfile p)
    {
        bool dusty = p.Devils && GameSettings.AmbientLife.Index > 0 && daylight > .6f && rain < .05f && windSpeed < 9;
        if (_devil == null)
        {
            _devilPm = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/DustDevil.gdshader") };
            var mat = (StandardMaterial3D)_moteMat.Duplicate();
            mat.AlbedoTexture = DotTexture();
            _devil = new GpuParticles3D
            {
                Amount = 1400, Lifetime = 9, LocalCoords = true, Emitting = false, TopLevel = true,
                VisibilityAabb = new Aabb(new Vector3(-20, -2, -20), new Vector3(40, 45, 40)),
                DrawPass1 = new QuadMesh { Size = new Vector2(.6f, .6f), Material = mat },
                ProcessMaterial = _devilPm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_devil);
        }
        var dir = new Vector3(windDir.X, 0, windDir.Y).Normalized();
        if (_devilAge < 0)
        {
            _devil.Emitting = false;
            if (!dusty || GroundY == null) return;
            _devilWait -= (float)delta;
            if (_devilWait > 0 && !ForceDevil) return;
            // a new one 50–160 m away, ahead of the camera more often than behind
            var cam = GetParent<Node3D>().GlobalTransform;
            var fwd = -cam.Basis.Z; fwd.Y = 0; fwd = fwd.LengthSquared() > 1e-4f ? fwd.Normalized() : Vector3.Forward;
            float a = ForceDevil ? .15f : (float)(_rng.NextDouble() - .5) * 2.2f, d = ForceDevil ? 70 : 50 + 110 * (float)_rng.NextDouble();
            if (ForceDevil) { ForceDevil = false; _devilAge = 3; }
            var off = fwd.Rotated(Vector3.Up, a) * d;
            _devilPos = cam.Origin + off;
            _devilPos.Y = GroundY(_devilPos.X, _devilPos.Z) - .5f;
            _devilLife = 18 + 20 * (float)_rng.NextDouble();
            if (_devilAge < 0) _devilAge = 0;
            _devil.Restart();
        }
        _devilAge += (float)delta;
        _devilPos += dir * Math.Max(windSpeed, 1.5f) * .45f * (float)delta;
        if (GroundY != null) _devilPos.Y = Mathf.Lerp(_devilPos.Y, GroundY(_devilPos.X, _devilPos.Z) - .5f, (float)delta * 2);
        _devil.GlobalPosition = _devilPos;
        float k = Mathf.SmoothStep(0, 4, _devilAge) * (1 - Mathf.SmoothStep(_devilLife - 6, _devilLife, _devilAge));
        _devil.Emitting = _devilAge < _devilLife - 4;
        _devilPm!.SetShaderParameter("strength", k);
        _devilPm.SetShaderParameter("lean", dir * (2 + windSpeed * .8f));
        _devilPm.SetShaderParameter("height", 22 + 18 * (_devilLife % 1));
        var c = p.Colour;
        ((StandardMaterial3D)((QuadMesh)_devil.DrawPass1).Material).AlbedoColor = new Color(c.R * .92f, c.G * .85f, c.B * .78f, .55f);
        if (_devilAge > _devilLife) { _devilAge = -1; _devilWait = 25 + 60 * (float)_rng.NextDouble(); }
    }

    /// <summary>Tests only (<c>--wisp-test</c>): every swoosh slot busy, in front of the camera at 10–25 m, for close captures.</summary>
    private static readonly bool WispTest = Array.IndexOf(OS.GetCmdlineUserArgs(), "--wisp-test") >= 0;

    /// <summary>The light's colour around the player (Atmosphere: the sun's, toward the fog's): the swooshes take it.</summary>
    public Color Tint { get; set; } = Colors.White;

    /// <summary>The biome around the player (its id) and how much of it (0..1).</summary>
    public void SetBiome(string id, float share) => _biome = share >= .5f ? id : _biome == "" ? id : _biome;

    public override void _Ready()
    {
        _wispMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, UseParticleTrails = true,
            ProximityFadeEnabled = true, ProximityFadeDistance = 2,
            AlbedoTexture = StreakTexture(),
        };
        _wispPm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(22, 3, 22),
            Gravity = Vector3.Zero, Spread = 4, InitialVelocityMin = 1, InitialVelocityMax = 1,
            ScaleMin = .7f, ScaleMax = 1.3f,
            ColorRamp = Ramp(new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0)),
            // (Noor's reference: long ribbons curving with the air — a slow, broad turbulence bends the trails)
            TurbulenceEnabled = true, TurbulenceNoiseStrength = 2.5f, TurbulenceNoiseScale = 14, TurbulenceNoiseSpeed = new Vector3(.2f, .1f, .2f),
        };
        _wisps = new GpuParticles3D
        {
            Amount = 14, Lifetime = 4, LocalCoords = false, Emitting = false, Position = new Vector3(0, 0, -6),
            VisibilityAabb = new Aabb(new Vector3(-50, -15, -50), new Vector3(100, 35, 100)),
            // Trails: each wisp a ribbon following its last ~0.9 s of path (a cross of two strips, seen from any side).
            TrailEnabled = true, TrailLifetime = .9,
            DrawPass1 = new RibbonTrailMesh { Shape = RibbonTrailMesh.ShapeEnum.Cross, Size = .06f, Sections = 12, SectionLength = .5f, SectionSegments = 2, Material = _wispMat },
            ProcessMaterial = _wispPm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_wisps);
        // Swooshes (D204, setting Wind wisps): the same kind of emitter, each particle a stroke drawn by WindWisp.gdshader.
        _swooshMat = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/WindWisp.gdshader") };
        _swooshPm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(24, 2.5f, 24),
            Gravity = Vector3.Zero, Spread = 6, InitialVelocityMin = 1, InitialVelocityMax = 1, LifetimeRandomness = .35,
        };
        _swooshes = new GpuParticles3D
        {
            Amount = 10, Lifetime = 2.6, LocalCoords = false, Emitting = false,
            VisibilityAabb = new Aabb(new Vector3(-60, -15, -60), new Vector3(120, 35, 120)),
            DrawPass1 = SwooshMesh(48), MaterialOverride = _swooshMat,
            ProcessMaterial = _swooshPm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_swooshes);
        _moteMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded, Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
            AlbedoTexture = DotTexture(),
        };
        _leafMat = (StandardMaterial3D)_moteMat.Duplicate();
        _leafMat.AlbedoTexture = LeafTexture();
        _motePm = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(9, 3, 9),
            Spread = 180, InitialVelocityMin = .1f, InitialVelocityMax = .4f,
            TurbulenceEnabled = true, TurbulenceNoiseStrength = 1.2f, TurbulenceNoiseScale = 3,
            ColorRamp = Ramp(new Color(1, 1, 1, 0), new Color(1, 1, 1, 1), new Color(1, 1, 1, 0)),
            AngleMin = 0, AngleMax = 360, AngularVelocityMin = -90, AngularVelocityMax = 90,
        };
        _motes = new GpuParticles3D
        {
            Amount = 160, Lifetime = 6, LocalCoords = false, Emitting = false, Position = new Vector3(0, 0, -4),
            VisibilityAabb = new Aabb(new Vector3(-30, -10, -30), new Vector3(60, 25, 60)),
            DrawPass1 = new QuadMesh { Size = new Vector2(.09f, .09f), Material = _moteMat },
            ProcessMaterial = _motePm, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_motes);
    }

    /// <summary>Per frame: wind (direction toward, speed m/s, gust 0..1), daylight 0..1, rain 0..1, whether the camera is
    /// in a forest (canopy 0..1).</summary>
    public void Tick(double delta, Vector2 windDir, float windSpeed, float gust, float daylight, float rain, float canopy)
    {
        _time += delta;
        int level = GameSettings.AmbientLife.Index;
        float target = level == 0 ? 0 : level == 1 ? 1 : 2;
        _amount = Mathf.MoveToward(_amount, target, (float)delta * .5f);
        var dir = new Vector3(windDir.X, 0, windDir.Y).Normalized();
        float light = Mathf.Clamp(daylight * 1.2f, .08f, 1);

        // Wind wisps: from ~4 m/s, more with the gusts; almost none in the rain (the rain shows the wind) or under a canopy.
        float windy = Mathf.SmoothStep(3.5f, 11, windSpeed) * (.45f + .55f * gust) * (1 - .8f * rain) * (1 - .7f * Mathf.Clamp(canopy, 0, 1));
        float wispK = windy * Math.Min(_amount, 1.5f);
        bool swoosh = GameSettings.WindWisps.Index >= 1;
        _wisps.Emitting = wispK > .03f && !swoosh;
        _swooshes.Emitting = wispK > .03f && swoosh;
        if (_swooshes.Emitting)
        {
            _swooshes.AmountRatio = WispTest ? 1 : Mathf.Clamp(wispK * .7f, 0, 1);
            // (the stroke draws itself at about the wind's speed; the particle only drifts a little)
            _swooshPm.Direction = dir;
            _swooshPm.InitialVelocityMin = windSpeed * .15f; _swooshPm.InitialVelocityMax = windSpeed * .3f;
            _swooshes.Position = -dir * 8 + new Vector3(0, .8f, 0);
            if (WispTest)
            {
                var cam = GetParent<Node3D>().GlobalTransform;
                var fwd = -cam.Basis.Z; fwd.Y = 0; fwd = fwd.Normalized();
                _swooshPm.EmissionBoxExtents = new Vector3(6, 1.5f, 6);
                _swooshes.GlobalPosition = cam.Origin + fwd * 16 - dir * 5 + new Vector3(0, 1.5f, 0);
            }
            _swooshMat.SetShaderParameter("wind_dir", dir);
            _swooshMat.SetShaderParameter("stroke_len", Mathf.Clamp(windSpeed * .9f, 5, 13));
            bool strands = GameSettings.WindWisps.Index == 2;
            _swooshMat.SetShaderParameter("strands", strands ? 1 : 0);
            _swooshMat.SetShaderParameter("width", strands ? .65f : .3f); // (room for the haze and the strands around it)
            // (unshaded, tinted by the light: lit by the scene they did not draw at all, and plain white was a cool grey in an
            // orange evening)
            _swooshMat.SetShaderParameter("colour", new Color(Tint.R * light, Tint.G * light, Tint.B * light, .24f + .1f * gust));
        }
        if (_wisps.Emitting)
        {
            _wisps.AmountRatio = Mathf.Clamp(wispK * .7f, 0, 1);
            _wispPm.Direction = dir + new Vector3(0, .03f, 0);
            float v = windSpeed * 1.1f;
            _wispPm.InitialVelocityMin = v * .85f; _wispPm.InitialVelocityMax = v * 1.15f;
            _wisps.Position = -dir * 10 + new Vector3(0, -.5f, 0); // (upwind of the camera, so they pass through the view)
            _wispMat.AlbedoColor = new Color(.92f * light, .95f * light, 1f * light, .085f + .05f * gust);
        }

        // Biome motes.
        var p = Profile(_biome);
        UpdateDevil(delta, windDir, windSpeed, daylight, rain, p);
        UpdateAccent(delta, windDir, windSpeed, daylight, rain);
        float moteK = p.Amount * Math.Min(_amount, 2) * (1 - .85f * rain) * (p.NeedsWind ? Mathf.SmoothStep(2, 8, windSpeed) : 1)
            * (p.NightToo ? 1 : Mathf.SmoothStep(.15f, .5f, daylight)) * (p.Leaves ? Flurry(gust) * 1.6f : 1);
        _motes.Emitting = moteK > .03f;
        if (_motes.Emitting)
        {
            _motes.AmountRatio = Mathf.Clamp(moteK * .5f, 0, 1);
            _motePm.Gravity = new Vector3(0, p.Gravity, 0) + dir * windSpeed * p.WindFollow;
            _motePm.Direction = dir;
            _motePm.ScaleMin = p.Size * .6f; _motePm.ScaleMax = p.Size * 1.4f;
            _motePm.TurbulenceNoiseStrength = p.Swirl;
            var c = p.Glow ? p.Colour : new Color(p.Colour.R * light, p.Colour.G * light, p.Colour.B * light, p.Colour.A);
            if (p.Leaves != _leafy) { _leafy = p.Leaves; ((QuadMesh)_motes.DrawPass1).Size = p.Leaves ? new Vector2(.22f, .22f) : new Vector2(.09f, .09f); ((QuadMesh)_motes.DrawPass1).Material = p.Leaves ? _leafMat : _moteMat; }
            (_leafy ? _leafMat : _moteMat).AlbedoColor = c;
            _motes.Position = -dir * Math.Min(windSpeed, 8) * .8f + new Vector3(0, p.Height, -2);
        }
    }

    private readonly record struct MoteProfile(float Amount, Color Colour, float Size, float Gravity, float WindFollow,
        float Swirl, float Height, bool NeedsWind, bool NightToo, bool Glow, bool Leaves = false, bool Devils = false);

    private static MoteProfile Profile(string biome) => biome switch
    {
        "desert" or "badlands" or "needle-rocks" or "savanna" =>
            new(1.6f, new Color(.98f, .86f, .66f, .7f), 2.0f, -.05f, .35f, 1.6f, .3f, true, false, false, false, true), // dust (paler than the ground: it catches the light); dust devils
        "volcanic" =>
            new(1.0f, new Color(.55f, .52f, .5f, .6f), 1.2f, -.12f, .2f, 1.0f, 1.5f, false, true, false),          // ash
        "arctic" or "tundra" or "alpine" =>
            new(.9f, new Color(.95f, .97f, 1f, .7f), 1.3f, -.35f, .45f, 1.2f, 3f, true, true, false),              // blown snow
        "swamp" or "blighted-swamp" or "marsh" or "fen" or "bayou" =>
            new(.8f, new Color(.78f, .82f, .6f, .5f), .7f, .02f, .05f, 2.2f, .2f, false, false, false),            // spores, midges
        "jungle" =>
            new(.7f, new Color(.9f, .9f, .7f, .45f), .7f, .01f, .08f, 1.8f, 1f, false, false, false),
        "temperate-forest" or "ancient-forest" =>
            new(.8f, new Color(.95f, .62f, .25f, .95f), 1.3f, -.35f, .45f, 1.6f, 3f, true, false, false, true),     // leaves in gusts (Noor's reference)
        "taiga" =>
            new(.5f, new Color(.95f, .93f, .82f, .45f), .7f, -.02f, .15f, 1.4f, 1f, false, false, false),
        _ =>
            new(.6f, new Color(1f, .97f, .85f, .5f), .7f, .0f, .2f, 1.4f, .5f, false, false, false),               // pollen, seeds
    };

    private static ArrayMesh CrossedStreak(float width, float length, Material material)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Quad(Vector3 side)
        {
            Vector3 a = -side * width / 2 + Vector3.Down * length / 2, b = side * width / 2 + Vector3.Down * length / 2;
            Vector3 c = side * width / 2 + Vector3.Up * length / 2, d = -side * width / 2 + Vector3.Up * length / 2;
            st.SetUV(new Vector2(0, 1)); st.AddVertex(a); st.SetUV(new Vector2(1, 1)); st.AddVertex(b); st.SetUV(new Vector2(1, 0)); st.AddVertex(c);
            st.SetUV(new Vector2(0, 1)); st.AddVertex(a); st.SetUV(new Vector2(1, 0)); st.AddVertex(c); st.SetUV(new Vector2(0, 0)); st.AddVertex(d);
        }
        Quad(Vector3.Right); Quad(Vector3.Back);
        st.SetMaterial(material);
        return st.Commit();
    }

    // A strip for one swoosh: UV.x along it (0..1), UV.y the side (0 / 1); WindWisp.gdshader bends and widens it.
    private static ArrayMesh SwooshMesh(int segments)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < segments; i++)
        {
            float a = (float)i / segments, b = (float)(i + 1) / segments;
            void V(float s, float side) { st.SetUV(new Vector2(s, side)); st.AddVertex(new Vector3(s, side, 0)); }
            V(a, 0); V(b, 0); V(b, 1);
            V(a, 0); V(b, 1); V(a, 1);
        }
        return st.Commit();
    }

    private static GradientTexture1D Ramp(Color a, Color b, Color c)
    {
        var g = new Gradient();
        g.SetColor(0, a); g.SetColor(1, c); g.AddPoint(.3f, b); g.AddPoint(.7f, b);
        return new GradientTexture1D { Gradient = g };
    }

    // A soft vertical streak (bright core, fading at the ends and edges) and a soft round dot.
    private static ImageTexture StreakTexture()
    {
        var img = Image.CreateEmpty(8, 64, false, Image.Format.Rgba8);
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 8; x++)
            {
                float u = (x + .5f) / 8 * 2 - 1, v = (y + .5f) / 64 * 2 - 1;
                float a = (1 - u * u) * (1 - v * v) * (1 - v * v);
                img.SetPixel(x, y, new Color(1, 1, 1, Math.Max(a, 0)));
            }
        return ImageTexture.CreateFromImage(img);
    }

    // A leaf: a pointed oval with a darker midrib.
    private static ImageTexture LeafTexture()
    {
        var img = Image.CreateEmpty(32, 32, false, Image.Format.Rgba8);
        for (int y = 0; y < 32; y++)
            for (int x = 0; x < 32; x++)
            {
                float u = (x + .5f) / 32 * 2 - 1, v = (y + .5f) / 32 * 2 - 1;
                float half = .55f * (1 - v * v) * (1 - .35f * v); // wider toward the stem end
                float inside = Mathf.Clamp((half - Math.Abs(u)) * 20, 0, 1) * (v > -.95f && v < .95f ? 1 : 0);
                float rib = 1 - .35f * Mathf.Clamp(1 - Math.Abs(u) * 18, 0, 1);
                img.SetPixel(x, y, new Color(rib, rib * .95f, rib * .9f, inside));
            }
        return ImageTexture.CreateFromImage(img);
    }

    private static ImageTexture DotTexture()
    {
        var img = Image.CreateEmpty(16, 16, false, Image.Format.Rgba8);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
            {
                float u = (x + .5f) / 16 * 2 - 1, v = (y + .5f) / 16 * 2 - 1, r = Mathf.Sqrt(u * u + v * v);
                img.SetPixel(x, y, new Color(1, 1, 1, Mathf.Clamp(1 - r, 0, 1)));
            }
        return ImageTexture.CreateFromImage(img);
    }
}
