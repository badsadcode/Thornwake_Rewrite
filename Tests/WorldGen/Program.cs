using System.Diagnostics;
using System.Globalization;
using Thornwake.WorldGen;

// Rewrite world-core tests.
//   dotnet run --project Tests/WorldGen                      run all tests
//   dotnet run --project Tests/WorldGen -- --filter=coast    run tests whose name contains "coast"
//   dotnet run --project Tests/WorldGen -- --record          re-record frozen fingerprints (generator change only!)
//   dotnet run --project Tests/WorldGen -- --maps=<dir>      write hill-shaded PNG previews instead of testing
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
string? filter = args.FirstOrDefault(a => a.StartsWith("--filter="))?[9..];
string? maps = args.FirstOrDefault(a => a.StartsWith("--maps="))?[7..];
bool record = args.Contains("--record");

if (args.Contains("--probe"))
{
    Probe.Run();
    return 0;
}

if (maps != null)
{
    Maps.Write(maps, args.FirstOrDefault(a => a.StartsWith("--profile="))?[10..]);
    return 0;
}

var timer = Stopwatch.StartNew();
try
{
    CoreTests.Register(filter, record);
    HydrologyTests.Register(filter);
}
catch (TestFailure failure)
{
    Console.WriteLine("FAIL " + failure.Message);
    return 1;
}
Console.WriteLine($"WORLDGEN_TESTS_PASS: {T.Count} tests, {T.TotalChecks} checks, {timer.Elapsed.TotalSeconds:0.0} s");
return 0;

internal static class Maps
{
    public static void Write(string directory, string? only)
    {
        Directory.CreateDirectory(directory);
        foreach (var (id, seed) in new[] { ("mountainous", 42), ("vast-world", 7319), ("archipelago", 42), ("balanced", 0), ("continental", 42) })
        {
            if (only != null && only != id) continue;
            var sw = Stopwatch.StartNew();
            var sampler = CoreTests.Sampler(id, seed);
            var hy = sampler.Plan.Hydrology;
            Console.WriteLine($"{id}/{seed}: plan {sampler.Plan.BuildMilliseconds:0} ms (raster {sampler.Plan.RasterMilliseconds:0} ms, hydrology {hy.BuildMilliseconds:0} ms), " +
                $"raster {sampler.Plan.Macro.Size}² ({sampler.Plan.Macro.Bytes / 1048576.0:0.0} MB), {hy.Reaches.Count} reaches, {hy.Lakes.Count} lakes, max incision {hy.MaxIncision:0} m");
            var p = sampler.Plan.Profile;
            MapRender.Window(sampler, Path.Combine(directory, $"{id}-{seed}-world.png"), sampler.Plan.Manifest.OriginX, sampler.Plan.Manifest.OriginZ, p.WorldSize, 1024);
            var continent = sampler.Plan.Layout.Land[0];
            MapRender.Window(sampler, Path.Combine(directory, $"{id}-{seed}-continent.png"), continent.Center.X, continent.Center.Z, continent.RadiusX * 2.6, 1024);
            if (sampler.Plan.Layout.Belts.Count > 0)
            {
                var belt = sampler.Plan.Layout.Belts[0];
                var k = belt.Points[belt.Points.Length / 2].Point;
                double bx = k.X + sampler.Plan.Manifest.OriginX, bz = k.Z + sampler.Plan.Manifest.OriginZ;
                MapRender.Window(sampler, Path.Combine(directory, $"{id}-{seed}-mountain-40km.png"), bx, bz, 40000, 1024);
                MapRender.Window(sampler, Path.Combine(directory, $"{id}-{seed}-mountain-4km.png"), bx, bz, 4000, 800);
            }
            // A lowland spot halfway between the continent centre and its coast.
            double lx = continent.Center.X, lz = continent.Center.Z + continent.RadiusZ * .35;
            MapRender.Window(sampler, Path.Combine(directory, $"{id}-{seed}-lowland-4km.png"), lx, lz, 4000, 800);
            MapRender.Window(sampler, Path.Combine(directory, $"{id}-{seed}-lowland-512m.png"), lx, lz, 512, 512);
            double cx = continent.Center.X;
            while (sampler.Height(cx, continent.Center.Z) > p.SeaLevel) cx += 400;
            MapRender.Window(sampler, Path.Combine(directory, $"{id}-{seed}-coast-8km.png"), cx, continent.Center.Z, 8000, 800);
            Console.WriteLine($"  maps written in {sw.Elapsed.TotalSeconds:0.0} s");
        }
    }
}

internal static class Probe
{
    // Diagnostic: distribution of walking-scale detail and relief on lowland.
    public static void Run()
    {
        foreach (var (id, seed) in new[] { ("mountainous", 42), ("vast-world", 7319), ("archipelago", 42) })
        {
            var hy = CoreTests.Sampler(id, seed).Plan.Hydrology;
            foreach (var r in hy.Reaches)
                if (r.DownstreamReach >= 0 && Math.Abs(r.Surface[^1] - hy.Reaches[r.DownstreamReach].Surface[0]) > .011)
                {
                    var nx = hy.Reaches[r.DownstreamReach];
                    Console.WriteLine($"{id}: reach {r.Id} end {r.Surface[^1]} -> {nx.Id} start {nx.Surface[0]},{nx.Surface[1]} startLake {nx.StartLake} upstream [{string.Join(",", nx.UpstreamReaches.Select(u => hy.Reaches[u].Surface[^1].ToString("0.000")))}] len {nx.Points.Length}");
                }
        }
        return;
        var s = CoreTests.Sampler("mountainous", 42);
        var f = s.Plan.Layout.Land[0];
        var rng = new Random(1);
        var details = new List<double>(); var rolling = new List<double>(); var ranges = new List<double>();
        var h = new double[129 * 129];
        for (int i = 0; i < 400; i++)
        {
            double x = f.Center.X + (rng.NextDouble() - .5) * f.RadiusX, z = f.Center.Z + (rng.NextDouble() - .5) * f.RadiusX;
            var t = s.Sample(x, z);
            if (t.Land < .1 || t.Mountain > .05) continue;
            details.Add(Math.Abs(t.Detail));
            rolling.Add(Math.Abs(Noise.ErodedFbm(x, z, 1600, 11, 2, 99, .6)));
            s.HeightGrid(x, z, 2, 129, 129, h);
            ranges.Add(h.Max() - h.Min());
        }
        static string Q(List<double> v) { v.Sort(); return $"p10 {v[v.Count / 10]:0.00} median {v[v.Count / 2]:0.00} p90 {v[v.Count * 9 / 10]:0.00}"; }
        Console.WriteLine($"|detail| m: {Q(details)}");
        Console.WriteLine($"|ErodedFbm| raw: {Q(rolling)}");
        Console.WriteLine($"relief range in 256 m windows: {Q(ranges)}");
        var plain = new List<double>();
        for (int i = 0; i < 4000; i++) plain.Add(Math.Abs(Noise.Fbm(rng.NextDouble() * 1e5, rng.NextDouble() * 1e5, 1600, 11, 2, 99, .6)));
        Console.WriteLine($"|Fbm| raw: {Q(plain)}");
        var grad = new List<double>();
        for (int i = 0; i < 4000; i++) grad.Add(Math.Abs(Noise.Gradient(rng.NextDouble() * 1e5, rng.NextDouble() * 1e5, 5)));
        Console.WriteLine($"|Gradient| raw: {Q(grad)}");
    }
}
