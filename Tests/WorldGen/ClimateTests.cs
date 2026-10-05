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

    private static Dictionary<string, double> Spread(WorldPlan plan, Climate c)
    {
        var counts = new Dictionary<string, double>(); int land = 0;
        double world = plan.Profile.WorldSize, step = world / 160;
        for (int j = 0; j < 160; j++)
            for (int i = 0; i < 160; i++)
            {
                double x = plan.Manifest.OriginX - world / 2 + (i + .5) * step, z = plan.Manifest.OriginZ - world / 2 + (j + .5) * step;
                plan.Macro.Sample(x, z, WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out double l, out double h, out _);
                if (l <= 0) continue;
                var id = c.Biomes[c.BiomeAt(x, z, h).A].Id;
                counts[id] = counts.GetValueOrDefault(id) + 1; land++;
            }
        foreach (var k in counts.Keys.ToList()) counts[k] /= land;
        return counts;
    }

    private static void Biomes()
    {
        foreach (var b in Thornwake.WorldGen.Biomes.Defaults) Thornwake.WorldGen.Biomes.Validate(b);
        T.Check(Thornwake.WorldGen.Biomes.Defaults.Select(b => b.Id).Distinct().Count() == Thornwake.WorldGen.Biomes.Defaults.Length, "unique biome ids");
        var json = System.Text.Json.JsonSerializer.Serialize(Thornwake.WorldGen.Biomes.Defaults);
        T.Check(Thornwake.WorldGen.Biomes.Parse(json).Count == Thornwake.WorldGen.Biomes.Defaults.Length, "biomes round-trip through JSON (custom biome format)");
        var plan = CoreTests.Sampler("vast-world", 7319).Plan;
        var spread = Spread(plan, plan.Climate);
        T.Note("vast world: " + string.Join(", ", spread.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} {kv.Value * 100:0} %")));
        T.Check(spread.Count >= 8 && spread.Values.Max() < .5, "a big world has many biomes, none dominating");
        var warm = new Climate(plan, plan.Style.Climate with { TemperatureOffset = 10 });
        var dry = new Climate(plan, plan.Style.Climate with { Moisture = .3 });
        var sw = Spread(plan, warm); var sd = Spread(plan, dry);
        T.Check(sw.GetValueOrDefault("tundra") + sw.GetValueOrDefault("ice") < spread.GetValueOrDefault("tundra") + spread.GetValueOrDefault("ice"), "warmer: less tundra and ice");
        T.Check(sd.GetValueOrDefault("desert") + sd.GetValueOrDefault("cold-desert") + sd.GetValueOrDefault("steppe") > spread.GetValueOrDefault("desert") + spread.GetValueOrDefault("cold-desert") + spread.GetValueOrDefault("steppe"), "drier: more desert and steppe");
    }
}
