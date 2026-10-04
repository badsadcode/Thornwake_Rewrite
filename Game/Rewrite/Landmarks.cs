using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

public readonly record struct Landmark(string Name, double X, double Z);

/// <summary>Interesting places to start or teleport to, derived from the generated world (pure, deterministic).</summary>
public static class Landmarks
{
    public static List<Landmark> Find(TerrainSampler s)
    {
        var plan = s.Plan; var hy = plan.Hydrology; var result = new List<Landmark>();
        var home = plan.Layout.Land[0];

        // A medium river near the first continent's centre: the default start.
        var reach = hy.Reaches.Where(r => r.Catchment[^1] is > 150 and < 4000)
            .OrderBy(r => Dist(r.Points[r.Points.Length / 2], home.Center)).FirstOrDefault()
            ?? hy.Reaches.OrderBy(r => Dist(r.Points[0], home.Center)).FirstOrDefault();
        if (reach != null)
        {
            int k = Math.Clamp(reach.Points.Length / 2, 1, reach.Points.Length - 2);
            var a = reach.Points[k - 1]; var b = reach.Points[k + 1];
            double tx = b.X - a.X, tz = b.Z - a.Z, len = Math.Sqrt(tx * tx + tz * tz);
            double off = reach.Width[k] * .5 + 28;
            result.Add(Dry(s, "Riverside (start)", reach.Points[k].X - tz / len * off, reach.Points[k].Z + tx / len * off));
        }
        if (hy.Lakes.Count > 0)
        {
            var lake = hy.Lakes.OrderByDescending(l => l.Area).First();
            double x = lake.Center.X;
            for (int i = 0; i < 4000 && s.Sample(x, lake.Center.Z).Water == WaterKind.Lake; i++) x += 15;
            result.Add(Dry(s, $"Shore of the largest lake ({lake.Area / 1e6:0} km²)", x + 30, lake.Center.Z));
        }
        var mouth = hy.Reaches.Where(r => r.EndsInSea).OrderByDescending(r => r.Catchment[^1]).FirstOrDefault();
        if (mouth != null)
        {
            var p = mouth.Points[Math.Max(0, mouth.Points.Length - 12)];
            result.Add(Dry(s, $"Mouth of the greatest river ({mouth.Catchment[^1]:0} km²)", p.X + mouth.Width[^1], p.Z));
        }
        // Highest point of the macro raster.
        var height = plan.Macro.Channel(WorldPlan.HeightChannel);
        int best = 0;
        for (int i = 1; i < height.Length; i++) if (height[i] > height[best]) best = i;
        int n = plan.Macro.Size;
        result.Add(new Landmark($"Highest summit (~{height[best]:0} m)", plan.Macro.CellX(best % n), plan.Macro.CellZ(best / n)));
        // A foothill viewpoint below the highest range.
        result.Add(Dry(s, "Foothills under the main range", plan.Macro.CellX(best % n) + plan.Profile.MountainWidth * 1.5, plan.Macro.CellZ(best / n)));
        double cx = home.Center.X;
        for (int i = 0; i < 4000 && s.Sample(cx, home.Center.Z).Water != WaterKind.Ocean; i++) cx += 100;
        result.Add(Dry(s, "Sea coast", cx - 60, home.Center.Z));
        for (int i = 0; i < plan.Layout.Land.Count && i < plan.Profile.Continents; i++)
            result.Add(Dry(s, $"Continent {i + 1} interior", plan.Layout.Land[i].Center.X, plan.Layout.Land[i].Center.Z));
        return result;
    }

    /// <summary>Nearest dry, gently sloped ground to (x, z) on a widening spiral.</summary>
    public static Landmark Dry(TerrainSampler s, string name, double x, double z)
    {
        for (int ring = 0; ring < 60; ring++)
        {
            int steps = Math.Max(1, ring * 6);
            for (int i = 0; i < steps; i++)
            {
                double angle = i * Math.PI * 2 / steps, r = ring * 25;
                double px = x + Math.Cos(angle) * r, pz = z + Math.Sin(angle) * r;
                var t = s.Sample(px, pz);
                if (t.Water != WaterKind.None) continue;
                double slope = Math.Abs(s.Height(px + 2, pz) - t.Height) + Math.Abs(s.Height(px, pz + 2) - t.Height);
                if (slope < 1.2) return new Landmark(name, px, pz);
            }
        }
        return new Landmark(name, x, z);
    }

    private static double Dist(Point2 a, Point2 b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Z - b.Z) * (a.Z - b.Z));
}
