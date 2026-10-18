using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// <c>--find-sites</c> (Atmosphere 2.0 M0): search the loaded world around the player for the nearest good place of
/// each kind used by the atmosphere capture scenes (dense temperate forest, swamp, taiga, desert, plains, a mountain
/// ridge, a coast), print them and quit. Developer tool; the results go into Content/benchmarks.json by hand.
/// </summary>
public partial class WorldLab
{
    private bool _findSitesPending = OS.GetCmdlineUserArgs().Contains("--find-sites");
    private bool _probeWaterPending = OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--probe-water="));

    /// <summary><c>--probe-water=x,z[,radius[,step]]</c>: the water kind and surface level on a grid around a point
    /// (developer tool: steps in the water surface, such as a river inside a lake at its own lower level).</summary>
    private void ProbeWater()
    {
        var v = OS.GetCmdlineUserArgs().First(a => a.StartsWith("--probe-water="))[14..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
        double cx = v[0], cz = v[1], r = v.Length > 2 ? v[2] : 60, step = v.Length > 3 ? v[3] : 10;
        for (double z = cz - r; z <= cz + r; z += step)
        {
            var line = new System.Text.StringBuilder($"{z - cz,5:0} ");
            for (double x = cx - r; x <= cx + r; x += step)
            {
                var t = _sampler!.Sample(x, z);
                string k = t.Water switch { WaterKind.Lake => "L", WaterKind.River => "R", WaterKind.Pond => "P", WaterKind.Ocean => "O", _ => "." };
                line.Append(t.Water == WaterKind.None ? $"   .{t.Height,6:0.0}" : $"   {k}{t.WaterLevel,6:0.0}");
            }
            GD.Print("PROBE " + line);
        }
        int lk = _sampler!.Plan.Hydrology.LakeDistance(cx, cz, out double cells);
        GD.Print($"PROBE revision {_sampler.Plan.Style.Revision}, nearest lake {lk} at {cells:0.00} cells" + (lk >= 0 ? $", level {_sampler.Plan.Hydrology.Lakes[lk].Level:0.0}, outlet reach {_sampler.Plan.Hydrology.Lakes[lk].OutletReach}" : ""));
        // Every lake: its level against its outlet river where the river leaves the lake (first point > 1.6 cells out).
        var hyd = _sampler.Plan.Hydrology;
        foreach (var lake in hyd.Lakes)
        {
            if (lake.OutletReach < 0) { GD.Print($"PROBE lake {lake.Id} level {lake.Level:0.0} area {lake.Area / 1e6:0.00} km² no outlet"); continue; }
            var reach = hyd.Reaches[lake.OutletReach];
            double inside = double.PositiveInfinity, exit = double.NaN;
            for (int k = 0; k < reach.Points.Length; k++)
            {
                int l = hyd.LakeDistance(reach.Points[k].X, reach.Points[k].Z, out double c);
                bool inLake = l == lake.Id && c < 1.6;
                if (inLake) inside = Math.Min(inside, reach.Surface[k]);
                else { exit = reach.Surface[k]; break; }
            }
            GD.Print($"PROBE lake {lake.Id} level {lake.Level:0.0} area {lake.Area / 1e6:0.00} km²: outlet lowest inside {inside:0.0} ({lake.Level - inside:0.0} m below), at exit {exit:0.0} ({lake.Level - exit:0.0} m below)");
        }
        if (v.Length > 4) { var t = _sampler.Sample(v[4], v[5]); GD.Print($"PROBE at {v[4]:0},{v[5]:0}: {t.Water} body {t.WaterBody} level {t.WaterLevel:0.0} ground {t.Height:0.0}"); }
        GetTree().Quit();
    }

    private bool _clearSpotPending = OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--clear-spot"));

    /// <summary><c>--clear-spot[=x,z]</c>: the nearest point (within 80 m of x, z or the player) with no tree within 9.5 m, so a camera
    /// circling 6 m behind the player stays out of the crowns, preferring dense forest around it; printed, then quit.</summary>
    private void ClearSpot()
    {
        var s = _sampler!;
        var config = DetailConfig.For(s);
        double px = PlayerX, pz = PlayerZ;
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--clear-spot="))?[13..].Split(',') is [var ax, var az])
        { px = double.Parse(ax, CultureInfo.InvariantCulture); pz = double.Parse(az, CultureInfo.InvariantCulture); }
        long tx = (long)Math.Floor(px / TileBuilder.BaseTile), tz = (long)Math.Floor(pz / TileBuilder.BaseTile);
        var trees = new List<Vector2>();
        int reach = (int)Math.Ceiling(110 / TileBuilder.BaseTile);
        for (long z = tz - reach; z <= tz + reach; z++)
            for (long x = tx - reach; x <= tx + reach; x++)
            {
                var key = new TileKey(0, x, z);
                foreach (var t in TileBuilder.Build(s, key, config).Trees)
                    trees.Add(new Vector2((float)(key.MinX + t.Transform.Origin.X), (float)(key.MinZ + t.Transform.Origin.Z)));
            }
        (double Score, double X, double Z, int Near)? best = null;
        for (double dz = -80; dz <= 80; dz += 2)
            for (double dx = -80; dx <= 80; dx += 2)
            {
                double x = px + dx, z = pz + dz;
                var t = s.Sample(x, z);
                if (t.Water != WaterKind.None) continue;
                var at = new Vector2((float)x, (float)z);
                float nearest = float.MaxValue; int around = 0;
                foreach (var tree in trees) { float d = tree.DistanceTo(at); nearest = Math.Min(nearest, d); if (d < 25) around++; }
                if (nearest < 9.5f) continue;
                double score = around - Math.Sqrt(dx * dx + dz * dz) * .15;
                if (best == null || score > best.Value.Score) best = (score, x, z, around);
            }
        GD.Print(best is { } b
            ? $"CLEAR_SPOT {b.X.ToString("0.0", CultureInfo.InvariantCulture)}, {b.Z.ToString("0.0", CultureInfo.InvariantCulture)} ({b.Near} trees within 25 m, {Math.Sqrt((b.X - px) * (b.X - px) + (b.Z - pz) * (b.Z - pz)):0} m away)"
            : "CLEAR_SPOT none within 80 m");
        GetTree().Quit();
    }

    private bool _raySpotPending = OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--ray-spot="));

    /// <summary><c>--ray-spot=x,z,azimuth[,elevation[,radius]]</c> (M2 rays): places within the radius (default 1500 m)
    /// in dense temperate forest on near-level ground, where the land toward the sun's azimuth (degrees from north,
    /// clockwise) stays below the sun's elevation (default 10°) for 1.5 km, so a low sun shines in through the trunks
    /// rather than from behind a hill; the best few printed, then quit.</summary>
    private void RaySpot()
    {
        var s = _sampler!;
        var v = OS.GetCmdlineUserArgs().First(a => a.StartsWith("--ray-spot="))[11..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
        double cx = v[0], cz = v[1], az = Mathf.DegToRad(v[2]), el = v.Length > 3 ? v[3] : 10, r = v.Length > 4 ? v[4] : 1500;
        double sx = Math.Sin(az), sz = -Math.Cos(az), horizon = Math.Tan(Mathf.DegToRad(el - 4));
        var config = DetailConfig.For(s);
        var found = new List<(double Score, double X, double Z, string Note)>();
        for (double z = cz - r; z <= cz + r; z += 50)
            for (double x = cx - r; x <= cx + r; x += 50)
            {
                var t = s.Sample(x, z);
                if (t.Water != WaterKind.None) continue;
                var (ba, _, share) = s.Plan.Biomes.Sample(x, z);
                if (share < .8 || s.Plan.Biomes.Pool[ba].Def.Id != "temperate-forest") continue;
                double forest = Vegetation.ForestDensity(s, x, z, t, config.ForestCover);
                if (forest < .7) continue;
                double rise = 0;
                foreach (var (ox, oz) in new[] { (30.0, 0.0), (-30.0, 0.0), (0.0, 30.0), (0.0, -30.0) })
                    rise = Math.Max(rise, Math.Abs(s.Sample(x + ox, z + oz).Height - t.Height) / 30);
                if (rise > .12) continue;
                double block = -1;
                for (double d = 60; d <= 1500; d += 30)
                    block = Math.Max(block, (s.Sample(x + sx * d, z + sz * d).Height - t.Height) / d);
                if (block > horizon) continue;
                double score = forest - rise * 2 - Math.Max(0, block) * 3;
                found.Add((score, x, z, string.Create(CultureInfo.InvariantCulture, $"forest {forest:0.00}, slope {rise:0.00}, land toward the sun {Mathf.RadToDeg(Math.Atan(block)):0.0}°")));
            }
        foreach (var f in found.OrderByDescending(f => f.Score).Take(8))
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"RAY_SPOT [{f.X:0.0}, {f.Z:0.0}] {f.Note}"));
        GD.Print($"RAY_SPOT_DONE ({found.Count} places)");
        GetTree().Quit(0);
    }

    private bool _sunBlockPending = OS.GetCmdlineUserArgs().Contains("--sun-block");

    /// <summary><c>--sun-block</c> (developer tool, with --at/--day/--hour): walk from the player toward the sun and print
    /// where the land first rises above the line to the sun (the sun hidden behind it), then quit (D147).</summary>
    private void SunBlock()
    {
        var s = _sampler!;
        var (px, pz) = _shotAt ?? (PlayerX, PlayerZ);
        double lat = s.Plan.Climate.Latitude(pz);
        var (el, az) = GameClock.Position(lat, GameClock.Declination(GameSettings.Season.Index), (_clock.Hour - 12) * 15);
        double sx = Math.Sin(Mathf.DegToRad(az)), sz = -Math.Cos(Mathf.DegToRad(az)), h0 = s.Sample(px, pz).Height + 1.7, tan = Math.Tan(Mathf.DegToRad(el));
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"SUN_BLOCK sun {el:0.0}° up, azimuth {az:0}°, eye {h0:0} m (hour {_clock.Hour:0.00}, latitude {lat:0.0})"));
        double highest = double.NegativeInfinity;
        for (double d = 20; d <= 30000; d += d < 2000 ? 20 : 100)
        {
            double above = s.Sample(px + sx * d, pz + sz * d).Height - (h0 + d * tan);
            double angle = Mathf.RadToDeg(Math.Atan2(s.Sample(px + sx * d, pz + sz * d).Height - h0, d));
            if (angle > highest) { highest = angle; if (above > 0) GD.Print(string.Create(CultureInfo.InvariantCulture, $"SUN_BLOCK land {above:0} m above the sun line at {d:0} m (horizon {angle:0.0}°)")); }
        }
        GD.Print(string.Create(CultureInfo.InvariantCulture, $"SUN_BLOCK_DONE highest horizon toward the sun {highest:0.0}° ({(highest > el ? "sun hidden" : "sun visible")})"));
        GetTree().Quit(0);
    }

    private void FindSites()
    {
        var s = _sampler!;
        var biomes = s.Plan.Biomes;
        double sea = s.Plan.Profile.SeaLevel;
        double px = PlayerX, pz = PlayerZ;
        var config = DetailConfig.For(s);
        var best = new Dictionary<string, (double D, double X, double Z, string Note)>();
        void Offer(string kind, double x, double z, string note)
        {
            double d = Math.Sqrt((x - px) * (x - px) + (z - pz) * (z - pz));
            if (!best.TryGetValue(kind, out var b) || d < b.D) best[kind] = (d, x, z, note);
        }
        for (double r = 0; r <= 150000; r += r < 30000 ? 250 : 1000)
        {
            double spacing = r < 30000 ? 250 : 1000;
            int n = Math.Max(1, (int)(2 * Math.PI * r / spacing));
            for (int i = 0; i < n; i++)
            {
                double a = i * 2 * Math.PI / n, x = px + Math.Cos(a) * r, z = pz + Math.Sin(a) * r;
                var t = s.Sample(x, z);
                if (t.Water != WaterKind.None) continue;
                var (ba, bb, share) = biomes.Sample(x, z);
                string id = biomes.Pool[ba].Def.Id;
                double forest = Vegetation.ForestDensity(s, x, z, t, config.ForestCover);
                double h = t.Height - sea;
                bool pure = share > .9;
                if (pure && id == "temperate-forest" && forest > .7) Offer("forest", x, z, $"forest {forest:0.00}");
                if (pure && (id is "swamp" or "marsh" or "bayou" or "fen")) Offer("swamp", x, z, id);
                if (pure && id == "taiga" && forest > .5) Offer("taiga", x, z, $"forest {forest:0.00}");
                if (pure && (id is "desert" or "badlands")) Offer("desert", x, z, id);
                if (pure && id == "temperate-grassland" && forest < .15) Offer("plains", x, z, $"forest {forest:0.00}");
                if (t.Mountain > .6 && h > 1200) Offer("ridge", x, z, $"{h:0} m");
                if (h is > 1 and < 6 && s.Sample(x + 60, z).Water == WaterKind.Ocean) Offer("coast", x, z, $"{h:0.0} m");
            }
            if (best.Count >= 7) break;
        }
        foreach (var (kind, b) in best.OrderBy(p => p.Key))
            GD.Print(string.Create(CultureInfo.InvariantCulture, $"SITE {kind}: [{b.X:0.0}, {b.Z:0.0}] {b.D / 1000:0.0} km away ({b.Note})"));
        GD.Print($"SITES_DONE ({best.Count} of 7 kinds within 150 km)");
        GetTree().Quit(0);
    }
}
