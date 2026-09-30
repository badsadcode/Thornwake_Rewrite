using System.Text.Json;
using Thornwake.Core;

/// <summary>Independent reference checks and frozen reconstruction evidence for the interrupted C2 layer.</summary>
internal static class DrainageValidationTests
{
    public static void Run(Action<bool, string> check, Action<DrainageAnalysis> invariants)
    {
        // Independent oracle: raise a horizontal threshold through every possible bed/outlet height,
        // and find face-connected cells reachable from eligible roots at that threshold. No heap,
        // downstream graph, priority-flood visitation order or production neighbour helper is used.
        double[] ReferenceSpills(DrainageGrid grid, IReadOnlyList<DrainageOutlet> roots)
        {
            var rootLevels = roots.ToDictionary(r => r.Cell, r => Math.Max(grid.Bed[r.Cell], r.Level));
            var result = Enumerable.Repeat(double.PositiveInfinity, grid.Count).ToArray();
            foreach (var root in rootLevels) result[root.Key] = root.Value;
            foreach (double level in grid.Bed.Concat(rootLevels.Values).Distinct().Order())
            {
                var visited = new bool[grid.Count]; var work = new Queue<int>();
                foreach (var root in rootLevels.Where(r => r.Value <= level))
                { visited[root.Key] = true; work.Enqueue(root.Key); }
                while (work.TryDequeue(out int cell))
                {
                    if (!rootLevels.ContainsKey(cell)) result[cell] = Math.Min(result[cell], level);
                    int x = cell % grid.Width, z = cell / grid.Width;
                    foreach (var (dx, dz) in new[] { (-1, 0), (1, 0), (0, -1), (0, 1) })
                    {
                        int nx = x + dx, nz = z + dz;
                        if (nx < 0 || nx >= grid.Width || nz < 0 || nz >= grid.Depth) continue;
                        int next = nz * grid.Width + nx;
                        if (visited[next] || grid.Bed[next] > level ||
                            rootLevels.TryGetValue(next, out double rootLevel) && rootLevel > level) continue;
                        visited[next] = true; work.Enqueue(next);
                    }
                }
            }
            return result;
        }

        void ReferenceCheck(DrainageGrid grid, DrainageOutlet[] roots, string name)
        {
            var analysis = DrainageAnalysis.Build(grid, roots, name);
            var reference = ReferenceSpills(grid, roots);
            invariants(analysis);
            check(reference.All(double.IsFinite) && reference.SequenceEqual(analysis.SpillHeight),
                name + ": independent threshold connectivity agrees with minimum spill heights");
            var reversed = DrainageAnalysis.Build(grid, roots.Reverse(), name);
            check(reversed.Fingerprint == analysis.Fingerprint, name + ": outlet enumeration cannot affect reconstruction");

            // Count every originating cell along its complete route, rather than reusing the reverse-heap reduction.
            var expectedArea = new double[grid.Count];
            for (int start = 0; start < grid.Count; start++)
                for (int cell = start; cell >= 0; cell = analysis.Downstream[cell])
                    expectedArea[cell] += grid.Spacing * grid.Spacing;
            check(expectedArea.SequenceEqual(analysis.CatchmentArea), name + ": exact contributing area at every cell");
            var rootSet = roots.Select(r => r.Cell).ToHashSet();
            var allBasinCells = analysis.Basins.SelectMany(b => b.Cells).ToArray();
            var expectedWetCandidates = Enumerable.Range(0, grid.Count)
                .Where(c => !rootSet.Contains(c) && reference[c] > grid.Bed[c]).ToArray();
            check(allBasinCells.Distinct().Count() == allBasinCells.Length &&
                allBasinCells.Order().SequenceEqual(expectedWetCandidates), name + ": candidate storage covers depressions once, excluding outlets");
            double capacity = expectedWetCandidates.Sum(c => (reference[c] - grid.Bed[c]) * grid.Spacing * grid.Spacing);
            check(analysis.Basins.Sum(b => b.CapacityEstimate) == capacity, name + ": capacity agrees with independent fill heights");
            foreach (var basin in analysis.Basins)
            {
                var remaining = basin.Cells.ToHashSet(); var work = new Queue<int>();
                work.Enqueue(basin.Cells[0]); remaining.Remove(basin.Cells[0]);
                while (work.TryDequeue(out int cell))
                    foreach (int next in remaining.ToArray())
                        if (Math.Abs(cell % grid.Width - next % grid.Width) + Math.Abs(cell / grid.Width - next / grid.Width) == 1)
                        { remaining.Remove(next); work.Enqueue(next); }
                check(remaining.Count == 0, name + ": basin cells are face-connected, not merged by equal level alone");
                var expectedExits = basin.Cells.Where(c => !basin.Cells.Contains(analysis.Downstream[c]))
                    .Select(c => new DrainageSpill(c, analysis.Downstream[c]));
                check(basin.Exits.SequenceEqual(expectedExits), name + ": all routing exits retained");
                check(basin.Cells.All(c => analysis.Basins[analysis.BasinIndex[c]].Id == basin.Id), name + ": candidate identity agrees with cell membership");
            }
            CheckMetadata(analysis, check);
        }

        ReferenceCheck(new(0, 0, 1, 7, 1, new double[] { 0, 4, 1, 4, 1, 4, 0 }),
            new[] { new DrainageOutlet(0, 0), new DrainageOutlet(6, 0) }, "separated-equal-spills");
        var separated = DrainageAnalysis.Build(new(0, 0, 1, 7, 1, new double[] { 0, 4, 1, 4, 1, 4, 0 }),
            new[] { new DrainageOutlet(0, 0), new DrainageOutlet(6, 0) }, "separated-equal-spills");
        check(separated.Basins.Count == 2 && separated.Basins.All(b => b.SpillLevel == 4 && b.CapacityEstimate == 3),
            "two equal-height depressions separated by a dry saddle remain distinct candidates");
        ReferenceCheck(new(0, 0, 1, 5, 1, new double[] { 0, 0, 0, 0, 0 }),
            new[] { new DrainageOutlet(0, 2), new DrainageOutlet(4, 7) }, "unequal-boundary-heads");
        ReferenceCheck(new(0, 0, 2, 1, 1, new double[] { 3 }), new[] { new DrainageOutlet(0, -2) }, "single-outlet-cell");
        ReferenceCheck(new(-16, -32, .5, 2, 2, new double[] { -3, 0, 1, 5 }),
            new[] { new DrainageOutlet(0, 0), new DrainageOutlet(1, 2), new DrainageOutlet(2, 1), new DrainageOutlet(3, 8) }, "all-explicit-outlets");

        var random = new Random(2937319);
        for (int scenario = 0; scenario < 96; scenario++)
        {
            int width = random.Next(1, 9), depth = random.Next(1, 9);
            var heights = Enumerable.Range(0, width * depth).Select(_ => (double)random.Next(-4, 10)).ToArray();
            var outletCells = Enumerable.Range(0, heights.Length).OrderBy(_ => random.Next()).Take(Math.Min(heights.Length, 1 + scenario % 4));
            var roots = outletCells.Select(c => new DrainageOutlet(c, random.Next(-4, 11))).ToArray();
            ReferenceCheck(new(-32, -48, scenario % 2 == 0 ? .5 : 2, width, depth, heights), roots, "reference-" + scenario);
        }

        // Global metadata ownership and overlapping read windows remain independent of chunk order and far origin.
        double originX = 8_000_000_000, originZ = -7_000_000_000;
        var far = DrainageAnalysis.Build(new(originX - 16, originZ - 16, .5, 64, 64, Enumerable.Repeat(3d, 4096).ToArray()),
            new[] { new DrainageOutlet(0, 3) }, "far-boundary");
        var west = WorldRegion.At(originX - 1, originZ); var east = WorldRegion.At(originX, originZ);
        var first = far.CellsForRegion(west).ToDictionary(c => c, c => JsonSerializer.Serialize(far.DescribeCell(c)));
        var second = far.CellsForRegion(east).Reverse().ToDictionary(c => c, c => JsonSerializer.Serialize(far.DescribeCell(c)));
        var shared = first.Keys.Intersect(second.Keys).ToArray();
        check(shared.Length > 0 && shared.All(c => first[c] == second[c]), "far-origin neighbouring chunks share identical metadata regardless of query order");
        check(far.TryDescribeCell(Math.BitDecrement(originX), originZ, out var left) &&
            far.TryDescribeCell(originX, originZ, out var right) && right!.Cell == left!.Cell + 1,
            "far-origin exact boundary has one half-open owner");
        check(far.TryDescribeCell(originX - 16, originZ - 16, out var corner) && corner!.Cell == 0 &&
            !far.TryDescribeCell(originX + 16, originZ, out var outside) && outside == null,
            "outer metadata bounds do not wrap or clamp into the world");
        check(!far.TryDescribeCell(double.NaN, originZ, out var missing) && missing == null &&
            !far.TryDescribeCell(originX, double.PositiveInfinity, out _), "nonfinite metadata queries stay unknown");
        var detailed = far.Reaches(.25).ToDictionary(r => r.Id);
        check(far.Reaches(4).All(r => detailed.TryGetValue(r.Id, out var lower) && r == lower),
            "reach display threshold changes selection, not identity or routing");

        CheckFrozenWorlds(check);
    }

    private static void CheckMetadata(DrainageAnalysis analysis, Action<bool, string> check)
    {
        for (int cell = 0; cell < analysis.Grid.Count; cell++)
        {
            var item = analysis.DescribeCell(cell);
            check(item.Id == $"drainage:{analysis.Fingerprint}:cell:{cell}" && item.Cell == cell && item.Center == analysis.Grid.Center(cell),
                "query identity includes frozen layer and global cell");
            check(item.BedElevation == analysis.Grid.Bed[cell] && item.SpillElevation == analysis.SpillHeight[cell] &&
                item.CatchmentArea == analysis.CatchmentArea[cell], "bed, routing height and contributing area remain distinct metadata");
            check(item.UpstreamCells.SequenceEqual(Enumerable.Range(0, analysis.Grid.Count).Where(c => analysis.Downstream[c] == cell)),
                "bounded upstream query finds every tributary once");
            check(item.DownstreamCell == (analysis.Downstream[cell] < 0 ? null : analysis.Downstream[cell]) && item.OutletCell == analysis.OutletCell[cell],
                "terminal outlets and intermediate downstream links stay distinct");
            check(item.BasinId == (analysis.BasinIndex[cell] < 0 ? null : analysis.Basins[analysis.BasinIndex[cell]].Id),
                "candidate basin identity agrees with query");
            check(analysis.TryDescribeCell(item.Center.X, item.Center.Z, out var sampled) && sampled!.Id == item.Id,
                "world-coordinate lookup returns the same global cell");
            if (item.UpstreamCells.Count > 0)
            {
                try { ((IList<int>)item.UpstreamCells)[0] = -1; throw new Exception("Metadata allows mutation"); }
                catch (NotSupportedException) { check(true, "upstream metadata is read-only"); }
            }
        }
    }

    private static void CheckFrozenWorlds(Action<bool, string> check)
    {
        var fixture = JsonSerializer.Deserialize<FrozenFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "drainage-v1.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        check(fixture.MacroAlgorithm == 3 && fixture.DrainageAlgorithm == DrainageAnalysis.Algorithm,
            "existing frozen algorithms still supported");
        check(fixture.Profiles.Length == 5 && fixture.Scenarios.Length == 15, "complete pre-continuation fixture matrix");
        foreach (var scenario in fixture.Scenarios)
        {
            var profile = fixture.Profiles.Single(p => p.Id == scenario.Profile);
            var world = MacroWorldManifest.Create(scenario.Seed, profile,algorithm:fixture.MacroAlgorithm) with { WorldId = fixture.WorldId };
            var analysis = MacroDrainage.Build(MacroDrainageRecipe.Create(world, fixture.Resolution));
            check(analysis.Fingerprint == scenario.Fingerprint, $"frozen {scenario.Profile}/{scenario.Seed} reconstruction matches pre-continuation output");
        }
    }

    private sealed record FrozenFixture(int MacroAlgorithm, int DrainageAlgorithm, int Resolution, string WorldId,
        MacroWorldProfile[] Profiles, FrozenScenario[] Scenarios);
    private sealed record FrozenScenario(string Profile, int Seed, string Fingerprint);
}
