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

        // Cliff rock (128 px, tiles both ways; the terrain shader tints it with the biome's rock colour): strata of
        // uneven thickness that wander, pinch out and are offset by small faults; ledges lit on top with a dark seam
        // under them; blocky fractures and pits. Neutral grey around 0.6 so the tint sets the colour.
        {
            int W = 128;
            var stone = new[] { (68, 66, 64), (92, 89, 86), (116, 113, 108), (140, 137, 131), (163, 160, 153), (186, 183, 176) };
            var t = new Tex(W, W, false);
            for (int y = 0; y < W; y++)
                for (int x = 0; x < W; x++)
                {
                    double u = x / (double)W, v = y / (double)W;
                    // Faults: every column block shifts the layers a little.
                    int block = (int)Math.Floor(u * 5 + Fbm(u, v, 2, 4, 141) * 1.5);
                    double shift = (Hash(((block % 5) + 5) % 5, 0, 142) - .5) * .06;
                    // Layer coordinate: warped, with thickness varying along the face (12 layers per tile on average).
                    double warp = (Fbm(u, v, 3, 2, 143) - .5) * .35 + (Fbm(u, v, 8, 4, 144) - .5) * .06;
                    double layerCoord = (v + warp + shift) * 12;
                    int layer = (int)Math.Floor(layerCoord);
                    double f = layerCoord - layer;
                    double thick = .35 + .65 * Hash(((layer % 12) + 12) % 12, 1, 145); // lighter/darker beds
                    double tone = .3 + .45 * thick + .25 * (Fbm(u, v, 16, 8, 146) - .5);
                    if (f < .12) tone += .22;                 // lit ledge top
                    if (f > .84) tone -= .35 * (f - .84) / .16; // shadowed seam under the ledge
                    // Some beds pinch out: blend into the layer above.
                    if (Fbm(u, v, 4, 3, 147 + ((layer % 12) + 12) % 12) < .3 && f > .5) tone = tone * .5 + .3;
                    // Blocky vertical fractures within each bed, and pits.
                    double cx = u * 10 + Hash(((layer % 12) + 12) % 12, 2, 148) * 10;
                    double edge = Math.Abs(cx - Math.Round(cx));
                    if (edge < .05 && Hash(((layer % 12) + 12) % 12, (int)Math.Round(cx) % 10, 149) < .7) tone -= .3;
                    if (Hash(x, y, 150) < .02) tone -= .2;
                    t.Set(x, y, Ramp(stone, tone, x, y));
                }
            t.Save(Path.Combine(dir, "T_Cliff_Rock_128.png"));
        }

        RockSet(dir);

        // Grass hanging over a cliff lip (v = 0 at the lip): a dense tuft band, then strands of different lengths.
        {
            var t = new Tex(64, 64, true);
            var green = new[] { (44, 62, 30), (62, 84, 38), (84, 106, 46), (108, 124, 56) };
            for (int s = 0; s < 34; s++)
            {
                double x = s * 1.9 + Hash(s, 0, 101) * 1.5, len = 10 + 50 * Math.Pow(Hash(s, 1, 102), 1.6);
                double curl = (Hash(s, 2, 103) - .5) * 6;
                for (int y = 0; y < len && y < 64; y++)
                {
                    double k = y / len;
                    int px = (int)Math.Round(x + curl * k * k + Math.Sin(y * .3 + s) * .6);
                    var c = green[Math.Clamp((int)((1 - k) * 3.2 + Hash(px, y, 104) * 1.2 - .4), 0, 3)];
                    t.Set(px, y, c); if (k < .35) t.Set(px + 1, y, green[1]);
                }
            }
            for (int x = 0; x < 64; x++)
                for (int y = 0; y < 5 + (int)(Hash(x, 0, 105) * 5); y++) t.Set(x, y, green[Math.Clamp((int)(Hash(x, y, 106) * 3) + (y < 2 ? 1 : 0), 0, 3)]);
            t.Save(Path.Combine(dir, "T_Drape_Grass_64.png"));
        }

        // Roots poking out of a cliff edge (v = 0 at the top): a few thick roots wandering down, thinning, forking.
        {
            var t = new Tex(64, 64, true);
            var root = new[] { (58, 44, 32), (82, 62, 44), (108, 84, 60) };
            void Root(double x, double y, double dx, double width, int depth, int seed)
            {
                for (int s = 0; s < 60 && y < 63; s++)
                {
                    dx += (Hash(seed, s, 111) - .5) * .5; dx = Math.Clamp(dx, -.9, .9);
                    x += dx; y += 1;
                    int w = Math.Max(1, (int)Math.Round(width));
                    for (int i = 0; i < w; i++) t.Set((int)Math.Round(x) + i, (int)y, root[i == 0 ? 2 : i == w - 1 ? 0 : 1]);
                    width *= .975;
                    if (depth < 2 && Hash(seed, s, 112) < .05) Root(x, y, dx + (Hash(seed, s, 113) < .5 ? -.7 : .7), width * .6, depth + 1, seed * 7 + s);
                    if (width < .6 && Hash(seed, s, 114) < .08) return;
                }
            }
            for (int r = 0; r < 6; r++) Root(6 + r * 10 + Hash(r, 0, 115) * 5, -1, (Hash(r, 1, 116) - .5) * .6, 3.2 + Hash(r, 2, 117) * 1.5, 0, r + 1);
            t.Save(Path.Combine(dir, "T_Roots_64.png"));
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
        Wetland(dir);
    }

    /// <summary>
    /// Wetland plants (Tom, round 8: Dead Marshes, northern fen, Louisiana bayou): bald cypress bark and feathery
    /// foliage, long Spanish moss, bog shrub leaves, pale marsh tussock and green sedge blades, lily pads and water
    /// hyacinth rosettes.
    /// </summary>
    private static void Wetland(string dir)
    {
        var baldBark = new[] { (58, 52, 46), (80, 72, 62), (104, 94, 80), (128, 118, 100), (150, 140, 120) };
        var baldLeaf = new[] { (36, 48, 28), (50, 64, 34), (66, 80, 40), (86, 98, 50), (110, 118, 64) };
        var bogLeaf = new[] { (40, 56, 30), (56, 76, 36), (76, 96, 44), (100, 112, 50), (120, 82, 52) };
        // Bald cypress: grey-brown bark with long vertical fissures (stringy).
        Fill(64, 64, (x, y, u, v) =>
        {
            double fissure = Fbm(u, v * .15, 14, 1, 101, 2);
            double t = .25 + .6 * Fbm(u, v, 10, 4, 102) - (fissure < .38 ? .35 : 0) + ((x * 5 + y / 13) % 9 == 0 ? -.15 : 0);
            return Ramp(baldBark, t, x, y);
        }).Save(Path.Combine(dir, "T_Bald_Bark_64.png"));
        // Feathery foliage: fine needle sprays, brighter toward the light.
        Fill(64, 64, (x, y, u, v) =>
        {
            double spray = ((x + 2 * y) % 5 == 0 || (2 * x - y + 64) % 7 == 0) ? .25 : 0;
            return Ramp(baldLeaf, .15 + .6 * Fbm(u, v, 8, 8, 103) + spray * Hash(x / 2, y / 2, 104), x, y);
        }).Save(Path.Combine(dir, "T_Bald_Foliage_64.png"));
        // Spanish moss (32 × 64, v = 0 at the top): many thin wavy grey-green strands, long and tapering.
        {
            var t = new Tex(32, 64, true);
            var pal = new[] { (112, 122, 100), (138, 148, 122), (164, 172, 146) };
            for (int s = 0; s < 22; s++)
            {
                double x = 1 + s * 1.4 + Hash(s, 0, 111), len = 26 + 38 * Hash(s, 1, 112);
                for (int y = 0; y < len && y < 64; y++)
                {
                    int px = (int)Math.Round(x + Math.Sin(y * .3 + s * 1.7) * 1.4 + y * (Hash(s, 2, 113) - .5) * .08);
                    if (y > len * .7 && Hash(px, y, 114) < .4) continue; // ragged ends
                    t.Set(px, y, pal[(int)(Hash(px, y, 115) * 3)]);
                }
            }
            t.Save(Path.Combine(dir, "T_Spanish_Moss_32.png"));
        }
        // Bog shrub (leatherleaf, bog myrtle): dense small leaves, some red-brown.
        Fill(64, 64, (x, y, u, v) =>
        {
            double leaf = Hash(x / 2, y / 2, 121), speck = Hash(x, y, 123);
            double t = .15 + .55 * Fbm(u, v, 8, 8, 122) + (leaf > .75 ? .25 : 0) - (speck < .15 ? .2 : 0);
            return leaf < .04 ? bogLeaf[4] : Ramp(bogLeaf[..4], t, x, y);
        }).Save(Path.Combine(dir, "T_Bog_Leaves_64.png"));
        // Grass clumps (v = 0 at the top): pale straw tussock (marsh) and fresh green sedge (fen).
        void Blades(string name, (int, int, int)[] pal, int seed)
        {
            var t = new Tex(64, 64, true);
            for (int b = 0; b < 46; b++)
            {
                double x0 = 32 + (Hash(b, 0, seed) - .5) * 26, lean = (Hash(b, 1, seed) - .5) * 1.6 + (x0 - 32) / 26;
                double len = 30 + 32 * Hash(b, 2, seed), curve = (Hash(b, 3, seed) - .5) * .02;
                var c = pal[(int)(Hash(b, 4, seed) * pal.Length)];
                for (int s = 0; s < len; s++)
                {
                    double k = s / len, x = x0 + lean * s * .45 + curve * s * s;
                    t.Set((int)Math.Round(x), 63 - s, k > .85 ? pal[^1] : c);
                    if (k < .4) t.Set((int)Math.Round(x) + 1, 63 - s, pal[0]);
                }
            }
            t.Save(Path.Combine(dir, name));
        }
        Blades("T_Tussock_64.png", new[] { (110, 102, 72), (138, 128, 88), (164, 152, 106), (186, 176, 128) }, 131);
        Blades("T_Sedge_64.png", new[] { (46, 76, 30), (64, 100, 38), (88, 124, 46), (120, 146, 64) }, 141);
        // Lily pads (top view, tiles loosely): round pads with a notch, a few white flowers.
        {
            var t = new Tex(64, 64, true);
            var pad = new[] { (34, 64, 30), (48, 84, 36), (66, 104, 44) };
            for (int p = 0; p < 20; p++)
            {
                double r = 4 + 5 * Hash(p, 2, 153), cx = r + 1 + Hash(p * 13 + 5, 17, 151) * (62 - 2 * r), cy = r + 1 + Hash(29, p * 7 + 3, 152) * (62 - 2 * r), notch = Hash(p, 3, 154) * Math.Tau;
                for (int y = (int)(cy - r); y <= cy + r; y++)
                    for (int x = (int)(cx - r); x <= cx + r; x++)
                    {
                        double dx = x - cx, dy = y - cy, d = Math.Sqrt(dx * dx + dy * dy);
                        if (d > r) continue;
                        double a = Math.Atan2(dy, dx), da = Math.Abs(Math.IEEERemainder(a - notch, Math.Tau));
                        if (da < .3 && d > r * .15) continue;
                        bool vein = Math.Abs(Math.IEEERemainder(a * 4, Math.PI)) < .12;
                        t.Set(x, y, d > r - 1 ? pad[0] : vein ? pad[2] : pad[1]);
                    }
                if (p % 4 == 1)
                    for (int k = 0; k < 6; k++) t.Set((int)cx + (k % 3) - 1, (int)cy + k / 3 - 1, k == 1 ? (230, 210, 90) : (236, 236, 226));
            }
            t.Save(Path.Combine(dir, "T_LilyPads_64.png"));
        }
        // Water hyacinth mat (top view): glossy round leaves in rosettes, lavender flower spikes.
        {
            var t = new Tex(64, 64, true);
            var leaf = new[] { (30, 54, 24), (42, 72, 30), (58, 92, 36), (78, 110, 46) };
            for (int y = 0; y < 64; y++)
                for (int x = 0; x < 64; x++)
                {
                    double u = x / 64.0, v = y / 64.0, r = Math.Sqrt((u - .5) * (u - .5) + (v - .5) * (v - .5)) * 2;
                    double m = Fbm(u, v, 4, 4, 161) + .45 - .75 * r * r; // a ragged round raft, never cut by the card edge
                    if (m < .5 || r > .97) continue;
                    double rosette = Hash(x / 4, y / 4, 162);
                    t.Set(x, y, Ramp(leaf, .2 + .6 * Fbm(u, v, 16, 16, 163) + (rosette > .7 ? .2 : 0), x, y));
                    if (m > .68 && Hash(x / 3, y / 3, 164) > .93) t.Set(x, y, Hash(x, y, 165) < .5 ? (150, 120, 190) : (184, 160, 220));
                }
            t.Save(Path.Combine(dir, "T_Hyacinth_64.png"));
        }
    }

    /// <summary>
    /// More rock looks (128 px, tile both ways, neutral so the biome rock colour tints them; Tom, round 7): granite
    /// blocks, basalt columns, banded sandstone, mossy rock, icy rock. Same pixel style as the layered cliff rock.
    /// </summary>
    private static void RockSet(string dir)
    {
        const int W = 128;
        var stone = new[] { (68, 66, 64), (92, 89, 86), (116, 113, 108), (140, 137, 131), (163, 160, 153), (186, 183, 176) };
        int Wrap(int v, int m) => ((v % m) + m) % m;

        // Tileable jittered-cell distances (Worley) on a grid of cx × cy cells, with optional stretch.
        (double D1, double D2, int Id) Cells(double u, double v, int cx, int cy, int seed)
        {
            double gx = u * cx, gy = v * cy, d1 = 9, d2 = 9; int id = 0;
            int ix = (int)Math.Floor(gx), iy = (int)Math.Floor(gy);
            for (int j = -1; j <= 1; j++)
                for (int i = -1; i <= 1; i++)
                {
                    int ci = Wrap(ix + i, cx), cj = Wrap(iy + j, cy);
                    double px = ix + i + .15 + .7 * Hash(ci, cj, seed), py = iy + j + .15 + .7 * Hash(ci, cj, seed + 1);
                    double d = Math.Sqrt((gx - px) * (gx - px) + (gy - py) * (gy - py));
                    if (d < d1) { d2 = d1; d1 = d; id = cj * cx + ci; } else if (d < d2) d2 = d;
                }
            return (d1, d2, id);
        }

        // Granite: a few big blocks with irregular, partly open joints, sheeting cracks, coarse speckle.
        Fill(W, W, (x, y, u, v) =>
        {
            double wu = u + (Fbm(u, v, 4, 4, 205) - .5) * .12, wv = v + (Fbm(u, v, 4, 4, 206) - .5) * .12;
            var (d1, d2, id) = Cells(((wu % 1) + 1) % 1, ((wv % 1) + 1) % 1, 3, 3, 201);
            double tone = .42 + .3 * Hash(id, 0, 202) + .25 * (Fbm(u, v, 8, 8, 203) - .5) + .12 * (Fbm(u, v, 32, 32, 207) - .5);
            tone -= .2 * d1 * d1;
            // Joints: open in places, closed (just a shade) elsewhere.
            double open = Fbm(u, v, 3, 3, 208);
            if (d2 - d1 < .05 && open > .4) tone -= .45;
            else if (d2 - d1 < .09) tone -= .12;
            // Sheeting: faint curved cracks parallel to the surface.
            double sheet = (v * 7 + Fbm(u, v, 2, 2, 209) * 2.5) % 1;
            if (sheet < .03 && Fbm(u, v, 6, 6, 210) > .5) tone -= .25;
            double speck = Hash(x, y, 204);
            if (speck > .92) tone += .22; else if (speck < .06) tone -= .18;
            return Ramp(stone, tone, x, y);
        }).Save(Path.Combine(dir, "T_Rock_Granite_128.png"));

        // Basalt: vertical columns of uneven width (1D, tiles both ways), dark joints, lit faces, cross-fractures.
        {
            var edges = new List<double> { 0 };
            for (int c = 1; c < 9; c++) edges.Add((c + (Hash(c, 0, 216) - .5) * .6) / 9.0);
            edges.Add(1);
            Fill(W, W, (x, y, u, v) =>
            {
                double wu = u + (Fbm(u, v, 1, 2, 217) - .5) * .02;
                wu = ((wu % 1) + 1) % 1;
                int c = 0; while (c < edges.Count - 2 && wu >= edges[c + 1]) c++;
                double a = edges[c], b = edges[c + 1], f = (wu - a) / (b - a);
                double tone = .32 + .2 * Hash(c % 9, 0, 212) + .12 * (Fbm(u, v, 8, 8, 213) - .5);
                tone += .2 * Math.Sin(f * Math.PI) - .08;                  // rounded faces
                if (f < .07 || f > .93) tone -= .35;                       // joints
                double cross = ((v * 5 + Hash(c % 9, 1, 214) * 5) % 1 + 1) % 1;
                if (cross < .035 && Hash(c % 9, (int)Math.Floor(v * 5 + Hash(c % 9, 1, 214) * 5), 215) < .7) tone -= .3;
                return Ramp(stone, tone * .9, x, y);
            }).Save(Path.Combine(dir, "T_Rock_Basalt_128.png"));
        }

        // Sandstone: soft wavy cross-bedded bands, eroded smooth, few cracks.
        Fill(W, W, (x, y, u, v) =>
        {
            double warp = (Fbm(u, v, 2, 2, 221) - .5) * .5 + Math.Sin(u * Math.PI * 2 * 2 + Fbm(u, v, 4, 4, 222) * 3) * .03;
            double bands = (v + warp) * 16;
            double f = bands - Math.Floor(bands);
            double tone = .5 + .22 * Math.Sin(bands * .9 + Hash(Wrap((int)Math.Floor(bands), 16), 0, 223) * 6) + .12 * (Fbm(u, v, 16, 8, 224) - .5);
            if (f < .08) tone -= .12;
            if (Fbm(u, v, 6, 6, 225) < .22) tone -= .18;                 // wind-eroded hollows
            return Ramp(stone, tone, x, y);
        }).Save(Path.Combine(dir, "T_Rock_Sandstone_128.png"));

        // Mossy rock: grey blocky rock with green moss on the tops and in patches, lichen dots.
        {
            var moss = new[] { (46, 70, 36), (64, 92, 44), (86, 112, 54) };
            Fill(W, W, (x, y, u, v) =>
            {
                var (d1, d2, id) = Cells(u, v, 6, 5, 231);
                double tone = .4 + .2 * Hash(id, 0, 232) + .2 * (Fbm(u, v, 8, 8, 233) - .5) - .2 * d1 * d1;
                bool joint = d2 - d1 < .08;
                if (joint) tone -= .4;
                double m = Fbm(u, v, 5, 5, 234) + .25 * (1 - v % .2 / .2) * 0; // patches
                if (!joint && m > .56) return Ramp(moss, (m - .56) * 4 + .2 * (Hash(x, y, 235) - .5), x, y);
                if (Hash(x, y, 236) > .975) return (150, 152, 110); // lichen
                return Ramp(stone, tone, x, y);
            }).Save(Path.Combine(dir, "T_Rock_Mossy_128.png"));
        }

        // Icy rock: dark rock with broad frost streaks running down and snow lodged on ledges.
        {
            var ice = new[] { (150, 170, 182), (190, 208, 218), (228, 238, 244) };
            Fill(W, W, (x, y, u, v) =>
            {
                double layer = (v + (Fbm(u, v, 3, 2, 241) - .5) * .3) * 7;
                double f = layer - Math.Floor(layer);
                double tone = .3 + .25 * (Fbm(u, v, 8, 8, 242) - .5) + .15 * Hash(Wrap((int)Math.Floor(layer), 7), 0, 243);
                if (f > .84) tone -= .25;
                double streak = Fbm(u, v, 12, 2, 244) * .7 + Fbm(u, v, 24, 4, 247) * .3;   // tall streaks
                double ledge = f < .16 ? (1 - f / .16) * Fbm(u, v, 16, 4, 245) : 0;
                if (ledge > .3) return Ramp(ice, .6 + .4 * ledge + .15 * (Hash(x, y, 246) - .5), x, y);
                if (streak > .58) return Ramp(ice, (streak - .58) * 2.5 + .1 * (Hash(x, y, 248) - .5), x, y);
                return Ramp(stone, tone, x, y);
            }).Save(Path.Combine(dir, "T_Rock_Icy_128.png"));
        }
    }
}
