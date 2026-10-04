using System.Diagnostics;
using Thornwake.Core;

/// <summary>Baseline numbers for the original A4 terrain path (Game/Core). Single-threaded unless stated.</summary>
internal static class LegacyBench
{
    public static void Run(Report report, string profileId, int seed, bool quick)
    {
        var profiles = MacroProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Content/world_profiles.json"));
        var profile = profiles.Single(p => p.Id == profileId);
        var manifest = MacroWorldManifest.Create(seed, profile, algorithm: 4);
        string key = $"legacy.{profileId}";

        long before = Bench.Allocated();
        var sw = Stopwatch.StartNew();
        var world = new MacroWorldGenerator(manifest);
        report.Add(key + ".world_construct_ms", sw.Elapsed.TotalMilliseconds);
        report.Add(key + ".world_construct_alloc_mb", (Bench.Allocated() - before) / 1048576.0);

        // Land sample points: spread over the first continent.
        var land = world.LandFeatures[0];
        var rng = new Random(1234);
        int n = quick ? 20000 : 200000;
        var xs = new double[n]; var zs = new double[n];
        for (int i = 0; i < n; i++) { xs[i] = land.Center.X + (rng.NextDouble() - .5) * land.RadiusX * 1.6; zs[i] = land.Center.Z + (rng.NextDouble() - .5) * land.RadiusX * 1.6; }
        double sink = 0; sw.Restart();
        for (int i = 0; i < n; i++) sink += world.Sample(xs[i], zs[i]).Elevation;
        double secs = sw.Elapsed.TotalSeconds;
        report.Add(key + ".sample_ns", secs * 1e9 / n);
        report.Add(key + ".samples_per_sec_millions", n / secs / 1e6);

        // Detailed (near) and outer playable tiles around the continent centre and its coast.
        var centre = PlayableWorldPolicy.TileAt(new WorldPoint(land.Center.X, 0, land.Center.Z));
        double coastX = land.Center.X;
        for (int i = 0; i < 48 && world.Sample(coastX, land.Center.Z).Elevation > profile.SeaLevel; i++) coastX += land.RadiusX / 24;
        var coast = PlayableWorldPolicy.TileAt(new WorldPoint(coastX, 0, land.Center.Z));
        int tiles = quick ? 3 : 12;
        foreach (var (name, origin) in new[] { ("inland", centre), ("coast", coast) })
        {
            sw.Restart();
            for (int i = 0; i < tiles; i++) PlayableTerrainPatch.Build(world, new WorldRegion(origin.X + i, origin.Z), default, true);
            report.Add($"{key}.tile_detailed_{name}_ms", sw.Elapsed.TotalMilliseconds / tiles);
            sw.Restart();
            for (int i = 0; i < tiles * 4; i++) PlayableTerrainPatch.Build(world, new WorldRegion(origin.X + i, origin.Z + 3), default, false);
            report.Add($"{key}.tile_outer_{name}_ms", sw.Elapsed.TotalMilliseconds / (tiles * 4));
        }
        // Time to fill the lab's full residency (1,681 tiles: 49 detailed + 1,632 outer) on one worker, estimated.
        double detailed = report.Get($"{key}.tile_detailed_inland_ms"), outer = report.Get($"{key}.tile_outer_inland_ms");
        report.Add(key + ".full_view_fill_est_s", (49 * detailed + 1632 * outer) / 1000);
        // Effective cost per rendered vertex for detailed tiles (33x33 vertices).
        report.Add(key + ".tile_detailed_us_per_vertex", detailed * 1000 / (33 * 33));

        sw.Restart();
        var drainage = MacroDrainage.Build(MacroDrainageRecipe.Create(manifest, 128));
        report.Add(key + ".drainage128_ms", sw.Elapsed.TotalMilliseconds);
        report.Add(key + ".drainage128_basins", drainage.Basins.Count);
        GC.KeepAlive(sink);
    }
}
