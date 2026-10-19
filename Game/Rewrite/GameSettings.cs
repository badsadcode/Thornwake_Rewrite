using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
    // Multiplayer: the host's world settings applied on a client, and the player's own values to save meanwhile and to
    // restore when the session ends (GAMEPLAY_DESIGN.md §14.0: the host's time, weather, wind, sea; graphics stay one's own).
    private static readonly Dictionary<Setting, double> Own = new();

    /// <summary>Take a value from the host without saving it over the player's own.</summary>
    public static void Override(Setting s, double value)
    {
        if (!Own.ContainsKey(s)) Own[s] = s.Value;
        s.Value = value;
    }

    /// <summary>The session ended: the player's own values back.</summary>
    public static void ClearOverrides()
    {
        var own = Own.ToList();
        Own.Clear();
        foreach (var (s, v) in own) s.Value = v;
    }

    /// <summary>Settings that change the world everyone shares (the host's count), not how it is drawn.</summary>
    public static bool AffectsWorld(Setting s) => s.Key.StartsWith("time.") || (s.Key.StartsWith("sim.") && s.Key != "sim.weather_volume")
        || s.Key is "sky.clouds" or "sky.cloud_speed" or "sky.sea_state";

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
    public static readonly Setting HorizonLand = Add(new("view.horizon", "View distance", "Horizon land", 0, 2, 1, 2,
        "Coarse far land (and sea) beyond the terrain, out to ~100 or ~250 km, following the Earth's curve and fading " +
        "into the haze — no hard edge where the terrain stops. Off = build 36 (the terrain just ends).",
        options: new[] { "off", "~100 km", "~250 km" }));
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
    public static readonly Setting TreeImpostors = Add(new("view.tree_impostors", "View distance", "Tree impostors", 0, 4, 1, 2,
        "Beyond the detailed trees, the same trees drawn as pictures of their own models (baked from 8 sides and above, " +
        "lit, swaying): looks like the 3D forest at a fraction of the cost. Off: as in build 41 (3D trees, then tree cards).",
        options: new[] { "off (build 41)", "~1.6 km", "~3.2 km", "~6.4 km", "~12.8 km" }, rebuild: true));
    public static readonly Setting TreeFullDetail = Add(new("view.tree_full_detail", "View distance", "Full-detail trees", 60, 300, 10, 140,
        "With tree impostors on: how far trees use their most detailed model (and cast shadows). Beyond, the lighter " +
        "model of the same tree. Build 42: 220 m.", "m", rebuild: true));
    public static readonly Setting FarTreeDistance = Add(new("view.far_trees", "View distance", "Distant tree cards", 0, 4, 1, 1,
        "Distance up to which forests are drawn as simple tree cards (at Terrain detail range ×1).",
        options: new[] { "~3 km", "~6 km", "~12 km", "~24 km", "~48 km" }, rebuild: true));
    public static readonly Setting ScatterDistance = Add(new("view.scatter", "View distance", "Rocks, bushes and logs", .5, 4, .1, 1,
        "Multiplier on how far small objects stay visible.", "×", rebuild: true));

    // ---- lighting and effects (live)
    public static readonly Setting TreeShadows = Add(new("fx.tree_shadows", "Lighting and effects", "Tree shadows", 0, 1, 1, 0,
        "With tree impostors on: simplified = near trees cast their shadow from the lighter model of the same tree (looks the " +
        "same in the shadow, about a third of the cost); detailed = from the full model (build 42).",
        options: new[] { "simplified", "detailed (build 42)" }, rebuild: true));
    public static readonly Setting PlantShadows = Add(new("fx.plant_shadows", "Lighting and effects", "Grass and plant shadows", 0, 1, 1, 0,
        "Grass, flowers, reeds and shore plants cast shadows (cut out by their sprites, swaying with them). Off: as in " +
        "build 32. Costly with dense grass: benchmark it.", options: new[] { "off", "on" }, rebuild: true));
    public static readonly Setting Shadows = Add(new("fx.shadows", "Lighting and effects", "Shadows", 0, 3, 1, 2,
        "Sun shadow quality and distance.", options: OffLowMediumHigh));
    public static readonly Setting Ssao = Add(new("fx.ssao", "Lighting and effects", "Ambient occlusion (SSAO)", 0, 1, 1, 1,
        "Soft contact shadows in creases and under objects."));
    public static readonly Setting Ssil = Add(new("fx.ssil", "Lighting and effects", "Bounce light (SSIL)", 0, 1, 1, 0,
        "Screen-space indirect light: colour bleeding from sunlit surfaces. Costs some frame time."));
    public static readonly Setting WaterStyle = Add(new("fx.water_style", "Lighting and effects", "Water", 0, 5, 1, 0,
        "Classic: the water of build 14 (kept as it is). Experimental: rolling swells, waves that turn toward the shore and " +
        "break in the shallows, calmer bays, smooth per-pixel wave lighting, richer foam, flow-driven rivers. Experimental 2: " +
        "the same, with a livelier ocean — many sharp overlapping waves, wave sets, bright light through the crests, streaky " +
        "storm foam and a far wilder storm. Experimental 3: experimental 2 with a GPU-simulated ocean (FFT) — choppy, crisp " +
        "crests that pinch and fold, whitecaps born where they fold and trailing behind them (falls back to experimental 2 " +
        "on the old-PC renderer). Experimental 3A: experimental 3's waves with foam that is born on breaking crests and " +
        "drifts away with the wind, breaking up into streaks; glassier, deeper water; livelier far sea. Experimental 4: 3A, but " +
        "rough and stormy water reflects less (calm water is the same as 3A). Switch any time to compare.",
        options: new[] { "classic", "experimental", "experimental 2", "experimental 3", "experimental 3A", "experimental 4" }));
    public static readonly Setting FoamStyle = Add(new("fx.foam_style", "Lighting and effects", "Foam style", 0, 1, 1, 0,
        "Experimental water foam (2 and later): lace (thin threads along bubble cells, build 38) or net (a thick, fluffy net of " +
        "foam around round clear holes, finer where the foam is fresh, patchy as it thins).", options: new[] { "lace", "net" }));
    public static readonly Setting GroundTextures = Add(new("fx.ground_textures", "Lighting and effects", "Ground textures", 0, 1, 1, 0,
        "Current: the ground of the previous builds (kept as it is). Noor: Noor's pixel-art textures on the ground — grass, " +
        "sand, snow and soil keep their biome colours with the texture's pattern; forest floor, mud (wet mud when it rains), " +
        "gravel, moss, peat, scree and river/sea beds use Noor's art up close and fade to the usual colours far away. " +
        "Cliffs keep their rock textures. Switch any time to compare.",
        options: new[] { "current", "Noor" }));
    public static readonly Setting GroundVariety = Add(new("fx.ground_variety", "Lighting and effects", "Ground variety", 0, 1, .05, 1,
        "With Noor ground textures: patches of bare ground where plants thin out, in the biome's character (soil; gravel in " +
        "cold mountains; sand where it is dry; moss where it is wet), and warm/cool tint patches in the grass. 0 = build 33.", "×"));
    public static readonly Setting GroundRelief = Add(new("fx.ground_relief", "Lighting and effects", "Ground relief", 0, 1, .05, 1,
        "With Noor ground textures: small bumps from the textures up close (grass, soil, forest floor, rock catch the light), " +
        "each ground its own roughness, darker crevices. 0 = build 33.", "×"));
    public static readonly Setting GroundParallax = Add(new("fx.ground_parallax", "Lighting and effects", "Ground parallax", 0, 1, .05, 1,
        "With Noor ground textures: within ~30 m the ground's textures get real-looking depth (dark gaps sink a few " +
        "centimetres and shift as you move). 0 = build 34.", "×"));
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
    public static readonly Setting CausticLight = Add(new("fx.caustic_light", "Lighting and effects", "Caustics", 0, 1, 1, 1,
        "The bright light patterns on the bed of shallow water. By the sun: they are the sun's light focused by the waves, " +
        "so they fade at dusk, are gone at night and weak under clouds. Build 74: always at full strength.",
        options: new[] { "build 74 (always)", "by the sun" }));
    public static readonly Setting SmokeLook = Add(new("fx.smoke", "Lighting and effects", "Smoke", 0, 1, 1, 1,
        "Smoke from torches and campfires. Smoke texture: puffs of a smoke picture (Tom's), turning and spreading as they " +
        "rise, campfires smoking too. Build 72: soft round dots from the torch only, no campfire smoke.",
        options: new[] { "build 72 (soft dots, torch only)", "smoke texture" }));
    public static readonly Setting TorchLook = Add(new("fx.torch_look", "Lighting and effects", "Torch in mist", 0, 1, 1, 0,
        "How a torch lights mist and fog (Tom, dump 221438: the torch lit a whole mist bank evenly, far trees standing dark " +
        "against an orange haze). Halo: the mist glows around the flame and fades within a few metres, the ground is lit as " +
        "before, and the smoke takes the light of the night instead of glowing white. Build 72: as before.",
        options: new[] { "build 72 (even glow)", "halo at the flame" }));
    public static readonly Setting ForestRays = Add(new("fx.forest_rays", "Lighting and effects", "Forest rays", 0, 3, 1, 0,
        "Atmosphere 2.0 M2, two candidates to compare: A = a thin volumetric haze under the canopy, lit through the gaps in " +
        "the crowns (real shafts, needs volumetric fog's cost); B = the screen-space sun shafts, stronger inside forests. " +
        "A2 (M2 draft, aiming at Unity HDRP's rays): A with more sunlight into the air, less sky light in the shadows and " +
        "the fog's cells close to the camera, so each trunk's shadow shows in the air. Build 47 = as until now.",
        options: new[] { "build 47", "A: volumetric forest air", "B: screen-space shafts", "A2: sharp rays (M2 draft)" }));
    public static readonly Setting RayScatter = Add(new("fx.ray_scatter", "Lighting and effects", "Forest rays A2: forward scattering", .2, .85, .05, .75,
        "How much the forest air throws the sun's light forward (A2 only). High: bright, crisp shafts when you look toward " +
        "the sun, almost none from the side or with the sun behind you. Low: shafts seen from every side, softer and with " +
        "more glowing haze between the trees. 0.75 = build 76.", "×"));
    public static readonly Setting RayWeather = Add(new("fx.ray_weather", "Lighting and effects", "Rays and haze", 0, 1, 1, 0,
        "When shafts of light show. Always: as build 81, wherever the effects are on (forest rays in every forest by day). " +
        "By the weather (draft): only when the air holds something to catch the light: morning haze after a cool night, " +
        "moisture after rain, near water and in misty or forested places, dust in dry country when the wind blows; calm " +
        "air keeps it, wind clears moisture, the midday sun burns it off. A dry, windy noon is crisp; a calm, wet dawn fills " +
        "with shafts, round rocks and single trees too.",
        options: new[] { "always (build 81)", "by the weather (draft)" }));
    public static readonly Setting RayStrength = Add(new("fx.ray_strength", "Lighting and effects", "Rays strength", .5, 2.5, .05, 1,
        "How bright the shafts are, for the forest rays (A, A2) and the rays in open air. 1 = build 82.", "×"));
    public static readonly Setting ForestSunGlare = Add(new("fx.forest_sun_glare", "Lighting and effects", "Forest rays: sun glare", 0, 1, 1, 1,
        "Looking at the sun from under the trees with forest rays A or A2 on. Softened: the sunlit forest air glows less " +
        "the more directly you look at the sun, so the sun and the crowns round it are not blown out to white; shafts " +
        "beside and across the view keep their strength. Build 80: the full glare.",
        options: new[] { "build 80 (full glare)", "softened" }));
    public static readonly Setting AirRays = Add(new("fx.air_rays", "Lighting and effects", "Rays in open air (M2 draft)", 0, 3, 1, 0,
        "Shafts of sunlight anywhere outdoors, kilometres long: past mountain peaks and ridges and through gaps in the " +
        "clouds, in the haze over valleys, lakes and plains (the forest rays only reach a few hundred metres). Replaces the " +
        "light beams under clouds while on. Low / medium / high: how finely each view ray is followed (GPU time). Off = as " +
        "build 79. While on, the terrain casts shadows farther (mountains up to ~3 km away shade the ground near you).",
        options: new[] { "off", "low", "medium", "high" }, rebuild: true));
    public static readonly Setting LightModel = Add(new("fx.light_model", "Lighting and effects", "Light model", 0, 1, 1, 0,
        "Atmosphere 2.0 (experimental, M1): sunlight by phase of the day — golden hour, a softer morning and afternoon, " +
        "a stronger white noon — with the sky light following. Build 47 = the light until now.",
        options: new[] { "build 47", "Atmosphere 2.0 (experimental)" }));
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
    public static readonly Setting GodRayContrast = Add(new("fx.god_ray_contrast", "Lighting and effects", "God ray contrast", 0, 1, .05, 0,
        "Makes sun shafts and light beams stand out: brighter rays through gaps, darker lanes behind clouds, ridges and " +
        "trees. 0 = build 32. With volumetric fog on, also strengthens the real shafts through trees and fog.", "×"));
    public static readonly Setting LightBeams = Add(new("fx.light_beams", "Lighting and effects", "Light beams under clouds", 0, 4, .05, 1,
        "Beams of sunlight (or moonlight) falling through gaps in broken cloud, visible from the side even when the sun " +
        "is hidden. 0 = off.", "×"));
    public static readonly Setting VolumetricFog = Add(new("fx.volumetric_fog", "Lighting and effects", "Volumetric fog", 0, 1, 1, 0,
        "Light scattering in the air near the camera (real shafts through trees). Costs frame time."));
    public static readonly Setting Haze = Add(new("fx.haze", "Lighting and effects", "Distance haze", 0, 2, .05, 1,
        "Aerial perspective: how quickly distant land fades into the sky colour.", "×"));
    public static readonly Setting Exposure = Add(new("fx.exposure", "Lighting and effects", "Brightness", .5, 1.6, .05, 1,
        "Camera exposure.", "×"));
    // Tom (2026-10-13): full overcast and storms, dense woods and nights may be darker. 0 / normal = build 56's light.
    public static readonly Setting StormDarkness = Add(new("fx.storm_darkness", "Lighting and effects", "Overcast and storm darkness", 0, 1, .05, .5,
        "How dark a fully overcast or stormy day is: less light through the deck, and the eye adapts to it less. 0 = build 56. " +
        "The clouds themselves darken with it.", "×"));
    public static readonly Setting ForestDarkness = Add(new("fx.forest_darkness", "Lighting and effects", "Forest darkness", 0, 1, .05, .5,
        "How dark dense woods are: the eye lifts the exposure less (0 = build 56: up to +½ stop under a closed canopy) and " +
        "less sky light reaches the ground under the crowns.", "×"));
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
    public static readonly Setting CloudQuality = Add(new("sky.cloud_quality", "Sky", "Cloud quality", 0, 3, 1, 1,
        "One layer; layered (high cirrus, the main clouds and low fast clouds, each with its own wind); volumetric (experimental: " +
        "real 3D clouds with billowing shapes, light through them and sky colours in their shadows; crisp in calm air, smudged " +
        "in wind — much heavier on the GPU); volumetric v2 (experimental: the same clouds also in front of the land — they " +
        "come down onto the mountains, wrap the summits, lie as a sea of cloud below a peak and can be walked or flown into).",
        options: new[] { "one layer", "layered", "volumetric (experimental)", "volumetric v2 (experimental)" }));
    public static readonly Setting CloudResolution = Add(new("sky.cloud_resolution", "Sky", "Cloud resolution", 0, 2, 1, 1,
        "Detail of the cloud texture: crisper edges and finer wisps at higher settings (a little more memory and start-up time).",
        options: new[] { "256", "512", "1024" }));
    public static readonly Setting CloudBlend = Add(new("sky.cloud_blend", "Sky", "Cloud blend (v2)", 0, 1, 1, 1,
        "Volumetric v2 only: how the clouds in front of the land are blended with the same clouds in the sky. Matched: the " +
        "same sun colour and haze as the sky's clouds, and clouds also over the farthest land (no seam at the mountains' " +
        "outline, no cloudless band under the horizon). Build 52: as before.",
        options: new[] { "build 52", "matched" }));
    public static readonly Setting CloudLook = Add(new("sky.cloud_look", "Sky", "Cloud look", 0, 1, 1, 1,
        "Volumetric clouds. Reference (default since D96, round 32): the sky's clouds take Godot's fog once " +
        "instead of twice (brighter, clearer, and the same as the clouds in front of the land), wind streaks only in " +
        "strong wind, crisper cauliflower edges, flat blue-grey bases and bright tops (the M3 references). Build 53: the " +
        "clouds as before.",
        options: new[] { "build 53", "reference" }));
    public static readonly Setting CloudCaps = Add(new("sky.cloud_caps", "Sky", "Cloud caps", 0, 1, 1, 0,
        "Volumetric v2: the cloud that mountains catch. Dense: thick enough to hide the summit it sits on (from a distance " +
        "the rock no longer shows through). Build 54: as before.",
        options: new[] { "build 54", "dense" }));
    public static readonly Setting CloudTurbulence = Add(new("sky.cloud_turbulence", "Sky", "Cloud turbulence", 0, 1, 1, 0,
        "Volumetric v2: near you a cloud churns — wisps and eddies drifting by when you walk or fly through cloud.",
        options: new[] { "off", "on" }));
    public static readonly Setting LensStyle = Add(new("sky.lens_style", "Sky", "Lens clouds", 0, 1, 1, 0,
        "Lens clouds over high peaks. Stacked: a pile of thinner, smaller plates with uneven rims and a second lens " +
        "downwind. Build 54: four large elliptical plates.",
        options: new[] { "build 54", "stacked" }));
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
    public static readonly Setting ActionText = Add(new("ui.action_text", "Interface", "Action text", 0, 1, 1, 1,
        "The bar over Alden's head while something takes time (making, gathering, felling, casting) also says what it is."));
    public static readonly Setting ShoulderAim = Add(new("ui.shoulder_aim", "Interface", "Over-the-shoulder aim", 0, 1, 1, 1,
        "With the bow in hand or while building, the camera moves over Alden's right shoulder so the aim (the screen's centre) " +
        "is not hidden behind him. Off: the camera stays straight behind (as before build 63)."));
    public static readonly Setting TestingTools = Add(new("sim.testing", "Simulation", "Testing tools", 0, 1, 1, 0,
        "For trying things out (D128): building needs no materials (blueprints are built at once), and the pack (I) gets buttons " +
        "that give materials, weapons and tools. A sandbox rule: in multiplayer the host's applies to everyone."));
    public static readonly Setting ShowSnapPoints = Add(new("ui.snap_points", "Interface", "Show snap points", 0, 1, 1, 1,
        "While building, small dots mark the snap points of the pieces near the ghost (blue), the ghost's own (yellow) and " +
        "those that meet (green). P while building switches them."));
    public static readonly Setting BowDrop = Add(new("ui.bow_drop", "Interface", "Bow aim", 0, 1, 1, 0,
        "Arrows fly in a real arc and drop with distance (D131). Allow for the drop: the shot is raised just enough to come down " +
        "on the point under the crosshair. Straight: the arrow leaves toward the crosshair and drops below it — you hold over " +
        "yourself (as in builds 63–66).", options: new[] { "allow for the drop", "straight (hold over yourself)" }));
    public static readonly Setting ArrowTrails = Add(new("ui.arrow_trails", "Interface", "Arrow trails", 0, 1, 1, 1,
        "A thin trail behind every arrow, fading in about a second: you see the arc it flew."));
    public static readonly Setting BowHand = Add(new("ui.bow_hand", "Interface", "Bow hand", 0, 1, 1, 0,
        "Which of Alden's hands holds the bow (the other draws the string).", options: new[] { "right", "left (as builds 63–67)" }));
    public static readonly Setting BlueprintShadows = Add(new("ui.blueprint_shadows", "Interface", "Blueprint shadows", 0, 1, 1, 0,
        "Whether placed blueprints (the see-through pieces waiting for materials) cast shadows. Off: as before (D134)."));
    public static readonly Setting ScanLook = Add(new("ui.scan_look", "Interface", "Scan look", 0, 1, 1, 0,
        "How the scan (Z) sweeps out (D134): a sonar wave lighting the land as it passes, or build 68's thin ring.",
        options: new[] { "sonar wave", "ring (build 68)" }));
    public static readonly Setting NeedsOn = Add(new("sim.needs", "Simulation", "Hunger and cold", 0, 1, 1, 1,
        "Step 1 placeholder needs (D110): food, body warmth, wetness and health. Off: none of them change (testing the world)."));
    public static readonly Setting Animals = Add(new("sim.animals", "Simulation", "Animals", 0, 3, 1, 2,
        "How many animals live around the players (D120): deer, stags, foxes and wolves, by biome. A sandbox rule (in " +
        "multiplayer the host's applies to everyone).", options: new[] { "none", "few", "normal", "many" }));
    public static readonly Setting NightDarkness = Add(new("sim.night_darkness", "Simulation", "Night darkness", 0, 4, 1, 1,
        "How dark nights are on the land, as a sandbox rule (in multiplayer the host's applies to everyone): the light the " +
        "sky and the moon give the ground. The sky, moon, stars and clouds look the same. Normal = build 56.",
        options: new[] { "bright", "normal", "dark", "very dark", "pitch black" }));
    public static readonly Setting RainErosion = Add(new("sim.erosion", "Simulation", "Rain erosion", 0, 10, .25, 1,
        "Streams of rain water slowly wear the ground away and leave the soil where they slow down: at 1 a strong rain " +
        "stream cuts about 2 cm an hour, so gullies take many storms. 0 = off. Around the player (the water simulation's " +
        "128 m), stored like digging.", "×"));
    public static readonly Setting WeatherOn = Add(new("sim.weather", "Simulation", "Weather", 0, 1, 1, 1,
        "Simulated weather: fronts every few game hours bring clouds, rain, storms or snow, more often in wet climates. " +
        "Off: the Cloud cover setting and H (rain) only."));
    public static readonly Setting WeatherModel = Add(new("sim.weather_model", "Simulation", "Weather model", 0, 1, 1, 1,
        "Classic: the build 35 weather — fronts come by the clock, the same rain everywhere at once, wind and waves as " +
        "before. Simulated: weather systems, frontal rain bands and thunderstorm cells move across the whole world with " +
        "the wind — you can see a storm coming, sail into it or leave it behind; the wind gusts out of storms and the " +
        "sea builds with the wind.", options: new[] { "classic (build 35)", "simulated" }));
    // ---- Audio (round 29, Tom: "an audio tab in the options"): its own tab in the menu.
    public static readonly Setting MasterVolume = Add(new("audio.master", "Audio", "Master volume", 0, 1.5, .05, 1,
        "Everything the game plays. 0 = silent.", "×"));
    public static readonly Setting WeatherVolume = Add(new("sim.weather_volume", "Audio", "Weather sounds (all)", 0, 1.5, .05, 1,
        "Thunder, rain and wind together (simulated weather model). 0 = silent.", "×"));
    public static readonly Setting RainVolume = Add(new("audio.rain", "Audio", "Rain", 0, 1.5, .05, 1,
        "Rain on the ground and water: a soft hiss in drizzle, a roar in a downpour, drops ticking in light rain.", "×"));
    public static readonly Setting RainTone = Add(new("audio.rain_tone", "Audio", "Rain tone", 0, 1, .05, .35,
        "Soft (0: muffled, as heard from under a roof or through a hood) to crisp (1: build 39's bright hiss).", ""));
    public static readonly Setting WindVolume = Add(new("audio.wind", "Audio", "Wind", 0, 1.5, .05, 1,
        "The wind rushing, rising and falling with gusts.", "×"));
    public static readonly Setting ThunderVolume = Add(new("audio.thunder", "Audio", "Thunder", 0, 1.5, .05, 1,
        "Thunder: a crack close by, a long low roll from far away (it arrives after distance ÷ 343 m/s).", "×"));
    public static readonly Setting DistantRain = Add(new("sim.distant_rain", "Simulation", "Distant rain", 0, 1.5, .05, 1,
        "Grey curtains of rain under distant showers and storms (simulated weather model). 0 = off.", "×"));
    public static readonly Setting RainFrequency = Add(new("sim.rain_frequency", "Simulation", "Rain frequency", .3, 2.5, .05, 1,
        "How often weather fronts bring rain (1 = as the climate says).", "×"));
    public static readonly Setting RainStrength = Add(new("sim.rain", "Simulation", "Rain strength", .1, 4, .1, 1,
        "How hard it rains when rain is on (H). 1 = a heavy storm, exaggerated so runoff shows within a minute.", "×"));

    // ---- camera and performance (live)
    public static readonly Setting Fov = Add(new("view.fov", "Camera and performance", "Field of view", 50, 110, 1, 70, "Vertical field of view.", "°"));
    public static readonly Setting AntiAliasing = Add(new("perf.aa", "Camera and performance", "Anti-aliasing", 0, 4, 1, 1,
        "Edge smoothing. TAA is the smoothest but blends past frames: moving waves and the character can leave a faint " +
        "ghost (the water has no motion data). SMAA: sharp, no ghosting. FSR 2: temporal like TAA, sharper, and upscales " +
        "with Render scale below 1. (MSAA is not offered: it breaks the water's screen reads.)",
        options: new[] { "off", "FXAA", "TAA", "SMAA", "FSR 2" }));
    public static readonly Setting FogSmoothing = Add(new("perf.fog_smoothing", "Camera and performance", "Fog smoothing over time", .5, .95, .05, .9,
        "How much the volumetric fog blends with past frames: high = smooth but may trail behind moving things, low = " +
        "no trails but grainier fog. 0.9 = build 34.", "×"));
    // D142 (Tom): heavy effects stay player settings with an off, also in the finished game.
    public static readonly Setting FogQuality = Add(new("perf.fog_quality", "Camera and performance", "Volumetric fog quality", 0, 4, 1, 2,
        "How finely the volumetric fog (forest rays, mist patches, the torch's glow in mist) is computed: a grid of cells " +
        "across the view and into the distance. Finer = sharper rays and edges, more GPU time (RTX 3080, forest at " +
        "sunrise: high +0.8 ms, very high +3 ms). Off: no volumetric fog at all, for weak GPUs. Build 75 = 96 × 96 × 96.",
        options: new[] { "off", "low (64 × 64 × 64)", "build 75 (96 × 96 × 96)", "high (160 × 160 × 128)", "very high (256 × 256 × 160)" }));
    public static readonly Setting RenderScale = Add(new("perf.scale", "Display", "Render scale", .5, 1, .05, 1,
        "Render the 3D view at a lower resolution and upscale (AMD FSR) for more frames per second.", "×"));
    public static readonly Setting VSync = Add(new("perf.vsync", "Display", "V-Sync", 0, 1, 1, 1, "Synchronise to the monitor (no tearing)."));
    public static readonly Setting MaxFps = Add(new("perf.max_fps", "Display", "Frame rate limit", 0, 240, 10, 0,
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

    /// <summary>A benchmark is changing settings: nothing is written until it ends (D146: a benchmark that hung and was
    /// killed left its test profile in Tom's settings file).</summary>
    public static bool SaveSuspended { get; set; }

    public static void Save()
    {
        if (!_loaded || SaveSuspended) return;
        var file = new ConfigFile();
        foreach (var s in All) file.SetValue("settings", s.Key, Own.TryGetValue(s, out double own) ? own : s.Value);
        file.Save(FilePath);
    }

    public static void ResetSection(string section)
    {
        foreach (var s in All) if (s.Section == section) s.Value = s.Default;
    }

}
