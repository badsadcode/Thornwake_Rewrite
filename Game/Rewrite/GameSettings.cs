using System;
using System.Collections.Generic;
using System.Globalization;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// One tweakable number: a slider (Step &gt; 0), a checkbox (Options == null, range 0..1, step 1) or a choice
/// (Options != null, value = option index). Values are doubles so every kind shares one store and one UI builder.
/// Reads are plain field reads (safe from tile-builder worker threads; a change only takes effect on the next build).
/// </summary>
public sealed class Setting
{
    public string Key { get; }
    public string Section { get; }
    public string Label { get; }
    public string Tip { get; }
    public double Min { get; }
    public double Max { get; }
    public double Step { get; }
    public double Default { get; set; }
    public string[]? Options { get; }
    public string Unit { get; }
    /// <summary>Changing it needs the terrain tiles rebuilt (scatter density, view distances).</summary>
    public bool Rebuild { get; }
    /// <summary>Value shown as "auto" (the world recipe decides).</summary>
    public double? AutoAt { get; init; }
    /// <summary>Optional word shown after the number (e.g. the sea state's calm / breezy / rough / storm).</summary>
    public Func<double, string>? Describe { get; init; }
    public bool IsToggle => Options == null && Min == 0 && Max == 1 && Step == 1;
    private double _value;

    public Setting(string key, string section, string label, double min, double max, double step, double @default, string tip,
        string unit = "", string[]? options = null, bool rebuild = false)
    {
        Key = key; Section = section; Label = label; Min = min; Max = max; Step = step; Default = @default; Tip = tip;
        Unit = unit; Options = options; Rebuild = rebuild; _value = @default;
    }

    public double Value
    {
        get => _value;
        set
        {
            double v = Math.Clamp(value, Min, Max);
            if (v == _value) return;
            _value = v;
            Changed?.Invoke(this);
        }
    }

    /// <summary>Set without raising <see cref="Changed"/> (loading saved settings).</summary>
    internal void LoadValue(double value) => _value = Math.Clamp(value, Min, Max);
    public bool On => _value >= .5;
    public int Index => (int)Math.Round(_value);
    public float F => (float)_value;
    public event Action<Setting>? Changed;

    public string Display()
    {
        if (AutoAt is { } auto && Math.Abs(_value - auto) < 1e-9) return "auto";
        if (Options != null) return Options[Math.Clamp(Index, 0, Options.Length - 1)];
        if (IsToggle) return On ? "on" : "off";
        string number = Step >= 1 ? _value.ToString("0", CultureInfo.InvariantCulture)
            : Step >= .1 ? _value.ToString("0.0", CultureInfo.InvariantCulture) : _value.ToString("0.00", CultureInfo.InvariantCulture);
        if (Describe != null) number += " " + Describe(_value);
        return Unit.Length == 0 ? number : Unit == "×" ? number + "×" : $"{number} {Unit}";
    }
}

/// <summary>
/// Player settings for the rewrite world: detail, view distances, effects, sky, camera and performance. Saved to
/// <c>user://rewrite_settings.cfg</c>. Adding a setting is one line here; the Esc menu builds its controls from
/// <see cref="All"/>, grouped by section.
/// </summary>
public static class GameSettings
{
    public static readonly List<Setting> All = new();
    private const string FilePath = "user://rewrite_settings.cfg";
    private static bool _loaded;

    private static Setting Add(Setting s) { All.Add(s); return s; }
    private static readonly string[] OffLowMediumHigh = { "off", "low", "medium", "high" };

    // ---- detail (tiles rebuild). Only decoration here: trees, rocks and logs are world objects, set per world
    // (New world tab), never switched by a graphics option.
    public static readonly Setting GrassDensity = Add(new("detail.grass_density", "Detail", "Grass density", 0, 3, .05, 1,
        "Grass tufts per square metre (1 = default; 0 = no grass).", "×", rebuild: true));
    public static readonly Setting GrassStyle = Add(new("detail.grass_style", "Detail", "Grass and plants", 0, 1, 1, 1,
        "Plant populations: grass, tall grass, wild wheat, reeds, water lilies and underwater plants in patches and " +
        "communities (Noor's sprites). Classic: the earlier evenly spread grass tufts.",
        options: new[] { "classic tufts", "plant populations" }, rebuild: true));
    public static readonly Setting GrassDistance = Add(new("detail.grass_distance", "Detail", "Grass distance", 40, 400, 5, 120,
        "How far away grass and plants are drawn. Beyond ~150 m only with a wider Terrain detail range (grass grows on " +
        "the nearest terrain ring only). Heavy: test and benchmark.", "m", rebuild: true));
    // ---- view distance (tiles rebuild)
    public static readonly Setting TerrainDistance = Add(new("view.terrain", "View distance", "Terrain", 0, 3, 1, 1,
        "How far the terrain reaches before the horizon fog. ~80 km is for testing (more distant tiles to build).",
        options: new[] { "~10 km", "~20 km", "~40 km", "~80 km" }, rebuild: true));
    // Round 25 (Tom: "the loss of detail is pretty near, still visible — a slider so I can benchmark it").
    public static readonly Setting TerrainDetail = Add(new("view.terrain_detail", "View distance", "Terrain detail range", 1, 4, 1, 1,
        "How far each terrain detail level reaches: the full-detail ground, grass, 3D trees and every coarser ring move " +
        "out together (×2 ≈ 1.7× farther, ×3 ≈ 2.3×, ×4 ≈ 3×; build 32 = ×1). Many more tiles to build and draw: benchmark it.",
        "×", rebuild: true));
    public static readonly Setting TerrainMesh = Add(new("view.terrain_mesh", "View distance", "Terrain mesh detail", 0, 2, 1, 0,
        "Ground triangles beyond the nearest ring: normal (4 m, then coarser; build 32), high (2 m) or ultra (1 m — the " +
        "same as the nearest ring). Shapes, ridges and river banks keep their detail farther out. 4× / 16× the triangles " +
        "of those tiles: benchmark it.", options: new[] { "normal", "high", "ultra" }, rebuild: true));
    public static readonly Setting TreeDistance = Add(new("view.trees", "View distance", "Detailed trees", 0, 4, 1, 1,
        "Distance up to which full 3D trees are drawn (at Terrain detail range ×1; farther with a wider range).",
        options: new[] { "~400 m", "~800 m", "~1.6 km", "~3.2 km", "~6.4 km" }, rebuild: true));
    public static readonly Setting FarTreeDistance = Add(new("view.far_trees", "View distance", "Distant tree cards", 0, 4, 1, 1,
        "Distance up to which forests are drawn as simple tree cards (at Terrain detail range ×1).",
        options: new[] { "~3 km", "~6 km", "~12 km", "~24 km", "~48 km" }, rebuild: true));
    public static readonly Setting ScatterDistance = Add(new("view.scatter", "View distance", "Rocks, bushes and logs", .5, 4, .1, 1,
        "Multiplier on how far small objects stay visible.", "×", rebuild: true));

    // ---- lighting and effects (live)
    public static readonly Setting PlantShadows = Add(new("fx.plant_shadows", "Lighting and effects", "Grass and plant shadows", 0, 1, 1, 0,
        "Grass, flowers, reeds and shore plants cast shadows (cut out by their sprites, swaying with them). Off: as in " +
        "build 32. Costly with dense grass: benchmark it.", options: new[] { "off", "on" }, rebuild: true));
    public static readonly Setting Shadows = Add(new("fx.shadows", "Lighting and effects", "Shadows", 0, 3, 1, 2,
        "Sun shadow quality and distance.", options: OffLowMediumHigh));
    public static readonly Setting Ssao = Add(new("fx.ssao", "Lighting and effects", "Ambient occlusion (SSAO)", 0, 1, 1, 1,
        "Soft contact shadows in creases and under objects."));
    public static readonly Setting Ssil = Add(new("fx.ssil", "Lighting and effects", "Bounce light (SSIL)", 0, 1, 1, 0,
        "Screen-space indirect light: colour bleeding from sunlit surfaces. Costs some frame time."));
    public static readonly Setting WaterStyle = Add(new("fx.water_style", "Lighting and effects", "Water", 0, 4, 1, 0,
        "Classic: the water of build 14 (kept as it is). Experimental: rolling swells, waves that turn toward the shore and " +
        "break in the shallows, calmer bays, smooth per-pixel wave lighting, richer foam, flow-driven rivers. Experimental 2: " +
        "the same, with a livelier ocean — many sharp overlapping waves, wave sets, bright light through the crests, streaky " +
        "storm foam and a far wilder storm. Experimental 3: experimental 2 with a GPU-simulated ocean (FFT) — choppy, crisp " +
        "crests that pinch and fold, whitecaps born where they fold and trailing behind them (falls back to experimental 2 " +
        "on the old-PC renderer). Experimental 3A: experimental 3's waves with foam that is born on breaking crests and " +
        "drifts away with the wind, breaking up into streaks; glassier, deeper water; livelier far sea. Switch any time to compare.",
        options: new[] { "classic", "experimental", "experimental 2", "experimental 3", "experimental 3A" }));
    public static readonly Setting GroundTextures = Add(new("fx.ground_textures", "Lighting and effects", "Ground textures", 0, 1, 1, 0,
        "Current: the ground of the previous builds (kept as it is). Noor: Noor's pixel-art textures on the ground — grass, " +
        "sand, snow and soil keep their biome colours with the texture's pattern; forest floor, mud (wet mud when it rains), " +
        "gravel, moss, peat, scree and river/sea beds use Noor's art up close and fade to the usual colours far away. " +
        "Cliffs keep their rock textures. Switch any time to compare.",
        options: new[] { "current", "Noor" }));
    public static readonly Setting WaterReflections = Add(new("fx.water_reflections", "Lighting and effects", "Water reflections", 0, 2, 1, 2,
        "Screen-space reflections of the landscape on water (sky only when off).", options: new[] { "sky only", "medium", "high" }));
    public static readonly Setting Ssr = Add(new("fx.ssr", "Lighting and effects", "Screen-space reflections (other surfaces)", 0, 1, 1, 0,
        "Godot's SSR for shiny opaque surfaces (rocks, wet ground). Mostly subtle."));
    public static readonly Setting UnderwaterLight = Add(new("fx.underwater_light", "Lighting and effects", "Underwater light", 0, 2, 1, 2,
        "Light shafts fanning down from the moving surface and the dancing net of light on the bed when you dive. " +
        "Simple: half the samples (older PCs). Off: the plain underwater view of build 30.",
        options: new[] { "off", "simple", "full" }));
    public static readonly Setting Look = Add(new("fx.look", "Lighting and effects", "Look", 0, 1, 1, 1,
        "Natural: stronger colour and contrast (ACES tone mapping), deeper shade, glow only on real highlights (no " +
        "spilling light), less grey haze. Classic: the look of build 29.",
        options: new[] { "classic", "natural" }));
    public static readonly Setting Glow = Add(new("fx.glow", "Lighting and effects", "Glow (bloom)", 0, 1.5, .05, .6,
        "Bright sky, sun reflections and foam bleed softly into their surroundings.", "×"));
    public static readonly Setting SunStyle = Add(new("fx.sun_style", "Lighting and effects", "Sun", 0, 1, 1, 0,
        "Classic: the bright sun of build 11. Natural: the disc changes with the time of day — softer white at noon, " +
        "larger, orange and red near the horizon, with a wider warm glow at sunrise and sunset.",
        options: new[] { "classic", "natural" }));
    public static readonly Setting SunGlare = Add(new("fx.sun_glare", "Lighting and effects", "Sun glare", 0, 1.5, .05, 1,
        "Brightness of the sun disc and its halo in the sky (1 = build 11).", "×"));
    public static readonly Setting SunShafts = Add(new("fx.sun_shafts", "Lighting and effects", "Sun shafts", 0, 3, .05, .6,
        "Light rays from the sun through clouds, trees and over ridges (screen-space). Up to 3 since build 33.", "×"));
    public static readonly Setting SunShaftReach = Add(new("fx.sun_shaft_reach", "Lighting and effects", "Sun shaft reach", 0, 1, .05, 0,
        "How far the sun shafts stretch across the scene and how strongly they show away from the sun. 0 = build 32. " +
        "With volumetric fog on, also strengthens the real shafts through trees and fog.", "×"));
    public static readonly Setting LightBeams = Add(new("fx.light_beams", "Lighting and effects", "Light beams under clouds", 0, 2, .05, 1,
        "Beams of sunlight (or moonlight) falling through gaps in broken cloud, visible from the side even when the sun " +
        "is hidden. 0 = off.", "×"));
    public static readonly Setting VolumetricFog = Add(new("fx.volumetric_fog", "Lighting and effects", "Volumetric fog", 0, 1, 1, 0,
        "Light scattering in the air near the camera (real shafts through trees). Costs frame time."));
    public static readonly Setting Haze = Add(new("fx.haze", "Lighting and effects", "Distance haze", 0, 2, .05, 1,
        "Aerial perspective: how quickly distant land fades into the sky colour.", "×"));
    public static readonly Setting Exposure = Add(new("fx.exposure", "Lighting and effects", "Brightness", .5, 1.6, .05, 1,
        "Camera exposure.", "×"));
    public static readonly Setting GroundFog = Add(new("fx.ground_fog", "Lighting and effects", "Ground fog and mist", 0, 2, 1, 1,
        "Mist pooling in valleys, over rivers and lakes below you — thickest at dawn, after rain and in misty biomes (swamps, " +
        "old forests, taiga). With patches: drifting ground-hugging wisps around you (volumetric).",
        options: new[] { "off", "valleys", "valleys + mist patches" }));
    public static readonly Setting DepthOfField = Add(new("fx.dof", "Lighting and effects", "Depth of field", 0, 2, 1, 0,
        "Blurs the far distance a little, like a camera lens (the character and nearby ground stay sharp).",
        options: new[] { "off", "subtle", "strong" }));
    public static readonly Setting LeafTranslucency = Add(new("fx.leaf_translucency", "Lighting and effects", "Leaf translucency", 0, 2, .05, .8,
        "Subsurface scattering on leaves and plants: sunlight glows through canopies, grass and fronds seen against the sun (0 = off).", "×"));
    public static readonly Setting Saturation = Add(new("fx.saturation", "Lighting and effects", "Colour saturation", .5, 1.5, .05, 1.05,
        "Overall colourfulness.", "×"));

    // ---- sky (live)
    public static readonly Setting Clouds = Add(new("sky.clouds", "Sky", "Cloud cover", 0, 1, .05, .45,
        "Share of the sky covered by clouds (0 = clear). With simulated weather on, it sets how cloudy the weather tends to be (low: mostly clear days, high: mostly grey)."));
    public static readonly Setting CloudShadows = Add(new("sky.cloud_shadows", "Sky", "Cloud shadows", 0, 1, .05, .55,
        "How dark the shadows of clouds drifting over the land are.", "×"));
    public static readonly Setting SeaState = Add(new("sky.sea_state", "Sky", "Sea state", 0, 1, .05, .25,
        "Waves on the sea and lakes, smoothly from calm (a long soft swell) through breezy and rough to storm (huge seas, " +
        "experimental 2). Rain and storms make the sea rougher on top of this.")
        { Describe = v => v < .2 ? "calm" : v < .45 ? "breezy" : v < .75 ? "rough" : "storm" });
    public static readonly Setting CloudQuality = Add(new("sky.cloud_quality", "Sky", "Cloud quality", 0, 2, 1, 1,
        "One layer; layered (high cirrus, the main clouds and low fast clouds, each with its own wind); volumetric (experimental: " +
        "real 3D clouds with billowing shapes, light through them and sky colours in their shadows; crisp in calm air, smudged " +
        "in wind — much heavier on the GPU).", options: new[] { "one layer", "layered", "volumetric (experimental)" }));
    public static readonly Setting CloudResolution = Add(new("sky.cloud_resolution", "Sky", "Cloud resolution", 0, 2, 1, 1,
        "Detail of the cloud texture: crisper edges and finer wisps at higher settings (a little more memory and start-up time).",
        options: new[] { "256", "512", "1024" }));
    public static readonly Setting BiomeMoods = Add(new("sky.biome_moods", "Sky", "Biome skies", 0, 2, 1, 0,
        "Each biome tints the sky and haze a little (jungle humid, desert dusty, volcanic ashen, blighted swamp gloomy). " +
        "Off keeps the realistic sky everywhere.", options: new[] { "off (realistic)", "subtle", "strong" }));
    public static readonly Setting CloudSpeed = Add(new("sky.cloud_speed", "Sky", "Wind strength", 0, 4, .1, 1,
        "Strength of the simulated wind (it veers and gusts by itself, storms blow harder): clouds, waves, grass and trees.", "×"));

    public static readonly Setting Stars = Add(new("sky.stars", "Sky", "Night sky", 0, 2, .05, 1,
        "Brightness of the stars, the Milky Way and the moon at night (0 = none).", "×"));
    public static readonly Setting Planets = Add(new("sky.planets", "Sky", "Planets", 0, 1, 1, 1,
        "A few bright wandering planets among the stars."));

    // ---- time (live)
    public static readonly Setting DayLength = Add(new("time.day_length", "Time", "Day length", 10, 1440, 10, 120,
        "Real minutes for one full in-game day (24 h). 120 = two hours; 1440 = real time.", "min"));
    public static readonly Setting DaylightShare = Add(new("time.daylight", "Time", "Daylight share", 0, 90, 5, 0,
        "Share of the day with the sun up. Auto: realistic, from the latitude where you are and the season. " +
        "Otherwise a percentage (e.g. 70 = long days, short nights).", "%") { AutoAt = 0 });
    public static readonly Setting Season = Add(new("time.season", "Time", "Season", 0, 3, 1, 1,
        "Sets how high the sun climbs and (with Auto daylight) how long days are.", options: new[] { "spring", "summer", "autumn", "winter" }));
    public static readonly Setting TimePaused = Add(new("time.paused", "Time", "Stop time", 0, 1, 1, 0,
        "Freeze the time of day (the Esc menu slider still sets it)."));

    // ---- simulation (live)
    public static readonly Setting WaterSimulation = Add(new("sim.water", "Simulation", "Local water simulation", 0, 1, 1, 1,
        "Simulated water around the player (G pour, J spring, H rain, K clear). Runs in a 128 m area that follows you.", rebuild: true));
    public static readonly Setting WeatherOn = Add(new("sim.weather", "Simulation", "Weather", 0, 1, 1, 1,
        "Simulated weather: fronts every few game hours bring clouds, rain, storms or snow, more often in wet climates. " +
        "Off: the Cloud cover setting and H (rain) only."));
    public static readonly Setting RainFrequency = Add(new("sim.rain_frequency", "Simulation", "Rain frequency", .3, 2.5, .05, 1,
        "How often weather fronts bring rain (1 = as the climate says).", "×"));
    public static readonly Setting RainStrength = Add(new("sim.rain", "Simulation", "Rain strength", .1, 4, .1, 1,
        "How hard it rains when rain is on (H). 1 = a heavy storm, exaggerated so runoff shows within a minute.", "×"));

    // ---- camera and performance (live)
    public static readonly Setting Fov = Add(new("view.fov", "Camera and performance", "Field of view", 50, 110, 1, 70, "Vertical field of view.", "°"));
    public static readonly Setting AntiAliasing = Add(new("perf.aa", "Camera and performance", "Anti-aliasing", 0, 2, 1, 1,
        "Edge smoothing. (MSAA is not offered: it breaks the water's screen reads.)", options: new[] { "off", "FXAA", "TAA" }));
    public static readonly Setting RenderScale = Add(new("perf.scale", "Camera and performance", "Render scale", .5, 1, .05, 1,
        "Render the 3D view at a lower resolution and upscale (AMD FSR) for more frames per second.", "×"));
    public static readonly Setting VSync = Add(new("perf.vsync", "Camera and performance", "V-Sync", 0, 1, 1, 1, "Synchronise to the monitor (no tearing)."));
    public static readonly Setting MaxFps = Add(new("perf.max_fps", "Camera and performance", "Frame rate limit", 0, 240, 10, 0,
        "0 = unlimited.", "fps"));

    /// <summary>Raised after any setting changes (after <see cref="Setting.Changed"/>).</summary>
    public static event Action<Setting>? Changed;

    static GameSettings()
    {
        foreach (var s in All) s.Changed += x => { Changed?.Invoke(x); Save(); };
    }

    /// <summary>Load saved values once (missing or damaged entries keep their defaults).</summary>
    public static void Load()
    {
        if (_loaded) return;
        _loaded = true;
        var file = new ConfigFile();
        if (file.Load(FilePath) != Error.Ok) return;
        foreach (var s in All)
        {
            var v = file.GetValue("settings", s.Key, s.Default);
            if (v.VariantType is Variant.Type.Float or Variant.Type.Int) s.LoadValue(v.AsDouble());
        }
    }

    public static void Save()
    {
        if (!_loaded) return;
        var file = new ConfigFile();
        foreach (var s in All) file.SetValue("settings", s.Key, s.Value);
        file.Save(FilePath);
    }

    public static void ResetSection(string section)
    {
        foreach (var s in All) if (s.Section == section) s.Value = s.Default;
    }

}
