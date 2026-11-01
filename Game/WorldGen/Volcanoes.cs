using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

/// <summary>
/// Volcanoes and calderas (landform no. 7, terrain revision 28, D246; docs/LANDFORMS.md). A big landform: each cone or
/// caldera is raised in the plan's height raster before erosion, so the rivers form on it and run down its flanks;
/// what the raster's cells cannot hold — the sharp summit, the crater, the caldera's cliffs, lava flows — is added at
/// walking scale (<see cref="Ground"/>: the exact shape minus what the raster holds of it). A caldera's basin is a
/// plan lake (the hydrology fills it to the breach in its rim, where its river leaves); a quiet cone's crater holds a
/// small lake of its own (<see cref="CraterLake"/>). The volcanic biome is drawn onto them (BiomeMap).
/// </summary>
public sealed class Volcanoes : PlanLandform
{
    private sealed class Volcano
    {
        public double X, Z, Height, Radius, Power;
        /// <summary>Cone: the crater's radius and depth; caldera: the caldera's radius (to the rim) and the floor's depth
        /// below the rim.</summary>
        public double Inner, Depth;
        public bool Caldera, Lake;
        /// <summary>Caldera: the breach in the rim (direction, how deep: a share of the depth).</summary>
        public double BreachX, BreachZ, Breach;
        public (double Angle, double From, double To, double Width, double Thickness)[] Flows = Array.Empty<(double, double, double, double, double)>();
        public ulong Seed;
        public double LakeLevel = double.NaN;
        /// <summary>The level it is built on: the land's mean height round its foot, before it was raised.</summary>
        public double Base;
    }

    private readonly int _revision;
    private readonly List<Volcano> _volcanoes = new();
    private MacroRaster? _macro, _raise;
    private double _sea;

    public Volcanoes(int revision) { _revision = revision; }

    public override LandformInfo Info => LandformCatalog.Volcanoes;

    /// <summary>Number of volcanoes (cones and calderas) in this world.</summary>
    public int Count => _volcanoes.Count;

    /// <summary>Each volcano's middle, radius, height over the land, and whether it is a caldera (tests, captures, biomes).</summary>
    public IEnumerable<(double X, double Z, double Radius, double Height, bool Caldera)> Probes()
    {
        foreach (var v in _volcanoes) yield return (v.X, v.Z, v.Radius, v.Height, v.Caldera);
    }

    public override void Shape(MacroRaster macro, WorldManifest manifest, LandformSetting setting)
    {
        _macro = macro;
        _sea = manifest.Profile.SeaLevel;
        Place(macro, manifest, setting);
        if (_volcanoes.Count == 0) return;
        // Each volcano is built on a level base (the land's mean height round its foot), replacing the relief under it
        // (added onto the mountains' relief, a cone was lost in it and a caldera's floor drained away through it): the
        // raster takes the volcano without its crater (smaller than a cell) before erosion; its slopes are smoother than
        // the mountains' (less mountain share), rockier toward the top.
        var land = macro.Channel(WorldPlan.LandChannel);
        var height = macro.Channel(WorldPlan.HeightChannel);
        var mountain = macro.Channel(WorldPlan.MountainChannel);
        foreach (var v in _volcanoes)
        {
            double sum = 0;
            for (int d = 0; d < 16; d++)
            {
                double a = d * Math.Tau / 16;
                sum += macro.Sample(WorldPlan.HeightChannel, v.X + Math.Cos(a) * v.Radius * 1.05, v.Z + Math.Sin(a) * v.Radius * 1.05);
            }
            v.Base = sum / 16;
        }
        _raise = new MacroRaster(macro.Size, macro.MinX, macro.MinZ, macro.Cell, 1);
        var raised = _raise.Channel(0);
        for (int j = 0; j < macro.Size; j++)
            for (int i = 0; i < macro.Size; i++)
            {
                int k = j * macro.Size + i;
                double x = macro.CellX(i), z = macro.CellZ(j);
                foreach (var v in _volcanoes)
                {
                    double w = Weight(v, x, z) * DetMath.Smooth((land[k] - .2) / .3);
                    if (w <= 0) continue;
                    double shape = Profile(v, x, z, false, out _);
                    raised[k] = (float)(w * shape);
                    height[k] = (float)(height[k] * (1 - w) + w * (v.Base + shape));
                    mountain[k] = (float)(mountain[k] * (1 - w) + w * .2 * shape / v.Height);
                }
            }
    }

    /// <summary>
    /// How much each raster cell resists erosion (for the hydrology): a caldera's rim and the slope just outside it,
    /// whole (its breach included — the lake spills there and its river leaves; cut through, the basin drained); null
    /// when there is no caldera.
    /// </summary>
    public float[]? Resistance(MacroRaster macro)
    {
        if (!_volcanoes.Exists(v => v.Caldera)) return null;
        var resist = new float[macro.Size * macro.Size];
        for (int j = 0; j < macro.Size; j++)
            for (int i = 0; i < macro.Size; i++)
            {
                double x = macro.CellX(i), z = macro.CellZ(j);
                foreach (var v in _volcanoes)
                {
                    if (!v.Caldera) continue;
                    double r = Math.Sqrt(Sq(x - v.X) + Sq(z - v.Z));
                    double ring = DetMath.Smooth((r - v.Inner * .6) / (v.Inner * .2)) * (1 - DetMath.Smooth((r - v.Inner * 1.5) / (v.Inner * .3)));
                    resist[j * macro.Size + i] = Math.Max(resist[j * macro.Size + i], (float)ring);
                }
            }
        return resist;
    }

    /// <summary>The raster cell under each caldera's middle (its floor: always a lake); null when there is no caldera.</summary>
    public IReadOnlyCollection<int>? LakeCells(MacroRaster macro)
    {
        if (!_volcanoes.Exists(v => v.Caldera)) return null;
        var cells = new HashSet<int>();
        foreach (var v in _volcanoes)
            if (v.Caldera) cells.Add((int)((v.Z - macro.MinZ) / macro.Cell) * macro.Size + (int)((v.X - macro.MinX) / macro.Cell));
        return cells;
    }

    /// <summary>How much the volcano replaces the land here: whole within 75 % of its radius, fading out to its foot.</summary>
    private static double Weight(Volcano v, double x, double z)
    {
        double r = Math.Sqrt(Sq(x - v.X) + Sq(z - v.Z));
        return 1 - DetMath.Smooth((r - v.Radius * .75) / (v.Radius * .25));
    }

    /// <summary>
    /// A few per world (one per 160 × 160 km of land, at least one; 16 at most): inland (land 0.6 or more at the
    /// middle, 0.35 all round the foot), most beside mountain chains (their shoulders), some alone; apart from each other.
    /// Three in ten are calderas. Cones: 900–2,200 m (× size) over the land, slopes averaging 1 in 4–6, a crater 250–600 m
    /// across its rim (× size) 80–200 m deep, a lake in six of ten; 3–6 lava flows down the flanks. Calderas: broad
    /// shields 500–1,100 m high, 9–16 km round, a caldera 2.5–5 km (× size) with cliffs 250–500 m deep, its rim breached
    /// on one side to a third of that.
    /// </summary>
    private void Place(MacroRaster macro, WorldManifest manifest, LandformSetting setting)
    {
        var rng = new RandomStream(LandformCatalog.Seed(manifest, Info));
        var land = macro.Channel(WorldPlan.LandChannel);
        var mountain = macro.Channel(WorldPlan.MountainChannel);
        int landCells = 0;
        var shoulders = new List<int>(); var plains = new List<int>();
        for (int c = 0; c < land.Length; c++)
        {
            if (land[c] > 0) landCells++;
            if (land[c] < .6f) continue;
            if (mountain[c] > .05f && mountain[c] < .45f) shoulders.Add(c);
            else if (mountain[c] <= .05f) plains.Add(c);
        }
        double landKm2 = landCells * macro.Cell * macro.Cell / 1e6;
        int want = Math.Clamp((int)Math.Round(setting.Amount * landKm2 / (160 * 160)), landKm2 > 5000 ? 1 : 0, 16);
        for (int attempt = 0; attempt < want * 40 && _volcanoes.Count < want; attempt++)
        {
            var from = rng.Unit() < .65 && shoulders.Count > 0 ? shoulders : plains;
            if (from.Count == 0) continue;
            int k = from[rng.Int(from.Count)];
            var v = new Volcano { X = macro.CellX(k % macro.Size), Z = macro.CellZ(k / macro.Size), Caldera = rng.Unit() < .3, Seed = rng.Next() };
            if (v.Caldera)
            {
                v.Height = rng.Between(500, 1100) * setting.Size; v.Radius = rng.Between(9000, 16000) * setting.Size; v.Power = 1.5;
                v.Inner = rng.Between(2500, 5000) * setting.Size; v.Depth = rng.Between(250, 500) * setting.Size;
                double a = rng.Unit() * Math.Tau;
                v.BreachX = Math.Cos(a); v.BreachZ = Math.Sin(a); v.Breach = rng.Between(.55, .7);
            }
            else
            {
                v.Height = rng.Between(900, 2200) * setting.Size; v.Radius = v.Height * rng.Between(4, 6); v.Power = rng.Between(1.8, 2.4);
                v.Inner = rng.Between(250, 600) * setting.Size; v.Depth = rng.Between(80, 200) * setting.Size;
                v.Lake = rng.Unit() < .6;
                int flows = 3 + rng.Int(4);
                v.Flows = new (double, double, double, double, double)[flows];
                for (int f = 0; f < flows; f++)
                    v.Flows[f] = (rng.Unit() * Math.Tau, rng.Between(.12, .25), rng.Between(.5, .9), rng.Between(150, 400) * setting.Size, rng.Between(6, 15));
            }
            bool ok = true;
            for (int d = 0; d < 8 && ok; d++)
            {
                double a = d * Math.Tau / 8;
                if (macro.Sample(WorldPlan.LandChannel, v.X + Math.Cos(a) * v.Radius, v.Z + Math.Sin(a) * v.Radius) < .35) ok = false;
            }
            foreach (var o in _volcanoes) if (Math.Sqrt(Sq(o.X - v.X) + Sq(o.Z - v.Z)) < o.Radius + v.Radius + 5000) ok = false;
            if (ok) _volcanoes.Add(v);
        }
    }

    /// <summary>
    /// The volcano's height over the land it stands on (m) at a point: <paramref name="exact"/> with its crater or
    /// caldera cliffs (walking scale), otherwise the shape the raster holds (no crater; the caldera's basin rounded by
    /// the cells anyway). The outline wanders ±6 % (a cone is never a perfect circle). <paramref name="steep"/>: how
    /// steep the ground there is meant to be (0 … 1, for the rock).
    /// </summary>
    private static double Profile(Volcano v, double x, double z, bool exact, out double steep)
    {
        steep = 0;
        double dx = x - v.X, dz = z - v.Z, r0 = Math.Sqrt(dx * dx + dz * dz);
        if (r0 >= v.Radius * 1.1) return 0;
        double r = r0 * (1 + .06 * Noise.Fbm(x, z, v.Radius * .3, 2, 0, v.Seed));
        if (r >= v.Radius) return 0;
        if (!v.Caldera)
        {
            double u = Math.Max(0, (r - v.Inner) / (v.Radius - v.Inner));
            double cone = v.Height * Math.Pow(1 - u, v.Power);
            steep = DetMath.Smooth((v.Power * v.Height / (v.Radius - v.Inner) * Math.Pow(1 - u, v.Power - 1) - .45) / .3);
            if (r >= v.Inner || !exact) return cone;
            // The crater: a bowl under a sharp rim.
            double q = r / v.Inner;
            steep = Math.Max(steep, DetMath.Smooth((q - .55) / .3));
            return v.Height - v.Depth * (1 - q * q);
        }
        // Caldera: a shield outside the rim; inside, cliffs down to a flat floor; the rim breached on one side. Its outline
        // wanders ±10 % at its own scale (a first one was a drawn circle).
        r *= 1 + .1 * Noise.Fbm(x, z, v.Inner * .6, 3, 0, v.Seed + 3);
        if (r >= v.Radius) return 0;
        double breach = 0;
        if (r0 > 1e-6)
        {
            double along = (dx * v.BreachX + dz * v.BreachZ) / r0;
            breach = v.Breach * DetMath.Smooth((along - .93) / .06) * (1 - DetMath.Smooth((r - v.Inner * 1.6) / (v.Inner * .5)));
        }
        double top = v.Height - v.Depth * breach;
        if (r >= v.Inner)
        {
            double u = (r - v.Inner) / (v.Radius - v.Inner);
            return Math.Min(top, v.Height * Math.Pow(1 - u, v.Power) + (top - v.Height) * (1 - u));
        }
        double wall = v.Depth * (exact ? 1 : 3), floor = v.Height - v.Depth;
        double t = DetMath.Smooth((r - (v.Inner - wall)) / wall);
        steep = exact ? DetMath.Smooth(1 - Math.Abs(t - .5) * 2.5) : 0;
        return floor + (top - floor) * t;
    }

    /// <summary>
    /// Walking-scale correction (m): the exact volcano (crater, caldera cliffs, sharp summit) minus what the raster holds
    /// of it, 18–30 gullies down its flanks, and the lava flows — raised lobes 6–15 m thick and 150–400 m wide running down the flanks, wavering, bare
    /// rock. <paramref name="rock"/>: summit, crater and caldera walls, flows.
    /// </summary>
    public override double Ground(double x, double z, double minWavelength, out double rock)
    {
        rock = 0;
        if (_volcanoes.Count == 0 || _raise == null || _macro == null) return 0;
        double g = 0;
        foreach (var v in _volcanoes)
        {
            double dx = x - v.X, dz = z - v.Z, r = Math.Sqrt(dx * dx + dz * dz);
            if (r >= v.Radius * 1.1 + _macro.Cell * 2) continue;
            double fade = DetMath.Smooth((_macro.Sample(WorldPlan.LandChannel, x, z) - .2) / .3);
            double exact = Profile(v, x, z, true, out double steep);
            g += Weight(v, x, z) * fade * exact - _raise.Sample(0, x, z);
            rock = Math.Max(rock, steep * fade);
            if (v.Caldera || r >= v.Radius) continue;
            double angle = Math.Atan2(dz, dx), u = r / v.Radius;
            // Gullies: 18–30 V-shaped channels round the cone (a smooth cone read as a mound), 10–30 m deep, from below the
            // crater to the lower slopes, wavering, spreading apart downhill (noise lines drew hundreds of thin cracks).
            double zone = DetMath.Smooth((u - .06) / .08) * (1 - DetMath.Smooth((u - .6) / .3));
            if (zone > 0)
            {
                int count = 18 + (int)(v.Seed % 13);
                double phase = count * angle + 2.2 * Noise.Fbm(x, z, 1800, 2, 0, v.Seed + 13);
                double gully = DetMath.Smooth((Math.Cos(phase) - .45) / .55) * (.6 + .4 * Noise.Fbm(x, z, 900, 2, 0, v.Seed + 14));
                g -= (10 + 20 * DetMath.Smooth(1 - u / .6)) * Math.Max(0, gully) * zone * fade;
            }
            // Lava flows: lobes down the flanks, each along a wavering line from below the summit.
            foreach (var (a0, from, to, width, thickness) in v.Flows)
            {
                if (u < from - .02 || u > to + .05) continue;
                double a = a0 + .25 * Noise.Fbm(r, a0 * 1000, v.Radius * .25, 2, 0, v.Seed + 9);
                double off = Math.Abs(Math.IEEERemainder(angle - a, Math.Tau)) * r;
                double w = width * (.6 + .8 * (u - from) / Math.Max(.05, to - from)); // (widening downhill)
                double across = 1 - DetMath.Smooth((off - w * .5) / (w * .25));
                if (across <= 0) continue;
                double ends = DetMath.Smooth((u - from) / .03) * (1 - DetMath.Smooth((u - to) / .04));
                double lobe = thickness * across * ends * (1 + .3 * Noise.Fbm(x, z, 120, 2, 0, v.Seed + 11));
                g += lobe * fade;
                rock = Math.Max(rock, .9 * across * ends * fade);
            }
        }
        return g;
    }

    /// <summary>
    /// A quiet cone's crater lake (revision 28): water within 85 % of the crater's radius, its level 40 % of the crater's
    /// depth over the floor's middle (the rim stands far above it, so it is closed in), over ground (metres above the
    /// sea) lower than that; <paramref name="level"/> is absolute. The level is taken from the plan once it is final (the
    /// floor is where erosion left the summit plus the crater).
    /// </summary>
    public bool CraterLake(double x, double z, double ground, out double level)
    {
        level = double.NaN;
        if (_volcanoes.Count == 0 || _raise == null || _macro == null) return false;
        foreach (var v in _volcanoes)
        {
            if (!v.Lake) continue;
            double dx = x - v.X, dz = z - v.Z;
            if (dx * dx + dz * dz > Sq(v.Inner * .85)) continue;
            if (double.IsNaN(v.LakeLevel))
            {
                double floor = _macro.Sample(WorldPlan.HeightChannel, v.X, v.Z) + Profile(v, v.X, v.Z, true, out _) - _raise.Sample(0, v.X, v.Z);
                v.LakeLevel = floor + .4 * v.Depth; // (deterministic: a race writes the same value)
            }
            if (ground < v.LakeLevel) { level = _sea + v.LakeLevel; return true; }
        }
        return false;
    }

    private static double Sq(double v) => v * v;
}
