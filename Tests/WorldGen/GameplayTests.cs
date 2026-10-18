using Thornwake.Gameplay;
using Thornwake.WorldGen;

/// <summary>Step 1's placeholder mechanics (GAMEPLAY_DESIGN.md §14–15, D110): content, inventory, the host's rules, needs.</summary>
internal static class GameplayTests
{
    public static void Register(string? filter)
    {
        T.Run("Gameplay: the content files load and every reference resolves", ContentLoads, filter);
        T.Run("Gameplay: inventory stacks, takes by item and by tag, travels over the network", InventoryWorks, filter);
        T.Run("Gameplay: chop a tree, build a campfire, fuel it, cook, eat (host rules)", Slice, filter);
        T.Run("Gameplay: needs — hunger, cold, wet, a fire's warmth, health", NeedsWork, filter);
        T.Run("Gameplay: gathering — stones, berries (they grow back), grass for fibre; a fishing rod", Gathering, filter);
        T.Run("Gameplay: fishing — which fish where and when, the tug-of-war, landing one (D114)", FishingWorks, filter);
        T.Run("Gameplay: death — the pack drops where you fell, a new character starts with the kit (D115)", Death, filter);
        T.Run("Gameplay: saving — a world's changes and players by name, written and read back (D116)", Saving, filter);
        T.Run("Gameplay: hunting — animals graze, notice, flee, hunt; weapons, arrows, carcasses, butchering (D120)", Hunting, filter);
        T.Run("Gameplay: building — blueprints, materials, snapping, swapping shapes, overlaps, roofs shelter (D121)", BuildingWorks, filter);
    }

    private static Catalog Load() => new(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content/gameplay_items.json")),
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content/gameplay_recipes.json")),
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content/gameplay_animals.json")),
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content/gameplay_pieces.json")));

    private static void ContentLoads()
    {
        var c = Load();
        T.Check(c.Items.ContainsKey("stone_axe") && c.Item("stone_axe").Has("chopping"), "the axe chops");
        T.Check(c.Pieces["campfire"].Category == "Camp" && c.Objects["campfire"].Tags.Contains("fire"), "a campfire is a fire, built as a piece");
        T.Check(c.Animals.Count >= 4 && c.Animals["deer"].Yield.Any(y => y.Item == "raw_meat"), "animals load, deer give meat");
        T.Check(c.Categories.Contains("Wall") && c.Pieces.Values.Count(p => p.Category == "Roof") >= 10, "building categories and roof shapes");
        T.Check(c.Start.Count > 0, "a start kit");
        bool threw = false;
        try { _ = new Catalog("{\"items\":[]}", "{\"recipes\":[{\"id\":\"x\",\"inputs\":[{\"item\":\"nothing\"}]}]}"); } catch (FormatException) { threw = true; }
        T.Check(threw, "a recipe naming an unknown item is refused when loading");
    }

    private static void InventoryWorks()
    {
        var c = Load();
        var inv = new Inventory(c);
        inv.Add("branch", 45); // stacks of 30
        T.Check(inv.Stacks.Count == 2 && inv.Count("branch") == 45, "45 branches in two stacks");
        inv.Add("log", 3);
        T.Check(inv.CountTag("fuel") == 48 && inv.CountTag("heavy") == 3, "counted by tag");
        T.Near(inv.Weight, 45 * .8 + 3 * 8, 1e-9, "weight");
        T.Check(inv.Take(new Ingredient(null, "wood", 47)) is { } taken && taken.Sum(t => t.Count) == 47, "47 wood taken (any kind)");
        T.Check(inv.CountTag("wood") == 1, "one left");
        T.Check(inv.Take(new Ingredient("log", null, 5)) == null && inv.CountTag("wood") == 1, "too few: nothing taken");
        var copy = new Inventory(c);
        using (var ms = new MemoryStream())
        {
            using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) inv.Write(w);
            ms.Position = 0;
            using var r = new BinaryReader(ms);
            copy.Read(r);
        }
        T.Check(copy.Stacks.SequenceEqual(inv.Stacks), "the copy on the player's PC is the same");
    }

    private sealed class World : IWorldQuery
    {
        public ObjectRemovals Removals { get; } = new();
        public double Hours { get; set; } = 10;
        public (double X, double Z, double Scale, int Species)? Tree(long cx, long cz) => cx == 3 && cz == 4 ? (21.5, 29.0, 1.1, 5) : null;
        public (double X, double Z, double Speed) Wind { get; set; } = (1, 0, 0);
        public double Ground(double x, double z) => 5;
        public (double X, double Z) GatherSpot(int layer, long cx, long cz) => (cx * 3.5 + 1, cz * 3.5 + 1);
        public string WaterNear(double x, double z, double radius) => x > 1000 ? "river" : "";
        public IReadOnlySet<string> BiomeTags(double x, double z) => new HashSet<string> { "temperate", "forest" };
        public bool IsWater(double x, double z) => x > 1000;
        public List<(double X, double Z, double R)> Trees { get; } = new();
        public bool Obstacle(double x, double z, double radius) => Trees.Any(t => (t.X - x) * (t.X - x) + (t.Z - z) * (t.Z - z) < (t.R + radius) * (t.R + radius));
    }

    private static void Slice()
    {
        var c = Load();
        var world = new World();
        var rules = new GameRules(c, world);
        const int me = 7, other = 9;
        var inv = rules.Inventory(me);
        T.Check(inv.Count("stone_axe") == 1, "starts with an axe");
        T.Check(!rules.Chop(me, 3, 4, 40, 40).Ok, "too far from the tree");
        T.Check(!rules.Chop(me, 5, 5, 21, 29).Ok, "no tree there");
        T.Check(rules.Inventory(other).HasTag("chopping"), "every new character has an axe");
        // Felling in three acts (D112, §5a). Hits: the tree stands until the last one; every hit is an event for everyone
        // (it shakes, the notch grows). The notch faces where the first hit came from: I stand south-west of it.
        int hits = GameRules.TreeHits(1.1);
        for (int i = 1; i < hits; i++) T.Check(rules.Chop(me, 3, 4, 21, 28).Ok && !world.Removals.Contains(ObjectRemovals.Trees, 3, 4), $"hit {i} of {hits}: standing");
        var events = rules.TakeTreeEvents();
        T.Check(events.Count == hits - 1 && events.All(e => !e.Felled && e.Total == hits) && events[^1].HitsLeft == 1, $"{events.Count} hit events, the last with one hit left");
        T.Check(events[0].DirZ < -.8 && events[0].DirX < 0, $"the notch faces me: {events[0].DirX:0.00}, {events[0].DirZ:0.00}");
        // The last hit from the other side: it still falls toward the notch (no wind).
        var chop = rules.Chop(me, 3, 4, 22, 30);
        T.Check(chop.Ok && world.Removals.Contains(ObjectRemovals.Trees, 3, 4), "felled: " + chop.Message);
        T.Check(world.Removals.Get(ObjectRemovals.Trees, 3, 4) == 10, "with the time it fell");
        var fall = rules.TakeTreeEvents().Single();
        T.Check(fall.Felled && Math.Abs(fall.DirX - events[0].DirX) < 1e-9 && Math.Abs(fall.DirZ - events[0].DirZ) < 1e-9, $"it falls toward the notch: {fall.DirX:0.00}, {fall.DirZ:0.00}");
        T.Check(!rules.Chop(me, 3, 4, 21, 28).Ok, "only once");
        // Act 3: a bare trunk lies along the fall (mine to move), the crown's branches where it came down.
        var trunk = rules.Objects.All.Single(o => o.Kind == "trunk");
        T.Check(trunk.Owner == me && trunk.Z < 29 && trunk.Size == 1.1f && trunk.Variant == 5, $"a trunk lies toward the fall ({trunk.X:0.0}, {trunk.Z:0.0}), its PC is mine");
        var crownPile = rules.Objects.All.Single(o => o.Kind == "branch_pile");
        T.Check(crownPile.Contents.Single().Count == GameRules.CrownBranches(1.1), "branches where the crown came down");
        T.Check(inv.Count("log") == 0, "nothing in the pack yet");
        // Size sets the yield: the trunk's volume.
        T.Check(GameRules.TrunkLogs(.8) < GameRules.TrunkLogs(1.1) && GameRules.TrunkLogs(1.1) < GameRules.TrunkLogs(1.6), $"logs by size: {GameRules.TrunkLogs(.8)}, {GameRules.TrunkLogs(1.1)}, {GameRules.TrunkLogs(1.6)}");
        T.Check(GameRules.TreeHits(.8) < GameRules.TreeHits(1.6), "and the effort");
        // Moving it: only its owner's reports count; someone else close by takes it over (to push it).
        double x0 = trunk.X;
        T.Check(rules.Objects.Move(me, trunk.Id, x0 + 1, trunk.Y, trunk.Z, 0, 0, 0, 1) && trunk.X == x0 + 1 && rules.Objects.TakeMoved().Contains(trunk.Id), "it rolled a metre (my PC moves it; passed on)");
        T.Check(!rules.Objects.Move(other, trunk.Id, 0, 0, 0, 0, 0, 0, 1), "another's report is ignored");
        T.Check(!rules.Claim(other, trunk.Id, 200, 200), "not from afar");
        T.Check(rules.Claim(other, trunk.Id, trunk.X + 1, trunk.Z) && trunk.Owner == other, "taken over by the player pushing it");
        rules.Release(other);
        T.Check(trunk.Owner == 0, "a player leaving lets it go");
        // Cutting it up.
        int cuts = GameRules.TrunkHits(1.1);
        for (int i = 1; i < cuts; i++) T.Check(rules.ChopTrunk(me, trunk.Id, trunk.X, trunk.Z - 1).Ok && rules.Objects.Get(trunk.Id) != null, $"cut hit {i} of {cuts}");
        T.Check(!rules.ChopTrunk(me, trunk.Id, 200, 200).Ok, "not from afar");
        var split = rules.ChopTrunk(me, trunk.Id, trunk.X, trunk.Z - 1);
        var pile = rules.Objects.All.Single(o => o.Kind == "log_pile");
        T.Check(split.Ok && rules.Objects.Get(trunk.Id) == null && pile.Contents.Single() == ("log", GameRules.TrunkLogs(1.1)), $"split: {split.Message}");
        T.Check(!rules.Gather(me, pile.Id, 21, 100).Ok, "too far to pick it up");
        var gather = rules.Gather(me, pile.Id, pile.X, pile.Z - 2);
        T.Check(gather.Ok && inv.Count("log") == GameRules.TrunkLogs(1.1) && rules.Objects.Get(pile.Id) == null, $"picked up: {gather.Message}");
        T.Check(rules.Gather(me, crownPile.Id, crownPile.X, crownPile.Z - 1).Ok && inv.Count("branch") >= 4, "and the branches");
        T.Check(rules.TakeInventoryChanges().Contains(me), "my inventory is sent to me");
        rules.Objects.TakeChanged();
        // A strong wind has its say: notched from the south, in a 10 m/s west wind it falls north-east.
        var windy = new World { Wind = (1, 0, 10) };
        var rules2 = new GameRules(c, windy);
        for (int i = 0; i < hits; i++) rules2.Chop(me, 3, 4, 21.5, 26);
        var windFall = rules2.TakeTreeEvents().Last();
        T.Check(windFall.Felled && windFall.DirX > .3 && windFall.DirZ < -.5, $"in the wind it falls partly downwind: {windFall.DirX:0.00}, {windFall.DirZ:0.00}");

        T.Check(!rules.Make(me, "cook_fish", 21, 28, 0, 0, 0, 0).Ok, "no fish, no fire: cannot cook");
        T.Check(!rules.Place(me, "campfire", 40, 5, 40, 0, 0, 21, 28).Ok, "cannot build it out of reach");
        var blueprint = rules.Place(me, "campfire", 22, 5, 29.5, 0, 0, 21, 28);
        T.Check(blueprint.Ok && rules.Objects.All.Count == 1 && rules.Objects.All.First().Blueprint, "a campfire blueprint: " + blueprint.Message);
        var o = rules.Objects.All.First();
        T.Check(!o.Lit, "not burning yet");
        var fire = rules.Fuel(me, o.Id, 21, 28); // (E on a blueprint brings the materials)
        T.Check(fire.Ok && !o.Blueprint, "campfire built: " + fire.Message);
        T.Check(o.Lit && o.Fuel == c.Objects["campfire"].Fuel, "and lit");
        T.Check(inv.Count("log") >= 0 && rules.Objects.TakeChanged().Contains(o.Id), "sent to everyone");
        T.Check(GameRules.FireWarmth(c, rules.Objects, 22, 29.5) > 10 && GameRules.FireWarmth(c, rules.Objects, 40, 40) == 0, "warm beside it, not far away");

        inv.Add("raw_fish", 2);
        T.Check(rules.StationNear("fire", 21, 28), "the fire is a working station");
        var cook = rules.Make(me, "cook_fish", 21, 28, 0, 0, 0, 0);
        T.Check(cook.Ok && inv.Count("cooked_fish") == 1 && inv.Count("raw_fish") == 1, "cooked: " + cook.Message);
        T.Check(!rules.Make(me, "cook_fish", 35, 35, 0, 0, 0, 0).Ok, "not away from the fire");

        rules.Step(o.Fuel + 1);
        T.Check(!o.Lit && rules.Objects.TakeChanged().Contains(o.Id), "it burns out (and everyone sees it)");
        T.Check(!rules.Make(me, "cook_fish", 21, 28, 0, 0, 0, 0).Ok, "a cold fire cooks nothing");
        inv.Add("log", 1);
        var fuel = rules.Fuel(me, o.Id, 21, 28);
        T.Check(fuel.Ok && o.Lit && o.Fuel == c.Item("log").Fuel, "a log relights it: " + fuel.Message);
        T.Check(!rules.Fuel(me, o.Id, 40, 40).Ok, "not from afar");

        var eat = rules.Eat(me, "cooked_fish");
        T.Check(eat.Ok && eat.Food == c.Item("cooked_fish").Food && inv.Count("cooked_fish") == 0, "eaten: " + eat.Message);
        T.Check(!rules.Eat(me, "log").Ok, "logs are not food");
    }

    private static void Gathering()
    {
        var c = Load();
        var world = new World();
        var rules = new GameRules(c, world);
        const int me = 7, other = 9;
        var inv = rules.Inventory(me);
        T.Check(!rules.Forage(me, "stone", 10, 10, 0, 0, 0, 0).Ok, "a stone 50 m away: too far");
        T.Check(rules.Forage(me, "stone", 10, 10, 0, 0, 36, 36).Ok && inv.Count("stone") == 1, "picked up a stone");
        T.Check(world.Removals.Contains(ObjectRemovals.Stones, 10, 10), "it is gone from the world (for everyone)");
        T.Check(!rules.Forage(other, "stone", 10, 10, 0, 0, 36, 36).Ok, "nobody picks it up twice");
        T.Check(rules.Forage(me, "berries", 2, 3, 0, 0, 8, 11).Ok && inv.Count("berries") == 4, "picked berries");
        T.Check(!rules.Forage(me, "berries", 2, 3, 0, 0, 8, 11).Ok, "the bush is picked");
        world.Hours += 47; rules.Step(30);
        T.Check(world.Removals.Contains(ObjectRemovals.Berries, 2, 3), "47 hours later: still bare");
        world.Hours += 2; rules.Step(30);
        T.Check(!world.Removals.Contains(ObjectRemovals.Berries, 2, 3) && world.Removals.Contains(ObjectRemovals.Stones, 10, 10), "two days later the berries are back (the stone is not)");
        T.Check(rules.Eat(me, "berries").Ok && inv.Count("berries") == 3, "berries are food");
        T.Check(!rules.Forage(me, "grass", 0, 0, 50, 50, 0, 0).Ok, "grass out of reach");
        T.Check(!rules.Forage(me, "grass", 0, 0, 6, 6, 6, 6, py: 8.5).Ok, "not from a platform 3.5 m above the ground");
        for (int i = 0; i < 2; i++) T.Check(rules.Forage(me, "grass", 0, 0, 1 + 3 * i, 1, 1 + 3 * i, 1).Ok, "grass pulled");
        T.Check(inv.Count("plant_fibre") == 4, "4 plant fibre");
        T.Check(world.Removals.Contains(ObjectRemovals.Grass, 1, 1) && world.Removals.Contains(ObjectRemovals.Grass, 0, 0) && world.Removals.Contains(ObjectRemovals.Grass, 2, 2), "the grass around the spot is gone (a 3 × 3 m patch)");
        T.Check(!rules.Forage(me, "grass", 0, 0, 1, 1, 1, 1).Ok, "nothing left to pull there");
        world.Hours += 73; rules.Step(30);
        T.Check(!world.Removals.Contains(ObjectRemovals.Grass, 1, 1), "three days later it has grown back");
        inv.Add("branch", 1);
        var rod = rules.Make(me, "fishing_rod", 0, 0, 0, 0, 0, 0);
        T.Check(rod.Ok && inv.HasTag("fishing") && inv.Count("plant_fibre") == 0, "a fishing rod: " + rod.Message);
    }

    private static void FishingWorks()
    {
        var c = Load();
        T.Check(c.Fish.Count >= 6 && c.Item("fishing_rod").Prop("strength") > 0, "fish and the rod's strength load");
        var rng = new Random(5);
        // Which fish: only those living in that water; dusk hunters more often at dusk.
        for (int i = 0; i < 200; i++) T.Check(Fishing.Pick(c.Fish, "river", 12, rng) is { } f && f.Waters.Contains("river"), "a river fish in a river");
        T.Check(Fishing.Pick(c.Fish, "desert", 12, rng) == null, "no fish without water");
        int pikeNoon = 0, pikeDusk = 0;
        for (int i = 0; i < 2000; i++) { if (Fishing.Pick(c.Fish, "lake", 12, rng)!.Id == "pike") pikeNoon++; if (Fishing.Pick(c.Fish, "lake", 19, rng)!.Id == "pike") pikeDusk++; }
        T.Check(pikeDusk > pikeNoon * 2, $"pike bite at dusk: {pikeDusk} vs {pikeNoon} at noon (of 2000)");
        T.Check(Fishing.BiteDelay(6, 0, new Random(1)) < Fishing.BiteDelay(13, 0, new Random(1)), "quicker bites at dawn");
        // The tug-of-war: always against the pull and let go when it rests — landed; doing nothing — lost.
        var pike = c.Fish.First(f => f.Id == "pike");
        var good = new FishFight(pike, 1, 1, new Random(3));
        double t = 0;
        while (!good.Over && t < 120) { good.Step(.05, -good.Pull); t += .05; }
        T.Check(good.Landed && good.Line > .99, $"perfect play lands a pike in {t:0} s, the line untouched");
        var strain = new FishFight(pike, 1, 1, new Random(3));
        double highest = 0, held = 0;
        for (int i = 0; i < 400 && !strain.Over; i++) { strain.Step(.05, 0); if (strain.Pull != 0) highest = Math.Max(highest, strain.Tension); }
        var answered = new FishFight(pike, 1, 1, new Random(3));
        for (int i = 0; i < 400 && !answered.Over; i++) { answered.Step(.05, -answered.Pull); if (answered.Pull != 0) held = Math.Max(held, answered.Tension); }
        T.Check(highest > .9 && held < .7, $"tension: unanswered {highest:0.00}, answered at most {held:0.00}");
        // A quarter of a second late at every turn: still landed, the line worn.
        var late = new FishFight(pike, 1, 1, new Random(3));
        int seen = 0; double since = 0; t = 0;
        while (!late.Over && t < 120) { if (late.Pull != seen) { since += .05; if (since >= .25) { seen = late.Pull; since = 0; } } late.Step(.05, -seen); t += .05; }
        T.Check(late.Landed && late.Line < 1, $"a slow hand lands it too, the line at {late.Line:0.00}");
        var lazy = new FishFight(pike, 1, 1, new Random(3));
        t = 0;
        while (!lazy.Over && t < 120) { lazy.Step(.05, 0); t += .05; }
        T.Check(lazy.Lost, $"doing nothing loses it ({t:0} s)");
        var wrong = new FishFight(pike, 1, 1, new Random(3));
        while (!wrong.Over) wrong.Step(.05, wrong.Pull);
        T.Check(wrong.Lost, "pulling with it loses it");
        var strong = new FishFight(pike, 2, 1, new Random(3));
        double t2 = 0;
        while (!strong.Over && t2 < 120) { strong.Step(.05, -strong.Pull); t2 += .05; }
        T.Check(strong.Landed, "a stronger rod lands it too");
        // Landing it: the host's checks.
        var world = new World();
        var rules = new GameRules(c, world);
        var inv = rules.Inventory(7);
        T.Check(!rules.Catch(7, "trout", 1, 2000, 0).Ok, "no rod");
        inv.Add("fishing_rod");
        T.Check(!rules.Catch(7, "trout", 1, 2000, 0).Ok, "no bait");
        inv.Add("bait", 3);
        T.Check(!rules.Catch(7, "trout", 1, 0, 0).Ok, "no water here");
        T.Check(!rules.Catch(7, "cod", 1, 2000, 0).Ok && inv.CountTag("bait") == 3, "a sea fish in a river: refused, the bait kept");
        var trout = rules.Catch(7, "trout", 1.1, 2000, 0);
        T.Check(trout.Ok && inv.Count("raw_fish") == 1 && inv.CountTag("bait") == 2, "landed: " + trout.Message);
        T.Check(!rules.Catch(7, "trout", 1, 2000, 0).Ok, "not again at once");
        world.Hours += .1;
        var away = rules.Catch(7, "", 0, 2000, 0);
        T.Check(away.Ok && inv.CountTag("bait") == 1 && inv.Count("raw_fish") == 1, "lost one: the bait is gone — " + away.Message);
    }

    private static void Death()
    {
        var c = Load();
        var rules = new GameRules(c, new World());
        var inv = rules.Inventory(7);
        inv.Add("log", 3); inv.Add("berries", 5);
        int axes = inv.Count("stone_axe");
        var died = rules.Die(7, 40, 41);
        var pack = rules.Objects.All.Single(o => o.Kind == "pack");
        T.Check(died.Ok && pack.X == 40 && pack.Z == 41 && pack.Contents.Contains(("log", 3)) && pack.Contents.Contains(("berries", 5)), $"the pack lies where I fell: {died.Message}");
        T.Check(inv.Count("log") == 0 && inv.Count("stone_axe") == axes, "a new character with the start kit");
        T.Check(rules.Gather(9, pack.Id, 40, 42).Ok && rules.Inventory(9).Count("log") == 3, "anyone can pick it up (a corpse run — or a thief)");
        // A lean-to for a cold, rainy night.
        inv.Add("log", 3); inv.Add("branch", 8); inv.Add("plant_fibre", 4);
        T.Check(rules.Place(7, "lean_to", 2, 5, 0, 0, 0, 0, 0).Ok && !GameRules.Sheltered(c, rules.Objects, 2.5, .5), "a lean-to blueprint shelters nobody");
        var built = rules.Supply(7, rules.Objects.All.Single(o => o.Kind == "lean_to").Id, 0, 0);
        T.Check(built.Ok && GameRules.Sheltered(c, rules.Objects, 2.5, .5) && !GameRules.Sheltered(c, rules.Objects, 8, 0), "a lean-to: sheltered under it, not beside it — " + built.Message);
        var rainyNight = new BodyInput(Air: 2, Wind: 7, Rain: .9, Swimming: false, Fire: 0, Effort: 0);
        var under = rainyNight with { Rain = 0, Wind = 7 * .3, Fire = 9 };
        Needs open = new() { Warmth = 70 }, snug = new() { Warmth = 70 };
        for (int i = 0; i < 16; i++) { open.Step(.5, rainyNight); snug.Step(.5, under); }
        T.Check(open.Health < 100 && snug.Health >= 99 && snug.Warmth > 45, $"eight hours of cold rain: out in it health {open.Health:0}, warmth {open.Warmth:0}; under the lean-to by a fire health {snug.Health:0}, warmth {snug.Warmth:0}");
    }

    private static void Saving()
    {
        var c = Load();
        var world = new World();
        var rules = new GameRules(c, world);
        rules.Name(1, GameRules.HostName); rules.Name(9, "Bea");
        rules.Inventory(1).Add("log", 4); rules.Inventory(9).Add("stone", 2);
        for (int i = 0; i < GameRules.TreeHits(1.1); i++) rules.Chop(1, 3, 4, 21, 28);   // a felled tree, a trunk and branches
        rules.Forage(1, "stone", 10, 10, 0, 0, 36, 36);
        // A blueprint half supplied and an open door (D121): what they hold and their state are saved.
        int had = rules.Inventory(1).Count("log");
        rules.Inventory(1).Take("log", had); rules.Inventory(1).Add("log", 2);
        rules.Place(1, "log_foundation", 30, 5.3, 30, 0, 0, 28, 28);
        rules.Supply(1, rules.Objects.All.Single(o => o.Kind == "log_foundation").Id, 28, 28);
        rules.Inventory(1).Add("log", had);
        var edits = new TerrainEdits();
        edits.Apply(5, 5, 3, -1.5);
        var save = new WorldSave { WorldArgs = "--preset=mountainous --seed=42", Settings = { ("time.day_length", 90) }, Day = 3, Phase = .41, CloudDrift = new[] { 1.5, -2.0 }, Label = "test" };
        save.Capture(edits, world.Removals, rules.Objects);
        foreach (var (name, bytes) in rules.SaveInventories()) save.Players[name] = new PlayerSave { Inventory = bytes };
        save.Players[GameRules.HostName].HasBody = true; save.Players[GameRules.HostName].X = 12.5; save.Players[GameRules.HostName].Health = 64;
        var ms = new MemoryStream();
        save.Write(ms);
        ms.Position = 0;
        var back = WorldSave.Read(ms);
        T.Check(back.WorldArgs == save.WorldArgs && back.Day == 3 && back.Phase == .41 && back.CloudDrift.SequenceEqual(save.CloudDrift) && back.Settings.Single() == ("time.day_length", 90), "the world, its rules, the clock, the clouds");
        // A fresh world (regenerated from the seed) with the changes put back.
        var world2 = new World();
        var rules2 = new GameRules(c, world2);
        var edits2 = new TerrainEdits();
        back.Restore(edits2, world2.Removals, rules2.Objects);
        T.Near(edits2.Delta(5, 5), edits.Delta(5, 5), 0, "the dug ground");
        T.Check(world2.Removals.Contains(ObjectRemovals.Trees, 3, 4) && world2.Removals.Contains(ObjectRemovals.Stones, 10, 10), "the felled tree and the picked stone");
        T.Check(rules2.Objects.All.Count == rules.Objects.All.Count && rules2.Objects.All.Any(o => o.Kind == "trunk" && o.Variant == 5), "the trunk and the branches");
        T.Check(rules2.Objects.TakeChanged().Count == 0, "restored, not changed (nothing to send)");
        T.Check(rules2.Objects.All.Single(o => o.Kind == "log_foundation") is { Blueprint: true } bp2 && bp2.Supplied.Single() == ("log", 2), "the blueprint with its two logs");
        rules2.RestoreInventories(back.Players.ToDictionary(p => p.Key, p => p.Value.Inventory));
        rules2.Name(1, GameRules.HostName);
        T.Check(rules2.Inventory(1).Count("log") == 4, "the host's pack");
        T.Check(rules2.Inventory(5).Count("stone") == 0, "a stranger: a new pack");
        rules2.Name(5, "Bea");
        T.Check(rules2.Inventory(5).Count("stone") == 2, "Bea comes back (another id): her pack is hers");
        T.Check(back.Players[GameRules.HostName].HasBody && back.Players[GameRules.HostName].X == 12.5 && back.Players[GameRules.HostName].Health == 64, "where the host was, and how");
        bool threw = false;
        try { WorldSave.Read(new MemoryStream(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 })); } catch (InvalidDataException) { threw = true; }
        T.Check(threw, "not a save: refused");
    }

    private static void Hunting()
    {
        var c = Load();
        var world = new World { Hours = 12 };
        var rules = new GameRules(c, world);
        rules.Wildlife.Density = 0; // (only the animals put down here)
        const int me = 7;
        var deer = c.Animals["deer"];
        var a = rules.Wildlife.Spawn(deer, 100, 100);
        var far = new[] { new Hunter(me, 100, 5, 250, 0, false) };
        for (int i = 0; i < 50; i++) rules.StepWildlife(.1, far);
        T.Check(a.State is AnimalState.Graze or AnimalState.Idle or AnimalState.Walk && Math.Sqrt((a.X - 100) * (a.X - 100) + (a.Z - 100) * (a.Z - 100)) < 25, $"alone it grazes and wanders ({a.State})");
        // A hunter walking up slowly is seen at 25 m: it looks up, then runs.
        var near = new[] { new Hunter(me, a.X, 5, a.Z + 25, 1, false) };
        rules.StepWildlife(.25, near);
        T.Check(a.State == AnimalState.Alert, $"it looks up ({a.State})");
        for (int i = 0; i < 25; i++) rules.StepWildlife(.1, near);
        T.Check(a.State == AnimalState.Flee && a.Z < near[0].Z - 30, $"and flees ({a.State}, {near[0].Z - a.Z:0} m away)");
        // Heard: a sprinter is noticed from 30 m in the dark, a still hunter not.
        var b = rules.Wildlife.Spawn(deer, 300, 300);
        world.Hours = 23;
        rules.StepWildlife(.2, new[] { new Hunter(me, 300, 5, 330, 0, false) });
        T.Check(b.State != AnimalState.Alert && b.State != AnimalState.Flee, "a still hunter at night, 30 m away: unnoticed");
        rules.StepWildlife(.2, new[] { new Hunter(me, 300, 5, 330, 9, false) });
        T.Check(b.State is AnimalState.Alert or AnimalState.Flee, "a sprinting one: heard");
        world.Hours = 12;
        // A herd runs together.
        rules.Wildlife.Clear();
        var d1 = rules.Wildlife.Spawn(deer, 0, 0, group: 50); var d2 = rules.Wildlife.Spawn(deer, 5, 0, group: 50);
        var close = new[] { new Hunter(me, 0, 5, 8, 1, false) };
        for (int i = 0; i < 20; i++) rules.StepWildlife(.1, close);
        T.Check(d1.State == AnimalState.Flee && d2.State == AnimalState.Flee, "one is startled: the herd runs");
        // Weapons: reach and pace; two spear thrusts kill a deer; it becomes a carcass.
        rules.Wildlife.Clear();
        var inv = rules.Inventory(me);
        T.Check(!rules.Attack(me, 1, "spear", 0, 0).Ok, "no spear in the pack");
        inv.Add("spear");
        var target = rules.Wildlife.Spawn(deer, 50, 50);
        T.Check(!rules.Attack(me, target.Id, "spear", 50, 60).Ok, "out of reach");
        var hit = rules.Attack(me, target.Id, "spear", 50, 52.5);
        T.Check(hit.Ok && target.Health == deer.Health - c.Item("spear").Prop("damage") && target.State == AnimalState.Flee, "a thrust: wounded, it flees — " + hit.Message);
        T.Check(!rules.Attack(me, target.Id, "spear", target.X, target.Z - 2).Ok, "not again at once");
        rules.StepWildlife(1.1, far);
        var kill = rules.Attack(me, target.Id, "spear", target.X, target.Z - 2);
        T.Check(kill.Ok && target.State == AnimalState.Dead, "the second kills it: " + kill.Message);
        for (int i = 0; i < 30; i++) rules.StepWildlife(.1, far);
        var carcass = rules.Objects.All.Single(o => o.Kind == "carcass");
        T.Check(carcass.Ref == "deer" && carcass.Contents.Contains(("raw_meat", 4)) && rules.Wildlife.Get(target.Id) == null, "a deer's carcass, with its meat");
        T.Check(!rules.Gather(me, carcass.Id, carcass.X, carcass.Z).Ok, "not picked up whole: butchered");
        T.Check(!rules.Butcher(me, carcass.Id, carcass.X + 30, carcass.Z).Ok, "not from afar");
        var butcher = rules.Butcher(me, carcass.Id, carcass.X, carcass.Z + 1);
        T.Check(butcher.Ok && inv.Count("raw_meat") == 4 && inv.Count("hide") == 1 && inv.Count("bone") == 2 && rules.Objects.Get(carcass.Id) == null, butcher.Message);
        T.Check(GameRules.ButcherSeconds(c, inv, "deer") == deer.ButcherSeconds, "with the axe it takes the deer's time");
        inv.Add("stone_knife");
        T.Check(GameRules.ButcherSeconds(c, inv, "deer") < deer.ButcherSeconds, "a knife is quicker");
        // The bow: an arrow from the pack each shot; a full draw does the bow's damage; a miss may leave the arrow.
        T.Check(!rules.Shoot(me, 0, 1, 0, 5, 0, 0, 0).Ok, "no bow");
        inv.Add("bow"); inv.Add("arrow", 10);
        var stag = rules.Wildlife.Spawn(c.Animals["stag"], 0, 40);
        var shot = rules.Shoot(me, stag.Id, 1, 0, 6, 40, 0, 0);
        T.Check(shot.Ok && Math.Abs(stag.Health - (c.Animals["stag"].Health - c.Item("bow").Prop("damage"))) < 1e-9 && inv.Count("arrow") == 9, "a full draw: the bow's damage — " + shot.Message);
        T.Check(!rules.Shoot(me, stag.Id, 1, 0, 6, 40, 0, 0).Ok, "an arrow at a time");
        for (int i = 0; i < 8; i++) { rules.StepWildlife(.5, far); rules.Shoot(me, 0, .5, 10 + i, 5, 30, 0, 0); }
        int stuck = rules.Objects.All.Count(o => o.Kind == "arrow");
        T.Check(inv.Count("arrow") == 1 && stuck is >= 4 and <= 8, $"8 misses: {stuck} arrows lie where they fell (most can be found again)");
        T.Check(!rules.Gather(me, rules.Objects.All.First(o => o.Kind == "arrow").Id, 100, 100).Ok, "not from afar");
        // Wolves hunt at night: a pack chases a hunter close by and bites.
        rules.Wildlife.Clear();
        world.Hours = 23;
        var wolf = rules.Wildlife.Spawn(c.Animals["wolf"], 0, -15, group: 9);
        var hunter = new[] { new Hunter(me, 0, 5, 0, 0, false) };
        var bites = new List<Hurt>();
        for (int i = 0; i < 60; i++) { rules.StepWildlife(.1, hunter); bites.AddRange(rules.TakeHurts()); }
        T.Check(bites.Count > 0 && bites.All(h => h.Player == me && h.Damage == c.Animals["wolf"].Damage), $"the wolf bites ({bites.Count} bites in 6 s)");
        // Not through a floor (Tom, build 68): on a platform 3 m up, no bites — it waits below.
        rules.Wildlife.Clear();
        var below = rules.Wildlife.Spawn(c.Animals["wolf"], 0, -1, group: 10);
        var up = new[] { new Hunter(me, 0, 8, 0, 0, false) };
        rules.TakeHurts();
        for (int i = 0; i < 60; i++) rules.StepWildlife(.1, up);
        T.Check(rules.TakeHurts().Count == 0 && below.State is AnimalState.Chase or AnimalState.Attack, $"a wolf under a platform bites nobody ({below.State})");
        rules.Wildlife.Clear();
        wolf = rules.Wildlife.Spawn(c.Animals["wolf"], 0, -1.2, group: 9);
                // Round trees and through no walls (Tom, build 69; D135): a deer fleeing straight at a trunk goes round it, and one
        // fleeing at a row of walls never passes through them.
        var woods = new World { Hours = 12 };
        woods.Trees.Add((0, 6, .5));
        var wr = new GameRules(c, woods) { Testing = true };
        wr.Wildlife.Density = 0;
        var runner = wr.Wildlife.Spawn(deer, 0, 0);
        runner.Yaw = 0;
        var chaser = new[] { new Hunter(me, 0, 5, -6, 1, false) };
        double nearest = double.MaxValue, farthest = 0;
        for (int i = 0; i < 60; i++)
        {
            wr.StepWildlife(.1, chaser);
            nearest = Math.Min(nearest, Math.Sqrt(runner.X * runner.X + (runner.Z - 6) * (runner.Z - 6)));
            farthest = Math.Max(farthest, runner.Z);
        }
        T.Check(nearest > .5 + deer.Radius && farthest > 12, $"round the tree: never nearer its trunk than {nearest:0.00} m, on to {farthest:0} m");
        foreach (double x in new[] { -3.0, 0, 3 }) wr.Place(me, "log_wall", x, 5, 30, 0, 0, x, 25);
        var walls = wr.PiecesNear(0, 30, 10).ToList();
        var runner2 = wr.Wildlife.Spawn(deer, 0, 24);
        runner2.Yaw = 0;
        var chaser2 = new[] { new Hunter(me, 0, 5, 18, 1, false) };
        bool through = false;
        for (int i = 0; i < 80; i++)
        {
            wr.StepWildlife(.1, chaser2);
            if (walls.Any(p => Building.Inside(p.Def, p.Pose, new V3(runner2.X, runner2.Y + .5, runner2.Z)))) through = true;
        }
        T.Check(!through && (runner2.Z < 30 || Math.Abs(runner2.X) > 4.4), $"not through the walls (it ended at {runner2.X:0.0}, {runner2.Z:0.0})");
        // Hurt badly, it runs.
        wolf.Health = c.Animals["wolf"].Health * .2;
        for (int i = 0; i < 5; i++) rules.StepWildlife(.1, hunter);
        T.Check(wolf.State == AnimalState.Flee, $"a badly hurt wolf runs ({wolf.State})");
        // Animals appear out of sight around a hunter (in a forest), and none when the sandbox says none.
        world.Hours = 12;
        var wild = new GameRules(c, world);
        for (int i = 0; i < 300; i++) wild.StepWildlife(.1, hunter);
        T.Check(wild.Wildlife.All.Count is > 0 and <= Wildlife.PerArea + 4, $"{wild.Wildlife.All.Count} animals around");
        T.Check(wild.Wildlife.All.All(x => Math.Sqrt(x.X * x.X + x.Z * x.Z) > 50), "none appeared close by");
        var none = new GameRules(c, world);
        none.Wildlife.Density = 0;
        for (int i = 0; i < 300; i++) none.StepWildlife(.1, hunter);
        T.Check(none.Wildlife.All.Count == 0, "animals off: none");
        // Gone when nobody is near.
        for (int i = 0; i < 30; i++) wild.StepWildlife(.1, new[] { new Hunter(me, 5000, 5, 5000, 0, true) });
        T.Check(wild.Wildlife.All.All(x => Math.Sqrt((x.X - 5000) * (x.X - 5000) + (x.Z - 5000) * (x.Z - 5000)) < Wildlife.KeepRadius), "the animals left behind are gone");
        // What a player's PC is sent.
        var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, System.Text.Encoding.UTF8, true)) rules.Wildlife.Write(w, 0, 0, 200);
        ms.Position = 0;
        var copy = Wildlife.Read(new BinaryReader(ms), c.Animals);
        T.Check(copy.Count == 1 && copy[0].Def.Id == "wolf" && Math.Abs(copy[0].X - wolf.X) < 1e-9 && copy[0].State == wolf.State, "animals over the network");
    }

    private static void BuildingWorks()
    {
        var c = Load();
        var world = new World();
        var rules = new GameRules(c, world);
        const int me = 7;
        var inv = rules.Inventory(me);
        var foundation = c.Pieces["log_foundation"];
        // A blueprint first, then the materials.
        var placed = rules.Place(me, "log_foundation", 0, 5.3, 0, 0, 0, 0, -4);
        var f = rules.Objects.All.Single();
        T.Check(placed.Ok && f.Blueprint && inv.Count("log") == 0, "a foundation blueprint costs nothing yet: " + placed.Message);
        T.Check(!rules.Supply(me, f.Id, 0, -4).Ok, "no logs in the pack: " + rules.Supply(me, f.Id, 0, -4).Message);
        inv.Add("log", 2);
        var part = rules.Supply(me, f.Id, 0, -4);
        T.Check(part.Ok && f.Blueprint && f.Supplied.Single() == ("log", 2), "two logs brought: " + part.Message);
        inv.Add("log", 20);
        var done = rules.Supply(me, f.Id, 0, -4);
        T.Check(done.Ok && !f.Blueprint && inv.Count("log") == 18 && f.Supplied.Single() == ("log", 4), "finished with two more: " + done.Message);
        T.Check(!rules.Place(me, "log_foundation", 50, 5.3, 50, 0, 0, 0, -4).Ok, "out of reach");
        T.Check(!rules.Place(me, "log_foundation", 1, 5.3, 0, 0, 0, 0, -4).Ok, "overlapping the first: in the way");
        T.Check(!rules.Place(me, "log_foundation", 6, 30, 0, 0, 0, 0, -4).Ok, "hanging in the air: no ground");
        // Snapping: a foundation beside it, aimed a little off, goes edge to edge.
        var pieces = rules.PiecesNear(0, 0, 12).ToList();
        var next = Building.Snap(foundation, new V3(3.3, 5.3, .4), .2, pieces);
        T.Check(next is { } n && Math.Abs(n.Pose.X - 3) < 1e-6 && Math.Abs(n.Pose.Z) < 1e-6 && Math.Abs(n.Pose.Y - 5.3) < 1e-6 && n.Joints == 5, $"a foundation snaps beside it: {next}");
        // A wall on its east edge: facing it (turned along the edge) it stands along the edge, both ends on the corners;
        // turned a quarter, it stands across the deck on a grid line (an inner wall; Tom, build 65: the player's turn decides).
        var wall = c.Pieces["log_wall"];
        foreach (double facing in new[] { 1.2, 1.9, 4.5 })
        {
            var w = Building.Snap(wall, new V3(1.4, 5.3, .3), facing, pieces);
            T.Check(w is { } wp && Math.Abs(wp.Pose.X - 1.5) < 1e-6 && Math.Abs(wp.Pose.Z) < 1e-6 && Math.Abs(Math.Cos(wp.Pose.Yaw)) < 1e-6 && wp.Joints == 5,
                $"facing {facing:0.0}: the wall stands along the edge ({w})");
        }
        var across = Building.Snap(wall, new V3(.2, 5.3, .1), 0, pieces, new V3(0, 1, 0));
        T.Check(across is { } aw && Math.Abs(Math.Sin(aw.Pose.Yaw)) < 1e-6 && Math.Abs(aw.Pose.Y - 5.3) < 1e-6 && aw.Joints == 5,
            $"turned across: an inner wall on the deck's grid ({across})");
        // Stairs and half floors face the way they are turned (R): each quarter turn.
        var half = c.Pieces["log_floor_half"];
        foreach (double turn in new[] { 0, Math.PI / 2, Math.PI, 3 * Math.PI / 2 })
        {
            var h = Building.Snap(half, new V3(.4, 5.3, .4), turn, pieces, new V3(0, 1, 0));
            T.Check(h is { } hp && Math.Abs(Math.IEEERemainder(hp.Pose.Yaw - turn, Math.PI)) < 1e-6, $"a half floor turned {turn:0.0}: {h}");
        }
        // Tom (build 63): a wall aimed at a foundation's top went under it (hanging from the edge: as many joints as on
        // top). Aimed anywhere along the edges, from any heading, it stands on the deck.
        var up = new V3(0, 1, 0);
        int under = 0, tried = 0;
        // (on the deck near its edges, and on the ground just outside them: the aim slipping past the edge)
        foreach (var (ax, ay, az) in new[] { (1.4, 5.3, .3), (1.3, 5.3, -1.2), (-1.35, 5.3, .9), (.2, 5.3, 1.4), (-.7, 5.3, -1.4), (1.45, 5.3, 1.45), (1.1, 5.3, 0.0),
                     (1.7, 5.0, .3), (1.8, 5.0, -1.0), (-1.7, 5.0, .4), (.3, 5.0, 1.75), (-1.0, 5.0, -1.8), (1.75, 5.0, 1.75) })
            foreach (double facing in new[] { 0, .8, 1.6, 2.4, 3.2, 4.0, 4.8, 5.6 })
            {
                tried++;
                if (Building.Snap(wall, new V3(ax, ay, az), facing, pieces, up) is not { } s || Math.Abs(s.Pose.Y - 5.3) > 1e-6) under++;
            }
        T.Check(under == 0, $"a wall aimed at a foundation's top stands on it ({under} of {tried} went elsewhere)");
        var wpose = Building.Snap(wall, new V3(1.4, 5.3, .3), 1.2, pieces)!.Value.Pose;
        var wallPlaced = rules.Place(me, "log_wall", wpose.X, wpose.Y, wpose.Z, wpose.Yaw, 0, 0, -4);
        T.Check(wallPlaced.Ok, "the wall's blueprint: " + wallPlaced.Message);
        T.Check(!rules.Place(me, "log_wall", wpose.X, wpose.Y, wpose.Z, wpose.Yaw + Math.PI, 0, 0, -4).Ok, "a second wall in the same place: in the way");
        var wallObj = rules.Objects.All.Single(o => o.Kind == "log_wall");
        T.Check(rules.Supply(me, wallObj.Id, 0, -4).Ok && !wallObj.Blueprint && inv.Count("log") == 15, "the wall built");
        // A roof slope on its top, going up inward (toward −X) as the player faces it from outside.
        pieces = rules.PiecesNear(0, 0, 12).ToList();
        var roof = c.Pieces["log_roof_slope_steep"];
        T.Check(!rules.Place(me, roof.Id, 20, 9, 0, 0, 0, 15, 0).Ok, "a roof on nothing: refused");
        var rs = Building.Snap(roof, new V3(1.5, 8.3, .2), -Math.PI / 2, pieces);
        T.Check(rs is { } r && Math.Abs(r.Pose.X) < 1e-6 && Math.Abs(r.Pose.Y - 8.3) < 1e-6 && Math.Abs(r.Pose.Z) < 1e-6 && r.Joints == 5, $"the roof's eave sits along the wall top: {rs}");
        var rp = rs!.Value.Pose;
        T.Check(rules.Place(me, roof.Id, rp.X, rp.Y, rp.Z, rp.Yaw, 0, 0, -4).Ok, "the roof's blueprint");
        var roofObj = rules.Objects.All.Single(o => o.Kind == roof.Id);
        T.Check(!GameRules.Sheltered(c, rules.Objects, 0, 0, 5.3), "an unfinished roof keeps no rain off");
        rules.Supply(me, roofObj.Id, 0, -4);
        T.Check(GameRules.Sheltered(c, rules.Objects, 0, 0, 5.3) && !GameRules.Sheltered(c, rules.Objects, 0, 4, 5.3) && !GameRules.Sheltered(c, rules.Objects, 0, 0, 12), "under the finished roof: sheltered (not beside it, not above it)");
        // Another shape of the same category in its place (Tom: build a wall, then decide it needs a window): the
        // wall's logs come back and go into the window wall.
        var swap = rules.Place(me, "log_wall_window", 0, 0, 0, wallObj.Yaw, wallObj.Id, 0, -4);
        var window = rules.Objects.All.SingleOrDefault(o => o.Kind == "log_wall_window");
        T.Check(swap.Ok && rules.Objects.Get(wallObj.Id) == null && window != null && !window.Blueprint && Math.Abs(window.X - wallObj.X) < 1e-9 && inv.Count("log") == 12, "swapped for a window wall: " + swap.Message);
        T.Check(!rules.Place(me, "log_door", 0, 0, 0, 0, window!.Id, 0, -4).Ok, "a door is not a wall");
        // A doorway and a door in it; E opens it.
        var doorway = rules.Place(me, "log_wall_doorway", 0, 0, 0, window.Yaw, window.Id, 0, -4);
        var dw = rules.Objects.All.Single(o => o.Kind == "log_wall_doorway");
        T.Check(doorway.Ok && !dw.Blueprint && inv.Count("log") == 13, "a doorway (one log fewer): " + doorway.Message);
        pieces = rules.PiecesNear(0, 0, 12).ToList();
        var ds = Building.Snap(c.Pieces["log_door"], new V3(1.5, 6.3, .1), 0, pieces);
        T.Check(ds is { } dp && Math.Abs(dp.Pose.X - 1.5) < 1e-6 && Math.Abs(dp.Pose.Y - 5.3) < 1e-6 && dp.Joints == 2, $"the door snaps into the doorway: {ds}");
        inv.Add("plant_fibre", 4);
        rules.Place(me, "log_door", ds!.Value.Pose.X, ds.Value.Pose.Y, ds.Value.Pose.Z, ds.Value.Pose.Yaw, 0, 0, -4);
        var door = rules.Objects.All.Single(o => o.Kind == "log_door");
        rules.Supply(me, door.Id, 0, -4);
        T.Check(!door.Blueprint && rules.Use(me, door.Id, 1, 0).Ok && door.State == 1 && rules.Use(me, door.Id, 1, 0).Ok && door.State == 0, "it opens and shuts");
        // Turning a piece where it stands.
        T.Check(rules.Place(me, "log_door", 0, 0, 0, door.Yaw + Math.PI, door.Id, 0, -4).Message == "Turned.", "turned the other way round");
        // A campfire on the deck (Tom, build 64): it stands on a floor or foundation as on the ground; not in the air.
        inv.Add("branch", 8);
        var deckFire = rules.Place(me, "campfire", .5, 5.3, -.5, 0, 0, 0, -4);
        T.Check(deckFire.Ok && rules.Objects.All.Any(o => o.Kind == "campfire" && Math.Abs(o.Y - 5.3) < 1e-9), "a campfire on the foundation's deck: " + deckFire.Message);
        T.Check(!rules.Place(me, "campfire", .5, 7.5, .5, 0, 0, 0, -4).Ok, "not floating above it");
        var fireObj = rules.Objects.All.First(o => o.Kind == "campfire");
        rules.Supply(me, fireObj.Id, 0, -4);
        int logsBefore = inv.Count("log");
        T.Check(rules.Dismantle(me, fireObj.Id, 0, -4).Ok && inv.Count("log") == logsBefore + 2, "taken down again: its logs back (the kindling burnt)");
        // Taking it down gives everything back.
        int logs = inv.Count("log");
        var down = rules.Dismantle(me, f.Id, 0, -4);
        T.Check(down.Ok && rules.Objects.Get(f.Id) == null && inv.Count("log") == logs + 4, "the foundation taken down: " + down.Message);
        // A 6 × 6 m house with a hip roof: four outer corners, each aimed at near its house corner, meet at the top.
        var w2 = new World();
        var r2 = new GameRules(c, w2);
        r2.Inventory(me).Add("log", 200);
        void Put(string piece, double ax, double ay, double az, double facing)
        {
            var near2 = r2.PiecesNear(ax, az, 12).ToList();
            var s = Building.Snap(c.Pieces[piece], new V3(ax, ay, az), facing, near2);
            T.Check(s != null, $"{piece} at {ax}, {az} snaps");
            if (s is { } sp) T.Check(r2.Place(me, piece, sp.Pose.X, sp.Pose.Y, sp.Pose.Z, sp.Pose.Yaw, 0, 1.5, 1.5).Ok, $"{piece} placed at {sp.Pose}");
        }
        r2.Place(me, "log_foundation", 0, 5.3, 0, 0, 0, 0, 0);
        Put("log_foundation", 3.2, 5.3, .2, 0); Put("log_foundation", .2, 5.3, 3.2, 0); Put("log_foundation", 3.2, 5.3, 3.2, 0);
        // (each turned as a player facing that side would have it: along the side)
        foreach (var (lx, lz) in new[] { (-1.4, 0.0), (-1.4, 3.0), (4.4, 0.0), (4.4, 3.0), (0.0, -1.4), (3.0, -1.4), (0.0, 4.4), (3.0, 4.4) })
            Put("log_wall", lx, 5.3, lz, Math.Abs(lz % 3) < .01 && Math.Abs(lx) > 1 ? Math.PI / 2 : 0);
        foreach (var o in r2.Objects.All.ToList()) r2.Supply(me, o.Id, 1.5, 1.5);
        Put("log_roof_outer_steep", -1.2, 8.3, -1.2, Math.PI / 4);
        Put("log_roof_outer_steep", 4.2, 8.3, -1.2, -Math.PI / 4);
        Put("log_roof_outer_steep", -1.2, 8.3, 4.2, Math.PI * 3 / 4);
        Put("log_roof_outer_steep", 4.2, 8.3, 4.2, -Math.PI * 3 / 4);
        var tops = r2.Objects.All.Where(o => o.Kind == "log_roof_outer_steep").Select(o => Building.ToWorld(new Pose(o.X, o.Y, o.Z, o.Yaw), new V3(1.5, 3, 1.5))).ToList();
        T.Check(tops.Count == 4 && tops.All(p => p.DistanceTo(new V3(1.5, 11.3, 1.5)) < .01), $"a hip roof: the corners meet at the top ({string.Join("; ", tops)})");
        // Tom (build 67, dump 154613): a second storey — a wall aimed at the upper floor went under it, between the
        // storeys (its top on the floor's grid, its bottom on the foundation's: twice the joints).
        var s2 = new GameRules(c, new World()) { Testing = true };
        s2.Place(me, "log_foundation", 0, 5.3, 0, 0, 0, 0, -4);
        s2.Place(me, "log_wall", 1.5, 5.3, 0, Math.PI / 2, 0, 0, -4);
        s2.Place(me, "log_floor", 0, 8.3, 0, 0, 0, 0, -4);
        var near2s = s2.PiecesNear(0, 0, 12).ToList();
        foreach (var (ax, az) in new[] { (.2, .3), (1.3, .2), (-1.2, 1.1), (.5, -1.3) })
            foreach (double facing in new[] { 0, Math.PI / 2 })
            {
                var up2 = Building.Snap(wall, new V3(ax, 8.3, az), facing, near2s, new V3(0, 1, 0));
                T.Check(up2 is { } u && Math.Abs(u.Pose.Y - 8.3) < 1e-6, $"a wall aimed at the upper floor ({ax}, {az}) stands on it ({up2})");
            }
        // Tom (build 68, dump 164356): "something is in the way" at a corner with nothing there — two walls meeting in an L
        // touched exactly (1.42 + 0.08 = 1.5 m apart), so rounding decided. His corner, far out and turned 208°.
        var cw = c.Pieces["log_wall"];
        double yw = 208.362 * Math.PI / 180;
        var west = new Pose(-209832.973, 17.696, 1798.103, yw + Math.PI / 2);
        var southWall = new Pose(-209829.726, 17.696, 1794.645, yw);
        var corner = new Pose(-209832.366, 17.696, 1796.07, yw); // (the ghost of the dump: the next one along, at the west wall)
        int touching = 0;
        for (int k = 0; k < 200; k++)
        {
            double j = (k - 100) * 1e-7; // (nudged both ways: the tie fell either side of 1.5 m)
            var placedL = new List<Building.Placed> { new(1, cw, west with { X = west.X + j }), new(2, cw, southWall) };
            if (Building.Blocked(cw, corner with { Z = corner.Z + j }, placedL)) touching++;
        }
        T.Check(touching == 0, $"an L corner of walls is never in the way ({touching} of 200 nudged tries were)");
        T.Check(Building.Blocked(cw, southWall, new List<Building.Placed> { new(2, cw, southWall) }) && Building.Blocked(c.Pieces["log_door"], new Pose(0, 0, 0, 0),
            new List<Building.Placed> { new(3, c.Pieces["log_door"], new Pose(0, 0, 0, 0)) }), "but the same place twice still is (walls, doors)");
        // Diagonal walls and triangles (Tom, build 69; D135): a diagonal wall across a foundation, corner to corner, either
        // diagonal as you turn it; triangle foundations beside a square one, and two completing one cell.
        var dg = new GameRules(c, new World()) { Testing = true };
        dg.Place(me, "log_foundation", 0, 5.3, 0, 0, 0, 0, -4);
        var dgNear = dg.PiecesNear(0, 0, 12).ToList();
        var diag = c.Pieces["log_wall_diagonal"];
        foreach (var (want, label) in new[] { (Math.PI / 4, "one diagonal"), (3 * Math.PI / 4, "the other") })
        {
            var ds2 = Building.Snap(diag, new V3(.3, 5.3, .2), want + .1, dgNear, new V3(0, 1, 0));
            T.Check(ds2 is { } dwp && Math.Abs(dwp.Pose.X) < 1e-6 && Math.Abs(dwp.Pose.Z) < 1e-6 && Math.Abs(Math.IEEERemainder(dwp.Pose.Yaw - want, Math.PI)) < 1e-6 && dwp.Joints == 5,
                $"a diagonal wall corner to corner, {label}: {ds2}");
        }
        var tri = c.Pieces["log_foundation_triangle"];
        var ts = Building.Snap(tri, new V3(3.4, 5.3, -.6), 0, dgNear, new V3(0, 1, 0));
        T.Check(ts is { } tp && Math.Abs(tp.Pose.X - 3) < 1e-6 && Math.Abs(tp.Pose.Z) < 1e-6 && Math.Abs(tp.Pose.Y - 5.3) < 1e-6, $"a triangle foundation beside the square one: {ts}");
        T.Check(dg.Place(me, tri.Id, 3, 5.3, 0, 0, 0, 0, -4).Ok, "placed");
        T.Check(dg.Place(me, tri.Id, 3, 5.3, 0, Math.PI, 0, 0, -4).Ok, "its other half: the cell completed");
        T.Check(!dg.Place(me, tri.Id, 3, 5.3, 0, 0, 0, 0, -4).Ok && !dg.Place(me, "log_foundation", 3, 5.3, 0, 0, 0, 0, -4).Ok, "but not the same triangle twice, nor a square over them");
        // Edges (Tom: diagonal walls, doorways, doors and steps should snap to a diagonal line by themselves; D136): the
        // chosen wall shape in the size the nearest edge needs, and stairs with their top on an edge, running up onto it.
        var ed = new GameRules(c, new World()) { Testing = true };
        ed.Place(me, "log_foundation", 0, 5.3, 0, 0, 0, 0, -4);
        ed.Place(me, "log_foundation_half", 0, 5.3, -2.25, 0, 0, 0, -4);
        ed.Place(me, "log_foundation_triangle", 3, 5.3, 0, 0, 0, 0, -4);
        var en = ed.PiecesNear(0, 0, 12).ToList();
        var stdWall = c.Pieces["log_wall"];
        (PieceDef Piece, Pose Pose)? E(string piece, double x, double z, double facing = 0) =>
            Building.SnapPiece(c.Pieces[piece], c.Family(c.Pieces[piece]), new V3(x, 5.3, z), facing, en, new V3(0, 1, 0));
        T.Check(E("log_wall", .6, .55) is { } d1 && d1.Piece.Id == "log_wall_diagonal" && Math.Abs(d1.Pose.X) < 1e-6 && Math.Abs(d1.Pose.Z) < 1e-6,
            $"a wall aimed near the diagonal is the diagonal wall, corner to corner: {E("log_wall", .6, .55)}");
        T.Check(E("log_wall", 1.4, .3) is { } s1 && s1.Piece.Id == "log_wall" && Math.Abs(s1.Pose.X - 1.5) < 1e-6 && Math.Abs(s1.Pose.Z) < 1e-6,
            $"near a side: the straight wall along it: {E("log_wall", 1.4, .3)}");
        T.Check(E("log_wall", -1.45, -2.65, Math.PI / 2) is { } n1 && n1.Piece.Id == "log_wall_narrow", $"on the half foundation's short side: the narrow wall ({E("log_wall", -1.45, -2.65, Math.PI / 2)?.Piece.Id})");
        T.Check(E("log_wall_doorway", -.55, -.6) is { } dd && dd.Piece.Id == "log_wall_doorway_diagonal", $"a doorway near the diagonal: the diagonal doorway ({E("log_wall_doorway", -.55, -.6)?.Piece.Id})");
        T.Check(E("log_wall_window", 3.1, -.05) is { } dw2 && dw2.Piece.Id == "log_wall_window_diagonal", $"a window on the triangle's long side: diagonal ({E("log_wall_window", 3.1, -.05)?.Piece.Id})");
        // Stairs on the triangle's long side (outside it): running up onto it, diagonal, their top on that side.
        var sd = E("log_stairs_steep", 3.4, .3, -3 * Math.PI / 4 + .2); // (a player north-east of it, facing it)
        bool onDiag = sd is { } sp2 && Math.Abs(Math.IEEERemainder(sp2.Pose.Yaw + 3 * Math.PI / 4, Math.Tau)) < 1e-6
            && Building.ToWorld(sp2.Pose, new V3(0, 3, 1.5)) is var top2 && Math.Abs(top2.X - 3 + top2.Z) < 1e-6 && Math.Abs(top2.Y - 5.3) < 1e-6;
        T.Check(onDiag, $"steep stairs onto the triangle's long side, diagonally: {sd}");
        var ss = E("log_stairs", 1.8, .2, -Math.PI / 2); // (a player east of it, facing it)
        T.Check(ss is { } sp3 && Math.Abs(Math.IEEERemainder(sp3.Pose.Yaw + Math.PI / 2, Math.Tau)) < 1e-6 && Math.Abs(sp3.Pose.X - 3.5) < 1e-6 && Math.Abs(sp3.Pose.Y - 2.3) < 1e-6,
            $"stairs up onto the foundation's east side: {ss}");
        // Testing tools (D128): off, no kits; on, kits and pieces built at once without materials.
        var t3 = new GameRules(c, new World());
        T.Check(!t3.Give(me, "materials").Ok, "no free kits unless the testing tools are on");
        t3.Testing = true;
        T.Check(t3.Give(me, "materials").Ok && t3.Inventory(me).Count("log") >= 50 && t3.Give(me, "tools").Ok && t3.Inventory(me).HasTag("bow"), "kits given");
        int logs3 = t3.Inventory(me).Count("log");
        T.Check(t3.Place(me, "log_foundation", 0, 5.3, 0, 0, 0, 0, -4).Ok && t3.Objects.All.Single() is { Blueprint: false } && t3.Inventory(me).Count("log") == logs3,
            "free building: the foundation stands at once, nothing taken from the pack");
        // Overlap geometry: turned boxes.
        var pa = new Pose(0, 0, 0, 0); var pb = new Pose(2.9, 0, 0, Math.PI / 4);
        T.Check(Building.Overlaps(foundation, pa, foundation, pb) && !Building.Overlaps(foundation, pa, foundation, new Pose(3, 0, 0, 0)) && !Building.Overlaps(foundation, pa, foundation, new Pose(4.6, 0, 0, Math.PI / 4)),
            "boxes: a turned one overlapping, edge to edge not, far enough not");
    }

    private static void NeedsWork()
    {
        var mild = new BodyInput(Air: 18, Wind: 1, Rain: 0, Swimming: false, Fire: 0, Effort: 0);
        var n = new Needs();
        for (int i = 0; i < 24; i++) n.Step(1, mild);
        T.Check(n.Food < 80 - 40 && n.Food > 80 - 60, $"a day at rest: food {n.Food:0}");
        T.Check(n.Warmth > 70 && n.Health >= 99, $"mild air: warm ({n.Warmth:0}), healthy");

        var night = new BodyInput(Air: 1, Wind: 6, Rain: .8, Swimming: false, Fire: 0, Effort: 0);
        var cold = new Needs();
        for (int i = 0; i < 8; i++) cold.Step(.5, night);
        T.Check(cold.Warmth < 25 && cold.Wet > 80, $"four hours of cold rain and wind: warmth {cold.Warmth:0}, wet {cold.Wet:0}");
        T.Check(cold.Health < 100, $"and it hurts (health {cold.Health:0})");

        var fire = night with { Rain = 0, Fire = 14 };
        double before = cold.Warmth;
        for (int i = 0; i < 4; i++) cold.Step(.5, fire);
        T.Check(cold.Warmth > before + 20 && cold.Wet < 80, $"two hours by a fire: warmth {before:0} → {cold.Warmth:0}, drying ({cold.Wet:0})");

        var hungry = new Needs { Food = 0 };
        hungry.Step(1, mild);
        T.Check(hungry.Health < 100, "starving hurts");
        hungry.Eat(35);
        T.Check(hungry.Food == 35, "eating fills");

        double winterNight = Needs.AirTemperature(9, 50, 3, 4, 0), summerDay = Needs.AirTemperature(9, 50, 1, 15, 0);
        T.Check(winterNight < 0 && summerDay > 20, $"air: winter night {winterNight:0.0} °C, summer afternoon {summerDay:0.0} °C (yearly mean 9 °C, 50°N)");
        T.Check(Needs.AirTemperature(26, 5, 3, 15, 0) > 25, "the tropics stay warm in 'winter'");
    }
}
