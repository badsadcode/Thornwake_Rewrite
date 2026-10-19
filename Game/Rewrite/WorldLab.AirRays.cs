using System;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Rays in open air (M2, D148): the land around the player for AirRays.gdshader — ground heights over 20 km and, from
/// them, the sun-shadow height of every cell: the lowest height at which the air there sees the sun over every ridge
/// toward it. Built on a worker thread; heights again after moving 2.5 km, shadows again when the sun moves ~0.3°.
/// </summary>
public partial class WorldLab
{
    private const int AirN = 512; // (39 m cells: 256 gave one broad shadow per mountain, D148)
    private const float AirSize = 20000;
    private Task<(Image Map, double X, double Z, float[] Heights, string Log)>? _airJob;
    private double _airX = double.NaN, _airZ;
    private float[]? _airHeights;
    private Vector3 _airSun;
    private TerrainSampler? _airSampler;

    private readonly string? _airPreview = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--air-preview="))?[14..];

    /// <summary><c>--air-preview=out.png[,yaw offsets in degrees]</c> (developer tool, headless, with a dump's arguments and
    /// --set=fx.air_rays=1): the view rays' sunlit share of the haze, followed on the CPU through the same map as
    /// AirRays.gdshader (terrain only, no clouds), as a small picture per view: white = sunlit air, black = in a
    /// mountain's shadow, green tint = the ray ended on land (darker = farther). Finds where shafts can appear without
    /// opening a window; then quits.</summary>
    private void AirPreview(Image map, double mx, double mz)
    {
        var parts = _airPreview!.Split(',');
        var offsets = parts.Skip(1).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).DefaultIfEmpty(0).ToArray();
        const int W = 160, H = 90;
        double cell = AirSize / AirN;
        double sea = _sampler!.Plan.Profile.SeaLevel;
        float Read(double x, double z, int ch)
        {
            double u = (x - mx) / cell + AirN / 2.0 - .5, v = (z - mz) / cell + AirN / 2.0 - .5;
            if (u < 0 || v < 0 || u > AirN - 1 || v > AirN - 1) return (float)sea; // (outside: open air, as the shader)
            return map.GetPixel((int)Math.Round(u), (int)Math.Round(v))[ch];
        }
        var cam = _camera.GlobalPosition;
        double cx = cam.X + _originX, cz = cam.Z + _originZ, cy = cam.Y;
        double vfov = Mathf.DegToRad(GameSettings.Fov.F), aspect = 16.0 / 9;
        var strip = Image.CreateEmpty(W * offsets.Length, H, false, Image.Format.Rgb8);
        for (int o = 0; o < offsets.Length; o++)
        {
            double yaw = _yaw + Mathf.DegToRad((float)offsets[o]), pitch = _pitch;
            for (int py = 0; py < H; py++)
                for (int px = 0; px < W; px++)
                {
                    double sx = (2.0 * (px + .5) / W - 1) * Math.Tan(vfov / 2) * aspect, sy = (1 - 2.0 * (py + .5) / H) * Math.Tan(vfov / 2);
                    // camera basis: forward (−sin yaw cos pitch, sin pitch, −cos yaw cos pitch)
                    var f = new Vector3((float)(-Math.Sin(yaw) * Math.Cos(pitch)), (float)Math.Sin(pitch), (float)(-Math.Cos(yaw) * Math.Cos(pitch)));
                    var right = new Vector3((float)Math.Cos(yaw), 0, (float)-Math.Sin(yaw));
                    var up = right.Cross(f);
                    var d = (f + right * (float)sx + up * (float)sy).Normalized();
                    double lit = 0, all = 0, prev = 0, end = 30000; bool land = false;
                    for (int k = 1; k <= 200; k++)
                    {
                        double t = end * (k / 200.0) * (k / 200.0), dt = t - prev; prev = t;
                        double x = cx + d.X * t, y = cy + d.Y * t, z = cz + d.Z * t;
                        float ground = Read(x, z, 0), shadow = Read(x, z, 1);
                        if (y < ground && t > 200) { land = true; end = t; break; } // (the map's cells are 78 m: none nearer)
                        double haze = Math.Exp(-Math.Max(y - sea, 0) / 1400) * (1 + 3 * Math.Exp(-Math.Max(y - ground, 0) / 250)) * dt;
                        lit += (y > shadow ? 1 : 0) * haze; all += haze;
                    }
                    float share = (float)(lit / Math.Max(all, 1e-9));
                    float fade = land ? (float)Math.Clamp(1 - end / 30000, .3, 1) : 1;
                    strip.SetPixel(o * W + px, py, land ? new Color(share * .7f, share * .7f + .3f * fade, share * .7f) : new Color(share, share, share));
                }
        }
        string path = parts[0];
        strip.SavePng(path);
        GD.Print($"AIR_PREVIEW {path}: yaw {Mathf.RadToDeg(_yaw):0}° + {string.Join(",", offsets)}, pitch {Mathf.RadToDeg(_pitch):0.0}°, sun {Mathf.RadToDeg(Mathf.Asin(_atmosphere.SunDirection.Y)):0.0}° up");
        GetTree().Quit(0);
    }

    private readonly bool _airVantage = OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--ray-vantage"));
    // --ray-vantage=edge: only places where the land toward the sun stands within 1.5° of the sun (it peeks over a ridge)
    private readonly bool _airVantageEdge = OS.GetCmdlineUserArgs().Contains("--ray-vantage=edge");

    /// <summary><c>--ray-vantage</c> (developer tool, headless, with --at/--hour and --set=fx.air_rays=1): viewpoints within
    /// 6 km scored by how often the air in front of them toward the sun (±35°, 0.8–9 km, 50–700 m above the eye) changes
    /// between sunlit and a mountain's shadow — where shafts can show (Tom's reference 2: low, the sun behind peaks);
    /// then quits.</summary>
    private void AirVantage(Image map, double mx, double mz)
    {
        double cell = AirSize / AirN;
        float Read(double x, double z, int ch)
        {
            double u = (x - mx) / cell + AirN / 2.0 - .5, v = (z - mz) / cell + AirN / 2.0 - .5;
            if (u < 0 || v < 0 || u > AirN - 1 || v > AirN - 1) return float.NaN;
            return map.GetPixel((int)Math.Round(u), (int)Math.Round(v))[ch];
        }
        var sun = _atmosphere.SunDirection;
        double az = Math.Atan2(sun.X, -sun.Z);
        var found = new System.Collections.Generic.List<(int Score, double X, double Z, double Rise)>();
        for (double z = mz - 6000; z <= mz + 6000; z += 250)
            for (double x = mx - 6000; x <= mx + 6000; x += 250)
            {
                float g = Read(x, z, 0);
                if (float.IsNaN(g) || _sampler!.Sample(x, z).Water != WaterKind.None) continue;
                double around = 0; int n = 0;
                for (int k = 0; k < 8; k++) { float h = Read(x + Math.Cos(k * .785) * 2500, z + Math.Sin(k * .785) * 2500, 0); if (!float.IsNaN(h)) { around += h; n++; } }
                double rise = g - around / Math.Max(n, 1);
                if (_airVantageEdge)
                {
                    double horizon = -90, sx = Math.Sin(az), sz = -Math.Cos(az);
                    for (double d = 300; d <= 9500; d += 100)
                    {
                        float hh = Read(x + sx * d, z + sz * d, 0);
                        if (float.IsNaN(hh)) break;
                        horizon = Math.Max(horizon, Mathf.RadToDeg(Math.Atan2(hh - g - 2, d)));
                    }
                    if (Math.Abs(horizon - Mathf.RadToDeg(Mathf.Asin(sun.Y))) > 1.5) continue;
                }
                int score = 0;
                for (double a = -35; a <= 35; a += 5)
                {
                    double dirx = Math.Sin(az + a * Math.PI / 180), dirz = -Math.Cos(az + a * Math.PI / 180);
                    foreach (double dy in new[] { 50.0, 200, 400, 700 })
                    {
                        bool? last = null;
                        for (double d = 800; d <= 9000; d += 100)
                        {
                            float gg = Read(x + dirx * d, z + dirz * d, 0), ss = Read(x + dirx * d, z + dirz * d, 1);
                            if (float.IsNaN(gg)) break;
                            double y = g + 2 + dy;
                            if (y < gg) { last = null; continue; } // (inside a mountain: no air)
                            bool lit = y > ss;
                            if (last != null && last != lit) score++;
                            last = lit;
                        }
                    }
                }
                found.Add((score, x, z, rise));
            }
        double yaw = Math.Atan2(-sun.X, -sun.Z);
        foreach (var f in found.OrderByDescending(f => f.Score).Take(6))
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"RAY_VANTAGE --at={f.X:0},{f.Z:0} --view={yaw:0.000},-0.06,2.36 score {f.Score} ({f.Rise:0} m above its surroundings)"));
        GD.Print($"RAY_VANTAGE_DONE ({found.Count} sunlit raised places; sun {Mathf.RadToDeg(Mathf.Asin(sun.Y)):0.0}° up)");
        GetTree().Quit(0);
    }

    private Image? _airMap;
    private double _airMapX, _airMapZ;

    private void UpdateAirMap()
    {
        _atmosphere.TerrainSunAtCamera = 1;
        if (GameSettings.AirRays.Index == 0 || _sampler == null || _generation != null) return;
        if (_airMap != null)
        {
            // Is the camera in a mountain's shadow? (the near volumetric fog only knows the 450 m shadow map)
            var c = _camera.GlobalPosition;
            double u = (c.X + _originX - _airMapX) / (AirSize / AirN) + AirN / 2.0 - .5, v = (c.Z + _originZ - _airMapZ) / (AirSize / AirN) + AirN / 2.0 - .5;
            if (u >= 0 && v >= 0 && u <= AirN - 1 && v <= AirN - 1)
            {
                float shadow = _airMap.GetPixel((int)Math.Round(u), (int)Math.Round(v)).G;
                _atmosphere.TerrainSunAtCamera = Mathf.SmoothStep(shadow - 25, shadow + 25, c.Y);
            }
        }
        if (_airJob != null)
        {
            if (!_airJob.IsCompleted) return;
            var job = _airJob; _airJob = null;
            if (job.IsFaulted) { GD.PrintErr("AIR map failed: " + job.Exception?.GetBaseException().Message); return; }
            var r = job.Result;
            if (_airSampler != _sampler) { _airX = double.NaN; return; } // (a new world meanwhile)
            _airHeights = r.Heights;
            _atmosphere.SetAirMap(r.Map, r.X, r.Z, AirSize);
            _airMap = r.Map; _airMapX = r.X; _airMapZ = r.Z;
            GD.Print(r.Log);
            if (_airPreview != null && _shotAt is { } at && Math.Abs(PlayerX - at.X) + Math.Abs(PlayerZ - at.Z) < 2
                && Math.Abs(r.X - at.X) + Math.Abs(r.Z - at.Z) < 2) AirPreview(r.Map, r.X, r.Z);
            if (_airVantage && _shotAt is { } at2 && Math.Abs(r.X - at2.X) + Math.Abs(r.Z - at2.Z) < 2) AirVantage(r.Map, r.X, r.Z);
        }
        var sun = _atmosphere.SunDirection;
        bool moved = double.IsNaN(_airX) || _airSampler != _sampler || Math.Abs(PlayerX - _airX) + Math.Abs(PlayerZ - _airZ) > 2500;
        bool turned = sun.Dot(_airSun) < .99998f; // ~0.36°
        if (!moved && !turned) return;
        var sampler = _sampler;
        double cx = moved ? PlayerX : _airX, cz = moved ? PlayerZ : _airZ;
        var heights = moved ? null : _airHeights;
        _airX = cx; _airZ = cz; _airSun = sun; _airSampler = sampler;
        _airJob = Task.Run(() => BuildAirMap(sampler, cx, cz, heights, sun));
    }

    private static (Image, double, double, float[], string) BuildAirMap(TerrainSampler s, double cx, double cz, float[]? heights, Vector3 sun)
    {
        var watch = Stopwatch.StartNew();
        const int N = AirN;
        double cell = AirSize / N;
        if (heights == null)
        {
            heights = new float[N * N];
            Parallel.For(0, N, j =>
            {
                for (int i = 0; i < N; i++)
                {
                    // (the water's surface where there is water: the haze lies on it, and a sea floor's ridges are no land)
                    var t = s.Sample(cx + (i + .5 - N / 2.0) * cell, cz + (j + .5 - N / 2.0) * cell);
                    heights[j * N + i] = (float)(double.IsNaN(t.WaterLevel) ? t.Height : Math.Max(t.Height, t.WaterLevel));
                }
            });
        }
        double heightMs = watch.Elapsed.TotalMilliseconds;
        float top = float.MinValue;
        foreach (float h in heights) top = Math.Max(top, h);
        // Toward the sun across the map, a cell at a time: the shadow height is the highest of (land − drop of the sun
        // line over that distance); past the highest land nothing can rise above the line any more.
        var flat = new Vector2(sun.X, sun.Z);
        float run = flat.Length();
        Vector2 step = run > 1e-4f ? flat / run : Vector2.Zero;
        double tan = Math.Max(sun.Y, .005) / Math.Max(run, 1e-4);
        var data = new float[N * N * 2];
        Parallel.For(0, N, j =>
        {
            for (int i = 0; i < N; i++)
            {
                float h0 = heights[j * N + i], shadow = h0;
                for (int k = 1; k < 2 * N; k++)
                {
                    double drop = k * cell * tan;
                    if (top - drop <= shadow) break;
                    int x = (int)Math.Round(i + step.X * k), z = (int)Math.Round(j + step.Y * k);
                    if (x < 0 || z < 0 || x >= N || z >= N) break;
                    shadow = Math.Max(shadow, (float)(heights[z * N + x] - drop));
                }
                data[(j * N + i) * 2] = h0;
                data[(j * N + i) * 2 + 1] = shadow;
            }
        });
        var bytes = new byte[data.Length * 4];
        Buffer.BlockCopy(data, 0, bytes, 0, bytes.Length);
        var image = Image.CreateFromData(N, N, false, Image.Format.Rgf, bytes);
        int c = (N / 2) * N + N / 2, shaded = 0;
        for (int k = 0; k < N * N; k++) if (data[k * 2 + 1] > data[k * 2] + 1) shaded++;
        string log = $"AIR map {N}² over {AirSize / 1000:0} km: heights {heightMs:0} ms, shadows {watch.Elapsed.TotalMilliseconds - heightMs:0} ms (sun {Mathf.RadToDeg(Mathf.Asin(sun.Y)):0.0}° up); " +
                     $"centre ground {data[c * 2]:0} shadow {data[c * 2 + 1]:0}, top {top:0}, {100.0 * shaded / (N * N):0} % of cells in a mountain's shadow";
        return (image, cx, cz, heights, log);
    }
}
