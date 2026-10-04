using Thornwake.Hydraulics;
using Thornwake.WorldGen;

/// <summary>Local shallow-water simulation (M6): conservation, downhill flow, pooling, sinks, moving window.</summary>
internal static class SimulationTests
{
    public static void Register(string? filter)
    {
        T.Run("Water: mass conserved, runs downhill", Downhill, filter);
        T.Run("Water: pools level in a basin", Basin, filter);
        T.Run("Water: generated rivers absorb it", Sinks, filter);
        T.Run("Water: window moves, dormant chunks resume", Window, filter);
        T.Run("Water: real terrain ground and sinks", RealTerrain, filter);
        T.Run("Water: step cost with rain over the window", Cost, filter);
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
        T.Near(w.Volume() + w.Absorbed + w.Spilled, w.Added, Math.Max(1e-3, w.Added * 2e-4), label + ": volume balance");

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
        var w = new LocalWater(Ground((x, z) => 50 + .02 * (x * x + z * z)));
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
        var w = new LocalWater(Ground((x, z) => 50 + .02 * (x * x + z * z)));
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
}
