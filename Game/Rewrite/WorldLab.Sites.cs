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
