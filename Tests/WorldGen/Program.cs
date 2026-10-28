using System.Text;
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

// Refactor guards (D227): --sample-hash prints a hash of everything TerrainSampler.Sample returns (all fields) over grids
// at several detail levels, per profile, seed and terrain revision; --speed times single-threaded sampling.
// --hillshade=profile,seed,x,z,size,out.png[,step]: shaded relief of the terrain around a point (1 m by default), lit from
// the north-west, steep ground darker — to see what the ground holds at walking scale (combs, steps, fins).
if (args.FirstOrDefault(a => a.StartsWith("--hillshade=")) is { } hs)
{
    var p = hs[12..].Split(',');
    var s = CoreTests.Sampler(p[0], int.Parse(p[1]));
    // (an 8th value "off:<module>" switches one landform module off; "rev:<n>" picks a terrain revision)
    foreach (var opt in p.Skip(7))
    {
        if (opt.StartsWith("off:")) s = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with
            { Landforms = new Dictionary<string, LandformSetting> { [opt[4..]] = new(0, 1) } }));
        if (opt.StartsWith("rev:")) s = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = int.Parse(opt[4..]) }));
    }
    double cx = double.Parse(p[2]), cz = double.Parse(p[3]), size = double.Parse(p[4]), step = p.Length > 6 ? double.Parse(p[6]) : 1;
    int n = (int)(size / step);
    var h = new double[(n + 1) * (n + 1)];
    s.HeightGridParallel(cx - size / 2, cz - size / 2, step, n + 1, n + 1, h);
    var rgb = new byte[n * n * 3];
    for (int j = 0; j < n; j++)
        for (int i = 0; i < n; i++)
        {
            double dx = (h[j * (n + 1) + i + 1] - h[j * (n + 1) + i]) / step, dz = (h[(j + 1) * (n + 1) + i] - h[j * (n + 1) + i]) / step;
            double len = Math.Sqrt(dx * dx + dz * dz + 1), light = Math.Clamp((-dx * -.6 + -dz * -.6 + 1) / len / 1.5, 0, 1);
            byte v = (byte)(40 + 215 * light);
            int k = (j * n + i) * 3;
            rgb[k] = v; rgb[k + 1] = v; rgb[k + 2] = (byte)Math.Min(255, v + 10);
        }
    Png.Write(p[5], n, n, rgb);
    Console.WriteLine($"wrote {p[5]} ({n} px, {step} m per px)");
    return 0;
}
// --transect=profile,seed,x0,z0,x1,z1,steps: heights and detail along a line (to look at a ridge or step).
if (args.FirstOrDefault(a => a.StartsWith("--transect=")) is { } tr)
{
    var p = tr[11..].Split(',');
    var s = CoreTests.Sampler(p[0], int.Parse(p[1]));
    if (p.Length > 7 && p[7] == "bare") s = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with
        { Landforms = new Dictionary<string, LandformSetting> { ["mesas"] = new(0, 1), ["spires"] = new(0, 1) } }));
    double x0 = double.Parse(p[2]), z0 = double.Parse(p[3]), x1 = double.Parse(p[4]), z1 = double.Parse(p[5]);
    int n = int.Parse(p[6]);
    for (int i = 0; i <= n; i++)
    {
        double x = x0 + (x1 - x0) * i / n, z = z0 + (z1 - z0) * i / n;
        var t = s.Sample(x, z);
        Console.WriteLine($"{x:0.0},{z:0.0} height {t.Height:0.00} detail {t.Detail:0.00} mountain {t.Mountain:0.00}");
    }
    return 0;
}
// --river-stats: river point spacing and steep stretches (where the water drops > 12 m within 600 m), per test world.
if (args.Contains("--river-stats"))
{
    foreach (var (id, seed) in new[] { ("balanced", 42), ("mountainous", 42) })
    {
        var s = CoreTests.Sampler(id, seed);
        var reaches = s.Plan.Hydrology.Reaches;
        var gaps = new List<double>();
        int steep = 0;
        foreach (var r in reaches)
        {
            for (int k = 1; k < r.Points.Length; k++) gaps.Add(r.Along[k] - r.Along[k - 1]);
            for (int k = 0, j = 0; k < r.Points.Length; k++)
            {
                while (j < r.Points.Length - 1 && r.Along[j] - r.Along[k] < 600) j++;
                if (r.Surface[k] - r.Surface[j] > 12) { steep++; k = j; }
            }
        }
        gaps.Sort();
        Console.WriteLine($"{id}/{seed}: {reaches.Count} reaches, point spacing median {gaps[gaps.Count / 2]:0} m (10 % {gaps[gaps.Count / 10]:0}, 90 % {gaps[gaps.Count * 9 / 10]:0}), steep stretches {steep}");
    }
    return 0;
}
// --find-biome=profile,seed,biomeId: places where that biome is nearly pure, nearest the world's centre first.
if (args.FirstOrDefault(a => a.StartsWith("--find-biome=")) is { } fb)
{
    var p = fb[13..].Split(',');
    var s = CoreTests.Sampler(p[0], int.Parse(p[1]));
    var map = s.Plan.Biomes;
    double half = s.Plan.Profile.WorldSize / 2;
    var found = new List<(double D, double X, double Z)>();
    for (double z = -half; z < half; z += 2000)
        for (double x = -half; x < half; x += 2000)
        {
            var (a, _, share) = map.Sample(x, z);
            if (map.Pool[a].Def.Id == p[2] && share > .85 && s.Sample(x, z).Water == WaterKind.None) found.Add((x * x + z * z, x, z));
        }
    foreach (var f in found.OrderBy(f => f.D).Take(5)) Console.WriteLine($"{p[2]} at {f.X:0},{f.Z:0} ground {s.Height(f.X, f.Z):0}");
    return 0;
}
// --landform-probes: where the big landforms are in each test world (for captures and Go to spots).
if (args.Contains("--landform-probes"))
{
    foreach (var profile in CoreTests.Profiles)
        foreach (int seed in new[] { 42, 7319 })
        {
            var s = CoreTests.Sampler(profile.Id, seed);
            var falls = s.Plan.Hydrology.Falls;
            if (falls.Count > 0) Console.WriteLine($"{profile.Id}/{seed}: {falls.Count} waterfalls, {falls.Count(f => f.Height >= 30)} over 30 m, median {falls[falls.Count / 2].Height:0} m");
            foreach (var f in falls.Where(f => f.Height is > 40 and < 90).Take(3).Concat(falls.Take(2)))
            {
                var r = s.Plan.Hydrology.Reaches[f.Reach];
                int j = f.Point; while (j < r.Points.Length - 1 && r.Along[j] - r.Along[f.Point] < 150) j++;
                Console.WriteLine($"{profile.Id}/{seed} waterfall at {f.Lip.X:0},{f.Lip.Z:0} height {f.Height:0} m (reach {f.Reach}), 150 m downstream {r.Points[j].X:0},{r.Points[j].Z:0}, width {r.Width[f.Point]:0} m");
            }
            foreach (var e in s.Plan.PlanLandforms.OfType<Escarpments>())
                foreach (var (x, z, nx, nz, h) in e.Probes())
                    Console.WriteLine($"{profile.Id}/{seed} escarpment at {x:0},{z:0} toward plateau {nx:0.00},{nz:0.00} height {h:0} m, ground {s.Height(x, z):0}");
        }
    return 0;
}
if (args.Contains("--sample-hash"))
{
    foreach (var profile in CoreTests.Profiles)
        foreach (int seed in new[] { 42, -9 })
        {
            var baseSampler = CoreTests.Sampler(profile.Id, seed);
            var variants = new List<(string Name, TerrainStyle Style)> { ("classic8", baseSampler.Plan.Style with { BankVariation = 0, ShoreSlope = 0, MouthBlend = 0, Revision = 8 }),
                ("nomouth8", baseSampler.Plan.Style with { MouthBlend = 0, Revision = 8 }) };
            for (int rev = 8; rev <= TerrainStyle.CurrentRevision; rev++) variants.Add(($"r{rev}", baseSampler.Plan.Style with { Revision = rev }));
            foreach (var (name, style) in variants)
            {
                var s = current(name) ? baseSampler : new TerrainSampler(new WorldPlan(baseSampler.Plan.Manifest, style: style));
                using var sha = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
                var f = s.Plan.Layout.Land[0];
                var bytes = new List<byte>();
                foreach (var (cx, cz, spacing) in new[] { (profile.WorldSize * -.5, profile.WorldSize * -.5, profile.WorldSize / 48),
                    (f.Center.X, f.Center.Z, 1.0), (f.Center.X + f.RadiusX * .5, f.Center.Z, 37.0), (f.Center.X - f.RadiusX * 1.1, f.Center.Z, 7.0),
                    (f.Center.X - f.RadiusX * .3, f.Center.Z + f.RadiusZ * .2, 211.0) })
                    foreach (double mw in new[] { 2.0, 8.0, 64.0, double.PositiveInfinity })
                        for (int j = 0; j < 41; j++)
                            for (int i = 0; i < 41; i++)
                            {
                                var t = s.Sample(cx + i * spacing, cz + j * spacing, mw);
                                foreach (double d in new[] { t.Height, t.Land, t.Mountain, t.Detail, t.WaterLevel, t.Plume, (double)t.Water, t.WaterBody })
                                    sha.AppendData(BitConverter.GetBytes(d));
                            }
                Console.WriteLine($"{profile.Id}/{seed}/{name} {Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant()[..16]}");
            }
        }
    return 0;
    static bool current(string n) => n == "r" + TerrainStyle.CurrentRevision;
}
if (args.Contains("--speed"))
{
    var samplers = CoreTests.Profiles.Select(p => CoreTests.Sampler(p.Id, 42)).ToList();
    var buffer = new double[128 * 128];
    double best = double.MaxValue;
    for (int round = 0; round < 5; round++)
    {
        var sw = Stopwatch.StartNew();
        foreach (var s in samplers)
        {
            var f = s.Plan.Layout.Land[0];
            for (int k = 0; k < 6; k++)
                s.HeightGrid(f.Center.X + (k - 3) * f.RadiusX * .15, f.Center.Z + (k % 3 - 1) * f.RadiusZ * .15, 1, 128, 128, buffer);
        }
        best = Math.Min(best, sw.Elapsed.TotalMilliseconds);
    }
    Console.WriteLine($"SPEED {samplers.Count} profiles x 6 tiles of 128x128 at 1 m, single thread: best of 5 = {best:0.0} ms ({best * 1e6 / (samplers.Count * 6 * 128 * 128):0} ns per sample)");
    return 0;
}

// Peak probe (screenshots of mountain skies): --peaks=<profile>,<seed>,<count> prints the highest points on a 4 km grid
string? peaks = args.FirstOrDefault(a => a.StartsWith("--peaks="))?[8..];
if (peaks != null)
{
    var v = peaks.Split(',');
    var ts = CoreTests.Sampler(v[0], int.Parse(v[1]));
    double half = ts.Plan.Profile.WorldSize / 2;
    var found = new List<(double H, double X, double Z)>();
    for (double x = -half; x < half; x += 4000)
        for (double z = -half; z < half; z += 4000)
            found.Add((ts.Height(x, z, 200) - ts.Plan.Profile.SeaLevel, x, z));
    foreach (var f in found.OrderByDescending(f => f.H).Take(int.Parse(v[2]))) Console.WriteLine($"{f.X:0} {f.Z:0} {f.H:0} m");
    return 0;
}

// Spot probe: --spots=<profile>,<seed> prints mild alpine slopes (biome alpine, 2..7 °C) and the widest rivers
string? spots = args.FirstOrDefault(a => a.StartsWith("--spots="))?[8..];
if (spots != null)
{
    var v = spots.Split(',');
    var ts = CoreTests.Sampler(v[0], int.Parse(v[1]));
    var plan = ts.Plan;
    double half = plan.Profile.WorldSize / 2;
    int shown = 0;
    for (double x = -half; x < half && shown < 8; x += 3000)
        for (double z = -half; z < half && shown < 8; z += 3000)
        {
            var t = ts.Sample(x, z, 8);
            if (t.Water != WaterKind.None) continue;
            var (a, b, share) = plan.Biomes.Sample(x, z);
            if (plan.Biomes.Pool[a].Def.Id != "alpine" || share < .7) continue;
            double temp = plan.Climate.At(x, z, t.Height - plan.Profile.SeaLevel).Temperature;
            if (temp < 2 || temp > 7) continue;
            Console.WriteLine($"alpine {x:0} {z:0} h {t.Height:0} T {temp:0.0}"); shown++;
        }
    foreach (var r in plan.Hydrology.Reaches.OrderByDescending(r => r.Catchment[^1]).Take(5))
    {
        var p = r.Points[r.Points.Length / 2];
        Console.WriteLine($"river {p.X:0} {p.Z:0} catchment {r.Catchment[^1]:0}");
    }
    return 0;
}

// Mouth step probe: --mouths=<type id>,<km>,<seed>,<count>,<dir> scans 1 m grids around sea/lake mouths for steps
string? mouths = args.FirstOrDefault(a => a.StartsWith("--mouths="))?[9..];
if (mouths != null)
{
    var v = mouths.Split(',');
    var recipe = WorldRecipe.Catalog(CoreTests.Profiles).First(r => r.Id == v[0]);
    var plan = new WorldPlan(WorldManifest.Create(int.Parse(v[2]), recipe.Resolve(new WorldChoices { SizeKm = double.Parse(v[1]) }), worldId: CoreTests.FixedWorldId));
    var ts = new TerrainSampler(plan);
    Directory.CreateDirectory(v[4]);
    var list = plan.Hydrology.Reaches.Where(r => r.EndsInSea || r.EndLake >= 0).OrderByDescending(r => r.Catchment[^1]).ToList();
    int count = int.Parse(v[3]);
    for (int n = 0; n < count && n < list.Count; n++)
    {
        var r = list[n * Math.Max(1, list.Count / count)];
        // The mouth: first point (from the end backwards) that is on land / outside the lake.
        int k = r.Points.Length - 1;
        while (k > 0 && ts.Sample(r.Points[k].X, r.Points[k].Z).Water is WaterKind.Ocean or WaterKind.Lake) k--;
        double cx = r.Points[k].X, cz = r.Points[k].Z;
        const int N = 300;
        var h = new double[N * N];
        ts.HeightGridParallel(cx - N / 2, cz - N / 2, 1, N, N, h, 1);
        double worst = 0; int wi = 0;
        for (int j = 0; j < N - 1; j++)
            for (int i = 0; i < N - 1; i++)
            {
                double d = Math.Max(Math.Abs(h[j * N + i + 1] - h[j * N + i]), Math.Abs(h[(j + 1) * N + i] - h[j * N + i]));
                if (d > worst) { worst = d; wi = j * N + i; }
            }
        double wx = cx - N / 2 + wi % N, wz = cz - N / 2 + wi / N;
        var a = ts.Sample(wx, wz); var b = ts.Sample(wx + 1, wz); var c = ts.Sample(wx, wz + 1);
        Console.WriteLine($"  length {r.Length:0} along at mouth {r.Along[k]:0} points {r.Points.Length} k {k}");
        Console.WriteLine($"reach {r.Id} {(r.EndsInSea ? "sea" : "lake")} width {r.Width[^1]:0} m catchment {r.Catchment[^1]:0}: mouth {cx:0},{cz:0}; worst step {worst:0.00} m at {wx:0},{wz:0} " +
            $"({a.Height:0.00} {a.Water} | {b.Height:0.00} {b.Water} | {c.Height:0.00} {c.Water}, land {a.Land:0.000})");
        MapRender.Window(ts, Path.Combine(v[4], $"mouth-{n}.png"), cx, cz, 400, 400, 1);
        MapRender.Exaggeration = 1.5;
        MapRender.Window(ts, Path.Combine(v[4], $"mouth-{n}-true.png"), cx, cz, 400, 400, 1);
        MapRender.Exaggeration = null;
        if (n < 2)
            foreach (int off in new[] { -12, -4, 0, 3, 8 })
            {
                int q = Math.Clamp(k + off, 1, r.Points.Length - 2);
                double tx = r.Points[q + 1].X - r.Points[q - 1].X, tz = r.Points[q + 1].Z - r.Points[q - 1].Z, tl = Math.Sqrt(tx * tx + tz * tz);
                var sb = new System.Text.StringBuilder($"  section {off,3} (w {r.Width[q]:0}): ");
                for (int d = -60; d <= 60; d += 3)
                {
                    var t = ts.Sample(r.Points[q].X - tz / tl * d, r.Points[q].Z + tx / tl * d);
                    sb.Append($"{t.Height:0.0}{(t.Water == WaterKind.Ocean ? "o" : t.Water == WaterKind.River ? "r" : t.Water == WaterKind.Lake ? "l" : "")} ");
                }
                Console.WriteLine(sb);
            }
    }
    return 0;
}

// Biome maps: --biomes=<dir>,<type id>,<km>,<seed>: climate (temperature, precipitation) and biome maps
string? plantTextures = args.FirstOrDefault(a => a.StartsWith("--plant-textures="))?[17..];
if (plantTextures != null) { PlantTextures.Write(plantTextures); Console.WriteLine("plant textures written"); return 0; }

// Cliff finder: --cliffs=<type>,<km>,<seed>,<x>,<z>,<radius> → spots where the ground drops ≥ 5 m within 3 m.
string? cliffs = args.FirstOrDefault(a => a.StartsWith("--cliffs="))?[9..];
if (cliffs != null)
{
    var v = cliffs.Split(',');
    var recipe = WorldRecipe.Catalog(CoreTests.Profiles).First(r => r.Id == v[0]);
    var sampler = new TerrainSampler(new WorldPlan(WorldManifest.Create(int.Parse(v[2]), recipe.Resolve(new WorldChoices { SizeKm = double.Parse(v[1]) }), worldId: CoreTests.FixedWorldId)));
    double cx = v[3] == "c" ? sampler.Plan.Manifest.OriginX : double.Parse(v[3]), cz = v[4] == "c" ? sampler.Plan.Manifest.OriginZ : double.Parse(v[4]);
    double r = double.Parse(v[5]), step = Math.Max(37, r / 300);
    int found = 0;
    for (double z = cz - r; z < cz + r && found < 12; z += step)
        for (double x = cx - r; x < cx + r && found < 12; x += step)
        {
            double h = sampler.Height(x, z), lo = Math.Min(Math.Min(sampler.Height(x + 3, z), sampler.Height(x - 3, z)), Math.Min(sampler.Height(x, z + 3), sampler.Height(x, z - 3)));
            if (v.Length > 8 && sampler.Plan.Biomes.Pool[sampler.Plan.Biomes.Sample(x, z).A].Def.Id != v[8]) continue;
            if (h - lo > (v.Length > 7 ? double.Parse(v[7]) : 4) && h > sampler.Plan.Profile.SeaLevel + 3 && h < sampler.Plan.Profile.SeaLevel + (v.Length > 6 ? double.Parse(v[6]) : 40)) { Console.WriteLine($"cliff at {x:0},{z:0}: top {h:0.0} m, drop {h - lo:0.0} m"); found++; }
        }
    return 0;
}

// Biome probe: --biome-probe=<type id>,<km>,<seed>,<x>,<z> → geography, climate and the blend at that point.
string? biomeProbe = args.FirstOrDefault(a => a.StartsWith("--biome-probe="))?[14..];
if (biomeProbe != null)
{
    var v = biomeProbe.Split(',');
    var recipe = WorldRecipe.Catalog(CoreTests.Profiles).First(r => r.Id == v[0]);
    var plan = new WorldPlan(WorldManifest.Create(int.Parse(v[2]), recipe.Resolve(new WorldChoices { SizeKm = double.Parse(v[1]) }), worldId: CoreTests.FixedWorldId));
    double x = double.Parse(v[3]), z = double.Parse(v[4]);
    plan.Macro.Sample(x, z, WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out double land, out double h, out double m);
    var sampler = new TerrainSampler(plan);
    var (t, p) = plan.Climate.At(x, z, Math.Max(0, h));
    var (a, b, share) = plan.Biomes.Sample(x, z);
    Console.WriteLine($"macro land {land:0.000} height {h:0} m mountain {m:0.00}; ground {sampler.Height(x, z):0.0} m; {t:0.0} °C {p:0} mm; lat {plan.Climate.Latitude(z):0.0}");
    Console.WriteLine($"biome {plan.Biomes.Pool[a].Def.Id} {share:0.00} / {plan.Biomes.Pool[b].Def.Id}");
    if (plan.Biomes.TerrainAt(x, z, out var all, allBiomes: true))
        Console.WriteLine($"all-biome blend: pools {all.Pools:0.00} flatten {all.Flatten:0.00} dunes {all.Dunes:0.00} mesas {all.Mesas:0.00} spires {all.Spires:0.00}");
    if (plan.Biomes.TerrainAt(x, z, out var mods) && mods.Pools + mods.Flatten > 0)
    {
        double cover = mods.Pools, depth = mods.PoolDepth, size = mods.PoolSize, channels = mods.PoolChannels, flatten = mods.Flatten;
        int wet = 0, total = 0;
        for (double dz = -250; dz < 250; dz += 2) for (double dx = -250; dx < 250; dx += 2) { total++; if (sampler.Sample(x + dx, z + dz).Water == WaterKind.Pond) wet++; }
        plan.Macro.SampleGradient(WorldPlan.HeightChannel, x, z, out double gx, out double gz);
        Console.WriteLine($"wetland: pools {cover:0.00} depth {depth:0.0} m size {size:0} m channels {channels:0.00} flatten {flatten:0.00}; macro slope {Math.Sqrt(gx * gx + gz * gz):0.000}; ponds over 500 m: {wet * 100.0 / total:0.0} %");
    }
    return 0;
}

// Valley probe (captures from under a cloud layer, D171): --lows=<profile>,<seed>,<x>,<z>,<radius m> prints the lowest
// dry ground on a 400 m grid around a point, with its height above the sea and how far the land around rises.
string? lowsArg = args.FirstOrDefault(a => a.StartsWith("--lows="))?[7..];
if (lowsArg != null)
{
    var v = lowsArg.Split(',');
    var s = CoreTests.Sampler(v[0], int.Parse(v[1]));
    double cx = double.Parse(v[2]), cz = double.Parse(v[3]), r = double.Parse(v[4]), sea = s.Plan.Profile.SeaLevel;
    var found = new List<(double H, double X, double Z, double Rise)>();
    for (double x = cx - r; x <= cx + r; x += 400)
        for (double z = cz - r; z <= cz + r; z += 400)
        {
            var t = s.Sample(x, z);
            if (t.Water != WaterKind.None) continue;
            double rise = 0;
            for (int k = 0; k < 8; k++) rise = Math.Max(rise, s.Height(x + Math.Cos(k * Math.PI / 4) * 1500, z + Math.Sin(k * Math.PI / 4) * 1500) - t.Height);
            found.Add((t.Height - sea, x, z, rise));
        }
    foreach (var f in found.OrderBy(f => f.H).Take(12)) Console.WriteLine($"{f.X:0} {f.Z:0} ground {f.H:0} m, land within 1.5 km rises {f.Rise:0} m");
    return 0;
}

// Point probe on a test profile: --point=<profile id>,<seed>,<x>,<z> → biome terrain mods and heights (rev 11 / current).
string? pointArg = args.FirstOrDefault(a => a.StartsWith("--point="))?[8..];
if (pointArg != null)
{
    var v = pointArg.Split(',');
    var s = CoreTests.Sampler(v[0], int.Parse(v[1]));
    var s11 = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = 11 }));
    double x = double.Parse(v[2]), z = double.Parse(v[3]);
    s.Plan.Biomes.TerrainAt(x, z, out var m, allBiomes: true);
    Console.WriteLine($"mods: flatten {m.Flatten:0.000} pools {m.Pools:0.000} dunes {m.Dunes:0.000} mesas {m.Mesas:0.000} spires {m.Spires:0.000} presence {m.SpirePresence:0.000}");
    for (int dz = -2; dz <= 2; dz++)
    {
        var sb = new StringBuilder();
        for (int dx = -2; dx <= 2; dx++) sb.Append($"{s.Height(x + dx, z + dz):0.00}/{s11.Height(x + dx, z + dz):0.00}  ");
        Console.WriteLine(sb.ToString());
    }
    return 0;
}

// Lakes of a test profile: --lakes=<profile id>,<seed> → centre, area, level.
string? lakesArg = args.FirstOrDefault(a => a.StartsWith("--lakes="))?[8..];
if (lakesArg != null)
{
    var v = lakesArg.Split(',');
    var s = CoreTests.Sampler(v[0], int.Parse(v[1]));
    foreach (var lake in s.Plan.Hydrology.Lakes.OrderByDescending(l => l.Area).Take(8))
        Console.WriteLine($"lake {lake.Id}: centre {lake.Center.X:0},{lake.Center.Z:0} area {lake.Area / 1e6:0.00} km² level {lake.Level:0.0}");
    return 0;
}

// Steep river stretches (rapids) of a test profile: --steep-rivers=<profile id>,<seed>.
string? steepArg = args.FirstOrDefault(a => a.StartsWith("--steep-rivers="))?[15..];
if (steepArg != null)
{
    var v = steepArg.Split(',');
    var s = CoreTests.Sampler(v[0], int.Parse(v[1]));
    var found = new List<(double Slope, double X, double Z, double Width)>();
    foreach (var r in s.Plan.Hydrology.Reaches)
        for (int i = 0; i + 1 < r.Points.Length; i++)
        {
            int j = i; while (j + 1 < r.Points.Length && r.Along[j] - r.Along[i] < 150) j++;
            double run = r.Along[j] - r.Along[i];
            if (run < 100 || r.Width[i] < 8) continue;
            found.Add(((r.Surface[i] - r.Surface[j]) / run, r.Points[i].X, r.Points[i].Z, r.Width[i]));
        }
    double band = v.Length > 2 ? double.Parse(v[2]) / 100 : 1;
    foreach (var f in found.Where(f => f.Slope <= band).OrderByDescending(f => f.Slope).Where((f, k) => k % 7 == 0).Take(6))
        Console.WriteLine($"slope {f.Slope * 100:0.0} % at {f.X:0},{f.Z:0} width {f.Width:0} m");
    return 0;
}

// Height profile: --profile=<type id>,<km>,<seed>,<x>,<z>,<dx>,<dz>,<n> → n heights from (x,z) in steps (dx,dz).
string? profileArg = args.FirstOrDefault(a => a.StartsWith("--profile="))?[10..];
if (profileArg != null)
{
    var v = profileArg.Split(',');
    var recipe = WorldRecipe.Catalog(CoreTests.Profiles).First(r => r.Id == v[0]);
    var plan = new WorldPlan(WorldManifest.Create(int.Parse(v[2]), recipe.Resolve(new WorldChoices { SizeKm = double.Parse(v[1]) }), worldId: CoreTests.FixedWorldId));
    if (v.Length > 8) plan = new WorldPlan(plan.Manifest, style: plan.Style with { Revision = int.Parse(v[8]) });
    var sampler = new TerrainSampler(plan);
    double x = double.Parse(v[3]), z = double.Parse(v[4]), dx = double.Parse(v[5]), dz = double.Parse(v[6]);
    int n = int.Parse(v[7]);
    var sb = new StringBuilder();
    for (int i = 0; i < n; i++) sb.Append($"{sampler.Height(x + dx * i, z + dz * i):0.0} ");
    Console.WriteLine(sb.ToString());
    return 0;
}

string? biomeMaps = args.FirstOrDefault(a => a.StartsWith("--biomes="))?[9..];
if (biomeMaps != null)
{
    var v = biomeMaps.Split(',');
    var recipe = WorldRecipe.Catalog(CoreTests.Profiles).First(r => r.Id == v[1]);
    var plan = new WorldPlan(WorldManifest.Create(int.Parse(v[3]), recipe.Resolve(new WorldChoices { SizeKm = double.Parse(v[2]) }), worldId: CoreTests.FixedWorldId));
    var sw = Stopwatch.StartNew();
    var climate = new Climate(plan);
    Console.WriteLine($"climate {sw.ElapsedMilliseconds} ms, raster {climate.Raster.Size}², latitudes {climate.Latitude(plan.Manifest.OriginZ - plan.Profile.WorldSize / 2):0}° … {climate.Latitude(plan.Manifest.OriginZ + plan.Profile.WorldSize / 2):0}°");
    Directory.CreateDirectory(v[0]);
    int px = 512; double world = plan.Profile.WorldSize, step = world / px;
    var map = plan.Biomes;
    var counts = new int[map.Pool.Count]; int landCount = 0;
    var best = new (double Share, double X, double Z)[map.Pool.Count];
    var tImg = new byte[px * px * 3]; var pImg = new byte[px * px * 3]; var bImg = new byte[px * px * 3];
    for (int j = 0; j < px; j++)
        for (int i = 0; i < px; i++)
        {
            double x = plan.Manifest.OriginX - world / 2 + (i + .5) * step, z = plan.Manifest.OriginZ - world / 2 + (j + .5) * step;
            plan.Macro.Sample(x, z, WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out double land, out double h, out _);
            int k = (j * px + i) * 3;
            if (land <= 0) { foreach (var img in new[] { tImg, pImg, bImg }) { img[k] = 30; img[k + 1] = 55; img[k + 2] = 95; } continue; }
            var (t, p) = climate.At(x, z, h);
            double tn = Math.Clamp((t + 20) / 50, 0, 1), pn = Math.Clamp(Math.Log10(Math.Max(1, p)) / 3.6, 0, 1);
            tImg[k] = (byte)(255 * tn); tImg[k + 1] = (byte)(80 + 100 * (1 - Math.Abs(tn - .5) * 2)); tImg[k + 2] = (byte)(255 * (1 - tn));
            pImg[k] = (byte)(220 * (1 - pn)); pImg[k + 1] = (byte)(200 * (1 - pn) + 40); pImg[k + 2] = (byte)(120 + 135 * pn);
            var (ba, bb, share) = map.Sample(x, z);
            counts[ba]++; landCount++;
            if (share > best[ba].Share && (i * 7 + j * 13) % 5 == 0) best[ba] = (share, x, z);
            var ca = map.Pool[ba].Map; var cb = map.Pool[bb].Map;
            bImg[k] = (byte)(255 * (ca.R * share + cb.R * (1 - share))); bImg[k + 1] = (byte)(255 * (ca.G * share + cb.G * (1 - share))); bImg[k + 2] = (byte)(255 * (ca.B * share + cb.B * (1 - share)));
        }
    Png.Write(Path.Combine(v[0], $"{v[1]}-{v[2]}-temperature.png"), px, px, tImg);
    Png.Write(Path.Combine(v[0], $"{v[1]}-{v[2]}-precipitation.png"), px, px, pImg);
    Png.Write(Path.Combine(v[0], $"{v[1]}-{v[2]}-biomes.png"), px, px, bImg);
    for (int b = 0; b < map.Pool.Count; b++) if (counts[b] > 0) Console.WriteLine($"  {map.Pool[b].Def.Name}: {counts[b] * 100.0 / landCount:0.0} %  e.g. at {best[b].X:0},{best[b].Z:0}");
    return 0;
}

// Close-up of a world type: --typewin=<type id>,<km>,<seed>,<x>,<z>,<side>,<pixels>,<png>[,<exaggeration>]
string? typewin = args.FirstOrDefault(a => a.StartsWith("--typewin="))?[10..];
if (typewin != null)
{
    var v = typewin.Split(',');
    var recipe = WorldRecipe.Catalog(CoreTests.Profiles).First(r => r.Id == v[0]);
    var plan = new WorldPlan(WorldManifest.Create(int.Parse(v[2]), recipe.Resolve(new WorldChoices { SizeKm = double.Parse(v[1]) }), worldId: CoreTests.FixedWorldId));
    if (v.Length > 9 && int.Parse(v[9]) < 9) plan = new WorldPlan(WorldManifest.Create(int.Parse(v[2]), recipe.Resolve(new WorldChoices { SizeKm = double.Parse(v[1]), ScaleRevision = int.Parse(v[9]) }), worldId: CoreTests.FixedWorldId), style: plan.Style with { Revision = int.Parse(v[9]) });
    if (v.Length > 10) plan = new WorldPlan(plan.Manifest, style: plan.Style with { BankVariation = double.Parse(v[10]) });
    var ts = new TerrainSampler(plan);
    if (v.Length > 8) MapRender.Exaggeration = double.Parse(v[8]);
    double x, z;
    if (v[3] == "lowland")
    {
        // A dry lowland spot halfway from the first landmass centre toward its coast.
        var home = plan.Layout.Land.Count > 0 ? plan.Layout.Land[0].Center : new Point2(plan.Manifest.OriginX, plan.Manifest.OriginZ);
        x = home.X; z = home.Z + (plan.Layout.Land.Count > 0 ? plan.Layout.Land[0].RadiusZ * .3 : 0);
    }
    else if (v[3] == "peak")
    {
        // The highest macro cell (a mountain), for close-ups.
        var hr = plan.Macro.Channel(WorldPlan.HeightChannel); int best = 0;
        for (int q = 1; q < hr.Length; q++) if (hr[q] > hr[best]) best = q;
        x = plan.Macro.CellX(best % plan.Macro.Size); z = plan.Macro.CellZ(best / plan.Macro.Size);
        Console.WriteLine($"peak at {x:0},{z:0}, profile mountains {plan.Profile.MountainHeight:0} m high, {plan.Profile.MountainWidth:0} m wide, hills {plan.Profile.HillHeight:0} m / {plan.Profile.HillScale:0} m");
    }
    else { x = double.Parse(v[3]); z = double.Parse(v[4]); }
    MapRender.Window(ts, v[7], x, z, double.Parse(v[5]), int.Parse(v[6]), 2);
    var sb = new System.Text.StringBuilder("row: ");
    for (int d = -20; d <= 20; d++) sb.Append($"{ts.Sample(x + d, z).Height:0.0} ");
    Console.WriteLine(sb);
    var t = ts.Sample(x, z);
    Console.WriteLine($"land {t.Land:0.000} mountain {t.Mountain:0.00} detail {t.Detail:0.00} water {t.Water}; macro cell {plan.Macro.Cell:0} m");
    return 0;
}

// World type maps: --types=<dir>[,<km>[,<seed>[,<only id>]]] renders every world type's whole-world map
string? types = args.FirstOrDefault(a => a.StartsWith("--types="))?[8..];
if (types != null)
{
    var v = types.Split(',');
    double km = v.Length > 1 ? double.Parse(v[1]) : 256;
    int seed = v.Length > 2 ? int.Parse(v[2]) : 42;
    Directory.CreateDirectory(v[0]);
    foreach (var recipe in WorldRecipe.Catalog(CoreTests.Profiles))
    {
        if (v.Length > 3 && recipe.Id != v[3]) continue;
        var sw = Stopwatch.StartNew();
        var plan = new WorldPlan(WorldManifest.Create(seed, recipe.Resolve(new WorldChoices { SizeKm = km }), worldId: CoreTests.FixedWorldId));
        var land = plan.Macro.Channel(WorldPlan.LandChannel);
        var ts = new TerrainSampler(plan);
        MapRender.Window(ts, Path.Combine(v[0], $"{recipe.Id}-{km}km-{seed}.png"), plan.Manifest.OriginX, plan.Manifest.OriginZ, plan.Profile.WorldSize, 512);
        Console.WriteLine($"{recipe.Id}: land {land.Count(x => x > 0) * 100.0 / land.Length:0} % (grid {plan.HeightmapLandShare * 100:0} %), {plan.LandformShapes} landform shapes, " +
            $"{plan.Hydrology.Reaches.Count} reaches, {plan.Hydrology.Lakes.Count} lakes, {sw.Elapsed.TotalSeconds:0.0} s");
    }
    return 0;
}

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
            for (int j = 0; j < Math.Min(n, 4); j++) Console.WriteLine($"    reach {hits[j].Reach} d {hits[j].Distance:0.00} s {hits[j].Surface:0.000} w {hits[j].Width:0.00} along {hits[j].Along:0.0} side {hits[j].Side} half {ls.WaterHalfWidth(hits[j], out _):0.00}");
    }
    return 0;
}

if (args.Contains("--lake-mouths"))
{
    foreach (var (id, seed) in new[] { ("balanced", 0), ("mountainous", 42) })
    {
        var ms = CoreTests.Sampler(id, seed);
        foreach (var r in ms.Plan.Hydrology.Reaches.Where(r => r.EndLake >= 0).OrderByDescending(r => r.Catchment[^1]).Take(2))
        {
            var e = r.Points[^1];
            string png = $"/tmp/claude-0/lake-mouth-{id}-{r.Id}.png";
            MapRender.Window(ms, png, e.X, e.Z, 1500, 750);
            Console.WriteLine($"{id} reach {r.Id} into lake {r.EndLake} at {e.X:0},{e.Z:0} width {r.Width[^1]:0} -> {png}");
        }
    }
    return 0;
}

if (args.Contains("--bank-probe"))
{
    var bs = CoreTests.Sampler("mountainous", 42);
    for (double a = 25285; a < 25300; a += .5)
        Console.WriteLine($"{a:0.0} {bs.WaterHalfWidth(new RiverHit(219, 5, 0, 19.7, 1, 1, a, 1), out _):0.000}");
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
    GameplayTests.Register(filter);
    RecipeTests.Register(filter);
    ClimateTests.Register(filter);
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
                MapRender.Window(sampler, Path.Combine(directory, $"{id}-{seed}-mountain-flank-1500m.png"), bx + 3000, bz + 3000, 1500, 750);
                Console.WriteLine($"  mountain flank close-up at {bx + 3000:0}, {bz + 3000:0}");
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
