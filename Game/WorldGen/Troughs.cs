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
/// rises less than 800 m above it. Revision 26 (D244, landform no. 6): fjords, troughs whose floor runs below the sea
/// to the coast — the sea follows them inland (<see cref="Carve"/> reports it, the sampler draws it as the ocean).
/// </summary>
public sealed class Troughs
{
    private sealed class Trough
    {
        public required double[] X, Z, Along, Floor;
        public required double HalfFloor, Slope, Reach;
        public required ulong Seed;
        public double MinX, MinZ, MaxX, MaxZ;
        /// <summary>A fjord (revision 26): the floor below the sea from its head's shore to the coast.</summary>
        public bool Fjord;
        /// <summary>A fjord's mountain walls: the land raised by <see cref="Lift"/> within <see cref="Band"/> of the
        /// floor's edge, fading out over <see cref="Fade"/>.</summary>
        public double Lift, Band, Fade;
    }

    /// <summary>A fall where a river drops into a trough: a side river from its hanging valley, or the main river at
    /// the trough's head.</summary>
    public readonly record struct Fall(double X, double Z, double Height, bool Side);

    private const double GridCell = 2000, Spacing = 50, FloorAbove = 1.5;
    private readonly List<Trough> _troughs = new();
    private readonly List<Fall> _falls = new();
    /// <summary>Fjord coasts (revision 27): middle, radius, seed.</summary>
    private readonly List<(double X, double Z, double Radius, ulong Seed)> _coasts = new();
    private readonly double _minX, _minZ;
    private readonly int _revision;
    private readonly int _gridSize;
    private readonly int[] _gridStart, _gridItems;

    /// <summary>Number of troughs in this world (fjords not counted).</summary>
    public int Count { get { int n = 0; foreach (var c in _troughs) if (!c.Fjord) n++; return n; } }

    /// <summary>Number of fjords in this world (revision 26).</summary>
    public int FjordCount => _troughs.Count - Count;

    /// <summary>Every fall into a trough (tests, captures), highest first.</summary>
    public IReadOnlyList<Fall> Falls => _falls;

    /// <summary>The middle of each trough's floor, its level (metres above the sea), its half width and the direction
    /// down the trough there (tests, captures).</summary>
    public IEnumerable<(double X, double Z, double Floor, double HalfFloor, double Dx, double Dz)> Probes()
    {
        foreach (var c in _troughs)
        {
            if (c.Fjord) continue;
            int m = c.X.Length / 2;
            double dx = c.X[m + 1] - c.X[m - 1], dz = c.Z[m + 1] - c.Z[m - 1], l = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
            yield return (c.X[m], c.Z[m], c.Floor[m], c.HalfFloor, dx / l, dz / l);
        }
    }

    /// <summary>Each fjord's line from its head to the coast, every 50 m, with its floor (metres above the sea, below 0
    /// under the water) and half width (tests, captures).</summary>
    public IEnumerable<(double[] X, double[] Z, double[] Floor, double HalfFloor)> FjordLines()
    {
        foreach (var c in _troughs) if (c.Fjord) yield return (c.X, c.Z, c.Floor, c.HalfFloor);
    }

    public Troughs(WorldPlan plan, LandformSetting setting, LandformSetting? fjords = null)
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
        if (fjords is { Amount: > 0 } fs)
        {
            if (_revision >= 27) PlaceFjordCoasts(plan, fs, bounds, cirques);
            else PlaceFjords(plan, fs, bounds, cirques, canyons);
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
    /// Fjord coasts (revision 27, D245; Tom: fjords "more interesting to explore", not "randomly placed in the forest"):
    /// a stretch of the coldest (northern) coast, 25–45 km (× size) round its middle, with three or more river mouths. The
    /// plan's own height raster is raised there into a coastal range — 500–900 m (× size), in ridges and peaks a few
    /// kilometres apart (0.15–1.5 of it), from 0.5 km inland to full by 3 km, fading out 10–28 km inland and toward the
    /// stretch's ends, away from lakes — so the climate
    /// and biomes see mountains (cold, rocky) and the rivers run in deep valleys. Every river mouth there with 20 km² or
    /// more draining to it (3.5 km apart) becomes a fjord 8–22 km long; its tributaries joining under the water become
    /// side arms; islands and skerries lie off the coast (<see cref="Skerries"/>).
    /// </summary>
    private void PlaceFjordCoasts(WorldPlan plan, LandformSetting setting, (double MinX, double MinZ, double MaxX, double MaxZ)[] bounds,
        List<(double X, double Z, double Radius)> cirques)
    {
        var macro = plan.Macro; var hy = plan.Hydrology; var reaches = hy.Reaches;
        double sea = plan.Profile.SeaLevel;
        var rng = new RandomStream(LandformCatalog.Seed(plan.Manifest, LandformCatalog.Fjords));
        var land = macro.Channel(WorldPlan.LandChannel); var height = macro.Channel(WorldPlan.HeightChannel);
        var mountain = macro.Channel(WorldPlan.MountainChannel);
        int size = macro.Size;
        bool Sea(int i, int j) => i < 0 || j < 0 || i >= size || j >= size || land[j * size + i] <= 0;
        var coast = new List<int>();
        int landCells = 0;
        for (int j = 0; j < size; j++)
            for (int i = 0; i < size; i++)
            {
                if (Sea(i, j)) continue;
                landCells++;
                if (Sea(i - 1, j) || Sea(i + 1, j) || Sea(i, j - 1) || Sea(i, j + 1)) coast.Add(j * size + i);
            }
        if (coast.Count == 0) return;
        // The coldest coast: the northern 30 % of the coastal cells (north is −Z).
        coast.Sort((a, b) => a.CompareTo(b)); // (row-major: by row, then column — by Z first)
        var cold = coast.GetRange(0, Math.Max(1, coast.Count * 3 / 10));
        var mouths = new List<RiverReach>();
        foreach (var r in reaches) if (!r.Distributary && r.EndsInSea && r.Catchment[^1] >= 20) mouths.Add(r);
        double landKm2 = landCells * macro.Cell * macro.Cell / 1e6;
        int want = Math.Clamp((int)Math.Round(setting.Amount * landKm2 / (350 * 350)), 1, 6);
        for (int attempt = 0; attempt < want * 40 && _coasts.Count < want; attempt++)
        {
            int k = cold[rng.Int(cold.Count)];
            double cx = macro.CellX(k % size), cz = macro.CellZ(k / size);
            double radius = rng.Between(25000, 45000) * setting.Size, lift = rng.Between(500, 900) * setting.Size;
            ulong seed = rng.Next();
            var here = new List<RiverReach>();
            foreach (var r in mouths) if (Math.Sqrt(Sq(r.Points[^1].X - cx) + Sq(r.Points[^1].Z - cz)) < radius * .7) here.Add(r);
            if (here.Count < 3) continue;
            bool apart = true;
            foreach (var (ox, oz, or, _) in _coasts) if (Math.Sqrt(Sq(ox - cx) + Sq(oz - cz)) < radius + or) apart = false;
            if (!apart) continue;
            _coasts.Add((cx, cz, radius, seed));
            RaiseCoast(macro, hy, land, height, mountain, cx, cz, radius, lift, seed);
            // Fjords: every mouth, biggest first, 3.5 km apart.
            here.Sort((a, b) => b.Catchment[^1].CompareTo(a.Catchment[^1]));
            var placed = new List<(double X, double Z)>();
            foreach (var mouth in here)
            {
                double mx0 = mouth.Points[^1].X, mz0 = mouth.Points[^1].Z;
                bool spaced = true;
                foreach (var (px, pz) in placed) if (Math.Sqrt(Sq(px - mx0) + Sq(pz - mz0)) < 3500) spaced = false;
                if (!spaced) continue;
                double length = rng.Between(8000, 22000) * setting.Size, halfFloor = rng.Between(250, 450) * setting.Size;
                double slope = rng.Between(1.8, 2.6), depth = rng.Between(100, 300) * setting.Size;
                ulong fseed = rng.Next();
                // (shorter and narrower if a river would rise just above a fall into it: most refusals, D243)
                Trough? c = null; HashSet<int> mainChain = new();
                foreach (double scale in new[] { 1, .6, .35 })
                    if ((c = FjordLine(plan, mouth, mouth.Points.Length - 1, Math.Max(4000 * setting.Size, length * scale), halfFloor * (.6 + .4 * scale), slope, depth,
                        depth * .25, fseed, bounds, cirques, setting, out _, out mainChain)) != null) break;
                if (c == null) continue;
                placed.Add((mx0, mz0));
                // Side arms: tributaries joining the fjord where it is 30 m or more deep.
                int arms = 0;
                foreach (int id in mainChain)
                {
                    var r = reaches[id];
                    if (arms >= 2 || r.UpstreamReaches.Count < 2) continue;
                    Surface(c, r.Points[0].X, r.Points[0].Z, out double fj, out _, out _);
                    if (fj > -30) continue;
                    foreach (int u in r.UpstreamReaches)
                    {
                        if (mainChain.Contains(u) || reaches[u].Catchment[^1] < 8 || arms >= 2) continue;
                        double armLength = rng.Between(3000, 9000) * setting.Size, armHalf = rng.Between(180, 300) * setting.Size;
                        double armSlope = rng.Between(1.8, 2.6);
                        ulong aseed = rng.Next();
                        if (FjordLine(plan, reaches[u], reaches[u].Points.Length - 1, armLength, armHalf, armSlope, -fj, -fj, aseed, bounds, cirques, setting, out _, out _) != null)
                            arms++;
                    }
                }
            }
        }
    }

    /// <summary>
    /// A fjord (or a side arm) up a river from a point on it (a mouth or a junction), revision 27: up the bigger branch at
    /// each junction for <paramref name="length"/>, up to where the river stands 300 m (× size) over the sea, at least
    /// 3 km; a dry valley floor at the head (the river falls in 150 m at most; a river rising gently comes in without a
    /// fall) rising inland at up to 3.5 %, the head's
    /// shore at 10–50 % of the length, then the water deepening to <paramref name="depth"/> and rising to
    /// <paramref name="endDepth"/> below the sea at the start point (a sill at a mouth; the main fjord's floor at an arm's
    /// junction). A mouth's line runs on 1.5 km out to sea. Added (and the rivers lowered) only if <see cref="Lower"/>
    /// accepts it.
    /// </summary>
    private Trough? FjordLine(WorldPlan plan, RiverReach from, int point, double length, double halfFloor, double slope, double depth, double endDepth,
        ulong seed, (double MinX, double MinZ, double MaxX, double MaxZ)[] bounds, List<(double X, double Z, double Radius)> cirques,
        LandformSetting setting, out double[] floorOut, out HashSet<int> chain)
    {
        var hy = plan.Hydrology; var reaches = hy.Reaches;
        double sea = plan.Profile.SeaLevel;
        floorOut = Array.Empty<double>(); chain = new HashSet<int>();
        var xs = new List<double>(); var zs = new List<double>(); var ss = new List<double>();
        var r = from; int k = point; double run = 0, next = 0;
        while (true)
        {
            chain.Add(r.Id);
            for (; k >= 0 && run < length && (ss.Count == 0 || ss[^1] <= 300 * setting.Size); k--)
            {
                if (xs.Count > 0 && k + 1 < r.Points.Length) run += Math.Sqrt(Sq(r.Points[k].X - r.Points[k + 1].X) + Sq(r.Points[k].Z - r.Points[k + 1].Z));
                if (run >= next) { xs.Add(r.Points[k].X); zs.Add(r.Points[k].Z); ss.Add(r.Surface[k] - sea); next = run + Spacing; }
            }
            if (run >= length || r.UpstreamReaches.Count == 0 || r.StartLake >= 0 || ss[^1] > 300 * setting.Size) break;
            int up = r.UpstreamReaches[0];
            foreach (int u in r.UpstreamReaches) if (reaches[u].Catchment[^1] > reaches[up].Catchment[^1]) up = u;
            r = reaches[up]; k = r.Points.Length - 2;
        }
        if (run < 3000 * setting.Size || xs.Count < 50) return null;
        xs.Reverse(); zs.Reverse(); ss.Reverse();
        int n = xs.Count;
        for (int i = 0; i < n; i += 4)
        {
            if (hy.LakeNear(xs[i], zs[i]) >= 0) return null;
            foreach (var (qx, qz, qr) in cirques) if (Math.Sqrt(Sq(qx - xs[i]) + Sq(qz - zs[i])) < qr * 1.3 + halfFloor * 1.25 + 400) return null;
            foreach (var other in _troughs)
                if (!other.Fjord)
                    for (int j = 0; j < other.X.Length; j += 10)
                        if (Math.Abs(other.X[j] - xs[i]) + Math.Abs(other.Z[j] - zs[i]) < 4000) return null;
        }
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
        double head = Math.Max(Math.Min(30, ss[0] * .5), ss[0] - 150 * setting.Size), shore = DetMath.Clamp(head / (.035 * along[^1]), .1, .5);
        if (head / (shore * along[^1]) > .06) return null;
        var floor = new double[n];
        for (int i = 0; i < n; i++)
        {
            double u = along[i] / along[^1];
            double f = u < shore ? head * (1 - u / shore)
                : -(depth + (endDepth - depth) * DetMath.Smooth((u - .8) / .2)) * DetMath.Smooth((u - shore) / Math.Max(.15, (1 - shore) * .4));
            floor[i] = Math.Min(ss[i], f);
        }
        bool mouth = from.EndsInSea && point == from.Points.Length - 1;
        if (mouth)
        {
            double ex = mx[^1] - mx[^5], ez = mz[^1] - mz[^5], el = Math.Max(1e-6, Math.Sqrt(ex * ex + ez * ez));
            var lx = new List<double>(mx); var lz = new List<double>(mz); var la = new List<double>(along); var lf = new List<double>(floor);
            for (int e = 1; e <= 30; e++)
            {
                lx.Add(mx[^1] + ex / el * e * Spacing); lz.Add(mz[^1] + ez / el * e * Spacing);
                la.Add(along[^1] + e * Spacing); lf.Add(floor[^1]);
            }
            mx = lx.ToArray(); mz = lz.ToArray(); along = la.ToArray(); floor = lf.ToArray(); n = mx.Length;
        }
        var c = new Trough
        {
            X = mx, Z = mz, Along = along, Floor = floor, HalfFloor = halfFloor, Slope = slope, Seed = seed, Fjord = true,
            Reach = halfFloor * 1.25 + 40 + 2 * (depth + 1500 * setting.Size) / slope,
        };
        c.MinX = c.MaxX = c.X[0]; c.MinZ = c.MaxZ = c.Z[0];
        for (int i = 1; i < n; i++)
        {
            c.MinX = Math.Min(c.MinX, c.X[i]); c.MaxX = Math.Max(c.MaxX, c.X[i]);
            c.MinZ = Math.Min(c.MinZ, c.Z[i]); c.MaxZ = Math.Max(c.MaxZ, c.Z[i]);
        }
        if (!Lower(hy, c, chain, bounds, sea, setting)) return null;
        _troughs.Add(c);
        floorOut = floor;
        return c;
    }

    /// <summary>The coastal range of a fjord coast (revision 27), raised in the plan's height raster (and its mountain share).</summary>
    private static void RaiseCoast(MacroRaster macro, Hydrology hy, float[] land, float[] height, float[] mountain, double cx, double cz, double radius,
        double lift, ulong seed)
    {
        int size = macro.Size, reach = (int)Math.Ceiling(radius / macro.Cell) + 2;
        int ci = (int)((cx - macro.MinX) / macro.Cell), cj = (int)((cz - macro.MinZ) / macro.Cell);
        int i0 = Math.Max(0, ci - reach), i1 = Math.Min(size - 1, ci + reach), j0 = Math.Max(0, cj - reach), j1 = Math.Min(size - 1, cj + reach);
        int w = i1 - i0 + 1, h = j1 - j0 + 1;
        // Distance to the sea (cells; chamfer 1, √2) within the box.
        var dist = new double[w * h];
        for (int j = 0; j < h; j++)
            for (int i = 0; i < w; i++) dist[j * w + i] = land[(j + j0) * size + i + i0] <= 0 ? 0 : 1e9;
        for (int pass = 0; pass < 2; pass++)
            for (int jj = 0; jj < h; jj++)
                for (int ii = 0; ii < w; ii++)
                {
                    int i = pass == 0 ? ii : w - 1 - ii, j = pass == 0 ? jj : h - 1 - jj, dir = pass == 0 ? -1 : 1;
                    double d = dist[j * w + i];
                    if (i + dir >= 0 && i + dir < w) d = Math.Min(d, dist[j * w + i + dir] + 1);
                    if (j + dir >= 0 && j + dir < h)
                    {
                        d = Math.Min(d, dist[(j + dir) * w + i] + 1);
                        if (i + dir >= 0 && i + dir < w) d = Math.Min(d, dist[(j + dir) * w + i + dir] + 1.4142);
                        if (i - dir >= 0 && i - dir < w) d = Math.Min(d, dist[(j + dir) * w + i - dir] + 1.4142);
                    }
                    dist[j * w + i] = d;
                }
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                int k = j * size + i;
                if (land[k] <= 0) continue;
                double x = macro.CellX(i), z = macro.CellZ(j), d = Math.Sqrt(Sq(x - cx) + Sq(z - cz));
                double region = 1 - DetMath.Smooth((d - radius * .6) / (radius * .4));
                if (region <= 0) continue;
                double dc = Math.Min(dist[(j - j0) * w + i - i0], 1e6) * macro.Cell;
                double profile = DetMath.Smooth((dc - 500) / 2500) * (1 - DetMath.Smooth((dc - 10000) / 18000));
                int lake = hy.LakeDistance(x, z, out double cells);
                if (lake >= 0) profile *= DetMath.Smooth((cells - 1.6) / 2);
                // Ridges and peaks a few kilometres apart (an even raise read as one block), varying over 15 km.
                double ridge = 1 - Math.Abs(Noise.Fbm(x, z, 7000, 3, 0, seed + 1));
                double vary = Math.Max(.15, .45 + .8 * ridge * ridge + .25 * Noise.Fbm(x, z, 15000, 2, 0, seed));
                height[k] += (float)(lift * profile * region * vary);
                mountain[k] = Math.Max(mountain[k], (float)(.65 * profile * region));
            }
    }

    /// <summary>
    /// Fjords (landform no. 6, revision 26, D244): the last 6–18 km (× size) of a river reaching the sea through
    /// mountains (up to where the river stands 400 m × size over the sea), cut down like a trough but below the sea: a
    /// dry valley floor at the head (the river falls in from its amphitheatre, 150 m at most — falls of 470 m came out
    /// at first), rising inland at up to 3.5 %, its shore after 15–55 % of the length; then the floor drops to 80–220 m
    /// (× size) below the sea and rises to a sill a quarter as deep at the coast. Wider than a trough (500–1,000 m floor), steeper walls (1.6–2.4), its
    /// mouth open (no narrowing at the coast). Where: both sides stand 150 m or more over the sea 400 m beyond the floor;
    /// apart from troughs, canyons, cirques and lakes; one per 60 × 60 km of mountain ground.
    /// </summary>
    private void PlaceFjords(WorldPlan plan, LandformSetting setting, (double MinX, double MinZ, double MaxX, double MaxZ)[] bounds,
        List<(double X, double Z, double Radius)> cirques, List<(double X, double Z)> canyons)
    {
        var macro = plan.Macro; var hy = plan.Hydrology; var reaches = hy.Reaches;
        double sea = plan.Profile.SeaLevel;
        var rng = new RandomStream(LandformCatalog.Seed(plan.Manifest, LandformCatalog.Fjords));
        var mountain = macro.Channel(WorldPlan.MountainChannel);
        int mountainCells = 0;
        foreach (float v in mountain) if (v > .25f) mountainCells++;
        double km2 = mountainCells * macro.Cell * macro.Cell / 1e6;
        int want = Math.Clamp((int)Math.Round(setting.Amount * km2 / (60 * 60)), 0, 30), placed = 0;
        var mouths = new List<int>();
        foreach (var r in reaches)
            if (!r.Distributary && r.EndsInSea && r.Catchment[^1] >= 50) mouths.Add(r.Id);
        for (int attempt = 0; attempt < want * 40 && placed < want && mouths.Count > 0; attempt++)
        {
            var mouth = reaches[mouths[rng.Int(mouths.Count)]];
            double length = rng.Between(6000, 18000) * setting.Size, halfFloor = rng.Between(250, 500) * setting.Size;
            double slope = rng.Between(1.6, 2.4), depth = rng.Between(80, 220) * setting.Size;
            ulong seed = rng.Next();
            double lift = rng.Between(300, 600) * setting.Size, band = rng.Between(600, 1400), fade = rng.Between(4000, 7000);
            // Up the river from the sea, every 50 m, along the bigger branch at each junction.
            var xs = new List<double>(); var zs = new List<double>(); var ss = new List<double>();
            var chain = new HashSet<int>();
            var r = mouth; int k = r.Points.Length - 1; double run = 0, next = 0;
            while (true)
            {
                chain.Add(r.Id);
                for (; k >= 0 && run < length && (ss.Count == 0 || ss[^1] <= 400 * setting.Size); k--)
                {
                    if (xs.Count > 0 && k + 1 < r.Points.Length) run += Math.Sqrt(Sq(r.Points[k].X - r.Points[k + 1].X) + Sq(r.Points[k].Z - r.Points[k + 1].Z));
                    if (run >= next) { xs.Add(r.Points[k].X); zs.Add(r.Points[k].Z); ss.Add(r.Surface[k] - sea); next = run + Spacing; }
                }
                if (run >= length || r.UpstreamReaches.Count == 0 || r.StartLake >= 0 || ss[^1] > 400 * setting.Size) break;
                int up = r.UpstreamReaches[0];
                foreach (int u in r.UpstreamReaches) if (reaches[u].Catchment[^1] > reaches[up].Catchment[^1]) up = u;
                r = reaches[up]; k = r.Points.Length - 2;
            }
            if (run < 3500 * setting.Size || xs.Count < 60) continue;
            xs.Reverse(); zs.Reverse(); ss.Reverse();
            int n = xs.Count;
            if (ss[0] < 25) continue; // (the head stands above the sea: the river falls into the fjord)
            bool ok = true;
            for (int i = 0; i < n && ok; i += 4)
            {
                if (hy.LakeNear(xs[i], zs[i]) >= 0) ok = false;
                // (no lake under the raised walls either)
                int i0 = Math.Max(0, i - 2), i1 = Math.Min(n - 1, i + 2);
                double tx = xs[i1] - xs[i0], tz = zs[i1] - zs[i0], tl = Math.Max(1e-6, Math.Sqrt(tx * tx + tz * tz));
                foreach (double d in new[] { -halfFloor - band - fade, -halfFloor - band - fade * .5, -halfFloor - band, halfFloor + band, halfFloor + band + fade * .5, halfFloor + band + fade })
                    if (hy.LakeNear(xs[i] - tz / tl * d, zs[i] + tx / tl * d) >= 0) ok = false;
                foreach (var other in _troughs)
                    for (int j = 0; j < other.X.Length && ok; j += 10)
                        if (Math.Abs(other.X[j] - xs[i]) + Math.Abs(other.Z[j] - zs[i]) < 8000) ok = false;
                foreach (var (cx, cz) in canyons) if (Math.Abs(cx - xs[i]) + Math.Abs(cz - zs[i]) < 15000) ok = false;
                foreach (var (qx, qz, qr) in cirques) if (Math.Sqrt(Sq(qx - xs[i]) + Sq(qz - zs[i])) < qr * 1.3 + halfFloor * 1.25 + 400) ok = false;
            }
            if (!ok) continue;
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
            // The floor: dry at the head, the river falling in 150 m (× size) at most (half its height there where that is
            // less than 30 m), a valley floor rising inland at up to 3.5 % to the head's shore (15–55 % of the length); then
            // the deepest a quarter of the length on, a sill a quarter as deep at the coast; never above the old river.
            double head = Math.Max(Math.Min(30, ss[0] * .5), ss[0] - 150 * setting.Size), shore = DetMath.Clamp(head / (.035 * along[^1]), .15, .55);
            if (head / (shore * along[^1]) > .06) continue; // (a steep ramp, not a valley floor)
            var floor = new double[n];
            for (int i = 0; i < n; i++)
            {
                double u = along[i] / along[^1];
                double f = u < shore ? head * (1 - u / shore)
                    : -depth * DetMath.Smooth((u - shore) / .25) * (1 - .75 * DetMath.Smooth((u - .8) / .2));
                floor[i] = Math.Min(ss[i], f);
            }
            // Hilly ground on both sides: 400 m beyond the floor the land stands 60 m or more over the sea (on average over
            // the inner three quarters) — the fjord raises its own mountain walls (Lift): the worlds' coasts rise gently
            // from the sea, and asking for 300 m found no fjord at all; on the land as it was, a fjord had 40–60 m banks.
            double left = 0, right = 0;
            for (int q = 1; q <= 3; q++)
            {
                int i = q * n / 5, i0 = Math.Max(0, i - 2), i1 = Math.Min(n - 1, i + 2);
                double tx = mx[i1] - mx[i0], tz = mz[i1] - mz[i0], tl = Math.Max(1e-6, Math.Sqrt(tx * tx + tz * tz)), d = halfFloor + 400;
                left += macro.Sample(WorldPlan.HeightChannel, mx[i] - tz / tl * d, mz[i] + tx / tl * d);
                right += macro.Sample(WorldPlan.HeightChannel, mx[i] + tz / tl * d, mz[i] - tx / tl * d);
            }
            if (left < 3 * 60 || right < 3 * 60) continue;
            // The line runs on 1.5 km out to sea, so the walls do not close around the mouth (the coast ends them).
            {
                double ex = mx[^1] - mx[^5], ez = mz[^1] - mz[^5], el = Math.Max(1e-6, Math.Sqrt(ex * ex + ez * ez));
                var lx = new List<double>(mx); var lz = new List<double>(mz); var la = new List<double>(along); var lf = new List<double>(floor);
                for (int e = 1; e <= 30; e++)
                {
                    lx.Add(mx[^1] + ex / el * e * Spacing); lz.Add(mz[^1] + ez / el * e * Spacing);
                    la.Add(along[^1] + e * Spacing); lf.Add(floor[^1]);
                }
                mx = lx.ToArray(); mz = lz.ToArray(); along = la.ToArray(); floor = lf.ToArray(); n = mx.Length;
            }
            var c = new Trough
            {
                X = mx, Z = mz, Along = along, Floor = floor, HalfFloor = halfFloor, Slope = slope, Seed = seed, Fjord = true,
                Lift = lift, Band = band, Fade = fade,
                Reach = Math.Max(halfFloor * 1.25 + 40 + 2 * (depth + lift * 1.4 + 300) / slope, halfFloor + band * 1.6 + fade),
            };
            c.MinX = c.MaxX = c.X[0]; c.MinZ = c.MaxZ = c.Z[0];
            for (int i = 1; i < n; i++)
            {
                c.MinX = Math.Min(c.MinX, c.X[i]); c.MaxX = Math.Max(c.MaxX, c.X[i]);
                c.MinZ = Math.Min(c.MinZ, c.Z[i]); c.MaxZ = Math.Max(c.MaxZ, c.Z[i]);
            }
            if (Lower(hy, c, chain, bounds, sea, setting)) { _troughs.Add(c); placed++; }
        }
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
                if (c.Fjord) floor = Math.Max(floor, 0); // (in a fjord the river meets the sea)
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
                for (int k = 1; k < s.Length; k++)
                    if (s[k] >= s[k - 1])
                    {
                        double v = c.Fjord && s[k - 1] <= sea + 1e-9 ? sea : s[k - 1] - 1e-4; // (water at the sea's level stays there)
                        if (v != s[k]) { s[k] = v; changed = true; }
                    }
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
            if (_revision >= 25 && !c.Fjord)
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
        double half = c.HalfFloor * (1 + .2 * Noise.Fbm(x, z, 1500, 2, 0, c.Seed)) * (c.Fjord ? 1 : DetMath.Smooth(toEnd / 1500));
        wall = dist - half + 25 * Noise.Fbm(x, z, 180, 2, 0, c.Seed + 1);
        const double Foot = 120; // (a U: the wall steepens upward from the floor)
        double rise = wall <= 0 ? 0 : wall < Foot ? c.Slope * wall * wall / (2 * Foot) : c.Slope * (wall - Foot * .5);
        return floor + (c.Fjord ? FloorAbove * DetMath.Smooth(floor / 3) : FloorAbove) + rise;
    }

    /// <summary>True when the world has fjords (revision 26).</summary>
    public bool HasFjords => FjordCount > 0;

    /// <summary>True when the world has fjord coasts (revision 27): their islands and skerries come from <see cref="Skerries"/>.</summary>
    public bool HasCoasts => _coasts.Count > 0;

    /// <summary>The fjord coasts' middles and radii (tests, captures).</summary>
    public IEnumerable<(double X, double Z, double Radius)> Coasts()
    {
        foreach (var (x, z, r, _) in _coasts) yield return (x, z, r);
    }

    /// <summary>
    /// Islands and skerries off a fjord coast (revision 27): how much the sea floor rises here (m) — clusters of rocky
    /// islets (noise over 700 m where it rises over a threshold, in patches over 6 km; knobs and clefts on them), up to
    /// ~10 km out from the coast, over the coast's whole stretch, none in a fjord's channel out to sea. <paramref name="land"/>: the plan's land value (0 at the coast, about −0.6 where
    /// the deep sea begins).
    /// </summary>
    public double Skerries(double x, double z, double land)
    {
        double best = 0;
        foreach (var (cx, cz, radius, seed) in _coasts)
        {
            double d = Math.Sqrt(Sq(x - cx) + Sq(z - cz));
            double region = 1 - DetMath.Smooth((d - radius * .7) / (radius * .5));
            if (region <= 0) continue;
            double near = 1 - DetMath.Smooth((-land - .04) / .14);
            if (near <= 0) continue;
            double cluster = DetMath.Smooth((Noise.Fbm(x, z, 6000, 2, 0, seed + 5) + .15) / .4);
            double rock = Noise.Fbm(x, z, 700, 5, 0, seed + 6);
            best = Math.Max(best, 200 * Math.Max(0, rock - .2) * region * near * cluster);
        }
        if (best <= 0) return 0;
        // None in a fjord's channel out to sea (islands there closed the mouths off).
        int cell = Cell(z, _minZ) * _gridSize + Cell(x, _minX);
        for (int n = _gridStart[cell]; n < _gridStart[cell + 1]; n++)
        {
            var c = _troughs[_gridItems[n]];
            if (!c.Fjord || x < c.MinX - c.Reach || x > c.MaxX + c.Reach || z < c.MinZ - c.Reach || z > c.MaxZ + c.Reach) continue;
            Nearest(c, x, z, out double dist, out _);
            best *= DetMath.Smooth((dist - c.HalfFloor * 1.3) / 400);
        }
        // Rocky, not smooth domes (the sea stage adds no walking-scale relief): knobs and clefts a few tens of metres across.
        return best <= 0 ? 0 : best + 8 * Noise.Fbm(x, z, 90, 3, 0, 4271) * DetMath.Smooth(best / 12);
    }

    /// <summary>
    /// A fjord's mountain walls (revision 26): how much the land is raised here (m) — 300–600 m (× size, ±35 % by noise)
    /// within 0.6–1.4 km (±60 %) of the floor's edge, fading out over 4–7 km beyond (around the head too: the distance there is
    /// to the head), and over the last 800 m to the coast (sea cliffs, not a wall standing in the surf).
    /// <paramref name="share"/>: how much of the full raise this is (the ground there is mountain: rock, rugged relief).
    /// </summary>
    public double Raise(double x, double z, double coastDistance, out double share)
    {
        share = 0;
        if (_troughs.Count == 0) return 0;
        int cell = Cell(z, _minZ) * _gridSize + Cell(x, _minX);
        double best = 0;
        for (int n = _gridStart[cell]; n < _gridStart[cell + 1]; n++)
        {
            var c = _troughs[_gridItems[n]];
            if (!c.Fjord || x < c.MinX - c.Reach || x > c.MaxX + c.Reach || z < c.MinZ - c.Reach || z > c.MaxZ + c.Reach) continue;
            Nearest(c, x, z, out double dist, out _);
            // (an even band drew a raised block around the fjord: its edge wanders ±60 % and the raise ±35 %)
            double across = 1 - DetMath.Smooth((dist - c.HalfFloor - c.Band * (1 + .6 * Noise.Fbm(x, z, 4000, 2, 0, c.Seed + 3))) / c.Fade);
            if (across <= 0) continue;
            double s = across * DetMath.Smooth((coastDistance - 100) / 700);
            share = Math.Max(share, s);
            best = Math.Max(best, c.Lift * (1 + .35 * Noise.Fbm(x, z, 3000, 3, 0, c.Seed + 2)) * s);
        }
        return best;
    }

    /// <summary>
    /// The ground taken down into the troughs here (metres above the sea in and out), blended over 8 m on the floor and up
    /// to 30 m up the wall, so the wall's top is a rounded shoulder. <paramref name="flat"/>: how much of the walking-scale relief to take away (1 on the floor,
    /// fading out up the wall's foot); <paramref name="rock"/>: how bare the wall is; <paramref name="sea"/>: a fjord took
    /// the ground to within 3 m of the sea (the sea fills it wherever the ground with its relief lies below the sea).
    /// </summary>
    public double Carve(double x, double z, double ground, out double flat, out double rock, out bool sea)
    {
        flat = 0; rock = 0; sea = false;
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
            if (c.Fjord && carved < 3) sea = true;
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
