using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.WorldGen;

/// <summary>
/// Volcanoes and calderas (landform no. 7, terrain revision 28, D246; docs/LANDFORMS.md). A big landform: each cone or
/// caldera is raised in the plan's height raster before erosion, so the rivers form on it and run down its flanks;
/// what the raster's cells cannot hold — the sharp summit, the crater, the caldera's cliffs, lava flows — is added at
/// walking scale (<see cref="Ground"/>: the exact shape minus what the raster holds of it). A caldera's basin is a
/// plan lake (the hydrology fills it to the breach in its rim, where its river leaves); a quiet cone's crater holds a
/// small lake of its own (<see cref="CraterLake"/>). The volcanic biome is drawn onto them (BiomeMap). Revision 29
/// (D247, Tom: "tune first"): lava flows that follow the ground, taller cones (some with a collapsed side), gullies of
/// different depths and lengths, calderas with taller cliffs, islands and older, worn rims. Revision 30 (D248, Tom: "not
/// as smooth"): cinder cones, deep ravines with sharp ridges high on the cones, hummocky old lava fields and rougher
/// lower flanks, rock outcrops on rims and cliffs (<see cref="Detail"/>). Revision 31 (D250, Tom: "I'm really curious about
/// the lava"): active volcanoes, a lava lake in the crater and fresh flows molten down the middle (<see cref="LavaAt"/>,
/// which the game draws as glowing lava).
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
        /// <summary>Revision 29's shapes (the fields below are used only then).</summary>
        public bool Tuned;
        /// <summary>A cone's collapsed side: its direction and how much of the height it took (0 none).</summary>
        public double CollapseX, CollapseZ, Collapse;
        /// <summary>A caldera's island cone: where (offset from the middle), its radius, its height over the floor; and
        /// whether its rim is old and worn.</summary>
        public double IslandX, IslandZ, IslandRadius, IslandHeight, IslandAbove;
        public bool Worn;
        /// <summary>Lava flows traced down the ground (revision 29), once the plan is final.</summary>
        public List<Flow>? Traced;
        /// <summary>The bounds of all its traced flows (a quick reject for points far from them).</summary>
        public double FlowMinX = double.MaxValue, FlowMinZ = double.MaxValue, FlowMaxX = double.MinValue, FlowMaxZ = double.MinValue;
        /// <summary>Revision 30's detail (<see cref="Detail"/>), and its cinder cones: where, radius, height.</summary>
        public bool Detailed;
        /// <summary>Revision 31: an active cone — a lava lake in its crater (instead of water), its first flows fresh.</summary>
        public bool Active;
        public int FreshFlows;
        public double LavaLevel = double.NaN;
        public (double X, double Z, double Radius, double Height)[] Cinders = Array.Empty<(double, double, double, double)>();
        public (double Angle, double Length, double Width, double Thickness)[] FlowStarts = Array.Empty<(double, double, double, double)>();
        /// <summary>The level it is built on: the land's mean height round its foot, before it was raised; revision 29: and
        /// its tilt (m per m along X and Z: a level base cut a moat into a slope).</summary>
        public double Base, TiltX, TiltZ;
    }

    /// <summary>A lava flow traced down the ground: its line every 40 m, its width there, its thickness, and the bounds of
    /// each run of 10 segments (for a quick reject).</summary>
    private sealed class Flow
    {
        public required double[] X, Z, W;
        public required double Thickness;
        public required (double MinX, double MinZ, double MaxX, double MaxZ)[] Chunks;
        public double MinX, MinZ, MaxX, MaxZ;
        public ulong Seed;
    }

    private readonly int _revision;
    private readonly List<Volcano> _volcanoes = new();
    private readonly object _traceLock = new();
    private volatile bool _traced;
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

    /// <summary>Each volcano's middle and what it has (revision 29: a collapsed side, an island, a worn rim) (captures).</summary>
    public IEnumerable<(double X, double Z, string Kind)> Kinds()
    {
        foreach (var v in _volcanoes)
            yield return (v.X, v.Z, string.Join(", ", new[] { v.Caldera ? "caldera" : "cone", v.Collapse > 0 ? "collapsed side" : "",
                v.IslandRadius > 0 ? "island" : "", v.Worn ? "worn rim" : "", v.Lake ? "crater lake" : "", v.Active ? "active (lava lake)" : "" }.Where(k => k.Length > 0)));
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
            if (v.Tuned)
            {
                double sx = 0, sz = 0;
                for (int d = 0; d < 16; d++)
                {
                    double a = d * Math.Tau / 16, h = macro.Sample(WorldPlan.HeightChannel, v.X + Math.Cos(a) * v.Radius * 1.05, v.Z + Math.Sin(a) * v.Radius * 1.05);
                    sx += (h - v.Base) * Math.Cos(a); sz += (h - v.Base) * Math.Sin(a);
                }
                v.TiltX = sx / 8 / (v.Radius * 1.05); v.TiltZ = sz / 8 / (v.Radius * 1.05); // (least squares on the ring)
            }
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
                    // (a caldera's floor stays level inside its rim: tilted, its basin drained over the low side)
                    double tilt = v.Caldera ? DetMath.Smooth((Math.Sqrt(Sq(x - v.X) + Sq(z - v.Z)) - v.Inner * 1.2) / (v.Inner * .6)) : 1;
                    double level = v.Base + tilt * (v.TiltX * (x - v.X) + v.TiltZ * (z - v.Z));
                    height[k] = (float)(height[k] * (1 - w) + w * (level + shape));
                    // (revision 30: rougher lower flanks in patches, smooth ash near the top; before, smoother than the mountains
                    // all over; rough all round, a rugged ring)
                    if (v.Detailed)
                    {
                        double patch = DetMath.Smooth(.5 + 1.2 * Noise.Fbm(x, z, 5000, 2, 0, v.Seed + 81));
                        double m = .08 + .27 * patch * DetMath.Smooth((Math.Sqrt(Sq(x - v.X) + Sq(z - v.Z)) / v.Radius - .3) / .5);
                        mountain[k] = (float)(mountain[k] * (1 - w) + w * m);
                    }
                    else mountain[k] = (float)(mountain[k] * (1 - w) + w * .2 * shape / v.Height); // (kept in this exact form: bit-exact)
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
        if (v.Tuned) return 1 - DetMath.Smooth((r - v.Radius * .6) / (v.Radius * .45)); // (revision 29: a wider blend)
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
            if (_revision >= 29) TunedShape(v, rng, setting);
            else if (v.Caldera)
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
            // (its own random stream: the volcanoes stay where revision 29 put them; first placed inside the if/else above,
            // it cut the chain and revision 29's volcanoes took revision 28's shapes too — caught by the Build 122 fixture)
            if (_revision >= 30) Cinders(v, macro, setting);
            if (_revision >= 31) Activity(v); // (its own random stream too)
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
    /// Revision 29's volcanoes. Cones: 1,200–3,000 m (× size; the tallest stand over the snow line), slopes 1 in 3.5–5,
    /// steeper toward the top (power 2.2–2.8); three in ten with a collapsed side — a horseshoe scar from the summit taking
    /// 20–35 % of the height on one side (no crater lake: it would run out); 3–6 lava flows 2–7 km long (× size), traced
    /// down the ground. Calderas: cliffs 350–700 m deep; an island cone in the lake in 45 of 100 (400–900 m round, 30–150 m
    /// over the water); an old, worn rim in four of ten (its height wandering by a third of the depth, gentler walls).
    /// </summary>
    private static void TunedShape(Volcano v, RandomStream rng, LandformSetting setting)
    {
        v.Tuned = true;
        if (v.Caldera)
        {
            v.Height = rng.Between(500, 1100) * setting.Size; v.Radius = rng.Between(9000, 16000) * setting.Size; v.Power = 1.5;
            v.Inner = rng.Between(2500, 5000) * setting.Size; v.Depth = rng.Between(350, 700) * setting.Size;
            double a = rng.Unit() * Math.Tau;
            v.BreachX = Math.Cos(a); v.BreachZ = Math.Sin(a); v.Breach = rng.Between(.55, .7);
            if (rng.Unit() < .45)
            {
                double ia = rng.Unit() * Math.Tau, id = rng.Between(0, .3) * v.Inner;
                v.IslandX = Math.Cos(ia) * id; v.IslandZ = Math.Sin(ia) * id;
                v.IslandRadius = rng.Between(400, 900) * setting.Size;
                v.IslandAbove = rng.Between(30, 150);
                v.IslandHeight = v.Depth * (1 - v.Breach) + v.IslandAbove; // (set from the lake's level once it is known: SetLakes)
            }
            v.Worn = rng.Unit() < .4;
            return;
        }
        v.Height = rng.Between(1200, 3000) * setting.Size; v.Radius = v.Height * rng.Between(3.5, 5); v.Power = rng.Between(2.2, 2.8);
        v.Inner = rng.Between(250, 600) * setting.Size; v.Depth = rng.Between(80, 200) * setting.Size;
        if (rng.Unit() < .3)
        {
            double a = rng.Unit() * Math.Tau;
            v.CollapseX = Math.Cos(a); v.CollapseZ = Math.Sin(a); v.Collapse = rng.Between(.2, .35);
        }
        v.Lake = v.Collapse == 0 && rng.Unit() < .6;
        int flows = 3 + rng.Int(4);
        v.FlowStarts = new (double, double, double, double)[flows];
        for (int f = 0; f < flows; f++)
            v.FlowStarts[f] = (rng.Unit() * Math.Tau, rng.Between(2000, 7000) * setting.Size, rng.Between(150, 350) * setting.Size, rng.Between(6, 15));
    }

    /// <summary>
    /// Revision 30: cinder cones (their own random stream, from the volcano's seed). Cones: 3–7, half on the flanks (45–90
    /// % of the radius out), half round the foot (100–130 %); calderas: 2–5 on the shield outside the rim. Each 300–700 m
    /// round and 50–150 m high (× size), a crater in its top, on land.
    /// </summary>
    private static void Cinders(Volcano v, MacroRaster macro, LandformSetting setting)
    {
        v.Detailed = true;
        var rng = new RandomStream(v.Seed ^ 0x5C0B1A5EUL);
        int count = v.Caldera ? 2 + rng.Int(4) : 3 + rng.Int(5);
        var list = new List<(double, double, double, double)>();
        for (int n = 0; n < count; n++)
        {
            double a = rng.Unit() * Math.Tau;
            double u = v.Caldera ? rng.Between(Math.Max(.5, v.Inner * 1.6 / v.Radius), 1.2) : rng.Unit() < .5 ? rng.Between(.45, .9) : rng.Between(1, 1.3);
            double cr = rng.Between(300, 700) * setting.Size, ch = rng.Between(50, 150) * setting.Size;
            double x = v.X + Math.Cos(a) * u * v.Radius, z = v.Z + Math.Sin(a) * u * v.Radius;
            if (macro.Sample(WorldPlan.LandChannel, x, z) < .45) continue;
            list.Add((x, z, cr, ch));
        }
        v.Cinders = list.ToArray();
    }

    /// <summary>
    /// Revision 30's walking-scale detail on a volcano (Tom on revision 29: "add some more details ... so they are not as
    /// smooth"): the cinder cones; on a cone, 9–14 deep ravines (70–140 m on a 2 km cone) with sharp rocky ridges between
    /// them high up, from below the crater's rim to 45–65 % of the radius (the smooth ash slopes stay between); hummocky old lava fields
    /// on the lower flanks and round the foot, in patches (blocks and pressure ridges a few metres high, bare rock); rock
    /// outcrops along a crater's rim and a caldera's cliffs. <paramref name="fade"/>: the land fade (none at the sea).
    /// </summary>
    private static double Detail(Volcano v, double x, double z, double r, double fade, ref double rock)
    {
        double g = 0, u = r / v.Radius;
        foreach (var (cx, cz, cr, ch) in v.Cinders)
        {
            double d = Math.Sqrt(Sq(x - cx) + Sq(z - cz));
            if (d >= cr) continue;
            double q = d / cr * (1 + .08 * Noise.Fbm(x, z, cr * .6, 2, 0, v.Seed + 61));
            if (q >= 1) continue;
            g += ch * Math.Pow(1 - q, 1.3) - ch * .45 * (1 - DetMath.Smooth(q / .28));
            rock = Math.Max(rock, .55 * (1 - DetMath.Smooth((q - .5) / .5)));
        }
        if (u > 1.35) return g * fade;
        if (!v.Caldera)
        {
            // Ravines and knife ridges high on the cone, starting below the crater's rim (a ravine through the rim would
            // drain its lake).
            double start = Math.Max(.07, v.Inner * 1.5 / v.Radius);
            double zone = DetMath.Smooth((u - start) / .06) * (1 - DetMath.Smooth((u - .45) / .2));
            if (zone > 0)
            {
                int count = 9 + (int)(v.Seed % 6);
                double c = Math.Cos(count * Math.Atan2(z - v.Z, x - v.X) + 2 * Noise.Fbm(x, z, 3000, 2, 0, v.Seed + 71));
                double ravine = DetMath.Smooth((c - .45) / .55), crest = DetMath.Smooth((-c - .6) / .4);
                double deep = (70 + 70 * DetMath.Smooth(.5 + Noise.Fbm(x, z, 2000, 2, 0, v.Seed + 72))) * Math.Min(1, v.Height / 2000);
                g += (25 * crest - deep * ravine) * zone; // (40–90 m barely showed from the foot through the haze)
                rock = Math.Max(rock, zone * Math.Max(.5 * ravine, .7 * crest));
            }
        }
        // Old lava fields: hummocky, blocky ground on the lower flanks and round the foot, in patches.
        double field = DetMath.Smooth((u - .4) / .15) * (1 - DetMath.Smooth((u - 1.2) / .15))
            * DetMath.Smooth((Noise.Fbm(x, z, 3500, 2, 0, v.Seed + 41) + .05) / .25);
        if (field > 0)
        {
            double ridged = 1 - Math.Abs(Noise.Fbm(x, z, 140, 3, 0, v.Seed + 42));
            g += field * (6 * ridged * ridged - 2 + 2.5 * Noise.Fbm(x, z, 35, 2, 0, v.Seed + 43));
            rock = Math.Max(rock, .75 * field);
        }
        // Rock outcrops on a crater's rim or along a caldera's cliffs.
        double band = v.Caldera ? 1 - DetMath.Smooth(Math.Abs(r - v.Inner) / (v.Inner * .15))
            : 1 - DetMath.Smooth(Math.Abs(r - v.Inner) / (v.Inner * .35));
        if (band > 0)
        {
            double outcrop = Math.Max(0, Noise.Fbm(x, z, 90, 3, 0, v.Seed + 51) - .05);
            g += band * 30 * outcrop;
            rock = Math.Max(rock, .9 * band);
        }
        return g * fade;
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
            if (v.Collapse > 0)
            {
                // A collapsed side (revision 29): a horseshoe amphitheatre — a bowl cut into the summit, its floor sloping
                // away downhill, so it opens on the low side and its head is a steep curved wall (a wedge cut along two
                // radial lines first looked machined). The crater is gone with it there.
                if (r < v.Inner && exact) cone = Math.Min(cone, v.Height - v.Depth * (1 - Sq(r / v.Inner)));
                double rs = v.Radius * .18, px = v.X + v.CollapseX * rs * .7, pz = v.Z + v.CollapseZ * rs * .7;
                double d = Math.Sqrt(Sq(x - px) + Sq(z - pz)) * (1 + .12 * Noise.Fbm(x, z, rs * .5, 2, 0, v.Seed + 31));
                double scar = 1 - DetMath.Smooth((d - rs * .85) / (rs * .3));
                if (scar > 0)
                {
                    double down = (x - px) * v.CollapseX + (z - pz) * v.CollapseZ;
                    double atP = v.Height * Math.Pow(1 - Math.Max(0, (rs * .7 - v.Inner) / (v.Radius - v.Inner)), v.Power);
                    double scarFloor = atP - v.Collapse * v.Height - .3 * down;
                    double cut = scar * Math.Max(0, cone - scarFloor);
                    steep = Math.Max(steep, scar * DetMath.Smooth((cut - 40) / 120) * DetMath.Smooth(-down / rs + .2));
                    cone -= cut;
                }
                return cone;
            }
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
            breach = v.Tuned
                ? v.Breach * DetMath.Smooth((along - .82) / .14) * (1 - DetMath.Smooth((r - v.Inner * 1.6) / (v.Inner * .6))) // (wider: a narrow one was a slot)
                : v.Breach * DetMath.Smooth((along - .93) / .06) * (1 - DetMath.Smooth((r - v.Inner * 1.6) / (v.Inner * .5)));
        }
        double top = v.Height - v.Depth * breach;
        if (v.Worn) top -= v.Depth * .33 * DetMath.Smooth(.5 + Noise.Fbm(x, z, v.Inner * .5, 2, 0, v.Seed + 21)); // (an old rim, worn down unevenly)
        if (r >= v.Inner)
        {
            double u = (r - v.Inner) / (v.Radius - v.Inner);
            return Math.Min(top, v.Height * Math.Pow(1 - u, v.Power) + (top - v.Height) * (1 - u));
        }
        double wall = v.Depth * (exact ? v.Worn ? 2.2 : 1 : 3), floor = v.Height - v.Depth;
        double t = DetMath.Smooth((r - (v.Inner - wall)) / wall);
        steep = exact ? DetMath.Smooth(1 - Math.Abs(t - .5) * (v.Worn ? 4 : 2.5)) : 0;
        double inside = floor + (top - floor) * t;
        if (v.IslandRadius > 0 && exact)
        {
            // An island cone in the lake (revision 29), a small crater at its top.
            double ir = Math.Sqrt(Sq(dx - v.IslandX) + Sq(dz - v.IslandZ)) / v.IslandRadius;
            if (ir < 1)
            {
                double island = floor + v.IslandHeight * Math.Pow(1 - ir, 1.6) - v.IslandHeight * .15 * (1 - DetMath.Smooth(ir / .12));
                if (island > inside) { inside = island; steep = Math.Max(steep, .5 * DetMath.Smooth((.6 - ir) / .3)); }
            }
        }
        return inside;
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
            if (v.Detailed && r < v.Radius * 1.4 + 800)
            {
                double near = DetMath.Smooth((_macro.Sample(WorldPlan.LandChannel, x, z) - .2) / .3);
                g += Detail(v, x, z, r, near, ref rock);
            }
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
            if (v.Tuned)
            {
                // Revision 29: gullies of different depths and lengths, some fading out, a second, shallower set between
                // them lower down (an even starburst before).
                if (u > .05 && u < .95)
                {
                    int count = 16 + (int)(v.Seed % 11);
                    double warp = 3 * Noise.Fbm(x, z, 2200, 2, 0, v.Seed + 13);
                    double depthVary = DetMath.Smooth(.45 + 1.3 * Noise.Fbm(x, z, 2600, 2, 0, v.Seed + 14));
                    double reach = .4 + .35 * DetMath.Smooth(.5 + Noise.Fbm(x, z, 3000, 2, 0, v.Seed + 15));
                    double z1 = DetMath.Smooth((u - .06) / .08) * (1 - DetMath.Smooth((u - reach) / .2));
                    double g1 = DetMath.Smooth((Math.Cos(count * angle + warp) - .5) / .5) * depthVary * z1 * (8 + 26 * DetMath.Smooth(1 - u / .6));
                    double z2 = DetMath.Smooth((u - .25) / .1) * (1 - DetMath.Smooth((u - .85) / .1));
                    double g2 = DetMath.Smooth((Math.Cos(count * 1.7 * angle + warp * 1.3 + 1.1) - .6) / .4) * z2 * 9
                        * DetMath.Smooth(.5 + Noise.Fbm(x, z, 1700, 2, 0, v.Seed + 16));
                    g -= Math.Max(g1, g2) * fade;
                }
            }
            else if (zone > 0)
            {
                int count = 18 + (int)(v.Seed % 13);
                double phase = count * angle + 2.2 * Noise.Fbm(x, z, 1800, 2, 0, v.Seed + 13);
                double gully = DetMath.Smooth((Math.Cos(phase) - .45) / .55) * (.6 + .4 * Noise.Fbm(x, z, 900, 2, 0, v.Seed + 14));
                g -= (10 + 20 * DetMath.Smooth(1 - u / .6)) * Math.Max(0, gully) * zone * fade;
            }
            // Lava flows: lobes down the flanks, each along a wavering line from below the summit.
            if (v.Tuned) continue;
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
        if (_revision >= 29) g += TracedFlows(x, z, ref rock);
        return g;
    }

    /// <summary>
    /// Lava flows (revision 29): raised lobes along lines traced down the ground from vents below the summit — down the
    /// steepest way (with some momentum and a waver), round obstacles, spreading where the ground flattens, stopping
    /// there, at the sea or after 2–7 km. 150–350 m wide at the vent, widening twofold downhill and half again at the end,
    /// 6–15 m thick, their edges lobed; bare rock. (Revision 28's ran straight down the cone, over whatever lay there.)
    /// </summary>
    /// <summary>The nearest point of a flow's line: its distance, the flow's width there, how far along it lies (0 at the
    /// vent … 1 at the end) and which way the line runs there (downhill, unit); false when the point is outside every
    /// run's bounds.</summary>
    private static bool FlowNearest(Flow f, double x, double z, out double best, out double width, out double along, out double dirX, out double dirZ)
    {
        best = double.MaxValue; width = 0; along = 0; dirX = dirZ = 0;
        for (int c = 0; c < f.Chunks.Length; c++)
        {
            var b = f.Chunks[c];
            if (x < b.MinX || x > b.MaxX || z < b.MinZ || z > b.MaxZ) continue;
            int end = Math.Min(f.X.Length - 1, c * 10 + 10);
            for (int i = c * 10; i < end; i++)
            {
                double ax = f.X[i], az = f.Z[i], ex = f.X[i + 1] - ax, ez = f.Z[i + 1] - az, len2 = Math.Max(1e-9, ex * ex + ez * ez);
                double t = Math.Clamp(((x - ax) * ex + (z - az) * ez) / len2, 0, 1);
                double d = Math.Sqrt(Sq(ax + ex * t - x) + Sq(az + ez * t - z));
                if (d < best) { best = d; width = f.W[i] + (f.W[i + 1] - f.W[i]) * t; along = (i + t) / (f.X.Length - 1); dirX = ex; dirZ = ez; }
            }
        }
        double dl = Math.Sqrt(dirX * dirX + dirZ * dirZ);
        if (dl > 1e-9) { dirX /= dl; dirZ /= dl; }
        return best != double.MaxValue;
    }

    private double TracedFlows(double x, double z, ref double rock)
    {
        if (!_traced) Trace();
        double g = 0;
        foreach (var v in _volcanoes)
        {
            if (v.Traced == null || x < v.FlowMinX || x > v.FlowMaxX || z < v.FlowMinZ || z > v.FlowMaxZ) continue;
            foreach (var f in v.Traced)
            {
                if (x < f.MinX || x > f.MaxX || z < f.MinZ || z > f.MaxZ) continue;
                if (!FlowNearest(f, x, z, out double best, out double width, out _, out _, out _)) continue;
                double edge = width * .5 * (1 + .25 * Noise.Fbm(x, z, 160, 2, 0, f.Seed));
                double across = 1 - DetMath.Smooth((best - edge * .7) / (edge * .3 + 1));
                if (across <= 0) continue;
                double lobe = f.Thickness * across * (1 + .3 * Noise.Fbm(x, z, 120, 2, 0, f.Seed + 1));
                if (_revision >= 30) lobe += across * 3 * (1 - Math.Abs(Noise.Fbm(x, z, 45, 2, 0, f.Seed + 2))); // (a blocky crust)
                g = Math.Max(g, lobe);
                rock = Math.Max(rock, .9 * across);
            }
        }
        return g;
    }

    /// <summary>Traces every lava flow once the plan is final (the first time the ground is asked for one).</summary>
    private void Trace()
    {
        lock (_traceLock)
        {
            if (_traced || _macro == null || _raise == null) { _traced = true; return; }
            foreach (var v in _volcanoes)
            {
                if (!v.Tuned || v.Caldera) continue;
                v.Traced = new List<Flow>();
                for (int n = 0; n < v.FlowStarts.Length; n++)
                {
                    var (angle, length, width, thickness) = v.FlowStarts[n];
                    ulong seed = v.Seed + 100 + (ulong)n;
                    double vr = v.Inner * 1.4 + v.Radius * .05;
                    double x = v.X + Math.Cos(angle) * vr, z = v.Z + Math.Sin(angle) * vr, dx = Math.Cos(angle), dz = Math.Sin(angle);
                    var xs = new List<double> { x }; var zs = new List<double> { z }; var ws = new List<double> { width };
                    int flat = 0;
                    for (double run = 0; run < length; run += 40)
                    {
                        double gx = (Ground0(v, x + 20, z) - Ground0(v, x - 20, z)) / 40, gz = (Ground0(v, x, z + 20) - Ground0(v, x, z - 20)) / 40;
                        double grade = Math.Sqrt(gx * gx + gz * gz);
                        if (grade > 1e-6) { dx = dx * .6 - gx / grade * .4; dz = dz * .6 - gz / grade * .4; }
                        double turn = .35 * Noise.Fbm(run, n * 977, 600, 2, 0, seed);
                        double c = Math.Cos(turn), s = Math.Sin(turn), l = Math.Max(1e-9, Math.Sqrt(dx * dx + dz * dz));
                        double nx = (dx * c - dz * s) / l, nz = (dx * s + dz * c) / l;
                        x += nx * 40; z += nz * 40;
                        if (_macro.Sample(WorldPlan.LandChannel, x, z) < .25) break; // (the sea)
                        flat = grade < .02 ? flat + 1 : 0;
                        xs.Add(x); zs.Add(z); ws.Add(width * (1 + run / Math.Max(1, length)) * (flat > 0 ? 1.5 : 1));
                        if (flat > 6) break; // (spread out on the flat)
                    }
                    if (xs.Count < 5) continue;
                    ws[^1] *= 1.3;
                    var chunks = new (double, double, double, double)[(xs.Count - 1 + 9) / 10];
                    double pad = width * 3;
                    for (int c = 0; c < chunks.Length; c++)
                    {
                        double x0 = double.MaxValue, z0 = double.MaxValue, x1 = double.MinValue, z1 = double.MinValue;
                        for (int i = c * 10; i <= Math.Min(xs.Count - 1, c * 10 + 10); i++)
                        { x0 = Math.Min(x0, xs[i]); z0 = Math.Min(z0, zs[i]); x1 = Math.Max(x1, xs[i]); z1 = Math.Max(z1, zs[i]); }
                        chunks[c] = (x0 - pad, z0 - pad, x1 + pad, z1 + pad);
                    }
                    var f = new Flow { X = xs.ToArray(), Z = zs.ToArray(), W = ws.ToArray(), Thickness = thickness, Chunks = chunks, Seed = seed };
                    f.MinX = double.MaxValue; f.MinZ = double.MaxValue; f.MaxX = double.MinValue; f.MaxZ = double.MinValue;
                    foreach (var b in chunks) { f.MinX = Math.Min(f.MinX, b.Item1); f.MinZ = Math.Min(f.MinZ, b.Item2); f.MaxX = Math.Max(f.MaxX, b.Item3); f.MaxZ = Math.Max(f.MaxZ, b.Item4); }
                    v.Traced.Add(f);
                    v.FlowMinX = Math.Min(v.FlowMinX, f.MinX); v.FlowMinZ = Math.Min(v.FlowMinZ, f.MinZ);
                    v.FlowMaxX = Math.Max(v.FlowMaxX, f.MaxX); v.FlowMaxZ = Math.Max(v.FlowMaxZ, f.MaxZ);
                }
            }
            _traced = true;
        }
    }

    /// <summary>The ground a flow runs on (m): the plan's height with the volcano's exact shape, no walking-scale relief.</summary>
    private double Ground0(Volcano v, double x, double z) =>
        _macro!.Sample(WorldPlan.HeightChannel, x, z) + Weight(v, x, z) * Profile(v, x, z, true, out _) - _raise!.Sample(0, x, z);

    /// <summary>
    /// Once the hydrology has made the caldera lakes (revision 29): each island stands 30–150 m over its lake's real
    /// level (taken from the rim as planned, the lake settled higher and drowned the islands).
    /// </summary>
    public void SetLakes(Hydrology hy, double sea)
    {
        foreach (var v in _volcanoes)
        {
            if (!v.Caldera || v.IslandRadius <= 0 || hy.LakeNear(v.X, v.Z) is not (>= 0 and var lake)) continue;
            v.IslandHeight = hy.Lakes[lake].Level - sea - v.Base - (v.Height - v.Depth) + v.IslandAbove;
        }
    }

    /// <summary>On a caldera's island cone here (revision 29: dry ground inside a lake, by design).</summary>
    public bool OnIsland(double x, double z)
    {
        foreach (var v in _volcanoes)
            if (v.Caldera && v.IslandRadius > 0 && Sq(x - v.X - v.IslandX) + Sq(z - v.Z - v.IslandZ) < Sq(v.IslandRadius)) return true;
        return false;
    }

    /// <summary>
    /// Inside a caldera's rim here (revision 29), and the caldera's middle (where its plan lake is): its lake fills all the
    /// ground there below its level — the plan lake reaches only round the raster cells it was found in, and a deeper
    /// floor beyond them lay dry below the water, the lake ending in a squarish outline.
    /// </summary>
    public bool InCaldera(double x, double z, out double cx, out double cz)
    {
        cx = cz = 0;
        foreach (var v in _volcanoes)
        {
            if (!v.Caldera || !v.Tuned || Sq(x - v.X) + Sq(z - v.Z) > Sq(v.Inner * 1.15)) continue;
            cx = v.X; cz = v.Z;
            return true;
        }
        return false;
    }

    /// <summary>
    /// Revision 31: whether a cone is active (its own random stream, from its seed): 45 in 100 cones, not calderas; an active
    /// one has a lava lake in its crater instead of water, and its first 1–2 traced flows are fresh (molten).
    /// </summary>
    private static void Activity(Volcano v)
    {
        if (v.Caldera) return;
        var rng = new RandomStream(v.Seed ^ 0x1A7A1A7AUL);
        v.Active = rng.Unit() < .45;
        if (!v.Active) return;
        v.Lake = false;
        v.FreshFlows = 1 + rng.Int(2);
    }

    /// <summary>
    /// Where the game hangs lights over an active volcano's lava (revision 31): over the lava lake (its middle, heat 1),
    /// then every <paramref name="spacing"/> metres along each fresh flow's molten channel, with its heat there (1 at the
    /// vent, a third at the end).
    /// </summary>
    public IEnumerable<(double X, double Z, double Heat, bool Lake)> LavaLights(double x0, double z0, double spacing)
    {
        if (!_traced) Trace();
        foreach (var v in _volcanoes)
        {
            if (!v.Active || v.X != x0 || v.Z != z0) continue;
            yield return (v.X, v.Z, 1, true);
            if (v.Traced == null) continue;
            for (int n = 0; n < Math.Min(v.FreshFlows, v.Traced.Count); n++)
            {
                var f = v.Traced[n];
                int every = Math.Max(1, (int)Math.Round(spacing / 40));
                for (int i = every / 2; i < f.X.Length * .85; i += every)
                    yield return (f.X[i], f.Z[i], 1 - .65 * i / (f.X.Length - 1), false);
            }
        }
    }

    private (double X0, double Z0, double X1, double Z1)[]? _lavaBoxes;

    /// <summary>
    /// Whether a point lies within reach of any active volcano's lava (D257): its middle ± its radius and the longest a
    /// fresh flow can run (7 km × size, plus its width). The sampler asks every point about lava; outside these boxes the
    /// answer is at once no (asking every volcano cost 13 % of the sampler's speed).
    /// </summary>
    public bool NearLava(double x, double z)
    {
        var boxes = _lavaBoxes;
        if (boxes == null)
        {
            var list = new List<(double, double, double, double)>();
            foreach (var v in _volcanoes)
            {
                if (!v.Active) continue;
                double reach = v.Radius * .1 + v.Inner * 2;
                foreach (var (_, length, width, _) in v.FlowStarts) reach = Math.Max(reach, v.Inner * 1.4 + v.Radius * .05 + length + width * 2);
                list.Add((v.X - reach, v.Z - reach, v.X + reach, v.Z + reach));
            }
            _lavaBoxes = boxes = list.ToArray();
        }
        foreach (var (x0, z0, x1, z1) in boxes) if (x >= x0 && x <= x1 && z >= z0 && z <= z1) return true;
        return false;
    }

    /// <summary>The active volcanoes: middle and radius (the game draws their lava around the nearest).</summary>
    public IEnumerable<(double X, double Z, double Radius)> Active()
    {
        foreach (var v in _volcanoes) if (v.Active) yield return (v.X, v.Z, v.Radius);
    }

    /// <summary>
    /// An active cone's lava lake (revision 31): flat molten rock within 85 % of the crater's radius, 30 % of the crater's
    /// depth over the floor's middle, over ground (metres above the sea) lower than that; <paramref name="level"/> is the
    /// lake's height above the sea (the ground there is raised to it: a lava lake is a level surface, not water).
    /// </summary>
    public bool LavaLake(double x, double z, double ground, out double level)
    {
        level = double.NaN;
        if (_volcanoes.Count == 0 || _raise == null || _macro == null || !NearLava(x, z)) return false;
        foreach (var v in _volcanoes)
        {
            if (!v.Active) continue;
            double dx = x - v.X, dz = z - v.Z;
            if (dx * dx + dz * dz > Sq(v.Inner * .85)) continue;
            if (double.IsNaN(v.LavaLevel))
                v.LavaLevel = _macro.Sample(WorldPlan.HeightChannel, v.X, v.Z) + Profile(v, v.X, v.Z, true, out _) - _raise.Sample(0, v.X, v.Z) + .3 * v.Depth;
            if (ground < v.LavaLevel) { level = v.LavaLevel; return true; }
        }
        return false;
    }

    /// <summary>
    /// How molten and how crusted the ground is here (0 … 1 each; revision 31): the lava lake molten (where the crater's
    /// floor lies below its level), a fresh flow molten in a channel down its middle (half its width, brightest in its
    /// middle; D253) and crusted
    /// across all of it, both cooling along it (a third as hot at its end). For the game's lava look; no effect on the
    /// ground itself.
    /// </summary>
    public (float Molten, float Crust) LavaAt(double x, double z)
    {
        var (m, c, _, _) = LavaFlow(x, z);
        return (m, c);
    }

    /// <summary>
    /// <see cref="LavaAt"/> and which way the lava moves there (D252): a fresh flow down its line, its speed (0 … 1) the
    /// flow's heat; the lava lake slowly out from its middle (0.3 at its shore), as its crust creeps from the upwelling to
    /// the edges.
    /// </summary>
    public (float Molten, float Crust, float DirX, float DirZ) LavaFlow(double x, double z)
    {
        if (_volcanoes.Count == 0 || _revision < 31 || !NearLava(x, z)) return (0, 0, 0, 0);
        if (!_traced) Trace();
        double molten = 0, crust = 0, dirX = 0, dirZ = 0, lead = 0;
        foreach (var v in _volcanoes)
        {
            if (!v.Active) continue;
            double r = Math.Sqrt(Sq(x - v.X) + Sq(z - v.Z));
            if (r < v.Inner)
            {
                // (the lake: where the crater's exact bowl lies below the lake's level, 30 % of the depth over its floor)
                double bowl = Profile(v, x, z, true, out _);
                double lake = DetMath.Smooth((v.Height - .7 * v.Depth - bowl) / 3);
                molten = Math.Max(molten, lake);
                if (lake > lead && r > 1e-6) { lead = lake; double out_ = .3 * Math.Min(1, r / (v.Inner * .55)); dirX = (x - v.X) / r * out_; dirZ = (z - v.Z) / r * out_; }
            }
            if (v.Traced == null || x < v.FlowMinX || x > v.FlowMaxX || z < v.FlowMinZ || z > v.FlowMaxZ) continue;
            for (int n = 0; n < Math.Min(v.FreshFlows, v.Traced.Count); n++)
            {
                var f = v.Traced[n];
                if (x < f.MinX || x > f.MaxX || z < f.MinZ || z > f.MaxZ || !FlowNearest(f, x, z, out double best, out double width, out double along, out double tx, out double tz)) continue;
                double heat = 1 - .65 * along;
                double across = 1 - DetMath.Smooth((best - width * .35) / (width * .15 + 1));
                // (D253, Noor: "broader molten areas": the channel half the flow's width, brightest in its middle and falling off
                // toward the cooling edges — a fifth of it drew thin lines)
                double core = (1 - DetMath.Smooth((best - width * .12) / (width * .16 + 1))) * (.55 + .45 * (1 - DetMath.Smooth(best / (width * .14 + 1))));
                crust = Math.Max(crust, across * heat);
                molten = Math.Max(molten, core * heat * (1 - DetMath.Smooth((along - .85) / .15)));
                if (across * heat > lead) { lead = across * heat; dirX = tx * heat; dirZ = tz * heat; }
            }
        }
        return ((float)molten, (float)crust, (float)dirX, (float)dirZ);
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
