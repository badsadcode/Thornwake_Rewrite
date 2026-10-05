using Thornwake.Hydraulics;
using Thornwake.WorldGen;

/// <summary>Local shallow-water simulation (M6): conservation, downhill flow, pooling, sinks, moving window.</summary>
internal static class SimulationTests
{
    public static void Register(string? filter)
    {
        T.Run("Lakes: no dry ground below the surface at the lake's edge (revision 10)", LakeEdges, filter);
        T.Run("Water: mass conserved, runs downhill", Downhill, filter);
        T.Run("Water: pools level in a basin", Basin, filter);
        T.Run("Water: generated rivers absorb it", Sinks, filter);
        T.Run("Water: window moves, dormant chunks resume", Window, filter);
        T.Run("Water: real terrain ground and sinks", RealTerrain, filter);
        T.Run("Water: step cost with rain over the window", Cost, filter);
        T.Run("Water: puddles soak away and leave wet ground", Soak, filter);
        T.Run("Edits: dig and raise the generated ground", Edits, filter);
        T.Run("Edits: water follows a dug channel", DugChannel, filter);
        T.Run("Edits: a channel dug from a river fills from it", RiverFed, filter);
        T.Run("Edits: real river and lake feed dug ground", RealRiverFed, filter);
        T.Run("Edits: a trench dug through a river bed and bank shows one water surface", RiverTrench, filter);
        T.Run("Edits: a trench dug along a river bank fills from the river", BankTrench, filter);
        T.Run("Tools: strength, hardness, budget, flatten, directional cut", Tools, filter);
        T.Run("Waves: heights match the displaced surface; sea states; shallows", Waves, filter);
    }

    private static Func<long, long, ChunkGround> Ground(Func<double, double, double> height, Func<double, double, bool>? sink = null) => (cx, cz) =>
    {
        int n = LocalWater.ChunkCells;
        var floor = new float[n * n]; var wet = new bool[n * n];
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                double x = (cx * n + i + .5) * LocalWater.Cell, z = (cz * n + j + .5) * LocalWater.Cell;
                floor[j * n + i] = (float)height(x, z);
                wet[j * n + i] = sink?.Invoke(x, z) ?? false;
            }
        return new ChunkGround(floor, wet);
    };

    private static void Balance(LocalWater w, string label) =>
        T.Near(w.Volume() + w.Absorbed + w.Spilled + w.Soaked, w.Added + w.Drawn, Math.Max(1e-3, w.Added * 2e-4 + w.Drawn * 2e-6), label + ": volume balance");

    private static void Downhill()
    {
        var w = new LocalWater(Ground((x, z) => 100 - x * .05));
        w.Follow(0, 0);
        w.Add(-20, 0, 20);
        for (int i = 0; i < 600; i++) w.Step(1 / 60f);
        Balance(w, "slope");
        // Most water has moved toward +x (downhill).
        double up = 0, down = 0;
        for (double x = -60; x < 60; x += .5)
            for (double z = -60; z < 60; z += .5)
            {
                double d = w.DepthAt(x, z);
                if (x < -20) up += d; else down += d;
            }
        T.Check(down > up * 3, $"water runs downhill (uphill {up:0.0}, downhill {down:0.0})");
    }

    private static void Basin()
    {
        var w = new LocalWater(Ground((x, z) => 50 + .02 * (x * x + z * z))) { Infiltration = 0 };
        w.Follow(0, 0);
        w.Add(8, -5, 30);
        for (int i = 0; i < 3600; i++) w.Step(1 / 60f);
        Balance(w, "basin");
        double min = double.MaxValue, max = double.MinValue;
        for (double x = -20; x <= 20; x += .5)
            for (double z = -20; z <= 20; z += .5)
            {
                float s = w.SurfaceAt(x, z);
                if (float.IsNaN(s) || w.DepthAt(x, z) < .1) continue;
                min = Math.Min(min, s); max = Math.Max(max, s);
            }
        T.Note($"basin pool surface range {max - min:0.000} m");
        T.Check(max - min < .02, $"pool surface is level (range {max - min:0.000} m)");
        T.Near(w.Volume(), 30, .05, "nothing lost from a closed basin");
    }

    private static void Sinks()
    {
        // A slope running into a "river" strip at x > 20.
        var w = new LocalWater(Ground((x, z) => 100 - x * .05, (x, z) => x > 20));
        w.Follow(0, 0);
        w.Add(-10, 3, 15);
        for (int i = 0; i < 2400; i++) w.Step(1 / 60f);
        Balance(w, "sink");
        T.Check(w.Absorbed > 14, $"river absorbed the runoff ({w.Absorbed:0.00} of 15 m³)");
    }

    private static void Window()
    {
        var w = new LocalWater(Ground((x, z) => 50 + .02 * (x * x + z * z))) { Infiltration = 0 };
        w.Follow(0, 0);
        w.Add(0, 0, 10);
        for (int i = 0; i < 300; i++) w.Step(1 / 60f);
        float before = w.DepthAt(0, 0);
        w.Follow(1000, 0);
        T.Check(w.DormantChunks > 0 && w.DepthAt(0, 0) == 0, "water left behind is dormant");
        T.Near(w.Volume(), 10, 1e-3, "dormant water is kept");
        w.Follow(0, 0);
        T.Check(w.DormantChunks == 0, "dormant chunks resumed");
        T.Near(w.DepthAt(0, 0), before, 1e-6, "same depth after coming back");
        // A small move keeps simulating without reloading.
        w.Follow(20, 0);
        T.Near(w.Volume(), 10, 1e-3, "volume after a small move");
    }

    private static void Soak()
    {
        // A 3 m³ puddle in a shallow hollow: some soaks into the dry soil at first, then the soil is saturated and the
        // puddle lasts (only slow evaporation and seepage).
        var w = new LocalWater(Ground((x, z) => 50 + .002 * (x * x + z * z)));
        w.Follow(0, 0);
        w.Add(0, 0, 3);
        for (int i = 0; i < 60 * 600; i++) w.Step(1 / 60f);
        Balance(w, "soak");
        T.Note($"after 10 minutes: {w.Volume():0.00} m³ left, {w.Soaked:0.00} m³ soaked in");
        T.Check(w.Soaked > .1 && w.Volume() > 1.5, "dry soil soaks some water, then the puddle lasts");
        T.Check(w.WetnessAt(0, 0) > 0, "ground wet where the puddle is");
        // A dug pond filled by 5 minutes of rain still holds most of its water 20 minutes after the rain.
        var p = new LocalWater(Ground((x, z) => 50 - 1.5 * Math.Max(0, 1 - (x * x + z * z) / 64.0)));
        p.Follow(0, 0);
        p.RainRate = 6e-5f;
        for (int i = 0; i < 60 * 300; i++) p.Step(1 / 60f);
        p.RainRate = 0;
        double inPond() { double v = 0; for (double x = -8; x <= 8; x += .5) for (double z = -8; z <= 8; z += .5) v += p.DepthAt(x, z) * .25; return v; }
        double filled = inPond();
        for (int i = 0; i < 60 * 1200; i++) p.Step(1 / 60f);
        double later = inPond();
        T.Note($"pond: {filled:0.00} m³ after the rain, {later:0.00} m³ 20 minutes later");
        T.Check(filled > .3 && later > filled * .6, "a rain-filled pond lasts after the rain");
        Balance(p, "pond");
    }

    private static void Edits()
    {
        var s = CoreTests.Sampler("mountainous", 42);
        var f = s.Plan.Layout.Land[0];
        double x = Math.Round(f.Center.X + 1234.5), z = Math.Round(f.Center.Z - 777.25); // on a 1 m edit vertex
        double before = s.Height(x, z), beside = s.Height(x + 40, z);
        s.Edits.Apply(x, z, 3, -1.5);
        T.Near(s.Height(x, z), before - 1.5, 1e-6, "brush centre lowered by the full amount");
        T.Near(s.Height(x + 40, z), beside, 0, "ground outside the brush unchanged");
        T.Check(s.Edits.Delta(x + 2, z) < 0 && s.Edits.Delta(x + 2, z) > -1.5, "smooth brush edge");
        // Raise ground out of a river: dry there.
        var reach = s.Plan.Hydrology.Reaches.First(r => r.Catchment[^1] > 500 && !r.Distributary);
        var p = reach.Points[reach.Points.Length / 2];
        T.Check(s.Sample(p.X, p.Z).Water == WaterKind.River, "river centre is wet");
        s.Edits.Apply(p.X, p.Z, 6, 20);
        T.Check(s.Sample(p.X, p.Z).Water == WaterKind.None, "ground raised out of the river is dry");
        s.Edits.Clear();
        T.Near(s.Height(x, z), before, 0, "clearing edits restores the generated ground");
        T.Check(s.Sample(p.X, p.Z).Water == WaterKind.River, "and the river");
    }

    private static void DugChannel()
    {
        // A level basin full of water next to dry flat ground; digging a channel lets the water run out into it.
        var edits = new TerrainEdits();
        Func<double, double, double> ground = (x, z) => (Math.Abs(x) < 8 && Math.Abs(z) < 8 ? 49 : 50) + edits.Delta(x, z);
        var w = new LocalWater(Ground(ground)) { Infiltration = 0 };
        w.Follow(0, 0);
        for (double x = -7; x <= 7; x += .5) for (double z = -7; z <= 7; z += .5) w.Add(x, z, .9 * .25);
        for (int i = 0; i < 600; i++) w.Step(1 / 60f);
        double outside = 0;
        for (double x = 10; x < 40; x += .5) outside += w.DepthAt(x, 0);
        T.Check(outside < .01, "water held in the basin");
        for (double x = 6; x <= 40; x += .5) edits.Apply(x, 0, 1.5, -1.6 - x * .01);
        w.ReloadGround(0, -5, 45, 5);
        for (int i = 0; i < 1800; i++) w.Step(1 / 60f);
        outside = 0;
        for (double x = 12; x < 40; x += .5) outside += w.DepthAt(x, 0);
        T.Note($"after digging: water along the channel sums to {outside:0.00} m of depth over 56 cells");
        T.Check(outside > .5, "water flows into the dug channel");
        Balance(w, "channel");
    }

    private static void RiverFed()
    {
        // A river (fixed surface 49.8 m over a 48 m bed) at x > 20; dry land at 50 m. Dig a channel from x = 2 to the river.
        var edits = new TerrainEdits();
        Func<long, long, ChunkGround> load = (cx, cz) =>
        {
            int n = LocalWater.ChunkCells;
            var floor = new float[n * n]; var sink = new bool[n * n]; var level = new float[n * n];
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    double x = (cx * n + i + .5) * LocalWater.Cell, z = (cz * n + j + .5) * LocalWater.Cell;
                    bool river = x > 20;
                    floor[j * n + i] = (float)((river ? 48 : 50) + edits.Delta(x, z));
                    sink[j * n + i] = river; level[j * n + i] = river ? 49.8f : float.NaN;
                }
            return new ChunkGround(floor, sink, level);
        };
        var w = new LocalWater(load) { Infiltration = 0 };
        w.Follow(0, 0);
        for (int i = 0; i < 300; i++) w.Step(1 / 60f);
        T.Check(w.Volume() < 1e-6 && w.Drawn < 1e-6, "undisturbed banks: the river stays in its bed (no cost, no water)");
        for (double x = 2; x <= 21; x += .5) edits.Apply(Math.Round(x), 0, 1.6, -.9 / 3);
        w.ReloadGround(-5, -5, 25, 5);
        for (int i = 0; i < 1800; i++) w.Step(1 / 60f);
        float s = w.SurfaceAt(8, 0);
        T.Note($"channel surface at 8 m: {s:0.000} m (river 49.8 m), {w.Drawn:0.0} m³ drawn from the river");
        T.Check(!float.IsNaN(s) && Math.Abs(s - 49.8) < .05, "the dug channel fills to the river's level");
        Balance(w, "river-fed channel");
    }

    private static void RealRiverFed()
    {
        var plan = CoreTests.Sampler("mountainous", 42).Plan;
        foreach (var kind in new[] { "river", "lake" })
        {
            var s = new TerrainSampler(plan); // own edit layer
            double px, pz, dx, dz;
            if (kind == "river")
            {
                var reach = plan.Hydrology.Reaches.First(r => r.Catchment[^1] > 300 && !r.Distributary && r.Points.Length > 40);
                int k = reach.Points.Length / 2;
                var a = reach.Points[k]; var b = reach.Points[k + 1];
                double tx = b.X - a.X, tz = b.Z - a.Z, len = Math.Sqrt(tx * tx + tz * tz);
                dx = -tz / len; dz = tx / len; px = a.X; pz = a.Z;
            }
            else
            {
                var lake = plan.Hydrology.Lakes.OrderByDescending(l => l.Area).First();
                px = lake.Center.X; pz = lake.Center.Z; dx = 1; dz = 0;
            }
            // Walk out to the shore, then dig a ~2 m deep trench from 3 m inside the water 20 m inland.
            double shore = 0;
            while (s.Sample(px + dx * shore, pz + dz * shore).Water != WaterKind.None && shore < 20000) shore += 1;
            double level = s.Sample(px + dx * (shore - 3), pz + dz * (shore - 3)).WaterLevel;
            for (double t = shore - 3; t <= shore + 20; t += 1) s.Edits.Apply(px + dx * t, pz + dz * t, 1.4, -2.2);
            var w = new LocalWater((cx, cz) => LocalWater.FromSampler(s, cx, cz)) { Infiltration = 0 };
            double ex = px + dx * (shore + 10), ez = pz + dz * (shore + 10);
            w.Follow(ex, ez);
            for (int i = 0; i < 60 * 40; i++) w.Step(1 / 60f);
            float surface = w.SurfaceAt(ex, ez);
            T.Note($"{kind}: water level {level:0.00} m, dug ground 10 m inland {s.Height(ex, ez):0.00} m, simulated surface {surface:0.00} m, drawn {w.Drawn:0.0} m³");
            // The river's level varies along the breach (and a breached levee floods the lower land behind), so the
            // trench fills to within a few decimetres of the sampled level.
            T.Check(!float.IsNaN(surface) && surface - s.Height(ex, ez) > .05 && Math.Abs(surface - level) < .35, $"{kind} water fills the trench");
            Balance(w, kind);
        }
    }

    private static void RiverTrench()
    {
        var plan = CoreTests.Sampler("mountainous", 42).Plan;
        var s = new TerrainSampler(plan);
        var reach = plan.Hydrology.Reaches.First(r => r.Catchment[^1] > 300 && !r.Distributary && r.Points.Length > 40);
        int k = reach.Points.Length / 2;
        var a = reach.Points[k]; var b = reach.Points[k + 1];
        double tx = b.X - a.X, tz = b.Z - a.Z, len = Math.Sqrt(tx * tx + tz * tz), dx = -tz / len, dz = tx / len;
        double shore = 0;
        while (s.Sample(a.X + dx * shore, a.Z + dz * shore).Water != WaterKind.None && shore < 2000) shore += 1;
        // A 2.5 m deep trench from the middle of the river out through the bank, 12 m inland (like Tom's dig).
        for (double t = 0; t <= shore + 12; t += 1) s.Edits.Apply(a.X + dx * t, a.Z + dz * t, 2, -2.5);
        var w = new LocalWater((cx, cz) => LocalWater.FromSampler(s, cx, cz)) { Infiltration = 0 };
        w.Follow(a.X + dx * shore, a.Z + dz * shore);
        for (int i = 0; i < 60 * 60; i++) w.Step(1 / 60f);
        // Highest river surface beside the breach (the river slopes: the trench fills to about its upstream end).
        double highest = double.MinValue;
        for (double u = -4; u <= 4; u += .5)
        {
            var r = new TerrainSampler(plan).Sample(a.X + dx * (shore - 1) + tx / len * u, a.Z + dz * (shore - 1) + tz / len * u);
            if (r.Water != WaterKind.None) highest = Math.Max(highest, r.WaterLevel);
        }
        int n = LocalWater.ChunkCells + 1, missing = 0, above = 0, cells = 0;
        double worstAbove = 0;
        var floor = new float[n * n]; var depth = new float[n * n]; var vx = new float[n * n]; var vz = new float[n * n]; var wet = new float[n * n];
        for (double t = 0; t <= shore + 12; t += .5)
        {
            double x = a.X + dx * t, z = a.Z + dz * t;
            var original = new TerrainSampler(plan).Sample(x, z);
            double level = original.Water != WaterKind.None ? original.WaterLevel : s.Sample(a.X + dx * Math.Max(0, shore - 3), a.Z + dz * Math.Max(0, shore - 3)).WaterLevel;
            long cx = (long)Math.Floor(x / LocalWater.ChunkSize), cz = (long)Math.Floor(z / LocalWater.ChunkSize);
            if (!w.ChunkView(cx, cz, floor, depth, vx, vz, wet)) continue;
            int i = (int)Math.Floor((x - cx * LocalWater.ChunkSize) / LocalWater.Cell), j = (int)Math.Floor((z - cz * LocalWater.ChunkSize) / LocalWater.Cell);
            int q = j * n + i;
            double ground = floor[q], surface = floor[q] + depth[q];
            if (ground > level - .05) continue; // dug ground still above the water: dry is right
            cells++;
            if (depth[q] < .02) missing++;
            else if (surface > Math.Max(level, highest) + .05) { above++; worstAbove = Math.Max(worstAbove, surface - level); if (above < 8) T.Note($"  t {t:0.0} orig {original.Water} level {level:0.00} surface {surface:0.00} ground {ground:0.00} shore {shore}"); }
        }
        T.Note($"trench: {cells} dug cells below the river surface, {missing} without water, {above} drawn above the river (worst +{worstAbove:0.00} m)");
        T.Check(cells > 10, "the trench reaches below the river");
        T.Check(missing == 0, "every dug cell below the river shows water");
        T.Check(above == 0, "no water drawn above the river surface");
        Balance(w, "trench");
    }

    private static void BankTrench()
    {
        var plan = CoreTests.Sampler("mountainous", 42).Plan;
        var s = new TerrainSampler(plan);
        var reach = plan.Hydrology.Reaches.First(r => r.Catchment[^1] > 300 && !r.Distributary && r.Points.Length > 40);
        int k = reach.Points.Length / 2;
        var a = reach.Points[k]; var b = reach.Points[k + 4];
        double tx = b.X - a.X, tz = b.Z - a.Z, len = Math.Sqrt(tx * tx + tz * tz), dx = -tz / len, dz = tx / len;
        tx /= len; tz /= len;
        double shore = 0;
        while (s.Sample(a.X + dx * shore, a.Z + dz * shore).Water != WaterKind.None && shore < 2000) shore += .5;
        double level = s.Sample(a.X + dx * (shore - 1), a.Z + dz * (shore - 1)).WaterLevel;
        // Like Tom's: a 4 m dig brush run along the bank (cutting into the river's edge), 2 m deep, 30 m long.
        for (double u = -15; u <= 15; u += 1) s.Edits.Apply(a.X + dx * (shore + 1.5) + tx * u, a.Z + dz * (shore + 1.5) + tz * u, 4, -2);
        var w = new LocalWater((cx, cz) => LocalWater.FromSampler(s, cx, cz)) { Infiltration = 0 };
        w.Follow(a.X + dx * shore, a.Z + dz * shore);
        int n = LocalWater.ChunkCells + 1;
        var floor = new float[n * n]; var depth = new float[n * n]; var vx = new float[n * n]; var vz = new float[n * n]; var wet = new float[n * n];
        (int Cells, int Dry) Count()
        {
            int cells = 0, dry = 0;
            for (double u = -12; u <= 12; u += .5)
                for (double v = shore - 1; v <= shore + 4; v += .5)
                {
                    double x = a.X + dx * v + tx * u, z = a.Z + dz * v + tz * u;
                    long cx = (long)Math.Floor(x / LocalWater.ChunkSize), cz = (long)Math.Floor(z / LocalWater.ChunkSize);
                    if (!w.ChunkView(cx, cz, floor, depth, vx, vz, wet)) continue;
                    int i = (int)Math.Floor((x - cx * LocalWater.ChunkSize) / LocalWater.Cell), j = (int)Math.Floor((z - cz * LocalWater.ChunkSize) / LocalWater.Cell);
                    int q = j * n + i;
                    if (floor[q] > level - .15 || s.Edits.Delta(x, z) > -.02) continue; // only dug ground clearly below the river
                    cells++;
                    if (depth[q] < .02) dry++;
                }
            return (cells, dry);
        }
        for (int i = 0; i < 60 * 10; i++) w.Step(1 / 60f);
        var early = Count();
        for (int i = 0; i < 60 * 50; i++) w.Step(1 / 60f);
        var late = Count();
        T.Note($"bank trench: {late.Cells} dug cells below the river; dry after 10 s: {early.Dry}, after 60 s: {late.Dry}; drawn from the river {w.Drawn:0.0} m³");
        T.Check(late.Cells > 20, "the trench reaches below the river");
        T.Check(late.Dry == 0, "the trench fills with water from the river");
        Balance(w, "bank trench");
    }

    private static void Tools()
    {
        var plan = CoreTests.Sampler("mountainous", 42).Plan;
        var s = new TerrainSampler(plan);
        var f = plan.Layout.Land[0];
        // Find soft lowland and hard mountain ground.
        var rng = new Random(5);
        (double X, double Z) soft = default, hard = default;
        for (int i = 0; i < 20000 && (soft == default || hard == default); i++)
        {
            double x = Math.Round(f.Center.X + (rng.NextDouble() - .5) * f.RadiusX), z = Math.Round(f.Center.Z + (rng.NextDouble() - .5) * f.RadiusX);
            var t = s.Sample(x, z);
            if (t.Water != WaterKind.None) continue;
            if (soft == default && t.Mountain < .01 && Math.Abs(s.Height(x + 1, z) - s.Height(x - 1, z)) < .1) soft = (x, z);
            if (hard == default && t.Mountain > .6) hard = (x, z);
        }
        var down = (0.0, -1.0, 0.0);
        double h0 = s.Height(soft.X, soft.Z);
        var r = TerrainSculpt.Apply(s, BrushMode.Dig, BrushShape.Round, 1.1, .22, (soft.X, h0, soft.Z), down, .12);
        T.Near(s.Height(soft.X, soft.Z), h0 - .22, 1e-4, "one shovel stroke lowers soft ground by its strength");
        T.Check(r.Volume < 0 && r.Volume > -1, $"a stroke moves a sensible volume ({r.Volume:0.00} m³)");
        double hh = s.Height(hard.X, hard.Z);
        var rock = TerrainSculpt.Apply(s, BrushMode.Dig, BrushShape.Round, 1.1, .22, (hard.X, hh, hard.Z), down, .12);
        T.Check(rock.Hardness > .6 && Math.Abs(rock.Volume) < Math.Abs(r.Volume) * .3, $"the shovel barely scratches rock ({rock.Volume:0.000} m³)");
        var pick = TerrainSculpt.Apply(s, BrushMode.Dig, BrushShape.Directional, .9, .3, (hard.X, s.Height(hard.X, hard.Z), hard.Z), (0.0, -.3, 1.0), .9);
        T.Check(Math.Abs(pick.Volume) > Math.Abs(rock.Volume) * 3, $"the pickaxe breaks rock ({pick.Volume:0.000} m³)");
        // Budget: raising with a nearly empty bag places only what is in it.
        var raise = TerrainSculpt.Apply(s, BrushMode.Raise, BrushShape.Round, 1.1, .22, (soft.X + 10, s.Height(soft.X + 10, soft.Z), soft.Z), down, 1, .05);
        T.Near(raise.Volume, .05, 1e-3, "raising is limited by the soil carried");
        // Flatten: repeated hoe strokes bring the ground to the aimed height.
        double target = s.Height(soft.X + 20, soft.Z) + .3;
        for (int i = 0; i < 10; i++) TerrainSculpt.Apply(s, BrushMode.Flatten, BrushShape.RoundHard, 1.3, .08, (soft.X + 20, target, soft.Z), down);
        T.Near(s.Height(soft.X + 20, soft.Z), target, .02, "flatten reaches the aimed height");
        // Directional dig into a slope: the cut follows the aiming ray into the ground beyond the hit point.
        double sx = soft.X + 40, sz = soft.Z;
        s.Edits.Modify(sx - 10, sz - 10, sx + 10, sz + 10, (x, z, c) => c + Math.Max(0, 6 - Math.Abs(x - (sx + 4)) * 1.2)); // a ridge east of the hit
        double before = s.Height(sx + 2, sz);
        TerrainSculpt.Apply(s, BrushMode.Dig, BrushShape.Directional, 1, 5, (sx, s.Height(sx, sz) + .5, sz), (1.0, 0.0, 0.0));
        T.Check(s.Height(sx + 2, sz) < before - .5, "a horizontal directional dig cuts into the slope ahead");
    }

    private static void Waves()
    {
        foreach (double state in new[] { 0.0, .3, 1.0 })
        {
            var w = new OceanWaves(state);
            var rng = new Random(2);
            double worst = 0, peak = 0;
            for (int i = 0; i < 2000; i++)
            {
                double x = (rng.NextDouble() - .5) * 1e5, z = (rng.NextDouble() - .5) * 1e5, t = rng.NextDouble() * 1000;
                // A particle displaced from (x, z) ends at (x + dx, z + dz) with height dy: Height there must equal dy.
                var d = w.Displacement(x, z, t);
                worst = Math.Max(worst, Math.Abs(w.Height(x + d.X, z + d.Z, t) - d.Y));
                peak = Math.Max(peak, d.Y);
            }
            T.Note($"sea state {state:0.0}: wave crests up to {peak:0.00} m, height query error {worst * 1000:0.0} mm");
            T.Check(worst < .02, "Height() inverts the Gerstner motion");
            if (state == 0) T.Check(peak < .4, "calm sea is calm");
            if (state == 1) T.Check(peak > 2.5, "storm waves are big");
        }
        var storm = new OceanWaves(1);
        T.Check(Math.Abs(storm.Height(10, 20, 5, .5)) < .1 * storm.WaveHeight, "waves die out in shallow water");
        T.Check(Math.Abs(storm.Height(10, 20, 5, 1000, lake: true)) < .4 * storm.WaveHeight, "lakes carry only small waves");
    }

    private static void Cost()
    {
        var w = new LocalWater(Ground((x, z) => 50 + 3 * Math.Sin(x * .05) * Math.Cos(z * .07) - x * .01));
        w.Follow(0, 0);
        w.RainRate = 5e-5f;
        for (int i = 0; i < 60; i++) w.Step(1 / 60f);
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < 240; i++) w.Step(1 / 60f);
        double ms = sw.Elapsed.TotalMilliseconds / 240;
        T.Note($"full {LocalWater.Side}² window ({LocalWater.Side * LocalWater.Side} cells) under rain: {ms:0.00} ms per step");
        Balance(w, "rain");
        T.Check(ms < 20, "a step stays affordable");
    }

    private static void RealTerrain()
    {
        var s = CoreTests.Sampler("mountainous", 42);
        var hy = s.Plan.Hydrology;
        var reach = hy.Reaches.First(r => r.Catchment[^1] > 300 && !r.Distributary && r.Points.Length > 20);
        var p = reach.Points[reach.Points.Length / 2];
        var w = new LocalWater((cx, cz) => LocalWater.FromSampler(s, cx, cz));
        w.Follow(p.X, p.Z);
        // Mesh-consistent floor: at a 2 m vertex the floor equals the rendered vertex height.
        double vx = Math.Floor(p.X / 2) * 2 + 20.25, vz = Math.Floor(p.Z / 2) * 2 + 20.25;
        T.Check(!float.IsNaN(w.SurfaceAt(vx, vz)) || w.DepthAt(vx, vz) == 0, "dry land starts dry");
        // Pour on the bank 30 m from the river: within a minute most of it has joined the river.
        double off = reach.Width[reach.Points.Length / 2] * .5 + 30;
        var q = reach.Points[reach.Points.Length / 2 + 1];
        double tx = q.X - p.X, tz = q.Z - p.Z, len = Math.Sqrt(tx * tx + tz * tz);
        double bx = p.X - tz / len * off, bz = p.Z + tx / len * off;
        w.Add(bx, bz, 8);
        for (int i = 0; i < 3600; i++) w.Step(1 / 60f);
        Balance(w, "real terrain");
        T.Note($"poured 8 m³ beside a {reach.Width[reach.Points.Length / 2]:0} m river: {w.Absorbed:0.0} m³ reached water bodies, " +
            $"{w.Volume():0.0} m³ still on land, {w.Spilled:0.0} m³ left the window");
        T.Check(w.Absorbed + w.Spilled > 1, "runoff drains away from the bank");
    }

    /// <summary>
    /// Tom (build 11): lakes cut off along straight lines. Around every lake, ground within its reach that lies below
    /// the surface must be lake (revision 10); the old 3×3-cell rule left such ground dry (straight dry walls).
    /// </summary>
    private static void LakeEdges()
    {
        int dryOld = 0, dryNew = 0, samples = 0;
        foreach (var (id, seed) in new[] { ("balanced", 42), ("vast-world", 7319), ("archipelago", 42) })
        {
            var s10 = CoreTests.Sampler(id, seed);
            var s9 = new TerrainSampler(new WorldPlan(s10.Plan.Manifest, style: s10.Plan.Style with { Revision = 9 }));
            var hy = s10.Plan.Hydrology;
            double cell = s10.Plan.Macro.Cell;
            for (int l = 0; l < Math.Min(hy.Lakes.Count, 12); l++)
            {
                var lake = hy.Lakes[l];
                // A ring of probes from the lake's centre outward to 3 cells past its cells.
                for (int a = 0; a < 48; a++)
                {
                    double ang = a * Math.Tau / 48;
                    for (double r = 0; r < 40 * cell; r += cell * .25)
                    {
                        double x = lake.Center.X + Math.Cos(ang) * r, z = lake.Center.Z + Math.Sin(ang) * r;
                        if (hy.LakeDistance(x, z, out double d) != l || d > 1.8) continue;
                        samples++;
                        var t9 = s9.Sample(x, z, 4); var t10 = s10.Sample(x, z, 4);
                        if (t9.Water == WaterKind.None && t9.Height < lake.Level - .05 && !(t9.Water == WaterKind.River)) dryOld++;
                        if (t10.Water == WaterKind.None && t10.Height < lake.Level - .05 && d < 1.6) dryNew++;
                    }
                }
            }
        }
        T.Note($"{samples} probes near lake edges: dry ground below the surface — revision 9: {dryOld}, revision 10: {dryNew}");
        T.Check(samples > 500 && dryNew == 0, "no dry ground below a lake's surface within its reach");
    }
}
