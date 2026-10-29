using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

/// <summary>
/// Glacial troughs with hanging valleys (landform no. 5, terrain revision 24, D242; docs/LANDFORMS.md). A hanging
/// valley needs a main valley far deeper than its side valleys, and the plan's rivers never make one (at the
/// junctions of small side rivers the main valley lies 5–8 m lower across, 77 m at most), so the main valley is cut
/// down the way a glacier cut it: along a steep stretch of mountain river (3–8 km, dropping 40 m or more) the river is
/// lowered onto a flat floor that falls gently (15 % of its old grade), between steep walls, under an amphitheatre at
/// its head where the river falls in. Side rivers keep their level to the lip of the wall and fall onto the floor —
/// the hanging valleys. Placed once in the plan after the rivers, canyons and cirques; the trough is ground under the
/// walking-scale relief (<see cref="Carve"/>), the rivers' water is lowered in the plan, every lowering ends on the
/// trough's floor (rivers stay strictly descending, lakes are untouched). Revision 25 (D243): no fall from a river that
/// rises less than 800 m above it.
/// </summary>
public sealed class Troughs
{
    private sealed class Trough
    {
        public required double[] X, Z, Along, Floor;
        public required double HalfFloor, Slope, Reach;
        public required ulong Seed;
        public double MinX, MinZ, MaxX, MaxZ;
    }

    /// <summary>A fall where a river drops into a trough: a side river from its hanging valley, or the main river at
    /// the trough's head.</summary>
    public readonly record struct Fall(double X, double Z, double Height, bool Side);

    private const double GridCell = 2000, Spacing = 50, FloorAbove = 1.5;
    private readonly List<Trough> _troughs = new();
    private readonly List<Fall> _falls = new();
    private readonly double _minX, _minZ;
    private readonly int _revision;
    private readonly int _gridSize;
    private readonly int[] _gridStart, _gridItems;

    /// <summary>Number of troughs in this world.</summary>
    public int Count => _troughs.Count;

    /// <summary>Every fall into a trough (tests, captures), highest first.</summary>
    public IReadOnlyList<Fall> Falls => _falls;

    /// <summary>The middle of each trough's floor, its level (metres above the sea), its half width and the direction
    /// down the trough there (tests, captures).</summary>
    public IEnumerable<(double X, double Z, double Floor, double HalfFloor, double Dx, double Dz)> Probes()
    {
        foreach (var c in _troughs)
        {
            int m = c.X.Length / 2;
            double dx = c.X[m + 1] - c.X[m - 1], dz = c.Z[m + 1] - c.Z[m - 1], l = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
            yield return (c.X[m], c.Z[m], c.Floor[m], c.HalfFloor, dx / l, dz / l);
        }
    }

    public Troughs(WorldPlan plan, LandformSetting setting)
    {
        var macro = plan.Macro; var hy = plan.Hydrology; var p = plan.Profile;
        double sea = p.SeaLevel;
        _revision = plan.Style.Revision;
        var rng = new RandomStream(LandformCatalog.Seed(plan.Manifest, LandformCatalog.Troughs));
        var mountain = macro.Channel(WorldPlan.MountainChannel);
        int mountainCells = 0;
        foreach (float v in mountain) if (v > .25f) mountainCells++;
        double km2 = mountainCells * macro.Cell * macro.Cell / 1e6;
        int want = Math.Clamp((int)Math.Round(setting.Amount * km2 / (25 * 25)), 0, 60);
        var candidates = new List<int>();
        foreach (var r in hy.Reaches)
        {
            if (r.Distributary || r.Points.Length < 8 || r.Catchment[r.Catchment.Length / 2] < 20) continue;
            var mid = r.Points[r.Points.Length / 2];
            if (macro.Sample(WorldPlan.MountainChannel, mid.X, mid.Z) > .1) candidates.Add(r.Id);
        }
        var reaches = hy.Reaches;
        var bounds = new (double MinX, double MinZ, double MaxX, double MaxZ)[reaches.Count];
        for (int i = 0; i < reaches.Count; i++)
        {
            var pts = reaches[i].Points;
            double x0 = pts[0].X, x1 = x0, z0 = pts[0].Z, z1 = z0;
            foreach (var q in pts) { x0 = Math.Min(x0, q.X); x1 = Math.Max(x1, q.X); z0 = Math.Min(z0, q.Z); z1 = Math.Max(z1, q.Z); }
            bounds[i] = (x0, z0, x1, z1);
        }
        var cirques = new List<(double X, double Z, double Radius)>();
        if (plan.Cirques != null) foreach (var (qx, qz, _, qr) in plan.Cirques.Probes()) cirques.Add((qx, qz, qr));
        var canyons = new List<(double X, double Z)>();
        if (plan.Canyons != null) foreach (var (cx, cz, _) in plan.Canyons.Probes()) canyons.Add((cx, cz));

        for (int attempt = 0; attempt < want * 30 && _troughs.Count < want && candidates.Count > 0; attempt++)
        {
            var start = reaches[candidates[rng.Int(candidates.Count)]];
            int k = rng.Int(start.Points.Length - 1);
            double length = rng.Between(3000, 8000) * setting.Size, halfFloor = rng.Between(150, 350) * setting.Size, slope = rng.Between(1.3, 2.0);
            ulong seed = rng.Next();
            // The stretch, following the river downstream across junctions, resampled every 50 m.
            var xs = new List<double>(); var zs = new List<double>(); var ss = new List<double>();
            var chain = new HashSet<int>();
            var r = start; double run = 0, next = 0; bool ok = true;
            while (true)
            {
                chain.Add(r.Id);
                for (; k < r.Points.Length; k++)
                {
                    if (xs.Count > 0) run += Math.Sqrt(Sq(r.Points[k].X - r.Points[k - 1].X) + Sq(r.Points[k].Z - r.Points[k - 1].Z));
                    if (run >= next) { xs.Add(r.Points[k].X); zs.Add(r.Points[k].Z); ss.Add(r.Surface[k] - sea); next = run + Spacing; }
                    if (run >= length) break;
                }
                if (run >= length) break;
                if (r.EndLake >= 0 || r.EndsInSea || r.DownstreamReach < 0) { ok = false; break; }
                r = reaches[r.DownstreamReach]; k = 1;
            }
            if (!ok || xs.Count < 40) continue;
            double drop = ss[0] - ss[^1];
            if (drop < 40 || drop / length > .08) continue;
            // In the mountains, away from lakes, other troughs, canyons and cirques.
            int n = xs.Count;
            double reach = halfFloor * 1.25 + 40;
            for (int i = 0; i < n && ok; i += 4)
            {
                if (hy.LakeNear(xs[i], zs[i]) >= 0) ok = false;
                foreach (var other in _troughs)
                    if (Math.Abs(other.X[other.X.Length / 2] - xs[i]) + Math.Abs(other.Z[other.Z.Length / 2] - zs[i]) < 15000) ok = false;
                foreach (var (cx, cz) in canyons) if (Math.Abs(cx - xs[i]) + Math.Abs(cz - zs[i]) < 15000) ok = false;
                foreach (var (qx, qz, qr) in cirques) if (Math.Sqrt(Sq(qx - xs[i]) + Sq(qz - zs[i])) < qr * 1.3 + reach + 400) ok = false;
            }
            if (!ok || macro.Sample(WorldPlan.MountainChannel, xs[n / 2], zs[n / 2]) < .15) continue;
            // The trough follows the river's course averaged over ±400 m: a glacier cut its valley straighter than the river
            // that winds over its floor now (following the bends drew walls with a ripple at every meander); the floor is
            // wide enough to hold the bends.
            var mx = new double[n]; var mz = new double[n];
            double bends = 0;
            for (int i = 0; i < n; i++)
            {
                int w = Math.Min(8, Math.Min(i, n - 1 - i));
                for (int j = i - w; j <= i + w; j++) { mx[i] += xs[j]; mz[i] += zs[j]; }
                mx[i] /= 2 * w + 1; mz[i] /= 2 * w + 1;
                bends = Math.Max(bends, Math.Sqrt(Sq(mx[i] - xs[i]) + Sq(mz[i] - zs[i])));
            }
            halfFloor = Math.Max(halfFloor, bends + 80);
            var along = new double[n];
            for (int i = 1; i < n; i++) along[i] = along[i - 1] + Math.Sqrt(Sq(mx[i] - mx[i - 1]) + Sq(mz[i] - mz[i - 1]));
            // The floor: the water falls in at the head (85 % of the stretch's drop, 160 m at most × size) and runs on at an
            // even grade to its old level at the end (a share of the old grade carried the old river's falls across the
            // floor as steps); never above the old river.
            double end = ss[^1], deepest = Math.Min(.85 * drop, 160 * setting.Size);
            var floor = new double[n];
            for (int i = 0; i < n; i++) floor[i] = Math.Min(ss[i], ss[0] - deepest + (end - ss[0] + deepest) * along[i] / along[^1]);
            // Walls worth the name: the land 300 m beyond the floor's edge stands 50 m or more over the floor on both sides,
            // on average along the trough (a trough in low hills drew wandering steps a few metres high).
            double left = 0, right = 0;
            for (int q = 1; q <= 3; q++)
            {
                int i = q * n / 4, i0 = Math.Max(0, i - 2), i1 = Math.Min(n - 1, i + 2);
                double tx = mx[i1] - mx[i0], tz = mz[i1] - mz[i0], tl = Math.Max(1e-6, Math.Sqrt(tx * tx + tz * tz)), d = halfFloor + 300;
                left += macro.Sample(WorldPlan.HeightChannel, mx[i] - tz / tl * d, mz[i] + tx / tl * d) - floor[i];
                right += macro.Sample(WorldPlan.HeightChannel, mx[i] + tz / tl * d, mz[i] - tx / tl * d) - floor[i];
            }
            if (left < 3 * 50 || right < 3 * 50) continue;
            var c = new Trough
            {
                X = mx, Z = mz, Along = along, Floor = floor, HalfFloor = halfFloor, Slope = slope, Seed = seed,
                Reach = halfFloor * 1.25 + 40 + 2 * (deepest + 150) / slope,
            };
            c.MinX = c.MaxX = c.X[0]; c.MinZ = c.MaxZ = c.Z[0];
            for (int i = 1; i < n; i++)
            {
                c.MinX = Math.Min(c.MinX, c.X[i]); c.MaxX = Math.Max(c.MaxX, c.X[i]);
                c.MinZ = Math.Min(c.MinZ, c.Z[i]); c.MaxZ = Math.Max(c.MaxZ, c.Z[i]);
            }
            if (Lower(hy, c, chain, bounds, sea, setting)) _troughs.Add(c);
        }
        _falls.Sort((a, b) => b.Height.CompareTo(a.Height));
        // Troughs near each 2 km cell.
        _minX = macro.MinX; _minZ = macro.MinZ;
        _gridSize = (int)Math.Ceiling(macro.Size * macro.Cell / GridCell);
        var cells = new List<int>[_gridSize * _gridSize];
        for (int i = 0; i < _troughs.Count; i++)
        {
            var c = _troughs[i];
            for (int j = Cell(c.MinZ - c.Reach, _minZ); j <= Cell(c.MaxZ + c.Reach, _minZ); j++)
                for (int a = Cell(c.MinX - c.Reach, _minX); a <= Cell(c.MaxX + c.Reach, _minX); a++)
                    (cells[j * _gridSize + a] ??= new List<int>()).Add(i);
        }
        _gridStart = new int[cells.Length + 1];
        var items = new List<int>();
        for (int i = 0; i < cells.Length; i++) { _gridStart[i] = items.Count; if (cells[i] != null) items.AddRange(cells[i]); }
        _gridStart[^1] = items.Count;
        _gridItems = items.ToArray();
    }

    /// <summary>
    /// Lowers every river the trough takes in (or leaves the plan unchanged and returns false): from the first point
    /// where the trough's ground lies below the water (the lip) each river runs at the floor's level, the junctions
    /// meet, the water still strictly descends. Refused when a river would leave the trough lowered (more than 3 m
    /// below its old level where the trough does not reach) or a lake would be touched. Falls over 8 m get a plunge
    /// pool and a narrow notch down the wall (the river's gorge).
    /// </summary>
    private bool Lower(Hydrology hy, Trough c, HashSet<int> chain, (double MinX, double MinZ, double MaxX, double MaxZ)[] bounds, double sea, LandformSetting setting)
    {
        var reaches = hy.Reaches;
        var lowered = new Dictionary<int, double[]>();
        double[] Get(int id)
        {
            if (!lowered.TryGetValue(id, out var s)) lowered[id] = s = (double[])reaches[id].Surface.Clone();
            return s;
        }
        bool Inside(double x, double z, double water) => Surface(c, x, z, out _, out _, out _) < water - sea + 2;
        for (int id = 0; id < reaches.Count; id++)
        {
            var b = bounds[id];
            if (reaches[id].Distributary || b.MaxX < c.MinX - c.Reach || b.MinX > c.MaxX + c.Reach || b.MaxZ < c.MinZ - c.Reach || b.MinZ > c.MaxZ + c.Reach) continue;
            var r = reaches[id]; double[]? s = null; bool inside = false;
            for (int k = 0; k < r.Points.Length; k++)
            {
                if (!inside) inside = Inside(r.Points[k].X, r.Points[k].Z, r.Surface[k]);
                if (!inside) continue;
                Surface(c, r.Points[k].X, r.Points[k].Z, out double floor, out _, out _);
                if (floor + sea < r.Surface[k]) (s ??= Get(id))[k] = floor + sea;
            }
        }
        if (lowered.Count == 0) return false;
        // Junctions meet and the water strictly descends (lowering a reach's start carries on downstream as far as it must).
        for (int pass = 0; pass < 30; pass++)
        {
            bool changed = false;
            foreach (int id in new List<int>(lowered.Keys))
            {
                var r = reaches[id]; var s = lowered[id];
                if (r.DownstreamReach >= 0)
                {
                    var down = Get(r.DownstreamReach);
                    if (s[^1] < down[0]) { down[0] = s[^1]; changed = true; }
                    else if (s[^1] > down[0]) { s[^1] = down[0]; changed = true; }
                }
                for (int k = 1; k < s.Length; k++) if (s[k] >= s[k - 1]) { s[k] = s[k - 1] - 1e-4; changed = true; }
            }
            if (!changed) break;
            if (pass == 29) return false;
        }
        foreach (var (id, s) in lowered)
        {
            var r = reaches[id];
            if (r.StartLake >= 0 || r.EndLake >= 0) return false;
            for (int k = 0; k < s.Length; k++)
                if (r.Surface[k] - s[k] > 3 && !Inside(r.Points[k].X, r.Points[k].Z, r.Surface[k])) return false;
            // Revision 25 (D243): a river that rises here (a source) runs 800 m or more before it falls in — a source just
            // above the lip showed a river starting from nothing at the edge of the wall (Tom's dump 17:28:48, build 120).
            if (_revision >= 25 && r.UpstreamReaches.Count == 0)
                for (int k = 0; k < s.Length; k++)
                    if (r.Surface[k] - s[k] > .5) { if (r.Along[k] < 800) return false; break; }
            // (and no water lowered to within a metre of the sea: lowering carried on downstream pushed a river at the sea's
            // level below it)
            if (_revision >= 25)
                for (int k = 0; k < s.Length; k++)
                    if (s[k] < r.Surface[k] && s[k] < sea + 1) return false;
        }
        // Accepted: the plan's water takes the new levels; falls into the trough.
        var falls = new List<(int Reach, int Point, double Height, double To)>();
        foreach (var (id, s) in lowered)
        {
            var r = reaches[id];
            int lip = -1;
            for (int k = 1; k < s.Length; k++) if (r.Surface[k] - s[k] > 4) { lip = k - 1; break; }
            if (lip >= 0 && s[lip] - s[lip + 1] >= 8)
            {
                // The notch down the wall: from the lip to where the river reaches the floor.
                int foot = lip + 1;
                while (foot < s.Length - 1)
                {
                    Surface(c, r.Points[foot].X, r.Points[foot].Z, out _, out double wall, out _);
                    if (wall <= 0) break;
                    foot++;
                }
                falls.Add((id, lip, s[lip] - s[lip + 1], r.Along[foot] + 30));
            }
            r.Surface = s;
            hy.DropFalls(id, k => k + 1 < s.Length && s[k] - s[k + 1] < 4); // (a waterfall the trough's floor has taken in)
        }
        foreach (var (id, lip, height, to) in falls)
        {
            var r = reaches[id];
            for (int j = lip + 1; j < r.Points.Length && r.Along[j] - r.Along[lip] < 40; j++)
            {
                r.Depth[j] = (float)(r.Depth[j] + Math.Min(10, .25 * height) * setting.Size);
                r.Width[j] = (float)(r.Width[j] * (1 + .5 * setting.Size));
            }
            hy.AddGorge(id, r.Along[lip], Math.Min(to, r.Length));
            _falls.Add(new Fall(r.Points[lip].X, r.Points[lip].Z, height, !chain.Contains(id)));
        }
        return true;
    }

    private int Cell(double v, double min) => Math.Clamp((int)Math.Floor((v - min) / GridCell), 0, _gridSize - 1);

    /// <summary>
    /// The trough's ground here (metres above the sea, like the plan's heights): the floor 1.5 m over the water's level
    /// within half the floor's width (150–350 m × size, wider where the river's bends need it, ±20 %, narrowing to
    /// nothing over the last 1.5 km), then a wall steepening over 120 m to slope 1.3–2 (a U), its line wandering ±25 m; rising without end (the ground beside wins above
    /// the wall's top). <paramref name="floor"/>: the water's level here (metres above the sea); <paramref name="wall"/>:
    /// metres out from the foot of the wall (negative on the floor); <paramref name="along"/>: metres from the head.
    /// </summary>
    private static double Surface(Trough c, double x, double z, out double floor, out double wall, out double along)
    {
        Nearest(c, x, z, out double dist, out along);
        int i = Math.Clamp(Array.BinarySearch(c.Along, along) is int f && f >= 0 ? f : ~f - 1, 0, c.Along.Length - 2);
        double u = DetMath.Clamp((along - c.Along[i]) / Math.Max(1e-6, c.Along[i + 1] - c.Along[i]), 0, 1);
        floor = c.Floor[i] + (c.Floor[i + 1] - c.Floor[i]) * u;
        double toEnd = Math.Sqrt(Sq(x - c.X[^1]) + Sq(z - c.Z[^1]));
        double half = c.HalfFloor * (1 + .2 * Noise.Fbm(x, z, 1500, 2, 0, c.Seed)) * DetMath.Smooth(toEnd / 1500);
        wall = dist - half + 25 * Noise.Fbm(x, z, 180, 2, 0, c.Seed + 1);
        const double Foot = 120; // (a U: the wall steepens upward from the floor)
        double rise = wall <= 0 ? 0 : wall < Foot ? c.Slope * wall * wall / (2 * Foot) : c.Slope * (wall - Foot * .5);
        return floor + FloorAbove + rise;
    }

    /// <summary>
    /// The ground taken down into the troughs here (metres above the sea in and out), blended over 8 m on the floor and up
    /// to 30 m up the wall, so the wall's top is a rounded shoulder. <paramref name="flat"/>: how much of the walking-scale relief to take away (1 on the floor,
    /// fading out up the wall's foot); <paramref name="rock"/>: how bare the wall is.
    /// </summary>
    public double Carve(double x, double z, double ground, out double flat, out double rock)
    {
        flat = 0; rock = 0;
        if (_troughs.Count == 0) return ground;
        int cell = Cell(z, _minZ) * _gridSize + Cell(x, _minX);
        double carved = ground;
        for (int n = _gridStart[cell]; n < _gridStart[cell + 1]; n++)
        {
            var c = _troughs[_gridItems[n]];
            if (x < c.MinX - c.Reach || x > c.MaxX + c.Reach || z < c.MinZ - c.Reach || z > c.MaxZ + c.Reach) continue;
            double t = Surface(c, x, z, out _, out double wall, out _);
            double k = 8 + 22 * DetMath.Smooth(wall / 150); // (a rounded shoulder at the wall's top; a tight blend on the floor)
            if (t >= carved + k) continue;
            double before = carved;
            carved = SmoothMin(carved, t, k);
            double cut = DetMath.Smooth((before - carved) / 15);
            flat = Math.Max(flat, cut * (1 - DetMath.Smooth((wall + 20) / 50)));
            rock = Math.Max(rock, .85 * cut * DetMath.Smooth((wall + 10) / 30));
        }
        return carved;
    }

    /// <summary>Distance to the trough's line and the position along it, blended over the segments within 60 m of the
    /// nearest, or 80 % of the distance further out (blended over 60 m only, the floor's slope broke into wedges
    /// fanning out from each bend).</summary>
    private static void Nearest(Trough c, double x, double z, out double dist, out double t)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < c.X.Length; i++) best = Math.Min(best, SegmentDistance(c, i, x, z, out _));
        double ws = 0, ts = 0, blend = Math.Max(60, best * .8);
        for (int i = 0; i + 1 < c.X.Length; i++)
        {
            double d = SegmentDistance(c, i, x, z, out double u);
            double w = DetMath.Smooth(1 - (d - best) / blend);
            if (w <= 0) continue;
            ws += w; ts += w * (c.Along[i] + u * (c.Along[i + 1] - c.Along[i]));
        }
        dist = best; t = ts / ws;
    }

    private static double SegmentDistance(Trough c, int i, double x, double z, out double u)
    {
        double ax = c.X[i], az = c.Z[i], dx = c.X[i + 1] - ax, dz = c.Z[i + 1] - az, len2 = Math.Max(1e-9, dx * dx + dz * dz);
        u = Math.Clamp(((x - ax) * dx + (z - az) * dz) / len2, 0, 1);
        return Math.Sqrt(Sq(ax + dx * u - x) + Sq(az + dz * u - z));
    }

    private static double SmoothMin(double a, double b, double k)
    {
        double h = DetMath.Clamp(.5 + .5 * (b - a) / k, 0, 1);
        return b + (a - b) * h - k * h * (1 - h);
    }

    private static double Sq(double v) => v * v;
}
