using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

/// <summary>
/// Cirques with a tarn (landform no. 4, terrain revision 23, D241; docs/LANDFORMS.md): bowls carved high on mountain
/// flanks, mostly facing north (away from the sun: north is −Z), with a steep back wall, gentler sides, a rock bar at
/// the front and a small calm lake in the floor — the mountain tarns Tom asked about (a plan lake needs ~2 km², so
/// none were ever this small). Placed once in the plan after the rivers are final, away from rivers and lakes; the
/// bowl is walking-scale ground (<see cref="Carve"/>) and the tarn is still water of its own (<see cref="Tarn"/>,
/// drawn as a pond). The plan's rivers and lakes are unchanged.
/// </summary>
public sealed class Cirques
{
    private readonly record struct Bowl(double Fx, double Fz, double Dx, double Dz, double Radius, double Floor, double Depth, double Back, double Front, ulong Seed);

    private const double GridCell = 2000;
    /// <summary>The tarn's level below the lip's: more than the rim blend's dip (a quarter of its 6 m), so no ground
    /// beside the water lies below it.</summary>
    private const double TarnBelow = 2;
    private readonly List<Bowl> _bowls = new();
    private readonly double _minX, _minZ, _sea;
    private readonly int _gridSize;
    private readonly int[] _gridStart, _gridItems;

    /// <summary>Number of cirques in this world.</summary>
    public int Count => _bowls.Count;

    /// <summary>Each tarn's middle, its water level and the cirque's radius (tests, captures).</summary>
    public IEnumerable<(double X, double Z, double Level, double Radius)> Probes()
    {
        foreach (var b in _bowls) yield return (b.Fx, b.Fz, _sea + b.Floor - TarnBelow, b.Radius);
    }

    public Cirques(WorldPlan plan, LandformSetting setting)
    {
        var macro = plan.Macro; var hy = plan.Hydrology; var p = plan.Profile;
        _sea = p.SeaLevel;
        var rng = new RandomStream(LandformCatalog.Seed(plan.Manifest, LandformCatalog.Cirques));
        var mountain = macro.Channel(WorldPlan.MountainChannel);
        var height = macro.Channel(WorldPlan.HeightChannel);
        double high = p.LowlandHeight + .3 * p.MountainHeight;
        var starts = new List<int>();
        for (int k = 0; k < mountain.Length; k++)
            if (mountain[k] > .35f && height[k] > high) starts.Add(k);
        double km2 = starts.Count * macro.Cell * macro.Cell / 1e6;
        int want = Math.Clamp((int)Math.Round(setting.Amount * km2 / (10 * 10)), 0, 600);
        for (int attempt = 0; attempt < want * 40 && _bowls.Count < want && starts.Count > 0; attempt++)
        {
            int k = starts[rng.Int(starts.Count)];
            double cx = macro.CellX(k % macro.Size) + rng.Between(-.5, .5) * macro.Cell, cz = macro.CellZ(k / macro.Size) + rng.Between(-.5, .5) * macro.Cell;
            double radius = rng.Between(250, 650) * setting.Size, depth = rng.Between(6, 18), chance = rng.Unit();
            double hc = macro.SampleGradient(WorldPlan.HeightChannel, cx, cz, out double gx, out double gz), grade = Math.Sqrt(gx * gx + gz * gz);
            if (grade < .12 || grade > .8) continue; // a flank: not a summit or a plain, not a cliff
            double dx = -gx / grade, dz = -gz / grade; // downhill, where the bowl opens
            if (chance > .25 + .75 * Math.Max(0, -dz)) continue; // mostly facing north (−Z), away from the sun
            double lipX = cx + dx * radius * .85, lipZ = cz + dz * radius * .85;
            double floor = macro.Sample(WorldPlan.HeightChannel, lipX, lipZ);
            if (hc - floor < 20) continue;
            double fx = cx + dx * radius * .15, fz = cz + dz * radius * .15;
            if (hy.NearestRiver(fx, fz, radius + 300, out _) || hy.LakeNear(fx, fz) >= 0 || hy.LakeNear(lipX, lipZ) >= 0) continue;
            bool apart = true;
            foreach (var b in _bowls) if (Math.Sqrt((b.Fx - fx) * (b.Fx - fx) + (b.Fz - fz) * (b.Fz - fz)) < 2.5 * Math.Max(b.Radius, radius)) apart = false;
            if (!apart) continue;
            _bowls.Add(new Bowl(fx, fz, dx, dz, radius, floor, depth, rng.Between(1.3, 1.9), rng.Between(.25, .45), rng.Next()));
        }
        _minX = macro.MinX; _minZ = macro.MinZ;
        _gridSize = (int)Math.Ceiling(macro.Size * macro.Cell / GridCell);
        var cells = new List<int>[_gridSize * _gridSize];
        for (int n = 0; n < _bowls.Count; n++)
        {
            var b = _bowls[n];
            for (int j = Cell(b.Fz - b.Radius * 1.2, _minZ); j <= Cell(b.Fz + b.Radius * 1.2, _minZ); j++)
                for (int i = Cell(b.Fx - b.Radius * 1.2, _minX); i <= Cell(b.Fx + b.Radius * 1.2, _minX); i++)
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
    /// The bowl's surface at a point (metres above the sea, like the plan's heights), or +∞ outside every cirque: a dish-shaped floor 6–18 m below
    /// the lip's level within 55 % of the radius, then walls rising steeply at the back (slope 1.3–1.9) and gently at
    /// the front (0.25–0.45, the rock bar), by how far the point lies uphill of the floor's middle.
    /// </summary>
    private static double Surface(in Bowl b, double x, double z, double detail)
    {
        double r = Reach(b, x, z, out double uphill);
        if (r > 1.2) return double.PositiveInfinity;
        // The mountain's own rock carries into the walls, coming in from the foot of the wall upward (its relief averages
        // tens of metres above the plan's height: all at once it stepped the wall's foot into a ledge); the floor has a
        // small bumpiness of its own, none at its edge (so the tarn stays closed in).
        double rough = r < .55 ? 3 * Noise.Fbm(x, z, 45, 3, 0, b.Seed + 1) * (1 - DetMath.Smooth((r - .45) / .1))
            : .8 * detail * DetMath.Smooth((r - .55) / .35);
        if (r < .55) { double u = r / .55; return b.Floor - b.Depth * (1 - u * u) + rough; }
        double slope = b.Front + (b.Back - b.Front) * DetMath.Smooth(.5 + .5 * uphill);
        return b.Floor + (r - .55) * b.Radius * slope + rough;
    }

    /// <summary>
    /// How far a point lies out from the floor's middle, as a share of the radius: wider across the slope than up and
    /// down it (a horseshoe, not a dish), the outline lobed by noise. <paramref name="uphill"/>: 1 at the back wall,
    /// −1 toward the lip.
    /// </summary>
    private static double Reach(in Bowl b, double x, double z, out double uphill)
    {
        double px = (x - b.Fx) / b.Radius, pz = (z - b.Fz) / b.Radius;
        double along = px * b.Dx + pz * b.Dz, across = -px * b.Dz + pz * b.Dx;
        double r = Math.Sqrt(along * along * 1.2 + across * across / 1.25);
        uphill = r > 1e-9 ? -along / Math.Sqrt(along * along + across * across + 1e-12) : 0;
        return r * (1 + .2 * Noise.Fbm(x, z, b.Radius * .7, 2, 0, b.Seed));
    }

    /// <summary>How much the ground (metres above the sea) changes here (m): taken down to the bowl, blended over a few
    /// metres so the rim has no crease; and a rock bar around the floor (0.5 m over the lip's level, fading out by 13 %
    /// of the radius) wherever the ground beside the floor would lie lower — on the downhill side the real ground, with
    /// its rock, could dip below the lip the bowl was planned from, and the tarn ran out over it.</summary>
    public double Carve(double x, double z, double ground, double detail)
    {
        if (_bowls.Count == 0) return 0;
        int c = Cell(z, _minZ) * _gridSize + Cell(x, _minX);
        double carved = ground;
        for (int n = _gridStart[c]; n < _gridStart[c + 1]; n++)
        {
            var b = _bowls[_gridItems[n]];
            double s = Surface(b, x, z, detail);
            if (s < carved + 6) carved = SmoothMin(carved, s, 6);
            double r = Reach(b, x, z, out _);
            if (r >= .55 && r < .68)
            {
                double sill = b.Floor + .5, keep = DetMath.Smooth((r - .62) / .06);
                if (carved < sill) carved = sill + (carved - sill) * keep;
            }
        }
        return carved - ground;
    }

    /// <summary>The tarn here, if any: water within the floor, 2 m below the lip's level, over ground (metres above the
    /// sea) lower than that; <paramref name="level"/> is absolute, like every water level.</summary>
    public bool Tarn(double x, double z, double ground, out double level)
    {
        level = double.NaN;
        if (_bowls.Count == 0) return false;
        int c = Cell(z, _minZ) * _gridSize + Cell(x, _minX);
        for (int n = _gridStart[c]; n < _gridStart[c + 1]; n++)
        {
            var b = _bowls[_gridItems[n]];
            if (Reach(b, x, z, out _) >= .55) continue;
            double l = b.Floor - TarnBelow;
            if (ground < l) { level = _sea + l; return true; }
        }
        return false;
    }

    private static double SmoothMin(double a, double b, double k)
    {
        double h = DetMath.Clamp(.5 + .5 * (b - a) / k, 0, 1);
        return b + (a - b) * h - k * h * (1 - h);
    }

    private static double Sq(double v) => v * v;
}
