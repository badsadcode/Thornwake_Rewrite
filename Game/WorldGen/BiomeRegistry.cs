using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Thornwake.WorldGen;

/// <summary>A biome ready for use: its definition plus resolved asset sets (palettes, tree lists).</summary>
public sealed class ResolvedBiome
{
    public required BiomeDefinition Def { get; init; }
    public required int Index { get; init; }
    public required (float R, float G, float B) Grass { get; init; }
    public required (float R, float G, float B) Dry { get; init; }
    public required (float R, float G, float B) Soil { get; init; }
    public required (float R, float G, float B) ForestFloor { get; init; }
    public required (float R, float G, float B) Rock { get; init; }
    public required (float R, float G, float B) Map { get; init; }
    /// <summary>Ground snow cover from the ground set (ice: 1).</summary>
    public required double Snow { get; init; }
    /// <summary>Tree assets (names) with cumulative weights for picking.</summary>
    public required (string Asset, double Cumulative)[] Trees { get; init; }
    /// <summary>The classic look of build 9 and earlier (biomes off): trees follow elevation as before.</summary>
    public bool Classic { get; init; }

    public string PickTree(double unit)
    {
        if (Trees.Length == 0) return "";
        double total = Trees[^1].Cumulative, u = unit * total;
        foreach (var t in Trees) if (u < t.Cumulative) return t.Asset;
        return Trees[^1].Asset;
    }
}

/// <summary>
/// All biome definitions and asset sets (built-in Content/biomes.json + Content/asset_sets.json, plus custom files),
/// validated, with the procedural pool (definitions with <see cref="BiomeDefinition.InPool"/>). A custom definition
/// with a built-in id replaces the built-in one.
/// </summary>
public sealed class BiomeRegistry
{
    public IReadOnlyList<BiomeDefinition> All { get; }
    public IReadOnlyDictionary<string, AssetSet> Sets { get; }
    /// <summary>The biomes worlds generate with, resolved (index = position in this list).</summary>
    public IReadOnlyList<ResolvedBiome> Pool { get; }

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    };

    /// <summary>Registry the game and tests use unless a world says otherwise (set at startup from the content files).</summary>
    public static BiomeRegistry? Current { get; set; }

    /// <summary>Biomes off: one classic temperate biome (build 9 look).</summary>
    public static readonly BiomeRegistry ClassicOnly = new(
        new[] { new BiomeDefinition { Id = "classic", Name = "Classic temperate", Appearance = new() { Ground = "TemperateGround" } } },
        new[] { new AssetSet { Id = "TemperateGround", Kind = "ground", Grass = "566b3f", Dry = "7b8059", Soil = "5c4a35", ForestFloor = "3c4b2b", Rock = "77766a" },
                new AssetSet { Id = "BroadleafTrees", Kind = "trees", Items = new() { ["Oak_A"] = 1 } } },
        classic: true);

    public BiomeRegistry(IEnumerable<BiomeDefinition> biomes, IEnumerable<AssetSet> sets, bool classic = false)
    {
        var setMap = new Dictionary<string, AssetSet>();
        foreach (var s in sets) { Validate(s); setMap[s.Id] = s; }
        var byId = new Dictionary<string, BiomeDefinition>();
        var order = new List<string>();
        foreach (var b in biomes) { Validate(b, setMap); if (!byId.ContainsKey(b.Id)) order.Add(b.Id); byId[b.Id] = b; }
        All = order.Select(id => byId[id]).ToArray();
        Sets = setMap;
        var pool = new List<ResolvedBiome>();
        foreach (var b in All.Where(b => b.InPool))
        {
            var ground = setMap[b.Appearance.Ground];
            var trees = new List<(string, double)>();
            double cumulative = 0;
            foreach (var (setId, weight) in b.Vegetation.Trees)
            {
                var set = setMap[setId];
                double sum = set.Items.Values.Sum();
                foreach (var (asset, w) in set.Items) { cumulative += weight * w / Math.Max(1e-9, sum); trees.Add((asset, cumulative)); }
            }
            pool.Add(new ResolvedBiome
            {
                Def = b, Index = pool.Count,
                Grass = Hex(ground.Grass ?? "566b3f"), Dry = Hex(ground.Dry ?? "7b8059"), Soil = Hex(ground.Soil ?? "5c4a35"),
                ForestFloor = Hex(ground.ForestFloor ?? "3c4b2b"), Rock = Hex(ground.Rock ?? "77766a"), Map = Hex(b.Appearance.MapColor),
                Snow = ground.Snow, Trees = trees.ToArray(), Classic = classic,
            });
        }
        if (pool.Count == 0) throw new InvalidDataException("No biome is in the generation pool.");
        Pool = pool;
    }

    public static BiomeRegistry Parse(string biomesJson, string setsJson, IEnumerable<string>? customBiomes = null, IEnumerable<string>? customSets = null)
    {
        var biomes = new List<BiomeDefinition>(ParseList<BiomeDefinition>(biomesJson));
        var sets = new List<AssetSet>(ParseList<AssetSet>(setsJson));
        foreach (var json in customSets ?? Array.Empty<string>()) sets.AddRange(ParseOneOrMany<AssetSet>(json));
        foreach (var json in customBiomes ?? Array.Empty<string>()) biomes.AddRange(ParseOneOrMany<BiomeDefinition>(json));
        return new BiomeRegistry(biomes, sets);
    }

    private static T[] ParseList<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T[]>(json, Options) ?? Array.Empty<T>(); }
        catch (JsonException e) { throw new InvalidDataException("Damaged biome data: " + e.Message, e); }
    }

    /// <summary>A custom file may hold one definition or a list.</summary>
    private static T[] ParseOneOrMany<T>(string json) => json.TrimStart().StartsWith('[') ? ParseList<T>(json)
        : new[] { JsonSerializer.Deserialize<T>(json, Options) ?? throw new InvalidDataException("Empty definition.") };

    public static string ToJson(BiomeDefinition b) => JsonSerializer.Serialize(b, new JsonSerializerOptions { WriteIndented = true });

    private static void Validate(AssetSet s)
    {
        if (string.IsNullOrWhiteSpace(s.Id)) throw new InvalidDataException("Asset set without an id.");
        foreach (var c in new[] { s.Grass, s.Dry, s.Soil, s.ForestFloor, s.Rock })
            if (c != null && !IsHex(c)) throw new InvalidDataException($"Asset set '{s.Id}': bad colour '{c}'.");
    }

    private static void Validate(BiomeDefinition b, Dictionary<string, AssetSet> sets)
    {
        string where = $"Biome '{b.Id}'";
        if (string.IsNullOrWhiteSpace(b.Id) || string.IsNullOrWhiteSpace(b.Name)) throw new InvalidDataException("A biome needs an id and a name.");
        var p = b.Placement;
        foreach (var (r, name) in new[] { (p.Temperature, "Temperature"), (p.Moisture, "Moisture"), (p.Elevation, "Elevation"), (p.Slope, "Slope"),
                     (p.Mountain, "Mountain"), (p.Latitude, "Latitude"), (p.RegionSize, "RegionSize") })
            if (r != null && (r.Length != 2 || !(r[0] < r[1]) || r.Any(v => !double.IsFinite(v))))
                throw new InvalidDataException($"{where}: {name} must be [min, max].");
        if (!double.IsFinite(p.Weight) || p.Weight < 0 || p.Weight > 100) throw new InvalidDataException($"{where}: Weight out of range.");
        if (p.Patch != null && (p.Patch.Coverage <= 0 || p.Patch.Coverage > 1)) throw new InvalidDataException($"{where}: Patch coverage must be in (0, 1].");
        if (!sets.TryGetValue(b.Appearance.Ground, out var g) || g.Kind != "ground") throw new InvalidDataException($"{where}: unknown ground set '{b.Appearance.Ground}'.");
        foreach (var t in b.Vegetation.Trees.Keys)
            if (!sets.TryGetValue(t, out var ts) || ts.Kind != "trees") throw new InvalidDataException($"{where}: unknown tree set '{t}'.");
        if (!IsHex(b.Appearance.MapColor)) throw new InvalidDataException($"{where}: bad map colour.");
        if (b.Vegetation.TreeSize is not { Length: 2 } ts2 || ts2[0] <= 0 || ts2[1] < ts2[0]) throw new InvalidDataException($"{where}: TreeSize must be [min, max].");
    }

    private static bool IsHex(string c) => c.Length == 6 && c.All(Uri.IsHexDigit);

    public static (float, float, float) Hex(string c) =>
        (Convert.ToInt32(c[..2], 16) / 255f, Convert.ToInt32(c.Substring(2, 2), 16) / 255f, Convert.ToInt32(c.Substring(4, 2), 16) / 255f);
}
