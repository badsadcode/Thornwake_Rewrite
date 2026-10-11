using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// The benchmark (docs/BENCHMARK.md; Tom, 2026-10-11: "reproducible across different PCs, with consistent test
/// conditions, detailed performance measurements, and exported results"). Scenes come from Content/benchmarks.json:
/// a fixed place, view and hour in a world that generates identically everywhere. For each scene the runner applies a
/// quality profile, freezes time, weather and clouds, turns V-Sync and the frame limit off, waits until every terrain
/// tile is built and stays built, warms up until no more shaders compile, then records every frame: frame time, GPU
/// time, render-thread CPU time, script time and tile commits. Afterwards it hides one kind of object at a time (far
/// 3D trees, all 3D trees, tree cards, grass, rocks — each has its own render layer, so this needs no rebuild) and
/// switches costly effects off one at a time, and measures what each one saved. Results: results.json (everything),
/// summary.txt (readable) and one frames-*.csv per scene in the user folder's bench/ directory.
/// Start: menu → Benchmark, or <c>--bench=standard [--bench-profile=high] [--bench-out=dir]</c> (quits when done).
/// </summary>
public partial class WorldLab
{
    private enum BenchStage { World, Place, Settle, Warmup, Measure, Ablate, Done }

    private sealed record BenchScenario(string Id, string Name, double[]? At, string? Landmark, float[]? View, double Height, double Hour,
        double Seconds, List<double[]>? Path, double Altitude, double Speed, float Pitch, float Zoom);

    private sealed record BenchJob(BenchScenario Scene, Dictionary<string, double> Variant, string Label);

    /// <summary>One thing to take away: object kinds (hidden in every pass, shadows too), shadow casting of kinds
    /// (incl. the terrain), a setting, or a special shadow change (soft filter, shadow distance).</summary>
    private sealed record BenchAblation(string Name, uint HideLayers, string? Key, double Value, uint NoShadowLayers = 0, bool TerrainShadows = false,
        string? Special = null);

    private sealed class BenchRun
    {
        public required JsonObject Config;
        public required string Suite, Profile, OutDir;
        public required List<BenchJob> Jobs;
        public required Dictionary<string, double> Saved;
        public bool Ablate, QuitWhenDone;
        public int Index;
        public BenchStage Stage = BenchStage.World;
        public double StageTime, StableTime;
        public ulong LastTicks;
        public readonly List<float> Frame = new(), Gpu = new(), Cpu = new(), Script = new(), Commit = new();
        public double DrawCalls, Primitives, Objects, MaxDrawCalls, Compiles;
        public int Samples;
        public double Distance;
        public JsonObject? Current;
        public List<BenchAblation> Ablations = new();
        public int AblationIndex;
        public bool AblationMeasuring;
        public double BaseFrame, BaseGpu;
        public readonly JsonArray Results = new();
        public readonly DateTime Started = DateTime.Now;
        public string Status = "";
    }

    private BenchRun? _bench;
    private string? _benchWorldKey;
    private Label _benchLabel = null!;
    private const double BenchWarmup = 3, BenchAblateSeconds = 4;

    private static JsonObject BenchConfig() =>
        JsonNode.Parse(Godot.FileAccess.GetFileAsString("res://Content/benchmarks.json"))!.AsObject();

    private static string[] BenchSuites() => BenchConfig()["suites"]!.AsObject().Select(p => p.Key).ToArray();

    /// <summary>Start a benchmark run (suite and profile ids from benchmarks.json; profile "current" keeps the player's settings).</summary>
    private void BenchStart(string suite, string profile, string? outDir = null, bool quitWhenDone = false)
    {
        var config = BenchConfig();
        var suiteNode = config["suites"]?[suite]?.AsObject() ?? throw new ArgumentException($"no benchmark suite '{suite}'");
        if (profile != "current" && config["profiles"]?[profile] == null) throw new ArgumentException($"no benchmark profile '{profile}'");
        var scenes = config["scenarios"]!.AsArray().Select(n => ParseScene(n!.AsObject())).ToDictionary(s => s.Id);
        var variants = suiteNode["variants"]?.AsArray().Select(v => v!.AsObject().ToDictionary(p => p.Key, p => p.Value!.GetValue<double>())).ToList()
            ?? new List<Dictionary<string, double>> { new() };
        var jobs = new List<BenchJob>();
        foreach (var id in suiteNode["scenarios"]!.AsArray().Select(n => n!.GetValue<string>()))
            foreach (var v in variants)
                jobs.Add(new BenchJob(scenes[id], v, v.Count == 0 ? id : id + " " + string.Join(" ", v.Select(p => $"{p.Key}={p.Value.ToString(CultureInfo.InvariantCulture)}"))));
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        outDir ??= Path.Combine(OS.GetUserDataDir(), "bench", $"{stamp}-{suite}-{profile}");
        Directory.CreateDirectory(outDir);
        _bench = new BenchRun
        {
            Config = config, Suite = suite, Profile = profile, OutDir = outDir, Jobs = jobs, QuitWhenDone = quitWhenDone,
            Ablate = suiteNode["ablate"]?.GetValue<bool>() ?? false,
            Saved = GameSettings.All.ToDictionary(s => s.Key, s => s.Value),
        };
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        _menu.Visible = false;
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _benchLabel.Visible = true;
        GD.Print($"BENCH start suite={suite} profile={profile} jobs={jobs.Count} out={outDir}");
    }

    private static BenchScenario ParseScene(JsonObject o)
    {
        double[]? D(string k) => o[k]?.AsArray().Select(n => n!.GetValue<double>()).ToArray();
        var view = D("view");
        return new BenchScenario(o["id"]!.GetValue<string>(), o["name"]?.GetValue<string>() ?? "", D("at"), o["landmark"]?.GetValue<string>(),
            view?.Select(v => (float)v).ToArray(), o["height"]?.GetValue<double>() ?? 0, o["hour"]?.GetValue<double>() ?? 12,
            o["seconds"]?.GetValue<double>() ?? 15,
            o["path"]?.AsArray().Select(p => p!.AsArray().Select(n => n!.GetValue<double>()).ToArray()).ToList(),
            o["altitude"]?.GetValue<double>() ?? 60, o["speed"]?.GetValue<double>() ?? 60,
            (float)(o["pitch"]?.GetValue<double>() ?? -.1), (float)(o["zoom"]?.GetValue<double>() ?? 8));
    }

    private void BenchAbort(string why)
    {
        if (_bench == null) return;
        _bench.Status = "aborted: " + why;
        BenchFinish();
    }

    /// <summary>Set every setting for the current job: profile (from defaults) or the player's own, then the frozen
    /// test conditions, then the job's variant.</summary>
    private void BenchApplySettings(BenchRun run, BenchJob job)
    {
        var values = new Dictionary<string, double>(run.Saved);
        if (run.Profile != "current")
        {
            foreach (var s in GameSettings.All) if (s.Section != "Audio") values[s.Key] = s.Default;
            foreach (var (k, v) in run.Config["profiles"]![run.Profile]!["settings"]!.AsObject()) values[k] = v!.GetValue<double>();
        }
        foreach (var (k, v) in run.Config["frozen"]!.AsObject()) values[k] = v!.GetValue<double>();
        foreach (var (k, v) in job.Variant) values[k] = v;
        foreach (var s in GameSettings.All) if (values.TryGetValue(s.Key, out double v)) s.Value = v;
    }

    private bool BenchSettledNow => _streamer.Settled && !_holding && !(_horizon?.Busy ?? false) && _generation == null;

    /// <summary>One step of the run, every frame (end of _Process).</summary>
    private void BenchStep(double delta)
    {
        var run = _bench!;
        ulong now = Time.GetTicksUsec();
        float frameMs = run.LastTicks == 0 ? (float)(delta * 1000) : (now - run.LastTicks) / 1000f;
        run.LastTicks = now;
        run.StageTime += frameMs / 1000.0;
        if (run.Index >= run.Jobs.Count) { BenchFinish(); return; }
        var job = run.Jobs[run.Index];
        var scene = job.Scene;
        _benchLabel.Text = $"Benchmark {run.Index + 1}/{run.Jobs.Count}: {scene.Name}{(job.Variant.Count > 0 ? " · " + string.Join(", ", job.Variant.Select(p => $"{p.Key} {p.Value}")) : "")}\n" +
                           $"{run.Stage}{(run.Stage == BenchStage.Ablate && run.AblationIndex < run.Ablations.Count ? ": without " + run.Ablations[run.AblationIndex].Name : "")} · Esc stops";
        switch (run.Stage)
        {
            case BenchStage.World:
            {
                var world = run.Config["world"]!.AsObject();
                string key = world.ToJsonString();
                if (_benchWorldKey == key && _sampler != null && _generation == null) { BenchApplySettings(run, job); Next(BenchStage.Place); break; }
                if (_generation != null) break;
                if (_benchWorldKey != key)
                {
                    _benchWorldKey = key;
                    BenchApplySettings(run, job);
                    int index = Math.Max(0, _recipes.ToList().FindIndex(r => r.Id == world["preset"]!.GetValue<string>()));
                    _profileList.Select(index);
                    SelectType(index);
                    foreach (var (k, v) in world["gen"]!.AsObject())
                        if (_gen.All.FirstOrDefault(g => g.Key == k) is { } g) g.Value = v!.GetValue<double>();
                    _seedEdit.Text = world["seed"]!.GetValue<int>().ToString(CultureInfo.InvariantCulture);
                    Generate();
                    if (_generation == null) { run.Status = "the benchmark world could not be generated: " + _status; BenchFinish(); }
                }
                break;
            }
            case BenchStage.Place:
            {
                double x, z;
                if (scene.Path is { Count: > 1 } path) { x = path[0][0]; z = path[0][1]; }
                else if (scene.Landmark != null && _landmarks.FirstOrDefault(l => l.Name.StartsWith(scene.Landmark)) is { Name: not null } l) { x = l.X; z = l.Z; }
                else if (scene.At != null) { x = scene.At[0]; z = scene.At[1]; }
                else { x = PlayerX; z = PlayerZ; }
                Teleport(x, z);
                SetFly(true);
                if (scene.Path is { Count: > 1 } p2) { _yaw = Mathf.Atan2(-(float)(p2[1][0] - p2[0][0]), -(float)(p2[1][1] - p2[0][1])); _pitch = scene.Pitch; _zoom = scene.Zoom; }
                else if (scene.View is { Length: 3 } v) { _yaw = v[0]; _pitch = v[1]; _zoom = v[2]; }
                _arm.SpringLength = _zoom;
                _pendingHour = scene.Hour;
                run.Distance = 0;
                Next(BenchStage.Settle);
                break;
            }
            case BenchStage.Settle:
            {
                if (!_holding)
                {
                    // Hold the player at the scene's height above the ground or water.
                    var t = _sampler!.Sample(PlayerX, PlayerZ);
                    double ground = double.IsNaN(t.WaterLevel) ? t.Height : Math.Max(t.Height, t.WaterLevel);
                    double h = scene.Path != null ? scene.Altitude : scene.Height;
                    _player.Position = new Vector3(_player.Position.X, (float)(ground + h + .05), _player.Position.Z);
                    _player.Velocity = Vector3.Zero;
                }
                run.StableTime = BenchSettledNow ? run.StableTime + frameMs / 1000.0 : 0;
                if (run.StableTime > 2) Next(BenchStage.Warmup);
                else if (run.StageTime > 900) { run.Status = $"terrain never settled in '{scene.Id}'"; BenchFinish(); }
                break;
            }
            case BenchStage.Warmup:
                // Wait until shaders stop compiling (each new material compiles once; that stall is not the scene's cost).
                if (BenchCompileDelta() > 0) run.StableTime = 0; else run.StableTime += frameMs / 1000.0;
                if ((run.StageTime > BenchWarmup && run.StableTime > 1) || run.StageTime > 30) { BenchBeginMeasure(run); Next(BenchStage.Measure); }
                break;
            case BenchStage.Measure:
            {
                BenchRecord(run, frameMs);
                bool done;
                if (scene.Path is { Count: > 1 } path)
                {
                    run.Distance += scene.Speed * frameMs / 1000.0;
                    done = !BenchFly(path, run.Distance, scene.Altitude);
                }
                else done = run.StageTime >= scene.Seconds;
                if (done)
                {
                    run.Current = BenchSummarise(run, job);
                    run.BaseFrame = run.Frame.Average(); run.BaseGpu = run.Gpu.Average();
                    File.WriteAllText(Path.Combine(run.OutDir, $"frames-{run.Index + 1:00}-{scene.Id}.csv"), BenchCsv(run));
                    run.Ablations = run.Ablate && scene.Path == null ? BenchAblations() : new List<BenchAblation>();
                    run.AblationIndex = 0; run.AblationMeasuring = false;
                    if (run.Ablations.Count > 0) { BenchApplyAblation(run.Ablations[0], true); Next(BenchStage.Ablate); }
                    else Next(BenchStage.Done);
                }
                break;
            }
            case BenchStage.Ablate:
            {
                var a = run.Ablations[run.AblationIndex];
                if (!run.AblationMeasuring)
                {
                    if (run.StageTime > 1.2 && BenchSettledNow) { run.AblationMeasuring = true; run.StageTime = 0; BenchBeginMeasure(run); }
                    break;
                }
                BenchRecord(run, frameMs);
                if (run.StageTime < BenchAblateSeconds) break;
                double f = run.Frame.Average(), g = run.Gpu.Average();
                (run.Current!["ablation"] as JsonArray ?? (JsonArray)(run.Current["ablation"] = new JsonArray())).Add(new JsonObject
                {
                    ["without"] = a.Name, ["frame_ms"] = R(f), ["gpu_ms"] = R(g),
                    ["saves_frame_ms"] = R(run.BaseFrame - f), ["saves_gpu_ms"] = R(run.BaseGpu - g),
                    ["fps"] = R(1000 / Math.Max(.01, f)),
                });
                BenchApplyAblation(a, false);
                run.AblationIndex++; run.AblationMeasuring = false;
                if (run.AblationIndex < run.Ablations.Count) { BenchApplyAblation(run.Ablations[run.AblationIndex], true); run.StageTime = 0; }
                else Next(BenchStage.Done);
                break;
            }
            case BenchStage.Done:
                run.Results.Add(run.Current);
                GD.Print($"BENCH {job.Label}: {run.Current!["fps_avg"]} fps avg, 1% low {run.Current["fps_1pct_low"]}, gpu {run.Current["gpu_ms"]!["avg"]} ms");
                run.Index++;
                if (run.Index < run.Jobs.Count) { BenchApplySettings(run, run.Jobs[run.Index]); Next(BenchStage.World); }
                else BenchFinish();
                break;
        }

        void Next(BenchStage stage) { run.Stage = stage; run.StageTime = 0; run.StableTime = 0; }
    }

    /// <summary>Move along the flight path; false once past its end.</summary>
    private bool BenchFly(List<double[]> path, double distance, double altitude)
    {
        for (int i = 0; i + 1 < path.Count; i++)
        {
            double dx = path[i + 1][0] - path[i][0], dz = path[i + 1][1] - path[i][1], length = Math.Sqrt(dx * dx + dz * dz);
            if (distance > length) { distance -= length; continue; }
            double x = path[i][0] + dx * distance / length, z = path[i][1] + dz * distance / length;
            var t = _sampler!.Sample(x, z);
            double ground = double.IsNaN(t.WaterLevel) ? t.Height : Math.Max(t.Height, t.WaterLevel);
            _player.Position = new Vector3((float)(x - _originX), (float)(ground + altitude), (float)(z - _originZ));
            _player.Velocity = Vector3.Zero;
            _yaw = Mathf.LerpAngle(_yaw, Mathf.Atan2(-(float)dx, -(float)dz), .05f);
            return true;
        }
        return false;
    }

    private double _benchCompiles = -1;

    /// <summary>Pipeline compilations since the last call (Godot's monitors count them in total since start).</summary>
    private double BenchCompileDelta()
    {
        double total = BenchCompilesTotal();
        double delta = _benchCompiles < 0 ? 0 : Math.Max(0, total - _benchCompiles);
        _benchCompiles = total;
        return delta;
    }

    private static double BenchCompilesTotal() =>
        Performance.GetMonitor(Performance.Monitor.PipelineCompilationsCanvas) + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsMesh) +
        Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSurface) + Performance.GetMonitor(Performance.Monitor.PipelineCompilationsDraw) +
        Performance.GetMonitor(Performance.Monitor.PipelineCompilationsSpecialization);

    private void BenchBeginMeasure(BenchRun run)
    {
        run.Frame.Clear(); run.Gpu.Clear(); run.Cpu.Clear(); run.Script.Clear(); run.Commit.Clear();
        run.DrawCalls = run.Primitives = run.Objects = run.MaxDrawCalls = run.Compiles = 0; run.Samples = 0;
        _streamer.ResetCommitStats();
    }

    private void BenchRecord(BenchRun run, float frameMs)
    {
        var vp = GetViewport().GetViewportRid();
        run.Frame.Add(frameMs);
        run.Gpu.Add((float)RenderingServer.ViewportGetMeasuredRenderTimeGpu(vp));
        run.Cpu.Add((float)(RenderingServer.ViewportGetMeasuredRenderTimeCpu(vp) + RenderingServer.GetFrameSetupTimeCpu()));
        run.Script.Add((float)(Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000));
        run.Commit.Add((float)_streamer.FrameCommitMs);
        double draws = RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame);
        run.DrawCalls += draws; run.MaxDrawCalls = Math.Max(run.MaxDrawCalls, draws);
        run.Primitives += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame);
        run.Objects += RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame);
        run.Compiles += BenchCompileDelta();
        run.Samples++;
    }

    private static double R(double v) => Math.Round(v, 2);

    private static double Pct(List<float> values, double p)
    {
        if (values.Count == 0) return 0;
        var sorted = values.OrderBy(v => v).ToList();
        return sorted[Math.Clamp((int)Math.Ceiling(p / 100 * sorted.Count) - 1, 0, sorted.Count - 1)];
    }

    /// <summary>Average FPS of the slowest p % of frames ("1 % low").</summary>
    private static double Low(List<float> frames, double p)
    {
        if (frames.Count == 0) return 0;
        var worst = frames.OrderByDescending(v => v).Take(Math.Max(1, (int)(frames.Count * p / 100))).ToList();
        return 1000 / worst.Average();
    }

    private static JsonObject Spread(List<float> v) => new()
    {
        ["avg"] = R(v.Count > 0 ? v.Average() : 0), ["p50"] = R(Pct(v, 50)), ["p95"] = R(Pct(v, 95)), ["p99"] = R(Pct(v, 99)),
        ["max"] = R(v.Count > 0 ? v.Max() : 0),
    };

    private JsonObject BenchSummarise(BenchRun run, BenchJob job)
    {
        double seconds = run.Frame.Sum() / 1000;
        int n = Math.Max(1, run.Samples);
        var settings = new JsonObject();
        foreach (var s in GameSettings.All.Where(s => s.Section != "Audio")) settings[s.Key] = s.Value;
        return new JsonObject
        {
            ["id"] = job.Scene.Id, ["name"] = job.Scene.Name, ["label"] = job.Label,
            ["variant"] = new JsonObject(job.Variant.Select(p => KeyValuePair.Create(p.Key, (JsonNode?)JsonValue.Create(p.Value)))),
            ["frames"] = run.Frame.Count, ["seconds"] = R(seconds),
            ["fps_avg"] = R(run.Frame.Count / Math.Max(.001, seconds)),
            ["fps_1pct_low"] = R(Low(run.Frame, 1)), ["fps_01pct_low"] = R(Low(run.Frame, .1)),
            ["frame_ms"] = Spread(run.Frame), ["gpu_ms"] = Spread(run.Gpu), ["cpu_render_ms"] = Spread(run.Cpu),
            ["script_ms"] = Spread(run.Script), ["tile_commit_ms"] = Spread(run.Commit),
            ["hitches"] = new JsonObject
            {
                ["over_33ms"] = run.Frame.Count(f => f > 33.3f), ["over_50ms"] = run.Frame.Count(f => f > 50),
                ["over_100ms"] = run.Frame.Count(f => f > 100), ["worst_tile_commit_ms"] = R(_streamer.MaxCommitMs),
            },
            ["draw_calls_avg"] = R(run.DrawCalls / n), ["draw_calls_max"] = run.MaxDrawCalls,
            ["primitives_avg"] = R(run.Primitives / n), ["objects_avg"] = R(run.Objects / n),
            ["shader_compiles"] = run.Compiles,
            ["memory_mb"] = new JsonObject
            {
                ["video"] = R(RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.VideoMemUsed) / 1048576.0),
                ["textures"] = R(RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TextureMemUsed) / 1048576.0),
                ["buffers"] = R(RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.BufferMemUsed) / 1048576.0),
                ["static"] = R(Performance.GetMonitor(Performance.Monitor.MemoryStatic) / 1048576.0),
            },
            ["terrain"] = new JsonObject { ["tiles"] = _streamer.Resident, ["triangles"] = _streamer.Triangles, ["levels"] = _streamer.Levels },
            ["content"] = BenchContent(),
            ["position"] = new JsonArray(R(PlayerX), R(PlayerZ)),
            ["settings"] = settings,
        };
    }

    private readonly Dictionary<ulong, long> _benchMeshTriangles = new();

    private long MeshTriangles(Mesh? mesh)
    {
        if (mesh == null) return 0;
        ulong id = mesh.GetRid().Id;
        if (_benchMeshTriangles.TryGetValue(id, out long t)) return t;
        long count = 0;
        if (mesh is ArrayMesh am)
            for (int s = 0; s < am.GetSurfaceCount(); s++)
            {
                int idx = am.SurfaceGetArrayIndexLen(s);
                count += (idx > 0 ? idx : am.SurfaceGetArrayLen(s)) / 3;
            }
        return _benchMeshTriangles[id] = count;
    }

    /// <summary>What is loaded, per kind and tile level: MultiMesh nodes, instances, instance triangles (if all drawn),
    /// shadow-casting nodes; trees and tree cards per level.</summary>
    private JsonObject BenchContent()
    {
        var kinds = new SortedDictionary<string, (long Nodes, long Instances, long Triangles, long Shadow)>();
        var trees = new SortedDictionary<int, (long Trees, long Impostors, long Cards)>();
        foreach (var child in _streamer.GetChildren().Concat(_streamer.Field.GetChildren()))
        {
            // Terrain tiles "T<level>_x_z"; tree field cells "C<level>_x_z" (level 1: 128 m near cells, 4: 1 km far cells).
            if (child is not Node3D tile || !(tile.Name.ToString().StartsWith("T") || tile.Name.ToString().StartsWith("C"))) continue;
            bool cell = tile.Name.ToString().StartsWith("C");
            int level = (int.TryParse(tile.Name.ToString().Split('_')[0][1..], out int lv) ? lv : -1) + (cell ? 100 : 0);
            var tc = trees.GetValueOrDefault(level);
            long Meta(string k) => tile.HasMeta(k) ? tile.GetMeta(k).AsInt64() : 0;
            trees[level] = (tc.Trees + Meta("trees"), tc.Impostors + Meta("impostors"), tc.Cards + Meta("cards"));
            foreach (var node in tile.GetChildren())
            {
                if (node is not MultiMeshInstance3D m || m.Multimesh == null) continue;
                string kind = m.Layers switch
                {
                    RenderLayers.TreesNear => "trees_near", RenderLayers.TreesFar => "trees_far", RenderLayers.TreeCards => "tree_cards",
                    RenderLayers.Plants => "plants", RenderLayers.Rocks => "rocks_props", RenderLayers.TreeImpostors => "tree_impostors", _ => "other",
                };
                string key = level >= 100 ? $"{kind}/cell{level - 100}" : $"{kind}/L{level}";
                var v = kinds.GetValueOrDefault(key);
                long inst = m.Multimesh.InstanceCount;
                kinds[key] = (v.Nodes + 1, v.Instances + inst, v.Triangles + inst * MeshTriangles(m.Multimesh.Mesh),
                    v.Shadow + (m.CastShadow != GeometryInstance3D.ShadowCastingSetting.Off ? 1 : 0));
            }
        }
        var o = new JsonObject();
        var byKind = new JsonObject();
        foreach (var (k, v) in kinds)
            byKind[k] = new JsonObject { ["nodes"] = v.Nodes, ["instances"] = v.Instances, ["triangles"] = v.Triangles, ["shadow_nodes"] = v.Shadow };
        o["multimesh"] = byKind;
        var t = new JsonObject();
        foreach (var (level, v) in trees) t[level >= 100 ? $"cell{level - 100}" : $"L{level}"] = new JsonObject { ["trees_3d"] = v.Trees, ["tree_impostors"] = v.Impostors, ["tree_cards"] = v.Cards };
        o["trees_per_level"] = t;
        return o;
    }

    private string BenchCsv(BenchRun run)
    {
        var sb = new StringBuilder("frame,frame_ms,gpu_ms,cpu_render_ms,script_ms,tile_commit_ms\n");
        for (int i = 0; i < run.Frame.Count; i++)
            sb.Append(CultureInfo.InvariantCulture, $"{i},{run.Frame[i]:0.###},{run.Gpu[i]:0.###},{run.Cpu[i]:0.###},{run.Script[i]:0.###},{run.Commit[i]:0.###}\n");
        return sb.ToString();
    }

    /// <summary>Things to take away one at a time: object kinds (render layers, instant) and effects that need no
    /// terrain rebuild, where they are on.</summary>
    private const uint AllObjects = RenderLayers.TreesNear | RenderLayers.TreesFar | RenderLayers.TreeImpostors | RenderLayers.TreeCards |
                                    RenderLayers.Plants | RenderLayers.Rocks | RenderLayers.Objects;

    private List<BenchAblation> BenchAblations()
    {
        var list = new List<BenchAblation>
        {
            new("far 3D trees (LOD2)", RenderLayers.TreesFar, null, 0),
            new("all 3D trees", RenderLayers.TreesNear | RenderLayers.TreesFar, null, 0),
            new("tree impostors", RenderLayers.TreeImpostors, null, 0),
            new("tree cards", RenderLayers.TreeCards, null, 0),
            new("grass and plants", RenderLayers.Plants, null, 0),
            new("rocks, bushes and logs", RenderLayers.Rocks | RenderLayers.Objects, null, 0),
            new("all vegetation and objects", AllObjects, null, 0),
            new("tree shadows", 0, null, 0, NoShadowLayers: RenderLayers.TreesNear | RenderLayers.TreesFar),
            new("rock, bush and log shadows", 0, null, 0, NoShadowLayers: RenderLayers.Rocks | RenderLayers.Objects),
            new("grass and plant shadows", 0, null, 0, NoShadowLayers: RenderLayers.Plants),
            new("terrain shadows", 0, null, 0, TerrainShadows: true),
        };
        if (GameSettings.Shadows.Index >= 3) list.Add(new("soft shadow filter (low instead of high)", 0, null, 0, Special: "soft-low"));
        if (GameSettings.Shadows.Index >= 2) list.Add(new("shadow distance (90 m instead of the setting's)", 0, null, 0, Special: "shadow-90"));
        (string Name, string Key, double Off)[] effects =
        {
            ("all shadows", "fx.shadows", 0), ("volumetric clouds (flat clouds instead)", "sky.cloud_quality", 0), ("volumetric fog", "fx.volumetric_fog", 0),
            ("screen-space reflections", "fx.ssr", 0), ("water reflections (low)", "fx.water_reflections", 0), ("SSAO", "fx.ssao", 0),
            ("SSIL", "fx.ssil", 0), ("sun shafts", "fx.sun_shafts", 0), ("light beams", "fx.light_beams", 0), ("ground fog", "fx.ground_fog", 0),
            ("anti-aliasing", "perf.aa", 0),
        };
        foreach (var (name, key, off) in effects)
            if (GameSettings.All.FirstOrDefault(s => s.Key == key) is { Rebuild: false } s && s.Value != off) list.Add(new(name, 0, key, off));
        return list;
    }

    private readonly Dictionary<string, double> _benchAblationRestore = new();
    private readonly List<(GeometryInstance3D Node, bool Visible, GeometryInstance3D.ShadowCastingSetting Shadow)> _benchNodeRestore = new();
    private float _benchShadowDistance = -1;

    /// <summary>Every drawn object of the terrain tiles and the tree field.</summary>
    private IEnumerable<GeometryInstance3D> BenchGeometry()
    {
        foreach (var root in _streamer.GetChildren().Concat(_streamer.Field.GetChildren()))
            if (root is Node3D n)
                foreach (var child in n.GetChildren())
                    if (child is GeometryInstance3D g) yield return g;
    }

    private void BenchApplyAblation(BenchAblation a, bool apply)
    {
        if (!apply)
        {
            foreach (var (node, visible, shadow) in _benchNodeRestore)
                if (GodotObject.IsInstanceValid(node)) { node.Visible = visible; node.CastShadow = shadow; }
            _benchNodeRestore.Clear();
        }
        else if (a.HideLayers != 0 || a.NoShadowLayers != 0 || a.TerrainShadows)
            foreach (var g in BenchGeometry())
            {
                bool hide = (g.Layers & a.HideLayers) != 0;
                bool noShadow = (g.Layers & a.NoShadowLayers) != 0 || (a.TerrainShadows && g.Layers == RenderLayers.Terrain && g is MeshInstance3D);
                if (!hide && !noShadow) continue;
                _benchNodeRestore.Add((g, g.Visible, g.CastShadow));
                if (hide) g.Visible = false;
                if (noShadow) g.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
            }
        switch (a.Special)
        {
            case "soft-low":
                RenderingServer.DirectionalSoftShadowFilterSetQuality(apply ? RenderingServer.ShadowQuality.SoftLow : RenderingServer.ShadowQuality.SoftHigh);
                break;
            case "shadow-90":
                if (apply) { _benchShadowDistance = _atmosphere.Sun.DirectionalShadowMaxDistance; _atmosphere.Sun.DirectionalShadowMaxDistance = 90; }
                else if (_benchShadowDistance > 0) { _atmosphere.Sun.DirectionalShadowMaxDistance = _benchShadowDistance; _benchShadowDistance = -1; }
                break;
        }
        if (a.Key != null && GameSettings.All.FirstOrDefault(s => s.Key == a.Key) is { } s)
        {
            if (apply) { _benchAblationRestore[a.Key] = s.Value; s.Value = a.Value; }
            else if (_benchAblationRestore.Remove(a.Key, out double v)) s.Value = v;
        }
    }

    private static JsonObject BenchMachine() => new()
    {
        ["os"] = $"{OS.GetName()} {OS.GetVersion()}", ["cpu"] = OS.GetProcessorName(), ["cpu_threads"] = OS.GetProcessorCount(),
        ["ram_gb"] = R(OS.GetMemoryInfo()["physical"].AsInt64() / 1073741824.0),
        ["gpu"] = RenderingServer.GetVideoAdapterName(), ["gpu_vendor"] = RenderingServer.GetVideoAdapterVendor(),
        ["gpu_type"] = RenderingServer.GetVideoAdapterType().ToString(), ["graphics_api"] = RenderingServer.GetVideoAdapterApiVersion(),
        ["driver"] = string.Join(" ", OS.GetVideoAdapterDriverInfo()),
        ["rendering"] = $"{RenderingServer.GetCurrentRenderingDriverName()} / {RenderingServer.GetCurrentRenderingMethod()}",
        ["screen"] = $"{DisplayServer.ScreenGetSize().X}x{DisplayServer.ScreenGetSize().Y} @ {DisplaySettings.Hz(DisplayServer.WindowGetCurrentScreen())}",
        ["window"] = $"{DisplayServer.WindowGetSize().X}x{DisplayServer.WindowGetSize().Y} ({DisplayServer.WindowGetMode()})",
    };

    private void BenchFinish()
    {
        var run = _bench!;
        _bench = null;
        foreach (var (node, visible, shadow) in _benchNodeRestore)
            if (GodotObject.IsInstanceValid(node)) { node.Visible = visible; node.CastShadow = shadow; }
        _benchNodeRestore.Clear();
        foreach (var (k, v) in _benchAblationRestore) if (GameSettings.All.FirstOrDefault(s => s.Key == k) is { } s) s.Value = v;
        _benchAblationRestore.Clear();
        foreach (var s in GameSettings.All) if (run.Saved.TryGetValue(s.Key, out double v)) s.Value = v;
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), false);
        _benchLabel.Visible = false;
        var fps = run.Results.Select(r => r!["fps_avg"]!.GetValue<double>()).Where(f => f > 0).ToList();
        var json = new JsonObject
        {
            ["schema"] = 1,
            ["game"] = new JsonObject
            {
                ["version"] = ProjectSettings.GetSetting("application/config/version", "?").AsString(),
                ["godot"] = Engine.GetVersionInfo()["string"].AsString(),
            },
            ["suite"] = run.Suite, ["profile"] = run.Profile, ["started"] = run.Started.ToString("s", CultureInfo.InvariantCulture),
            ["minutes"] = R((DateTime.Now - run.Started).TotalMinutes), ["status"] = run.Status.Length > 0 ? run.Status : "complete",
            ["machine"] = BenchMachine(),
            ["overall_fps_geomean"] = fps.Count > 0 ? R(Math.Exp(fps.Average(Math.Log))) : 0,
            ["scenarios"] = run.Results.DeepClone(),
        };
        File.WriteAllText(Path.Combine(run.OutDir, "results.json"), json.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        File.WriteAllText(Path.Combine(run.OutDir, "summary.txt"), BenchText(json));
        GD.Print($"BENCH_DONE {json["status"]} geomean {json["overall_fps_geomean"]} fps → {run.OutDir}");
        _status = $"Benchmark {json["status"]}: {json["overall_fps_geomean"]} fps (geometric mean). Results in {run.OutDir}";
        if (run.QuitWhenDone) GetTree().Quit(run.Status.Length > 0 ? 1 : 0);
        else OS.ShellOpen(run.OutDir);
    }

    private static string BenchText(JsonObject j)
    {
        var sb = new StringBuilder();
        var m = j["machine"]!;
        sb.AppendLine($"Thornwake benchmark · suite {j["suite"]} · profile {j["profile"]} · {j["started"]} · {j["status"]}");
        sb.AppendLine($"Game {j["game"]!["version"]} · Godot {j["game"]!["godot"]}");
        sb.AppendLine($"{m["os"]} · {m["cpu"]} ({m["cpu_threads"]} threads) · {m["ram_gb"]} GB RAM");
        sb.AppendLine($"{m["gpu"]} ({m["gpu_vendor"]}, {m["graphics_api"]}, driver {m["driver"]}) · {m["rendering"]}");
        sb.AppendLine($"Screen {m["screen"]} · window {m["window"]}");
        sb.AppendLine($"Overall: {j["overall_fps_geomean"]} fps (geometric mean of the scenes)");
        sb.AppendLine();
        sb.AppendLine($"{"Scene",-44} {"FPS",7} {"1%low",7} {"frame p95",9} {"GPU avg",8} {"GPU p95",8} {"CPU rnd",8} {"draws",7} {"hitch>50",8}");
        foreach (var s in j["scenarios"]!.AsArray())
            sb.AppendLine($"{s!["label"],-44} {s["fps_avg"],7} {s["fps_1pct_low"],7} {s["frame_ms"]!["p95"],9} {s["gpu_ms"]!["avg"],8} {s["gpu_ms"]!["p95"],8} {s["cpu_render_ms"]!["avg"],8} {s["draw_calls_avg"],7} {s["hitches"]!["over_50ms"],8}");
        foreach (var s in j["scenarios"]!.AsArray())
        {
            if (s!["ablation"] is not JsonArray ab || ab.Count == 0) continue;
            sb.AppendLine();
            sb.AppendLine($"What things cost in '{s["label"]}' (frame {s["frame_ms"]!["avg"]} ms, GPU {s["gpu_ms"]!["avg"]} ms):");
            foreach (var a in ab) sb.AppendLine($"  without {a!["without"],-42} saves GPU {a["saves_gpu_ms"],7} ms, frame {a["saves_frame_ms"],7} ms → {a["fps"]} fps");
        }
        foreach (var s in j["scenarios"]!.AsArray())
        {
            sb.AppendLine();
            sb.AppendLine($"Loaded in '{s!["label"]}': terrain {s["terrain"]!["tiles"]} tiles, {s["terrain"]!["triangles"]} triangles");
            foreach (var (level, v) in s["content"]!["trees_per_level"]!.AsObject())
                if (v!["trees_3d"]!.GetValue<long>() > 0 || v["tree_impostors"]!.GetValue<long>() > 0 || v["tree_cards"]!.GetValue<long>() > 0)
                    sb.AppendLine($"  {level}: {v["trees_3d"]} 3D trees, {v["tree_impostors"]} impostors, {v["tree_cards"]} tree cards");
            foreach (var (k, v) in s["content"]!["multimesh"]!.AsObject())
                sb.AppendLine($"  {k,-16} {v!["nodes"],6} nodes {v["instances"],9} instances {v["triangles"],11} triangles {v["shadow_nodes"],5} casting shadows");
        }
        return sb.ToString();
    }
}
