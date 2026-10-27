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
    public readonly Setting Size, Shape, Continents, Islands, ContinentSize, CoastRoughness, Ranges, MountainHeight, MountainWidth, HillHeight, Classic;
    // ---- terrain
    public readonly Setting Hills, Plains, Rock, CoastDetail, Erosion, Shores;
    // ---- water
    public readonly Setting Rivers, RiverWidth, Meanders, Wander, WidthVariation, Banks, Lakes, Deltas;
    // ---- vegetation and objects
    public readonly Setting ForestCover, ForestDensity, Rocks, Props;

    /// <param name="catalogue">The world types, for the land shapes' names (<see cref="WorldRecipe.ShapeIds"/>).</param>
    public WorldGenOptions(IReadOnlyList<WorldRecipe>? catalogue = null)
    {
        _shapes = Array.ConvertAll(WorldRecipe.ShapeIds, id => catalogue?.FirstOrDefault(r => r.Id == id));
        const string S = "World shape", T = "Terrain", W = "Rivers and lakes";
        Classic = Add(new("gen.version", S, "Terrain version", 0, 8, 1, 0,
            "Generate the world as in an earlier build, to compare or go back: Build 9 (before biomes), Build 8 (before estuaries and gentler bulging banks), Preview 7 (before smooth river mouths), Build 5 (before natural banks and steeper shores), Build 51 (before rivers inside lakes took the lake's level), Build 52 (before sea pockets inside lakes became lake), Build 114 (before the Landforms settings), Build 115 (before escarpments and plateaus).",
            options: new[] { "current", "Build 9", "Build 8", "Preview 7", "Build 5", "Build 51", "Build 52", "Build 114", "Build 115" }));
        Size = Add(new("gen.size", S, "World size", 0, WorldRecipe.Sizes.Length - 1, 1, 4, "Width and length of the world. The world type adapts to any size.",
            options: Array.ConvertAll(WorldRecipe.Sizes, k => $"{k:0} km × {k:0} km")));
        Shape = Add(new("gen.shape", S, "Land shape", 0, WorldRecipe.ShapeIds.Length, 1, 0,
            "The layout of land and sea: the world type's own, or another type's (the rest of the world type stays: its parameters, climate and landforms).",
            options: new[] { "the world type's" }.Concat(WorldRecipe.ShapeIds.Select((id, i) => _shapes[i] is { } r ? $"{r.ShortName} ({r.Group.ToLowerInvariant()})" : id)).ToArray()));
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
        // Landform modules (revision 17, D229): how often and how big, for every world type.
        const string L = "Landforms";
        foreach (var info in LandformCatalog.All)
            _landforms.Add((info,
                Add(new($"gen.landform.{info.Id}.amount", L, info.Name, 0, LandformCatalog.AmountNames.Length - 1, 1, LandformCatalog.DesignedAmount,
                    $"How often {info.Name.ToLowerInvariant()} appear {info.Where} (off removes them). Current terrain version only.",
                    options: LandformCatalog.AmountNames)),
                Add(new($"gen.landform.{info.Id}.size", L, info.Name + " size", .5, 2, .05, 1,
                    $"How big and high {info.Name.ToLowerInvariant()} are. Current terrain version only.", "×"))));
        // One toggle per built-in biome (custom biomes from the user folder are always included).
        const string B = "Biomes in this world";
        foreach (var (id, name) in BuiltInBiomes())
            _biomeToggles.Add((id, Add(new("gen.biome." + id, B, name, 0, 1, 1, 1, $"Include {name} in this world."))));
    }

    public readonly Setting Latitude, LatitudeSpan, Warmth, Wetness, BiomePlacement, PatchSize;
    private readonly List<(string Id, Setting Toggle)> _biomeToggles = new();
    private readonly List<(LandformInfo Info, Setting Amount, Setting Size)> _landforms = new();
    private readonly WorldRecipe?[] _shapes;

    /// <summary>The recipe whose land shape the world takes: the chosen shape, else the world type itself.</summary>
    public WorldRecipe ShapeOf(WorldRecipe type) => Shape.Index > 0 && _shapes[Shape.Index - 1] is { } shape ? shape : type;

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
        Set(Shape, 0); Set(Continents, 0); Set(Islands, -1); Set(Ranges, 0);
        foreach (var s in new[] { ContinentSize, CoastRoughness, MountainHeight, MountainWidth, HillHeight }) Set(s, 1);
        var style = TerrainStyle.For(recipe.Base);
        Set(Plains, 1 - style.PlainsRoughness);
        Set(Classic, 0);
        Set(ForestCover, 1.15); Set(ForestDensity, 1.2); Set(Rocks, 1); Set(Props, 1);
        Set(Latitude, 45); Set(LatitudeSpan, 0); Set(Warmth, 0); Set(Wetness, 1);
        foreach (var s in new[] { Hills, Rock, CoastDetail, Erosion, Shores, Rivers, RiverWidth, Meanders, Wander, WidthVariation, Banks, Lakes, Deltas }) Set(s, 1);
        foreach (var (_, amount, size) in _landforms) { Set(amount, LandformCatalog.DesignedAmount); Set(size, 1); }
        // A world type may start from its own parameter values (saved custom types).
        foreach (var (key, value) in recipe.Settings)
            if (All.Find(x => x.Key == key) is { } setting && !ShapeKeys.Contains(key)) Set(setting, value);
    }

    /// <summary>Keys baked into a world type's profile (or chosen per world) rather than stored as its parameters.</summary>
    private static readonly HashSet<string> ShapeKeys = new()
    {
        "gen.version", "gen.size", "gen.shape", "gen.continents", "gen.islands", "gen.continent_size", "gen.coast", "gen.ranges",
        "gen.mountain_height", "gen.mountain_width", "gen.hill_height",
    };

    /// <summary>Current non-shape parameter values, to store with a new world type.</summary>
    public Dictionary<string, double> TypeSettings()
    {
        var result = new Dictionary<string, double>();
        foreach (var s in All) if (!ShapeKeys.Contains(s.Key)) result[s.Key] = s.Value;
        return result;
    }

    public WorldChoices Choices => ChoicesBase with { ScaleRevision = Era >= 2 ? 8 : 9 };

    /// <summary>The terrain version as an age: 0 = current, Build 51, 52, 114 or 115 (the same apart from <see cref="TerrainStyle.Revision"/>),
    /// 1 = Build 9, 2 = Build 8, 3 = Preview 7, 4 = Build 5 (Builds 51, 52, 114 and 115 were added after them, at indices 5–8).</summary>
    private int Era => Classic.Index >= 5 ? 0 : Classic.Index;

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

    /// <summary>The profile and terrain style these parameters describe, starting from <paramref name="recipe"/>.</summary>
    /// <param name="pinnedRevision">The terrain revision a world recorded (its <c>--revision=</c>), used for the
    /// "current" terrain version so a world never changes when a new revision ships (D229); null = the newest.</param>
    public (WorldProfile Profile, TerrainStyle Style) Build(WorldRecipe recipe, int? pinnedRevision = null)
    {
        // (revisions 14 and 15 are "current" with an older revision, as Builds 51 and 52; older ones differ in more)
        int current = pinnedRevision is >= 14 and <= TerrainStyle.CurrentRevision ? pinnedRevision.Value : TerrainStyle.CurrentRevision;
        var profile = ShapeOf(recipe).Resolve(Choices);
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
            BankVariation = Era == 4 ? 0 : Banks.Value,
            ShoreSlope = Era == 4 ? 0 : Shores.Value,
            MouthBlend = Era >= 3 ? 0 : 1,
            Revision = Era >= 2 ? 8 : Era == 1 ? 9 : Classic.Index == 5 ? 14 : Classic.Index == 6 ? 15 : Classic.Index == 7 ? 16 : Classic.Index == 8 ? 17 : current,
            Landforms = _landforms.ToDictionary(l => l.Info.Id, l => new LandformSetting(LandformCatalog.AmountValues[l.Amount.Index], l.Size.Value)),
            ForestCover = ForestCover.Value, ForestDensity = ForestDensity.Value, RockAmount = Rocks.Value, PropAmount = Props.Value,
            LakeShare = b.LakeShare * Lakes.Value,
            DeltaMinCatchmentKm2 = Deltas.Value <= 0 ? double.PositiveInfinity : b.DeltaMinCatchmentKm2 / Deltas.Value,
            Climate = new ClimateSettings
            {
                CentreLatitude = Latitude.Value, LatitudeSpan = LatitudeSpan.Value <= 0 ? -1 : LatitudeSpan.Value,
                TemperatureOffset = Warmth.Value, Moisture = Wetness.Value, Biomes = Era == 0,
                BiomePlacement = BiomePlacement.Index, PatchSize = BiomePlacement.Index == 1 ? PatchSize.Value : 0,
                ExcludedBiomes = _biomeToggles.Where(t => !t.Toggle.On).Select(t => t.Id).ToArray() is { Length: > 0 } ex ? ex : null,
            },
        };
        return (profile, style);
    }
}
