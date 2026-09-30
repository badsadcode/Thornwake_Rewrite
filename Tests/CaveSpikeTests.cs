using System.Diagnostics;
using System.Numerics;
using System.Text.Json;
using Thornwake.Core;

internal static class CaveSpikeTests
{
    public static int Run()
    {
        int checks = 0;
        void Check(bool condition, string label) { if (!condition) throw new Exception("CAVE SPIKE: " + label); checks++; }
        void Reject(Action action) { try { action(); } catch (InvalidDataException) { checks++; return; } throw new Exception("CAVE SPIKE accepted invalid save"); }
        var state = new CaveSpikeState(); var field = new CaveSpikeField(state);
        Check(field.Density(new(0, 1, -8)) < 0, "chamber contains air");
        Check(field.Density(new(0, 7, -8)) > 0, "solid roof separates cave from overworld");
        Check(field.Density(new(0, 11, -8)) < 0, "air above overworld at same XZ");
        Check(field.Density(new(9, -6, -9)) < 0 && field.WaterAt(new(9, -6, -9)) == null, "cavity below lake remains dry without connectivity");
        var lake = field.Reservoirs[0];
        Check(lake.Level > 5 && field.WaterAt(new(lake.X, lake.Level - .1f, lake.Z)) == lake.Id, "elevated reservoir has local surface");
        Check(field.WaterAt(new(1, -1.5f, -9)) == "cave-pool", "underground reservoir uses different elevation");
        Check(field.WaterAt(new(20, -6, -9)) == null, "water does not fill all lower world space");
        for (int i = 0; i < 50; i++) Check(field.Density(new(0, 1, 20 - i * .5f)) < 0, "entrance connects to chamber");
        Check(!field.TryCut(new(20, 0, 0, 2)), "cuts cannot silently escape hybrid volume footprint");
        Check(!field.TryCut(new(-8, field.Surface(-8, -8), -8, 1)), "full cut footprint respects support fixture");
        var p = new Vector3(0, -3, 0); float before = field.Density(p);
        Check(before > 0 && field.TryCut(new(0, -2.5f, 0, 1.5f)) && field.Density(p) < 0, "excavation removes real solid across four region owners");
        var id = field.DepositId;
        Check(field.DepositContains(new(0, 1, -4)) && field.DepositId == id, "spatial deposit identity independent of edits");
        string directory = Path.Combine(Path.GetTempPath(), "thornwake-cave-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory); string path = Path.Combine(directory, "spike.json");
        try
        {
            CaveSpikeStore.Save(path, state); var loaded = CaveSpikeStore.Load(path); var restored = new CaveSpikeField(loaded);
            for (int i = -12; i <= 12; i++) Check(restored.Density(new(i * .3f, -.5f, i)) == field.Density(new(i * .3f, -.5f, i)), "density survives save/reload exactly");
            Check(restored.DepositId == id, "deposit ID survives reload");
            CaveSpikeStore.Save(path, loaded); Check(File.Exists(path + ".bak"), "atomic save retains previous file");
            File.WriteAllText(path, "{\"Version\":99,\"Seed\":1,\"Cuts\":[]}"); Reject(() => CaveSpikeStore.Load(path));
            File.WriteAllText(path, "{}"); Reject(() => CaveSpikeStore.Load(path));
            File.WriteAllText(path, "{\"Version\":1,\"Seed\":1,\"Cuts\":null}"); Reject(() => CaveSpikeStore.Load(path));
            Reject(() => CaveSpikeStore.Save(path, new CaveSpikeState { Cuts = new() { new(float.NaN, 0, 0, 1) } }));
        }
        finally { foreach (string file in Directory.GetFiles(directory)) File.Delete(file); Directory.Delete(directory); }
        foreach (float spacing in new[] { 1f, .5f })
        {
            var left = VolumeMesher.Build(field, new(-1, 0), spacing, true);
            var right = VolumeMesher.Build(field, new(0, 0), spacing, true);
            Check(left.TriangleCount > 100 && right.TriangleCount > 100, "real volume surfaces generated");
            var edgeLeft = left.Vertices.Where(v => v.X == 0).Distinct().OrderBy(v => v.Y).ThenBy(v => v.Z).ToArray();
            var edgeRight = right.Vertices.Where(v => v.X == 0).Distinct().OrderBy(v => v.Y).ThenBy(v => v.Z).ToArray();
            Check(edgeLeft.SequenceEqual(edgeRight), "cross-border cut vertices agree exactly at both resolutions");
            for (int i = 0; i < left.Vertices.Count; i += 3)
            {
                var a = left.Vertices[i]; var b = left.Vertices[i + 1]; var c = left.Vertices[i + 2];
                Check(float.IsFinite(a.Y) && Vector3.Dot(Vector3.Cross(b - a, c - a), left.Normals[i] + left.Normals[i + 1] + left.Normals[i + 2]) <= .00001f,
                    "finite clockwise surface with outward normals");
            }
            Check(left.Vertices.Any(v => field.DepositContains(v) && v.Y < field.Surface(v.X, v.Z) - 1), "same spatial vein is exposed on cave wall");
            Check(left.Vertices.Any(v => field.DepositContains(v) && v.Y > field.Surface(v.X, v.Z) - .4f), "same spatial vein is exposed on surface");
            // The hybrid interface x=-16 is deliberately outside every cavity/cut: both sides use the same edge profile.
            foreach (var v in left.Vertices.Where(v => v.X == -16))
            {
                float z0 = MathF.Floor(v.Z / spacing) * spacing, t = (v.Z - z0) / spacing;
                float expected = field.Surface(-16, z0) * (1 - t) + field.Surface(-16, z0 + spacing) * t;
                Check(Math.Abs(v.Y - expected) < .00002f, "hybrid surface/volume seam lies on same linear height edge");
            }
        }
        return checks + CaveMeshTopologyTests.Run() + CaveWaterTests.Run() + LocalWaterTests.Run() + CaveBrushTests.Run() + CaveSurfaceRegressionTests.Run();
    }

    public static void Benchmark(string path)
    {
        var results = new List<object>(); var field = new CaveSpikeField(new());
        _ = VolumeMesher.Build(field, new(-1, 0), 1, true); // JIT warmup
        foreach (float spacing in new[] { 1f, .5f }) foreach (bool full in new[] { false, true })
        {
            long before = GC.GetAllocatedBytesForCurrentThread(), triangles = 0, buffers = 0, samples = 0;
            var watch = Stopwatch.StartNew();
            for (int x = -2; x <= 1; x++) for (int z = -2; z <= 1; z++)
            {
                var region = new TerrainRegion(x, z);
                var mesh = VolumeMesher.Build(field, region, spacing, full || CaveSpikeField.IsVolumeRegion(region));
                triangles += mesh.TriangleCount; buffers += mesh.BufferBytes; samples += mesh.DensitySamples;
            }
            watch.Stop();
            results.Add(new { mode = full ? "full-volume" : "hybrid", spacing, regions = 16, milliseconds = watch.Elapsed.TotalMilliseconds,
                triangles, densitySamples = samples, vertexNormalBytes = buffers, managedAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - before });
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine(File.ReadAllText(path));
    }
}





