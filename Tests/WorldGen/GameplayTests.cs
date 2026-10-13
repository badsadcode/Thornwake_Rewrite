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
        public (double X, double Z, double Scale)? Tree(long cx, long cz) => cx == 3 && cz == 4 ? (21.5, 29.0, 1.1) : null;
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
        var chop = rules.Chop(me, 3, 4, 21, 28);
        T.Check(chop.Ok && world.Removals.Contains(ObjectRemovals.Trees, 3, 4), "felled: " + chop.Message);
        T.Check(world.Removals.Get(ObjectRemovals.Trees, 3, 4) == 10, "with the time it fell");
        T.Check(!rules.Chop(me, 3, 4, 21, 28).Ok, "only once");
        T.Check(inv.Count("log") >= 2 && inv.Count("branch") >= 4, $"logs {inv.Count("log")}, branches {inv.Count("branch")}");
        T.Check(rules.TakeInventoryChanges().Contains(me), "my inventory is sent to me");

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
