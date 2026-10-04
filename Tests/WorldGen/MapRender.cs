using Thornwake.WorldGen;

/// <summary>Hill-shaded elevation maps rendered straight from the sampler (no engine), for visual review.</summary>
internal static class MapRender
{
    /// <summary>Render a square window centred on (cx, cz) with the given side length (metres).</summary>
    public static void Window(TerrainSampler sampler, string path, double cx, double cz, double side, int pixels, double? minWavelength = null)
    {
        double spacing = side / pixels;
        int n = pixels + 2; // one-pixel border for shading
        var heights = new double[n * n];
        sampler.HeightGridParallel(cx - side / 2 - spacing, cz - side / 2 - spacing, spacing, n, n, heights, minWavelength ?? spacing * 2);
        var rgb = new byte[pixels * pixels * 3];
        double sea = sampler.Plan.Profile.SeaLevel, top = sampler.Plan.Profile.MountainHeight + sampler.Plan.Profile.LowlandHeight;
        // Exaggerate relief for small windows so walking-scale shape is readable; always light from the north-west.
        double exaggeration = Math.Clamp(4000 / side, 1, 8);
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
                int o = (y * pixels + x) * 3;
                rgb[o] = (byte)Math.Clamp(r * light, 0, 255);
                rgb[o + 1] = (byte)Math.Clamp(g * light, 0, 255);
                rgb[o + 2] = (byte)Math.Clamp(b * light, 0, 255);
            }
        Png.Write(path, pixels, pixels, rgb);
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
