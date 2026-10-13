using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Thornwake.Gameplay;

/// <summary>An item kind (Content/gameplay_items.json): rules ask for its tags, not its id (GAMEPLAY_DESIGN.md §2).</summary>
public sealed record ItemDef(string Id, string Name, IReadOnlySet<string> Tags, double Weight, int Stack, double Food, double Fuel, string Description)
{
    public bool Has(string tag) => Tags.Contains(tag);
}

/// <summary>An item or any item with a tag, and how many.</summary>
public sealed record Ingredient(string? Item, string? Tag, int Count)
{
    public bool Matches(ItemDef d) => Item != null ? d.Id == Item : Tag != null && d.Has(Tag);
    public override string ToString() => $"{Count} × {Item ?? Tag}";
}

/// <summary>
/// Everything made or done (§2's action pipeline): inputs used up, tools needed (tags in the pack), a working station
/// within reach (a lit fire), the time it takes, and items made or an object placed.
/// </summary>
public sealed record RecipeDef(string Id, string Name, IReadOnlyList<Ingredient> Inputs, IReadOnlyList<string> Tools, string? Station,
    double Seconds, IReadOnlyList<Ingredient> Outputs, string? Places, string Description);

/// <summary>A kind of object players put into the world (a campfire): its tags, fuel (minutes) and warmth (°C close by).</summary>
public sealed record ObjectDef(string Id, string Name, IReadOnlySet<string> Tags, double Fuel, double MaxFuel, double Warmth, double Reach);

/// <summary>Items, recipes and placeable objects, from the content files (engine-free: the caller reads the files).</summary>
public sealed class Catalog
{
    public IReadOnlyDictionary<string, ItemDef> Items { get; }
    public IReadOnlyDictionary<string, RecipeDef> Recipes { get; }
    public IReadOnlyDictionary<string, ObjectDef> Objects { get; }
    /// <summary>What a new character carries.</summary>
    public IReadOnlyList<Ingredient> Start { get; }

    public Catalog(string itemsJson, string recipesJson)
    {
        using var items = JsonDocument.Parse(itemsJson);
        var list = new Dictionary<string, ItemDef>();
        foreach (var e in items.RootElement.GetProperty("items").EnumerateArray())
        {
            var d = new ItemDef(Str(e, "id")!, Str(e, "name") ?? Str(e, "id")!, Tags(e), Num(e, "weight"), (int)Num(e, "stack", 1),
                Num(e, "food"), Num(e, "fuel"), Str(e, "description") ?? "");
            list.Add(d.Id, d);
        }
        Items = list;
        Start = items.RootElement.TryGetProperty("start", out var start) ? start.EnumerateArray().Select(Parse).ToList() : new List<Ingredient>();

        using var recipes = JsonDocument.Parse(recipesJson);
        var objects = new Dictionary<string, ObjectDef>();
        if (recipes.RootElement.TryGetProperty("objects", out var objs))
            foreach (var e in objs.EnumerateArray())
            {
                var o = new ObjectDef(Str(e, "id")!, Str(e, "name") ?? Str(e, "id")!, Tags(e), Num(e, "fuel"), Num(e, "max_fuel"), Num(e, "warmth"), Num(e, "reach", 3));
                objects.Add(o.Id, o);
            }
        Objects = objects;
        var rs = new Dictionary<string, RecipeDef>();
        foreach (var e in recipes.RootElement.GetProperty("recipes").EnumerateArray())
        {
            var r = new RecipeDef(Str(e, "id")!, Str(e, "name") ?? Str(e, "id")!,
                e.TryGetProperty("inputs", out var i) ? i.EnumerateArray().Select(Parse).ToList() : new List<Ingredient>(),
                e.TryGetProperty("tools", out var t) ? t.EnumerateArray().Select(x => x.GetString()!).ToList() : new List<string>(),
                Str(e, "station"), Num(e, "seconds", 1),
                e.TryGetProperty("outputs", out var o) ? o.EnumerateArray().Select(Parse).ToList() : new List<Ingredient>(),
                Str(e, "places"), Str(e, "description") ?? "");
            rs.Add(r.Id, r);
        }
        Recipes = rs;
        // Every reference must resolve: a typo in a content file fails here, not in the middle of a game.
        foreach (var r in rs.Values)
        {
            foreach (var x in r.Inputs.Concat(r.Outputs)) Check(x);
            if (r.Places != null && !objects.ContainsKey(r.Places)) throw new FormatException($"recipe {r.Id}: unknown object {r.Places}");
            if (r.Outputs.Any(x => x.Item == null)) throw new FormatException($"recipe {r.Id}: outputs must name items");
        }
        foreach (var s in Start) Check(s);
    }

    private void Check(Ingredient x)
    {
        if (x.Item != null && !Items.ContainsKey(x.Item)) throw new FormatException($"unknown item {x.Item}");
        if (x.Item == null && x.Tag == null) throw new FormatException("an ingredient needs an item or a tag");
        if (x.Count < 1) throw new FormatException($"{x}: count must be at least 1");
    }

    public ItemDef Item(string id) => Items.TryGetValue(id, out var d) ? d : throw new KeyNotFoundException($"unknown item {id}");

    private static Ingredient Parse(JsonElement e) => new(Str(e, "item"), Str(e, "tag"), (int)Num(e, "count", 1));
    private static string? Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;
    private static double Num(JsonElement e, string name, double fallback = 0) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : fallback;
    private static IReadOnlySet<string> Tags(JsonElement e) =>
        e.TryGetProperty("tags", out var t) ? t.EnumerateArray().Select(x => x.GetString()!).ToHashSet() : new HashSet<string>();
}
