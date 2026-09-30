using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Frozen reconstruction inputs for an opt-in layer; contains no local fluid state.</summary>
public sealed record RegionalDrainageRecipe
{
    public required int Format { get; init; }
    public required int Algorithm { get; init; }
    public required int Subdivisions { get; init; }
    public required MacroDrainageRecipe Coarse { get; init; }
    public required string CoarseFingerprint { get; init; }
    public void Validate()
    {
        if (Format != 1 || Algorithm != RegionalDrainageRefiner.Algorithm || Subdivisions is not (16 or 32 or 64 or 128) ||
            Coarse == null || CoarseFingerprint == null || CoarseFingerprint.Length != 64 || !CoarseFingerprint.All(Uri.IsHexDigit))
            throw new InvalidDataException("Unsupported regional drainage recipe.");
        Coarse.Validate();
    }
    public string Digest()
    {
        Validate(); return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this))).ToLowerInvariant();
    }
}

public readonly record struct DrainagePort(string Id, int FromCell, int ToCell, MacroPoint Position, double BedElevation);
public readonly record struct CorridorPoint(double X, double Z, double BedElevation);
/// <summary>Relative network rank, NOT a calibrated discharge or an independently supplied water source.</summary>
public enum RiverFlowPotential { Headwater, Tributary, Mainstem }
/// <summary>Sampled bed corridor; cut/rise diagnostics describe a hypothetical downhill channel, not water motion across a lake.</summary>
public sealed record RefinedRiverRoute(string Id, string RiverId, string? IncomingPortId, string? OutgoingPortId,
    IReadOnlyList<CorridorPoint> Points, double MaximumUphillStep, double DownhillCutEstimate);
public readonly record struct LakeFootprintCell(int X, int Z, double MeanBed, int Component);
public sealed record RefinedLakePatch(string LakeId, string CoarseBasinId, double CandidateSpillElevation,
    IReadOnlyList<LakeFootprintCell> InteriorCells, int Components, int UnresolvedShoreCells,
    double SampledArea, double CapacityEstimate, IReadOnlyList<DrainagePort> Inlets, IReadOnlyList<DrainagePort> Outlets)
{
    public int BlockedOutletCount => Outlets.Count(p => p.BedElevation >= CandidateSpillElevation);
}
public readonly record struct RegionalCorridorLink(string Id, CorridorPoint From, CorridorPoint To, bool ContinuesUpstream, bool ContinuesDownstream);
public readonly record struct CorridorFragment(string LinkId, CorridorPoint From, CorridorPoint To,
    bool ContinuesUpstream, bool ContinuesDownstream);

/// <summary>One coarse cell refined on demand. No neighbouring tiles need to be loaded.</summary>
public sealed record RegionalDrainageTile(string LayerId, int Cell, MacroPoint Min, double Span, double SampleSpacing,
    int Side, IReadOnlyList<double> Bed, string RiverId, string? DownstreamRiverId, int DestinationCell,
    int RiverOrder, RiverFlowPotential FlowPotential, double CatchmentArea, IReadOnlyList<DrainagePort> Incoming,
    DrainagePort? Outgoing, IReadOnlyList<RefinedRiverRoute> Routes, IReadOnlyList<RegionalCorridorLink> Links, RefinedLakePatch? Lake)
{
    /// <summary>Read-only render-region clipping. Fragment ends remain continuations, never new sources/drains.</summary>
    public IReadOnlyList<CorridorFragment> CorridorsForRegion(WorldRegion region)
    {
        region.Validate(); double x0 = region.X * (double)WorldRegion.Size, z0 = region.Z * (double)WorldRegion.Size;
        double x1 = x0 + WorldRegion.Size, z1 = z0 + WorldRegion.Size;
        var result = new List<CorridorFragment>();
        foreach (var link in Links)
            {
                var a = link.From; var b = link.To;
                double dx = b.X - a.X, dz = b.Z - a.Z, lo = 0, hi = 1;
                // A segment lying on a border belongs to the region on the positive side.
                if (dx == 0 && (a.X < x0 || a.X >= x1) || dz == 0 && (a.Z < z0 || a.Z >= z1)) continue;
                bool Clip(double p, double q)
                {
                    if (p == 0) return q >= 0;
                    double t = q / p;
                    if (p < 0) { if (t > hi) return false; lo = Math.Max(lo, t); }
                    else { if (t < lo) return false; hi = Math.Min(hi, t); }
                    return true;
                }
                if (!Clip(-dx, a.X - x0) || !Clip(dx, x1 - a.X) || !Clip(-dz, a.Z - z0) || !Clip(dz, z1 - a.Z) || hi <= lo) continue;
                CorridorPoint At(double t) => new(a.X + dx * t, a.Z + dz * t, a.BedElevation + (b.BedElevation - a.BedElevation) * t);
                result.Add(new(link.Id, At(lo), At(hi), link.ContinuesUpstream || lo > 0, link.ContinuesDownstream || hi < 1));
            }
        return result.AsReadOnly();
    }
}

/// <summary>
/// Algorithm 1: canonical low-sill ports, bounded weighted terrain paths and conservative candidate lake patches.
/// Baseline proposals only. Uphill paths and disconnected lake components require further terrain validation.
/// </summary>
public sealed class RegionalDrainageRefiner
{
    public const int Algorithm = 1;
    public RegionalDrainageRecipe Recipe { get; }
    public DrainageAnalysis Drainage { get; }
    public string LayerId { get; }
    private readonly MacroWorldGenerator _world;
    private readonly int[] _order;
    private RegionalDrainageRefiner(RegionalDrainageRecipe recipe, DrainageAnalysis drainage, CancellationToken cancellation)
    {
        Recipe = recipe; Drainage = drainage; LayerId = "regional-drainage:" + recipe.Digest();
        _world = new(recipe.Coarse.World);
        int n = drainage.Grid.Count;
        _order = new int[n]; var waiting = new int[n]; var max = new int[n]; var maxCount = new int[n];
        for (int c = 0; c < n; c++) if (drainage.Downstream[c] >= 0) waiting[drainage.Downstream[c]]++;
        var work = new Queue<int>();
        for (int c = 0; c < n; c++) if (waiting[c] == 0) work.Enqueue(c);
        int processed = 0;
        while (work.TryDequeue(out int cell))
        {
            if ((processed++ & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            _order[cell] = max[cell] == 0 ? 1 : max[cell] + (maxCount[cell] >= 2 ? 1 : 0);
            int next = drainage.Downstream[cell]; if (next < 0) continue;
            if (_order[cell] > max[next]) { max[next] = _order[cell]; maxCount[next] = 1; }
            else if (_order[cell] == max[next]) maxCount[next]++;
            if (--waiting[next] == 0) work.Enqueue(next);
        }
        if (processed != n) throw new InvalidDataException("Coarse drainage graph contains a cycle.");
    }
    public static RegionalDrainageRefiner Create(MacroDrainageRecipe coarse, int subdivisions = 64, CancellationToken cancellation = default)
    {
        if (subdivisions is not (16 or 32 or 64 or 128)) throw new ArgumentOutOfRangeException(nameof(subdivisions));
        var drainage = MacroDrainage.Build(coarse, cancellation);
        var recipe = new RegionalDrainageRecipe { Format = 1, Algorithm = Algorithm, Subdivisions = subdivisions, Coarse = coarse, CoarseFingerprint = drainage.Fingerprint };
        return new(recipe, drainage, cancellation);
    }
    public static RegionalDrainageRefiner Restore(RegionalDrainageRecipe recipe, CancellationToken cancellation = default)
    {
        recipe.Validate(); var drainage = MacroDrainage.Build(recipe.Coarse, cancellation);
        if (drainage.Fingerprint != recipe.CoarseFingerprint) throw new InvalidDataException("Regional drainage dependency changed.");
        return new(recipe, drainage, cancellation);
    }
    public string RiverId(int cell) { _ = Drainage.Grid.Center(cell); return $"{LayerId}:river:{cell}"; }
    public string LakeId(int basin)
    {
        if (basin < 0 || basin >= Drainage.Basins.Count) throw new ArgumentOutOfRangeException(nameof(basin));
        return $"{LayerId}:lake:{Drainage.Basins[basin].Cells[0]}";
    }
    private MacroPoint Node(int cell, int x, int z)
    {
        var grid = Drainage.Grid; int subdivisions = Recipe.Subdivisions;
        // Identical global lattice arithmetic on both sides of a tile boundary, including far origins.
        double step = grid.Spacing / subdivisions;
        return new(grid.MinX + ((long)(cell % grid.Width) * subdivisions + x) * step,
            grid.MinZ + ((long)(cell / grid.Width) * subdivisions + z) * step);
    }
    public DrainagePort Port(int fromCell, CancellationToken cancellation = default)
    {
        _ = Drainage.Grid.Center(fromCell); int to = Drainage.Downstream[fromCell];
        if (to < 0) throw new ArgumentException("Ocean terminal has no downstream port.", nameof(fromCell));
        int width = Drainage.Grid.Width, s = Recipe.Subdivisions;
        int dx = to % width - fromCell % width, dz = to / width - fromCell / width;
        MacroPoint best = default; double height = double.PositiveInfinity;
        for (int i = 1; i < s; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            var p = Node(fromCell, dx == 0 ? i : dx > 0 ? s : 0, dz == 0 ? i : dz > 0 ? s : 0);
            double h = _world.Sample(p.X, p.Z).Elevation;
            if (h < height) { best = p; height = h; }
        }
        return new($"{LayerId}:port:{fromCell}:{to}", fromCell, to, best, height);
    }
    public RegionalDrainageTile Build(int cell, CancellationToken cancellation = default)
    {
        cancellation.ThrowIfCancellationRequested(); var info = Drainage.DescribeCell(cell);
        int s = Recipe.Subdivisions, side = s + 1, count = side * side;
        double step = Drainage.Grid.Spacing / s;
        var bed = new double[count];
        for (int z = 0; z <= s; z++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x <= s; x++) { var p = Node(cell, x, z); bed[z * side + x] = _world.Sample(p.X, p.Z).Elevation; }
        }
        var incoming = info.UpstreamCells.Select(c => Port(c, cancellation)).ToArray();
        DrainagePort? outgoing = info.DownstreamCell.HasValue ? Port(cell, cancellation) : null;
        int Index(MacroPoint p)
        {
            var min = Node(cell, 0, 0);
            return (int)Math.Round((p.Z - min.Z) / step) * side + (int)Math.Round((p.X - min.X) / step);
        }
        int end = outgoing.HasValue ? Index(outgoing.Value.Position) : (s / 2) * side + s / 2;
        var allowedBorder = incoming.Select(p => Index(p.Position)).Append(end).ToHashSet();
        var distances = Enumerable.Repeat(double.PositiveInfinity, count).ToArray(); var parent = Enumerable.Repeat(-1, count).ToArray();
        var queue = new PriorityQueue<int, (double Cost, int Index)>(); distances[end] = 0; queue.Enqueue(end, (0, end));
        double low = bed.Min(); int visited = 0;
        while (queue.TryDequeue(out int current, out var priority))
        {
            if ((visited++ & 255) == 0) cancellation.ThrowIfCancellationRequested();
            if (priority.Cost != distances[current]) continue;
            int x = current % side, z = current / side;
            foreach (var (dx, dz) in Offsets)
            {
                int nx = x + dx, nz = z + dz;
                if (nx < 0 || nx > s || nz < 0 || nz > s) continue;
                int next = nz * side + nx;
                if ((nx == 0 || nx == s || nz == 0 || nz == s) && !allowedBorder.Contains(next)) continue;
                // Positive additive costs: prefer short lower-terrain routes and penalize actual downstream climbs.
                // These versioned heuristics propose corridors; they neither incise nor promise passable fluid paths.
                double cost = distances[current] + step * (1 + (bed[next] - low) * 4 / Drainage.Grid.Spacing)
                    + Math.Max(0, bed[current] - bed[next]) * 16;
                if (cost >= distances[next]) continue;
                distances[next] = cost; parent[next] = current; queue.Enqueue(next, (cost, next));
            }
        }
        var starts = incoming.Select(p => (Index: Index(p.Position), Port: (string?)p.Id)).ToList();
        // A headwater is a candidate corridor origin, not an injected source. Ocean-only tiles have no route.
        if (starts.Count == 0 && outgoing.HasValue) starts.Add(((s / 2) * side + s / 2, null));
        var routes = new List<RefinedRiverRoute>(); var usedEdges = new HashSet<int>();
        foreach (var start in starts)
        {
            var points = new List<CorridorPoint>(); int at = start.Index;
            double maxUphill = 0, cut = 0, runningLow = bed[at];
            while (true)
            {
                cancellation.ThrowIfCancellationRequested(); var p = Node(cell, at % side, at / side);
                points.Add(new(p.X, p.Z, bed[at]));
                runningLow = Math.Min(runningLow, bed[at]); cut = Math.Max(cut, bed[at] - runningLow);
                if (at == end) break;
                int next = parent[at];
                if (next < 0 || points.Count > count) throw new InvalidDataException("No regional continuation to the prescribed port.");
                usedEdges.Add(at); maxUphill = Math.Max(maxUphill, bed[next] - bed[at]); at = next;
            }
            routes.Add(new($"{RiverId(cell)}:branch:{(start.Port == null ? "head" : incoming.Single(p => p.Id == start.Port).FromCell.ToString(System.Globalization.CultureInfo.InvariantCulture))}",
                RiverId(cell), start.Port, outgoing?.Id, points.AsReadOnly(), maxUphill, cut));
        }
        var hasUpstream = usedEdges.Select(n => parent[n]).Concat(incoming.Select(p => Index(p.Position))).ToHashSet();
        CorridorPoint Point(int n) { var p = Node(cell, n % side, n / side); return new(p.X, p.Z, bed[n]); }
        // Each used tree edge appears once, even when several tributary paths share the downstream trunk.
        var links = usedEdges.OrderBy(n => n).Select(n => new RegionalCorridorLink($"{RiverId(cell)}:link:{n}", Point(n), Point(parent[n]),
            hasUpstream.Contains(n), parent[n] != end || outgoing.HasValue)).ToArray();
        var lake = BuildLake(cell, bed, incoming, outgoing, cancellation);
        int order = _order[cell];
        return new(LayerId, cell, Node(cell, 0, 0), Drainage.Grid.Spacing, step, side, Array.AsReadOnly(bed), RiverId(cell),
            info.DownstreamCell.HasValue ? RiverId(info.DownstreamCell.Value) : null, info.OutletCell, order,
            order == 1 ? RiverFlowPotential.Headwater : order == 2 ? RiverFlowPotential.Tributary : RiverFlowPotential.Mainstem,
            info.CatchmentArea, Array.AsReadOnly(incoming), outgoing, routes.AsReadOnly(), Array.AsReadOnly(links), lake);
    }
    private RefinedLakePatch? BuildLake(int cell, double[] bed, DrainagePort[] incoming, DrainagePort? outgoing, CancellationToken cancellation)
    {
        int basinIndex = Drainage.BasinIndex[cell]; if (basinIndex < 0) return null;
        var basin = Drainage.Basins[basinIndex]; int s = Recipe.Subdivisions, side = s + 1;
        var mean = new double[s * s]; var wet = new bool[s * s]; var components = Enumerable.Repeat(-1, s * s).ToArray();
        int shores = 0;
        for (int z = 0; z < s; z++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < s; x++)
            {
                double a = bed[z * side + x], b = bed[z * side + x + 1], c = bed[(z + 1) * side + x], d = bed[(z + 1) * side + x + 1];
                double max = Math.Max(Math.Max(a, b), Math.Max(c, d)), min = Math.Min(Math.Min(a, b), Math.Min(c, d));
                int i = z * s + x; mean[i] = (a + b + c + d) * .25;
                wet[i] = max < basin.SpillLevel;
                if (!wet[i] && min < basin.SpillLevel) shores++;
            }
        }
        int componentCount = 0; var work = new Queue<int>();
        for (int start = 0; start < wet.Length; start++)
        {
            if (!wet[start] || components[start] >= 0) continue;
            components[start] = componentCount; work.Enqueue(start);
            while (work.TryDequeue(out int current))
            {
                if ((current & 255) == 0) cancellation.ThrowIfCancellationRequested();
                foreach (var (dx, dz) in Offsets)
                {
                    int x = current % s + dx, z = current / s + dz;
                    if (x < 0 || x >= s || z < 0 || z >= s) continue;
                    int next = z * s + x;
                    if (!wet[next] || components[next] >= 0) continue;
                    components[next] = componentCount; work.Enqueue(next);
                }
            }
            componentCount++;
        }
        var cells = Enumerable.Range(0, wet.Length).Where(i => wet[i]).Select(i => new LakeFootprintCell(i % s, i / s, mean[i], components[i])).ToArray();
        double area = Math.Pow(Drainage.Grid.Spacing / s, 2);
        var inlets = incoming.Where(p => Drainage.BasinIndex[p.FromCell] != basinIndex).ToArray();
        var outlets = outgoing.HasValue && Drainage.BasinIndex[outgoing.Value.ToCell] != basinIndex ? new[] { outgoing.Value } : Array.Empty<DrainagePort>();
        return new(LakeId(basinIndex), basin.Id, basin.SpillLevel, Array.AsReadOnly(cells), componentCount, shores,
            cells.Length * area, cells.Sum(c => (basin.SpillLevel - c.MeanBed) * area), Array.AsReadOnly(inlets), Array.AsReadOnly(outlets));
    }
    private static readonly (int X, int Z)[] Offsets = { (-1, 0), (1, 0), (0, -1), (0, 1) };
}



