using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

/// <summary>
/// Alluvial fans (landform no. 13, terrain revision 41, D283; docs/LANDFORMS.md): the gravel a mountain stream drops where it
/// leaves the mountains for a plain, spread in a cone. Placed once in the plan on the final rivers, where a stream falling
/// steeply eases onto gentle land (the mountain front), more often in dry country; each is an apron 300–800 m long opening
/// 80–120° downstream from that apex, 5–12 m of gravel laid on the ground there thinning to nothing at its toe and sides (the
/// plain beside the stream lies above the river's level, so a cone built down from the stream lay under the ground). The
/// stream runs through it in a trench near the apex (rivers win), dry channels radiate over it, gravel lies on it.
/// </summary>
public sealed class Fans
{
    private readonly record struct Fan(double X, double Z, double Ax, double Az, double Radius, double Half, double Height, ulong Seed);

    private const double GridCell = 4000;
    private readonly List<Fan> _fans = new();
    private readonly double _minX, _minZ;
    private readonly int _gridSize;
    private readonly int[] _gridStart, _gridItems;

    /// <summary>Number of fans in this world.</summary>
    public int Count => _fans.Count;

    /// <summary>Each fan's apex, its axis (downstream), length, half-angle and its gravel's thickness at the apex (tests,
    /// captures).</summary>
    public IEnumerable<(double X, double Z, double Ax, double Az, double Radius, double Half, double Height)> Probes()
    {
        foreach (var f in _fans) yield return (f.X, f.Z, f.Ax, f.Az, f.Radius, f.Half, f.Height);
    }

    public Fans(WorldPlan plan, Climate climate, LandformSetting setting)
    {
        var macro = plan.Macro; var hy = plan.Hydrology;
        var rng = new RandomStream(LandformCatalog.Seed(plan.Manifest, LandformCatalog.Fans));
        foreach (var r in hy.Reaches)
        {
            if (r.Distributary || r.Points.Length < 10 || r.Length < 1500) continue;
            int n = r.Points.Length;
            // The mountain front: the first point where the stream, falling over 2 % above it, eases to under 0.7 % below it
            // onto land that is not mountain (the plan's mountain share marks the high ranges only: rivers start below them).
            int apex = -1;
            for (int i = 4; i < n - 5 && apex < 0; i++)
            {
                double up = r.Along[i] - r.Along[i - 4], down = r.Along[i + 4] - r.Along[i];
                if (up <= 0 || down <= 0) continue;
                double above = (r.Surface[i - 4] - r.Surface[i]) / up, below = (r.Surface[i] - r.Surface[i + 4]) / down;
                if (above < .03 || below > .006 || below > above / 4) continue;
                if (macro.Sample(WorldPlan.MountainChannel, r.Points[i + 4].X, r.Points[i + 4].Z) > .15) continue;
                // (not where the stream leaves a gorge or runs into a canyon: the gravel lay on the canyon's rims)
                if (hy.Gorge(r.Id, r.Along[i]) > 0 || hy.Gorge(r.Id, r.Along[i + 4]) > 0) continue;
                apex = i;
            }
            ulong seed = rng.Next();
            double roll = rng.Unit(), radiusShare = rng.Between(0, 1), halfShare = rng.Between(0, 1), thick = rng.Between(5, 12);
            if (apex < 0 || r.Width[apex] < 3 || r.Width[apex] > 45) continue;
            var p = r.Points[apex];
            // More in dry country: 40 % of the places below ~300 mm a year, 6 % over ~1500.
            double rain = climate.At(p.X, p.Z, 0).Precipitation;
            if (roll > setting.Amount * (.06 + .34 * DetMath.Smooth((1500 - rain) / 1200))) continue;
            double ax = r.Points[apex + 3].X - p.X, az = r.Points[apex + 3].Z - p.Z, al = Math.Sqrt(ax * ax + az * az);
            if (al < 1e-6) continue;
            ax /= al; az /= al;
            double radius = (300 + 500 * radiusShare) * setting.Size, half = .7 + .35 * halfShare;
            // Room for it: land, no canyon, no lake, not over another fan (the apex, the toe and the edges at three distances).
            bool clear = true;
            for (int a = 0; a < 10 && clear; a++)
            {
                double t = a < 1 ? 0 : (a - 1) / 3 / 2.0 + .35, ang = a < 1 ? 0 : ((a - 1) % 3 - 1) * half;
                if (a == 9) { t = 1; ang = 0; }
                double ca = Math.Cos(ang), sa = Math.Sin(ang);
                double px = p.X + (ax * ca - az * sa) * radius * t, pz = p.Z + (az * ca + ax * sa) * radius * t;
                if (macro.Sample(WorldPlan.LandChannel, px, pz) < .2 || plan.Canyons is { } canyons && canyons.Uplift(px, pz) > 1 || hy.LakeNear(px, pz) >= 0 || (hy.LakeDistance(px, pz, out double lakeCells) >= 0 && lakeCells < 2.5)) clear = false;
            }
            foreach (var f in _fans)
                if (Math.Sqrt((f.X - p.X) * (f.X - p.X) + (f.Z - p.Z) * (f.Z - p.Z)) < f.Radius + radius + 100) { clear = false; break; }
            if (clear) _fans.Add(new Fan(p.X, p.Z, ax, az, radius, half, thick * setting.Size, seed));
        }
        _minX = macro.MinX; _minZ = macro.MinZ;
        _gridSize = Math.Max(1, (int)Math.Ceiling(macro.Size * macro.Cell / GridCell));
        var cells = new List<int>[_gridSize * _gridSize];
        for (int k = 0; k < _fans.Count; k++)
        {
            var f = _fans[k];
            double reach = f.Radius * 1.15 + 20;
            for (int j = Cell(f.Z - reach, _minZ); j <= Cell(f.Z + reach, _minZ); j++)
                for (int i = Cell(f.X - reach, _minX); i <= Cell(f.X + reach, _minX); i++)
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
    /// The ground (metres above the sea, in and out) with the fans' gravel laid on it: thickest near each apex (easing in over
    /// its first 150 m from the stream's mouth), thinning to nothing at its toe and fading out at its sides, dry channels in it;
    /// <paramref name="gravel"/> how much loose gravel lies there (0 … 1, drawn as scree).
    /// </summary>
    public double Fill(double x, double z, double ground, out double gravel)
    {
        gravel = 0;
        if (_fans.Count == 0) return ground;
        int c = Cell(z, _minZ) * _gridSize + Cell(x, _minX);
        double add = 0;
        for (int k = _gridStart[c]; k < _gridStart[c + 1]; k++)
        {
            var f = _fans[_gridItems[k]];
            double dx = x - f.X, dz = z - f.Z, r = Math.Sqrt(dx * dx + dz * dz);
            if (r > f.Radius * 1.15 || r < 1e-6) continue;
            double along = dx * f.Ax + dz * f.Az, ang = Math.Acos(Math.Clamp(along / r, -1, 1));
            // The edge wanders (±12 % of the half-angle and of the length); the gravel thins out toward it.
            double half = f.Half * (1 + .12 * Noise.Fbm(dx, dz, f.Radius * .6, 2, 0, f.Seed));
            double length = f.Radius * (1 + .12 * Noise.Fbm(dx, dz, f.Radius * .5, 2, 0, f.Seed + 1));
            // (the side's fade at least 60 m wide: near the apex, where the fan is narrow, it stood the edge at 43°)
            double side = 1 - DetMath.Smooth((ang - half * .55) * r / Math.Max(half * .45 * r, 60));
            if (side <= 0 || r >= length) continue;
            double t = r / length, thick = f.Height * Math.Pow(1 - t, 1.4) * DetMath.Smooth(r / 150) * side;
            // Dry channels radiating from the apex, a few tens of metres apart, wandering.
            double arc = ang * r + 25 * Noise.Fbm(dx, dz, 140, 2, 0, f.Seed + 2);
            double channel = 1 - DetMath.Smooth(Math.Abs(Math.Sin(arc * Math.PI / 55)) / .35);
            thick -= Math.Min(thick * .5, .8 * channel * (1 - t));
            if (thick > add) add = thick;
            gravel = Math.Max(gravel, side * DetMath.Smooth(thick / 1.5) * (.3 + .4 * (1 - t)));
        }
        return ground + add;
    }
}
