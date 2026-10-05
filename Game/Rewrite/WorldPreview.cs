using System;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Top-down map of a generated world plan for the creation screen: sea depth, land height with hill shading,
/// mountains and snow, lakes and rivers. Reads the macro raster and hydrology only (fast: ~0.1 s at 512 px).
/// </summary>
public static class WorldPreview
{
    public static Image Render(WorldPlan plan, int pixels, bool biomes = false)
    {
        if (biomes) return RenderBiomes(plan, pixels);
        var raster = plan.Macro;
        var p = plan.Profile;
        double world = p.WorldSize, x0 = plan.Manifest.OriginX - world / 2, z0 = plan.Manifest.OriginZ - world / 2, step = world / pixels;
        var height = new float[pixels * pixels];
        var land = new float[pixels * pixels];
        System.Threading.Tasks.Parallel.For(0, pixels, j =>
        {
            for (int i = 0; i < pixels; i++)
            {
                double x = x0 + (i + .5) * step, z = z0 + (j + .5) * step;
                raster.Sample(x, z, WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out double l, out double h, out _);
                land[j * pixels + i] = (float)l; height[j * pixels + i] = (float)h;
            }
        });
        var data = new byte[pixels * pixels * 3];
        double top = p.LowlandHeight + p.HillHeight + p.MountainHeight;
        for (int j = 0; j < pixels; j++)
            for (int i = 0; i < pixels; i++)
            {
                int k = j * pixels + i;
                Color c;
                if (land[k] <= 0)
                {
                    float deep = Mathf.Clamp(-land[k] / .5f, 0, 1);
                    c = new Color(.36f, .58f, .7f).Lerp(new Color(.1f, .22f, .38f), deep);
                }
                else
                {
                    float h = (float)(height[k] / top);
                    c = h < .12f ? new Color(.42f, .55f, .3f).Lerp(new Color(.5f, .58f, .33f), h / .12f)
                        : h < .35f ? new Color(.5f, .58f, .33f).Lerp(new Color(.55f, .48f, .32f), (h - .12f) / .23f)
                        : h < .7f ? new Color(.55f, .48f, .32f).Lerp(new Color(.5f, .47f, .45f), (h - .35f) / .35f)
                        : new Color(.5f, .47f, .45f).Lerp(new Color(.93f, .94f, .96f), (h - .7f) / .3f);
                    // Hill shading from the north-west.
                    float hx = height[j * pixels + Math.Min(i + 1, pixels - 1)] - height[j * pixels + Math.Max(i - 1, 0)];
                    float hz = height[Math.Min(j + 1, pixels - 1) * pixels + i] - height[Math.Max(j - 1, 0) * pixels + i];
                    float shade = Mathf.Clamp(1 - (hx + hz) / (float)(step * 2) * 6, .55f, 1.35f);
                    c = new Color(c.R * shade, c.G * shade, c.B * shade);
                    if (plan.Hydrology.LakeAt(x0 + (i + .5) * step, z0 + (j + .5) * step) >= 0) c = new Color(.3f, .5f, .62f);
                }
                data[k * 3] = (byte)(Mathf.Clamp(c.R, 0, 1) * 255); data[k * 3 + 1] = (byte)(Mathf.Clamp(c.G, 0, 1) * 255); data[k * 3 + 2] = (byte)(Mathf.Clamp(c.B, 0, 1) * 255);
            }
        // Rivers: polylines, brighter and thicker with catchment.
        foreach (var reach in plan.Hydrology.Reaches)
        {
            var pts = reach.Points;
            for (int n = 1; n < pts.Length; n++)
            {
                double strength = Math.Clamp(Math.Log10(reach.Catchment[n] / 30 + 1) / 2.2, .25, 1);
                Line(data, pixels, (pts[n - 1].X - x0) / step, (pts[n - 1].Z - z0) / step, (pts[n].X - x0) / step, (pts[n].Z - z0) / step, strength);
            }
        }
        return Image.CreateFromData(pixels, pixels, false, Image.Format.Rgb8, data);
    }

    private static void Line(byte[] data, int n, double ax, double az, double bx, double bz, double strength)
    {
        int steps = (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(bz - az))) + 1;
        for (int s = 0; s <= steps; s++)
        {
            double t = s / (double)steps;
            int x = (int)(ax + (bx - ax) * t), z = (int)(az + (bz - az) * t);
            if (x < 0 || z < 0 || x >= n || z >= n) continue;
            int k = (z * n + x) * 3;
            data[k] = (byte)(data[k] + (70 - data[k]) * strength);
            data[k + 1] = (byte)(data[k + 1] + (130 - data[k + 1]) * strength);
            data[k + 2] = (byte)(data[k + 2] + (200 - data[k + 2]) * strength);
        }
    }

    /// <summary>Biome map (D42): each land pixel in its biome's map colour (blended at borders), with hill shading.</summary>
    public static Image RenderBiomes(WorldPlan plan, int pixels)
    {
        var p = plan.Profile;
        var map = plan.Biomes;
        double world = p.WorldSize, x0 = plan.Manifest.OriginX - world / 2, z0 = plan.Manifest.OriginZ - world / 2, step = world / pixels;
        var colors = new Color[map.Pool.Count];
        for (int b = 0; b < colors.Length; b++) colors[b] = new Color(map.Pool[b].Map.R, map.Pool[b].Map.G, map.Pool[b].Map.B);
        var height = new float[pixels * pixels];
        var data = new byte[pixels * pixels * 3];
        System.Threading.Tasks.Parallel.For(0, pixels, j =>
        {
            for (int i = 0; i < pixels; i++)
            {
                double x = x0 + (i + .5) * step, z = z0 + (j + .5) * step;
                plan.Macro.Sample(x, z, WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out double l, out double h, out _);
                int k = j * pixels + i;
                height[k] = (float)h;
                Color c;
                if (l <= 0) c = new Color(.36f, .58f, .7f).Lerp(new Color(.1f, .22f, .38f), Mathf.Clamp(-(float)l / .5f, 0, 1));
                else if (plan.Hydrology.LakeAt(x, z) >= 0) c = new Color(.3f, .5f, .62f);
                else { var (a, b, share) = map.Sample(x, z); c = colors[b].Lerp(colors[a], (float)share); }
                data[k * 3] = (byte)(c.R * 255); data[k * 3 + 1] = (byte)(c.G * 255); data[k * 3 + 2] = (byte)(c.B * 255);
            }
        });
        // Hill shading over land.
        for (int j = 1; j < pixels - 1; j++)
            for (int i = 1; i < pixels - 1; i++)
            {
                int k = j * pixels + i;
                if (height[k] <= 0) continue;
                float hx = height[k + 1] - height[k - 1], hz = height[k + pixels] - height[k - pixels];
                float shade = Mathf.Clamp(1 - (hx + hz) / (float)(step * 2) * 6, .6f, 1.3f);
                for (int c = 0; c < 3; c++) data[k * 3 + c] = (byte)Mathf.Clamp(data[k * 3 + c] * shade, 0, 255);
            }
        return Image.CreateFromData(pixels, pixels, false, Image.Format.Rgb8, data);
    }

    /// <summary>Share of the world that is land (raster cells), for the creation screen's summary.</summary>
    public static double LandShare(WorldPlan plan)
    {
        var land = plan.Macro.Channel(WorldPlan.LandChannel);
        int count = 0;
        foreach (var v in land) if (v > 0) count++;
        return count / (double)land.Length;
    }
}
