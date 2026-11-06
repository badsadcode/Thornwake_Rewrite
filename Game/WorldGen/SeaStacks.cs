using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

/// <summary>
/// Sea stacks and arches (landform no. 9, terrain revision 35, D266; docs/LANDFORMS.md): pillars of rock and arches
/// standing in the sea off steep coasts open to the sea — what is left of a headland the waves have cut back. The first
/// landform that is not ground but rock models: the generator only says where each one stands and how big it is (the
/// terrain is unchanged); the game builds and draws the rock (SeaStackView). Placed once from the terrain itself
/// (<see cref="TerrainSampler.SeaStacks"/>): coast cells of the plan where the land rises 60 m or more within 1.5 km and
/// the sea lies open beyond (nine tenths of the 20 km in front), groups of 2–7 stacks 40–350 m out, about as high as the
/// cliff behind (now and then an arch, its opening to the sea), each on a seabed 1.5–30 m deep with sea all round it.
/// Water wins: nothing stands on land or in a lake.
/// </summary>
public sealed class SeaStacks
{
    public enum Kind { Stack, Arch }

    /// <param name="X">Middle (for an arch, between its legs).</param>
    /// <param name="Height">Top above the sea (m).</param>
    /// <param name="Radius">A stack's radius at the waterline; an arch's half thickness (m).</param>
    /// <param name="Seabed">The ground under it, metres above the sea (negative).</param>
    /// <param name="DirX">A stack: out to sea (unit); an arch: along the coast, the way its legs lie.</param>
    /// <param name="Span">An arch's opening, leg to leg (m); 0 for a stack.</param>
    public readonly record struct Rock(Kind Kind, double X, double Z, double Height, double Radius, double Seabed, double DirX, double DirZ,
        double Span, double LeanX, double LeanZ, ulong Seed);

    private const double GridCell = 4000;
    private readonly List<Rock> _rocks = new();
    private readonly double _minX, _minZ;
    private readonly int _gridSize;
    private readonly int[] _gridStart, _gridItems;

    public IReadOnlyList<Rock> Rocks => _rocks;
    /// <summary>Coast cells that met the rule (tests, tuning).</summary>
    public int Candidates { get; }

    public SeaStacks(TerrainSampler s, LandformSetting setting)
    {
        var plan = s.Plan; var macro = plan.Macro; double sea = plan.Profile.SeaLevel;
        var rng = new RandomStream(LandformCatalog.Seed(plan.Manifest, LandformCatalog.SeaStacks));
        var land = macro.Channel(WorldPlan.LandChannel);
        int n = macro.Size;
        var candidates = new List<(double X, double Z, double Dx, double Dz)>();
        for (int j = 1; j < n - 1; j++)
            for (int i = 1; i < n - 1; i++)
            {
                int k = j * n + i;
                if (land[k] <= 0 || (land[k - 1] > 0 && land[k + 1] > 0 && land[k - n] > 0 && land[k + n] > 0)) continue;
                double x = macro.CellX(i), z = macro.CellZ(j);
                macro.SampleGradient(WorldPlan.LandChannel, x, z, out double gx, out double gz);
                double g = Math.Sqrt(gx * gx + gz * gz);
                if (g < 1e-12) continue;
                double dx = -gx / g, dz = -gz / g; // out to sea
                // Steep: the land 1.5 km inland stands 60 m or more over the sea.
                if (macro.Sample(WorldPlan.HeightChannel, x - dx * 1500, z - dz * 1500) < 60) continue;
                // Open: nine tenths of the sea in front (out to 20 km, ±30°) is sea.
                int open = 0, all = 0;
                for (int a = -2; a <= 2; a++)
                {
                    double ang = a * Math.PI / 12, ux = dx * Math.Cos(ang) - dz * Math.Sin(ang), uz = dx * Math.Sin(ang) + dz * Math.Cos(ang);
                    for (double r = 2000; r <= 20000; r += 2000, all++)
                        if (macro.Sample(WorldPlan.LandChannel, x + ux * r, z + uz * r) <= 0) open++;
                }
                if (open < all * .9) continue;
                candidates.Add((x, z, dx, dz));
            }
        Candidates = candidates.Count;
        int want = Math.Clamp((int)Math.Round(setting.Amount * candidates.Count * .06), candidates.Count > 0 ? 1 : 0, 80);
        var sites = new List<(double X, double Z)>();
        for (int attempt = 0; attempt < want * 6 && sites.Count < want && candidates.Count > 0; attempt++)
        {
            var c = candidates[rng.Int(candidates.Count)];
            ulong siteSeed = rng.Next();
            bool apart = true;
            foreach (var (sx, sz) in sites) if (Math.Abs(sx - c.X) + Math.Abs(sz - c.Z) < 8000) { apart = false; break; }
            if (!apart) continue;
            if (Site(s, sea, setting, c.X, c.Z, c.Dx, c.Dz, new RandomStream(siteSeed))) sites.Add((c.X, c.Z));
        }
        // Revision 36: off about half the headlands' points (docs/LANDFORMS.md no. 10), their own streams.
        if (plan.Headlands is { } headlands)
            foreach (var (x, z, dx, dz, seed) in headlands.Points())
                if (Hashing.Unit(seed) < .5 * Math.Min(1, setting.Amount)) Site(s, sea, setting, x, z, dx, dz, new RandomStream(seed));
        // A coarse grid for lookups near a point.
        _minX = macro.MinX; _minZ = macro.MinZ;
        _gridSize = Math.Max(1, (int)Math.Ceiling(macro.Size * macro.Cell / GridCell));
        var lists = new List<int>[_gridSize * _gridSize];
        for (int r = 0; r < _rocks.Count; r++)
        {
            int cell = Cell(_rocks[r].Z, _minZ) * _gridSize + Cell(_rocks[r].X, _minX);
            (lists[cell] ??= new List<int>()).Add(r);
        }
        _gridStart = new int[lists.Length + 1];
        var items = new List<int>();
        for (int g = 0; g < lists.Length; g++) { _gridStart[g] = items.Count; if (lists[g] != null) items.AddRange(lists[g]); }
        _gridStart[lists.Length] = items.Count;
        _gridItems = items.ToArray();
    }

    /// <summary>One group off a coast cell: the shore found by walking out to sea, then the stacks (and maybe an arch)
    /// in front of it. False when nothing fitted.</summary>
    private bool Site(TerrainSampler s, double sea, LandformSetting setting, double x, double z, double dx, double dz, RandomStream rng)
    {
        // The shore: the first sea along the way out (the plan's coast cell is up to a cell from the detailed coastline).
        double shoreX = double.NaN, shoreZ = 0;
        for (double r = -1500; r <= 4000; r += 25)
        {
            var t = s.Sample(x + dx * r, z + dz * r, 8);
            if (t.Water == WaterKind.Ocean && t.WaterLevel - t.Height > 1) { shoreX = x + dx * r; shoreZ = z + dz * r; break; }
        }
        if (double.IsNaN(shoreX)) return false;
        double tx = -dz, tz = dx; // along the coast
        // The cliff behind: the highest ground 100–400 m inland — what the stacks were cut from, so about as high.
        double cliff = 0;
        for (double r = 100; r <= 400; r += 100) cliff = Math.Max(cliff, s.Sample(shoreX - dx * r, shoreZ - dz * r, 8).Height - sea);
        double tall = cliff >= 15 ? Math.Min(cliff, 90) : 22;
        int before = _rocks.Count;
        int count = 2 + rng.Int(6);
        double scale = setting.Size;
        for (int a = 0; a < count * 4 && _rocks.Count - before < count; a++)
        {
            double along = rng.Between(-400, 400), outward = 40 + 310 * Math.Pow(rng.Unit(), 1.6);
            double h = Math.Max(10, tall * rng.Between(.55, 1.05) * scale * (1 - .35 * Math.Min(1, outward / 350))); // (the far ones worn lower)
            double radius = Math.Max(5, h * rng.Between(.22, .38));
            double px = shoreX + tx * along + dx * outward, pz = shoreZ + tz * along + dz * outward;
            if (!Clear(s, px, pz, radius + 6, out double bed) || !Apart(px, pz, radius)) continue;
            double lean = rng.Between(0, .06), la = rng.Unit() * Math.Tau;
            _rocks.Add(new Rock(Kind.Stack, px, pz, h, radius, bed - sea, dx, dz, 0, lean * Math.Cos(la), lean * Math.Sin(la), rng.Next()));
        }
        // An arch now and then: its legs along the coast, the opening facing the sea (seen from the sea and the cliffs; with
        // its legs one behind the other going out, it read as a lump from the sea).
        if (rng.Unit() < .4 * Math.Min(1, setting.Amount))
            for (int a = 0; a < 12; a++)
            {
                double h = Math.Max(18, tall * rng.Between(.6, .95) * scale), span = h * rng.Between(.5, .9), half = Math.Clamp(span * rng.Between(.22, .32), 5, 12);
                double along = rng.Between(-350, 350), outward = rng.Between(50, 200);
                double px = shoreX + tx * along + dx * outward, pz = shoreZ + tz * along + dz * outward;
                double reach = span * .5 + half * 1.6;
                if (!Clear(s, px - tx * reach * .6, pz - tz * reach * .6, half * 1.8 + 4, out double bedNear)
                    || !Clear(s, px + tx * reach * .6, pz + tz * reach * .6, half * 1.8 + 4, out double bedFar)
                    || !Apart(px, pz, reach)) continue;
                _rocks.Add(new Rock(Kind.Arch, px, pz, h, half, Math.Min(bedNear, bedFar) - sea, tx, tz, span, 0, 0, rng.Next()));
                break;
            }
        return _rocks.Count > before;
    }

    /// <summary>Sea 1.5–30 m deep at the middle and all round within <paramref name="radius"/> (eight points).</summary>
    private static bool Clear(TerrainSampler s, double x, double z, double radius, out double bed)
    {
        var t = s.Sample(x, z, 8);
        bed = t.Height;
        double depth = t.WaterLevel - t.Height;
        if (t.Water != WaterKind.Ocean || depth < 1.5 || depth > 30) return false;
        for (int a = 0; a < 8; a++)
        {
            var u = s.Sample(x + radius * Math.Cos(a * Math.PI / 4), z + radius * Math.Sin(a * Math.PI / 4), 8);
            if (u.Water != WaterKind.Ocean || u.WaterLevel - u.Height < .5) return false;
            bed = Math.Min(bed, u.Height);
        }
        return true;
    }

    private bool Apart(double x, double z, double radius)
    {
        foreach (var r in _rocks)
        {
            double reach = r.Kind == Kind.Arch ? r.Span * .5 + r.Radius * 1.6 : r.Radius;
            if (Math.Sqrt((r.X - x) * (r.X - x) + (r.Z - z) * (r.Z - z)) < reach + radius + 12) return false;
        }
        return true;
    }

    /// <summary>The stacks and arches within <paramref name="range"/> of a point.</summary>
    public IEnumerable<Rock> Near(double x, double z, double range)
    {
        if (_rocks.Count == 0) yield break;
        int i0 = Cell(x - range, _minX), i1 = Cell(x + range, _minX), j0 = Cell(z - range, _minZ), j1 = Cell(z + range, _minZ);
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                int g = j * _gridSize + i;
                for (int k = _gridStart[g]; k < _gridStart[g + 1]; k++)
                {
                    var r = _rocks[_gridItems[k]];
                    if (Math.Abs(r.X - x) <= range && Math.Abs(r.Z - z) <= range) yield return r;
                }
            }
    }

    private int Cell(double v, double min) => Math.Clamp((int)Math.Floor((v - min) / GridCell), 0, _gridSize - 1);
}
