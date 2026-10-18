using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.Gameplay;

/// <summary>
/// Building (D121, Tom: "first the player places a blueprint of the element of their choice, then they add the materials
/// needed"): a piece is placed as a blueprint (no cost yet), E brings materials from the pack until it is finished;
/// aiming at a piece of the same category with another shape replaces it (its materials come back and go into the new
/// one); dismantling gives everything back. The campfire and the lean-to are pieces too (better placement, Tom).
/// </summary>
public sealed partial class GameRules
{
    /// <summary>Testing tools (D128; a sandbox rule): pieces are built at once without materials, and Give hands out kits.</summary>
    public bool Testing { get; set; }

    /// <summary>Farthest from the player a piece can be placed, supplied or taken down (m).</summary>
    public const double BuildReach = 9;

    /// <summary>The pieces within <paramref name="reach"/> m of a point.</summary>
    public IEnumerable<Building.Placed> PiecesNear(double x, double z, double reach) => PiecesNear(Catalog, Objects, x, z, reach);

    public static IEnumerable<Building.Placed> PiecesNear(Catalog catalog, PlacedObjects objects, double x, double z, double reach) =>
        objects.Near(x, z, reach).Select(o => (o, catalog.Piece(o.Kind))).Where(t => t.Item2 != null)
            .Select(t => new Building.Placed(t.o.Id, t.Item2!, new Pose(t.o.X, t.o.Y, t.o.Z, t.o.Yaw)));

    /// <summary>
    /// Place a blueprint of piece <paramref name="pieceId"/> at (x, y, z) turned <paramref name="yaw"/>, or in place of
    /// piece <paramref name="replace"/> (another shape of the same category). Checks: within reach, not overlapping a
    /// piece of its group, standing on the ground or joined to a piece. A replaced piece's materials go back into the
    /// pack and as many as fit go straight into the new one.
    /// </summary>
    public ActionResult Place(int player, string pieceId, double x, double y, double z, double yaw, long replace, double px, double pz)
    {
        if (!Catalog.Pieces.TryGetValue(pieceId, out var def)) return ActionResult.Fail("Unknown piece.");
        if (!double.IsFinite(x + y + z + yaw)) return ActionResult.Fail("");
        if (replace != 0 && Objects.Get(replace) is { } swapped) { x = swapped.X; y = swapped.Y; z = swapped.Z; } // (in its place)
        if (Dist(px, pz, x, z) > BuildReach + 2) return ActionResult.Fail("Too far away to build there.");
        var pose = new Pose(x, y, z, yaw);
        var near = PiecesNear(x, z, 12).ToList();
        PlacedObject? old = null;
        if (replace != 0)
        {
            old = Objects.Get(replace);
            if (old == null || Catalog.Piece(old.Kind) is not { } was || was.Category != def.Category) return ActionResult.Fail("That cannot be swapped for this.");
            if (was.Id == def.Id && Math.Abs(Math.IEEERemainder(old.Yaw - yaw, Math.Tau)) < .01) return ActionResult.Fail("It is that already.");
            pose = new Pose(old.X, old.Y, old.Z, yaw);
        }
        if (Building.Blocked(def, pose, near, replace)) return ActionResult.Fail("Something is in the way.");
        if (!Building.Supported(def, pose, _world.Ground, near, replace)) return ActionResult.Fail(def.Ground ? "It needs firm ground or something to rest on." : "It needs something to rest on.");
        var inv = Inventory(player);
        string refund = "";
        if (old != null)
        {
            foreach (var (item, count) in old.Supplied) inv.Add(item, count);
            refund = old.Supplied.Count > 0 ? " (its materials went into the new one)" : "";
            Objects.Remove(old.Id);
            _inventoryChanged.Add(player);
        }
        var o = Objects.Add(def.Id, pose.X, pose.Y, pose.Z, (float)pose.Yaw, 0);
        o.Blueprint = def.Cost.Count > 0 && !Testing;
        if (Testing) foreach (var c in def.Cost) o.Supplied.Add((c.Item!, c.Count)); // (built for free, as if supplied)
        o.Owner = 0;
        if (!o.Blueprint) Finish(o, def);
        if (old != null && o.Blueprint)
        {
            var r = Supply(player, o.Id, px, pz);
            return new(true, old.Kind == def.Id ? "Turned." : $"Swapped for {def.Name.ToLowerInvariant()}{refund}. {r.Message}");
        }
        return new(true, o.Blueprint ? $"Blueprint: {def.Name.ToLowerInvariant()} — E brings {Needs(def, o)}." : $"{def.Name} built.");
    }

    /// <summary>Bring materials from the pack to a blueprint: as much of each as it still needs and the pack has.</summary>
    public ActionResult Supply(int player, long objectId, double px, double pz)
    {
        if (Objects.Get(objectId) is not { Blueprint: true } o || Catalog.Piece(o.Kind) is not { } def) return ActionResult.Fail("Nothing to build there.");
        if (Dist(px, pz, o.X, o.Z) > BuildReach) return ActionResult.Fail("Too far away.");
        var inv = Inventory(player);
        var brought = new List<string>();
        foreach (var need in def.Cost)
        {
            int missing = need.Count - Have(o, need.Item!);
            int give = Math.Min(missing, inv.Count(need.Item!));
            if (give <= 0) continue;
            inv.Take(need.Item!, give);
            int i = o.Supplied.FindIndex(s => s.Item == need.Item);
            if (i >= 0) o.Supplied[i] = (need.Item!, o.Supplied[i].Count + give); else o.Supplied.Add((need.Item!, give));
            brought.Add($"{give} {Catalog.Item(need.Item!).Name.ToLowerInvariant()}");
            _inventoryChanged.Add(player);
        }
        Objects.Touch(o.Id);
        if (def.Cost.All(c => Have(o, c.Item!) >= c.Count))
        {
            Finish(o, def);
            return new(true, $"{def.Name} built.");
        }
        if (brought.Count == 0) return ActionResult.Fail($"The {def.Name.ToLowerInvariant()} still needs {Needs(def, o)} — none in your pack.");
        return new(true, $"Brought {string.Join(", ", brought)}; still needs {Needs(def, o)}.");
    }

    private void Finish(PlacedObject o, PieceDef def)
    {
        o.Blueprint = false;
        if (Catalog.Objects.TryGetValue(def.Id, out var behaviour) && behaviour.Fuel > 0) o.Fuel = behaviour.Fuel; // (a campfire is lit)
        Objects.Touch(o.Id);
    }

    private static int Have(PlacedObject o, string item) => o.Supplied.Where(s => s.Item == item).Sum(s => s.Count);

    /// <summary>What a blueprint still needs ("2 logs, 4 plant fibre").</summary>
    public string Needs(PieceDef def, PlacedObject o) =>
        string.Join(", ", def.Cost.Where(c => Have(o, c.Item!) < c.Count).Select(c => $"{c.Count - Have(o, c.Item!)} {Catalog.Item(c.Item!).Name.ToLowerInvariant()}"));

    /// <summary>Take a piece down: everything that went into it back into the pack.</summary>
    public ActionResult Dismantle(int player, long objectId, double px, double pz)
    {
        if (Objects.Get(objectId) is not { } o || Catalog.Piece(o.Kind) is not { } def) return ActionResult.Fail("Nothing to take down there.");
        if (Dist(px, pz, o.X, o.Z) > BuildReach) return ActionResult.Fail("Too far away.");
        var inv = Inventory(player);
        // (a finished piece built before materials were tracked — an old save's campfire — gives its full cost back)
        var back = o.Blueprint || o.Supplied.Count > 0 ? o.Supplied.ToList() : def.Cost.Select(c => (c.Item!, c.Count)).ToList();
        if (def.Id == "campfire" && !o.Blueprint) back.RemoveAll(b => Catalog.Item(b.Item1).Has("fuel") && b.Item1 != "log"); // (the kindling burnt)
        foreach (var (item, count) in back) inv.Add(item, count);
        Objects.Remove(o.Id);
        _inventoryChanged.Add(player);
        return new(true, back.Count > 0 ? $"{def.Name} taken down: {string.Join(", ", back.Select(b => $"{b.Item2} {Catalog.Item(b.Item1).Name.ToLowerInvariant()}"))} back." : $"{def.Name} taken down.");
    }

    /// <summary>Testing tools (D128): a kit into the pack — "materials" or "tools" (weapons, tools, arrows, food).</summary>
    public ActionResult Give(int player, string kit)
    {
        if (!Testing) return ActionResult.Fail("Testing tools are off (Settings → Simulation → Testing tools, the host's).");
        (string Item, int Count)[] items = kit switch
        {
            "materials" => new[] { ("log", 60), ("branch", 60), ("plant_fibre", 60), ("stone", 30), ("bone", 20), ("hide", 10), ("berries", 20) },
            "tools" => new[] { ("stone_axe", 1), ("stone_knife", 1), ("spear", 1), ("bow", 1), ("arrow", 40), ("stone_sword", 1), ("fishing_rod", 1), ("bait", 20), ("cooked_meat", 10) },
            _ => System.Array.Empty<(string, int)>(),
        };
        if (items.Length == 0) return ActionResult.Fail("Unknown kit.");
        var inv = Inventory(player);
        foreach (var (item, count) in items) if (Catalog.Items.ContainsKey(item)) inv.Add(item, count);
        _inventoryChanged.Add(player);
        return new(true, kit == "materials" ? "Testing: materials added to the pack." : "Testing: weapons, tools, arrows and food added to the pack.");
    }

    /// <summary>Use a piece (E): a door opens or shuts.</summary>
    public ActionResult Use(int player, long objectId, double px, double pz)
    {
        if (Objects.Get(objectId) is not { Blueprint: false } o || Catalog.Piece(o.Kind) is not { } def || !def.Has("door")) return ActionResult.Fail("Nothing to use there.");
        if (Dist(px, pz, o.X, o.Z) > UseReach + 1) return ActionResult.Fail("Too far away.");
        o.State = o.State == 0 ? 1 : 0;
        Objects.Touch(o.Id);
        return new(true, "");
    }
}
