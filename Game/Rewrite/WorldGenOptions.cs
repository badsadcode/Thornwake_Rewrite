using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// World-generation parameters of the "New world" tab: world size, then the world type's recipe with every shape
/// parameter on Auto (the recipe decides for this size) unless overridden; terrain, water and forest values are
/// multipliers (1 = as designed). The same type, size, seed and parameters always give the same world.
/// </summary>
public sealed class WorldGenOptions
{
    public readonly List<Setting> All = new();
    private Setting Add(Setting s) { All.Add(s); return s; }

    // ---- world shape (profile)
    public readonly Setting Size, Continents, Islands, ContinentSize, CoastRoughness, Ranges, MountainHeight, MountainWidth, HillHeight, Classic;
    // ---- terrain
    public readonly Setting Hills, Plains, Rock, CoastDetail, Erosion, Shores;
    // ---- water
    public readonly Setting Rivers, RiverWidth, Meanders, Wander, WidthVariation, Banks, Lakes, Deltas;
    // ---- vegetation and objects
    public readonly Setting ForestCover, ForestDensity, Rocks, Props;

    public WorldGenOptions()
    {
        const string S = "World shape", T = "Terrain", W = "Rivers and lakes";
        Classic = Add(new("gen.version", S, "Terrain version", 0, 4, 1, 0,
            "Generate the world as in an earlier build, to compare or go back: Build 9 (before biomes), Build 8 (before estuaries and gentler bulging banks), Preview 7 (before smooth river mouths), Build 5 (before natural banks and steeper shores).",
            options: new[] { "current", "Build 9", "Build 8", "Preview 7", "Build 5" }));
        Size = Add(new("gen.size", S, "World size", 0, WorldRecipe.Sizes.Length - 1, 1, 4, "Width and length of the world. The world type adapts to any size.",
            options: Array.ConvertAll(WorldRecipe.Sizes, k => $"{k:0} km × {k:0} km")));
        var counts = new string[WorldRecipe.MaxContinents + 1];
        counts[0] = "auto";
        for (int i = 1; i <= WorldRecipe.MaxContinents; i++) counts[i] = i.ToString();
        Continents = Add(new("gen.continents", S, "Continents", 0, WorldRecipe.MaxContinents, 1, 0, "Large landmasses. Auto: chosen from the world type and size.", options: counts));
        Islands = Add(new("gen.islands", S, "Islands", -1, WorldRecipe.MaxIslands, 1, -1, "Small islands (left end = auto: from the world type and size).") { AutoAt = -1 });
        ContinentSize = Add(new("gen.continent_size", S, "Continent size", .5, 1.6, .05, 1, "Landmass size relative to what the world type would choose.", "×"));
        CoastRoughness = Add(new("gen.coast", S, "Coastline roughness", 0, 1.5, .05, 1, "Large bays, peninsulas and capes.", "×"));
        Ranges = Add(new("gen.ranges", S, "Mountain ranges per continent", 0, 7, 1, 0, "Mountain belts on each continent.",
            options: new[] { "auto", "0", "1", "2", "3", "4", "5", "6" }));
        MountainHeight = Add(new("gen.mountain_height", S, "Mountain height", 0, 2, .05, 1, "Height of the peaks.", "×"));
        MountainWidth = Add(new("gen.mountain_width", S, "Mountain width", .5, 2, .05, 1, "Width of the mountain ranges.", "×"));
        HillHeight = Add(new("gen.hill_height", S, "Broad hills", 0, 2.5, .05, 1, "Height of the broad hills between the mountains.", "×"));

        Hills = Add(new("gen.hills", T, "Rolling ground", 0, 3, .05, 1, "Walking-scale hills and hollows on the lowland.", "×"));
        Plains = Add(new("gen.plains", T, "Plains flatness", 0, 1, .05, .6, "How flat the plains between hill country are (1 = very flat)."));
        Rock = Add(new("gen.rock", T, "Mountain crags", 0, 3, .05, 1, "Rocky ridged detail on mountains.", "×"));
        CoastDetail = Add(new("gen.coast_detail", T, "Coastline detail", 0, 3, .05, 1, "Small-scale wiggles of the shoreline.", "×"));
        Erosion = Add(new("gen.erosion", T, "Erosion", 0, 3, .05, 1, "How deeply rivers have carved their valleys.", "×"));
        Shores = Add(new("gen.shores", T, "Shore steepness", 0, 3, .05, 1, "How quickly the sea deepens and the land rises at the coast (0 = very gentle, wide shallows).", "×"));

        Rivers = Add(new("gen.rivers", W, "Number of rivers", .25, 4, .05, 1, "More rivers means smaller streams are shown too.", "×"));
        RiverWidth = Add(new("gen.river_width", W, "River width", .3, 3, .05, 1, "Channel width for a given catchment.", "×"));
        Meanders = Add(new("gen.meanders", W, "Meanders", 0, 3, .05, 1, "Tight river loops.", "×"));
        Wander = Add(new("gen.wander", W, "Wandering", 0, 3, .05, 1, "Large bends and swings across the valley floor.", "×"));
        WidthVariation = Add(new("gen.width_variation", W, "Width variety", 0, 3, .05, 1, "Pools, narrows and broad stretches along each river.", "×"));
        Banks = Add(new("gen.banks", W, "Bank irregularity", 0, 3, .05, 1, "Uneven shorelines: each bank widens, narrows and wobbles on its own, with bulges, shallow shelves and cuts.", "×"));
        const string V = "Forests and objects";
        ForestCover = Add(new("gen.forest_cover", V, "Forest cover", 0, 2, .05, 1.15, "How much of the land is forest (patch size); 0 = open land.", "×"));
        ForestDensity = Add(new("gen.forest_density", V, "Forest density", .2, 1.6, .05, 1.2, "Trees per area inside forests.", "×"));
        Rocks = Add(new("gen.rocks", V, "Stones, rocks and boulders", 0, 2, .05, 1, "Amount of stones, rocks, boulders and outcrops.", "×"));
        Props = Add(new("gen.props", V, "Bushes, stumps and logs", 0, 2, .05, 1, "Amount of bushes, berry bushes, stumps and fallen logs.", "×"));
        Lakes = Add(new("gen.lakes", W, "Lakes", 0, 4, .05, 1, "Share of land that may be lake (largest basins first).", "×"));
        Deltas = Add(new("gen.deltas", W, "Deltas", 0, 4, .05, 1, "How readily large rivers split into deltas on flat coasts (0 = none).", "×"));

        const string C = "Climate and biomes";
        Latitude = Add(new("gen.latitude", C, "Latitude of the centre", 0, 80, 1, 45,
            "Where the world lies on its planet: 0 = equator (tropical), 45 = temperate, 70 = arctic. The north edge is colder.", "°"));
        LatitudeSpan = Add(new("gen.latitude_span", C, "Climate range", 0, 90, 1, 0,
            "Degrees of latitude from south to north edge (left end = auto: more range for bigger worlds).", "°") { AutoAt = 0 });
        Warmth = Add(new("gen.warmth", C, "Warmer / colder", -15, 15, .5, 0, "Added to every temperature.", "°C"));
        Wetness = Add(new("gen.wetness", C, "Wetter / drier", .2, 3, .05, 1, "Multiplier on rain and snow (deserts and steppe ↔ forests and rainforest).", "×"));
        BiomePlacement = Add(new("gen.biome_placement", C, "Biome layout", 0, 1, 1, 0,
            "Realistic: biomes follow latitude, temperature and rain (the default). Patchwork: a game layout — organic patches " +
            "of random biomes repeated all over the land, ignoring the climate (mountain biomes stay on mountains).",
            options: new[] { "realistic", "patchwork" }));
        PatchSize = Add(new("gen.patch_size", C, "Patch size", 2, 60, 1, 12, "Patchwork layout: typical size of a biome patch.", "km"));
        // One toggle per built-in biome (custom biomes from the user folder are always included).
        const string B = "Biomes in this world";
        foreach (var (id, name) in BuiltInBiomes())
            _biomeToggles.Add((id, Add(new("gen.biome." + id, B, name, 0, 1, 1, 1, $"Include {name} in this world."))));
    }

    public readonly Setting Latitude, LatitudeSpan, Warmth, Wetness, BiomePlacement, PatchSize;
    private readonly List<(string Id, Setting Toggle)> _biomeToggles = new();

    private static IEnumerable<(string, string)> BuiltInBiomes()
    {
        string json;
        try { json = Godot.FileAccess.GetFileAsString("res://Content/biomes.json"); } catch { yield break; }
        if (string.IsNullOrEmpty(json)) yield break;
        using var doc = System.Text.Json.JsonDocument.Parse(json, new System.Text.Json.JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip });
        foreach (var b in doc.RootElement.EnumerateArray())
            if (b.TryGetProperty("Id", out var id) && b.TryGetProperty("Name", out var name)) yield return (id.GetString()!, name.GetString()!);
    }

    /// <summary>Select a world type: its native size, every shape parameter on Auto, multipliers reset.</summary>
    public void LoadRecipe(WorldRecipe recipe)
    {
        void Set(Setting s, double v) { s.Default = Math.Clamp(v, s.Min, s.Max); s.Value = s.Default; }
        int native = Array.IndexOf(WorldRecipe.Sizes, recipe.NativeKm);
        Set(Size, native >= 0 ? native : 4);
        Set(Continents, 0); Set(Islands, -1); Set(Ranges, 0);
        foreach (var s in new[] { ContinentSize, CoastRoughness, MountainHeight, MountainWidth, HillHeight }) Set(s, 1);
        var style = TerrainStyle.For(recipe.Base);
        Set(Plains, 1 - style.PlainsRoughness);
        Set(Classic, 0);
        Set(ForestCover, 1.15); Set(ForestDensity, 1.2); Set(Rocks, 1); Set(Props, 1);
        Set(Latitude, 45); Set(LatitudeSpan, 0); Set(Warmth, 0); Set(Wetness, 1);
        foreach (var s in new[] { Hills, Rock, CoastDetail, Erosion, Shores, Rivers, RiverWidth, Meanders, Wander, WidthVariation, Banks, Lakes, Deltas }) Set(s, 1);
        // A world type may start from its own parameter values (saved custom types).
        foreach (var (key, value) in recipe.Settings)
            if (All.Find(x => x.Key == key) is { } setting && !ShapeKeys.Contains(key)) Set(setting, value);
    }

    /// <summary>Keys baked into a world type's profile (or chosen per world) rather than stored as its parameters.</summary>
    private static readonly HashSet<string> ShapeKeys = new()
    {
        "gen.version", "gen.size", "gen.continents", "gen.islands", "gen.continent_size", "gen.coast", "gen.ranges",
        "gen.mountain_height", "gen.mountain_width", "gen.hill_height",
    };

    /// <summary>Current non-shape parameter values, to store with a new world type.</summary>
    public Dictionary<string, double> TypeSettings()
    {
        var result = new Dictionary<string, double>();
        foreach (var s in All) if (!ShapeKeys.Contains(s.Key)) result[s.Key] = s.Value;
        return result;
    }

    public WorldChoices Choices => ChoicesBase with { ScaleRevision = Classic.Index >= 2 ? 8 : 9 };

    private WorldChoices ChoicesBase => new()
    {
        SizeKm = WorldRecipe.Sizes[Size.Index],
        Continents = Continents.Index == 0 ? null : Continents.Index,
        Islands = Islands.Value < 0 ? null : (int)Islands.Value,
        Ranges = Ranges.Index == 0 ? null : Ranges.Index - 1,
        ContinentSize = ContinentSize.Value, CoastRoughness = CoastRoughness.Value, MountainHeight = MountainHeight.Value,
        MountainWidth = MountainWidth.Value, Hills = HillHeight.Value,
    };

    public void Reset() { foreach (var s in All) s.Value = s.Default; }

    /// <summary>True when every parameter equals the preset (the world is then exactly the preset world).</summary>
    public bool IsPreset => All.TrueForAll(s => s.Value == s.Default);

    /// <summary>The profile and terrain style these parameters describe, starting from <paramref name="preset"/>.</summary>
    public (WorldProfile Profile, TerrainStyle Style) Build(WorldRecipe recipe)
    {
        var profile = recipe.Resolve(Choices);
        var b = TerrainStyle.For(profile);
        var style = b with
        {
            LowlandDetail = b.LowlandDetail * Hills.Value,
            PlainsRoughness = 1 - Plains.Value,
            MountainDetail = b.MountainDetail * Rock.Value,
            CoastDetailShift = b.CoastDetailShift * CoastDetail.Value,
            ErosionStrength = b.ErosionStrength * Erosion.Value,
            RiverMinCatchmentKm2 = b.RiverMinCatchmentKm2 / Rivers.Value,
            RiverWidthPerSqrtKm2 = b.RiverWidthPerSqrtKm2 * RiverWidth.Value,
            MeanderStrength = Meanders.Value,
            WanderStrength = Wander.Value,
            WidthVariation = WidthVariation.Value,
            BankVariation = Classic.Index == 4 ? 0 : Banks.Value,
            ShoreSlope = Classic.Index == 4 ? 0 : Shores.Value,
            MouthBlend = Classic.Index >= 3 ? 0 : 1,
            Revision = Classic.Index >= 2 ? 8 : Classic.Index == 1 ? 9 : 13,
            ForestCover = ForestCover.Value, ForestDensity = ForestDensity.Value, RockAmount = Rocks.Value, PropAmount = Props.Value,
            LakeShare = b.LakeShare * Lakes.Value,
            DeltaMinCatchmentKm2 = Deltas.Value <= 0 ? double.PositiveInfinity : b.DeltaMinCatchmentKm2 / Deltas.Value,
            Climate = new ClimateSettings
            {
                CentreLatitude = Latitude.Value, LatitudeSpan = LatitudeSpan.Value <= 0 ? -1 : LatitudeSpan.Value,
                TemperatureOffset = Warmth.Value, Moisture = Wetness.Value, Biomes = Classic.Index == 0,
                BiomePlacement = BiomePlacement.Index, PatchSize = BiomePlacement.Index == 1 ? PatchSize.Value : 0,
                ExcludedBiomes = _biomeToggles.Where(t => !t.Toggle.On).Select(t => t.Id).ToArray() is { Length: > 0 } ex ? ex : null,
            },
        };
        return (profile, style);
    }
}
