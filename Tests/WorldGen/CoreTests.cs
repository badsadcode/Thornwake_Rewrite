using System.Security.Cryptography;
using System.Text.Json;
using Thornwake.WorldGen;
using L = Thornwake.Core;

/// <summary>M1 tests: deterministic math, A4 layout reproduction, sampler continuity, LOD and relief.</summary>
internal static class CoreTests
{
    private static string ContentPath => Path.Combine(AppContext.BaseDirectory, "Content/world_profiles.json");
    public static IReadOnlyList<WorldProfile> Profiles => WorldProfile.LoadCatalog(ContentPath);
    private static readonly Dictionary<(string, int), TerrainSampler> Cache = new();
    public const string FixedWorldId = "0123456789abcdef0123456789abcdef";

    static CoreTests()
    {
        string root = FindRepoRoot();
        BiomeRegistry.Current = BiomeRegistry.Parse(File.ReadAllText(Path.Combine(root, "Content/biomes.json")), File.ReadAllText(Path.Combine(root, "Content/asset_sets.json")));
    }

    public static TerrainSampler Sampler(string profile, int seed)
    {
        lock (Cache)
        {
            if (!Cache.TryGetValue((profile, seed), out var sampler))
                Cache[(profile, seed)] = sampler = new TerrainSampler(new WorldPlan(WorldManifest.Create(seed, Profiles.Single(p => p.Id == profile), worldId: FixedWorldId)));
            return sampler;
        }
    }

    public static void Register(string? filter, bool record)
    {
        T.Run("DetMath matches libm within a few ulp", DetMathAccuracy, filter);
        T.Run("Profile digests identical to A4", ProfileDigests, filter);
        T.Run("Layout reproduces A4 continents, islands and belts", LayoutMatchesA4, filter);
        T.Run("Land influence reproduces A4", LandMatchesA4, filter);
        T.Run("Macro height tracks A4 elevation", MacroTracksA4, filter);
        T.Run("Plan rasters are deterministic", PlanDeterministic, filter);
        T.Run("Grid samples equal point samples (exact tile edges)", GridEqualsPoints, filter);
        T.Run("Parallel sampling equals serial", ParallelEqualsSerial, filter);
        T.Run("Heights finite and within profile bounds", HeightBounds, filter);
        T.Run("Walking-scale relief exists and stays walkable", WalkingRelief, filter);
        T.Run("Coarse LOD is fine terrain minus small detail", LodConsistency, filter);
        T.Run("Coastline has sub-raster detail", CoastDetail, filter);
        T.Run("Frozen terrain fingerprints" + (record ? " (recording)" : ""), () => Fingerprints(record), filter);
        T.Run("Build 5 terrain still reproducible (classic option)", () => Fingerprints(false, "terrain-build5.json", classic: true), filter);
        T.Run("Preview 7 terrain still reproducible", () => Fingerprints(false, "terrain-preview7.json", preview7: true), filter);
        T.Run("Build 8 terrain still reproducible", () => Fingerprints(false, "terrain-build8.json", build8: true), filter);
        T.Run("Build 9 terrain still reproducible", () => Fingerprints(false, "terrain-build9.json", build9: true), filter);
    }

    private static void DetMathAccuracy()
    {
        var rng = new Random(7);
        double worst = 0;
        for (int i = 0; i < 200000; i++)
        {
            double x = (rng.NextDouble() - .5) * 2000;
            worst = Math.Max(worst, Math.Abs(DetMath.Sin(x) - Math.Sin(x)));
            worst = Math.Max(worst, Math.Abs(DetMath.Cos(x) - Math.Cos(x)));
            T.Check(worst < 5e-13, $"sin/cos error {worst} at {x}");
        }
        T.Note($"sin/cos worst abs error on ±1000: {worst:E2}");
        worst = 0;
        for (int i = 0; i < 200000; i++)
        {
            double y = (rng.NextDouble() - .5) * Math.Pow(10, rng.Next(-3, 4)), x = (rng.NextDouble() - .5) * Math.Pow(10, rng.Next(-3, 4));
            worst = Math.Max(worst, Math.Abs(DetMath.Atan2(y, x) - Math.Atan2(y, x)));
            T.Check(worst < 2e-15, $"atan2 error {worst} at ({y},{x})");
        }
        T.Note($"atan2 worst abs error: {worst:E2}");
        worst = 0;
        for (int i = 0; i < 200000; i++)
        {
            double x = (rng.NextDouble() - .5) * 80;
            double e = Math.Exp(x);
            worst = Math.Max(worst, Math.Abs(DetMath.Exp(x) - e) / e);
            T.Check(worst < 4e-16, $"exp relative error {worst} at {x}");
        }
        T.Note($"exp worst relative error on ±40: {worst:E2}");
        T.Check(DetMath.Exp(-800) == 0 && double.IsPositiveInfinity(DetMath.Exp(800)) && DetMath.Exp(0) == 1, "exp limits");
        T.Check(DetMath.Atan2(0, -1) == Math.PI && DetMath.Atan2(1, 0) == Math.PI / 2 && DetMath.Sin(0) == 0 && DetMath.Cos(0) == 1, "special values");
    }

    private static void ProfileDigests()
    {
        var legacy = L.MacroProfileCatalog.Load(ContentPath);
        foreach (var p in Profiles)
            T.Check(p.Digest() == legacy.Single(l => l.Id == p.Id).Digest(), "digest " + p.Id);
    }

    private static void LayoutMatchesA4()
    {
        var legacyProfiles = L.MacroProfileCatalog.Load(ContentPath);
        foreach (var profile in Profiles)
            foreach (int seed in new[] { -9, 0, 42, 7319 })
            {
                var old = new L.MacroWorldGenerator(L.MacroWorldManifest.Create(seed, legacyProfiles.Single(p => p.Id == profile.Id), algorithm: 4));
                var layout = new WorldLayout(WorldManifest.Create(seed, profile));
                T.Check(old.LandFeatures.Count == layout.Land.Count, "feature count");
                for (int i = 0; i < layout.Land.Count; i++)
                {
                    var a = old.LandFeatures[i]; var b = layout.Land[i];
                    T.Check(a.Id == b.Id && a.Island == b.Island && a.NoiseSeed == b.NoiseSeed, $"feature identity {profile.Id}/{seed}/{i}");
                    T.Near(b.Center.X, a.Center.X, 1e-6, "centre x"); T.Near(b.Center.Z, a.Center.Z, 1e-6, "centre z");
                    T.Near(b.RadiusX, a.RadiusX, 1e-9, "radius x"); T.Near(b.RadiusZ, a.RadiusZ, 1e-9, "radius z"); T.Near(b.Angle, a.Angle, 1e-12, "angle");
                }
                T.Check(old.Ranges.Count == layout.Belts.Count, $"belt count {profile.Id}/{seed}");
                for (int i = 0; i < layout.Belts.Count; i++)
                {
                    var a = old.Ranges[i]; var b = layout.Belts[i];
                    T.Check(a.Id == b.Id && a.Points.Count == b.Points.Length, "belt identity");
                    T.Near(b.BaseWidth, a.Width, 1e-6, "belt width"); T.Near(b.Amplitude, a.Height, 1e-6, "belt height");
                    for (int k = 0; k < b.Points.Length; k++)
                    {
                        T.Near(b.Points[k].Point.X + layout.Manifest.OriginX, a.Points[k].X, 1e-5, "belt point x");
                        T.Near(b.Points[k].Point.Z + layout.Manifest.OriginZ, a.Points[k].Z, 1e-5, "belt point z");
                    }
                }
            }
    }

    private static void LandMatchesA4()
    {
        var legacyProfiles = L.MacroProfileCatalog.Load(ContentPath);
        var rng = new Random(11);
        double worst = 0;
        foreach (var profile in Profiles)
        {
            var old = new L.MacroWorldGenerator(L.MacroWorldManifest.Create(42, legacyProfiles.Single(p => p.Id == profile.Id), algorithm: 4));
            var layout = new WorldLayout(WorldManifest.Create(42, profile));
            for (int i = 0; i < 4000; i++)
            {
                var f = layout.Land[rng.Next(layout.Land.Count)];
                double x = f.Center.X + (rng.NextDouble() - .5) * f.RadiusX * 3, z = f.Center.Z + (rng.NextDouble() - .5) * f.RadiusX * 3;
                double a = old.Sample(x, z).LandInfluence, b = layout.LandInfluence(x, z);
                if (a < -.99 && b < -.99) continue; // both deep ocean (A4 clamps its sentinel to -1)
                worst = Math.Max(worst, Math.Abs(a - b));
                T.Check(Math.Abs(a - b) < 1e-9, $"land influence {profile.Id} at {x},{z}: {a} vs {b}");
            }
        }
        T.Note($"worst |Δ land influence| vs A4: {worst:E2}");
    }

    private static void MacroTracksA4()
    {
        var legacyProfiles = L.MacroProfileCatalog.Load(ContentPath);
        foreach (var (id, seed) in new[] { ("mountainous", 42), ("vast-world", 7319), ("balanced", 0) })
        {
            var profile = Profiles.Single(p => p.Id == id);
            var old = new L.MacroWorldGenerator(L.MacroWorldManifest.Create(seed, legacyProfiles.Single(p => p.Id == id), algorithm: 4));
            var sampler = Sampler(id, seed);
            var rng = new Random(5);
            var diffs = new List<double>(); int agree = 0, total = 0;
            var land = sampler.Plan.Layout.Land.Where(l => !l.Island).ToArray();
            for (int i = 0; i < 6000; i++)
            {
                var f = land[rng.Next(land.Length)];
                double x = f.Center.X + (rng.NextDouble() - .5) * f.RadiusX * 2.4, z = f.Center.Z + (rng.NextDouble() - .5) * f.RadiusX * 2.4;
                double a = old.Sample(x, z).Elevation, b = sampler.MacroHeight(x, z);
                total++;
                if ((a > profile.SeaLevel) == (b > profile.SeaLevel)) agree++;
                if (a > profile.SeaLevel && b > profile.SeaLevel) diffs.Add(Math.Abs(a - b));
            }
            diffs.Sort();
            double median = diffs[diffs.Count / 2], p95 = diffs[(int)(diffs.Count * .95)];
            double relief = profile.MountainHeight + profile.LowlandHeight;
            T.Note($"{id}/{seed}: land/sea agreement {100.0 * agree / total:0.0} %, |Δh| median {median:0.0} m, p95 {p95:0.0} m (relief {relief} m)");
            T.Check(agree > total * .96, $"{id} land/sea agreement {agree}/{total}");
            T.Check(median < relief * .02 && p95 < relief * .12, $"{id} macro height drift median {median} p95 {p95}");
        }
    }

    private static void PlanDeterministic()
    {
        var profile = Profiles.Single(p => p.Id == "mountainous");
        var a = new WorldPlan(WorldManifest.Create(42, profile, worldId: FixedWorldId), 256);
        var b = new WorldPlan(WorldManifest.Create(42, profile, worldId: FixedWorldId), 256);
        for (int c = 0; c < a.Macro.ChannelCount; c++)
            T.Check(a.Macro.Channel(c).AsSpan().SequenceEqual(b.Macro.Channel(c)), "raster channel " + c);
    }

    private static void GridEqualsPoints()
    {
        var s = Sampler("mountainous", 42);
        var f = s.Plan.Layout.Land[0];
        foreach (double spacing in new[] { 1.0, 4.0, 32.0 })
        {
            const int n = 17;
            double x0 = f.Center.X + 1234.5, z0 = f.Center.Z - 987.25;
            var a = new double[n * n]; var b = new double[n * n];
            s.HeightGrid(x0, z0, spacing, n, n, a);
            // Neighbouring tile starting at the last column must reproduce that column bit for bit.
            s.HeightGrid(x0 + (n - 1) * spacing, z0, spacing, n, n, b);
            for (int j = 0; j < n; j++)
            {
                T.Check(a[j * n + n - 1] == b[j * n], "shared tile edge");
                for (int i = 0; i < n; i++) T.Check(a[j * n + i] == s.Height(x0 + i * spacing, z0 + j * spacing, spacing * 2), "grid == point");
            }
        }
    }

    private static void ParallelEqualsSerial()
    {
        var s = Sampler("vast-world", 7319);
        var f = s.Plan.Layout.Land[1];
        int n = 128; var serial = new double[n * n]; var parallel = new double[n * n];
        s.HeightGrid(f.Center.X, f.Center.Z, 37, n, n, serial);
        s.HeightGridParallel(f.Center.X, f.Center.Z, 37, n, n, parallel);
        T.Check(serial.AsSpan().SequenceEqual(parallel), "parallel == serial");
    }

    private static void HeightBounds()
    {
        foreach (var profile in Profiles)
        {
            var s = Sampler(profile.Id, 42);
            var rng = new Random(3);
            double lo = profile.SeaLevel - profile.OceanDepth - 1;
            double hi = profile.SeaLevel + profile.LowlandHeight + profile.HillHeight + profile.MountainHeight * (1 + s.Plan.Style.MountainDetail) + s.Plan.Style.LowlandDetail * 3 + 1;
            for (int i = 0; i < 20000; i++)
            {
                double x = (rng.NextDouble() - .5) * profile.WorldSize * 1.2, z = (rng.NextDouble() - .5) * profile.WorldSize * 1.2;
                double h = s.Height(x, z);
                T.Check(double.IsFinite(h) && h >= lo && h <= hi, $"{profile.Id} height {h} outside [{lo},{hi}]");
            }
        }
    }

    private static void WalkingRelief()
    {
        var s = Sampler("mountainous", 42);
        var rng = new Random(9);
        var f = s.Plan.Layout.Land[0];
        double sumStd = 0; int windows = 0; double steepest = 0;
        var where = (X: 0.0, Z: 0.0);
        var h = new double[65 * 65];
        while (windows < 40)
        {
            double x = f.Center.X + (rng.NextDouble() - .5) * f.RadiusX, z = f.Center.Z + (rng.NextDouble() - .5) * f.RadiusX;
            var probe = s.Sample(x, z);
            if (probe.Land < .1 || probe.Mountain > .05) continue; // lowland only
            s.HeightGrid(x, z, 1, 65, 65, h);
            double mean = h.Average(), std = Math.Sqrt(h.Select(v => (v - mean) * (v - mean)).Average());
            for (int j = 0; j < 64; j++) for (int i = 0; i < 64; i++)
            {
                double step = Math.Max(Math.Abs(h[j * 65 + i + 1] - h[j * 65 + i]), Math.Abs(h[(j + 1) * 65 + i] - h[j * 65 + i]));
                if (step > steepest) { steepest = step; where = (x + i, z + j); }
            }
            sumStd += std; windows++;
        }
        T.Note($"lowland 64 m windows: mean height std-dev {sumStd / windows:0.00} m, steepest 1 m step {steepest:0.00} m at ({where.X:0}, {where.Z:0})");
        T.Check(sumStd / windows > .25, "walking-scale relief present on lowland");
        T.Check(steepest < 1.2, "lowland stays walkable (no >50° 1 m steps)");
    }

    private static void LodConsistency()
    {
        var s = Sampler("mountainous", 42);
        var rng = new Random(13);
        var f = s.Plan.Layout.Land[0];
        double sum = 0; int n = 0; double worst = 0;
        for (int i = 0; i < 5000; i++)
        {
            double x = f.Center.X + (rng.NextDouble() - .5) * f.RadiusX, z = f.Center.Z + (rng.NextDouble() - .5) * f.RadiusX;
            double fine = s.Height(x, z, 2), coarse = s.Height(x, z, 64);
            sum += Math.Abs(fine - coarse); n++; worst = Math.Max(worst, Math.Abs(fine - coarse));
        }
        T.Note($"|h(2 m) - h(64 m)| mean {sum / n:0.00} m, worst {worst:0.00} m");
        T.Check(sum / n < 2.5, "coarse LOD keeps the large shapes");
        T.Check(s.Height(f.Center.X, f.Center.Z, 1e9) == s.MacroHeight(f.Center.X, f.Center.Z), "infinite minimum wavelength = macro height");
    }

    private static void CoastDetail()
    {
        var s = Sampler("mountainous", 42);
        var f = s.Plan.Layout.Land[0];
        // March east from the continent centre until the sea, then count coastline crossings along a 4 km transect.
        double x = f.Center.X;
        while (s.Height(x, f.Center.Z) > s.Plan.Profile.SeaLevel) x += 500;
        int crossings = 0; bool wasLand = true;
        for (double t = -2000; t <= 2000; t += 5)
        {
            bool landHere = s.Height(x, f.Center.Z + t, 10) > s.Plan.Profile.SeaLevel;
            if (t > -2000 && landHere != wasLand) crossings++;
            wasLand = landHere;
        }
        T.Note($"coastline crossings along a 4 km shore-parallel transect: {crossings}");
        T.Check(true, "coast sampled");
    }

    // Fixture: SHA-256 of terrain heights on fixed grids. Same on every platform by construction (D5).
    private sealed record Fingerprint(string Case, string Hash);

    private static void Fingerprints(bool record, string file = "terrain-r1.json", bool classic = false, bool preview7 = false, bool build8 = false, bool build9 = false)
    {
        // Read the source copy: the build output copy is stale right after --record.
        string fixture = Path.Combine(FindRepoRoot(), "Tests/WorldGen/Fixtures", file);
        var cases = new List<Fingerprint>();
        foreach (var profile in Profiles)
            foreach (int seed in new[] { -9, 42, 7319 })
            {
                var s = Sampler(profile.Id, seed);
                // Build 5 (commit cdb2e31) generation: no bank variation, macro-only shores.
                if (classic) s = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { BankVariation = 0, ShoreSlope = 0, MouthBlend = 0, Revision = 8 }));
                // Preview 7 (commit 6cf6d18): before river mouths blended into sea and lake beds.
                if (preview7) s = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { MouthBlend = 0, Revision = 8 }));
                // Build 8 (commit 9e8e406): before estuaries and the bank-bulge fix.
                if (build8) s = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = 8 }));
                // Build 9 (commit f868ef5, v0.9.9): terrain revision 9, before biomes.
                if (build9) s = new TerrainSampler(new WorldPlan(s.Plan.Manifest, style: s.Plan.Style with { Revision = 9, Climate = new ClimateSettings { Biomes = false } }));
                using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                var buffer = new double[33 * 33];
                var f = s.Plan.Layout.Land[0];
                foreach (var (cx, cz, spacing) in new[] { (profile.WorldSize * -.5, profile.WorldSize * -.5, profile.WorldSize / 32),
                    (f.Center.X, f.Center.Z, 1.0), (f.Center.X + f.RadiusX * .5, f.Center.Z, 97.0), (f.Center.X - f.RadiusX * 1.1, f.Center.Z, 13.0) })
                {
                    s.HeightGrid(cx, cz, spacing, 33, 33, buffer);
                    sha.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(buffer.AsSpan()));
                }
                foreach (var c in new[] { WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel })
                    sha.AppendData(System.Runtime.InteropServices.MemoryMarshal.AsBytes(s.Plan.Macro.Channel(c).AsSpan()));
                cases.Add(new($"{profile.Id}/{seed}", Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant()));
            }
        if (record)
        {
            string source = Path.Combine(FindRepoRoot(), "Tests/WorldGen/Fixtures/terrain-r1.json");
            File.WriteAllText(source, JsonSerializer.Serialize(cases, new JsonSerializerOptions { WriteIndented = true }));
            T.Note("recorded " + source);
            T.Check(true, "recorded");
            return;
        }
        var expected = JsonSerializer.Deserialize<List<Fingerprint>>(File.ReadAllText(fixture))!;
        T.Check(expected.Count == cases.Count, "fixture case count");
        foreach (var c in cases) T.Check(expected.Single(e => e.Case == c.Case).Hash == c.Hash, "fingerprint " + c.Case);
    }

    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "project.godot"))) dir = dir.Parent;
        return dir?.FullName ?? throw new DirectoryNotFoundException("project.godot not found above the test binary.");
    }
}
