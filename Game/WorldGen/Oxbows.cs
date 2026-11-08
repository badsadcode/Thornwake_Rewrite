using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

/// <summary>
/// Oxbow lakes (landform no. 12, terrain revision 39, D281; docs/LANDFORMS.md): crescent lakes beside lowland rivers, the
/// loops a meandering river left behind when it cut through their neck. Placed once in the plan after the rivers are final,
/// beside gentle stretches of river across flat floodplains, clear of other rivers, lakes and the coast; each is a crescent
/// of still water (drawn as a pond, as the cirques' tarns) a little above the river's level, its open side toward the river,
/// its horns tapering, a low rim of ground round it so it stays closed. The plan's rivers and lakes are unchanged: water wins.
/// </summary>
public sealed class Oxbows
{
    private readonly record struct Bow(double X, double Z, double Nx, double Nz, double Radius, double Width, double Open, double Level, double Depth, ulong Seed);

    private const double GridCell = 4000;
    private readonly List<Bow> _bows = new();
    private readonly double _minX, _minZ, _sea;
    private readonly int _gridSize;
    private readonly int[] _gridStart, _gridItems;

    /// <summary>Number of oxbow lakes in this world.</summary>
    public int Count => _bows.Count;

    /// <summary>Each lake's middle (the crescent's centre), radius, width and water level (absolute) (tests, captures).</summary>
    public IEnumerable<(double X, double Z, double Radius, double Width, double Level, double Nx, double Nz, double Open)> Probes()
    {
        foreach (var b in _bows) yield return (b.X, b.Z, b.Radius, b.Width, b.Level, b.Nx, b.Nz, b.Open);
    }

    public Oxbows(WorldPlan plan, LandformSetting setting)
    {
        var macro = plan.Macro; var hy = plan.Hydrology;
        _sea = plan.Profile.SeaLevel;
        var rng = new RandomStream(LandformCatalog.Seed(plan.Manifest, LandformCatalog.Oxbows));
        // Gentle lowland reaches of some size: the rivers that meander across floodplains.
        var reaches = new List<RiverReach>();
        double total = 0;
        foreach (var r in hy.Reaches)
        {
            if (r.Distributary || r.Points.Length < 12 || r.Length < 3000) continue;
            float w = r.Width[r.Width.Length / 2];
            if (w < 10 || w > 140) continue;
            reaches.Add(r); total += r.Length;
        }
        int want = Math.Clamp((int)Math.Round(setting.Amount * total / 9000), 0, 400);
        for (int attempt = 0; attempt < want * 20 && _bows.Count < want && reaches.Count > 0; attempt++)
        {
            var r = reaches[rng.Int(reaches.Count)];
            int n = r.Points.Length, i = 3 + rng.Int(n - 6);
            double side = rng.Unit() < .5 ? -1 : 1, radiusShare = rng.Between(2.4, 4.2), gap = rng.Between(60, 160), open = rng.Between(.65, 1.05);
            double widthShare = rng.Between(.8, 1.15), depth = rng.Between(1.5, 3.2);
            ulong seed = rng.Next();
            // Gentle: under 1.5 m a km here.
            double run = r.Along[i + 3] - r.Along[i - 3];
            if (run <= 0 || (r.Surface[i - 3] - r.Surface[i + 3]) / run > .0015) continue;
            double width = r.Width[i];
            double fx = r.Points[i + 1].X - r.Points[i - 1].X, fz = r.Points[i + 1].Z - r.Points[i - 1].Z, fl = Math.Sqrt(fx * fx + fz * fz);
            if (fl < 1e-6) continue;
            double nx = -fz / fl * side, nz = fx / fl * side; // out from the river, the side the loop lies on
            double radius = Math.Clamp(width * radiusShare, 70, 420) * setting.Size, w = Math.Max(14, width * widthShare);
            double cx = r.Points[i].X + nx * (width * .5 + gap + radius), cz = r.Points[i].Z + nz * (width * .5 + gap + radius);
            double level = r.Surface[i] + .3;
            // Room for it: flat lowland, no river, lake or sea near the crescent (16 points round it and its middle).
            bool clear = true;
            for (int a = 0; a <= 16 && clear; a++)
            {
                double px, pz;
                if (a == 16) { px = cx; pz = cz; }
                else
                {
                    double ang = a * Math.Tau / 16;
                    px = cx + Math.Cos(ang) * radius; pz = cz + Math.Sin(ang) * radius;
                }
                // (every point, the open side's too: the horns and their rim came within a few metres of rivers)
                if (hy.NearestRiver(px, pz, w * .5 + 34, out _) || hy.LakeNear(px, pz) >= 0 || (hy.LakeDistance(px, pz, out double lakeCells) >= 0 && lakeCells < 2.5)) clear = false;
                else if (macro.Sample(WorldPlan.LandChannel, px, pz) < .25 || macro.Sample(WorldPlan.MountainChannel, px, pz) > .12) clear = false;
                else if (macro.Sample(WorldPlan.HeightChannel, px, pz) + _sea - level > 7) clear = false;
            }
            foreach (var b in _bows)
                if (Math.Sqrt((b.X - cx) * (b.X - cx) + (b.Z - cz) * (b.Z - cz)) < b.Radius + radius + b.Width + w + 60) { clear = false; break; }
            if (clear) _bows.Add(new Bow(cx, cz, nx, nz, radius, w, open, level, depth, seed));
        }
        _minX = macro.MinX; _minZ = macro.MinZ;
        _gridSize = Math.Max(1, (int)Math.Ceiling(macro.Size * macro.Cell / GridCell));
        var cells = new List<int>[_gridSize * _gridSize];
        for (int k = 0; k < _bows.Count; k++)
        {
            var b = _bows[k];
            double reach = b.Radius + b.Width + 40;
            for (int j = Cell(b.Z - reach, _minZ); j <= Cell(b.Z + reach, _minZ); j++)
                for (int i = Cell(b.X - reach, _minX); i <= Cell(b.X + reach, _minX); i++)
                    (cells[j * _gridSize + i] ??= new List<int>()).Add(k);
        }
        _gridStart = new int[cells.Length + 1];
        var items = new List<int>();
        for (int c = 0; c < cells.Length; c++) { _gridStart[c] = items.Count; if (cells[c] != null) items.AddRange(cells[c]); }
        _gridStart[^1] = items.Count;
        _gridItems = items.ToArray();
    }

    private int Cell(double v, double min) => Math.Clamp((int)Math.Floor((v - min) / GridCell), 0, _gridSize - 1);

    /// <summary>
    /// The ground (metres above the sea, in and out) round and under the oxbow lakes, and whether this is one's water:
    /// <paramref name="level"/> its surface (absolute). Inside the crescent a shallow bed below the water, the horns tapering
    /// to nothing; round it a rim raised to 0.5 m over the water where the ground lies lower, so the lake stays closed.
    /// </summary>
    public bool Apply(double x, double z, ref double height, out double level) => Apply(x, z, ref height, out level, out _);

    /// <param name="near">Inside an oxbow lake's rim (the wetland pools stay out: they cut its rim down, D281).</param>
    public bool Apply(double x, double z, ref double height, out double level, out bool near)
    {
        level = double.NaN; near = false;
        if (_bows.Count == 0) return false;
        int c = Cell(z, _minZ) * _gridSize + Cell(x, _minX);
        bool wet = false;
        for (int k = _gridStart[c]; k < _gridStart[c + 1]; k++)
        {
            var b = _bows[_gridItems[k]];
            double dx = x - b.X, dz = z - b.Z, r = Math.Sqrt(dx * dx + dz * dz);
            if (r > b.Radius + b.Width + 40 || r < 1e-6) continue;
            // The angle from the open side (toward the river): the water only where it is over the opening's half-angle.
            double ang = Math.Acos(Math.Clamp(-(dx * b.Nx + dz * b.Nz) / r, -1, 1));
            double radius = b.Radius * (1 + .07 * Noise.Fbm(x, z, b.Radius * 1.3, 2, 0, b.Seed));
            double d = Math.Abs(r - radius), half = b.Width * .5 * DetMath.Smooth((ang - b.Open) / .4) * (1 + .15 * Noise.Fbm(x, z, 60, 2, 0, b.Seed + 1));
            double lvl = b.Level - _sea;
            double rim = (1 - DetMath.Smooth((d - half - 6) / 22)) * DetMath.Smooth((ang - b.Open + .25) / .25);
            if (rim > 0) near = true;
            if (rim > 0 && height < lvl + .5) height += (lvl + .5 - height) * rim;
            if (half > .5 && d < half)
            {
                double bed = lvl - .25 - b.Depth * (1 - (d / half) * (d / half));
                if (bed < height) height = bed;
                wet = true; level = b.Level;
            }
        }
        return wet;
    }
}
