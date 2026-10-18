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
    }

    private static Catalog Load() => new(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content/gameplay_items.json")),
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Content/gameplay_recipes.json")));

    private static void ContentLoads()
    {
        var c = Load();
        T.Check(c.Items.ContainsKey("stone_axe") && c.Item("stone_axe").Has("chopping"), "the axe chops");
        T.Check(c.Recipes["campfire"].Places == "campfire" && c.Objects["campfire"].Tags.Contains("fire"), "a campfire is a fire");
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
        T.Check(!rules.Make(me, "campfire", 21, 28, 40, 0, 40, 0).Ok, "cannot build it out of reach");
        var fire = rules.Make(me, "campfire", 21, 28, 22, 5, 29.5, 0);
        T.Check(fire.Ok && rules.Objects.All.Count == 1, "campfire built: " + fire.Message);
        var o = rules.Objects.All.First();
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
        var built = rules.Make(7, "lean_to", 0, 0, 2, 5, 0, 0);
        T.Check(built.Ok && GameRules.Sheltered(c, rules.Objects, 2.5, .5) && !GameRules.Sheltered(c, rules.Objects, 8, 0), "a lean-to: sheltered under it, not beside it");
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
