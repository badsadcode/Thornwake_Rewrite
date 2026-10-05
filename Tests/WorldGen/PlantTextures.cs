// Pixel-art textures for the placeholder plants (palms, cacti, dead trees, acacia, jungle trees, swamp cypress,
// dry shrubs, hanging moss and vines). Small hand-tuned palettes (5 tones per material, like the woodland pack's
// ~20-colour maps) with ordered dithering, tileable where they wrap around trunks. Deterministic: rerunning
// rewrites identical files. Run: dotnet run -c Release -- --plant-textures=../../Assets/Vegetation/Placeholder

internal static class PlantTextures
{
    private sealed class Tex
    {
        public readonly int W, H; public readonly byte[] Px; public readonly bool Alpha;
        public Tex(int w, int h, bool alpha) { W = w; H = h; Alpha = alpha; Px = new byte[w * h * 4]; }
        public void Set(int x, int y, (int R, int G, int B) c, int a = 255)
        {
            if (x < 0 || y < 0 || x >= W || y >= H) return;
            int k = (y * W + x) * 4; Px[k] = (byte)c.R; Px[k + 1] = (byte)c.G; Px[k + 2] = (byte)c.B; Px[k + 3] = (byte)a;
        }
        public bool Filled(int x, int y) => x >= 0 && y >= 0 && x < W && y < H && Px[(y * W + x) * 4 + 3] > 0;
        public void Save(string path)
        {
            if (Alpha) { Png.Write(path, W, H, Px, alpha: true); return; }
            var rgb = new byte[W * H * 3];
            for (int i = 0; i < W * H; i++) { rgb[i * 3] = Px[i * 4]; rgb[i * 3 + 1] = Px[i * 4 + 1]; rgb[i * 3 + 2] = Px[i * 4 + 2]; }
            Png.Write(path, W, H, rgb);
        }
    }

    private static readonly int[] Bayer = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

    private static double Hash(int x, int y, int seed)
    {
        unchecked
        {
            uint h = (uint)(x * 374761393 + y * 668265263 + seed * 2147483647);
            h = (h ^ (h >> 13)) * 1274126177u; h ^= h >> 16;
            return (h & 0xFFFFFF) / 16777216.0;
        }
    }

    /// <summary>Tileable value noise: <paramref name="cx"/> × <paramref name="cy"/> lattice cells across the texture.</summary>
    private static double Noise(double u, double v, int cx, int cy, int seed)
    {
        double x = u * cx, y = v * cy;
        int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
        double fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        double L(int i, int j) => Hash(((i % cx) + cx) % cx, ((j % cy) + cy) % cy, seed);
        double a = L(x0, y0), b = L(x0 + 1, y0), c = L(x0, y0 + 1), d = L(x0 + 1, y0 + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }

    private static double Fbm(double u, double v, int cx, int cy, int seed, int octaves = 3)
    {
        double sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < octaves; o++) { sum += Noise(u, v, cx << o, cy << o, seed + o * 31) * amp; norm += amp; amp *= .5; }
        return sum / norm;
    }

    /// <summary>Palette tone for 0..1 with a 4×4 ordered dither between neighbouring tones (pixel-art shading).</summary>
    private static (int, int, int) Ramp((int, int, int)[] pal, double t, int x, int y)
    {
        double f = Math.Clamp(t, 0, .9999) * (pal.Length - 1) + (Bayer[(y & 3) * 4 + (x & 3)] / 16.0 - .47) * .9;
        return pal[Math.Clamp((int)Math.Floor(f + .5), 0, pal.Length - 1)];
    }

    private static void Line(Tex t, double x0, double y0, double x1, double y1, Func<double, (int, int, int)> colour, int width = 1)
    {
        int steps = (int)Math.Ceiling(Math.Max(Math.Abs(x1 - x0), Math.Abs(y1 - y0)) * 1.5) + 1;
        for (int s = 0; s <= steps; s++)
        {
            double k = s / (double)steps, x = x0 + (x1 - x0) * k, y = y0 + (y1 - y0) * k;
            for (int w = 0; w < width; w++) t.Set((int)Math.Round(x) + w, (int)Math.Round(y), colour(k));
        }
    }

    private static Tex Fill(int w, int h, Func<int, int, double, double, (int, int, int)> f)
    {
        var t = new Tex(w, h, false);
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.Set(x, y, f(x, y, x / (double)w, y / (double)h));
        return t;
    }

    public static void Write(string dir)
    {
        Directory.CreateDirectory(dir);
        var palmBark = new[] { (46, 38, 30), (70, 57, 43), (96, 80, 60), (124, 106, 80), (150, 132, 100) };
        var frond = new[] { (34, 52, 28), (50, 74, 34), (72, 98, 44), (102, 124, 56), (138, 146, 70) };
        var cactus = new[] { (38, 60, 40), (54, 82, 52), (74, 104, 62), (98, 128, 74), (126, 150, 88) };
        var dead = new[] { (52, 48, 44), (78, 72, 64), (104, 97, 86), (132, 124, 110), (160, 152, 136) };
        var acaciaBark = new[] { (40, 32, 26), (58, 46, 36), (80, 64, 48), (104, 86, 64), (128, 108, 80) };
        var acaciaLeaf = new[] { (46, 54, 28), (68, 78, 36), (94, 102, 46), (124, 124, 58), (154, 146, 72) };
        var jungleBark = new[] { (58, 54, 44), (78, 74, 60), (98, 93, 76), (118, 111, 92), (136, 128, 106) };
        var moss = new[] { (40, 62, 32), (58, 84, 40), (80, 106, 50) };
        var jungleLeaf = new[] { (20, 40, 22), (30, 58, 28), (44, 80, 34), (64, 104, 42), (92, 130, 54) };
        var cypressBark = new[] { (60, 40, 30), (84, 58, 42), (110, 78, 56), (136, 100, 72), (158, 122, 90) };
        var cypressLeaf = new[] { (28, 44, 30), (40, 60, 36), (58, 80, 42), (78, 98, 50), (100, 116, 60) };

        // Palm trunk: rings every 8 px (dark seam, lit lip), leaf scars staggered between rings.
        Fill(64, 64, (x, y, u, v) =>
        {
            int r = y % 8; double t = .5 + .28 * (Fbm(u, v, 4, 8, 11) - .5) * 2;
            if (r == 0) t -= .45; else if (r == 1) t -= .18; else if (r == 7) t += .16;
            if ((x + (y / 8 % 2) * 4) % 8 == 0 && r > 1 && r < 6) t -= .15;
            return Ramp(palmBark, t, x, y);
        }).Save(Path.Combine(dir, "T_Palm_Bark_64.png"));

        // Palm frond (v = 0 at the base): midrib up the middle, leaflets angled toward the tip, yellowing tips.
        {
            var t = new Tex(64, 64, true);
            for (int y0 = 2; y0 < 60; y0 += 3)
            {
                double along = (y0 + 4) / 64.0, len = 27 * Math.Pow(Math.Sin(Math.PI * Math.Min(1, along * .95 + .05)), .7);
                for (int side = -1; side <= 1; side += 2)
                {
                    double jitter = (Hash(y0, side, 3) - .5) * 2;
                    double x1 = 32 + side * (2 + len), y1 = y0 + len * .55 + jitter;
                    Line(t, 32 + side * 1.5, y0, x1, y1, k =>
                    {
                        double tone = .35 + .45 * (1 - k) + (Hash((int)(k * 40), y0, side + 9) - .5) * .25;
                        return k > .82 ? (138, 140, 66) : Ramp(frond, tone, (int)(x1 * k), y0);
                    });
                    Line(t, 32 + side * 1.5, y0 + 1, 32 + side * (2 + len * .6), y0 + 1 + len * .33, k => Ramp(frond, .25 + .3 * (1 - k), y0, (int)(k * 9)));
                }
            }
            for (int y = 0; y < 64; y++) { t.Set(31, y, frond[3]); t.Set(32, y, y < 48 ? frond[4] : frond[3]); }
            t.Save(Path.Combine(dir, "T_Palm_Frond_64.png"));
        }

        // Cactus skin: vertical ribs every 8 px, pale spine clusters on the ridges.
        {
            var t = Fill(64, 64, (x, y, u, v) => Ramp(cactus, .52 + .32 * Math.Cos(2 * Math.PI * (x + .5) / 8) + .22 * (Fbm(u, v, 4, 4, 21) - .5), x, y));
            for (int x = 4; x < 64; x += 8)
                for (int y = (x / 8 % 2) * 4 + 2; y < 64; y += 8)
                {
                    t.Set(x, y, (206, 198, 152)); t.Set(x - 1, y - 1, (176, 170, 132)); t.Set(x + 1, y - 1, (176, 170, 132)); t.Set(x, y + 1, (150, 146, 112));
                }
            t.Save(Path.Combine(dir, "T_Cactus_Skin_64.png"));
        }

        // Dead, weathered wood: grey with long vertical grain and dark cracks.
        Fill(64, 64, (x, y, u, v) =>
        {
            double grain = Fbm(u, v, 16, 2, 31), crack = Fbm(u, v, 8, 3, 37);
            double t = .25 + .6 * grain + .15 * (Hash(x, y, 5) - .5);
            if (crack < .3) t -= .35;
            return Ramp(dead, t, x, y);
        }).Save(Path.Combine(dir, "T_Dead_Bark_64.png"));

        // Acacia: dark rough bark; dry olive, clumpy small-leaf foliage.
        Fill(64, 64, (x, y, u, v) => Ramp(acaciaBark, .2 + .7 * Fbm(u, v, 8, 6, 41) + ((y + x / 3) % 7 == 0 ? -.25 : 0), x, y))
            .Save(Path.Combine(dir, "T_Acacia_Bark_64.png"));
        Fill(64, 64, (x, y, u, v) => Ramp(acaciaLeaf, .15 + .8 * Fbm(u, v, 6, 6, 43) + .25 * (Hash(x / 2, y / 2, 44) - .5), x, y))
            .Save(Path.Combine(dir, "T_Acacia_Foliage_64.png"));

        // Jungle giant: pale bark with moss patches; dense broad leaves (cell pattern with dark gaps).
        Fill(64, 64, (x, y, u, v) =>
        {
            double m = Fbm(u, v, 4, 6, 53);
            return m > .58 ? Ramp(moss, (m - .58) * 4 + .2 * (Hash(x, y, 54) - .5), x, y) : Ramp(jungleBark, .2 + .7 * Fbm(u, v, 8, 3, 51), x, y);
        }).Save(Path.Combine(dir, "T_Jungle_Bark_64.png"));
        Fill(64, 64, (x, y, u, v) =>
        {
            // Nearest and second-nearest jittered points on an 8×8 tileable grid: leaves, with dark seams between.
            double best = 9, second = 9;
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int ci = (x / 8 + i + 8) % 8, cj = (y / 8 + j + 8) % 8;
                    double px = (x / 8 + i) * 8 + 8 * Hash(ci, cj, 61), py = (y / 8 + j) * 8 + 8 * Hash(ci, cj, 62);
                    double d = Math.Sqrt((x - px) * (x - px) * .7 + (y - py) * (y - py));
                    if (d < best) { second = best; best = d; } else if (d < second) second = d;
                }
            double tone = .9 - best / 7 + .15 * (Fbm(u, v, 4, 4, 63) - .5);
            if (second - best < 1.1) tone = .05;
            return Ramp(jungleLeaf, tone, x, y);
        }).Save(Path.Combine(dir, "T_Jungle_Foliage_64.png"));

        // Swamp cypress: fibrous reddish bark; fine feathery dark foliage.
        Fill(64, 64, (x, y, u, v) => Ramp(cypressBark, .15 + .75 * Fbm(u, v, 20, 2, 71) + ((x * 7 + y / 9) % 11 == 0 ? -.3 : 0), x, y))
            .Save(Path.Combine(dir, "T_Cypress_Bark_64.png"));
        Fill(64, 64, (x, y, u, v) => Ramp(cypressLeaf, .2 + .65 * Fbm(u, v, 8, 8, 73) + .3 * (((x + y) % 4 == 0 ? 1 : 0) - .3) * Hash(x / 3, y, 74), x, y))
            .Save(Path.Combine(dir, "T_Cypress_Foliage_64.png"));

        // Hanging moss (v = 0 at the top): wavy grey-green strands of different lengths.
        {
            var t = new Tex(32, 32, true);
            var pal = new[] { (82, 92, 68), (106, 116, 86), (132, 140, 106) };
            for (int s = 0; s < 11; s++)
            {
                double x = 1.5 + s * 2.8 + Hash(s, 0, 81), len = 14 + 18 * Hash(s, 1, 82);
                for (int y = 0; y < len && y < 32; y++)
                {
                    int px = (int)Math.Round(x + Math.Sin(y * .45 + s) * 1.1);
                    var c = pal[(int)(Hash(px, y, 83) * 3)];
                    t.Set(px, y, c); if (y < len * .5) t.Set(px + 1, y, pal[0]);
                }
            }
            t.Save(Path.Combine(dir, "T_Moss_32.png"));
        }

        // Vine strand (tiles vertically): a wavy stem with alternating leaves.
        {
            var t = new Tex(32, 32, true);
            var leaf = new[] { (36, 70, 32), (56, 96, 42), (82, 120, 52) };
            for (int y = 0; y < 32; y++)
            {
                int x = 16 + (int)Math.Round(Math.Sin(y * Math.PI * 2 / 32) * 2);
                t.Set(x, y, (64, 70, 38)); t.Set(x + 1, y, (52, 58, 32));
            }
            for (int y = 2; y < 32; y += 6)
            {
                int side = (y / 6) % 2 == 0 ? 1 : -1, x = 16 + (int)Math.Round(Math.Sin(y * Math.PI * 2 / 32) * 2) + side * 2;
                for (int j = 0; j < 4; j++)
                    for (int i = 0; i < 5 - j; i++) t.Set(x + side * i, y + j, leaf[Math.Clamp(2 - j + (i == 0 ? -1 : 0), 0, 2)]);
            }
            t.Save(Path.Combine(dir, "T_Vine_32.png"));
        }

        // Dry desert shrub: twiggy branches fanning up from the base, a few olive leaves at the ends.
        {
            var t = new Tex(64, 64, true);
            var twig = new[] { (74, 58, 44), (98, 78, 58), (122, 100, 74) };
            void Branch(double x, double y, double angle, double len, int depth, int seed)
            {
                double x1 = x + Math.Sin(angle) * len, y1 = y - Math.Cos(angle) * len;
                Line(t, x, y, x1, y1, k => twig[Math.Min(2, depth)], depth == 0 ? 2 : 1);
                if (depth >= 2 || len < 6)
                {
                    for (int l = 0; l < 3; l++)
                        t.Set((int)(x1 + (Hash(seed, l, 91) - .5) * 4), (int)(y1 + (Hash(seed, l, 92) - .5) * 4), Hash(seed, l, 93) < .5 ? (112, 112, 62) : (138, 132, 72));
                    return;
                }
                for (int b = 0; b < 3; b++)
                    Branch(x1, y1, angle + (b - 1) * .55 + (Hash(seed, b, 94) - .5) * .4, len * (.55 + .2 * Hash(seed, b, 95)), depth + 1, seed * 3 + b + 1);
            }
            for (int r = 0; r < 4; r++) Branch(32 + (r - 1.5) * 3, 63, (r - 1.5) * .42, 20 + 4 * Hash(r, 0, 96), 0, r + 1);
            t.Save(Path.Combine(dir, "T_Shrub_Dry_64.png"));
        }
    }
}
