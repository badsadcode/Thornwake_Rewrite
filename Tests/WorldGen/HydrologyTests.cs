using Thornwake.WorldGen;

/// <summary>M2 tests: erosion, lakes, river network and channel carving.</summary>
internal static class HydrologyTests
{
    public static void Register(string? filter)
    {
        T.Run("Rivers descend strictly and join without steps", RiversDescend, filter);
        T.Run("Every river reaches the sea or a lake", RiversTerminate, filter);
        T.Run("Lakes: level, storage and outlet", LakesValid, filter);
        T.Run("Sampler: channel below water, banks above", ChannelCarving, filter);
        T.Run("Sampler: lake surfaces and shores", LakeSampling, filter);
        T.Run("Erosion carves valleys but keeps ranges", ErosionShape, filter);
    }

    private static IEnumerable<TerrainSampler> Worlds() =>
        new[] { ("mountainous", 42), ("vast-world", 7319), ("archipelago", 42) }.Select(w => CoreTests.Sampler(w.Item1, w.Item2));

    private static void RiversDescend()
    {
        foreach (var s in Worlds())
        {
            var hy = s.Plan.Hydrology;
            T.Check(hy.Reaches.Count > 50, $"{s.Plan.Profile.Id}: has a river network ({hy.Reaches.Count} reaches)");
            foreach (var r in hy.Reaches)
            {
                for (int k = 1; k < r.Points.Length; k++)
                    T.Check(r.Surface[k] < r.Surface[k - 1] || r.Surface[k] == r.Surface[k - 1] && r.Surface[k] <= s.Plan.Profile.SeaLevel + 1e-9,
                        $"reach {r.Id} surface rises at point {k}: {r.Surface[k - 1]} -> {r.Surface[k]}");
                T.Check(r.Surface[^1] >= s.Plan.Profile.SeaLevel - 1e-9, "never below the sea");
                T.Check(r.Width.All(w => w >= s.Plan.Style.RiverMinWidth && w <= s.Plan.Style.RiverMaxWidth), "width within limits");
                if (r.DownstreamReach >= 0)
                {
                    var next = hy.Reaches[r.DownstreamReach];
                    T.Check(r.Points[^1] == next.Points[0], "junction shares its point");
                    T.Near(r.Surface[^1], next.Surface[0], .011, "junction water level continuous");
                }
            }
        }
    }

    private static void RiversTerminate()
    {
        foreach (var s in Worlds())
        {
            var hy = s.Plan.Hydrology;
            foreach (var r in hy.Reaches)
            {
                var cur = r; int guard = 0;
                while (cur.DownstreamReach >= 0 && guard++ < 100000) cur = hy.Reaches[cur.DownstreamReach];
                T.Check(guard < 100000, "no cycles");
                T.Check(cur.EndsInSea || cur.EndLake >= 0, $"reach {r.Id} ends in sea or lake");
            }
            int sea = hy.Reaches.Count(r => r.EndsInSea);
            T.Note($"{s.Plan.Profile.Id}: {hy.Reaches.Count} reaches, {sea} river mouths, {hy.Lakes.Count} lakes, " +
                $"largest catchment {hy.Reaches.Max(r => r.Catchment[^1]):0} km², widest {hy.Reaches.Max(r => r.Width.Max()):0} m");
        }
    }

    private static void LakesValid()
    {
        foreach (var s in Worlds())
        {
            var hy = s.Plan.Hydrology;
            foreach (var lake in hy.Lakes)
            {
                T.Check(lake.Level > s.Plan.Profile.SeaLevel && lake.Volume > 0 && lake.MaxDepth >= s.Plan.Style.LakeMinDepth, $"lake {lake.Id} level/volume/depth");
                T.Check(hy.LakeCell[lake.OutletCell] == lake.Id, "outlet is a lake cell");
                if (lake.OutletReach >= 0) T.Near(hy.Reaches[lake.OutletReach].Surface[0], lake.Level, .02, "outlet river starts at the lake level");
                foreach (int inflow in lake.InflowReaches) T.Check(hy.Reaches[inflow].Surface[^1] >= lake.Level - .02, "inflow arrives at or above the lake level");
            }
            if (hy.Lakes.Count > 0)
            {
                double land = 0; var macro = s.Plan.Macro.Channel(WorldPlan.HeightChannel);
                foreach (var h in macro) if (h > 0) land++;
                double share = hy.Lakes.Sum(l => l.Area) / (land * s.Plan.Macro.Cell * s.Plan.Macro.Cell);
                T.Check(share <= s.Plan.Style.LakeShare + 1e-9, $"lake share {share}");
            }
        }
    }

    private static void ChannelCarving()
    {
        var s = CoreTests.Sampler("mountainous", 42);
        var hy = s.Plan.Hydrology;
        var rng = new Random(4);
        int tested = 0, banks = 0, low = 0;
        foreach (var r in hy.Reaches.OrderBy(_ => rng.Next()).Take(150))
            for (int k = 1; k < r.Points.Length - 1; k += 5)
            {
                var p = r.Points[k];
                var centre = s.Sample(p.X, p.Z);
                if (centre.Water != WaterKind.River) continue; // a larger river's channel may own this spot
                tested++;
                T.Check(centre.Height < centre.WaterLevel, "river bed below the water surface");
                // Banks: just outside the channel the ground is dry and above the water.
                double tx = r.Points[k + 1].X - r.Points[k - 1].X, tz = r.Points[k + 1].Z - r.Points[k - 1].Z, len = Math.Sqrt(tx * tx + tz * tz);
                double offset = r.Width[k] * .5 + 3;
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    var bank = s.Sample(p.X - tz / len * offset * side, p.Z + tx / len * offset * side);
                    if (bank.Water != WaterKind.None) continue;
                    banks++;
                    if (bank.Height < centre.WaterLevel - .05) low++; // usually another reach's floodplain winning nearby
                }
            }
        T.Note($"{tested} channel cross-sections checked; {low} of {banks} dry bank samples below the water");
        T.Check(low <= banks * .03, "banks above the water");
        T.Check(tested > 100, "enough channel samples");
    }

    private static void LakeSampling()
    {
        foreach (var s in Worlds())
            foreach (var lake in s.Plan.Hydrology.Lakes.Take(20))
            {
                // The deepest lake cell is under water.
                int n = s.Plan.Macro.Size, deepest = -1; double low = double.PositiveInfinity;
                var h = s.Plan.Macro.Channel(WorldPlan.HeightChannel);
                for (int c = 0; c < s.Plan.Hydrology.LakeCell.Length; c++)
                    if (s.Plan.Hydrology.LakeCell[c] == lake.Id && h[c] < low) { low = h[c]; deepest = c; }
                var t = s.Sample(s.Plan.Macro.CellX(deepest % n), s.Plan.Macro.CellZ(deepest / n));
                T.Check(t.Water == WaterKind.Lake || t.Water == WaterKind.River, $"{s.Plan.Profile.Id} lake {lake.Id} deepest point is wet ({t.Water}, h {t.Height}, level {lake.Level})");
                if (t.Water == WaterKind.Lake) T.Check(t.WaterLevel == lake.Level && t.Height < lake.Level, "lake level and bed");
            }
    }

    private static void ErosionShape()
    {
        foreach (var s in Worlds())
        {
            var hy = s.Plan.Hydrology;
            T.Note($"{s.Plan.Profile.Id}: max incision {hy.MaxIncision:0} m, plan {s.Plan.BuildMilliseconds:0} ms (hydrology {hy.BuildMilliseconds:0} ms)");
            T.Check(hy.MaxIncision > 50, "rivers incised valleys");
            T.Check(hy.MaxIncision < s.Plan.Profile.MountainHeight, "incision bounded");
        }
    }
}
