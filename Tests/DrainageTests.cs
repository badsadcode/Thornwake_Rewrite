using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Thornwake.Core;

internal static class DrainageTests
{
    public static int Run(string? reportPath = null)
    {
        int checks = 0;
        void Check(bool ok, string why) { if (!ok) throw new Exception("DRAINAGE: " + why); checks++; }
        void Reject(Action action, string why)
        {
            try { action(); }
            catch (Exception e) when (e is ArgumentException or InvalidDataException or IOException or OperationCanceledException)
            { checks++; return; }
            throw new Exception("DRAINAGE accepted " + why);
        }
        void Invariants(DrainageAnalysis a)
        {
            var rootSet = a.Outlets.Select(o => o.Cell).ToHashSet();
            double total = a.Outlets.Sum(o => a.CatchmentArea[o.Cell]);
            double expected = a.Grid.Count * a.Grid.Spacing * a.Grid.Spacing;
            Check(Math.Abs(total - expected) <= Math.Max(1e-8, expected * 1e-12), "all contributing area arrives at explicit outlets");
            for (int c = 0; c < a.Grid.Count; c++)
            {
                int next = a.Downstream[c];
                Check(a.SpillHeight[c] >= a.Grid.Bed[c], "routing never lowers original bed");
                Check(rootSet.Contains(a.OutletCell[c]), "known ultimate outlet");
                if (next >= 0)
                {
                    int dx = Math.Abs(c % a.Grid.Width - next % a.Grid.Width), dz = Math.Abs(c / a.Grid.Width - next / a.Grid.Width);
                    Check(dx + dz == 1, "face-connected routing only");
                    Check(a.SpillHeight[next] <= a.SpillHeight[c], "nonascending downstream routing surface");
                    Check(a.CatchmentArea[next] >= a.CatchmentArea[c], "downstream area accumulates");
                }
                else Check(rootSet.Contains(c), "only explicit outlets terminate");
                int visited = 0, end = c;
                while (a.Downstream[end] >= 0 && visited++ <= a.Grid.Count) end = a.Downstream[end];
                Check(visited <= a.Grid.Count && end == a.OutletCell[c], "acyclic including equal-height flats");
            }
            foreach (var basin in a.Basins)
            {
                Check(basin.CapacityEstimate > 0 && basin.Exits.Count > 0, "basin has storage and spill prerequisite");
                Check(basin.Cells.All(c => a.SpillHeight[c] == basin.SpillLevel), "single connected fill level");
                Check(basin.Exits.All(e => basin.Cells.Contains(e.From) && !basin.Cells.Contains(e.To)), "spill leaves basin");
            }
        }
        DrainageValidationTests.Run(Check, Invariants);
        var bed = Enumerable.Repeat(9d, 25).ToArray();
        for (int z = 1; z < 4; z++) for (int x = 1; x < 4; x++) bed[z * 5 + x] = 1;
        bed[13] = 4; bed[14] = 0;
        var bowlGrid = new DrainageGrid(-4, -4, 2, 5, 5, bed);
        var bowl = DrainageAnalysis.Build(bowlGrid, new[] { new DrainageOutlet(14, 0) }, "bowl-v1");
        Invariants(bowl);
        Check(bowl.Basins.Count == 1, "known bowl has one basin");
        Check(bowl.Basins[0].Cells.Count == 8 && bowl.Basins[0].SpillLevel == 4 && bowl.Basins[0].CapacityEstimate == 96,
            "analytic bowl spill and storage at arbitrary grid spacing");
        Check(bowl.Grid.Bed.SequenceEqual(bed), "priority flood preserves physical terrain");
        bed[12] = -999;
        Check(bowl.Grid.Bed[12] == 1, "caller cannot mutate sampled terrain");
        var high = new DrainageGrid(-4, -4, 2, 5, 5, bowl.Grid.Bed.Select(h => h + 100).ToArray());
        var elevated = DrainageAnalysis.Build(high, new[] { new DrainageOutlet(14, 100) }, "bowl-v1");
        Check(elevated.Basins[0].SpillLevel == 104 && elevated.Basins[0].CapacityEstimate == 96, "elevated lakes retain relative capacity");
        Check(elevated.Downstream.SequenceEqual(bowl.Downstream), "vertical translation preserves topology");
        var reaches = bowl.Reaches(4);
        Check(reaches.Count > 0 && reaches.All(r => r.To == bowl.Downstream[r.From]), "river prerequisites follow authoritative graph");
        Check(reaches.All(r => bowl.BasinIndex[r.From] < 0 || bowl.BasinIndex[r.From] != bowl.BasinIndex[r.To]), "no false river inside flat lake candidate");

        var nestedBed = Enumerable.Repeat(9d, 21).ToArray();
        double[] channel = { 0, 4, 1, 6, 2, 3, 9 };
        channel.CopyTo(nestedBed, 7);
        var nested = DrainageAnalysis.Build(new(0, 0, 1, 7, 3, nestedBed), new[] { new DrainageOutlet(7, 0) }, "nested-v1");
        Invariants(nested);
        Check(nested.Basins.Count == 2 && nested.Basins.Select(b => b.SpillLevel).Order().SequenceEqual(new[] { 4d, 6d }), "stepped basins retain distinct outlet levels");
        Check(nested.Basins.Sum(b => b.CapacityEstimate) == 10, "nested storage not double-counted");
        var diagonalBed = Enumerable.Repeat(9d, 9).ToArray(); diagonalBed[0] = diagonalBed[4] = 0;
        var diagonal = DrainageAnalysis.Build(new(0, 0, 1, 3, 3, diagonalBed), new[] { new DrainageOutlet(0, 0) }, "diagonal");
        Check(diagonal.SpillHeight[4] == 9, "diagonal low cells cannot leak through an intact ridge");
        var flatGrid = new DrainageGrid(-16, -16, 1, 33, 33, Enumerable.Repeat(5d, 33 * 33).ToArray());
        var roots = new[] { new DrainageOutlet(0, 5), new DrainageOutlet(1088, 5) };
        var flat = DrainageAnalysis.Build(flatGrid, roots, "flat");
        var reverse = DrainageAnalysis.Build(flatGrid, roots.Reverse(), "flat");
        Invariants(flat);
        Check(flat.Fingerprint == reverse.Fingerprint && flat.Downstream.SequenceEqual(reverse.Downstream), "outlet order cannot change ties");
        Check(flat.Basins.Count == 0, "flat terrain is not automatically a lake");
        Check(flat.Downstream.Where(p => p < 0).Count() == 2, "remaining domain edges are sealed, not invented drains");
        bool hasLeft = flat.Grid.TryCell(-.01, 0, out int left), hasRight = flat.Grid.TryCell(0, 0, out int right);
        Check(hasLeft && hasRight && right == left + 1, "negative coordinate boundary ownership");
        var leftWindow = flat.CellsForRegion(new(-1, 0)); var rightWindow = flat.CellsForRegion(new(0, 0));
        Check(leftWindow.Contains(right) && rightWindow.Contains(left), "chunk halos share cross-boundary routing cells");
        Check(flat.CellsForRegion(new(100, 100)).Count == 0 && !flat.Grid.TryCell(500, 500, out _), "unknown data stays unknown");
        Check(flat.Grid.TryCell(-16, -16, out _) && !flat.Grid.TryCell(17, 17, out _), "half-open domain bounds");
        Reject(() => DrainageAnalysis.Build(flatGrid, Array.Empty<DrainageOutlet>(), "none"), "missing outlets");
        Reject(() => DrainageAnalysis.Build(flatGrid, new[] { roots[0], roots[0] }, "duplicate"), "duplicate outlet");
        Reject(() => DrainageAnalysis.Build(flatGrid, new[] { new DrainageOutlet(-1, 0) }, "bad"), "invalid index");
        Reject(() => DrainageAnalysis.Build(flatGrid, new[] { new DrainageOutlet(0, double.NaN) }, "bad"), "invalid level");
        Reject(() => new DrainageGrid(0, 0, 1, 513, 1, new double[513]), "unbounded grid");
        Reject(() => bowl.Reaches(0), "zero catchment threshold");
        Reject(() => DrainageAnalysis.Build(flatGrid, roots, "cancelled", new CancellationToken(true)), "cancelled build");

        var profiles = MacroProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Content", "world_profiles.json"));
        var reports = new List<object>();
        MacroDrainageRecipe? savedRecipe = null; DrainageAnalysis? saved = null;
        foreach (var profile in profiles)
            foreach (int seed in new[] { -9, 0, 7319 })
            {
                var world = MacroWorldManifest.Create(seed, profile) with { WorldId = "0123456789abcdef0123456789abcdef" };
                var recipe = MacroDrainageRecipe.Create(world, 64);
                var watch = System.Diagnostics.Stopwatch.StartNew(); var a = MacroDrainage.Build(recipe); watch.Stop();
                Invariants(a);
                var b = MacroDrainage.Build(recipe);
                Check(a.Fingerprint == b.Fingerprint && a.Downstream.SequenceEqual(b.Downstream), "macro drainage deterministic across regeneration");
                var terrain = new MacroWorldGenerator(world);
                for (int i = 0; i < a.Grid.Count; i += 37)
                {
                    var point = a.Grid.Center(i);
                    Check(a.Grid.Bed[i] == terrain.Sample(point.X, point.Z).Elevation, "macro terrain is unchanged");
                }
                Check(a.Outlets.All(o => a.Grid.Bed[o.Cell] <= profile.SeaLevel), "only sampled ocean seeds outflow");
                Check(a.Outlets.All(o => a.BasinIndex[o.Cell] == -1), "ocean excluded from inland lake candidates");
                var region = WorldRegion.At(world.OriginX, world.OriginZ);
                Check(a.CellsForRegion(region).Count <= 9, "local coarse query bounded independent of total world extent");
                reports.Add(new { profile = profile.Id, seed, recipe.Resolution, cells = a.Grid.Count, outlets = a.Outlets.Count,
                    basins = a.Basins.Count, reaches = a.Reaches(a.Grid.Spacing * a.Grid.Spacing * 4).Count,
                    capacityEstimate = a.Basins.Sum(basin => basin.CapacityEstimate), milliseconds = watch.Elapsed.TotalMilliseconds, a.Fingerprint });
                if (profile.Id == "vast-world" && seed == 7319) { savedRecipe = recipe; saved = a; }
            }
        string directory = Path.Combine(Path.GetTempPath(), "thornwake-drainage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string file = Path.Combine(directory, "drainage.json");
            MacroDrainageStore.Create(file, savedRecipe!);
            byte[] original = File.ReadAllBytes(file);
            var loaded = MacroDrainageStore.Load(file);
            Check(loaded.Recipe == savedRecipe && loaded.Analysis.Fingerprint == saved!.Fingerprint, "frozen recipe save/regeneration");
            Check(loaded.Analysis.Basins.Select(b => b.Id).SequenceEqual(saved!.Basins.Select(b => b.Id)), "persistent basin identities reconstructed");
            for (int cell = 0; cell < saved!.Grid.Count; cell += 37)
                Check(JsonSerializer.Serialize(loaded.Analysis.DescribeCell(cell)) == JsonSerializer.Serialize(saved.DescribeCell(cell)),
                    "cell metadata survives frozen reconstruction");
            string cancelled = Path.Combine(directory, "cancelled.json");
            Reject(() => MacroDrainageStore.Create(cancelled, savedRecipe!, new CancellationToken(true)), "cancelled checkpoint creation");
            Check(!File.Exists(cancelled), "cancelled creation publishes no checkpoint");
            Reject(() => MacroDrainageStore.Load(file, new CancellationToken(true)), "cancelled checkpoint reconstruction");
            Reject(() => loaded.Analysis.DescribeCell(-1), "negative metadata cell");
            Reject(() => loaded.Analysis.DescribeCell(loaded.Analysis.Grid.Count), "metadata beyond the grid");
            Reject(() => MacroDrainageStore.Create(file, savedRecipe!), "overwrite of saved recipe");
            Check(original.SequenceEqual(File.ReadAllBytes(file)), "failed overwrite preserves original bytes");
            Check(Directory.GetFiles(directory, "*.tmp").Length == 0, "atomic-write temporary cleanup");
            var json = JsonNode.Parse(original)!; json["Recipe"]!["DrainageVersion"] = 999;
            File.WriteAllText(file, json.ToJsonString()); Reject(() => MacroDrainageStore.Load(file), "unsupported drainage version");
            json = JsonNode.Parse(original)!; json["ExpectedFingerprint"] = new string('0', 64);
            File.WriteAllText(file, json.ToJsonString()); Reject(() => MacroDrainageStore.Load(file), "changed output fingerprint");
            json = JsonNode.Parse(original)!; json["Recipe"]!["Resolution"] = 32;
            File.WriteAllText(file, json.ToJsonString()); Reject(() => MacroDrainageStore.Load(file), "silent resolution regeneration");
            json = JsonNode.Parse(original)!; json["Recipe"]!.AsObject().Remove("DrainageVersion");
            File.WriteAllText(file, json.ToJsonString()); Reject(() => MacroDrainageStore.Load(file), "missing required version");
            var prior = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new("tr-TR");
                Check(MacroDrainage.Build(savedRecipe!).Fingerprint == saved!.Fingerprint, "culture-independent reconstruction");
            }
            finally { CultureInfo.CurrentCulture = prior; }
        }
        finally { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        if (reportPath != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath))!);
            File.WriteAllText(reportPath, JsonSerializer.Serialize(new { algorithm = DrainageAnalysis.Algorithm, checks,
                note = "Coarse routing and candidate storage only; no generated riverbeds or local-water injection.", scenarios = reports }, new JsonSerializerOptions { WriteIndented = true }));
        }
        Console.WriteLine($"DRAINAGE_PASS: explicit outlets, deterministic flats, basin spill/storage, chunk windows and frozen reconstruction ({checks} checks)");
        return checks;
    }
}


