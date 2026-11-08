using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.WorldGen;

/// <summary>
/// Headlands and coves (landform no. 10, terrain revision 36, D270; docs/LANDFORMS.md): along stretches of coast, rocky
/// points reaching a few hundred metres out to sea with cliffs round them, and rounder coves between them, their ground
/// let down to a low beach. Placed once in the plan on coast cells well away from rivers, lakes, fjord coasts and
/// volcanoes (a river's mouth stays where the plan put it: water wins). The coastline is moved where it is detailed
/// (<see cref="TerrainSampler"/>'s coast stage), through the land's gradient like its fine wiggle: along each stretch a
/// noise over the coast's own direction says point or cove, so points reach straight out. Sea stacks stand off some of
/// the points (<see cref="Points"/>).
/// </summary>
public sealed class Headlands
{
    private readonly record struct Stretch(double X, double Z, double Dx, double Dz, double Radius, double Out, double In, double Wave, double Rise, ulong Seed);

    private const double GridCell = 8000;
    private readonly List<Stretch> _stretches = new();
    private readonly double _minX, _minZ;
    private readonly int _gridSize;
    private readonly int[] _gridStart, _gridItems;

    /// <summary>Number of stretches of coast with headlands and coves in this world.</summary>
    public int Count => _stretches.Count;

    /// <summary>Each stretch's middle, its way out to sea and radius (tests, captures).</summary>
    public IEnumerable<(double X, double Z, double Dx, double Dz, double Radius)> Probes()
    {
        foreach (var s in _stretches) yield return (s.X, s.Z, s.Dx, s.Dz, s.Radius);
    }

    /// <summary>Where each stretch's noise makes a point (near the plan's coast, its way out to sea), for the sea stacks.</summary>
    public IEnumerable<(double X, double Z, double Dx, double Dz, ulong Seed)> Points()
    {
        foreach (var s in _stretches)
        {
            double tx = -s.Dz, tz = s.Dx, last = -1;
            for (double t = -s.Radius * .7; t <= s.Radius * .7; t += 100)
            {
                double n = Pattern(s, t), next = Pattern(s, t + 100);
                if (n > .2 && n >= last && n > next) yield return (s.X + tx * t, s.Z + tz * t, s.Dx, s.Dz, Hashing.Mix(s.Seed + (ulong)(long)(t + 100000)));
                last = n;
            }
        }
    }

    public Headlands(WorldPlan plan, LandformSetting setting)
    {
        var macro = plan.Macro; var hy = plan.Hydrology;
        var rng = new RandomStream(LandformCatalog.Seed(plan.Manifest, LandformCatalog.Headlands));
        var land = macro.Channel(WorldPlan.LandChannel);
        int n = macro.Size;
        var coast = new List<int>();
        for (int j = 1; j < n - 1; j++)
            for (int i = 1; i < n - 1; i++)
            {
                int k = j * n + i;
                if (land[k] > 0 && (land[k - 1] <= 0 || land[k + 1] <= 0 || land[k - n] <= 0 || land[k + n] <= 0)) coast.Add(k);
            }
        var away = new List<(double X, double Z, double Radius)>();
        if (plan.Troughs != null) away.AddRange(plan.Troughs.Coasts());
        if (plan.PlanLandforms.OfType<Volcanoes>().FirstOrDefault() is { } volcanoes)
            foreach (var (x, z, r, _, _) in volcanoes.Cones()) away.Add((x, z, r * 1.3));
        // (every mouth by itself: a coast curves, and on an island one lay inside a stretch far from its straight line)
        foreach (var reach in hy.Reaches)
            if (reach.EndsInSea) away.Add((reach.Points[^1].X, reach.Points[^1].Z, 900));
        // (and every lake by itself, twice its round radius: a stretch raised and moved the ground round a lake by the coast, and
        // the lake's water was gone from a spot of Tom's dump 182351)
        foreach (var lake in hy.Lakes) away.Add((lake.Center.X, lake.Center.Z, 2 * Math.Sqrt(lake.Area / Math.PI) + 1500));
        int want = Math.Clamp((int)Math.Round(setting.Amount * coast.Count * macro.Cell / 45000), coast.Count > 0 ? 1 : 0, 160);
        for (int attempt = 0; attempt < want * 12 && _stretches.Count < want && coast.Count > 0; attempt++)
        {
            int k = coast[rng.Int(coast.Count)];
            double x = macro.CellX(k % n), z = macro.CellZ(k / n);
            double radius = rng.Between(4000, 10000) * setting.Size, wave = rng.Between(1400, 2800) * setting.Size;
            double outward = rng.Between(280, 700) * setting.Size, inward = rng.Between(160, 420) * setting.Size, rise = rng.Between(18, 60) * setting.Size;
            ulong seed = rng.Next();
            macro.SampleGradient(WorldPlan.LandChannel, x, z, out double gx, out double gz);
            double g = Math.Sqrt(gx * gx + gz * gz);
            if (g < 1e-12) continue;
            double dx = -gx / g, dz = -gz / g;
            bool clear = true;
            foreach (var (ax, az, ar) in away) if (Math.Sqrt((ax - x) * (ax - x) + (az - z) * (az - z)) < ar + radius) { clear = false; break; }
            foreach (var s in _stretches) if (Math.Sqrt((s.X - x) * (s.X - x) + (s.Z - z) * (s.Z - z)) < s.Radius + radius + 3000) { clear = false; break; }
            // No river or lake within the stretch's reach of the coast: their mouths and shores stay where the plan has them.
            for (double t = -radius; t <= radius && clear; t += Math.Max(1000, radius / 4))
                foreach (double o in new[] { -1000.0, 0, 1000 })
                {
                    double px = x - dz * t + dx * o, pz = z + dx * t + dz * o;
                    if (hy.NearestRiver(px, pz, 1600, out _) || hy.LakeNear(px, pz) >= 0) { clear = false; break; }
                }
            if (clear) _stretches.Add(new Stretch(x, z, dx, dz, radius, outward, inward, wave, rise, seed));
        }
        _minX = macro.MinX; _minZ = macro.MinZ;
        _gridSize = Math.Max(1, (int)Math.Ceiling(macro.Size * macro.Cell / GridCell));
        var lists = new List<int>[_gridSize * _gridSize];
        for (int s = 0; s < _stretches.Count; s++)
        {
            var st = _stretches[s];
            for (int j = Cell(st.Z - st.Radius, _minZ); j <= Cell(st.Z + st.Radius, _minZ); j++)
                for (int i = Cell(st.X - st.Radius, _minX); i <= Cell(st.X + st.Radius, _minX); i++)
                    (lists[j * _gridSize + i] ??= new List<int>()).Add(s);
        }
        _gridStart = new int[lists.Length + 1];
        var items = new List<int>();
        for (int c = 0; c < lists.Length; c++) { _gridStart[c] = items.Count; if (lists[c] != null) items.AddRange(lists[c]); }
        _gridStart[lists.Length] = items.Count;
        _gridItems = items.ToArray();
    }

    /// <summary>Point (above 0) or cove (below) at a position along the coast.</summary>
    private static double Pattern(in Stretch s, double t) => Noise.Fbm(t, 11.5, s.Wave, 2, 0, s.Seed);

    /// <summary>
    /// How far the coastline moves here (metres, out to sea above 0). <paramref name="point"/>: how high this point's rock
    /// stands (metres, 0 off the points); <paramref name="cove"/>: how much of a cove this is (0 … 1). Points narrow, coves
    /// wider and rounder; everything fades out toward the stretch's ends.
    /// </summary>
    public double Shift(double x, double z, out double point, out double cove)
    {
        point = 0; cove = 0;
        if (_stretches.Count == 0) return 0;
        int c = Cell(z, _minZ) * _gridSize + Cell(x, _minX);
        double total = 0;
        for (int k = _gridStart[c]; k < _gridStart[c + 1]; k++)
        {
            var s = _stretches[_gridItems[k]];
            double dx = x - s.X, dz = z - s.Z, d2 = dx * dx + dz * dz;
            if (d2 >= s.Radius * s.Radius) continue;
            double w = 1 - DetMath.Smooth((Math.Sqrt(d2) - .5 * s.Radius) / (.5 * s.Radius));
            if (w <= 0) continue;
            double n = Pattern(s, dx * -s.Dz + dz * s.Dx);
            double p = DetMath.Smooth((n - .1) / .34), q = DetMath.Smooth((-n + .04) / .3);
            total += w * (s.Out * p - s.In * q);
            point = Math.Max(point, w * p * s.Rise);
            cove = Math.Max(cove, w * q);
        }
        return total;
    }

    /// <summary>
    /// The land's height there (metres above the sea in and out). A point is rock: a low shelf at the waterline, cliffs on a
    /// jagged line rising to its height, ridged crags on top, bare in patches (D270, Tom: rougher; with gentle knobs the points
    /// were grassy domes with a cliff at the foot — the land's own walking-scale relief fades out toward the shore). A cove's
    /// ground is let down to a low, gently rising beach, back to the land's own height ~500 m in.
    /// <paramref name="coast"/>: metres from the detailed coastline. <paramref name="rock"/>: how bare.
    /// </summary>
    /// <param name="rounded">Revision 38 (D274, Tom: the cliffs' "top edge, the sharp edge, doesn't look all that good"): the
    /// cliff steep, then rolling over into the top in a tight, rough, bare shoulder (over 0.25·H + 5 m) — before, it reached its
    /// full height within ~8 m of the shore and stopped there in a knife edge.</param>
    /// <param name="moved">How far the stretches moved the coastline here (m): with <paramref name="rounded"/>, wherever they
    /// moved it the land comes down to the sea in the same rounded cliff — moved out, the land's height rises with the land
    /// value, very fast (on the points it stood in sheer walls 30 m high within 18 m of the sea); moved in, the sea cut into
    /// land tens of metres high (the walls of Tom's dumps 214200 and 214234).</param>
    public static double Relief(double x, double z, double minWavelength, double ground, double point, double cove, double coast, out double rock,
        bool rounded = false, double moved = 0)
    {
        rock = 0;
        double shoulder = 0;
        if (rounded && moved != 0 && ground > 3 && !double.IsInfinity(coast))
        {
            // (a tight shoulder, rough and bare: over 0.6·H + 8 m the roll-over was a smooth grassy ramp that broke the cliff —
            // Tom: "way too smooth ... while there are still sharp edges"; and full as soon as the coast moved 15 m, so it is
            // the same along the whole stretch)
            double run = .25 * ground + 5, u = DetMath.Clamp((coast - 1.5) / run, 0, 1), v = 1 - u;
            double rough = (Noise.Ridged(x, z, 18, 3, minWavelength, 0x4EB0UL, .5) - .45) * Math.Min(6, .12 * ground);
            double cap = 2.5 * DetMath.Smooth(coast / 5) + (ground - 2.5) * (1 - v * v * v) + rough * 4 * u * v;
            double k = DetMath.Smooth(Math.Abs(moved) / 15);
            ground += (Math.Min(ground, cap) - ground) * k;
            shoulder = k * (1 - DetMath.Smooth((coast - run) / 6));
        }
        if (point > 0)
        {
            double c = double.IsInfinity(coast) ? 1000 : coast;
            double line = c + 28 * Noise.Fbm(x, z, 75, 2, minWavelength, 0x4EAEUL);
            double crag = Noise.Ridged(x, z, 240, 5, minWavelength, 0x4EADUL, .5) - .3;
            double cliff = DetMath.Smooth(line / 35);
            double top = point * (.8 + 1.1 * crag) * cliff * (.7 + .3 * DetMath.Smooth(c / 300));
            if (rounded)
            {
                // h = top · (1 − (1 − u)³), u over a run growing with the height: ~75° at the foot, ~60° a third of the way
                // across, rolling over to level at the top (a convex shoulder, no lip).
                double run = .25 * Math.Max(top, 0) + 5, u = DetMath.Clamp((c - 1.5) / run, 0, 1), v = 1 - u;
                ground = Math.Max(ground, 2.5 * DetMath.Smooth(c / 5) + top * (1 - v * v * v));
            }
            else ground = Math.Max(ground, 2.5 * DetMath.Smooth(c / 5) + DetMath.Smooth(c / 8) * top);
            // Bare: the cliff band, and patches over the top (more on the higher crags).
            double strength = Math.Min(1, point / 12);
            double patches = DetMath.Smooth((crag + .1 + .35 * Noise.Fbm(x, z, 110, 2, minWavelength, 0x4EAFUL)) / .3);
            rock = strength * Math.Max(.85 * (1 - DetMath.Smooth((line - 45) / 45)), .75 * patches * (1 - DetMath.Smooth((c - 420) / 200)));
        }
        rock = Math.Max(rock, .8 * shoulder);
        if (cove > 0 && !double.IsInfinity(coast))
        {
            double beach = 1.2 + .03 * coast, k = cove * (1 - DetMath.Smooth((coast - 180) / 340));
            if (beach < ground) ground += (beach - ground) * k;
        }
        return ground;
    }

    private int Cell(double v, double min) => Math.Clamp((int)Math.Floor((v - min) / GridCell), 0, _gridSize - 1);
}
