using System;

namespace Thornwake.WorldGen;

/// <summary>
/// River terraces (landform no. 12, second half, terrain revision 40, D282; docs/LANDFORMS.md): the old floodplains a river
/// left behind as it cut down, flat steps along the sides of broad valleys. The river stage's valley side (the smooth slope
/// from the bank top up to the land) is stepped where a broad river runs through a valley of some depth: one to three
/// treads, each a level bench at a fixed height over the river's bank top (so they follow the river's fall along its
/// length), joined by short risers; above the last one the valley side goes on as before. In patches along each side of
/// the river, not everywhere; never in gorges, estuaries, near lakes or in the river's own channel and banks.
/// </summary>
public sealed class Terraces
{
    private readonly double _amount, _size;
    private readonly ulong _seed;

    public Terraces(WorldPlan plan, LandformSetting setting)
    {
        _amount = setting.Amount; _size = setting.Size;
        _seed = LandformCatalog.Seed(plan.Manifest, LandformCatalog.Terraces);
    }

    /// <summary>Where along the river there are terraces (0 … 1). (Not by the river's side: the side flips inside tight bends,
    /// and a terrace with it — cut ground jumped 4 m between neighbours.)</summary>
    public double Presence(double lx, double lz, double width)
    {
        double wide = DetMath.Smooth((width - 12) / 12);
        if (wide <= 0) return 0;
        double patch = Noise.Fbm(lx, lz, 1700, 2, 0, _seed + 3);
        return wide * DetMath.Smooth((patch + .25 * (_amount - 1) + .02) / .07);
    }

    /// <summary>
    /// The ground beside a river cut into terraces: <paramref name="h"/> its height over the river's bank top (metres) at
    /// <paramref name="w"/> metres out from the bank, <paramref name="weight"/> how much (the presence and fades). Out from the
    /// bank: the floodplain, then one to three level treads 15–70 m wide, each 3–7 m over the one below, on risers of about 1 in
    /// 2; behind the last a valley wall of 1 in 2 up to where it meets the ground, all within ~180 m of the bank (the river's
    /// reach), fading out by 250 m. Only ever lowers the ground (a cut, as the river made them).
    /// </summary>
    private const double MaxCut = 10;

    public double Step(double h, double w, double lx, double lz, double weight)
    {
        if (h <= 0 || weight <= 0 || w <= 0) return h;
        weight *= 1 - DetMath.Smooth((w - 180) / 70);
        if (weight <= 0) return h;
        double step = _size * (3 + 4 * DetMath.Smooth(.5 + 1.5 * Noise.Fbm(lx, lz, 2600, 2, 0, _seed + 5)));
        double tiers = 1 + 2 * DetMath.Smooth(.5 + 1.5 * Noise.Fbm(lx, lz, 3300, 2, 0, _seed + 7));
        double tread = DetMath.Clamp((150 - 2 * step * tiers) / (tiers + .6), 15, 70) * (.8 + .4 * DetMath.Smooth(.5 + 1.5 * Noise.Fbm(lx, lz, 900, 2, 0, _seed + 9)));
        double g = Staircase(w, step, tiers, tread);
        // A soft minimum (no crease where the cut meets the ground), and never over 10 m deep (a soft maximum): into a
        // steep hillside the tread follows the hill 10 m down rather than leaving a wall at the end of the river's reach.
        double k = 1.5, m = DetMath.Clamp(.5 + .5 * (g - h) / k, 0, 1), cut = g + (h - g) * m - k * m * (1 - m);
        double floor = h - MaxCut, kk = 2, mm = DetMath.Clamp(.5 + .5 * (cut - floor) / kk, 0, 1);
        cut = floor + (cut - floor) * mm + kk * mm * (1 - mm);
        return h + (Math.Min(cut, h) - h) * weight;
    }

    private static double Staircase(double w, double step, double tiers, double tread)
    {
        double pos = tread * .6, g = 0, riser = 2 * step;
        if (w < pos) return 0; // the floodplain, widened
        for (int k = 1; k <= 3; k++)
        {
            if (w < pos + riser) { double t = (w - pos) / riser; return g + step * (.5 * DetMath.Smooth(t) + .5 * t); }
            g += step; pos += riser;
            double width = tread * DetMath.Clamp(tiers - k + 1, 0, 1);
            if (w < pos + width) return g;
            pos += width;
        }
        return g + (w - pos) * .5; // the valley wall behind
    }
}
