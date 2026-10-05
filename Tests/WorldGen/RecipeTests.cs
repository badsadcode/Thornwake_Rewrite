using Thornwake.WorldGen;

/// <summary>World types as size-independent recipes (D35).</summary>
internal static class RecipeTests
{
    public static void Register(string? filter)
    {
        T.Run("Recipes: native size on auto is exactly the preset", Identity, filter);
        T.Run("Recipes: every type gives a valid world at every size", AllSizes, filter);
        T.Run("Recipes: land share survives 128 km and 4096 km", Coverage, filter);
        T.Run("Recipes: a saved world type round-trips through JSON", CustomType, filter);
        T.Run("Landforms: templates shape the world and stay valid", Templates, filter);
    }

    private static IReadOnlyList<WorldRecipe> Recipes => WorldRecipe.Catalog(CoreTests.Profiles);

    private static void Identity()
    {
        foreach (var r in Recipes)
        {
            var p = r.Resolve(new WorldChoices { SizeKm = r.NativeKm });
            T.Check(p.Digest() == r.Base.Digest(), $"{r.Id}: auto at {r.NativeKm} km keeps the preset digest");
        }
        foreach (var preset in CoreTests.Profiles)
            T.Check(Recipes.Any(r => r.Base.Digest() == preset.Digest()), $"preset {preset.Id} is a world type");
    }

    private static void AllSizes()
    {
        foreach (var r in Recipes)
            foreach (double km in WorldRecipe.Sizes)
            {
                var p = r.Resolve(new WorldChoices { SizeKm = km });
                p.Validate();
                T.Check(p.WorldSize == km * 1000, "size applied");
                if (r.Mainland) T.Check(p.Continents == r.Base.Continents, $"{r.Id}: one dominant landmass at {km} km");
            }
        var big = Recipes.First(r => r.Id == "balanced").Resolve(new WorldChoices { SizeKm = 4096 });
        var small = Recipes.First(r => r.Id == "balanced").Resolve(new WorldChoices { SizeKm = 128 });
        T.Note($"balanced: 128 km → {small.Continents} continent(s), {small.Islands} islands; 4096 km → {big.Continents} continents, {big.Islands} islands, continent radius {big.ContinentRadius / 1000:0} km");
        T.Check(big.Continents > small.Continents && big.Islands > small.Islands, "bigger worlds get more landmasses rather than giant ones");
        var custom = Recipes.First(r => r.Id == "archipelago").Resolve(new WorldChoices { SizeKm = 4096, Islands = 200, Continents = 7, Ranges = 5 });
        T.Check(custom.Islands == 200 && custom.Continents == 7 && custom.RangesPerContinent == 5, "overrides win over auto");
    }

    private static void Coverage()
    {
        foreach (var id in new[] { "balanced", "archipelago" })
        {
            var r = Recipes.First(x => x.Id == id);
            var shares = new List<double>();
            foreach (double km in new[] { 128.0, r.NativeKm, 4096 })
            {
                var plan = new WorldPlan(WorldManifest.Create(42, r.Resolve(new WorldChoices { SizeKm = km }), worldId: CoreTests.FixedWorldId));
                var land = plan.Macro.Channel(WorldPlan.LandChannel);
                shares.Add(land.Count(v => v > 0) / (double)land.Length);
            }
            T.Note($"{id}: land share at 128 km / native / 4096 km = {string.Join(" / ", shares.Select(v => (v * 100).ToString("0") + " %"))}");
            T.Check(shares.Max() / Math.Max(1e-6, shares.Min()) < 2.2, $"{id}: land share stays in the same range across sizes");
        }
    }

    private static void CustomType()
    {
        var source = Recipes.First(r => r.Id == "archipelago");
        var profile = source.Resolve(new WorldChoices { SizeKm = 512, Islands = 150, CoastRoughness = 1.2 });
        var saved = WorldRecipe.Create("My Shattered Isles!", "test", profile, false, new Dictionary<string, double> { ["gen.rivers"] = 1.4 });
        T.Check(saved.Id == "my-shattered-isles" && saved.Custom, "id slug from the name");
        var loaded = WorldRecipe.FromJson(saved.ToJson());
        T.Check(loaded.Id == saved.Id && loaded.Name == saved.Name && loaded.Settings["gen.rivers"] == 1.4, "name, id and parameters survive");
        T.Check(loaded.Resolve(new WorldChoices()).Digest() == saved.Base.Digest(), "native size on auto gives the saved world");
        var big = loaded.Resolve(new WorldChoices { SizeKm = 2048 });
        T.Check(big.WorldSize == 2048000 && big.Islands > profile.Islands, "a saved type adapts to other sizes");
        var catalog = WorldRecipe.Catalog(CoreTests.Profiles, new[] { loaded });
        T.Check(catalog.Count == Recipes.Count + 1 && catalog[^1].Id == loaded.Id, "custom types follow the built-in ones");
        bool rejected = false;
        try { WorldRecipe.FromJson("{\"Id\":\"\",\"Name\":\"x\"}"); } catch (Exception) { rejected = true; }
        T.Check(rejected, "a broken file is rejected, not loaded");
    }

    private static double LandAt(WorldPlan plan, double u, double v)
    {
        var p = plan.Profile;
        plan.Macro.Sample(plan.Manifest.OriginX + (u - .5) * p.WorldSize, plan.Manifest.OriginZ + (v - .5) * p.WorldSize,
            WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out double land, out _, out _);
        return land;
    }

    private static void Templates()
    {
        foreach (double km in new[] { 64.0, 512 })
        {
            WorldPlan Plan(string id, int seed = 42) => new(WorldManifest.Create(seed, Recipes.First(r => r.Id == id).Resolve(new WorldChoices { SizeKm = km }), worldId: CoreTests.FixedWorldId));
            var inland = Plan("inland-sea");
            T.Note($"centre {LandAt(inland, .5, .5):0.00} ring {string.Join(" ", new[] { (.5, .2), (.5, .8), (.2, .5), (.8, .5) }.Select(q => LandAt(inland, q.Item1, q.Item2).ToString("0.00")))}");
            T.Check(LandAt(inland, .5, .5) < 0 && new[] { (.5, .22), (.5, .78), (.22, .5), (.78, .5) }.Count(q => LandAt(inland, q.Item1, q.Item2) > 0) >= 2, $"inland sea at {km} km: sea in the middle, land around it");
            var twin = Plan("twin-continents");
            T.Check(LandAt(twin, .28, .5) > 0 && LandAt(twin, .72, .5) > 0 && LandAt(twin, .5, .5) < 0, $"twin continents at {km} km: two lands, ocean between");
            var a = Plan("shattered"); var b = Plan("shattered");
            T.Check(a.Macro.Channel(WorldPlan.HeightChannel).AsSpan().SequenceEqual(b.Macro.Channel(WorldPlan.HeightChannel)), "same seed, same template world");
            var other = Plan("shattered", 7);
            T.Check(!a.Macro.Channel(WorldPlan.LandChannel).AsSpan().SequenceEqual(other.Macro.Channel(WorldPlan.LandChannel)), "another seed varies the template");
        }
        // A template survives saving as a custom type.
        var rift = Recipes.First(r => r.Id == "rift");
        var copy = WorldRecipe.FromJson(WorldRecipe.Create("My rift", "", rift.Base, true, new Dictionary<string, double>()).ToJson());
        T.Check(copy.Base.Landforms!.Length == rift.Base.Landforms!.Length && copy.Base.Landforms[1].Step == "Trough", "landforms survive JSON");
        // The example in docs/WORLD_CREATION.md loads and generates.
        var doc = File.ReadAllText(Path.Combine(CoreTests.FindRepoRoot(), "docs/WORLD_CREATION.md"));
        int start = doc.IndexOf("{\n  \"Id\": \"two-seas\""), end = doc.IndexOf("```", start);
        var example = WorldRecipe.FromJson(doc[start..end]);
        var examplePlan = new WorldPlan(WorldManifest.Create(42, example.Resolve(new WorldChoices { SizeKm = 128 }), worldId: CoreTests.FixedWorldId));
        T.Check(examplePlan.LandformShapes > 0 && WorldShare(examplePlan) > .05, "the documented example world type loads and makes land");
        foreach (var bad in new[] { """{"Step":"Volcano"}""", """{"Step":"Hills","Count":[1000]}""", """{"Step":"Mask","Shape":"Spiral"}""" })
        {
            bool rejected = false;
            try { Landforms.Validate(new[] { System.Text.Json.JsonSerializer.Deserialize<Landform>(bad)! }); } catch (System.IO.InvalidDataException) { rejected = true; }
            T.Check(rejected, $"rejects {bad}");
        }
    }

    private static double WorldShare(WorldPlan plan) => plan.Macro.Channel(WorldPlan.LandChannel).Count(v => v > 0) / (double)plan.Macro.Channel(WorldPlan.LandChannel).Length;
}
