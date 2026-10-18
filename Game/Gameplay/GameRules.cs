using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.WorldGen;

namespace Thornwake.Gameplay;

/// <summary>What the rules need to know about the world (the game answers from the generated world).</summary>
public interface IWorldQuery
{
    /// <summary>The tree of lattice cell (cx, cz) if one grows there (felled or not): its place, size and species.</summary>
    (double X, double Z, double Scale, int Species)? Tree(long cx, long cz);
    /// <summary>Generated objects taken out (felled trees).</summary>
    ObjectRemovals Removals { get; }
    /// <summary>Game time in hours since the world began (time stamps for regrowth).</summary>
    double Hours { get; }
    /// <summary>The wind where the trees are (direction it blows toward, speed m/s): it decides where a tree falls.</summary>
    (double X, double Z, double Speed) Wind { get; }
    /// <summary>Ground height (m) at a point (where a felled tree's logs lie).</summary>
    double Ground(double x, double z);
    /// <summary>The kind of water ("ocean", "lake", "river", "pond") within <paramref name="radius"/> m of a point, or "".</summary>
    string WaterNear(double x, double z, double radius);
    /// <summary>Where the gatherable of a layer's lattice cell stands (a loose stone, a berry bush), whether or not one grows there.</summary>
    (double X, double Z) GatherSpot(int layer, long cx, long cz);
    /// <summary>The biome's tags at a point ("forest", "cold"…: where animals live; D120).</summary>
    IReadOnlySet<string> BiomeTags(double x, double z);
    /// <summary>Water at a point (animals keep out of it).</summary>
    bool IsWater(double x, double z);
}

/// <summary>A tree was hit (shake it) or felled (it falls toward Dir); everyone draws it.</summary>
/// <summary>A tree was hit (it shakes; the notch grows: HitsLeft of Total) or felled (it falls toward Dir). Dir is the
/// notch's side while it stands (from the trunk toward where the first hits came from), the fall's once felled.</summary>
public readonly record struct TreeEvent(long Cx, long Cz, int HitsLeft, int Total, bool Felled, double DirX, double DirZ);

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
public sealed partial class GameRules
{
    public const double ChopReach = 4.5, UseReach = 3.5, PlaceReach = 6;

    public Catalog Catalog { get; }
    public PlacedObjects Objects { get; } = new();
    private readonly IWorldQuery _world;
    private readonly Dictionary<int, Inventory> _inventories = new();
    private readonly HashSet<int> _inventoryChanged = new();
    // A tree being felled: hits so far and the notch's side (D112).
    private readonly Dictionary<(long, long), (int Hits, double Nx, double Nz)> _treeHits = new();
    private readonly List<TreeEvent> _treeEvents = new();

    /// <summary>The tree hits and falls since the last call (the host sends them to everyone).</summary>
    public List<TreeEvent> TakeTreeEvents() { var l = _treeEvents.ToList(); _treeEvents.Clear(); return l; }

    /// <summary>Hits a tree takes: 3 + 4 × its size (a small birch 6, a big oak 8).</summary>
    public static int TreeHits(double scale) => 3 + (int)Math.Round(4 * scale);
    /// <summary>How tall a tree of this size is, roughly (where its crown comes down).</summary>
    public static double TreeHeight(double scale) => 9 * scale;
    /// <summary>The trunk left lying after a fall (D112): its length and radius (m) for a tree of this size.</summary>
    public static double TrunkLength(double scale) => 7.2 * scale;
    public static double TrunkRadius(double scale) => .12 + .12 * scale;
    /// <summary>Logs in a trunk: its real volume (π r² L) at ~0.3 m³ a log — tree size sets the yield (Tom, §5a).</summary>
    public static int TrunkLogs(double scale) => Math.Max(1, (int)Math.Round(Math.PI * TrunkRadius(scale) * TrunkRadius(scale) * TrunkLength(scale) / .3));
    /// <summary>Hits that cut a trunk up into logs: two per cut (one cut fewer than logs), at least two.</summary>
    public static int TrunkHits(double scale) => Math.Max(2, 2 * (TrunkLogs(scale) - 1));
    /// <summary>Branches from the crown.</summary>
    public static int CrownBranches(double scale) => 3 + (int)Math.Round(3 * scale);

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

    // ---- saving (D116): players are known by name across sessions (ids change every time they connect).
    /// <summary>The host's own player in a save.</summary>
    public const string HostName = "host";
    private readonly Dictionary<int, string> _names = new();
    private readonly Dictionary<string, byte[]> _savedInventories = new();

    /// <summary>A player's name (on joining): a loaded save's inventory under that name becomes theirs.</summary>
    public void Name(int player, string name)
    {
        _names[player] = name;
        if (!_savedInventories.Remove(name, out var bytes)) return;
        var inv = new Inventory(Catalog);
        using (var r = new System.IO.BinaryReader(new System.IO.MemoryStream(bytes))) inv.Read(r);
        _inventories[player] = inv;
        _inventoryChanged.Add(player);
    }

    /// <summary>Every player's inventory by name (those here, and those of a loaded save who have not come back yet).</summary>
    public Dictionary<string, byte[]> SaveInventories()
    {
        var all = new Dictionary<string, byte[]>(_savedInventories);
        foreach (var (id, inv) in _inventories)
        {
            if (!_names.TryGetValue(id, out var name)) continue;
            using var ms = new System.IO.MemoryStream();
            using (var w = new System.IO.BinaryWriter(ms)) inv.Write(w);
            all[name] = ms.ToArray();
        }
        return all;
    }

    /// <summary>A loaded save's inventories: each one goes to its player when they are named (the host at once).</summary>
    public void RestoreInventories(Dictionary<string, byte[]> saved)
    {
        _savedInventories.Clear();
        foreach (var (name, bytes) in saved) _savedInventories[name] = bytes;
        foreach (var (id, name) in _names.ToList()) Name(id, name);
    }

    /// <summary>The players whose inventories changed since the last call (the host sends them theirs).</summary>
    public List<int> TakeInventoryChanges() { var l = _inventoryChanged.ToList(); _inventoryChanged.Clear(); return l; }

    /// <summary>Send this player's inventory again with the next changes (they were not there to receive it).</summary>
    public void InventoryChanged(int player) => _inventoryChanged.Add(player);

    /// <summary>
    /// One axe hit on the tree of lattice cell (cx, cz) (Tom: felling as real work in three acts, §5a; D112): an axe
    /// (tag <c>chopping</c>) in the pack, within reach. The first hit sets the **notch** on the side the chopper stands;
    /// after <see cref="TreeHits"/> hits the tree falls **toward the notch**, the wind pushing it (Old Berta: "the wind
    /// decides where a tree lands"). It leaves a bare **trunk** lying along the fall — a physical log the felling player's
    /// PC moves (it rolls, can be pushed) and the axe cuts into logs — and the crown's branches where it came down.
    /// </summary>
    public ActionResult Chop(int player, long cx, long cz, double px, double pz)
    {
        var inv = Inventory(player);
        if (!inv.HasTag("chopping")) return ActionResult.Fail("You need something to chop with (an axe).");
        if (_world.Removals.Contains(ObjectRemovals.Trees, cx, cz)) return ActionResult.Fail("That tree is already down.");
        if (_world.Tree(cx, cz) is not { } tree) return ActionResult.Fail("There is no tree there.");
        if (Dist(px, pz, tree.X, tree.Z) > ChopReach + 1) return ActionResult.Fail("Too far from the tree.");
        int total = TreeHits(tree.Scale);
        if (!_treeHits.TryGetValue((cx, cz), out var work))
        {
            // The notch faces the chopper (from the trunk toward them).
            double ax = px - tree.X, az = pz - tree.Z, al = Math.Sqrt(ax * ax + az * az);
            work = al > 1e-6 ? (0, ax / al, az / al) : (0, 1, 0);
        }
        int left = total - work.Hits - 1;
        if (left > 0)
        {
            _treeHits[(cx, cz)] = (work.Hits + 1, work.Nx, work.Nz);
            _treeEvents.Add(new TreeEvent(cx, cz, left, total, false, work.Nx, work.Nz));
            return new(true, left == 2 ? "It creaks…" : "");
        }
        _treeHits.Remove((cx, cz));
        _world.Removals.Remove(ObjectRemovals.Trees, cx, cz, _world.Hours);
        // Toward the notch; the wind has its say (up to half in a gale).
        var wind = _world.Wind;
        double k = Math.Clamp(wind.Speed / 14, 0, .5);
        double dx = work.Nx * (1 - k) + wind.X * k, dz = work.Nz * (1 - k) + wind.Z * k, dl = Math.Max(Math.Sqrt(dx * dx + dz * dz), 1e-6);
        dx /= dl; dz /= dl;
        _treeEvents.Add(new TreeEvent(cx, cz, 0, total, true, dx, dz));
        double half = TrunkLength(tree.Scale) / 2, r = TrunkRadius(tree.Scale), tx = tree.X + dx * half, tz = tree.Z + dz * half;
        var trunk = Objects.Add("trunk", tx, _world.Ground(tx, tz) + r, tz, (float)Math.Atan2(dx, dz), 0);
        // (lying along the fall: the log's long axis, its local up, turned onto the direction — a quarter turn about up × dir)
        double s45 = Math.Sqrt(.5);
        trunk.Qx = (float)(dz * s45); trunk.Qy = 0; trunk.Qz = (float)(-dx * s45); trunk.Qw = (float)s45;
        trunk.Size = (float)tree.Scale;
        trunk.Variant = tree.Species;
        trunk.Owner = player;
        double crown = TreeHeight(tree.Scale) * .8, bx = tree.X + dx * crown, bz = tree.Z + dz * crown;
        var branches = Objects.Add("branch_pile", bx, _world.Ground(bx, bz), bz, (float)Math.Atan2(dx, dz), 0);
        branches.Contents.Add(("branch", CrownBranches(tree.Scale)));
        return new(true, $"Timber! Cut the trunk up with the axe ({TrunkLogs(tree.Scale)} logs in it); the branches lie where the crown came down (E).");
    }

    /// <summary>One axe hit on a fallen trunk; after <see cref="TrunkHits"/> it is cut into logs lying where it was.</summary>
    public ActionResult ChopTrunk(int player, long objectId, double px, double pz)
    {
        if (!Inventory(player).HasTag("chopping")) return ActionResult.Fail("You need something to chop with (an axe).");
        if (Objects.Get(objectId) is not { Kind: "trunk" } t) return ActionResult.Fail("Nothing to cut up there.");
        if (Dist(px, pz, t.X, t.Z) > TrunkLength(t.Size) / 2 + ChopReach) return ActionResult.Fail("Too far from the trunk.");
        t.Hits++;
        if (t.Hits < TrunkHits(t.Size)) { Objects.Touch(t.Id); return new(true, t.Hits % 2 == 0 ? $"A cut through ({t.Hits / 2} of {TrunkLogs(t.Size) - 1})." : ""); }
        Objects.Remove(t.Id);
        int logs = TrunkLogs(t.Size);
        var pile = Objects.Add("log_pile", t.X, t.Y - TrunkRadius(t.Size), t.Z, t.Yaw, 0);
        pile.Contents.Add(("log", logs));
        return new(true, $"The trunk splits into {logs} logs (E picks them up).");
    }

    /// <summary>
    /// E on nature (Tom: E gathers; D113): a loose stone picked up (gone for good), berries picked from a bush (they grow
    /// back in <see cref="BerryRegrowHours"/>), or long grass pulled for plant fibre (anywhere it grows; nothing changes).
    /// Whether one really lies there is the player's PC's word (it draws them); the host checks the reach and that it
    /// was not taken already.
    /// </summary>
    public ActionResult Forage(int player, string what, long cx, long cz, double x, double z, double px, double pz, double py = double.NaN)
    {
        var inv = Inventory(player);
        // (on the ground's level: not from up on a platform — Tom, build 68)
        if (!double.IsNaN(py) && Math.Abs(py - _world.Ground(x, z)) > 2) return ActionResult.Fail("You cannot reach the ground from up here.");
        switch (what)
        {
            case "stone" or "berries":
            {
                int layer = what == "stone" ? ObjectRemovals.Stones : ObjectRemovals.Berries;
                var at = _world.GatherSpot(layer, cx, cz);
                if (Dist(px, pz, at.X, at.Z) > UseReach) return ActionResult.Fail("Too far away.");
                if (!_world.Removals.Remove(layer, cx, cz, _world.Hours)) return ActionResult.Fail(what == "stone" ? "Someone took it already." : "Already picked.");
                if (what == "stone") inv.Add("stone");
                else inv.Add("berries", 4);
                _inventoryChanged.Add(player);
                return new(true, what == "stone" ? "A good stone." : "4 handfuls of berries.");
            }
            case "grass":
            {
                if (Dist(px, pz, x, z) > UseReach) return ActionResult.Fail("Too far away.");
                // The grass is pulled: the cells around the spot are bare (for everyone) until it grows back (Tom).
                long gx = (long)Math.Floor(x / ObjectRemovals.GrassCell), gz = (long)Math.Floor(z / ObjectRemovals.GrassCell);
                int pulled = 0;
                for (long dz = -1; dz <= 1; dz++)
                    for (long dx = -1; dx <= 1; dx++)
                        if (_world.Removals.Remove(ObjectRemovals.Grass, gx + dx, gz + dz, _world.Hours)) pulled++;
                if (pulled == 0) return ActionResult.Fail("No long grass left here.");
                inv.Add("plant_fibre", pulled >= 5 ? 2 : 1);
                _inventoryChanged.Add(player);
                return new(true, pulled >= 5 ? "Plant fibre x2." : "Plant fibre x1 (little left here).");
            }
        }
        return ActionResult.Fail("Nothing to gather there.");
    }

    /// <summary>
    /// A fish landed (or lost: <paramref name="fish"/> empty) after the reel-in on the player's PC (D114). The host checks
    /// a rod and bait in the pack, water nearby where that fish lives, and not too often; the bait is used up either way.
    /// </summary>
    public ActionResult Catch(int player, string fish, double kg, double px, double pz)
    {
        var inv = Inventory(player);
        if (!inv.HasTag("fishing")) return ActionResult.Fail("You need a fishing rod.");
        if (!inv.HasTag("bait")) return ActionResult.Fail("No bait left: fish will not bite on a bare hook.");
        double now = _world.Hours;
        if (_lastCatch.TryGetValue(player, out double last) && now - last < .02) return ActionResult.Fail("Too quick.");
        string water = _world.WaterNear(px, pz, 22);
        if (water.Length == 0) return ActionResult.Fail("No water here.");
        FishDef? def = null;
        if (fish.Length > 0 && (Catalog.Fish.FirstOrDefault(f => f.Id == fish) is not { } d || !d.Waters.Contains(water)))
            return ActionResult.Fail("That fish does not live here.");
        if (fish.Length > 0) def = Catalog.Fish.First(f => f.Id == fish);
        inv.Take(new Ingredient(null, "bait", 1));
        _inventoryChanged.Add(player);
        _lastCatch[player] = now;
        if (def == null) return new(true, "It got away — and the bait with it.");
        kg = Math.Clamp(kg, def.MinKg, def.MaxKg);
        inv.Add("raw_fish");
        return new(true, FormattableString.Invariant($"A {def.Name.ToLowerInvariant()}, {kg:0.0} kg!"));
    }

    private readonly Dictionary<int, double> _lastCatch = new();

    /// <summary>
    /// A character died (GAMEPLAY_DESIGN §4; D115): everything they carried drops where they fell — a pack to come back
    /// for (E picks it up; anyone can) — and the player goes on as a new character with the start kit.
    /// </summary>
    public ActionResult Die(int player, double px, double pz)
    {
        var inv = Inventory(player);
        string dropped = "";
        if (inv.Stacks.Count > 0)
        {
            var pack = Objects.Add("pack", px, _world.Ground(px, pz), pz, 0, 0);
            foreach (var (item, count) in inv.Stacks)
            {
                int i = pack.Contents.FindIndex(c => c.Item == item);
                if (i >= 0) pack.Contents[i] = (item, pack.Contents[i].Count + count); else pack.Contents.Add((item, count));
            }
            dropped = " Everything you carried lies where you fell.";
        }
        inv.Clear();
        foreach (var s in Catalog.Start) inv.Add(s.Item!, s.Count);
        _inventoryChanged.Add(player);
        return new(true, "You died." + dropped);
    }

    /// <summary>Berries grow back on a picked bush after this many game hours (two days); pulled grass after three.</summary>
    public const double BerryRegrowHours = 48, GrassRegrowHours = 72;
    private double _regrowCheck;

    /// <summary>A player close to a moving object takes it over (their PC moves it from now on: they can push it).</summary>
    public bool Claim(int player, long objectId, double px, double pz)
    {
        if (Objects.Get(objectId) is not { } o || o.Owner == player || o.Owner == 0 && o.Kind != "trunk") return false;
        if (Dist(px, pz, o.X, o.Z) > TrunkLength(o.Size) / 2 + 3) return false;
        o.Owner = player;
        Objects.Touch(o.Id);
        return true;
    }

    /// <summary>A player left: the objects their PC moved stay where they are until someone takes them over.</summary>
    public void Release(int player)
    {
        foreach (var o in Objects.All.Where(o => o.Owner == player).ToList()) { o.Owner = 0; Objects.Touch(o.Id); }
    }

    /// <summary>Pick up what lies in a placed object within reach (a log pile): into the pack, the object goes.</summary>
    public ActionResult Gather(int player, long objectId, double px, double pz)
    {
        if (Objects.Get(objectId) is not { Contents.Count: > 0 } o) return ActionResult.Fail("Nothing to pick up there.");
        if (o.Kind == "carcass") return ActionResult.Fail("Butcher it first (E with a knife, an axe or a sword in the pack).");
        if (Dist(px, pz, o.X, o.Z) > UseReach + 1.5) return ActionResult.Fail("Too far away.");
        var inv = Inventory(player);
        foreach (var (item, count) in o.Contents) inv.Add(item, count);
        string what = string.Join(", ", o.Contents.Select(c => $"{c.Count} × {Catalog.Item(c.Item).Name.ToLowerInvariant()}"));
        Objects.Remove(o.Id);
        _inventoryChanged.Add(player);
        return new(true, $"Picked up {what}.");
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
        if (Objects.Get(objectId) is { Blueprint: true }) return Supply(player, objectId, px, pz);
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

    /// <summary>Advance the world's objects by <paramref name="minutes"/> of game time; picked bushes regrow.</summary>
    public void Step(double minutes)
    {
        Objects.Step(minutes);
        if ((_regrowCheck -= minutes) > 0) return;
        _regrowCheck = 10; // (every ten game minutes)
        double now = _world.Hours;
        foreach (var (layer, x, z) in _world.Removals.Keys())
            if (_world.Removals.Get(layer, x, z) is { } at
                && (layer == ObjectRemovals.Berries && now - at >= BerryRegrowHours || layer == ObjectRemovals.Grass && now - at >= GrassRegrowHours))
                _world.Removals.Restore(layer, x, z);
    }

    /// <summary>Under a shelter (D115): within a finished lean-to's reach, or under a finished roof (D121; at height
    /// <paramref name="y"/>, the feet) — the rain stays off, most of the wind.</summary>
    public static bool Sheltered(Catalog catalog, PlacedObjects objects, double x, double z, double y = double.NaN) =>
        objects.Near(x, z, 6).Any(o => !o.Blueprint && catalog.Objects.TryGetValue(o.Kind, out var d) && d.Tags.Contains("shelter") && Dist(x, z, o.X, o.Z) <= d.Reach)
        || !double.IsNaN(y) && objects.Near(x, z, 4).Any(o => !o.Blueprint && catalog.Piece(o.Kind) is { } p && p.Has("roof")
            && Building.ToWorld(new Pose(o.X, o.Y, o.Z, o.Yaw), p.BoxCenter).Y is var cy && cy > y + 1 && cy < y + 14
            && Building.Inside(p, new Pose(o.X, o.Y, o.Z, o.Yaw), new V3(x, cy, z)));

    /// <summary>Warmth (°C) the lit fires give at a point: full at the fire, nothing at the edge of its reach × 2.</summary>
    public static double FireWarmth(Catalog catalog, PlacedObjects objects, double x, double z) =>
        objects.Near(x, z, 12).Where(o => o.Lit && catalog.Objects.TryGetValue(o.Kind, out var d) && d.Warmth > 0)
            .Sum(o => { var d = catalog.Objects[o.Kind]; double k = 1 - Dist(x, z, o.X, o.Z) / (d.Reach * 2); return d.Warmth * Math.Clamp(k, 0, 1); });

    private static double Dist(double ax, double az, double bx, double bz) => Math.Sqrt((ax - bx) * (ax - bx) + (az - bz) * (az - bz));
}
