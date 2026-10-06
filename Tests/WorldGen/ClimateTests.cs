using Thornwake.WorldGen;

/// <summary>Climate and biomes (D42).</summary>
internal static class ClimateTests
{
    public static void Register(string? filter)
    {
        T.Run("Climate: colder north and up high, wetter toward the wind, deterministic", Climate, filter);
        T.Run("Biomes: catalogue valid, sensible spread, settings shift it", Biomes, filter);
    }

    private static void Climate()
    {
        var plan = CoreTests.Sampler("balanced", 42).Plan;
        var c = plan.Climate;
        double ox = plan.Manifest.OriginX, oz = plan.Manifest.OriginZ, half = plan.Profile.WorldSize / 2;
        T.Check(c.At(ox, oz - half * .8, 0).Temperature < c.At(ox, oz + half * .8, 0).Temperature, "the north is colder than the south");
        T.Check(c.At(ox, oz, 2000).Temperature < c.At(ox, oz, 0).Temperature - 10, "2 km up is >10 °C colder");
        // Westerlies (temperate world): west coasts wetter than the land far inland behind them.
        var f = plan.Layout.Land[0];
        double wet = 0, dry = 0; int n = 0;
        for (double u = -.6; u <= .6; u += .1)
        {
            double z = f.Center.Z + u * f.RadiusZ, x = f.Center.X - f.RadiusX;
            while (plan.Macro.Sample(WorldPlan.LandChannel, x, z) <= 0 && x < f.Center.X) x += plan.Macro.Cell;
            if (x >= f.Center.X) continue;
            wet += c.At(x + 5000, z, 0).Precipitation; dry += c.At(x + f.RadiusX * 1.2, z, 0).Precipitation; n++;
        }
        T.Note($"west coast {wet / n:0} mm, {f.RadiusX * 1.2 / 1000:0} km inland {dry / n:0} mm");
        T.Check(n > 3 && wet > dry, "the windward coast is wetter than the interior");
        var again = new Climate(plan, plan.Style.Climate);
        T.Check(again.Raster.Channel(Thornwake.WorldGen.Climate.PrecipitationChannel).AsSpan().SequenceEqual(c.Raster.Channel(Thornwake.WorldGen.Climate.PrecipitationChannel)), "deterministic");
    }

    /// <summary>Share of the world's land where each biome is strongest.</summary>
    private static Dictionary<string, double> Spread(BiomeMap map)
    {
        var c = map.Coverage();
        return map.Pool.Where(b => c[b.Index] > 0).ToDictionary(b => b.Def.Id, b => c[b.Index]);
    }

    private static void Biomes()
    {
        var reg = BiomeRegistry.Current!;
        T.Check(reg.Pool.Count == 18 && reg.All.Select(b => b.Id).Distinct().Count() == reg.All.Count, $"{reg.Pool.Count} built-in biomes in the pool, unique ids");
        // A custom biome (World Site Editor format) joins the pool without code changes; one out of the pool does not.
        string root = CoreTests.FindRepoRoot();
        string biomes = File.ReadAllText(Path.Combine(root, "Content/biomes.json")), sets = File.ReadAllText(Path.Combine(root, "Content/asset_sets.json"));
        var custom = """{ "Id": "crystal-fields", "Name": "Crystal Fields", "Placement": { "Temperature": [0, 20], "Weight": 3 }, "Appearance": { "Ground": "MeadowGround", "MapColor": "a0e0ff" } }""";
        var hidden = """{ "Id": "hidden", "Name": "Hidden", "InPool": false }""";
        var withCustom = BiomeRegistry.Parse(biomes, sets, new[] { custom, hidden });
        T.Check(withCustom.Pool.Any(b => b.Def.Id == "crystal-fields") && withCustom.Pool.All(b => b.Def.Id != "hidden") && withCustom.All.Any(b => b.Id == "hidden"), "custom biomes join the pool (or stay out of it)");
        bool rejected = false;
        try { BiomeRegistry.Parse(biomes, sets, new[] { """{ "Id": "bad", "Name": "Bad", "Appearance": { "Ground": "NoSuchSet" } }""" }); } catch (InvalidDataException) { rejected = true; }
        T.Check(rejected, "a biome naming an unknown asset set is rejected");
        // Every tree named by a tree set is a model the game has (Vegetation.Species); otherwise it would silently fall
        // back to the first species.
        string vegetation = File.ReadAllText(Path.Combine(root, "Game/Rewrite/Vegetation.cs"));
        int open = vegetation.IndexOf("Species =", StringComparison.Ordinal), close = vegetation.IndexOf("};", open, StringComparison.Ordinal);
        var species = System.Text.RegularExpressions.Regex.Matches(vegetation[open..close], "\"([A-Za-z_]+)\"").Select(m => m.Groups[1].Value).ToHashSet();
        var missing = reg.Sets.Values.Where(set => set.Kind == "trees").SelectMany(set => set.Items.Keys).Where(k => !species.Contains(k)).Distinct().ToArray();
        T.Check(missing.Length == 0 && species.Count >= 27, $"tree sets name only existing models ({species.Count} species; missing: {string.Join(", ", missing)})");
        var plan = CoreTests.Sampler("vast-world", 7319).Plan;
        var spread = Spread(plan.Biomes);
        T.Note("vast world: " + string.Join(", ", spread.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value * 100:0.0} %")));
        T.Check(spread.Count >= 10 && spread.Values.Max() < .45, "a big world has many biomes, none dominating");
        T.Check(spread.ContainsKey("ancient-forest") && spread.ContainsKey("volcanic"), "patch biomes appear");
        // Biomes stay near their ranges even where no biome fits (the fallback weighs every preference).
        int lowAlpine = 0, alpine = 0; double step = plan.Profile.WorldSize / 300;
        for (int j = 0; j < 300; j++)
            for (int i = 0; i < 300; i++)
            {
                double x = plan.Manifest.OriginX - plan.Profile.WorldSize / 2 + (i + .5) * step, z = plan.Manifest.OriginZ - plan.Profile.WorldSize / 2 + (j + .5) * step;
                double land = plan.Macro.Sample(WorldPlan.LandChannel, x, z), h = plan.Macro.Sample(WorldPlan.HeightChannel, x, z);
                if (land <= 0 || plan.Biomes.Pool[plan.Biomes.Sample(x, z).A].Def.Id != "alpine") continue;
                alpine++; if (h < 900) lowAlpine++;
            }
        T.Check(alpine > 0 && lowAlpine < alpine * .03, $"alpine stays in the mountains ({lowAlpine} of {alpine} samples below 900 m)");
        // Patchwork layout (round 7): many biomes everywhere, regardless of latitude; excluded biomes never appear;
        // the default layout leaves the profile digest (world identity) unchanged.
        var patchStyle = plan.Style with { Climate = plan.Style.Climate with { BiomePlacement = 1, PatchSize = 40, ExcludedBiomes = new[] { "desert" } } };
        string defaultJson = System.Text.Json.JsonSerializer.Serialize(new ClimateSettings());
        T.Check(!defaultJson.Contains("BiomePlacement") && !defaultJson.Contains("Excluded"), "default biome layout leaves saved worlds unchanged (no new JSON fields)");
        var patch = new WorldPlan(plan.Manifest, style: patchStyle);
        var patchSpread = Spread(patch.Biomes);
        T.Note("patchwork: " + string.Join(", ", patchSpread.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value * 100:0.0} %")));
        T.Check(!patchSpread.ContainsKey("desert") && patchSpread.Count >= 10 && patchSpread.Values.Max() < .3, "patchwork: many biomes, none dominating, excluded one absent");
        // Jungle (hot) appears in the cold north half too.
        int northJungle = 0; double north = patch.Manifest.OriginZ - patch.Profile.WorldSize * .35;
        for (int i = 0; i < 200; i++)
        {
            double x = patch.Manifest.OriginX + (i - 100) * patch.Profile.WorldSize / 220;
            for (double z = north - 80000; z <= north + 80000; z += 20000)
                if (patch.Macro.Sample(WorldPlan.LandChannel, x, z) > 0 && patch.Biomes.Pool[patch.Biomes.Sample(x, z).A].Def.Id == "jungle") northJungle++;
        }
        T.Check(northJungle > 0, $"patchwork ignores latitude (jungle samples in the far north: {northJungle})");
        var customPlan = new WorldPlan(plan.Manifest, style: plan.Style, biomes: withCustom);
        T.Check(Spread(customPlan.Biomes).ContainsKey("crystal-fields"), "a custom biome is generated");
        var warm = new BiomeMap(plan, new Climate(plan, plan.Style.Climate with { TemperatureOffset = 10 }), reg);
        var dry = new BiomeMap(plan, new Climate(plan, plan.Style.Climate with { Moisture = .3 }), reg);
        var sw = Spread(warm); var sd = Spread(dry);
        double Cold(Dictionary<string, double> s) => s.GetValueOrDefault("tundra") + s.GetValueOrDefault("arctic") + s.GetValueOrDefault("taiga");
        double Dry(Dictionary<string, double> s) => s.GetValueOrDefault("desert") + s.GetValueOrDefault("badlands") + s.GetValueOrDefault("savanna") + s.GetValueOrDefault("temperate-grassland");
        T.Check(Cold(sw) < Cold(spread), "warmer: less taiga, tundra and ice");
        T.Check(Dry(sd) > Dry(spread), "drier: more desert, badlands, savanna and grassland");
        var again = new BiomeMap(plan, plan.Climate, reg);
        bool same = true;
        for (int k = 0; k < 2000 && same; k++)
        {
            double x = plan.Manifest.OriginX + (k * 7919 % 1000 - 500) * 1500.0, z = plan.Manifest.OriginZ + (k * 104729 % 1000 - 500) * 1500.0;
            same = again.Sample(x, z) == plan.Biomes.Sample(x, z);
        }
        T.Check(same, "deterministic");
    }
}
