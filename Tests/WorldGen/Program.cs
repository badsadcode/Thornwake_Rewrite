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
            // River close-ups: the largest mouth (estuary/delta), a confluence and a meandering middle stretch.
            var mouth = hy.Reaches.Where(r => r.EndsInSea && !r.Distributary).OrderByDescending(r => r.Catchment[^1]).First();
            MapRender.Window(sampler, Path.Combine(directory, $"{id}-{seed}-river-mouth-6km.png"), mouth.Points[^1].X, mouth.Points[^1].Z, 6000, 900);
            var join = hy.Reaches.Where(r => r.UpstreamReaches.Count >= 2 && r.Catchment[0] > 300).OrderBy(r => r.Catchment[0]).First();
            MapRender.Window(sampler, Path.Combine(directory, $"{id}-{seed}-river-junction-2km.png"), join.Points[0].X, join.Points[0].Z, 2000, 800);
            var mid = hy.Reaches.Where(r => r.Catchment[^1] is > 200 and < 2000 && r.Points.Length > 40).OrderBy(r => r.Id).First();
            var mp = mid.Points[mid.Points.Length / 2];
            MapRender.Window(sampler, Path.Combine(directory, $"{id}-{seed}-river-middle-3km.png"), mp.X, mp.Z, 3000, 900);
            Console.WriteLine($"  maps written in {sw.Elapsed.TotalSeconds:0.0} s");
        }
    }
}

internal static class Probe
{
    // Diagnostic: walking-scale relief statistics and river data sizes. Extend ad hoc when investigating.
    public static void Run()
    {
        foreach (var (id, seed) in new[] { ("mountainous", 42), ("balanced", 0) })
        {
            var s = CoreTests.Sampler(id, seed);
            var hy = s.Plan.Hydrology;
            Console.WriteLine($"{id}/{seed}: {hy.Reaches.Count} reaches ({hy.Reaches.Count(r => r.Distributary)} delta branches), " +
                $"{hy.PointCount} river points, {hy.IndexEntries} index entries");
            var f = s.Plan.Layout.Land[0];
            var rng = new Random(1);
            var ranges = new List<double>();
            var h = new double[129 * 129];
            for (int i = 0; i < 200; i++)
            {
                double x = f.Center.X + (rng.NextDouble() - .5) * f.RadiusX, z = f.Center.Z + (rng.NextDouble() - .5) * f.RadiusX;
                var t = s.Sample(x, z);
                if (t.Land < .1 || t.Mountain > .05) continue;
                s.HeightGrid(x, z, 2, 129, 129, h);
                ranges.Add(h.Max() - h.Min());
            }
            ranges.Sort();
            Console.WriteLine($"  lowland relief in 256 m windows: median {ranges[ranges.Count / 2]:0.0} m, p90 {ranges[ranges.Count * 9 / 10]:0.0} m");
        }
    }
}
