using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.WorldGen;

namespace Thornwake.Gameplay;

/// <summary>What the rules need to know about the world (the game answers from the generated world).</summary>
public interface IWorldQuery
{
    /// <summary>The tree of lattice cell (cx, cz) if one grows there (felled or not): its place and size.</summary>
    (double X, double Z, double Scale)? Tree(long cx, long cz);
    /// <summary>Generated objects taken out (felled trees).</summary>
    ObjectRemovals Removals { get; }
    /// <summary>Game time in hours since the world began (time stamps for regrowth).</summary>
    double Hours { get; }
}

/// <summary>An action's outcome, told to the player who asked.</summary>
public readonly record struct ActionResult(bool Ok, string Message, double Food = 0)
{
    public static ActionResult Fail(string message) => new(false, message);
}

/// <summary>
/// The rules of step 1's actions (GAMEPLAY_DESIGN.md §14 step 1, §15; D110), run by the host only (the server part):
/// every player's inventory, the placed objects, and what chopping, making, placing, fuelling and eating do. A player's
/// PC asks; this checks (distance, tools, materials) and applies; the host then sends what changed. Engine-free and
/// tested headless; the player's position comes from the caller (the host knows where everyone is).
/// </summary>
public sealed class GameRules
{
    public const double ChopReach = 4.5, UseReach = 3.5, PlaceReach = 6;

    public Catalog Catalog { get; }
    public PlacedObjects Objects { get; } = new();
    private readonly IWorldQuery _world;
    private readonly Dictionary<int, Inventory> _inventories = new();
    private readonly HashSet<int> _inventoryChanged = new();

    public GameRules(Catalog catalog, IWorldQuery world) { Catalog = catalog; _world = world; }

    /// <summary>A player's inventory; a new character starts with the start kit.</summary>
    public Inventory Inventory(int player)
    {
        if (_inventories.TryGetValue(player, out var inv)) return inv;
        inv = new Inventory(Catalog);
        foreach (var s in Catalog.Start) inv.Add(s.Item!, s.Count);
        _inventories[player] = inv;
        _inventoryChanged.Add(player);
        return inv;
    }

    /// <summary>The players whose inventories changed since the last call (the host sends them theirs).</summary>
    public List<int> TakeInventoryChanges() { var l = _inventoryChanged.ToList(); _inventoryChanged.Clear(); return l; }

    /// <summary>Fell the tree of lattice cell (cx, cz): an axe (tag <c>chopping</c>) in the pack, within reach; logs and
    /// branches by the tree's size.</summary>
    public ActionResult Chop(int player, long cx, long cz, double px, double pz)
    {
        var inv = Inventory(player);
        if (!inv.HasTag("chopping")) return ActionResult.Fail("You need something to chop with (an axe).");
        if (_world.Removals.Contains(ObjectRemovals.Trees, cx, cz)) return ActionResult.Fail("That tree is already down.");
        if (_world.Tree(cx, cz) is not { } tree) return ActionResult.Fail("There is no tree there.");
        if (Dist(px, pz, tree.X, tree.Z) > ChopReach + 1) return ActionResult.Fail("Too far from the tree.");
        _world.Removals.Remove(ObjectRemovals.Trees, cx, cz, _world.Hours);
        int logs = 1 + (int)Math.Round(tree.Scale * 2), branches = 3 + (int)Math.Round(tree.Scale * 3);
        inv.Add("log", logs); inv.Add("branch", branches);
        _inventoryChanged.Add(player);
        return new(true, $"Timber! {logs} logs and {branches} branches.");
    }

    /// <summary>Make a recipe: its tools in the pack, its inputs, a working station within reach; items go into the
    /// pack, or the object is placed at (x, y, z) facing <paramref name="yaw"/>.</summary>
    public ActionResult Make(int player, string recipeId, double px, double pz, double x, double y, double z, float yaw)
    {
        if (!Catalog.Recipes.TryGetValue(recipeId, out var r)) return ActionResult.Fail("Unknown recipe.");
        var inv = Inventory(player);
        if (Missing(inv, r, px, pz) is { } why) return ActionResult.Fail(why);
        if (r.Places != null && Dist(px, pz, x, z) > PlaceReach) return ActionResult.Fail("Too far away to put it there.");
        foreach (var input in r.Inputs) inv.Take(input);
        foreach (var output in r.Outputs) inv.Add(output.Item!, output.Count);
        _inventoryChanged.Add(player);
        if (r.Places is { } kind)
        {
            var def = Catalog.Objects[kind];
            Objects.Add(kind, x, y, z, yaw, def.Fuel);
            return new(true, $"{def.Name} built{(def.Fuel > 0 ? " and lit" : "")}.");
        }
        return new(true, $"Made: {string.Join(", ", r.Outputs.Select(o => $"{o.Count} × {Catalog.Item(o.Item!).Name}"))}.");
    }

    /// <summary>Why the recipe cannot be made here and now (null when it can).</summary>
    public string? Missing(Inventory inv, RecipeDef r, double px, double pz)
    {
        foreach (var tool in r.Tools)
            if (!inv.HasTag(tool)) return $"Needs a tool: {tool}.";
        foreach (var input in r.Inputs)
            if (inv.Count(input) < input.Count) return $"Needs {input.Count} × {(input.Item != null ? Catalog.Item(input.Item).Name : input.Tag)} ({inv.Count(input)} in the pack).";
        if (r.Station is { } station && !StationNear(station, px, pz)) return $"Needs a {station} nearby{(station == "fire" ? " (a lit campfire)" : "")}.";
        return null;
    }

    /// <summary>A working object with the tag within its reach (a fire must be burning).</summary>
    public bool StationNear(string tag, double px, double pz) =>
        Objects.Near(px, pz, 10).Any(o => Catalog.Objects.TryGetValue(o.Kind, out var d) && d.Tags.Contains(tag)
            && (!d.Tags.Contains("fire") || o.Lit) && Dist(px, pz, o.X, o.Z) <= d.Reach);

    /// <summary>Put fuel on a fire (the item with the most fuel in the pack, or the one named): it relights if out.</summary>
    public ActionResult Fuel(int player, long objectId, double px, double pz, string? item = null)
    {
        if (Objects.Get(objectId) is not { } o || !Catalog.Objects.TryGetValue(o.Kind, out var def) || def.MaxFuel <= 0)
            return ActionResult.Fail("Nothing to put fuel on.");
        if (Dist(px, pz, o.X, o.Z) > UseReach) return ActionResult.Fail("Too far from the fire.");
        if (o.Fuel >= def.MaxFuel - 1) return ActionResult.Fail("The fire has all the wood it can take.");
        var inv = Inventory(player);
        var fuel = item != null ? (inv.Count(item) > 0 ? Catalog.Item(item) : null)
            : inv.Stacks.Select(s => Catalog.Item(s.Item)).Where(d => d.Has("fuel")).OrderByDescending(d => d.Fuel).FirstOrDefault();
        if (fuel == null || !fuel.Has("fuel")) return ActionResult.Fail("You have nothing to burn.");
        bool wasOut = !o.Lit;
        inv.Take(fuel.Id);
        o.Fuel = Math.Min(def.MaxFuel, o.Fuel + fuel.Fuel);
        Objects.Touch(o.Id);
        _inventoryChanged.Add(player);
        return new(true, $"{fuel.Name} on the fire{(wasOut ? ": it catches again" : "")} ({o.Fuel:0} min of wood).");
    }

    /// <summary>Eat one of the item: the food value goes to the player's needs (on their PC).</summary>
    public ActionResult Eat(int player, string item)
    {
        if (!Catalog.Items.TryGetValue(item, out var d) || !d.Has("food")) return ActionResult.Fail("You cannot eat that.");
        var inv = Inventory(player);
        if (!inv.Take(item)) return ActionResult.Fail($"No {d.Name} left.");
        _inventoryChanged.Add(player);
        return new(true, $"You eat the {d.Name.ToLowerInvariant()}{(d.Has("raw") ? " (raw: not very filling)" : "")}.", d.Food);
    }

    /// <summary>Advance the world's objects by <paramref name="minutes"/> of game time.</summary>
    public void Step(double minutes) => Objects.Step(minutes);

    /// <summary>Warmth (°C) the lit fires give at a point: full at the fire, nothing at the edge of its reach × 2.</summary>
    public static double FireWarmth(Catalog catalog, PlacedObjects objects, double x, double z) =>
        objects.Near(x, z, 12).Where(o => o.Lit && catalog.Objects.TryGetValue(o.Kind, out var d) && d.Warmth > 0)
            .Sum(o => { var d = catalog.Objects[o.Kind]; double k = 1 - Dist(x, z, o.X, o.Z) / (d.Reach * 2); return d.Warmth * Math.Clamp(k, 0, 1); });

    private static double Dist(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
}
