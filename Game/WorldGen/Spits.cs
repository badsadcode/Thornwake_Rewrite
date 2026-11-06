using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.WorldGen;

/// <summary>
/// Spits, sandbars and lagoons (landform no. 11, terrain revision 37, D271; docs/LANDFORMS.md): on flat coasts with a gentle
/// sea floor, sand laid on the sea bed just above the water, the water behind it left sea but shallow (a lagoon by its
/// depth, for now; the water rework can calm it). Three kinds: a spit growing from the shore along the coast, curling back
/// at its tip, a bay behind it; a barrier of two spits meeting, an inlet left between them, a lagoon behind; a sandbar
/// offshore, broken up, mostly awash. Placed once in the plan, well away from river mouths, lakes, headlands, fjord coasts
/// and volcanoes. A ridge's place is measured from the plan's coastline (its land value over its gradient, smooth: the fine
/// wiggle of the coast would have bent it at every scale), so it runs along the coast without a list of its points. Only
/// the sea stage changes: land stays land.
/// </summary>
public sealed class Spits
{
    public enum Kind { Spit, Barrier, Bar }

    /// <param name="Tx">Along the coast (unit): a spit grows this way from its root at (X, Z); a barrier and a bar have their middle there.</param>
    private readonly record struct Site(Kind Kind, double X, double Z, double Tx, double Tz, double Length, double Off, double Width, double Crest, double Lagoon, double Gap, double GapWidth, ulong Seed);

    private const double GridCell = 8000;
    private readonly List<Site> _sites = new();
    private readonly double _minX, _minZ;
    private readonly int _gridSize;
    private readonly int[] _gridStart, _gridItems;

    public int Count => _sites.Count;
    /// <summary>Coast cells that met the rule (tests, tuning).</summary>
    public int Candidates { get; }

    /// <summary>Each site's root or middle, kind, length and way along the coast (tests, captures).</summary>
    public IEnumerable<(Kind Kind, double X, double Z, double Tx, double Tz, double Length, double Off)> Probes()
    {
        foreach (var s in _sites) yield return (s.Kind, s.X, s.Z, s.Tx, s.Tz, s.Length, s.Off);
    }

    public Spits(WorldPlan plan, LandformSetting setting)
    {
        var macro = plan.Macro; var hy = plan.Hydrology; double oceanDepth = plan.Profile.OceanDepth;
        var rng = new RandomStream(LandformCatalog.Seed(plan.Manifest, LandformCatalog.Spits));
        var land = macro.Channel(WorldPlan.LandChannel);
        int n = macro.Size;
        var away = new List<(double X, double Z, double Radius)>();
        foreach (var reach in hy.Reaches) if (reach.EndsInSea) away.Add((reach.Points[^1].X, reach.Points[^1].Z, 1500));
        foreach (var lake in hy.Lakes) away.Add((lake.Center.X, lake.Center.Z, 2 * Math.Sqrt(lake.Area / Math.PI) + 1500));
        if (plan.Troughs != null) away.AddRange(plan.Troughs.Coasts());
        if (plan.PlanLandforms.OfType<Volcanoes>().FirstOrDefault() is { } volcanoes)
            foreach (var (x, z, r, _, _) in volcanoes.Cones()) away.Add((x, z, r * 1.3));
        if (plan.Headlands != null) foreach (var (x, z, _, _, r) in plan.Headlands.Probes()) away.Add((x, z, r + 500));
        // Flat coasts with a gentle sea floor: low land behind, and under 40 m of water 1.5 km out.
        bool Gentle(double x, double z, out double dx, out double dz, out double g)
        {
            macro.SampleGradient(WorldPlan.LandChannel, x, z, out double gx, out double gz);
            g = Math.Sqrt(gx * gx + gz * gz);
            dx = dz = 0;
            if (g < 1e-12) return false;
            dx = -gx / g; dz = -gz / g;
            return oceanDepth * DetMath.Smooth(g * 1500 / .6) < 40;
        }
        var candidates = new List<(double X, double Z, double Dx, double Dz)>();
        for (int j = 1; j < n - 1; j++)
            for (int i = 1; i < n - 1; i++)
            {
                int k = j * n + i;
                if (land[k] <= 0 || (land[k - 1] > 0 && land[k + 1] > 0 && land[k - n] > 0 && land[k + n] > 0)) continue;
                double x = macro.CellX(i), z = macro.CellZ(j);
                if (!Gentle(x, z, out double dx, out double dz, out _)) continue;
                if (macro.Sample(WorldPlan.HeightChannel, x - dx * 1500, z - dz * 1500) > 110) continue;
                candidates.Add((x, z, dx, dz));
            }
        Candidates = candidates.Count;
        int want = Math.Clamp((int)Math.Round(setting.Amount * candidates.Count * .05), candidates.Count > 0 ? 1 : 0, 70);
        for (int attempt = 0; attempt < want * 12 && _sites.Count < want && candidates.Count > 0; attempt++)
        {
            var c = candidates[rng.Int(candidates.Count)];
            double roll = rng.Unit(), side = rng.Unit() < .5 ? -1 : 1;
            var kind = roll < .45 ? Kind.Spit : roll < .8 ? Kind.Barrier : Kind.Bar;
            double length = rng.Between(1800, 5500) * setting.Size, off = rng.Between(380, 850) * setting.Size, width = rng.Between(45, 95) * setting.Size;
            double crest = kind == Kind.Bar ? rng.Between(.2, .7) : rng.Between(1.3, 2.3), lagoon = rng.Between(1.2, 3.2);
            double gap = rng.Between(-.4, .4) * length, gapWidth = rng.Between(90, 240);
            ulong seed = rng.Next();
            double tx = -c.Dz * side, tz = c.Dx * side;
            double t0 = kind == Kind.Spit ? -300 : -length - 300, t1 = length + 300;
            bool clear = true;
            foreach (var (ax, az, ar) in away) if (Math.Sqrt((ax - c.X) * (ax - c.X) + (az - c.Z) * (az - c.Z)) < ar + length + 500) { clear = false; break; }
            foreach (var s in _sites) if (Math.Sqrt((s.X - c.X) * (s.X - c.X) + (s.Z - c.Z) * (s.Z - c.Z)) < s.Length + length + 2500) { clear = false; break; }
            // The coast runs straight enough, with the same gentle sea, all along: no ridge across a headland or a bend.
            for (double t = t0; t <= t1 && clear; t += 400)
            {
                double px = c.X + tx * t + c.Dx * off, pz = c.Z + tz * t + c.Dz * off;
                if (!Gentle(px, pz, out double ux, out double uz, out double g) || ux * c.Dx + uz * c.Dz < .8) { clear = false; break; }
                double d = -macro.Sample(WorldPlan.LandChannel, px, pz) / g;
                if (d < off * .45 || d > off * 1.7) clear = false;
            }
            if (clear) _sites.Add(new Site(kind, c.X, c.Z, tx, tz, length, off, width, crest, lagoon, gap, gapWidth, seed));
        }
        _minX = macro.MinX; _minZ = macro.MinZ;
        _gridSize = Math.Max(1, (int)Math.Ceiling(macro.Size * macro.Cell / GridCell));
        var lists = new List<int>[_gridSize * _gridSize];
        for (int s = 0; s < _sites.Count; s++)
        {
            var st = _sites[s];
            double reach = st.Length + st.Off + 400;
            for (int j = Cell(st.Z - reach, _minZ); j <= Cell(st.Z + reach, _minZ); j++)
                for (int i = Cell(st.X - reach, _minX); i <= Cell(st.X + reach, _minX); i++)
                    (lists[j * _gridSize + i] ??= new List<int>()).Add(s);
        }
        _gridStart = new int[lists.Length + 1];
        var items = new List<int>();
        for (int c = 0; c < lists.Length; c++) { _gridStart[c] = items.Count; if (lists[c] != null) items.AddRange(lists[c]); }
        _gridStart[lists.Length] = items.Count;
        _gridItems = items.ToArray();
    }

    /// <summary>
    /// The sea bed here (metres above the sea in and out) with the sand on it. <paramref name="distance"/>: metres out from
    /// the plan's coastline.
    /// </summary>
    public double Seabed(double x, double z, double distance, double bed)
    {
        if (_sites.Count == 0) return bed;
        int c = Cell(z, _minZ) * _gridSize + Cell(x, _minX);
        for (int k = _gridStart[c]; k < _gridStart[c + 1]; k++)
        {
            var s = _sites[_gridItems[k]];
            double dx = x - s.X, dz = z - s.Z, t = dx * s.Tx + dz * s.Tz;
            // How far along (0 at the root or the near end, 1 at the tip or far end), and its line out from the coast.
            double u = s.Kind == Kind.Spit ? t / s.Length : (t + s.Length) / (2 * s.Length);
            if (u < -.05 || u > 1.05) continue;
            double along = Noise.Fbm(t, 3.5, 900, 2, 0, s.Seed);
            double off, width = s.Width * (1 + .35 * Noise.Fbm(t, 8.5, 260, 2, 0, s.Seed + 1)), crest = s.Crest;
            bool lagoon;
            switch (s.Kind)
            {
                case Kind.Spit:
                    // Out from the shore over its first third, curling back toward it at its tip, thinner toward the tip.
                    off = s.Off * (DetMath.Smooth(u / .35) * (1 - .6 * DetMath.Smooth((u - .84) / .16)) + .16 * along);
                    width *= 1 - .45 * DetMath.Clamp(u, 0, 1);
                    crest *= DetMath.Smooth((1.02 - u) / .05);
                    lagoon = true;
                    break;
                case Kind.Barrier:
                    // Joined to the shore at both ends, an inlet left between the two spits.
                    off = s.Off * (DetMath.Smooth(u / .28) * DetMath.Smooth((1 - u) / .28) + .13 * along);
                    crest = -2.6 + (crest + 2.6) * DetMath.Smooth((Math.Abs(t - s.Gap) - s.GapWidth) / 70);
                    lagoon = true;
                    break;
                default:
                    // Offshore, ending in the water both ways, broken into shoals.
                    off = s.Off * (1.1 + .22 * along);
                    width *= 1.3;
                    crest = -2.2 + (crest + 2.2) * DetMath.Smooth(.55 + 1.5 * Noise.Fbm(t, 5.5, 480, 2, 0, s.Seed + 2)) * DetMath.Smooth(Math.Min(u, 1 - u) / .14);
                    lagoon = false;
                    break;
            }
            double ends = DetMath.Smooth(Math.Min(u, 1 - u) / .05 + 1);
            // Behind the ridge the sea floor rises to a shallow lagoon; the bay behind a spit opens toward its tip.
            if (lagoon)
            {
                double behind = DetMath.Smooth((off - distance) / 70) * DetMath.Smooth(u / .12) * (s.Kind == Kind.Spit ? 1 - DetMath.Smooth((u - .8) / .25) : DetMath.Smooth((1 - u) / .12));
                double floor = -s.Lagoon * (.75 + .35 * Noise.Fbm(x, z, 300, 2, 0, s.Seed + 3)) * DetMath.Smooth(distance / 90 + .25);
                if (behind > 0 && floor > bed) bed += (floor - bed) * behind;
            }
            // The ridge: a low crest with small dunes, its sides falling away under the water.
            double across = Math.Abs(distance - off);
            if (across < width * 2.4 && width > 1)
            {
                double top = crest > 0 ? crest * (1 + .55 * Noise.Fbm(x, z, 90, 2, 0, s.Seed + 4)) : crest;
                double ridge = top - (top + 4.5) * DetMath.Smooth(across / (width * 2.1));
                bed = Math.Max(bed, ridge - (1 - ends) * 6);
            }
        }
        return bed;
    }

    private int Cell(double v, double min) => Math.Clamp((int)Math.Floor((v - min) / GridCell), 0, _gridSize - 1);
}
