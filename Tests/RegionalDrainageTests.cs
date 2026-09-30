using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Thornwake.Core;

internal static class RegionalDrainageTests
{
    public static int Run(string? reportPath = null)
    {
        int checks = 0;
        void Check(bool value, string reason) { if (!value) throw new Exception("REGIONAL_DRAINAGE: " + reason); checks++; }
        void Reject(Action action, string reason)
        {
            try { action(); } catch (Exception e) when (e is ArgumentException or InvalidDataException or OperationCanceledException) { checks++; return; }
            throw new Exception("REGIONAL_DRAINAGE accepted " + reason);
        }
        string Serialize(RegionalDrainageTile tile) => JsonSerializer.Serialize(tile);
        using var frozenWorlds = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "drainage-v1.json")));
        var profiles = frozenWorlds.RootElement.GetProperty("Profiles").Deserialize<MacroWorldProfile[]>()!;
        using var golden = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "regional-drainage-v1.json")));
        Check(golden.RootElement.GetProperty("Algorithm").GetInt32() == RegionalDrainageRefiner.Algorithm, "frozen regional algorithm supported");
        var reports = new List<object>();
        foreach (string id in new[] { "vast-world", "archipelago" })
        {
            var world = MacroWorldManifest.Create(7319, profiles.Single(p => p.Id == id), originX: 8_000_000_000, originZ: -7_000_000_000)
                with { WorldId = "0123456789abcdef0123456789abcdef" };
            var generator = new MacroWorldGenerator(world);
            var refiner = RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(world));
            var recipe = JsonSerializer.Deserialize<RegionalDrainageRecipe>(JsonSerializer.Serialize(refiner.Recipe))!;
            var restored = RegionalDrainageRefiner.Restore(recipe);
            var drainage = refiner.Drainage;
            Check(restored.LayerId == refiner.LayerId && restored.Drainage.Fingerprint == drainage.Fingerprint, "frozen recipe reconstructs dependencies and identity");
            var cells = Enumerable.Range(0, drainage.Grid.Count).Where(c => drainage.Downstream[c] >= 0)
                .OrderByDescending(c => drainage.CatchmentArea[c]).Take(6)
                .Concat(drainage.Basins.Take(4).SelectMany(b => b.Cells.Take(2)))
                .Distinct().ToArray();
            var tiles = new Dictionary<int, RegionalDrainageTile>();
            var watch = System.Diagnostics.Stopwatch.StartNew();
            foreach (int cell in cells) tiles[cell] = refiner.Build(cell);
            watch.Stop();
            int conflicts = 0, pathPoints = 0, lakeSamples = 0, nonStraight = 0;
            foreach (int cell in cells.Reverse())
            {
                var tile = tiles[cell]; var regenerated = restored.Build(cell);
                Check(Serialize(tile) == Serialize(regenerated), "tile data independent of reconstruction/build order");
                string hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Serialize(tile)))).ToLowerInvariant();
                Check(hash == golden.RootElement.GetProperty("Tiles").GetProperty(id).GetProperty(cell.ToString(CultureInfo.InvariantCulture)).GetString(),
                    "regional v1 fixed reconstruction fingerprint");
                Check(tile.Bed.Count == 65 * 65 && tile.SampleSpacing == drainage.Grid.Spacing / 64, "one bounded regional lattice, not whole-world fine data");
                Check(tile.RiverId == refiner.RiverId(cell) && tile.DownstreamRiverId == refiner.RiverId(drainage.Downstream[cell]) &&
                    tile.DestinationCell == drainage.OutletCell[cell], "stable river continuation and terminal relationship");
                var children = drainage.DescribeCell(cell).UpstreamCells.Select(c => refiner.Build(c).RiverOrder).ToArray();
                int max = children.Length == 0 ? 0 : children.Max();
                Check(tile.RiverOrder == (max == 0 ? 1 : max + (children.Count(o => o == max) > 1 ? 1 : 0)), "Strahler tributary order");
                var next = restored.Build(drainage.Downstream[cell]);
                Check(tile.Outgoing.HasValue && next.Incoming.Contains(tile.Outgoing.Value), "neighbours reconstruct identical shared port and elevation");
                int dxTile = next.Cell % drainage.Grid.Width - tile.Cell % drainage.Grid.Width;
                int dzTile = next.Cell / drainage.Grid.Width - tile.Cell / drainage.Grid.Width;
                for (int edge = 0; edge < tile.Side; edge++)
                {
                    int a = dxTile != 0 ? edge * tile.Side + (dxTile > 0 ? tile.Side - 1 : 0) : (dzTile > 0 ? tile.Side - 1 : 0) * tile.Side + edge;
                    int b = dxTile != 0 ? edge * tile.Side + (dxTile > 0 ? 0 : tile.Side - 1) : (dzTile > 0 ? 0 : tile.Side - 1) * tile.Side + edge;
                    Check(tile.Bed[a] == next.Bed[b], "all shared boundary terrain samples are bit-identical");
                }
                foreach (var route in tile.Routes)
                {
                    Check(route.Points.Count >= 2 && route.Points.Count <= tile.Bed.Count, "bounded connected route to prescribed exit");
                    var last = route.Points[^1]; var port = tile.Outgoing!.Value;
                    Check(last.X == port.Position.X && last.Z == port.Position.Z && last.BedElevation == port.BedElevation && route.OutgoingPortId == port.Id,
                        "all branches end at declared continuation, never an arbitrary tile edge");
                    var nextRoute = next.Routes.Single(r => r.IncomingPortId == port.Id);
                    Check(nextRoute.Points[0] == last, "corridor endpoints are bit-identical across independently reconstructed tiles");
                    double low = route.Points[0].BedElevation, cut = 0, rise = 0;
                    for (int i = 0; i < route.Points.Count; i++)
                    {
                        var p = route.Points[i];
                        Check(double.IsFinite(p.BedElevation) && p.BedElevation == generator.Sample(p.X, p.Z).Elevation, "route uses unmodified authoritative terrain");
                        Check(p.X >= tile.Min.X && p.X <= tile.Min.X + tile.Span && p.Z >= tile.Min.Z && p.Z <= tile.Min.Z + tile.Span,
                            "regional path stays inside bounded domain");
                        low = Math.Min(low, p.BedElevation); cut = Math.Max(cut, p.BedElevation - low);
                        if (i == 0) continue;
                        var before = route.Points[i - 1];
                        Check(Math.Abs(p.X - before.X) + Math.Abs(p.Z - before.Z) == tile.SampleSpacing, "face-connected path without coarse-centre jumps");
                        rise = Math.Max(rise, p.BedElevation - before.BedElevation);
                    }
                    Check(route.DownhillCutEstimate == cut && route.MaximumUphillStep == rise, "uphill conflicts are reported rather than silently carved");
                    if (rise > 0) conflicts++;
                    if (route.Points.Select(p => p.X).Distinct().Count() > 2 && route.Points.Select(p => p.Z).Distinct().Count() > 2) nonStraight++;
                    pathPoints += route.Points.Count;
                    // A segment crosses many 16 m chunks: compare neighbours on one actual shared face.
                    var a = route.Points[0]; var b = route.Points[1];
                    string linkId = tile.Links.Single(link => link.From == a && link.To == b).Id;
                    bool horizontal = a.X != b.X;
                    double coordinate = horizontal ? (a.X + b.X) * .5 : (a.Z + b.Z) * .5;
                    double boundary = Math.Floor(coordinate / WorldRegion.Size) * WorldRegion.Size;
                    var lowRegion = horizontal ? WorldRegion.At(boundary - .5, a.Z) : WorldRegion.At(a.X, boundary - .5);
                    var highRegion = horizontal ? WorldRegion.At(boundary, a.Z) : WorldRegion.At(a.X, boundary);
                    var lf = tile.CorridorsForRegion(lowRegion).Single(f => f.LinkId == linkId);
                    var hf = tile.CorridorsForRegion(highRegion).Single(f => f.LinkId == linkId);
                    bool forward = horizontal ? b.X > a.X : b.Z > a.Z;
                    Check((forward ? lf.To : lf.From) == (forward ? hf.From : hf.To), "clipped corridor meets exactly at render chunk boundary");
                    Check((forward ? lf.ContinuesDownstream && hf.ContinuesUpstream : hf.ContinuesDownstream && lf.ContinuesUpstream),
                        "clipped ends are continuations, not sources or drains");
                }
                var uniquePathEdges = tile.Routes.SelectMany(r => r.Points.Zip(r.Points.Skip(1), (a,b) => (a,b))).Distinct().ToHashSet();
                Check(tile.Links.Select(l => (l.From,l.To)).ToHashSet().SetEquals(uniquePathEdges) && tile.Links.Count == uniquePathEdges.Count,
                    "merged tributary trunk is represented once in the authoritative proposal graph");
                Check(tile.Links.Select(l => l.Id).Distinct().Count() == tile.Links.Count, "canonical edge identities unique");
                foreach (var link in tile.Links)
                {
                    Check(tile.Links.Count(l => l.From == link.To) <= 1, "shared regional trunk has only one downstream continuation");
                    if (link.ContinuesDownstream && tile.Links.All(l => l.From != link.To))
                        Check(tile.Outgoing.HasValue && link.To.X == tile.Outgoing.Value.Position.X && link.To.Z == tile.Outgoing.Value.Position.Z,
                            "regional graph terminates only at explicit handoff");
                }
                if (tile.Lake is { } lake)
                {
                    int basin = drainage.BasinIndex[cell]; var coarse = drainage.Basins[basin];
                    Check(lake.LakeId == refiner.LakeId(basin) && lake.CoarseBasinId == coarse.Id && lake.CandidateSpillElevation == coarse.SpillLevel,
                        "lake candidate identity and level retain coarse dependency");
                    Check(lake.Outlets.All(p => coarse.Exits.Contains(new DrainageSpill(p.FromCell, p.ToCell))), "lake outflow corresponds to declared basin spill");
                    Check(lake.Inlets.All(p => drainage.BasinIndex[p.FromCell] != basin && p.ToCell == cell), "lake inlet excludes internal basin connections");
                    Check(lake.SampledArea == lake.InteriorCells.Count * tile.SampleSpacing * tile.SampleSpacing && lake.SampledArea <= tile.Span * tile.Span,
                        "non-overlapping finite patch area");
                    Check(Math.Abs(lake.CapacityEstimate - lake.InteriorCells.Sum(c => (lake.CandidateSpillElevation - c.MeanBed) * tile.SampleSpacing * tile.SampleSpacing)) < .01,
                        "finite conceptual capacity at candidate level");
                    foreach (var wet in lake.InteriorCells)
                    {
                        int i = wet.Z * tile.Side + wet.X;
                        Check(new[] { tile.Bed[i], tile.Bed[i + 1], tile.Bed[i + tile.Side], tile.Bed[i + tile.Side + 1] }.All(h => h < lake.CandidateSpillElevation),
                            "lake interior does not include sampled rock above the candidate spill");
                    }
                    Check(lake.BlockedOutletCount == lake.Outlets.Count(p => p.BedElevation >= lake.CandidateSpillElevation), "geometric spill blockage is explicit");
                    Check(lake.InteriorCells.Select(c => c.Component).Distinct().Count() == lake.Components, "all candidate components have samples");
                    foreach (var component in lake.InteriorCells.GroupBy(c => c.Component))
                    {
                        var remaining = component.Select(c => (c.X, c.Z)).ToHashSet(); var work = new Queue<(int X,int Z)>();
                        var first = remaining.First(); remaining.Remove(first); work.Enqueue(first);
                        while (work.TryDequeue(out var c))
                            foreach (var n in new[] { (c.X - 1,c.Z), (c.X + 1,c.Z), (c.X,c.Z - 1), (c.X,c.Z + 1) })
                                if (remaining.Remove(n)) work.Enqueue(n);
                        Check(remaining.Count == 0, "candidate patch never merges disconnected interiors");
                    }
                    lakeSamples += lake.InteriorCells.Count;
                }
            }
            Check(conflicts > 0 && nonStraight > 0 && lakeSamples > 0, "fixture exercises barriers, terrain-shaped paths and candidate lakes");
            int terminal = drainage.OutletCell[cells[0]];
            var ocean = refiner.Build(terminal);
            Check(ocean.Outgoing == null && ocean.DownstreamRiverId == null && ocean.Lake == null, "only explicit ocean terminates the skeleton");
            Reject(() => refiner.Port(terminal), "ocean creates a phantom edge port");
            Reject(() => refiner.Build(-1), "out of bounds tile");
            Reject(() => refiner.Build(cells[0], new CancellationToken(true)), "cancelled tile");
            Reject(() => RegionalDrainageRefiner.Restore(recipe with { CoarseFingerprint = new string('0', 64) }), "mismatched coarse dependency");
            Reject(() => RegionalDrainageRefiner.Restore(recipe with { Algorithm = 999 }), "unsupported refinement version");
            Reject(() => RegionalDrainageRefiner.Restore(recipe with { Subdivisions = 4096 }), "unbounded fine grid");
            var culture = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = new("tr-TR"); Check(Serialize(restored.Build(cells[0])) == Serialize(tiles[cells[0]]), "culture-independent identity and reconstruction"); }
            finally { CultureInfo.CurrentCulture = culture; }
            var concurrent = Task.WhenAll(cells.Take(3).Select(c => Task.Run(() => Serialize(refiner.Build(c))))).GetAwaiter().GetResult();
            Check(concurrent.SequenceEqual(cells.Take(3).Select(c => Serialize(tiles[c]))), "concurrent regional requests share no mutable solve state");
            var fine = RegionalDrainageRefiner.Restore(recipe with { Subdivisions = 128 });
            var fineTile = fine.Build(cells[0]);
            Check(fineTile.Side == 129 && fineTile.Bed.Count == 16641 && fine.LayerId != refiner.LayerId && fine.Drainage.Fingerprint == drainage.Fingerprint,
                "maximum regional resolution is bounded and versioned separately from unchanged coarse data");
            var map = RegionalDrainageMap.Build(generator, tiles[cells[0]], 128);
            var repeatedMap = RegionalDrainageMap.Build(generator, tile: tiles[cells[0]], resolution: 128);
            Check(map.Pixels.SequenceEqual(repeatedMap.Pixels), "regional diagnostic pixels deterministic");
            reports.Add(new { profile = id, cells = cells.Length, milliseconds = watch.Elapsed.TotalMilliseconds, sampleSpacing = tiles[cells[0]].SampleSpacing,
                pathPoints, uphillRoutes = conflicts, lakeSamples, refiner.LayerId,
                tileHashes = cells.ToDictionary(c => c.ToString(CultureInfo.InvariantCulture), c => Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Serialize(tiles[c])))).ToLowerInvariant()),
                firstTileHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Serialize(tiles[cells[0]])))).ToLowerInvariant() });
        }
        if (reportPath != null) File.WriteAllText(reportPath, JsonSerializer.Serialize(new { checks, scenarios = reports }, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"REGIONAL_DRAINAGE_PASS: reconstruction, canonical ports, terrain paths, lake patches, hierarchy and chunk continuity ({checks} checks)");
        return checks;
    }
}





