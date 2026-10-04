using System.Diagnostics;
using Thornwake.WorldGen;

/// <summary>Numbers for the rewrite's world core (Game/WorldGen), comparable with <see cref="LegacyBench"/>.</summary>
internal static class NewBench
{
    public static void Run(Report report, string profileId, int seed, bool quick)
    {
        var profile = WorldProfile.LoadCatalog(Path.Combine(AppContext.BaseDirectory, "Content/world_profiles.json")).Single(p => p.Id == profileId);
        var manifest = WorldManifest.Create(seed, profile);
        string key = $"new.{profileId}";

        long before = Bench.Allocated();
        var sw = Stopwatch.StartNew();
        var plan = new WorldPlan(manifest);
        report.Add(key + ".plan_build_ms", sw.Elapsed.TotalMilliseconds);
        report.Add(key + ".plan_raster_mb", plan.Macro.Bytes / 1048576.0);
        report.Add(key + ".plan_alloc_mb", (Bench.Allocated() - before) / 1048576.0);
        var sampler = new TerrainSampler(plan);

        var land = plan.Layout.Land[0];
        var rng = new Random(1234);
        int n = quick ? 20000 : 200000;
        var xs = new double[n]; var zs = new double[n];
        for (int i = 0; i < n; i++) { xs[i] = land.Center.X + (rng.NextDouble() - .5) * land.RadiusX * 1.6; zs[i] = land.Center.Z + (rng.NextDouble() - .5) * land.RadiusX * 1.6; }
        double sink = 0;
        foreach (var (name, minWavelength) in new[] { ("full_detail_2m", 2.0), ("lod_64m", 64.0), ("macro_only", double.PositiveInfinity) })
        {
            sw.Restart();
            for (int i = 0; i < n; i++) sink += sampler.Height(xs[i], zs[i], minWavelength);
            report.Add($"{key}.sample_{name}_ns", sw.Elapsed.TotalSeconds * 1e9 / n);
        }

        // Same tile shape as the legacy lab (256 m, 8 m spacing, 33x33) and the rewrite's near tile (64 m, 1 m, 65x65).
        var buffer = new double[65 * 65];
        int tiles = quick ? 10 : 60;
        sw.Restart();
        for (int i = 0; i < tiles; i++) sampler.HeightGrid(land.Center.X + i * 256, land.Center.Z, 8, 33, 33, buffer);
        report.Add(key + ".tile_256m_8m_ms", sw.Elapsed.TotalMilliseconds / tiles);
        sw.Restart();
        for (int i = 0; i < tiles; i++) sampler.HeightGrid(land.Center.X + i * 64, land.Center.Z + 512, 1, 65, 65, buffer);
        double near = sw.Elapsed.TotalMilliseconds / tiles;
        report.Add(key + ".tile_64m_1m_ms", near);
        report.Add(key + ".tile_64m_1m_us_per_vertex", near * 1000 / (65 * 65));

        // Parallel throughput on all cores (what a worker pool can sustain).
        sw.Restart();
        int parallelTiles = quick ? 32 : 192;
        Parallel.For(0, parallelTiles, () => new double[65 * 65], (i, _, local) =>
        { sampler.HeightGrid(land.Center.X + i * 64, land.Center.Z - 4096, 1, 65, 65, local); return local; }, _ => { });
        report.Add(key + ".tiles_64m_1m_per_sec_all_cores", parallelTiles / sw.Elapsed.TotalSeconds);
        GC.KeepAlive(sink);
    }
}
