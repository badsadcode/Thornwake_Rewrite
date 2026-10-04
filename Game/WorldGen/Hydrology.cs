using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.WorldGen;

public enum WaterKind : byte { None, Ocean, Lake, River }

/// <summary>A lake: a depression left after erosion, filled to its spill level (finite storage, Supplement §9.4).</summary>
public sealed record Lake(int Id, double Level, double Area, double Volume, double MaxDepth, int OutletCell, Point2 Center)
{
    /// <summary>Reach that drains the lake (-1 if it spills straight into the sea or another lake).</summary>
    public int OutletReach { get; internal set; } = -1;
    public List<int> InflowReaches { get; } = new();
}

/// <summary>
/// One river reach between junctions: smoothed, meandering polyline with a strictly descending water surface.
/// Reaches link into a network: confluences start a new reach; the network ends in the sea or a lake.
/// </summary>
public sealed class RiverReach
{
    public int Id { get; init; }
    public required Point2[] Points { get; init; }
    /// <summary>Water surface elevation (absolute metres), strictly decreasing downstream.</summary>
    public required double[] Surface { get; set; }
    public required double[] Width { get; init; }
    public required double[] Depth { get; init; }
    /// <summary>Upstream catchment (km²) at each point — the discharge proxy until climate exists.</summary>
    public required double[] Catchment { get; init; }
    public int DownstreamReach { get; internal set; } = -1;
    public int EndLake { get; internal set; } = -1;
    public int StartLake { get; internal set; } = -1;
    public bool EndsInSea { get; internal set; }
    /// <summary>Delta branch splitting off a main reach near the sea (not part of the upstream network).</summary>
    public bool Distributary { get; internal set; }
    /// <summary>Main reach and point index this distributary branches from (-1 for ordinary reaches).</summary>
    public int ParentReach { get; internal set; } = -1;
    public int ParentPoint { get; internal set; } = -1;
    /// <summary>Raster cell where the reach ends (diagnostics).</summary>
    public int EndCell { get; internal set; } = -1;
    public List<int> UpstreamReaches { get; } = new();
}

/// <summary>Nearest-river result for a point.</summary>
public readonly record struct RiverHit(int Reach, double Distance, double Surface, double Width, double Depth, double Catchment);

/// <summary>
/// Raster-scale erosion and hydrology (replaces the original 17-file hydrology research chain; see DECISIONS D8).
/// <list type="number">
/// <item>Priority-flood (Barnes et al. 2014, ε-variant) from the sea: filled surface, steepest-descent receivers on
/// it, and a topological order — in one pass.</item>
/// <item>Implicit stream-power incision (Braun &amp; Willett 2013), no deposition, several iterations: dendritic
/// river valleys; peaks and ridges (small catchments) are barely touched.</item>
/// <item>Lakes = remaining depressions above size/depth thresholds; smaller pits are filled.</item>
/// <item>Rivers = cells above a catchment threshold, chained into reaches between junctions.</item>
/// </list>
/// All loops are single-threaded with fixed neighbour order, so the result is deterministic.
/// </summary>
public sealed class Hydrology
{
    public IReadOnlyList<Lake> Lakes => _lakes;
    public IReadOnlyList<RiverReach> Reaches => _reaches;
    /// <summary>Total river polyline points and spatial-index entries (memory diagnostics).</summary>
    public long PointCount => _reaches.Sum(r => (long)r.Points.Length);
    public long IndexEntries => _index.Entries;
    /// <summary>Lake id per raster cell (-1 none).</summary>
    public int[] LakeCell { get; }
    public double MaxIncision { get; }
    public double BuildMilliseconds { get; internal set; }

    private readonly MacroRaster _raster;
    private readonly List<Lake> _lakes = new();
    private readonly List<RiverReach> _reaches = new();
    private SegmentIndex _index = null!;
    private readonly double _seaLevel;


    private static readonly int[] Dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
    private static readonly int[] Dz = { 0, 1, 1, 1, 0, -1, -1, -1 };
    private static readonly double[] Dist = { 1, 1.4142135623730951, 1, 1.4142135623730951, 1, 1.4142135623730951, 1, 1.4142135623730951 };

    /// <summary>
    /// Erode <paramref name="height"/> (metres above sea; ≤ 0 is sea) in place and extract lakes and rivers.
    /// </summary>
    internal Hydrology(MacroRaster raster, double[] height, WorldProfile profile, TerrainStyle style, ulong seed)
    {
        _raster = raster; _seaLevel = profile.SeaLevel;
        int n = raster.Size, count = n * n;
        double cell = raster.Cell, cellArea = cell * cell;
        var original = (double[])height.Clone();
        var filled = new double[count];
        var receiver = new int[count];
        var order = new int[count];
        var area = new double[count];

        // ---- erosion
        double strength = style.ErosionStrength;
        for (int iteration = 0; iteration < style.ErosionIterations; iteration++)
        {
            int ordered = Flood(n, height, filled, receiver, order);
            Accumulate(ordered, order, receiver, area, cellArea);
            for (int k = 0; k < ordered; k++)
            {
                int c = order[k], r = receiver[c];
                if (r < 0 || height[c] <= 0) continue;
                double distance = Distance(c, r, n) * cell;
                double f = strength * Math.Sqrt(area[c]) / distance;
                double next = (height[c] + f * height[r]) / (1 + f);
                // No deposition: depressions stay depressions (they become lakes); land never sinks below the sea.
                if (next < height[c]) height[c] = Math.Max(next, .5);
            }
        }
        double maxIncision = 0;
        for (int c = 0; c < count; c++) maxIncision = Math.Max(maxIncision, original[c] - height[c]);
        MaxIncision = maxIncision;

        // ---- lakes
        int finalCount = Flood(n, height, filled, receiver, order);
        Accumulate(finalCount, order, receiver, area, cellArea);
        LakeCell = new int[count];
        Array.Fill(LakeCell, -1);
        var visited = new bool[count];
        var stack = new Stack<int>();
        var candidates = new List<(int[] Members, double Level, double MaxDepth, double Volume)>();
        double landCells = 0;
        for (int c = 0; c < count; c++) if (height[c] > 0) landCells++;
        for (int k = 0; k < finalCount; k++)
        {
            int start = order[k];
            if (visited[start] || height[start] <= 0 || filled[start] - height[start] < .01) continue;
            // Flood-fill the connected wet region of this depression (8-neighbour).
            var members = new List<int>();
            stack.Push(start); visited[start] = true;
            while (stack.Count > 0)
            {
                int c = stack.Pop(); members.Add(c);
                int cx = c % n, cz = c / n;
                for (int d = 0; d < 8; d++)
                {
                    int x = cx + Dx[d], z = cz + Dz[d];
                    if (x < 0 || z < 0 || x >= n || z >= n) continue;
                    int m = z * n + x;
                    if (visited[m] || height[m] <= 0 || filled[m] - height[m] < .01) continue;
                    visited[m] = true; stack.Push(m);
                }
            }
            double level = double.PositiveInfinity, maxDepth = 0, volume = 0;
            foreach (int c in members) level = Math.Min(level, filled[c]);
            foreach (int c in members) { double depth = level - height[c]; maxDepth = Math.Max(maxDepth, depth); if (depth > 0) volume += depth * cellArea; }
            candidates.Add((members.ToArray(), level, maxDepth, volume));
        }
        // Keep the most voluminous depressions as lakes, up to a share of the land area; fill the rest into flat
        // basin floors (sediment-filled plains) so water routes straight through them.
        double budget = style.LakeShare * landCells, used = 0;
        foreach (var candidate in candidates.OrderByDescending(c => c.Volume).ThenBy(c => c.Members[0]))
        {
            var members = candidate.Members;
            bool keep = members.Length >= style.LakeMinCells && candidate.MaxDepth >= style.LakeMinDepth && used + members.Length <= budget;
            if (!keep) { foreach (int c in members) height[c] = filled[c]; continue; }
            used += members.Length;
            int id = _lakes.Count;
            var set = new HashSet<int>(members);
            int outlet = members.FirstOrDefault(c => receiver[c] < 0 || !set.Contains(receiver[c]), members[0]);
            double sx = 0, sz = 0;
            foreach (int c in members) { LakeCell[c] = id; sx += raster.CellX(c % n); sz += raster.CellZ(c / n); }
            _lakes.Add(new Lake(id, _seaLevel + candidate.Level, members.Length * cellArea, candidate.Volume, candidate.MaxDepth, outlet,
                new Point2(sx / members.Length, sz / members.Length)));
        }

        // ---- rivers
        ExtractRivers(n, height, filled, receiver, order, finalCount, area, style, seed);


    }

    // ------------------------------------------------------------------ priority-flood

    /// <summary>
    /// Priority-flood from every sea cell bordering land. Fills depressions (with a tiny ε slope so every land cell
    /// drains), assigns steepest-descent receivers on the filled surface and returns cells in topological order
    /// (receivers before donors). Sea cells get receiver -1 and are not ordered unless they border land.
    /// </summary>
    private static int Flood(int n, double[] h, double[] filled, int[] receiver, int[] order)
    {
        const double Epsilon = 1e-3;
        int count = n * n;
        var queue = new PriorityQueue<int, double>(count / 8);
        var done = new bool[count];
        Array.Fill(receiver, -1);
        for (int c = 0; c < count; c++)
        {
            filled[c] = h[c];
            if (h[c] > 0) continue;
            done[c] = true; // sea is base level
            int cx = c % n, cz = c / n;
            for (int d = 0; d < 8; d++)
            {
                int x = cx + Dx[d], z = cz + Dz[d];
                if (x >= 0 && z >= 0 && x < n && z < n && h[z * n + x] > 0) { queue.Enqueue(c, h[c]); break; }
            }
        }
        int ordered = 0;
        // Heap only (no "pit queue" shortcut): pops are then in non-decreasing filled height, which makes the pop
        // order a valid topological order for steepest descent. The shortcut broke this at the ε scale.
        while (queue.Count > 0)
        {
            int c = queue.Dequeue();
            order[ordered++] = c;
            int cx = c % n, cz = c / n;
            for (int d = 0; d < 8; d++)
            {
                int x = cx + Dx[d], z = cz + Dz[d];
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                int m = z * n + x;
                if (done[m]) continue;
                done[m] = true;
                filled[m] = Math.Max(h[m], filled[c] + Epsilon);
                queue.Enqueue(m, filled[m]);
            }
        }
        // Steepest descent on the filled surface (every land cell has a lower neighbour by construction).
        for (int k = 0; k < ordered; k++)
        {
            int c = order[k];
            if (h[c] <= 0) continue;
            int cx = c % n, cz = c / n, best = -1;
            double bestSlope = 0;
            for (int d = 0; d < 8; d++)
            {
                int x = cx + Dx[d], z = cz + Dz[d];
                if (x < 0 || z < 0 || x >= n || z >= n) continue;
                int m = z * n + x;
                double slope = (filled[c] - filled[m]) / Dist[d];
                if (slope > bestSlope) { bestSlope = slope; best = m; }
            }
            receiver[c] = best;
        }
        return ordered;
    }

    private static void Accumulate(int ordered, int[] order, int[] receiver, double[] area, double cellArea)
    {
        for (int k = 0; k < ordered; k++) area[order[k]] = cellArea;
        for (int k = ordered - 1; k >= 0; k--)
        {
            int c = order[k], r = receiver[c];
            if (r >= 0) area[r] += area[c];
        }
    }

    private static double Distance(int a, int b, int n)
    {
        int dx = Math.Abs(a % n - b % n), dz = Math.Abs(a / n - b / n);
        return dx + dz == 2 ? 1.4142135623730951 : 1;
    }

    // ------------------------------------------------------------------ rivers

    private void ExtractRivers(int n, double[] height, double[] filled, int[] receiver, int[] order, int ordered, double[] area,
        TerrainStyle style, ulong seed)
    {
        int count = n * n;
        double minArea = style.RiverMinCatchmentKm2 * 1e6;
        bool IsRiver(int c) => c >= 0 && height[c] > 0 && LakeCell[c] < 0 && area[c] >= minArea;
        var donors = new int[count];
        for (int c = 0; c < count; c++)
            if (IsRiver(c) && receiver[c] >= 0 && IsRiver(receiver[c])) donors[receiver[c]]++;
        // Topological rank: position in flood order (receivers first). Upstream cells have higher rank.
        var rank = new int[count];
        for (int k = 0; k < ordered; k++) rank[order[k]] = k;

        var starts = new List<int>();
        for (int c = 0; c < count; c++)
            if (IsRiver(c) && donors[c] != 1) starts.Add(c);
        // Upstream reaches first, so a confluence knows the surfaces arriving at it.
        starts.Sort((a, b) => rank[b].CompareTo(rank[a]));
        // Pass 1: cell chains and water surfaces, upstream first.
        var chains = new List<(List<int> Cells, double[] Surface, double[] Width, double[] Depth, double[] Catchment, int End, int StartLake)>();
        var reachAt = new Dictionary<int, int>();
        var arriving = new Dictionary<int, double>(); // junction cell -> lowest arriving surface
        foreach (int start in starts)
        {
            var cells = new List<int> { start };
            int cur = start, end;
            while (true)
            {
                int r = receiver[cur];
                if (r < 0 || height[r] <= 0 || LakeCell[r] >= 0 || !IsRiver(r) || donors[r] >= 2) { end = r; if (r >= 0) cells.Add(r); break; }
                cells.Add(r); cur = r;
            }
            reachAt[start] = chains.Count;
            var widths = new double[cells.Count]; var depths = new double[cells.Count]; var catchment = new double[cells.Count];
            var surface = new double[cells.Count];
            for (int i = 0; i < cells.Count; i++)
            {
                double km2 = area[cells[i]] / 1e6;
                catchment[i] = km2;
                widths[i] = DetMath.Clamp(style.RiverWidthPerSqrtKm2 * Math.Sqrt(km2), style.RiverMinWidth, style.RiverMaxWidth);
                depths[i] = .5 + .35 * Math.Sqrt(widths[i]);
            }
            int startLake = -1;
            double previous;
            if (arriving.TryGetValue(start, out double arrive)) previous = arrive + .01;
            else
            {
                // A lake outlet starts at the lake surface.
                int sx = start % n, sz = start / n;
                for (int d = 0; d < 8 && startLake < 0; d++)
                {
                    int x = sx + Dx[d], z = sz + Dz[d];
                    if (x < 0 || z < 0 || x >= n || z >= n) continue;
                    int m = z * n + x;
                    if (LakeCell[m] >= 0 && receiver[m] == start) startLake = LakeCell[m];
                }
                previous = startLake >= 0 ? _lakes[startLake].Level - _seaLevel + .01 : height[start] - Freeboard(depths[0]) + .01;
            }
            // Below the bank (freeboard), strictly descending, never below the sea.
            for (int i = 0; i < cells.Count; i++)
            {
                int c = cells[i];
                double ground = height[c] > 0 ? height[c] : 0;
                double step = i == 0 ? .01 : style.RiverMinSlope * Distance(cells[i - 1], c, n) * _raster.Cell;
                double sv = Math.Min(previous - step, ground - Freeboard(depths[i]));
                if (i == 0 && startLake >= 0) sv = previous - .01; // an outlet leaves at the lake's own level
                if (LakeCell[c] >= 0) sv = Math.Max(sv, Math.Min(_lakes[LakeCell[c]].Level - _seaLevel, previous - 1e-3));
                sv = Math.Max(sv, 0);
                if (sv >= previous) sv = Math.Max(0, previous - 1e-4); // flat only where it meets the sea
                surface[i] = sv; previous = sv;
            }
            if (end >= 0 && height[end] > 0 && LakeCell[end] < 0)
                arriving[end] = arriving.TryGetValue(end, out double other) ? Math.Min(other, surface[^1]) : surface[^1];
            chains.Add((cells, surface, widths, depths, catchment, end, startLake));
        }

        // Pass 2: confluences. All reaches meeting at a junction and the reach leaving it share one level: the
        // lowest of them. Surfaces are only ever lowered (descent preserved); repeat until the network is stable.
        var chainAt = new Dictionary<int, int>();
        for (int i = 0; i < chains.Count; i++) chainAt[chains[i].Cells[0]] = i;
        for (int sweep = 0; sweep < 1000; sweep++)
        {
            bool changed = false;
            var lowest = new Dictionary<int, double>();
            foreach (var chain in chains)
                if (chain.End >= 0 && chainAt.ContainsKey(chain.End))
                    lowest[chain.End] = lowest.TryGetValue(chain.End, out double o) ? Math.Min(o, chain.Surface[^1]) : chain.Surface[^1];
            foreach (var (junction, arrive) in lowest)
            {
                var down = chains[chainAt[junction]].Surface;
                double level = Math.Min(arrive, down[0]);
                if (down[0] != level)
                {
                    down[0] = level; changed = true;
                    for (int i = 1; i < down.Length; i++)
                        if (down[i] >= down[i - 1]) down[i] = Math.Max(0, down[i - 1] - 1e-4); else break;
                }
            }
            foreach (var chain in chains)
                if (chain.End >= 0 && chainAt.TryGetValue(chain.End, out int d) && chain.Surface[^1] != chains[d].Surface[0])
                { chain.Surface[^1] = Math.Min(chain.Surface[^1], chains[d].Surface[0]); changed = true; }
            if (!changed) break;
        }

        // Pass 2b: a river entering a lake arrives at (not below) the lake surface. Raise its lower stretch and,
        // through confluences, its tributaries — the minimal raise that keeps every surface strictly descending.
        var arrivingChains = new Dictionary<int, List<int>>();
        for (int i = 0; i < chains.Count; i++)
            if (chains[i].End >= 0 && chainAt.ContainsKey(chains[i].End))
                (arrivingChains.TryGetValue(chains[i].End, out var list) ? list : arrivingChains[chains[i].End] = new List<int>()).Add(i);
        void Raise(int index, double endLevel)
        {
            var surface = chains[index].Surface;
            if (surface[^1] >= endLevel) return;
            surface[^1] = endLevel;
            for (int i = surface.Length - 2; i >= 0; i--)
                if (surface[i] <= surface[i + 1]) surface[i] = surface[i + 1] + 1e-4; else break;
            if (arrivingChains.TryGetValue(chains[index].Cells[0], out var upstream))
                foreach (int up in upstream) Raise(up, surface[0]);
        }
        for (int i = 0; i < chains.Count; i++)
            if (chains[i].End >= 0 && height[chains[i].End] > 0 && LakeCell[chains[i].End] >= 0)
                Raise(i, _lakes[LakeCell[chains[i].End]].Level - _seaLevel);

        // Pass 3: polylines and links.
        for (int id = 0; id < chains.Count; id++)
        {
            var (cells, surface, widths, depths, catchment, end, startLake) = chains[id];
            bool mouth = end < 0 || height[end] <= 0 || LakeCell[end] >= 0;
            // Tributaries funnel toward the receiving channel; mouths open into estuaries.
            double endWidth = mouth ? widths[^1] * style.RiverMouthWidening
                : end >= 0 && chainAt.TryGetValue(end, out int down) ? Math.Max(widths[^1], chains[down].Width[0] * .85) : widths[^1];
            var reach = BuildReach(id, cells, surface, widths, depths, catchment, n, seed, endWidth, mouth);
            reach.StartLake = startLake;
            reach.EndCell = end;
            if (startLake >= 0) _lakes[startLake].OutletReach = id;
            if (end < 0 || height[end] <= 0) reach.EndsInSea = true;
            else if (LakeCell[end] >= 0) { reach.EndLake = LakeCell[end]; _lakes[LakeCell[end]].InflowReaches.Add(id); }
            else if (reachAt.TryGetValue(end, out int downstream))
            {
                reach.DownstreamReach = downstream;
            }
            _reaches.Add(reach);
        }
        foreach (var reach in _reaches)
            if (reach.DownstreamReach >= 0) _reaches[reach.DownstreamReach].UpstreamReaches.Add(reach.Id);
    }

    private static double Freeboard(double depth) => .6 + .15 * depth;

    /// <summary>
    /// Recompute every reach's water surface from the eroded height raster sampled bicubically along its final
    /// (smoothed, meandering) course, instead of the coarse cell values. This keeps rivers in the actual valley
    /// floor that the player sees, so banks no longer have to cut trenches down to a too-low river. Processed in
    /// network order (sources first): strictly descending, confluences shared, lake outlets at the lake level,
    /// lake inflows arriving at or above it. Then adds deltas to large rivers on flat coasts.
    /// </summary>
    internal void RefineSurfaces(TerrainStyle style)
    {
        double Ground(double x, double z) => _raster.Sample(1, x, z); // HeightChannel (metres above sea)
        var pending = new int[_reaches.Count];
        foreach (var r in _reaches) pending[r.Id] = r.UpstreamReaches.Count;
        var ready = new Queue<int>(_reaches.Where(r => r.UpstreamReaches.Count == 0).Select(r => r.Id));
        while (ready.Count > 0)
        {
            var r = _reaches[ready.Dequeue()];
            var pts = r.Points; var surface = new double[pts.Length];
            double previous;
            if (r.StartLake >= 0) previous = _lakes[r.StartLake].Level - _seaLevel + .01;
            else if (r.UpstreamReaches.Count > 0) previous = r.UpstreamReaches.Min(u => _reaches[u].Surface[^1] - _seaLevel) + .01;
            else previous = double.PositiveInfinity;
            for (int k = 0; k < pts.Length; k++)
            {
                // Lowest ground across the channel at this point (centre and both edges).
                double tx = pts[Math.Min(k + 1, pts.Length - 1)].X - pts[Math.Max(k - 1, 0)].X, tz = pts[Math.Min(k + 1, pts.Length - 1)].Z - pts[Math.Max(k - 1, 0)].Z;
                double len = Math.Max(1e-9, Math.Sqrt(tx * tx + tz * tz)), half = r.Width[k] * .5 + 25; // channel plus a margin
                double ground = Math.Min(Ground(pts[k].X, pts[k].Z), Math.Min(Ground(pts[k].X - tz / len * half, pts[k].Z + tx / len * half),
                    Ground(pts[k].X + tz / len * half, pts[k].Z - tx / len * half)));
                double step = k == 0 ? .01 : style.RiverMinSlope * Math.Sqrt(Sq(pts[k].X - pts[k - 1].X) + Sq(pts[k].Z - pts[k - 1].Z));
                double v = Math.Min(previous - step, ground - Freeboard(r.Depth[k]));
                if (k == 0 && r.StartLake >= 0) v = previous - .01;
                if (k == pts.Length - 1 && r.EndsInSea) v = 0;
                v = Math.Max(v, 0);
                if (k > 0 && v >= surface[k - 1]) v = Math.Max(0, surface[k - 1] - 1e-4);
                surface[k] = v; previous = v;
            }
            for (int k = 0; k < surface.Length; k++) surface[k] += _seaLevel;
            r.Surface = surface;
            if (r.DownstreamReach >= 0 && --pending[r.DownstreamReach] == 0) ready.Enqueue(r.DownstreamReach);
        }
        // Confluences: all arriving reaches end exactly where the outgoing reach starts.
        foreach (var r in _reaches)
            if (r.DownstreamReach >= 0) r.Surface[^1] = Math.Min(r.Surface[^1], _reaches[r.DownstreamReach].Surface[0]);
        foreach (var r in _reaches)
            if (r.UpstreamReaches.Count > 0) r.Surface[0] = r.UpstreamReaches.Min(u => _reaches[u].Surface[^1]);
        // Lake inflows arrive at the lake surface (raise only, propagated upstream through confluences).
        void Raise(RiverReach r, double end)
        {
            if (r.Surface[^1] >= end) return;
            r.Surface[^1] = end;
            for (int k = r.Surface.Length - 2; k >= 0; k--)
                if (r.Surface[k] <= r.Surface[k + 1]) r.Surface[k] = r.Surface[k + 1] + 1e-4; else break;
            foreach (int u in r.UpstreamReaches) Raise(_reaches[u], r.Surface[0]);
        }
        foreach (var r in _reaches) if (r.EndLake >= 0) Raise(r, _lakes[r.EndLake].Level);
        AddDeltas(style);
        _index = new SegmentIndex(_reaches, _raster, 768);
    }

    /// <summary>
    /// Large rivers reaching the sea across flat ground split into two extra distributary channels fanning out to the
    /// coast (a simple delta). Branch surfaces descend from the main river's level at the split to the sea.
    /// </summary>
    private void AddDeltas(TerrainStyle style)
    {
        double Land(double x, double z) => _raster.Sample(0, x, z);
        int count = _reaches.Count;
        for (int id = 0; id < count; id++)
        {
            var main = _reaches[id];
            if (!main.EndsInSea || main.Distributary || main.Catchment[^1] < style.DeltaMinCatchmentKm2 || main.Points.Length < 4) continue;
            var pts = main.Points;
            var length = new double[pts.Length];
            for (int k = 1; k < pts.Length; k++) length[k] = length[k - 1] + Math.Sqrt(Sq(pts[k].X - pts[k - 1].X) + Sq(pts[k].Z - pts[k - 1].Z));
            double split = DetMath.Clamp(main.Width[^1] * 8, 500, 2400);
            int kb = pts.Length - 1;
            while (kb > 1 && length[^1] - length[kb] < split) kb--;
            if (main.Surface[kb] - _seaLevel > 6) continue; // only across a flat coastal plain
            double dx = pts[^1].X - pts[kb].X, dz = pts[^1].Z - pts[kb].Z, dl = Math.Sqrt(dx * dx + dz * dz);
            if (dl < 100) continue;
            dx /= dl; dz /= dl;
            ulong h = Hashing.Mix((ulong)id * 0x9E3779B97F4A7C15UL + 0xDE17AUL);
            foreach (double side in new[] { -1.0, 1.0 })
            {
                double spread = dl * (.35 + .3 * Hashing.Unit(Hashing.Mix(h + (ulong)(side + 2))));
                // End point out to sea, beside the main mouth.
                double ex = pts[^1].X - dz * spread * side + dx * dl * .1, ez = pts[^1].Z + dx * spread * side + dz * dl * .1;
                for (int i = 0; i < 8 && Land(ex, ez) > 0; i++) { ex += dx * _raster.Cell * .5; ez += dz * _raster.Cell * .5; }
                if (Land(ex, ez) > 0) continue;
                // Quadratic Bezier with a sideways bow.
                double cx = pts[kb].X + dx * dl * .55 - dz * spread * .35 * side, cz = pts[kb].Z + dz * dl * .55 + dx * spread * .35 * side;
                int segments = Math.Max(4, (int)(dl / 60));
                var points = new Point2[segments + 1]; var surface = new double[segments + 1];
                var width = new double[segments + 1]; var depth = new double[segments + 1]; var catchment = new double[segments + 1];
                double s0 = main.Surface[kb];
                for (int i = 0; i <= segments; i++)
                {
                    double t = i / (double)segments, u = 1 - t;
                    points[i] = new(u * u * pts[kb].X + 2 * u * t * cx + t * t * ex, u * u * pts[kb].Z + 2 * u * t * cz + t * t * ez);
                    surface[i] = Math.Max(_seaLevel, s0 - (s0 - _seaLevel) * t - 1e-4 * i);
                    width[i] = main.Width[kb] * (.35 + .35 * t);
                    depth[i] = main.Depth[kb] * .7;
                    catchment[i] = main.Catchment[kb] * .25;
                }
                surface[^1] = _seaLevel;
                _reaches.Add(new RiverReach
                {
                    Id = _reaches.Count, Points = points, Surface = surface, Width = width, Depth = depth, Catchment = catchment,
                    EndsInSea = true, Distributary = true, ParentReach = id, ParentPoint = kb,
                });
            }
        }
    }

    /// <summary>
    /// Turn D8 cell centres into a natural channel: Catmull–Rom smoothing at ~1/8 cell spacing, then meanders
    /// (perpendicular noise displacement scaled by width, zero at both ends so junctions stay joined).
    /// </summary>
    private RiverReach BuildReach(int id, List<int> cells, double[] surface, double[] width, double[] depth, double[] catchment, int n, ulong seed,
        double endWidth, bool mouth)
    {
        int m = cells.Count;
        var cx = new double[m]; var cz = new double[m];
        for (int i = 0; i < m; i++) { cx[i] = _raster.CellX(cells[i] % n); cz[i] = _raster.CellZ(cells[i] / n); }
        // Round off the 45°/90° steps of D8 routing before the spline (endpoints fixed so junctions stay joined).
        for (int pass = 0; pass < 4; pass++)
        {
            double px = cx[0], pz = cz[0];
            for (int i = 1; i < m - 1; i++)
            {
                double nx = .25 * px + .5 * cx[i] + .25 * cx[i + 1], nz = .25 * pz + .5 * cz[i] + .25 * cz[i + 1];
                px = cx[i]; pz = cz[i]; cx[i] = nx; cz[i] = nz;
            }
        }
        // Adaptive subdivision: points every ~2.5 channel widths (25-120 m); meander wavelengths are >= 200 m.
        var subs = new int[m];
        int total = 1;
        for (int i = 0; i < m - 1; i++)
        {
            double seg = Math.Sqrt(Sq(cx[i + 1] - cx[i]) + Sq(cz[i + 1] - cz[i]));
            double spacing = DetMath.Clamp((width[i] + width[i + 1]) * 1.25, 25, 120);
            subs[i] = Math.Clamp((int)Math.Ceiling(seg / spacing), 2, 96);
            total += subs[i];
        }
        var points = new Point2[total]; var s = new double[total]; var w = new double[total]; var d = new double[total]; var a = new double[total];
        int kk = 0;
        for (int i = 0; i < m - 1; i++)
            for (int j = 0; j < subs[i]; j++, kk++)
            {
                double t = j / (double)subs[i];
                int i0 = Math.Max(0, i - 1), i3 = Math.Min(m - 1, i + 2);
                points[kk] = new(CatmullRom(cx[i0], cx[i], cx[i + 1], cx[i3], t), CatmullRom(cz[i0], cz[i], cz[i + 1], cz[i3], t));
                s[kk] = surface[i] + (surface[i + 1] - surface[i]) * t + _seaLevel;
                w[kk] = width[i] + (width[i + 1] - width[i]) * t;
                d[kk] = depth[i] + (depth[i + 1] - depth[i]) * t;
                a[kk] = catchment[i] + (catchment[i + 1] - catchment[i]) * t;
            }
        points[^1] = new(cx[m - 1], cz[m - 1]); s[^1] = surface[m - 1] + _seaLevel; w[^1] = width[m - 1]; d[^1] = depth[m - 1]; a[^1] = catchment[m - 1];

        // Meanders: arclength-parameterised noise. Flat reaches meander strongly; steep ones stay straighter.
        var length = new double[total];
        for (int k = 1; k < total; k++) length[k] = length[k - 1] + Math.Sqrt(Sq(points[k].X - points[k - 1].X) + Sq(points[k].Z - points[k - 1].Z));
        double reachLength = length[^1];
        double slope = reachLength > 0 ? Math.Max(0, surface[0] - surface[m - 1]) / reachLength : 0;
        double flatness = 1 - DetMath.Smooth((slope - .0008) / .012);
        var displaced = (Point2[])points.Clone();
        ulong noiseSeed = seed ^ Hashing.Mix((ulong)cells[0] + 0x51ED27UL);
        for (int k = 1; k < total - 1; k++)
        {
            double tx = points[k + 1].X - points[k - 1].X, tz = points[k + 1].Z - points[k - 1].Z;
            double len = Math.Sqrt(tx * tx + tz * tz);
            if (len <= 0) continue;
            // Smooth bends: wavelength at least 4.5x the amplitude (natural meander proportions, no kinks), mostly
            // one octave with a little variation.
            double amplitude = Math.Min(_raster.Cell * .3, w[k] * 4 + 30) * (.3 + .7 * flatness);
            double wavelength = Math.Max(Math.Max(200, w[k] * 14), amplitude * 4.5);
            double envelope = DetMath.Smooth(length[k] / (wavelength * .75)) * DetMath.Smooth((reachLength - length[k]) / (wavelength * .75));
            double offset = Noise.Fbm(length[k], 0, wavelength, 2, 0, noiseSeed, .3) * 2.4 * amplitude * envelope;
            // Slow wander across the valley floor on a kilometre scale, so long rivers never run ruler-straight.
            double wander = _raster.Cell * .26 * (.15 + .85 * flatness);
            double wanderLength = Math.Max(1100, wander * 5);
            double wanderEnvelope = DetMath.Smooth(length[k] / (wanderLength * .6)) * DetMath.Smooth((reachLength - length[k]) / (wanderLength * .6));
            offset += Noise.Gradient(length[k] / wanderLength + 3.7, 11.1, noiseSeed + 3) * 2.4 * wander * wanderEnvelope;
            displaced[k] = new(points[k].X - tz / len * offset, points[k].Z + tx / len * offset);
        }
        // Width varies along the river (pools and narrows), returning to the network's widths at both ends;
        // the last stretch widens toward the receiving channel or into an estuary.
        double tail = mouth ? DetMath.Clamp(endWidth * 5, 250, 1200) : Math.Max(endWidth * 3, 40);
        for (int k = 0; k < total; k++)
        {
            double ends = DetMath.Smooth(length[k] / 150) * DetMath.Smooth((reachLength - length[k]) / 150);
            double vary = 1 + .3 * Noise.Fbm(length[k], 17.3, Math.Max(300, w[k] * 25), 2, 0, noiseSeed + 7, .5) * 2.4 * ends;
            double funnel = DetMath.Smooth(1 - (reachLength - length[k]) / tail);
            double baseWidth = w[k] * Math.Max(.6, vary);
            w[k] = baseWidth + (Math.Max(endWidth, baseWidth) - baseWidth) * funnel * funnel;
            if (mouth) d[k] *= 1 + .25 * funnel;
        }
        return new RiverReach { Id = id, Points = displaced, Surface = s, Width = w, Depth = d, Catchment = a };
    }

    private static double CatmullRom(double p0, double p1, double p2, double p3, double t) =>
        .5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
    private static double Sq(double v) => v * v;

    // ------------------------------------------------------------------ queries

    /// <summary>Lake at the raster cell containing (x, z), or -1.</summary>
    public int LakeAt(double x, double z)
    {
        int i = (int)Math.Floor((x - _raster.MinX) / _raster.Cell), j = (int)Math.Floor((z - _raster.MinZ) / _raster.Cell);
        if (i < 0 || j < 0 || i >= _raster.Size || j >= _raster.Size) return -1;
        return LakeCell[j * _raster.Size + i];
    }

    /// <summary>Nearest lake among the 3×3 cells around (x, z) — lets shorelines extend into neighbouring cells.</summary>
    public int LakeNear(double x, double z)
    {
        int i = (int)Math.Floor((x - _raster.MinX) / _raster.Cell), j = (int)Math.Floor((z - _raster.MinZ) / _raster.Cell);
        int best = -1;
        for (int b = -1; b <= 1 && best < 0; b++)
            for (int a = -1; a <= 1; a++)
            {
                int ii = i + a, jj = j + b;
                if (ii < 0 || jj < 0 || ii >= _raster.Size || jj >= _raster.Size) continue;
                int id = LakeCell[jj * _raster.Size + ii];
                if (id >= 0 && (a == 0 && b == 0 || best < 0)) { best = id; if (a == 0 && b == 0) break; }
            }
        return best;
    }

    /// <summary>Nearest river within <paramref name="radius"/> metres (influence of banks/floodplain), if any.</summary>
    public bool NearestRiver(double x, double z, double radius, out RiverHit hit) => _index.Nearest(x, z, radius, out hit, out _);

    /// <summary>
    /// Every river segment whose influence covers (x, z), up to <c>hits.Length</c>, nearest channel edge first.
    /// Each segment's contribution is continuous and fades to nothing at its influence edge, so combining all of
    /// them (deepest carve, highest bank) has no seams — even across the midline of a tight bend, where two parts
    /// of one river at different water levels are equally near.
    /// </summary>
    public int NearbyRivers(double x, double z, Span<RiverHit> hits) => _index.All(x, z, hits);

    /// <summary>Largest distance at which a river can affect the terrain (channel + banks + floodplain).</summary>
    public static double Influence(double width) => width * .5 + Bank(width) + MaxFloodplain(width);
    public static double Bank(double width) => Math.Max(8, width * .8);

    /// <summary>Valley-side blend width: slope-limited (about 1 in 4) where the ground stands high above the banks.</summary>
    public static double Floodplain(double width, double rise) => DetMath.Clamp(Math.Max(Math.Max(30, width * 2.5), rise * 4), 30, MaxFloodplain(width));
    public static double MaxFloodplain(double width) => Math.Max(260, width * 3);

    // ------------------------------------------------------------------ spatial index

    /// <summary>Uniform bucket grid over river segments (CSR layout); each segment is inserted into every bucket its
    /// influence capsule touches, so a query reads exactly one bucket.</summary>
    private sealed class SegmentIndex
    {
        private readonly double _minX, _minZ, _bucket;
        private readonly int _side;
        private readonly int[] _start, _items;
        private readonly List<RiverReach> _reaches;
        private readonly (int Reach, int Index)[] _segments;
        public long Entries => _items.Length;

        public SegmentIndex(List<RiverReach> reaches, MacroRaster raster, double bucket)
        {
            _reaches = reaches; _bucket = bucket; _minX = raster.MinX; _minZ = raster.MinZ;
            _side = (int)Math.Ceiling(raster.Size * raster.Cell / bucket) + 1;
            var segments = new List<(int, int)>();
            var cellsOf = new List<(int Bucket, int Segment)>();
            for (int r = 0; r < reaches.Count; r++)
            {
                var reach = reaches[r];
                for (int k = 0; k + 1 < reach.Points.Length; k++)
                {
                    int segment = segments.Count;
                    segments.Add((r, k));
                    double pad = Influence(Math.Max(reach.Width[k], reach.Width[k + 1]));
                    var a = reach.Points[k]; var b = reach.Points[k + 1];
                    int x0 = BucketOf(Math.Min(a.X, b.X) - pad - _minX), x1 = BucketOf(Math.Max(a.X, b.X) + pad - _minX);
                    int z0 = BucketOf(Math.Min(a.Z, b.Z) - pad - _minZ), z1 = BucketOf(Math.Max(a.Z, b.Z) + pad - _minZ);
                    for (int z = z0; z <= z1; z++) for (int x = x0; x <= x1; x++) cellsOf.Add((z * _side + x, segment));
                }
            }
            _segments = segments.ToArray();
            _start = new int[_side * _side + 1];
            foreach (var (bucketIndex, _) in cellsOf) _start[bucketIndex + 1]++;
            for (int i = 0; i < _side * _side; i++) _start[i + 1] += _start[i];
            _items = new int[cellsOf.Count];
            var fill = (int[])_start.Clone();
            foreach (var (bucketIndex, segment) in cellsOf) _items[fill[bucketIndex]++] = segment;
        }

        private int BucketOf(double offset) => Math.Clamp((int)Math.Floor(offset / _bucket), 0, _side - 1);

        public int All(double x, double z, Span<RiverHit> hits)
        {
            int bx = (int)Math.Floor((x - _minX) / _bucket), bz = (int)Math.Floor((z - _minZ) / _bucket);
            if (bx < 0 || bz < 0 || bx >= _side || bz >= _side) return 0;
            int b = bz * _side + bx, count = 0;
            Span<double> scores = stackalloc double[hits.Length];
            for (int i = _start[b]; i < _start[b + 1]; i++)
            {
                var (r, k) = _segments[_items[i]];
                var reach = _reaches[r];
                var p = reach.Points[k]; var q = reach.Points[k + 1];
                double dx = q.X - p.X, dz = q.Z - p.Z, l2 = dx * dx + dz * dz;
                double t = l2 > 0 ? DetMath.Clamp(((x - p.X) * dx + (z - p.Z) * dz) / l2, 0, 1) : 0;
                double ex = x - p.X - dx * t, ez = z - p.Z - dz * t;
                double width = reach.Width[k] + (reach.Width[k + 1] - reach.Width[k]) * t;
                double influence = Influence(width), d2 = ex * ex + ez * ez;
                if (d2 > influence * influence) continue;
                double distance = Math.Sqrt(d2);
                double score = distance - width * .5;
                int slot;
                if (count < hits.Length) slot = count++;
                else
                {
                    // Buffer full: replace the farthest if this one is nearer.
                    int worst = 0;
                    for (int j = 1; j < count; j++) if (scores[j] > scores[worst]) worst = j;
                    if (score >= scores[worst]) continue;
                    slot = worst;
                }
                scores[slot] = score;
                hits[slot] = new RiverHit(r, distance, reach.Surface[k] + (reach.Surface[k + 1] - reach.Surface[k]) * t, width,
                    reach.Depth[k] + (reach.Depth[k + 1] - reach.Depth[k]) * t, reach.Catchment[k] + (reach.Catchment[k + 1] - reach.Catchment[k]) * t);
            }
            // Deterministic order: nearest channel edge first (ties by reach id).
            for (int i = 1; i < count; i++)
                for (int j = i; j > 0 && (scores[j] < scores[j - 1] || scores[j] == scores[j - 1] && hits[j].Reach < hits[j - 1].Reach); j--)
                {
                    (scores[j], scores[j - 1]) = (scores[j - 1], scores[j]);
                    (hits[j], hits[j - 1]) = (hits[j - 1], hits[j]);
                }
            return count;
        }

        public bool Nearest(double x, double z, double radius, out RiverHit hit, out RiverHit second)
        {
            hit = default; second = new RiverHit(-1, double.PositiveInfinity, 0, 0, 0, 0);
            int bx = (int)Math.Floor((x - _minX) / _bucket), bz = (int)Math.Floor((z - _minZ) / _bucket);
            if (bx < 0 || bz < 0 || bx >= _side || bz >= _side) return false;
            int b = bz * _side + bx;
            double best = double.PositiveInfinity, secondBest = double.PositiveInfinity;
            bool found = false;
            for (int i = _start[b]; i < _start[b + 1]; i++)
            {
                var (r, k) = _segments[_items[i]];
                var reach = _reaches[r];
                var p = reach.Points[k]; var q = reach.Points[k + 1];
                double dx = q.X - p.X, dz = q.Z - p.Z, l2 = dx * dx + dz * dz;
                double t = l2 > 0 ? DetMath.Clamp(((x - p.X) * dx + (z - p.Z) * dz) / l2, 0, 1) : 0;
                double ex = x - p.X - dx * t, ez = z - p.Z - dz * t;
                double distance = Math.Sqrt(ex * ex + ez * ez);
                double width = reach.Width[k] + (reach.Width[k + 1] - reach.Width[k]) * t;
                // Rank by distance from the channel edge, so a wide river's water wins over a creek's centreline.
                double score = distance - width * .5;
                if (distance > Influence(width) || distance > radius && radius > 0) continue;
                var candidate = new RiverHit(r, distance, reach.Surface[k] + (reach.Surface[k + 1] - reach.Surface[k]) * t, width,
                    reach.Depth[k] + (reach.Depth[k + 1] - reach.Depth[k]) * t, reach.Catchment[k] + (reach.Catchment[k + 1] - reach.Catchment[k]) * t);
                if (score < best)
                {
                    if (found && hit.Reach != r) { second = hit; secondBest = best; }
                    best = score; hit = candidate; found = true;
                }
                else if (r != hit.Reach && score < secondBest) { second = candidate; secondBest = score; }
            }
            if (found && second.Reach == hit.Reach) second = new RiverHit(-1, double.PositiveInfinity, 0, 0, 0, 0);
            return found;
        }
    }
}
