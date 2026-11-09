using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

/// <summary>
/// Banks at waterfall lips (terrain revision 42, D297; Tom: "where the waterfall drops on top.. there is no river bed.. the
/// sides.. terrain is smooth there.. water would spill"). At every stretch of river dropping 3 m or more steeper than 1 in 5 —
/// the falls the game draws (Rewrite FallSet) — the dry ground beside the river is raised to 1.8–3.2 m over the lip's water:
/// from about 25 m upstream (more on wide rivers) along the lip and down the sides of the fall, a rocky sill the water leaves
/// through in a channel. Only dry ground is raised, and only where it lies lower: the channel, every river level and lake
/// are as before.
/// </summary>
public sealed class FallLips
{
    private sealed record Lip(Point2[] Points, double[] Along, double Width, double Top, double Length, double Upstream,
        double MinX, double MinZ, double MaxX, double MaxZ, ulong Seed, double Bottom, int Reach);

    private const double GridCell = 2000;
    private readonly List<Lip> _lips = new();
    private readonly Dictionary<(int, int), List<int>> _grid = new();

    public int Count => _lips.Count;

    /// <summary>Revision 43 (D300): a rock face at each lip and a plunge pool below it (Tom: "Rock face and wide pool").</summary>
    public bool Faces { get; }

    /// <summary>Revision 44 (D301): the river's water sloping down over a fall (its level between the lip's and the foot's, where
    /// the pool did not reach: beside the pool's end, at the lip's ends on a bend) is the lip's water above the face and the
    /// pool below it — the pool reaches as far as that water did.</summary>
    public bool Follow { get; }

    /// <summary>Each fall's lip, downstream direction, width, the water's level at the lip and at the foot, and how long the drop
    /// runs along the river (tests).</summary>
    public IEnumerable<(double X, double Z, double Dx, double Dz, double Width, double Top, double Bottom, double Length)> Falls()
    {
        foreach (var l in _lips)
        {
            int k = Array.FindIndex(l.Along, a => a >= 0);
            var p = l.Points[k]; var q = l.Points[Math.Min(l.Points.Length - 1, k + 1)];
            double dx = q.X - p.X, dz = q.Z - p.Z, dl = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
            yield return (p.X, p.Z, dx / dl, dz / dl, l.Width, l.Top, l.Bottom, l.Length);
        }
    }

    /// <summary>Each lip (the river's point there), its downstream direction, the river's width and the water's level at the lip
    /// (absolute) (tests).</summary>
    public IEnumerable<(double X, double Z, double Dx, double Dz, double Width, double Top)> Probes()
    {
        foreach (var l in _lips)
        {
            int k = Array.FindIndex(l.Along, a => a >= 0);
            var p = l.Points[k]; var q = l.Points[Math.Min(l.Points.Length - 1, k + 1)];
            double dx = q.X - p.X, dz = q.Z - p.Z, dl = Math.Max(1e-6, Math.Sqrt(dx * dx + dz * dz));
            yield return (p.X, p.Z, dx / dl, dz / dl, l.Width, l.Top);
        }
    }

    public FallLips(WorldPlan plan, bool faces = false, bool follow = false)
    {
        Faces = faces;
        Follow = faces && follow;
        var rng = new RandomStream(Hashing.Mix((ulong)plan.Manifest.Seed ^ 0xFA11_11B5UL));
        foreach (var r in plan.Hydrology.Reaches)
        {
            if (r.Distributary || r.Points.Length < 2 || r.Along.Length != r.Points.Length) continue;
            int n = r.Points.Length;
            for (int k = 0; k + 1 < n; k++)
            {
                double drop = r.Surface[k] - r.Surface[k + 1], length = r.Along[k + 1] - r.Along[k];
                if (drop < 3 || length <= 0 || drop / length < .2) continue;
                double width = r.Width[k], upstream = Math.Max(25, width * .8);
                int a = k; while (a > 0 && r.Along[k] - r.Along[a] < upstream + 15) a--;
                int b = Math.Min(n - 1, k + 1);
                int count = b - a + 1;
                var pts = new Point2[count]; var along = new double[count];
                double minX = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxZ = double.MinValue;
                for (int i = 0; i < count; i++)
                {
                    pts[i] = r.Points[a + i]; along[i] = r.Along[a + i] - r.Along[k];
                    minX = Math.Min(minX, pts[i].X); maxX = Math.Max(maxX, pts[i].X); minZ = Math.Min(minZ, pts[i].Z); maxZ = Math.Max(maxZ, pts[i].Z);
                }
                // (revision 44: the river's water on the slope of a tall drop reaches well out to the sides and past the foot)
                double reach = Follow ? Math.Max(Reach(width), width * .5 + length + 30) : Reach(width);
                _lips.Add(new Lip(pts, along, width, r.Surface[k], length, upstream, minX - reach, minZ - reach, maxX + reach, maxZ + reach, rng.Next(),
                    r.Surface[k + 1], r.Id));
            }
        }
        for (int i = 0; i < _lips.Count; i++)
        {
            var l = _lips[i];
            for (int gx = Cell(l.MinX); gx <= Cell(l.MaxX); gx++)
                for (int gz = Cell(l.MinZ); gz <= Cell(l.MaxZ); gz++)
                {
                    if (!_grid.TryGetValue((gx, gz), out var list)) _grid[(gx, gz)] = list = new List<int>();
                    list.Add(i);
                }
        }
    }

    private static int Cell(double v) => (int)Math.Floor(v / GridCell);

    /// <summary>How far a point lies past the lip straight down the fall (the lip's point, toward the foot's) and to its side.</summary>
    private static void Straight(Lip l, double x, double z, out double along, out double side)
    {
        int k = Array.FindIndex(l.Along, a => a >= 0);
        var p = l.Points[k]; var q = l.Points[Math.Min(l.Points.Length - 1, k + 1)];
        double fx = q.X - p.X, fz = q.Z - p.Z, fl = Math.Max(1e-6, Math.Sqrt(fx * fx + fz * fz));
        along = ((x - p.X) * fx + (z - p.Z) * fz) / fl;
        side = ((x - p.X) * fz - (z - p.Z) * fx) / fl;
    }

    /// <summary>Revision 44: the water's level along the lip's stretch (<paramref name="along"/> past the lip, negative upstream): the
    /// lip's at the lip, easing to the river's own (<paramref name="river"/>; NaN: the lip's less the sag it would have) at the
    /// stretch's upstream end — level to the edge, without a step where the stretch begins.</summary>
    private static double LipLevel(Lip l, double along, double river)
    {
        double toLip = 1 - DetMath.Smooth(-along / l.Upstream);
        return double.IsNaN(river) ? l.Top - (1 - toLip) * .5 : river + (l.Top - river) * toLip;
    }

    /// <summary>Revision 44: how far past the river's sides the lip's water reaches (a quarter of the width, 1–4 m: 4 m doubled
    /// an 8 m stream's falling water).</summary>
    private static double Spill(double width) => Math.Clamp(width * .25, 1, 4);

    /// <summary>The plunge pool's half-width: a little wider than the river.</summary>
    private static double PoolHalf(double width) => width * .55 + 3;

    /// <summary>Distance to the river's line near a lip and how far along it from the lip (negative upstream).</summary>
    private static void Locate(Lip l, double x, double z, out double d, out double along)
    {
        double best = double.MaxValue; along = 0;
        for (int s = 0; s + 1 < l.Points.Length; s++)
        {
            var p = l.Points[s]; var q = l.Points[s + 1];
            double sx = q.X - p.X, sz = q.Z - p.Z, l2 = sx * sx + sz * sz;
            double t = l2 < 1e-9 ? 0 : ((x - p.X) * sx + (z - p.Z) * sz) / l2;
            // (past the last point, on along the river's last direction: the pool's end fades there)
            double tc = s + 2 == l.Points.Length ? Math.Max(0, t) : Math.Clamp(t, 0, 1);
            if (s + 2 != l.Points.Length || t <= 1) tc = Math.Clamp(t, 0, 1);
            double ex = p.X + sx * Math.Clamp(t, 0, 1) - x, ez = p.Z + sz * Math.Clamp(t, 0, 1) - z;
            double e2 = ex * ex + ez * ez;
            if (s + 2 == l.Points.Length && t > 1)
            {
                // beyond the foot: across from the extended line, along beyond the foot
                double len = Math.Sqrt(l2), ux = sx / len, uz = sz / len, px = x - q.X, pz = z - q.Z;
                double ahead = px * ux + pz * uz, side = Math.Abs(px * uz - pz * ux);
                e2 = side * side;
                if (e2 < best) { best = e2; along = l.Along[s + 1] + ahead; }
                continue;
            }
            if (e2 < best) { best = e2; along = l.Along[s] + (l.Along[s + 1] - l.Along[s]) * tc; }
        }
        d = Math.Sqrt(best);
    }

    /// <summary>
    /// Revision 43 (D300): the rock face at a lip and the plunge pool below it. From just past the lip to the fall's foot,
    /// across the pool's width, the ground drops sheer to the pool's floor (2–8 m under the foot's water, a bowl), only ever
    /// lowered; the pool fills to the foot's level (<paramref name="level"/>, absolute, NaN outside). <paramref name="rock"/>:
    /// how bare (the face and the walls beside the pool).
    /// </summary>
    /// <remarks>Revision 44 (D301, <see cref="Follow"/>): <paramref name="riverLevel"/> and <paramref name="riverBody"/> are the
    /// river's water here (absolute; NaN, -1 for none); where it lies on a fall's slope (<paramref name="ramp"/>) the level
    /// returned replaces it: the lip's above the face, the pool's below it (the ground lowered to the pool's floor).</remarks>
    public double Pool(double x, double z, double height, double sea, out double level, out int reach, out double rock)
        => Pool(x, z, height, sea, double.NaN, -1, false, out level, out reach, out rock, out _);

    /// <param name="dry">No water here yet (revision 44: within the river's width along the lip's stretch the channel fills to
    /// the lip's level).</param>
    public double Pool(double x, double z, double height, double sea, double riverLevel, int riverBody, bool dry, out double level, out int reach,
        out double rock, out bool ramp)
    {
        level = double.NaN; reach = -1; rock = 0; ramp = false;
        if (!Faces || !_grid.TryGetValue((Cell(x), Cell(z)), out var list)) return height;
        double result = height;
        foreach (int i in list)
        {
            var l = _lips[i];
            if (x < l.MinX || x > l.MaxX || z < l.MinZ || z > l.MaxZ) continue;
            Locate(l, x, z, out double d, out double along);
            double half = PoolHalf(l.Width) * (1 + .08 * Noise.Fbm(x, z, 30, 2, 0, l.Seed + 3));
            // Revision 44: the river's water on this fall's slope (its level between the lip's and the foot's).
            // (past the foot only half a metre or more over the pool: the river's surface eases down to the pool's level there)
            bool onSlope = false;
            if (Follow && dry && height < l.Top - sea - .05)
            {
                // (dry ground in the channel below the water's level along the lip's stretch: the water's edge hung over it — the
                // banks beyond rise over it, D297)
                Straight(l, x, z, out double sa, out double ss);
                if (sa >= -l.Upstream * .5 && sa < .2 && Math.Abs(ss) <= l.Width * .5 + Spill(l.Width) && height < LipLevel(l, sa, double.NaN) - sea - .05)
                { level = LipLevel(l, sa, double.NaN); reach = l.Reach; ramp = true; continue; }
            }
            if (Follow && riverBody == l.Reach && riverLevel < l.Top - .05)
            {
                // (how far past the lip straight down the fall: beside the lip the river's line bends away — along it, water beside
                // the face counted as upstream)
                Straight(l, x, z, out double straight, out double side);
                onSlope = riverLevel > l.Bottom + (straight > l.Length ? .5 : .05);
                // (upstream of the lip's stretch, as before)
                if (onSlope && straight < -l.Upstream) continue;
                if (onSlope && straight < .2)
                {
                    // Above the face: within the river's width the lip's water, level to the edge (the river's surface sagged up to
                    // 4 m toward the sides there, blended with the drop's; the ground under it lies below it already); beyond it
                    // dry, the banks of D297 rising there over the lip's water (the sagging water at the lip's ends lay below dry
                    // ground beside it, and lifted it hung over it — dump 112818).
                    if (Math.Abs(side) <= l.Width * .5 + Spill(l.Width)) { level = LipLevel(l, straight, riverLevel); reach = l.Reach; }
                    ramp = true;
                    continue;
                }
                if (onSlope) along = Math.Max(along, straight);
            }
            // Sheer: the face rises over half a metre past the lip, the walls over 2 m at the pool's sides; the pool ends at the
            // foot, shoaling over 8 m into the river below.
            double inPool = DetMath.Smooth((along - .2) / .6) * (1 - DetMath.Smooth((d - half) / 2)) * (1 - DetMath.Smooth((along - l.Length) / 8));
            if (onSlope) inPool = 1; // (revision 44: the pool takes in all of the slope's water past the face)
            if (inPool <= 0) continue;
            double depth = Math.Clamp((l.Top - l.Bottom) * .15, 2, 8);
            double bowl = 1 - (d / half) * (d / half) * .6;
            double floor = l.Bottom - sea - depth * Math.Max(.25, bowl) * (1 - DetMath.Smooth((along - l.Length + 4) / 8));
            if (onSlope)
            {
                // (as deep as the pool at its edge, shoaling past the foot alike: at 0.6 m the bed showed bright and the water ran
                // white beside the pool's dark water, along a straight line — dump 112829)
                double edgeBowl = 1 - Math.Min(1, (d / half) * (d / half)) * .6;
                floor = Math.Min(floor, l.Bottom - sea - Math.Max(.6, depth * edgeBowl * (1 - DetMath.Smooth((along - l.Length + 4) / 8))));
                ramp = true;
            }
            double lowered = height + (Math.Min(height, floor) - height) * inPool;
            if (lowered < result) result = lowered;
            if (inPool > .5 && result < l.Bottom - sea - .05) { level = l.Bottom; reach = l.Reach; }
            // Bare rock on the face and the walls (where the ground drops away steeply).
            rock = Math.Max(rock, DetMath.Smooth((height - result) / 3) * .9);
        }
        return result;
    }
    private static double Inner(double width) => width * .5 + Hydrology.Bank(width) * .6;
    private static double Band(double width) => 12 + width * .25;
    private static double Reach(double width) => Inner(width) + Band(width) * 1.5;

    /// <summary>
    /// Raises dry ground (metres above the sea) beside fall lips to the sill's height; <paramref name="rock"/> how bare it is
    /// there (0 … 1). <paramref name="sea"/>: the sea level (lip levels are absolute).
    /// </summary>
    public double Apply(double x, double z, double height, double sea, out double rock)
    {
        rock = 0;
        if (!_grid.TryGetValue((Cell(x), Cell(z)), out var list)) return height;
        double result = height;
        foreach (int i in list)
        {
            var l = _lips[i];
            if (x < l.MinX || x > l.MaxX || z < l.MinZ || z > l.MaxZ) continue;
            // Distance to the river's line near the lip and how far along it (negative upstream of the lip).
            double best = double.MaxValue, along = 0;
            for (int s = 0; s + 1 < l.Points.Length; s++)
            {
                var p = l.Points[s]; var q = l.Points[s + 1];
                double sx = q.X - p.X, sz = q.Z - p.Z, l2 = sx * sx + sz * sz;
                double t = l2 < 1e-9 ? 0 : Math.Clamp(((x - p.X) * sx + (z - p.Z) * sz) / l2, 0, 1);
                double ex = p.X + sx * t - x, ez = p.Z + sz * t - z, d2 = ex * ex + ez * ez;
                if (d2 < best) { best = d2; along = l.Along[s] + (l.Along[s + 1] - l.Along[s]) * t; }
            }
            double d = Math.Sqrt(best), w = l.Width, inner = Inner(w), band = Band(w);
            // Revision 44 (D301): near the lip in the fall's straight frame (beside the lip the river's line bends away: dry ground
            // beside the lip below its water counted as far down the fall, and stayed low — dump 112818).
            if (Follow)
            {
                Straight(l, x, z, out double sa, out double ss);
                if (sa > -l.Upstream - 10 && sa < l.Length) { along = sa; d = Math.Abs(ss); }
            }
            // Out of the channel (rising from its edge over the bank), fading out beyond the band.
            double across = DetMath.Smooth((d - w * .5 - .5) / Math.Max(2, inner - w * .5)) * (1 - DetMath.Smooth((d - inner - band * .5) / band));
            // (revision 44: beside the lip's stretch full height where the lip's water ends, a quarter of the width past the river's
            // sides, 1–4 m)
            if (Follow && along < .2) across = DetMath.Smooth((d - w * .5 - Spill(w) * .25) / (Spill(w) * .75)) * (1 - DetMath.Smooth((d - inner - band * .5) / band));
            // From the upstream end to the lip, on down the fall's sides, fading out toward its foot.
            double alongW = DetMath.Smooth((along + l.Upstream) / (l.Upstream * .6)) * (1 - DetMath.Smooth((along - l.Length * .6) / (l.Length * .4 + 4)));
            double weight = across * alongW;
            if (weight <= 0) continue;
            double rise = 1.8 + 1.4 * DetMath.Smooth(.5 + 1.5 * Noise.Fbm(x, z, 40, 2, 0, l.Seed));
            double target = l.Top - sea + rise;
            if (target <= result) continue;
            result += (target - result) * weight;
            rock = Math.Max(rock, weight * .85);
        }
        return result;
    }
}
