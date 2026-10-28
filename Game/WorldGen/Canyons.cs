using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

/// <summary>
/// Gorges and canyons (landform no. 3, terrain revision 21, D239; docs/LANDFORMS.md). Rivers seldom run deep below flat
/// high ground on their own (the plan's erosion carves wide valleys: 10–50 such stretches over 500 m in a world, the
/// longest under 4 km), so canyons are made the way antecedent rivers make them: along a few stretches of river the
/// land on both sides is raised into a plateau while the river keeps its course and level — it has cut the canyon.
/// Placed once, after the plan's rivers are final, on rivers with a real catchment and a gentle grade, inland, off the
/// mountains and away from lakes; the raise goes under the walking-scale ground (base height), and the river's own
/// channel cuts through it with steep walls (<see cref="Wall"/>). Tributaries crossing the raised land cut side
/// canyons the same way. Rivers, lakes and the sea are unchanged: water wins by construction.
/// </summary>
public sealed class Canyons
{
    private sealed class Canyon
    {
        public required double[] X, Z, Along;
        public required double Depth, Band, Fade, Length;
        public required ulong Seed;
        public double MinX, MinZ, MaxX, MaxZ;
    }

    private const double GridCell = 2000, Spacing = 100;
    private readonly List<Canyon> _canyons = new();
    private readonly int _revision;
    private readonly double _minX, _minZ;
    private readonly int _gridSize;
    private readonly int[] _gridStart, _gridItems;

    /// <summary>Number of canyons in this world.</summary>
    public int Count => _canyons.Count;

    /// <summary>The middle of each canyon's river and its depth (tests, captures).</summary>
    public IEnumerable<(double X, double Z, double Depth)> Probes()
    {
        foreach (var c in _canyons) yield return (c.X[c.X.Length / 2], c.Z[c.Z.Length / 2], c.Depth);
    }

    public Canyons(WorldPlan plan, LandformSetting setting)
    {
        _revision = plan.Style.Revision;
        var macro = plan.Macro; var hy = plan.Hydrology;
        var rng = new RandomStream(LandformCatalog.Seed(plan.Manifest, LandformCatalog.Canyons));
        var land = macro.Channel(WorldPlan.LandChannel);
        int landCells = 0;
        foreach (float v in land) if (v > .5f) landCells++;
        double landKm2 = landCells * macro.Cell * macro.Cell / 1e6;
        int want = Math.Clamp((int)Math.Round(setting.Amount * landKm2 / (180 * 180)), landKm2 > 6000 ? 1 : 0, 48);
        var candidates = new List<int>();
        foreach (var r in hy.Reaches)
            if (!r.Distributary && r.Length > 4000 && r.Catchment[r.Catchment.Length / 2] >= 150) candidates.Add(r.Id);
        for (int attempt = 0; attempt < want * 30 && _canyons.Count < want && candidates.Count > 0; attempt++)
        {
            var r = hy.Reaches[candidates[rng.Int(candidates.Count)]];
            double length = Math.Min(rng.Between(3000, 10000) * setting.Size, r.Length * .8);
            double start = rng.Between(r.Length * .1, Math.Max(r.Length * .1, r.Length * .9 - length));
            double depth = rng.Between(60, 180) * setting.Size, band = rng.Between(600, 1500), fade = rng.Between(1500, 2500);
            ulong seed = rng.Next();
            if (length < 2500) continue;
            // The stretch, resampled every 100 m; checked all along: inland, off the mountains, no lake, a gentle grade.
            var xs = new List<double>(); var zs = new List<double>();
            int k = 0; while (k < r.Points.Length - 1 && r.Along[k] < start) k++;
            int k0 = k;
            double next = r.Along[k];
            for (; k < r.Points.Length && r.Along[k] <= start + length; k++)
                if (r.Along[k] >= next) { xs.Add(r.Points[k].X); zs.Add(r.Points[k].Z); next = r.Along[k] + Spacing; }
            int k1 = Math.Min(k, r.Points.Length - 1);
            if (xs.Count < 20 || (r.Surface[k0] - r.Surface[k1]) / Math.Max(1, r.Along[k1] - r.Along[k0]) > .01) continue;
            bool ok = true;
            double reach = band + fade;
            for (int i = 0; i < xs.Count && ok; i += 3)
            {
                int i1 = Math.Min(i + 1, xs.Count - 1), i0 = Math.Max(i - 1, 0);
                double tx = xs[i1] - xs[i0], tz = zs[i1] - zs[i0], tl = Math.Max(1e-6, Math.Sqrt(tx * tx + tz * tz));
                double nx = -tz / tl, nz = tx / tl;
                foreach (double side in new[] { -1.0, 1.0 })
                    foreach (double d in new[] { band * .5, reach })
                    {
                        double px = xs[i] + nx * d * side, pz = zs[i] + nz * d * side;
                        if (macro.Sample(WorldPlan.LandChannel, px, pz) < .5 || macro.Sample(WorldPlan.MountainChannel, px, pz) > .3 ||
                            hy.LakeNear(px, pz) >= 0) ok = false;
                    }
                if (hy.LakeNear(xs[i], zs[i]) >= 0) ok = false;
                foreach (var other in _canyons)
                    if (Math.Abs(other.X[other.X.Length / 2] - xs[i]) + Math.Abs(other.Z[other.Z.Length / 2] - zs[i]) < 20000) ok = false;
            }
            if (!ok) continue;
            var along = new double[xs.Count];
            for (int i = 1; i < along.Length; i++) along[i] = along[i - 1] + Math.Sqrt(Sq(xs[i] - xs[i - 1]) + Sq(zs[i] - zs[i - 1]));
            var c = new Canyon { X = xs.ToArray(), Z = zs.ToArray(), Along = along, Depth = depth, Band = band, Fade = fade, Length = along[^1], Seed = seed };
            c.MinX = c.MaxX = c.X[0]; c.MinZ = c.MaxZ = c.Z[0];
            for (int i = 1; i < c.X.Length; i++)
            {
                c.MinX = Math.Min(c.MinX, c.X[i]); c.MaxX = Math.Max(c.MaxX, c.X[i]);
                c.MinZ = Math.Min(c.MinZ, c.Z[i]); c.MaxZ = Math.Max(c.MaxZ, c.Z[i]);
            }
            _canyons.Add(c);
        }
        // Canyons near each 2 km cell.
        _minX = macro.MinX; _minZ = macro.MinZ;
        _gridSize = (int)Math.Ceiling(macro.Size * macro.Cell / GridCell);
        var cells = new List<int>[_gridSize * _gridSize];
        for (int n = 0; n < _canyons.Count; n++)
        {
            var c = _canyons[n];
            double reach = c.Band + c.Fade + GridCell;
            for (int j = Cell(c.MinZ - reach, _minZ); j <= Cell(c.MaxZ + reach, _minZ); j++)
                for (int i = Cell(c.MinX - reach, _minX); i <= Cell(c.MaxX + reach, _minX); i++)
                    (cells[j * _gridSize + i] ??= new List<int>()).Add(n);
        }
        _gridStart = new int[cells.Length + 1];
        var items = new List<int>();
        for (int i = 0; i < cells.Length; i++) { _gridStart[i] = items.Count; if (cells[i] != null) items.AddRange(cells[i]); }
        _gridStart[^1] = items.Count;
        _gridItems = items.ToArray();
    }

    private int Cell(double v, double min) => Math.Clamp((int)Math.Floor((v - min) / GridCell), 0, _gridSize - 1);

    /// <summary>
    /// How much the land is raised here (m): the full depth within the band beside the river (600–1,500 m), fading
    /// out over 1.5–2.5 km beyond; the ends open out over 1.2 km; the depth wanders ±20 %. Revision 22 (D240) measures
    /// the ends and the wander in space (distance to the canyon's two ends, noise over the ground): revision 21 used
    /// the position along the river, which jumps where a meander brings two parts of the river side by side — a canyon's
    /// end beside its middle drew a 60 m cliff with no river at its foot (Tom's dump 21:59:22, build 118).
    /// </summary>
    public double Uplift(double x, double z)
    {
        if (_canyons.Count == 0) return 0;
        int cell = Cell(z, _minZ) * _gridSize + Cell(x, _minX);
        double best = 0;
        for (int n = _gridStart[cell]; n < _gridStart[cell + 1]; n++)
        {
            var c = _canyons[_gridItems[n]];
            Nearest(c, x, z, out double dist, out double t);
            double across = 1 - DetMath.Smooth((dist - c.Band) / c.Fade);
            if (across <= 0) continue;
            double ends, vary;
            if (_revision >= 22)
            {
                double toStart = Math.Sqrt(Sq(x - c.X[0]) + Sq(z - c.Z[0])), toEnd = Math.Sqrt(Sq(x - c.X[^1]) + Sq(z - c.Z[^1]));
                ends = DetMath.Smooth(toStart / 1500) * DetMath.Smooth(toEnd / 1500);
                vary = 1 + .2 * Noise.Fbm(x, z, 2500, 2, 0, c.Seed);
            }
            else
            {
                ends = DetMath.Smooth(t / 1200) * DetMath.Smooth((c.Length - t) / 1200);
                vary = 1 + .2 * Noise.Fbm(t, 0, 2500, 2, 0, c.Seed);
            }
            best = Math.Max(best, c.Depth * vary * across * ends);
        }
        return best;
    }

    /// <summary>
    /// How much a river's valley side here is a canyon wall (0 none … 1): where the land is raised by more than a few
    /// metres (the canyon's own river and the tributaries crossing its plateau).
    /// </summary>
    public static double Wall(double uplift) => DetMath.Smooth((uplift - 8) / 25);

    /// <summary>Distance to the canyon's river line and the position along it, blended over the segments within 60 m
    /// of the nearest (so the position along changes smoothly through the bends, as for escarpments, D235).</summary>
    private static void Nearest(Canyon c, double x, double z, out double dist, out double t)
    {
        double best = double.MaxValue;
        for (int i = 0; i + 1 < c.X.Length; i++) best = Math.Min(best, SegmentDistance(c, i, x, z, out _));
        double ws = 0, ts = 0;
        for (int i = 0; i + 1 < c.X.Length; i++)
        {
            double d = SegmentDistance(c, i, x, z, out double u);
            double w = DetMath.Smooth(1 - (d - best) / 60);
            if (w <= 0) continue;
            ws += w; ts += w * (c.Along[i] + u * (c.Along[i + 1] - c.Along[i]));
        }
        dist = best; t = ts / ws;
    }

    private static double SegmentDistance(Canyon c, int i, double x, double z, out double u)
    {
        double ax = c.X[i], az = c.Z[i], dx = c.X[i + 1] - ax, dz = c.Z[i + 1] - az, len2 = Math.Max(1e-9, dx * dx + dz * dz);
        u = Math.Clamp(((x - ax) * dx + (z - az) * dz) / len2, 0, 1);
        return Math.Sqrt(Sq(ax + dx * u - x) + Sq(az + dz * u - z));
    }

    private static double Sq(double v) => v * v;
}
