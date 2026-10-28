using System.Linq;
using Thornwake.Hydraulics;
using Thornwake.WorldGen;

/// <summary>Local shallow-water simulation (M6): conservation, downhill flow, pooling, sinks, moving window.</summary>
internal static class SimulationTests
{
    public static void Register(string? filter)
    {
        T.Run("Lakes: no dry ground below the surface at the lake's edge (revision 10 and later)", LakeEdges, filter);
        T.Run("Lakes: outlets keep the lake's level inside the lake (revision 15)", LakeOutlets, filter);
        T.Run("Lakes: no sea pocket inside a lake (revision 16)", LakeSeaPockets, filter);
        T.Run("Wetlands: real pools, never dry ground below a pool surface (revision 11)", Wetlands, filter);
        T.Run("Landforms: dunes, mesas and needle spires where their biomes are (revision 12)", Landforms, filter);
        T.Run("Landforms: each module's switch changes only its own landform (revision 17)", LandformSwitches, filter);
        T.Run("Landforms: water wins with every module at its largest (revision 17)", LandformWater, filter);
        T.Run("Landforms: escarpments and plateaus — cliffs inland, water keeps its place (revision 18)", EscarpmentForms, filter);
        T.Run("Landforms: no combs, fins or cracks at escarpments and mesas (revision 19)", NoCombs, filter);
        T.Run("Landforms: waterfalls drop the river at a step, water stays above its ground (revision 20)", Waterfalls, filter);
        T.Run("Landforms: canyons — raised land beside rivers that keep their course and level, no cliffs of its own (revisions 21, 22)", CanyonForms, filter);
        T.Run("Landforms: cirques hold closed-in tarns high in the mountains; rivers and lakes unchanged (revision 23)", CirqueForms, filter);
        T.Run("Water: mass conserved, runs downhill", Downhill, filter);
        T.Run("Water: rain erosion is slow, wears the stream's bed, keeps the soil", Erosion, filter);
        T.Run("Water: pools level in a basin", Basin, filter);
        T.Run("Water: generated rivers absorb it", Sinks, filter);
        T.Run("Water: window moves, dormant chunks resume", Window, filter);
        T.Run("Water: real terrain ground and sinks", RealTerrain, filter);
        T.Run("Water: step cost with rain over the window", Cost, filter);
        T.Run("Water: puddles soak away and leave wet ground", Soak, filter);
        T.Run("Water: a rain film on a bank does not redraw the river beside it", RainBank, filter);
        T.Run("Rivers: no river perched above the dry ground beside it (revision 13 confluences)", Perched, filter);
        T.Run("Edits: dig and raise the generated ground", Edits, filter);
        T.Run("Edits: chunks copied to another world give the same ground (multiplayer)", EditsCopied, filter);
        T.Run("Edits: erosion far apart (after a teleport) touches only its chunks, quickly", ErosionFarApart, filter);
        T.Run("Removals: felled trees as a world change (taken, sent, applied, put back)", Removals, filter);
        T.Run("Edits: water follows a dug channel", DugChannel, filter);
        T.Run("Edits: a channel dug from a river fills from it", RiverFed, filter);
        T.Run("Edits: real river and lake feed dug ground", RealRiverFed, filter);
        T.Run("Edits: a trench dug through a river bed and bank shows one water surface", RiverTrench, filter);
        T.Run("Edits: a trench dug along a river bank fills from the river", BankTrench, filter);
        T.Run("Tools: strength, hardness, budget, flatten, directional cut", Tools, filter);
        T.Run("Waves: heights match the displaced surface; sea states; shallows", Waves, filter);
        T.Run("Spectrum waves (experimental 2): steepness caps, heights match the surface, calm vs storm", Spectrum, filter);
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

    // Round 31 (Tom: streams that form in the rain "could erode the ground — REALLY SLOWLY"): a spring's stream down a
    // shallow groove for 10 minutes. Off: nothing changes. On: the bed is worn, by millimetres to centimetres, and every
    // bit of soil is accounted for (worn = settled + carried off + still in the water).
    private static void Erosion()
    {
        LocalWater Run(float rate)
        {
            var w = new LocalWater(Ground((x, z) => 100 - x * .05 + .03 * Math.Abs(z))) { ErosionRate = rate };
            w.Follow(0, 0);
            w.Sources.Add(new LocalWater.Source { X = -50, Z = 0, Rate = .6f });
            for (int i = 0; i < 600 * 30; i++) w.Step(1 / 30f);
            return w;
        }
        var off = Run(0);
        T.Check(off.TakeErosion(0) is not { Count: > 0 }, "erosion off: the ground does not change");
        var on = Run(1);
        double suspended = on.Suspended, lost = on.SedimentLost;
        var cells = on.TakeErosion(0) ?? new();
        double cut = cells.Where(c => c.Dz < 0).Sum(c => -c.Dz) * LocalWater.Cell * LocalWater.Cell;
        double fill = cells.Where(c => c.Dz > 0).Sum(c => c.Dz) * LocalWater.Cell * LocalWater.Cell;
        double deepest = cells.Count > 0 ? -cells.Min(c => c.Dz) : 0;
        T.Note($"10 min of a 0.6 m³/s stream: worn {cut * 1000:0.0} L, settled {fill * 1000:0.0} L, carried off {lost * 1000:0.0} L, in the water {suspended * 1000:0.0} L; deepest cut {deepest * 100:0.00} cm (≈ {deepest * 600:0.0} cm an hour)");
        T.Check(cut > 0 && deepest > 1e-4, "the stream wears its bed");
        T.Check(deepest * 6 < .1, "slowly: under 10 cm an hour where the stream is strongest");
        T.Near(cut, fill + lost + suspended, Math.Max(1e-6, cut * 1e-3), "soil conserved (worn = settled + carried off + in the water)");
    }

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

    private static void Perched()
    {
        // Tom's desert confluence (Mountain kingdoms 42): two side rivers crossing the main river's floodplain kept their
        // levels 2–3 m above the dry floor beside them (walls of water). Count dry points within 2 m of river water
        // whose ground lies more than 0.5 m below that water.
        foreach (int revision in new[] { 12, 13 })
        {
            var b = CoreTests.Sampler("mountainous", 42);
            var s = new TerrainSampler(new WorldPlan(b.Plan.Manifest, style: b.Plan.Style with { Revision = revision }));
            const double step = 2;
            int nx = 81, nz = 71, river = 0, perched = 0;
            var lvl = new double[nx * nz]; var ground = new double[nx * nz];
            for (int j = 0; j < nz; j++)
                for (int i = 0; i < nx; i++)
                {
                    var t = s.Sample(45280 + i * step, 136060 + j * step);
                    lvl[j * nx + i] = t.Water == WaterKind.River ? t.WaterLevel : double.NaN; ground[j * nx + i] = t.Height;
                    if (t.Water == WaterKind.River) river++;
                }
            for (int j = 1; j < nz - 1; j++)
                for (int i = 1; i < nx - 1; i++)
                {
                    int k = j * nx + i;
                    if (!double.IsNaN(lvl[k])) continue;
                    double highest = double.NegativeInfinity;
                    foreach (int n in new[] { k - 1, k + 1, k - nx, k + nx }) if (!double.IsNaN(lvl[n])) highest = Math.Max(highest, lvl[n]);
                    if (ground[k] < highest - .5) perched++;
                }
            T.Note($"revision {revision}: {river} river points, {perched} dry points beside the water lying > 0.5 m below it");
            if (revision >= 13) T.Check(perched <= river / 100, "no river perched above the dry ground beside it");
        }
    }

    private static void RainBank()
    {
        // A gentle slope to a river bank at x = 20, the river beyond (surface 99.5 m, 10 cm below the bank top). Rain wets the bank with a thin film; the view must
        // not redraw the river's own cells over it (it did, without the river's current: clear patches ending in a
        // chunk's straight edge, Tom's build 19 screenshot).
        int nc = LocalWater.ChunkCells;
        var w = new LocalWater((cx, cz) =>
        {
            var floor = new float[nc * nc]; var sink = new bool[nc * nc]; var level = new float[nc * nc];
            for (int j = 0; j < nc; j++)
                for (int i = 0; i < nc; i++)
                {
                    double x = (cx * nc + i + .5) * LocalWater.Cell, z = (cz * nc + j + .5) * LocalWater.Cell;
                    int k = j * nc + i;
                    floor[k] = (float)(x < 20 ? 100 - x * .02 : 99.2 - (x - 20) * .02); // a 0.4 m bank down to the river bed
                    sink[k] = x > 20; level[k] = x > 20 ? 99.5f : float.NaN;
                }
            return new ChunkGround(floor, sink, level, new bool[nc * nc]); // nothing dug
        }) { RainRate = 5e-5f };
        w.Follow(10, 0);
        for (int i = 0; i < 900; i++) w.Step(1 / 60f);
        int n = nc + 1, redrawn = 0, film = 0;
        var floorV = new float[n * n]; var depthV = new float[n * n]; var vx = new float[n * n]; var vz = new float[n * n]; var wetV = new float[n * n];
        for (long cx = -2; cx <= 3; cx++)
            for (long cz = -2; cz <= 2; cz++)
            {
                if (!w.ChunkView(cx, cz, floorV, depthV, vx, vz, wetV)) continue;
                for (int j = 0; j < nc; j++)
                    for (int i = 0; i < nc; i++)
                    {
                        double x = (cx * nc + i + .5) * LocalWater.Cell;
                        int k = j * n + i;
                        if (x > 20.5 && depthV[k] > .008f) redrawn++; // the view draws cells deeper than 8 mm
                        if (x < 19 && depthV[k] > .008f) film++;
                    }
            }
        T.Note($"rain on the bank: {film} wet cells drawn on the bank, {redrawn} river cells redrawn");
        T.Check(redrawn == 0, "a thin rain film does not make the view redraw the river");
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

    /// <summary>Multiplayer spike: the host sends changed chunks; a player applying them has the same ground, a late
    /// joiner who receives every chunk too, and an emptied chunk removes the edit.</summary>
    private static void EditsCopied()
    {
        var host = CoreTests.Sampler("mountainous", 42);
        var f = host.Plan.Layout.Land[0];
        double x = Math.Round(f.Center.X + 333), z = Math.Round(f.Center.Z + 211);
        host.Edits.TakeChanged();
        host.Edits.Apply(x, z, 3, -1.2);
        host.Edits.Apply(x + 31, z, 2, .8); // across a chunk edge
        var changed = host.Edits.TakeChanged();
        T.Check(changed.Count >= 2, $"the edits touch {changed.Count} chunks, reported as changed");
        T.Check(host.Edits.TakeChanged().Count == 0, "taken once");
        var client = CoreTests.Sampler("mountainous", 42);
        foreach (var (cx, cz) in changed) client.Edits.SetChunk(cx, cz, host.Edits.GetChunk(cx, cz));
        double worst = 0;
        for (double dx = -6; dx <= 40; dx += .37) for (double dz = -6; dz <= 6; dz += .41)
            worst = Math.Max(worst, Math.Abs(host.Height(x + dx, z + dz) - client.Height(x + dx, z + dz)));
        T.Near(worst, 0, 0, "the client's ground matches the host's");
        T.Check(client.Edits.TakeChanged().Count == 0, "chunks from the host are not sent back");
        var late = CoreTests.Sampler("mountainous", 42);
        foreach (var (cx, cz) in host.Edits.ChunkKeys()) late.Edits.SetChunk(cx, cz, host.Edits.GetChunk(cx, cz));
        T.Near(late.Height(x, z), host.Height(x, z), 0, "a late joiner gets the same ground");
        host.Edits.Clear();
        foreach (var (cx, cz) in host.Edits.TakeChanged()) client.Edits.SetChunk(cx, cz, host.Edits.GetChunk(cx, cz));
        T.Check(client.Edits.ChunkCount == 0, "clearing on the host clears the client");
    }

    // D106: rain erosion from the place left behind and the new place in one step. It used to change a rectangle over
    // both (millions of vertices after a long teleport: the host froze for 12–48 s).
    private static void ErosionFarApart()
    {
        var edits = new TerrainEdits();
        var reference = new TerrainEdits();
        var deltas = new Dictionary<(long X, long Z), double> { [(10, 20)] = -.003, [(11, 20)] = -.002, [(31, 20)] = .001, [(32, 20)] = .004, [(-6010, 7005)] = -.005 };
        var watch = System.Diagnostics.Stopwatch.StartNew();
        var touched = edits.Add(deltas);
        T.Check(watch.ElapsedMilliseconds < 50, $"took {watch.ElapsedMilliseconds} ms");
        T.Check(touched.Count == 3, $"{touched.Count} chunks touched (3 expected)");
        T.Check(edits.TakeChanged().Count == 3, "the touched chunks are reported as changed");
        foreach (var ((x, z), d) in deltas)
        {
            reference.Modify(x, z, x, z, (vx, vz, current) => vx == x && vz == z ? current + d : current);
            T.Near(edits.At(x, z), reference.At(x, z), 0, $"vertex {x}, {z} as by Modify");
        }
        T.Near(edits.At(12, 20), 0, 0, "neighbours untouched");
        edits.Add(deltas);
        T.Near(edits.At(-6010, 7005), -.01, 1e-6, "added again");
    }

    // D108: a felled tree is its lattice cell in ObjectRemovals; the host sends the changed keys like edit chunks.
    private static void Removals()
    {
        var host = new ObjectRemovals();
        T.Check(host.Remove(ObjectRemovals.Trees, 12, -7, 30.5), "felled");
        T.Check(!host.Remove(ObjectRemovals.Trees, 12, -7, 31), "felling it again does nothing");
        T.Check(host.Contains(ObjectRemovals.Trees, 12, -7) && !host.Contains(ObjectRemovals.Trees, 12, -6), "only that one");
        var changed = host.TakeChanged();
        T.Check(changed.Count == 1 && host.TakeChanged().Count == 0, "reported once as changed");
        var client = new ObjectRemovals();
        foreach (var (layer, x, z) in changed) client.Set(layer, x, z, host.Get(layer, x, z));
        T.Check(client.Contains(ObjectRemovals.Trees, 12, -7) && client.Get(ObjectRemovals.Trees, 12, -7) == 30.5, "the client has it, with the time");
        T.Check(client.TakeChanged().Count == 0, "applied from the host: not sent back");
        T.Check(host.Restore(ObjectRemovals.Trees, 12, -7) && host.Count == 0, "put back (regrowth)");
        foreach (var (layer, x, z) in host.TakeChanged()) client.Set(layer, x, z, host.Get(layer, x, z));
        T.Check(client.Count == 0, "and gone on the client too");
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

    private static void Spectrum()
    {
        Func<double, double> full = _ => 1;
        double windX = .8, windZ = -.6;
        double lastHs = 0;
        foreach (double state in new[] { 0.0, .25, .5, .75, 1.0 })
        {
            var w = new SpectrumWaves(state);
            T.Check(w.Hs > lastHs, "wave height grows steadily with the sea state");
            lastHs = w.Hs;
            var rng = new Random(3);
            double worst = 0, peak = 0, minHs = 1e9, maxHs = 0;
            for (int i = 0; i < 300; i++)
            {
                double x = (rng.NextDouble() - .5) * 2e5, z = (rng.NextDouble() - .5) * 2e5, t = rng.NextDouble() * 1000;
                var local = w.Local(x, z, windX, windZ, false);
                double sumA2 = 0, chop = 0;
                for (int j = 0; j < SpectrumWaves.WindCount; j++) sumA2 += local.Amp[j] * local.Amp[j];
                for (int j = 0; j < SpectrumWaves.Count; j++) chop += local.Q[j] * w.Waves[j].K * local.Amp[j];
                double hs = 4 * Math.Sqrt(sumA2 / 2);
                minHs = Math.Min(minHs, hs); maxHs = Math.Max(maxHs, hs);
                T.Check(chop < .99, "crests sharpen but never loop over");
                double g = w.Group(x, z, t, windX, windZ);
                var d = w.Displacement(x, z, t, full, local, g);
                double h = w.Height(x + d.X, z + d.Z, t, full, windX, windZ);
                worst = Math.Max(worst, Math.Abs(h - d.Y));
                peak = Math.Max(peak, d.Y);
            }
            T.Note($"spectrum sea state {state:0.00}: Hs {w.Hs:0.0} m (local {minHs:0.0}–{maxHs:0.0}), crests up to {peak:0.0} m, height error {worst * 1000:0.0} mm");
            T.Check(Math.Abs(minHs - w.Hs) < .01 * w.Hs + .01 && Math.Abs(maxHs - w.Hs) < .01 * w.Hs + .01, "same wave height everywhere (direction weights renormalised)");
            T.Check(worst < .1, "Height() inverts the spectrum sea's motion");
            if (state == 0) T.Check(peak < .6, "calm spectrum sea is calm");
            if (state == 1) T.Check(peak > 8, "storm spectrum sea is huge");
        }
        // Headings differ from place to place (sea and lakes), and swells come from their own directions.
        var sea = new SpectrumWaves(.5);
        double minDot = 1, minLake = 1, minSwell = 1;
        var h0 = sea.Headings(0, 0, windX, windZ, false);
        var s0 = h0;
        for (int i = 1; i < 40; i++)
        {
            var h = sea.Headings(i * 4000, i * 2500, windX, windZ, false);
            var hl = sea.Headings(i * 4000, i * 2500, windX, windZ, true);
            minDot = Math.Min(minDot, h.Wx * h0.Wx + h.Wz * h0.Wz);
            minLake = Math.Min(minLake, hl.Wx * h.Wx + hl.Wz * h.Wz);
            minSwell = Math.Min(minSwell, h.Sx * s0.Sx + h.Sz * s0.Sz);
        }
        T.Note($"heading spread: sea {Math.Acos(minDot) * 180 / Math.PI:0}°, lake vs sea {Math.Acos(minLake) * 180 / Math.PI:0}°, swell {Math.Acos(minSwell) * 180 / Math.PI:0}°");
        T.Check(minDot < Math.Cos(.5), "wind-sea heading varies across the ocean");
        T.Check(minLake < Math.Cos(.5), "lakes have their own wave headings");
        T.Check(minSwell < Math.Cos(1), "swells come from different directions in different seas");
        // Coast: in the shallows the wind sea and the swell turn toward the shore (bed rising toward +z here).
        var open = sea.Headings(0, 0, -1, 0, false);
        var coastal = sea.Headings(0, 0, -1, 0, false, 0, .03, SpectrumWaves.Coastal(4, 0, .03, false));
        T.Note($"heading toward the shore (+z): open sea {open.Wz:0.00}, coastal shallows {coastal.Wz:0.00}; swell {open.Sz:0.00} → {coastal.Sz:0.00}");
        T.Check(coastal.Wz > .8 && coastal.Sz > .9, "coastal waves turn toward the shore");
        T.Check(SpectrumWaves.Coastal(20, 0, .03, false) == 0 && SpectrumWaves.Coastal(4, 0, .03, true) == 0, "open water and lakes keep their own headings");
        // Smooth storms: a small sea-state change only nudges the surface (fixed wavelengths: no jumps).
        var a = new SpectrumWaves(.6); var b = new SpectrumWaves(.61);
        double jump = 0;
        for (int i = 0; i < 50; i++)
        {
            double x = 1000 + i * 37, z = -800 + i * 53;
            jump = Math.Max(jump, Math.Abs(a.Height(x, z, 50, full, windX, windZ) - b.Height(x, z, 50, full, windX, windZ)));
        }
        T.Note($"sea state .60 → .61: surface moves at most {jump * 100:0.0} cm");
        T.Check(jump < .25, "the sea grows smoothly with the sea state");
        // World anchored: re-centring the scene does not move the waves.
        var o = new SpectrumWaves(.5);
        double before = o.Height(5000, 3000, 20, full, windX, windZ);
        o.SetOrigin(1536, -3072);
        double after = o.Height(5000 - 1536, 3000 + 3072, 20, full, windX, windZ);
        T.Near(after, before, .01, "waves stay put when the float origin moves");
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
    /// <summary>
    /// Wetland pools (revision 11, round 8): in a marsh, a bayou and a fen of the vast world, standing water covers
    /// about the share the biome asks for, is no deeper than its depth, and no dry ground next to a pool lies below
    /// the pool's surface. Revision 10 has no pools.
    /// </summary>
    // The 2,048 km vast world (seed 7319) that the wetland and landform probes use.
    private static readonly Lazy<WorldPlan> Vast = new(() => new WorldPlan(WorldManifest.Create(7319,
        WorldRecipe.Catalog(CoreTests.Profiles).First(r => r.Id == "vast-world").Resolve(new WorldChoices { SizeKm = 2048 }), worldId: CoreTests.FixedWorldId)));

    /// <summary>
    /// Biome landforms (revision 12, round 9): a desert has transverse dunes (relief within a few hundred metres, ridges
    /// tens of metres apart), the badlands have mesas (ground raised tens of metres above revision 11), needle rocks rise
    /// as tall thin columns; no feature where its biome is absent; the same everywhere it is sampled (deterministic).
    /// </summary>
    private static void Landforms()
    {
        var plan = Vast.Value;
        var s = new TerrainSampler(plan);
        var s11 = new TerrainSampler(new WorldPlan(plan.Manifest, style: plan.Style with { Revision = 11 }));
        // Dunes: relief along a 600 m line, and how many ridges cross it.
        double x0 = 330000, z0 = -170000, lo = double.MaxValue, hi = double.MinValue;
        var line = new double[600];
        for (int i = 0; i < 600; i++) { line[i] = s.Height(x0 + i * .7, z0 + i * .7); lo = Math.Min(lo, line[i]); hi = Math.Max(hi, line[i]); }
        int ridges = 0;
        for (int i = 20; i < 580; i++)
        {
            bool top = line[i] > line[i - 20] + .5 && line[i] > line[i + 20] + .5;
            for (int k = i - 20; k <= i + 20 && top; k++) if (line[k] > line[i]) top = false;
            if (top) ridges++;
        }
        T.Note($"desert: {hi - lo:0.0} m relief over 600 m, {ridges} dune crests");
        T.Check(hi - lo > 4 && ridges >= 3 && ridges <= 20, $"desert dunes ({hi - lo:0.0} m relief, {ridges} crests)");
        // Mesas: badlands ground raised well above revision 11 in places.
        double raised = 0;
        for (int i = 0; i < 400; i++) raised = Math.Max(raised, s.Height(-531000 + i * 10, -250000) - s11.Height(-531000 + i * 10, -250000));
        T.Note($"badlands: up to {raised:0} m above revision 11 (mesas)");
        T.Check(raised > 30, $"badlands mesas ({raised:0} m)");
        // Needle spires: columns standing > 12 m above the ground 15 m away, a few of them in a 400 m square.
        int spires = 0;
        for (double z = 350000; z < 350400; z += 2)
            for (double x = -410000; x < -409600; x += 2)
            {
                double h = s.Height(x, z), around = Math.Min(Math.Min(s.Height(x + 15, z), s.Height(x - 15, z)), Math.Min(s.Height(x, z + 15), s.Height(x, z - 15)));
                if (h - around > 12) spires++;
            }
        T.Note($"needle rocks: {spires} points of 40,000 stand > 12 m above the ground 15 m away");
        T.Check(spires > 20, $"needle spires ({spires})");
        // A temperate forest has none of them (identical to revision 11 away from wetland/landform biomes).
        var (a, _, share) = plan.Biomes.Sample(-558000, -398000);
        int same = 0;
        for (int i = 0; i < 50; i++) if (s.Height(-558000 + i * 7, -398000) == s11.Height(-558000 + i * 7, -398000)) same++;
        T.Check(plan.Biomes.Pool[a].Def.Id == "temperate-forest" && same == 50, $"no landforms in {plan.Biomes.Pool[a].Def.Id} ({same}/50 unchanged)");
        T.Check(s.Height(x0 + 123.4, z0 - 56.7) == new TerrainSampler(plan).Height(x0 + 123.4, z0 - 56.7), "deterministic");
    }

    /// <summary>A sampler of the vast test world with these landform settings (others as designed).</summary>
    private static TerrainSampler WithLandforms(int revision = TerrainStyle.CurrentRevision, params (LandformInfo Info, double Amount, double Size)[] settings)
    {
        var plan = Vast.Value;
        return new TerrainSampler(new WorldPlan(plan.Manifest, style: plan.Style with
        {
            Revision = revision, Landforms = settings.ToDictionary(t => t.Info.Id, t => new LandformSetting(t.Amount, t.Size)),
        }));
    }

    private static void LandformSwitches()
    {
        var s = new TerrainSampler(Vast.Value);
        // Probe lines in the desert, the badlands and the needle rocks (see Landforms above).
        var lines = new (string Name, double X, double Z, double Dx, double Dz)[]
            { ("desert", 330000, -170000, .7, .7), ("badlands", -531000, -250000, 10, 0), ("needle rocks", -410000, 350000, 2, 0) };
        double[] Line(TerrainSampler t, int k) { var l = lines[k]; return Enumerable.Range(0, 400).Select(i => t.Height(l.X + i * l.Dx, l.Z + i * l.Dz)).ToArray(); }
        var designed = Enumerable.Range(0, lines.Length).Select(k => Line(s, k)).ToArray();
        int Changed(double[] a, double[] b) => a.Where((v, i) => v != b[i]).Count();
        // Each module off: its own landform changes; every point where its biome strength is 0 stays bit for bit the same
        // (biomes blend at their edges, so a trace of dunes may reach the needle rocks: those points may change).
        var plan = Vast.Value;
        double Strength(LandformInfo info, int k, int i)
        {
            var l = lines[k];
            if (!plan.Biomes.TerrainAt(l.X + i * l.Dx, l.Z + i * l.Dz, out var mods, allBiomes: true)) return 0;
            return info == LandformCatalog.Dunes ? mods.Dunes : info == LandformCatalog.Mesas ? mods.Mesas : mods.Spires;
        }
        foreach (var (info, own) in new[] { (LandformCatalog.Dunes, 0), (LandformCatalog.Mesas, 1), (LandformCatalog.Spires, 2) })
        {
            var off = WithLandforms(settings: (info, 0, 1));
            var lineOff = Enumerable.Range(0, lines.Length).Select(k => Line(off, k)).ToArray();
            int ownChanged = Changed(designed[own], lineOff[own]), outside = 0, wrong = 0;
            for (int k = 0; k < lines.Length; k++)
                for (int i = 0; i < 400; i++)
                    if (Strength(info, k, i) <= 0) { outside++; if (designed[k][i] != lineOff[k][i]) wrong++; }
            T.Note($"{info.Name} off: {ownChanged}/400 heights changed in the {lines[own].Name}; {outside} points outside its biomes, {wrong} changed");
            T.Check(ownChanged > 40, $"{info.Name} off changes the {lines[own].Name} ({ownChanged}/400)");
            T.Check(outside > 300 && wrong == 0, $"{info.Name} off leaves ground outside its biomes unchanged ({wrong} of {outside} changed)");
        }
        // Mesas only add ground: off is never higher; common raises more of the badlands than designed, rare less.
        var mesasOff = Line(WithLandforms(settings: (LandformCatalog.Mesas, 0, 1)), 1);
        int Raised(double[] l) => l.Where((v, i) => v - mesasOff[i] > 5).Count();
        int rare = Raised(Line(WithLandforms(settings: (LandformCatalog.Mesas, .4, 1)), 1)), normal = Raised(designed[1]),
            common = Raised(Line(WithLandforms(settings: (LandformCatalog.Mesas, 1.6, 1)), 1)),
            everywhere = Raised(Line(WithLandforms(settings: (LandformCatalog.Mesas, 2.5, 1)), 1));
        T.Note($"badlands raised > 5 m by mesas: rare {rare}, normal {normal}, common {common}, everywhere {everywhere} of 400");
        T.Check(designed[1].Where((v, i) => v < mesasOff[i] - 1e-9).Count() == 0, "mesas only add ground");
        T.Check(rare < normal * .7 && common > normal * 1.15 && everywhere > common, $"mesa amount orders the raised share ({rare} < {normal} < {common} < {everywhere})");
        // Spires: bigger size gives taller needles; dunes: bigger size gives more relief.
        double Relief(double[] l) => l.Max() - l.Min();
        double spiresBig = Relief(Line(WithLandforms(settings: (LandformCatalog.Spires, 1, 2)), 2)), spiresNormal = Relief(designed[2]);
        double dunesBig = Relief(Line(WithLandforms(settings: (LandformCatalog.Dunes, 1, 2)), 0)), dunesNormal = Relief(designed[0]);
        T.Note($"relief: needle rocks {spiresNormal:0} m → {spiresBig:0} m at size 2, desert {dunesNormal:0.0} m → {dunesBig:0.0} m");
        T.Check(spiresBig > spiresNormal * 1.3, "bigger spires stand taller");
        T.Check(dunesBig > dunesNormal * 1.3, "bigger dunes give more relief");
        // Older revisions ignore the settings (their worlds never change).
        var old = WithLandforms(16, (LandformCatalog.Dunes, 0, 1), (LandformCatalog.Mesas, 2.5, 2), (LandformCatalog.Spires, 0, 1));
        var old16 = WithLandforms(16);
        T.Check(Enumerable.Range(0, lines.Length).All(k => Changed(Line(old16, k), Line(old, k)) == 0), "revision 16 ignores the landform settings");
    }

    private static void LandformWater()
    {
        // Every water point of the designed world among the landforms keeps its water and level with every module at
        // its largest: rivers carve through, lakes keep their basins, the sea is never touched.
        var plan = Vast.Value;
        var s = new TerrainSampler(plan);
        var big = WithLandforms(settings: new[] { LandformCatalog.Dunes, LandformCatalog.Mesas, LandformCatalog.Spires }.Select(i => (i, 2.5, 2.0)).ToArray());
        double half = plan.Profile.WorldSize * .5;
        int water = 0, lost = 0, dryCells = 0, changed = 0;
        for (double z = -half; z < half; z += 1500)
            for (double x = -half; x < half; x += 1500)
            {
                if (!plan.Biomes.TerrainAt(x, z, out var mods, allBiomes: true) || mods.Dunes + mods.Mesas + mods.Spires <= 0) continue;
                // A small patch at each spot, so river channels and lake shores are met, not only open ground.
                for (int j = 0; j < 6; j++)
                    for (int i = 0; i < 6; i++)
                    {
                        double px = x + i * 9, pz = z + j * 9;
                        var a = s.Sample(px, pz);
                        var b = big.Sample(px, pz);
                        if (a.Height != b.Height) changed++;
                        if (a.Water == WaterKind.None) { dryCells++; continue; }
                        water++;
                        // (a river crossing another's valley floor flows at that floor, which follows the ground: its
                        // level may move by a hair, revision 13; lakes and the sea keep theirs exactly)
                        double slack = a.Water == WaterKind.River ? .01 : 0;
                        if (b.Water != a.Water || Math.Abs(b.WaterLevel - a.WaterLevel) > slack || !(b.Height < b.WaterLevel))
                            if (lost++ < 3) Console.WriteLine($"       {a.Water} at {px:0},{pz:0}: level {a.WaterLevel:R} → {b.Water} {b.WaterLevel:R}, ground {a.Height:R} → {b.Height:R}");
                    }
            }
        T.Note($"landform ground: {dryCells} dry and {water} water points sampled, {changed} heights changed by the largest settings");
        T.Check(changed > 1000, $"the largest settings change the ground ({changed})");
        T.Check(water > 100, $"water points among the landforms ({water})");
        T.Check(lost == 0, $"every water point keeps its water and level ({lost} lost)");
    }

    private static void EscarpmentForms()
    {
        int lines = 0, cliffs = 0, steps = 0, probes = 0, wet = 0, badWater = 0;
        foreach (var (id, seed) in new[] { ("balanced", 42), ("continental", 42), ("mountainous", 7319), ("vast-world", 7319) })
        {
            var s = CoreTests.Sampler(id, seed);
            var forms = s.Plan.PlanLandforms.OfType<Escarpments>().ToList();
            T.Check(forms.Count == 1, $"{id}: the escarpment module runs in revision 18");
            int count = forms.Sum(f => f.Count);
            lines += count;
            T.Note($"{id}/{seed}: {count} escarpments");
            foreach (var (x, z, nx, nz, height) in forms.SelectMany(f => f.Probes()))
            {
                // A transect across the edge, 2 m apart: the steepest step, and the rise from foot to top.
                double steepest = 0, prev = double.NaN, foot = double.MaxValue, top = double.MinValue;
                for (double d = -700; d <= 700; d += 2)
                {
                    var t = s.Sample(x + nx * d, z + nz * d);
                    if (t.Water != WaterKind.None) { wet++; if (!(t.Height < t.WaterLevel)) badWater++; }
                    if (!double.IsNaN(prev)) steepest = Math.Max(steepest, Math.Abs(t.Height - prev) / 2);
                    prev = t.Height;
                    if (d < -400) foot = Math.Min(foot, t.Height);
                    if (d > 400) top = Math.Max(top, t.Height);
                }
                probes++;
                if (steepest > 1.2) cliffs++;
                if (top - foot > .3 * height) steps++;
            }
            // Revision 17 has no escarpments; switched off, revision 18 is revision 17's ground (19 also changed mesas).
            var r17 = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = 17 }));
            var off = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with
            {
                Revision = 18,
                Landforms = new Dictionary<string, LandformSetting> { [LandformCatalog.Escarpments.Id] = new(0, 1) },
            }));
            T.Check(r17.Plan.PlanLandforms.Count == 0 && off.Plan.PlanLandforms.Count == 0, $"{id}: none in revision 17 or when off");
            var f0 = s.Plan.Layout.Land[0];
            int same = 0;
            for (int i = 0; i < 200; i++)
            {
                double px = f0.Center.X + (i % 20 - 10) * 1733.3, pz = f0.Center.Z + (i / 20 - 5) * 2711.7;
                if (off.Height(px, pz) == r17.Height(px, pz)) same++;
            }
            T.Check(same == 200, $"{id}: switched off gives revision 17's ground ({same}/200)");
        }
        T.Note($"{lines} escarpments, {probes} probed: {cliffs} with a face steeper than 50°, {steps} rising over 30 % of their height; {wet} water points on the transects");
        T.Check(lines >= 4, $"escarpments are placed ({lines})");
        T.Check(cliffs >= probes * .7, $"most have a cliff face ({cliffs}/{probes})");
        T.Check(steps >= probes * .7, $"most raise a plateau ({steps}/{probes})");
        T.Check(badWater == 0, $"water stays above its ground ({badWater} of {wet})");
    }

    private static void NoCombs()
    {
        // A comb tooth, a fin or a crack is a one-metre spike or notch: the ground jumps out of line with both neighbours
        // (second difference over 1 m above 6 m). Counted on the escarpments' own ground with mesas and spires switched off
        // (their tiers and needles are meant to be steep), and as sudden steps at the comb Tom found (mesas, 2026-10-28).
        int Spikes(TerrainSampler s, double cx, double cz, bool all = false)
        {
            const int N = 160;
            var h = new double[N * N];
            s.HeightGridParallel(cx - N / 2, cz - N / 2, 1, N, N, h);
            var forms = s.Plan.PlanLandforms.OfType<Escarpments>().ToList();
            int count = 0;
            for (int j = 1; j < N - 1; j++)
                for (int i = 1; i < N - 1; i++)
                {
                    double c = h[j * N + i];
                    if (Math.Abs(h[j * N + i - 1] - 2 * c + h[j * N + i + 1]) <= 6 && Math.Abs(h[(j - 1) * N + i] - 2 * c + h[(j + 1) * N + i]) <= 6) continue;
                    if (all || forms.Any(f => f.Ground(cx - N / 2 + i, cz - N / 2 + j, 2, out _) != 0)) count++;
                }
            return count;
        }
        // The comb's teeth were 2–4 m steps on gentle ground: a step over 1.5 m in one metre between gentle neighbours.
        int Steps(TerrainSampler s, double cx, double cz)
        {
            const int N = 300;
            var h = new double[N * N];
            s.HeightGridParallel(cx - N / 2, cz - N / 2, 1, N, N, h);
            int count = 0;
            for (int j = 0; j < N; j++)
                for (int i = 1; i < N - 2; i++)
                {
                    int k = j * N + i;
                    if (Math.Abs(h[k + 1] - h[k]) > 1.5 && Math.Abs(h[k] - h[k - 1]) < .4 && Math.Abs(h[k + 2] - h[k + 1]) < .4) count++;
                }
            return count;
        }
        int old = 0, now = 0, spots = 0;
        foreach (var (id, seed) in new[] { ("balanced", 42), ("continental", 42), ("mountainous", 42), ("mountainous", 7319), ("vast-world", 7319) })
        {
            var s = CoreTests.Sampler(id, seed);
            var s18 = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = 18 }));
            var bare = new Dictionary<string, LandformSetting> { [LandformCatalog.Mesas.Id] = new(0, 1), [LandformCatalog.Spires.Id] = new(0, 1) };
            var e18 = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = 18, Landforms = bare }));
            var e19 = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Landforms = bare }));
            var spotsHere = s.Plan.PlanLandforms.OfType<Escarpments>().SelectMany(e => e.Probes()).Select(p => (p.X, p.Z)).Take(8).ToList();
            // Tom's dumps from build 116: the fin at a bend of the line and the forest wall.
            if (id == "balanced" && seed == 42) { spotsHere.Add((137727, 17277)); spotsHere.Add((140623, 16518)); }
            if (id == "mountainous" && seed == 42)
            {
                spotsHere.Add((7011, 139478));
                int comb18 = Steps(s18, 6980, 140292), comb19 = Steps(s, 6980, 140292);
                T.Note($"the comb (mountainous 42, 6980, 140292): {comb18} sudden steps in revision 18, {comb19} in 19");
                T.Check(comb19 <= comb18 / 10, $"the comb is gone ({comb19} against {comb18})");
            }
            foreach (var (x, z) in spotsHere) { spots++; old += Spikes(e18, x, z); now += Spikes(e19, x, z); }
        }
        T.Note($"{spots} escarpment spots, 160 m square each: one-metre spikes and notches — revision 18: {old}, revision 19: {now}");
        T.Check(spots >= 15 && now <= old / 20, $"revision 19's escarpments have (almost) none ({now} against {old})");
    }

    private static void Waterfalls()
    {
        int falls = 0, tall = 0, checkedFalls = 0, dropped = 0, wet = 0, dry = 0, notDescending = 0, endsMoved = 0;
        foreach (var (id, seed) in new[] { ("balanced", 42), ("mountainous", 42), ("archipelago", 7319) })
        {
            var s = CoreTests.Sampler(id, seed);
            var hy = s.Plan.Hydrology;
            var h19 = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = 19 })).Plan.Hydrology;
            falls += hy.Falls.Count;
            tall += hy.Falls.Count(f => f.Height > 120.001);
            // Every reach still strictly descends; its ends (confluences, lakes, the sea) are where they were.
            for (int i = 0; i < hy.Reaches.Count; i++)
            {
                var r = hy.Reaches[i];
                // (a reach that already rose somewhere in revision 19 — lake inflows and confluences adjust ends — is judged
                // by whether the falls added a rise)
                int Rises(double[] v) { int n = 0; for (int k = 1; k < v.Length; k++) if (!(v[k] < v[k - 1])) n++; return n; }
                if (Rises(r.Surface) > Rises(h19.Reaches[i].Surface)) notDescending++;
                if (r.Surface[0] != h19.Reaches[i].Surface[0] || r.Surface[^1] != h19.Reaches[i].Surface[^1]) endsMoved++;
            }
            // At each of the first 12 falls: the water 30 m below the lip is far lower than at the lip, and along the
            // gorge (the river's line, 0–300 m below the lip) water lies above its ground.
            foreach (var f in hy.Falls.Take(12))
            {
                var r = hy.Reaches[f.Reach];
                int Below(double m) { int j = f.Point; while (j < r.Points.Length - 1 && r.Along[j] - r.Along[f.Point] < m) j++; return j; }
                checkedFalls++;
                var lip = s.Sample(r.Points[Math.Max(0, f.Point - 1)].X, r.Points[Math.Max(0, f.Point - 1)].Z);
                var low = s.Sample(r.Points[Below(30)].X, r.Points[Below(30)].Z);
                if (lip.Water == WaterKind.River && low.Water == WaterKind.River && lip.WaterLevel - low.WaterLevel > .5 * f.Height) dropped++;
                for (double m = 0; m <= 300; m += 10)
                {
                    var p = r.Points[Below(m)];
                    var t = s.Sample(p.X, p.Z);
                    if (t.Water == WaterKind.None) continue;
                    wet++;
                    if (!(t.Height < t.WaterLevel)) dry++;
                }
            }
        }
        T.Note($"{falls} waterfalls in 3 worlds; {checkedFalls} checked: {dropped} drop over half their height within 30 m; {wet} water points along their gorges, {dry} with the ground above the water");
        T.Check(falls > 100 && tall == 0, $"falls placed, none over 120 m ({falls}, {tall} too tall)");
        T.Check(notDescending == 0 && endsMoved == 0, $"rivers still descend, ends unchanged ({notDescending}, {endsMoved})");
        T.Check(dropped >= checkedFalls * .8, $"the water drops at the lip ({dropped}/{checkedFalls})");
        T.Check(wet > 300 && dry == 0, $"water above its ground below the falls ({dry} of {wet})");
    }

    private static void CanyonForms()
    {
        int canyons = 0, rivers = 0, raised = 0, walls = 0, wet = 0, dry = 0, levelMoved = 0;
        foreach (var (id, seed) in new[] { ("balanced", 42), ("continental", 42), ("mountainous", 42), ("archipelago", 7319), ("vast-world", 7319) })
        {
            var s = CoreTests.Sampler(id, seed);
            var s20 = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = 20 }));
            if (s.Plan.Canyons is not { } cy) continue;
            foreach (var (x, z, depth) in cy.Probes())
            {
                canyons++;
                var t = s.Sample(x, z); var t20 = s20.Sample(x, z);
                if (t.Water == WaterKind.River) rivers++;
                if (t.Water != t20.Water || t.WaterLevel != t20.WaterLevel) levelMoved++;
                // Across the canyon (both ways, 400 m): the land beside is raised, the walls are steep, water lies above
                // its ground.
                bool steep = false, up = false;
                foreach (var (dx, dz) in new[] { (1.0, 0.0), (0.0, 1.0) })
                {
                    double prev = double.NaN;
                    for (double d = -400; d <= 400; d += 2)
                    {
                        var p = s.Sample(x + dx * d, z + dz * d);
                        if (!double.IsNaN(prev) && Math.Abs(p.Height - prev) / 2 > 1.5) steep = true;
                        prev = p.Height;
                        if (p.Water != WaterKind.None) { wet++; if (!(p.Height < p.WaterLevel)) dry++; }
                        if (Math.Abs(d) == 400 && p.Height - s20.Sample(x + dx * d, z + dz * d).Height > .4 * depth) up = true;
                    }
                }
                if (steep) walls++;
                if (up) raised++;
            }
        }
        // The raise itself never makes a cliff (revision 22): its steepest slope around every canyon, 25 m apart, in
        // revision 21 (a meander set a canyon's end beside its middle: a 60 m wall with no river, Tom's dump 21:59:22) and now.
        double Steepest(TerrainSampler sampler)
        {
            double worst = 0;
            if (sampler.Plan.Canyons is not { } c) return 0;
            foreach (var (x, z, _) in c.Probes())
                for (double dz = -6000; dz <= 6000; dz += 25)
                {
                    double prev = double.NaN;
                    for (double dx = -6000; dx <= 6000; dx += 25)
                    {
                        double u = c.Uplift(x + dx, z + dz);
                        if (!double.IsNaN(prev)) worst = Math.Max(worst, Math.Abs(u - prev) / 25);
                        prev = u;
                    }
                }
            return worst;
        }
        var b42 = CoreTests.Sampler("balanced", 42);
        double now = Steepest(b42), before = Steepest(new TerrainSampler(new WorldPlan(b42.Plan.Manifest, style: b42.Plan.Style with { Revision = 21 })));
        T.Note($"steepest slope of the raise (balanced 42): revision 21 {before:0.00}, now {now:0.00}");
        T.Check(now < .25, $"the raise has no cliffs of its own ({now:0.00})");
        T.Note($"{canyons} canyons: river at the middle {rivers}, land raised 400 m out {raised}, steep walls {walls}; {wet} water points across them, {dry} with ground above the water; water level changed at {levelMoved}");
        T.Check(canyons >= 8 && rivers == canyons && levelMoved == 0, $"canyons follow rivers that keep their level ({rivers}/{canyons}, {levelMoved} moved)");
        T.Check(raised >= canyons * .8 && walls >= canyons * .8, $"raised land and steep walls ({raised}, {walls} of {canyons})");
        T.Check(wet > 100 && dry == 0, $"water above its ground ({dry} of {wet})");
    }

    private static void CirqueForms()
    {
        int cirques = 0, tarns = 0, water = 0, under = 0, leaks = 0, sameHydrology = 0, worlds = 0;
        foreach (var (id, seed) in new[] { ("balanced", 42), ("continental", 42), ("mountainous", 42), ("mountainous", 7319) })
        {
            var s = CoreTests.Sampler(id, seed);
            var h22 = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = 22 })).Plan.Hydrology;
            worlds++;
            if (h22.Reaches.Count == s.Plan.Hydrology.Reaches.Count && h22.Lakes.Count == s.Plan.Hydrology.Lakes.Count) sameHydrology++;
            if (s.Plan.Cirques is not { } cq) continue;
            cirques += cq.Count;
            foreach (var (x, z, level, radius) in cq.Probes().Take(10))
            {
                // A grid over the floor, 5 m apart: tarn water at its level over lower ground; next to it, water or ground
                // at least at the water's level (closed in).
                int here = 0;
                for (double dz = -radius * .6; dz <= radius * .6; dz += 5)
                    for (double dx = -radius * .6; dx <= radius * .6; dx += 5)
                    {
                        var t = s.Sample(x + dx, z + dz);
                        if (t.Water != WaterKind.Pond || Math.Abs(t.WaterLevel - level) > 1e-9) continue;
                        here++; water++;
                        if (!(t.Height < t.WaterLevel)) under++;
                        foreach (var (ox, oz) in new[] { (5.0, 0.0), (-5.0, 0.0), (0.0, 5.0), (0.0, -5.0) })
                        {
                            var n = s.Sample(x + dx + ox, z + dz + oz);
                            if (n.Water == WaterKind.None && n.Height < level - .05) leaks++;
                        }
                    }
                if (here > 20) tarns++;
            }
        }
        T.Note($"{cirques} cirques in 4 worlds; of 40 checked, {tarns} hold a tarn; {water} tarn points, {under} not over lower ground, {leaks} dry neighbours below the water");
        T.Check(cirques >= 40 && tarns >= 30, $"cirques with tarns ({cirques}, {tarns} of 40)");
        T.Check(under == 0 && leaks == 0, $"tarns lie over lower ground, closed in ({under}, {leaks})");
        T.Check(sameHydrology == worlds, $"the plan's rivers and lakes are as before ({sameHydrology}/{worlds})");
    }

    private static void Wetlands()
    {
        var plan = Vast.Value;
        var s = new TerrainSampler(plan);
        var s10 = new TerrainSampler(new WorldPlan(plan.Manifest, style: plan.Style with { Revision = 10 }));
        foreach (var (name, x0, z0, min, max) in new[] { ("marsh", -18000.0, 462000.0, .25, .6), ("bayou", 442000.0, 302000.0, .3, .75), ("fen", -713850.0, -293850.0, .04, .5) })
        {
            var (a, _, share) = plan.Biomes.Sample(x0, z0);
            var def = plan.Biomes.Pool[a].Def;
            T.Check(def.Id == name && share > .6, $"{name}: biome at the probe ({def.Id} {share:0.00})");
            const int N = 200;
            var grid = new TerrainSample[N * N];
            int ponds = 0, oldPonds = 0, bad = 0, tooDeep = 0;
            for (int j = 0; j < N; j++)
                for (int i = 0; i < N; i++)
                {
                    var t = grid[j * N + i] = s.Sample(x0 + i, z0 + j);
                    if (t.Water == WaterKind.Pond) { ponds++; if (t.WaterLevel - t.Height > def.Water.PoolDepth + .5) tooDeep++; }
                    if ((i & 7) == 0 && (j & 7) == 0 && s10.Sample(x0 + i, z0 + j).Water == WaterKind.Pond) oldPonds++;
                }
            for (int j = 1; j < N - 1; j++)
                for (int i = 1; i < N - 1; i++)
                {
                    var t = grid[j * N + i];
                    if (t.Water != WaterKind.None) continue;
                    foreach (int k in new[] { j * N + i - 1, j * N + i + 1, (j - 1) * N + i, (j + 1) * N + i })
                        if (grid[k].Water == WaterKind.Pond && t.Height < grid[k].WaterLevel - .02)
                        {
                            if (bad++ < 3) Console.WriteLine($"       dry at +{i},+{j}: ground {t.Height:0.00}, pool level {grid[k].WaterLevel:0.00}, pool bed {grid[k].Height:0.00}");
                            break;
                        }
                }
            double cover = ponds / (double)(N * N);
            Console.WriteLine($"     {name}: pools {cover * 100:0.0} % of 200 × 200 m (asks {def.Water.Pools:0.00}), dry spots below a pool surface {bad}, too deep {tooDeep}");
            T.Check(cover >= min && cover <= max, $"{name}: pool share {cover:0.00} within {min}…{max}");
            T.Check(bad == 0, $"{name}: no dry ground below a neighbouring pool's surface ({bad})");
            T.Check(tooDeep == 0, $"{name}: pools no deeper than the biome's depth ({tooDeep})");
            T.Check(oldPonds == 0, $"{name}: revision 10 has no pools");
        }
    }

    // Revision 15 (Tom: "holes in lakes … steep walls of water"): an outlet river ran through its lake following the
    // lake bed, 3–113 m below the surface. Now every outlet point inside its lake is at the lake's level, and the
    // sampler sees lake water (at the lake level) wherever an outlet lies inside its lake.
    private static void LakeOutlets()
    {
        int lakes = 0, inside = 0, below = 0, trench = 0, oldTrench = 0;
        foreach (var (id, seed) in new[] { ("mountainous", 42), ("balanced", 42) })
        {
            var s = CoreTests.Sampler(id, seed);
            var s14 = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = 14 }));
            var hy = s.Plan.Hydrology;
            foreach (var lake in hy.Lakes)
            {
                if (lake.OutletReach < 0) continue;
                lakes++;
                var reach = hy.Reaches[lake.OutletReach];
                for (int k = 0; k < reach.Points.Length; k++)
                {
                    var p = reach.Points[k];
                    if (!(hy.LakeDistance(p.X, p.Z, out double cells) == lake.Id && cells < 1.6)) break;
                    inside++;
                    // (a reach ending inside its lake takes its downstream reach's level at the last point: the sampler check
                    // below covers what that reach does inside the lake)
                    if (k < reach.Points.Length - 1 && reach.Surface[k] < lake.Level - .05) below++;
                    var t = s.Sample(p.X, p.Z);
                    if (!double.IsNaN(t.WaterLevel) && t.WaterLevel < lake.Level - .5) trench++;
                    var t14 = s14.Sample(p.X, p.Z);
                    if (!double.IsNaN(t14.WaterLevel) && t14.WaterLevel < lake.Level - .5) oldTrench++;
                }
            }
        }
        T.Note($"{lakes} lakes with outlets, {inside} outlet points inside them; water below the lake level: revision 14 {oldTrench}, now {trench}");
        T.Check(lakes > 5 && inside > 0, "lakes with outlets found");
        T.Check(below == 0, "outlet surface at the lake level inside the lake");
        T.Check(trench == 0, "no water trench inside a lake");
    }

    /// <summary>Tom's dump 182351: the coastline wiggle cut a pocket of sea (level 0) out of a lake at 13 m, a crater
    /// with walls of water. Probes every lake above sea level out to its reach; none may sample as sea.</summary>
    private static void LakeSeaPockets()
    {
        int samples = 0, oldSea = 0, sea = 0, lakes = 0;
        // Tom's world (dump 182351): vast continents, 32 km, seed 42, continent size 1.6, coast 1.3, rolling ground .95.
        var recipe = WorldRecipe.Catalog(CoreTests.Profiles).First(r => r.Id == "vast-world");
        var profile = recipe.Resolve(new WorldChoices { SizeKm = 32, ContinentSize = 1.6, CoastRoughness = 1.3 });
        var basis = TerrainStyle.For(profile);
        var tom = new TerrainSampler(new WorldPlan(WorldManifest.Create(42, profile, worldId: CoreTests.FixedWorldId),
            style: basis with { LowlandDetail = basis.LowlandDetail * .95, ForestCover = 2, ForestDensity = 1.6 }));
        var tomSea = tom.Sample(-5669.182, 5738.657);
        T.Note($"Tom's spot: {tomSea.Water} at {tomSea.WaterLevel:0.0} m, ground {tomSea.Height:0.0} m");
        T.Check(tomSea.Water == WaterKind.Lake, "Tom's spot is lake water");
        foreach (var s in new[] { tom, CoreTests.Sampler("vast-world", 42), CoreTests.Sampler("balanced", 42), CoreTests.Sampler("archipelago", 42) })
        {
            var s15 = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = 15 }));
            var hy = s.Plan.Hydrology;
            double cell = s.Plan.Macro.Cell;
            foreach (var lake in hy.Lakes)
            {
                if (lake.Level < s.Plan.Profile.SeaLevel + .05) continue;
                lakes++;
                for (int a = 0; a < 48; a++)
                {
                    double ang = a * Math.Tau / 48;
                    for (double r = 0; r < 40 * cell; r += cell * .25)
                    {
                        double x = lake.Center.X + Math.Cos(ang) * r, z = lake.Center.Z + Math.Sin(ang) * r;
                        if (hy.LakeDistance(x, z, out double d) != lake.Id) continue;
                        if (d > 1.6) break;
                        samples++;
                        if (s15.Sample(x, z).Water == WaterKind.Ocean) oldSea++;
                        if (s.Sample(x, z).Water == WaterKind.Ocean) sea++;
                    }
                }
            }
        }
        T.Note($"{lakes} lakes, {samples} probes within their reach; sampled as sea — revision 15: {oldSea}, now: {sea}");
        T.Check(samples > 1000, "probes inside lakes");
        T.Check(sea == 0, "no sea inside a lake's reach");
    }

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
        T.Note($"{samples} probes near lake edges: dry ground below the surface — revision 9: {dryOld}, current: {dryNew}");
        T.Check(samples > 500 && dryNew == 0, "no dry ground below a lake's surface within its reach");
    }
}
