using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Cell-centred coarse metadata. Bounds are NOT implicit drains or render chunk boundaries.</summary>
public sealed class DrainageGrid
{
    public double MinX { get; }
    public double MinZ { get; }
    public double Spacing { get; }
    public int Width { get; }
    public int Depth { get; }
    public IReadOnlyList<double> Bed { get; }
    public int Count => Width * Depth;
    public DrainageGrid(double minX, double minZ, double spacing, int width, int depth, IReadOnlyList<double> bed)
    {
        if (width < 1 || depth < 1 || width > 512 || depth > 512 ||
            !double.IsFinite(spacing) || spacing < .25 || spacing > 262144 || bed == null || bed.Count != width * depth)
            throw new ArgumentException("Unsupported drainage grid.");
        _ = WorldRegion.At(minX, minZ); _ = WorldRegion.At(minX + width * spacing, minZ + depth * spacing);
        var copy = bed.ToArray();
        if (copy.Any(h => !double.IsFinite(h) || Math.Abs(h) > 100000)) throw new ArgumentException("Invalid drainage height.");
        MinX = minX; MinZ = minZ; Spacing = spacing; Width = width; Depth = depth; Bed = Array.AsReadOnly(copy);
    }
    public MacroPoint Center(int index)
    {
        if (index < 0 || index >= Count) throw new ArgumentOutOfRangeException(nameof(index));
        return new(MinX + (index % Width + .5) * Spacing, MinZ + (index / Width + .5) * Spacing);
    }
    public bool TryCell(double x, double z, out int index)
    {
        index = -1;
        if (!double.IsFinite(x) || !double.IsFinite(z) || x < MinX || z < MinZ ||
            x >= MinX + Width * Spacing || z >= MinZ + Depth * Spacing) return false;
        index = (int)Math.Floor((z - MinZ) / Spacing) * Width + (int)Math.Floor((x - MinX) / Spacing);
        return true;
    }
}

public readonly record struct DrainageOutlet(int Cell, double Level);
public readonly record struct DrainageSpill(int From, int To);
/// <summary>A filled depression candidate; volume assumes full coarse square cells, not final lake geometry.</summary>
public sealed record DrainageBasin(string Id, double SpillLevel, double CapacityEstimate,
    IReadOnlyList<int> Cells, IReadOnlyList<DrainageSpill> Exits);
/// <summary>Routing prerequisite, NOT a carved channel or a local water source.</summary>
public readonly record struct DrainageReach(string Id, int From, int To, double CatchmentArea);

/// <summary>
/// Read-only natural routing facts for one global coarse cell. Spill elevation is a routing threshold,
/// not a physical water surface; catchment area is not flow. Links refer to this analysis layer's cells.
/// </summary>
public sealed record DrainageCellMetadata(string Id, int Cell, MacroPoint Center, double BedElevation,
    double SpillElevation, double CatchmentArea, int? DownstreamCell, int OutletCell,
    string? BasinId, IReadOnlyList<int> UpstreamCells);

public sealed class DrainageAnalysis
{
    public const int Algorithm = 1;
    public DrainageGrid Grid { get; }
    public string Fingerprint { get; }
    public IReadOnlyList<DrainageOutlet> Outlets { get; }
    public IReadOnlyList<double> SpillHeight { get; }
    public IReadOnlyList<int> Downstream { get; }
    public IReadOnlyList<int> OutletCell { get; }
    public IReadOnlyList<double> CatchmentArea { get; }
    public IReadOnlyList<int> BasinIndex { get; }
    public IReadOnlyList<DrainageBasin> Basins { get; }

    private DrainageAnalysis(DrainageGrid grid, string fingerprint, DrainageOutlet[] outlets, double[] spill,
        int[] downstream, int[] outletCell, double[] area, int[] basinIndex, List<DrainageBasin> basins)
    {
        Grid = grid; Fingerprint = fingerprint; Outlets = Array.AsReadOnly(outlets);
        SpillHeight = Array.AsReadOnly(spill); Downstream = Array.AsReadOnly(downstream);
        OutletCell = Array.AsReadOnly(outletCell); CatchmentArea = Array.AsReadOnly(area);
        BasinIndex = Array.AsReadOnly(basinIndex); Basins = basins.AsReadOnly();
    }

    public static DrainageAnalysis Build(DrainageGrid grid, IEnumerable<DrainageOutlet> outlets,
        string dependencyId, CancellationToken cancellation = default)
    {
        if (grid == null || outlets == null || string.IsNullOrWhiteSpace(dependencyId) || dependencyId.Length > 128)
            throw new ArgumentException("Drainage requires explicit inputs and dependency identity.");
        cancellation.ThrowIfCancellationRequested();
        var roots = outlets.Take(grid.Count + 1).OrderBy(o => o.Cell).ToArray();
        if (roots.Length == 0 || roots.Length > grid.Count || roots.Select(o => o.Cell).Distinct().Count() != roots.Length ||
            roots.Any(o => o.Cell < 0 || o.Cell >= grid.Count || !double.IsFinite(o.Level) || Math.Abs(o.Level) > 100000))
            throw new ArgumentException("At least one unique, valid explicit outlet is required.");
        // Specified binary scalar encoding, independent of locale, source order and process hashes.
        using var bytes = new MemoryStream();
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
        {
            writer.Write(Algorithm); writer.Write(dependencyId); writer.Write(grid.MinX); writer.Write(grid.MinZ);
            writer.Write(grid.Spacing); writer.Write(grid.Width); writer.Write(grid.Depth);
            foreach (double height in grid.Bed) writer.Write(height);
            foreach (var root in roots) { writer.Write(root.Cell); writer.Write(root.Level); }
        }
        string fingerprint = Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, (int)bytes.Length))).ToLowerInvariant();
        int n = grid.Count;
        var spill = new double[n]; var parent = Enumerable.Repeat(-1, n).ToArray();
        var owner = new int[n]; var seen = new bool[n]; var order = new int[n];
        // Unique cell index breaks equal-height ties; four-neighbour links cannot cross a diagonal ridge.
        var queue = new PriorityQueue<int, (double Height, int Cell)>();
        foreach (var root in roots)
        {
            seen[root.Cell] = true; owner[root.Cell] = root.Cell;
            spill[root.Cell] = Math.Max(grid.Bed[root.Cell], root.Level);
            queue.Enqueue(root.Cell, (spill[root.Cell], root.Cell));
        }
        int processed = 0;
        while (queue.TryDequeue(out int cell, out _))
        {
            if ((processed & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            order[processed++] = cell;
            foreach (int neighbour in Neighbours(grid, cell))
            {
                if (seen[neighbour]) continue;
                seen[neighbour] = true; parent[neighbour] = cell; owner[neighbour] = owner[cell];
                spill[neighbour] = Math.Max(grid.Bed[neighbour], spill[cell]);
                queue.Enqueue(neighbour, (spill[neighbour], neighbour));
            }
        }
        // Parents were removed from the heap before children were discovered: reverse order is a DAG reduction,
        // even over perfectly flat terrain. Area is a runoff prerequisite, not a flow velocity or source rate.
        var area = Enumerable.Repeat(grid.Spacing * grid.Spacing, n).ToArray();
        for (int k = n - 1; k >= 0; k--) if (parent[order[k]] >= 0) area[parent[order[k]]] += area[order[k]];
        var basinIndex = Enumerable.Repeat(-1, n).ToArray(); var basins = new List<DrainageBasin>();
        for (int start = 0; start < n; start++)
        {
            if ((start & 1023) == 0) cancellation.ThrowIfCancellationRequested();
            if (parent[start] < 0 || spill[start] <= grid.Bed[start] || basinIndex[start] >= 0) continue;
            int id = basins.Count; var cells = new List<int>(); var work = new Queue<int>();
            basinIndex[start] = id; work.Enqueue(start);
            while (work.TryDequeue(out int cell))
            {
                if ((cells.Count & 1023) == 0) cancellation.ThrowIfCancellationRequested();
                cells.Add(cell);
                foreach (int neighbour in Neighbours(grid, cell))
                    if (parent[neighbour] >= 0 && basinIndex[neighbour] < 0 && spill[neighbour] > grid.Bed[neighbour] &&
                        spill[neighbour] == spill[start])
                    { basinIndex[neighbour] = id; work.Enqueue(neighbour); }
            }
            cells.Sort();
            var exits = cells.Where(c => basinIndex[parent[c]] != id).Select(c => new DrainageSpill(c, parent[c])).ToArray();
            double capacity = cells.Sum(c => (spill[c] - grid.Bed[c]) * grid.Spacing * grid.Spacing);
            basins.Add(new($"drainage:{fingerprint}:basin:{start}", spill[start], capacity, cells.AsReadOnly(), Array.AsReadOnly(exits)));
        }
        // Include the derived topology, not just its inputs, so checkpoints detect accidental algorithm drift.
        using (var writer = new BinaryWriter(bytes, System.Text.Encoding.UTF8, true))
        {
            for (int c = 0; c < n; c++)
            { writer.Write(spill[c]); writer.Write(parent[c]); writer.Write(owner[c]); writer.Write(area[c]); writer.Write(basinIndex[c]); }
            foreach (var basin in basins)
            {
                writer.Write(basin.SpillLevel); writer.Write(basin.CapacityEstimate); writer.Write(basin.Exits.Count);
                foreach (var edge in basin.Exits) { writer.Write(edge.From); writer.Write(edge.To); }
            }
        }
        fingerprint = Convert.ToHexString(SHA256.HashData(bytes.GetBuffer().AsSpan(0, (int)bytes.Length))).ToLowerInvariant();
        basins = basins.Select(b => b with { Id = $"drainage:{fingerprint}:basin:{b.Cells[0]}" }).ToList();
        return new(grid, fingerprint, roots, spill, parent, owner, area, basinIndex, basins);
    }

    private static IEnumerable<int> Neighbours(DrainageGrid grid, int cell)
    {
        int x = cell % grid.Width, z = cell / grid.Width;
        if (x > 0) yield return cell - 1;
        if (x + 1 < grid.Width) yield return cell + 1;
        if (z > 0) yield return cell - grid.Width;
        if (z + 1 < grid.Depth) yield return cell + grid.Width;
    }

    /// <summary>
    /// Bounded query (at most four upstream neighbours). Identity survives reconstruction of this
    /// exact layer; it intentionally changes if its frozen inputs or algorithm output change.
    /// Querying an unloaded region never initializes water or introduces an outlet/source.
    /// </summary>
    public DrainageCellMetadata DescribeCell(int cell)
    {
        var center = Grid.Center(cell); // Validate before indexing any analysis array.
        var upstream = Neighbours(Grid, cell).Where(n => Downstream[n] == cell).OrderBy(n => n).ToArray();
        int basin = BasinIndex[cell], next = Downstream[cell];
        return new($"drainage:{Fingerprint}:cell:{cell}", cell, center, Grid.Bed[cell], SpillHeight[cell],
            CatchmentArea[cell], next < 0 ? null : next, OutletCell[cell],
            basin < 0 ? null : Basins[basin].Id, Array.AsReadOnly(upstream));
    }

    /// <summary>Half-open world bounds; missing coarse data stays unknown rather than becoming dry land or a drain.</summary>
    public bool TryDescribeCell(double worldX, double worldZ, out DrainageCellMetadata? metadata)
    {
        metadata = null;
        if (!Grid.TryCell(worldX, worldZ, out int cell)) return false;
        metadata = DescribeCell(cell); return true;
    }

    public IReadOnlyList<DrainageReach> Reaches(double minimumCatchmentArea)
    {
        if (!double.IsFinite(minimumCatchmentArea) || minimumCatchmentArea <= 0) throw new ArgumentOutOfRangeException(nameof(minimumCatchmentArea));
        var result = new List<DrainageReach>();
        for (int c = 0; c < Grid.Count; c++)
        {
            int next = Downstream[c];
            if (next < 0 || CatchmentArea[c] < minimumCatchmentArea || BasinIndex[c] >= 0 && BasinIndex[c] == BasinIndex[next]) continue;
            result.Add(new($"drainage:{Fingerprint}:reach:{c}", c, next, CatchmentArea[c]));
        }
        return result.AsReadOnly();
    }

    /// <summary>Shared global cells around a render region. Read-only window; never solves drainage at chunk edges.</summary>
    public IReadOnlyList<int> CellsForRegion(WorldRegion region, int halo = 1)
    {
        region.Validate(); if (halo < 0 || halo > 8) throw new ArgumentOutOfRangeException(nameof(halo));
        double x = region.X * (double)WorldRegion.Size, z = region.Z * (double)WorldRegion.Size;
        if (x + WorldRegion.Size <= Grid.MinX || z + WorldRegion.Size <= Grid.MinZ ||
            x >= Grid.MinX + Grid.Width * Grid.Spacing || z >= Grid.MinZ + Grid.Depth * Grid.Spacing) return Array.Empty<int>();
        int x0 = Math.Max(0, (int)Math.Floor((x - Grid.MinX) / Grid.Spacing) - halo);
        int z0 = Math.Max(0, (int)Math.Floor((z - Grid.MinZ) / Grid.Spacing) - halo);
        int x1 = Math.Min(Grid.Width - 1, (int)Math.Ceiling((x + WorldRegion.Size - Grid.MinX) / Grid.Spacing) - 1 + halo);
        int z1 = Math.Min(Grid.Depth - 1, (int)Math.Ceiling((z + WorldRegion.Size - Grid.MinZ) / Grid.Spacing) - 1 + halo);
        var result = new List<int>();
        for (int cz = z0; cz <= z1; cz++) for (int cx = x0; cx <= x1; cx++) result.Add(cz * Grid.Width + cx);
        return result.AsReadOnly();
    }
}


