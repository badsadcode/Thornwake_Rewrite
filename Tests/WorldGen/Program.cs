using System.Diagnostics;
using System.Globalization;
using Thornwake.WorldGen;

// Rewrite world-core tests.
//   dotnet run --project Tests/WorldGen                      run all tests
//   dotnet run --project Tests/WorldGen -- --filter=coast    run tests whose name contains "coast"
//   dotnet run --project Tests/WorldGen -- --record          re-record frozen fingerprints (generator change only!)
//   dotnet run --project Tests/WorldGen -- --maps=<dir>      write hill-shaded PNG previews instead of testing
//   dotnet run --project Tests/WorldGen -- --window=<profile>,<seed>,<x>,<z>,<side>,<pixels>,<png>   one close-up map
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

// Ad hoc close-up: --window=<profile>,<seed>,<x>,<z>,<side m>,<pixels>,<png>
string? window = args.FirstOrDefault(a => a.StartsWith("--window="))?[9..];
if (window != null)
{
    var v = window.Split(',');
    var ws = CoreTests.Sampler(v[0], int.Parse(v[1]));
    MapRender.Window(ws, v[6], double.Parse(v[2]), double.Parse(v[3]), double.Parse(v[4]), int.Parse(v[5]));
    return 0;
}

// Ad hoc: --dump=<profile>,<seed>,<x>,<z> prints the river reach nearest to a point
string? dump = args.FirstOrDefault(a => a.StartsWith("--dump="))?[7..];
if (dump != null)
{
    var v = dump.Split(',');
    var ds = CoreTests.Sampler(v[0], int.Parse(v[1]));
    double qx = double.Parse(v[2]), qz = double.Parse(v[3]);
    var hy = ds.Plan.Hydrology;
    Console.WriteLine($"cell {ds.Plan.Macro.Cell:0} m");
    if (hy.NearestRiver(qx, qz, 3000, out var hit))
    {
        var r = hy.Reaches[hit.Reach];
        Console.WriteLine($"reach {r.Id} points {r.Points.Length} sea {r.EndsInSea} distributary {r.Distributary}");
        for (int k = 0; k < r.Points.Length; k++)
        {
            double turn = 0;
            if (k > 0 && k + 1 < r.Points.Length)
            {
                double a1 = Math.Atan2(r.Points[k].Z - r.Points[k - 1].Z, r.Points[k].X - r.Points[k - 1].X);
                double a2 = Math.Atan2(r.Points[k + 1].Z - r.Points[k].Z, r.Points[k + 1].X - r.Points[k].X);
                turn = Math.IEEERemainder(a2 - a1, 2 * Math.PI) * 180 / Math.PI;
            }
            var at = ds.Sample(r.Points[k].X, r.Points[k].Z);
            Console.WriteLine($"{k} {r.Points[k].X:0} {r.Points[k].Z:0} w {r.Width[k]:0.0} s {r.Surface[k]:0.00} turn {turn:0} ground {at.Height:0.00} {at.Water} level {at.WaterLevel:0.00}");
        }
    }
    return 0;
}

// Ad hoc: --line=<profile>,<seed>,<x0>,<z0>,<x1>,<z1>,<n> prints samples along a line
string? line = args.FirstOrDefault(a => a.StartsWith("--line="))?[7..];
if (line != null)
{
    var v = line.Split(',');
    var ls = CoreTests.Sampler(v[0], int.Parse(v[1]));
    double x0 = double.Parse(v[2]), z0 = double.Parse(v[3]), x1 = double.Parse(v[4]), z1 = double.Parse(v[5]);
    int count = int.Parse(v[6]);
    Span<RiverHit> hits = stackalloc RiverHit[32];
    for (int i = 0; i <= count; i++)
    {
        double x = x0 + (x1 - x0) * i / count, z = z0 + (z1 - z0) * i / count;
        var t = ls.Sample(x, z);
        int n = ls.Plan.Hydrology.NearbyRivers(x, z, hits);
        Console.WriteLine($"{x:0.0} {z:0.0} h {t.Height:0.000} {t.Water} hits {n} nearest {(n > 0 ? hits[0].Distance : -1):0.0} w {(n > 0 ? hits[0].Width : 0):0.0}");
        if (args.Contains("--hits"))
            for (int j = 0; j < n; j++) Console.WriteLine($"    reach {hits[j].Reach} d {hits[j].Distance:0.00} s {hits[j].Surface:0.000} w {hits[j].Width:0.00}");
    }
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
    SimulationTests.Register(filter);
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
