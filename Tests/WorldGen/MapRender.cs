using Thornwake.WorldGen;
using System.Threading.Tasks;

/// <summary>Hill-shaded elevation maps rendered straight from the sampler (no engine), for visual review.</summary>
internal static class MapRender
{
    /// <summary>Relief exaggeration override for the shading (null = automatic by window size).</summary>
    public static double? Exaggeration;
    /// <summary>Render a square window centred on (cx, cz) with the given side length (metres).</summary>
    public static void Window(TerrainSampler sampler, string path, double cx, double cz, double side, int pixels, double? minWavelength = null)
    {
        double spacing = side / pixels;
        int n = pixels + 2; // one-pixel border for shading
        var heights = new double[n * n];
        var water = new double[pixels * pixels];
        double mw = minWavelength ?? spacing * 2;
        sampler.HeightGridParallel(cx - side / 2 - spacing, cz - side / 2 - spacing, spacing, n, n, heights, mw);
        Parallel.For(0, pixels, y =>
        {
            for (int x = 0; x < pixels; x++)
            {
                var t = sampler.Sample(cx - side / 2 + x * spacing, cz - side / 2 + y * spacing, mw);
                water[y * pixels + x] = t.Water is WaterKind.Lake or WaterKind.River ? t.WaterLevel - t.Height : 0;
            }
        });
        var rgb = new byte[pixels * pixels * 3];
        double sea = sampler.Plan.Profile.SeaLevel, top = sampler.Plan.Profile.MountainHeight + sampler.Plan.Profile.LowlandHeight;
        // Exaggerate relief for small windows so walking-scale shape is readable; always light from the north-west.
        double exaggeration = Exaggeration ?? Math.Clamp(4000 / side, 1, 8);
        for (int y = 0; y < pixels; y++)
            for (int x = 0; x < pixels; x++)
            {
                int i = (y + 1) * n + x + 1;
                double h = heights[i];
                double dx = (heights[i + 1] - heights[i - 1]) / (2 * spacing) * exaggeration;
                double dz = (heights[i + n] - heights[i - n]) / (2 * spacing) * exaggeration;
                double len = Math.Sqrt(dx * dx + dz * dz + 1);
                double shade = Math.Clamp(((-dx * -.6) + (-dz * -.6) + 1 * .53) / len, 0, 1); // light from -x,-z, above
                var (r, g, b) = Colour(h - sea, top);
                double light = h > sea ? .35 + .85 * shade : .75 + .25 * shade;
                double wet = water[y * pixels + x];
                if (wet > 0) { var deep = Math.Clamp(wet / 25, 0, 1); (r, g, b) = Mix((96, 150, 176), (28, 64, 110), Math.Sqrt(deep)); light = .9 + .1 * shade; }
                int o = (y * pixels + x) * 3;
                rgb[o] = (byte)Math.Clamp(r * light, 0, 255);
                rgb[o + 1] = (byte)Math.Clamp(g * light, 0, 255);
                rgb[o + 2] = (byte)Math.Clamp(b * light, 0, 255);
            }
        DrawRivers(sampler, rgb, pixels, cx - side / 2, cz - side / 2, spacing);
        Png.Write(path, pixels, pixels, rgb);
    }

    /// <summary>Overlay river centrelines where channels are narrower than a pixel (overview maps).</summary>
    private static void DrawRivers(TerrainSampler sampler, byte[] rgb, int pixels, double x0, double z0, double spacing)
    {
        foreach (var reach in sampler.Plan.Hydrology.Reaches)
            for (int k = 0; k + 1 < reach.Points.Length; k++)
            {
                if (reach.Width[k] >= spacing * 1.5) continue; // visible from the terrain samples already
                double strength = Math.Clamp(Math.Log10(reach.Catchment[k] / sampler.Plan.Style.RiverMinCatchmentKm2 + 1) / 2.5, .25, 1);
                var a = reach.Points[k]; var b = reach.Points[k + 1];
                double len = Math.Max(Math.Abs(b.X - a.X), Math.Abs(b.Z - a.Z)) / spacing;
                int steps = Math.Max(1, (int)Math.Ceiling(len * 2));
                for (int i = 0; i <= steps; i++)
                {
                    double t = i / (double)steps;
                    int px = (int)((a.X + (b.X - a.X) * t - x0) / spacing), py = (int)((a.Z + (b.Z - a.Z) * t - z0) / spacing);
                    if (px < 0 || py < 0 || px >= pixels || py >= pixels) continue;
                    int o = (py * pixels + px) * 3;
                    rgb[o] = (byte)(rgb[o] * (1 - strength) + 40 * strength);
                    rgb[o + 1] = (byte)(rgb[o + 1] * (1 - strength) + 90 * strength);
                    rgb[o + 2] = (byte)(rgb[o + 2] * (1 - strength) + 170 * strength);
                }
            }
    }

    private static (double, double, double) Colour(double h, double top)
    {
        if (h <= 0)
        {
            double d = Math.Clamp(-h / 2500, 0, 1);
            return Mix((70, 140, 170), (14, 38, 72), Math.Sqrt(d));
        }
        double t = h / top;
        if (h < 4) return Mix((196, 186, 140), (110, 140, 74), h / 4);
        if (t < .12) return Mix((110, 140, 74), (82, 118, 58), t / .12);
        if (t < .3) return Mix((82, 118, 58), (128, 122, 82), (t - .12) / .18);
        if (t < .6) return Mix((128, 122, 82), (118, 110, 104), (t - .3) / .3);
        return Mix((118, 110, 104), (236, 236, 240), Math.Clamp((t - .6) / .3, 0, 1));
    }

    private static (double, double, double) Mix((double, double, double) a, (double, double, double) b, double t) =>
        (a.Item1 + (b.Item1 - a.Item1) * t, a.Item2 + (b.Item2 - a.Item2) * t, a.Item3 + (b.Item3 - a.Item3) * t);
}
