using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Thornwake.Gameplay;

/// <summary>An item kind (Content/gameplay_items.json): rules ask for its tags, not its id (GAMEPLAY_DESIGN.md §2).</summary>
public sealed record ItemDef(string Id, string Name, IReadOnlySet<string> Tags, double Weight, int Stack, double Food, double Fuel, string Description,
    IReadOnlyDictionary<string, double>? Props = null)
{
    public bool Has(string tag) => Tags.Contains(tag);
    /// <summary>A property (a rod's strength and endurance…), or the fallback.</summary>
    public double Prop(string name, double fallback = 0) => Props != null && Props.TryGetValue(name, out double v) ? v : fallback;
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
    /// <summary>The fish (D114).</summary>
    public IReadOnlyList<FishDef> Fish { get; }
    /// <summary>The animals (D120), by id.</summary>
    public IReadOnlyDictionary<string, AnimalDef> Animals { get; }
    /// <summary>The building pieces (D121), by id, in the content file's order.</summary>
    public IReadOnlyDictionary<string, PieceDef> Pieces { get; }
    /// <summary>The building categories in the order they first appear (B's bar).</summary>
    public IReadOnlyList<string> Categories { get; }

    public Catalog(string itemsJson, string recipesJson, string? animalsJson = null, string? piecesJson = null)
    {
        using var items = JsonDocument.Parse(itemsJson);
        var list = new Dictionary<string, ItemDef>();
        foreach (var e in items.RootElement.GetProperty("items").EnumerateArray())
        {
            var d = new ItemDef(Str(e, "id")!, Str(e, "name") ?? Str(e, "id")!, Tags(e), Num(e, "weight"), (int)Num(e, "stack", 1),
                Num(e, "food"), Num(e, "fuel"), Str(e, "description") ?? "",
                e.TryGetProperty("props", out var props) ? props.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetDouble()) : null);
            list.Add(d.Id, d);
        }
        Items = list;
        Start = items.RootElement.TryGetProperty("start", out var start) ? start.EnumerateArray().Select(Parse).ToList() : new List<Ingredient>();
        Fish = items.RootElement.TryGetProperty("fish", out var fish)
            ? fish.EnumerateArray().Select(f =>
            {
                var kg = f.TryGetProperty("weight", out var w) ? w.EnumerateArray().Select(x => x.GetDouble()).ToArray() : new[] { .5, 1.0 };
                return new FishDef(Str(f, "id")!, Str(f, "name") ?? Str(f, "id")!,
                    f.GetProperty("waters").EnumerateArray().Select(x => x.GetString()!).ToHashSet(), Num(f, "stamina", 6), Num(f, "pull", 1), kg[0], kg[^1],
                    f.TryGetProperty("dusk", out var dusk) && dusk.ValueKind == JsonValueKind.True);
            }).ToList()
            : new List<FishDef>();

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

        var animals = new Dictionary<string, AnimalDef>();
        if (animalsJson != null)
        {
            using var doc = JsonDocument.Parse(animalsJson);
            foreach (var e in doc.RootElement.GetProperty("animals").EnumerateArray())
            {
                var group = e.TryGetProperty("group", out var g) ? g.EnumerateArray().Select(x => x.GetInt32()).ToArray() : new[] { 1, 1 };
                var a = new AnimalDef(Str(e, "id")!, Str(e, "name") ?? Str(e, "id")!, Str(e, "model") ?? "", Num(e, "height", 1), Num(e, "length", 1.5),
                    Num(e, "radius", .4), Num(e, "health", 50), Num(e, "walk", 1.5), Num(e, "run", 8), Num(e, "sight", 25), Num(e, "hearing", 12),
                    Str(e, "temper") ?? "shy", group[0], group[^1], Strings(e, "biomes"), Strings(e, "avoid"), Num(e, "weight", 1),
                    Num(e, "damage"), Num(e, "attack_range", 1.5), Num(e, "attack_seconds", 1.5),
                    e.TryGetProperty("yield", out var y) ? y.EnumerateArray().Select(Parse).ToList() : new List<Ingredient>(), Num(e, "butcher", 5));
                foreach (var x in a.Yield) Check(x);
                animals.Add(a.Id, a);
            }
        }
        Animals = animals;

        var pieces = new Dictionary<string, PieceDef>();
        var categories = new List<string>();
        if (piecesJson != null)
        {
            using var doc = JsonDocument.Parse(piecesJson);
            foreach (var e in doc.RootElement.GetProperty("pieces").EnumerateArray())
            {
                var box = e.TryGetProperty("box", out var b) ? b.EnumerateArray().Select(x => x.GetDouble()).ToArray() : new double[] { 0, .5, 0, .5, .5, .5 };
                var snap = e.TryGetProperty("snap", out var sp) ? sp.EnumerateArray().Select(p => { var v = p.EnumerateArray().Select(x => x.GetDouble()).ToArray(); return new V3(v[0], v[1], v[2]); }).ToList() : new List<V3>();
                string category = Str(e, "category") ?? "Other";
                var piece = new PieceDef(Str(e, "id")!, Str(e, "name") ?? Str(e, "id")!, category, Str(e, "shape") ?? Str(e, "name") ?? "", Str(e, "material") ?? "",
                    Str(e, "model") ?? "", e.TryGetProperty("cost", out var c) ? c.EnumerateArray().Select(Parse).ToList() : new List<Ingredient>(),
                    new V3(box[0], box[1], box[2]), new V3(box[3], box[4], box[5]), snap, e.TryGetProperty("ground", out var gr) && gr.ValueKind == JsonValueKind.True,
                    Num(e, "lift"), Num(e, "legs", .5), Tags(e), Str(e, "group") ?? category.ToLowerInvariant(),
                    e.TryGetProperty("attach", out _) ? Strings(e, "attach") : null);
                foreach (var x in piece.Cost) Check(x);
                if (piece.Cost.Any(x => x.Item == null)) throw new FormatException($"piece {piece.Id}: costs must name items");
                pieces.Add(piece.Id, piece);
                if (!categories.Contains(category)) categories.Add(category);
            }
        }
        Pieces = pieces;
        Categories = categories;
    }

    /// <summary>The piece of a placed object's kind, or null (a log pile, a trunk…).</summary>
    public PieceDef? Piece(string kind) => Pieces.GetValueOrDefault(kind);

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
    private static IReadOnlySet<string> Strings(JsonElement e, string name) =>
        e.TryGetProperty(name, out var t) ? t.EnumerateArray().Select(x => x.GetString()!).ToHashSet() : new HashSet<string>();
    private static IReadOnlySet<string> Tags(JsonElement e) =>
        e.TryGetProperty("tags", out var t) ? t.EnumerateArray().Select(x => x.GetString()!).ToHashSet() : new HashSet<string>();
}
