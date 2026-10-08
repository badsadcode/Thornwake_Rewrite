using System;
using System.Threading.Tasks;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// The "foam net" texture (round 29, Tom liked the foam of emilje/godot-water-shader — its look only, no image or code
/// taken): a seamless 2D map of sea foam as a thick, fluffy net around large round clear holes of different sizes,
/// small holes in the thickest parts, faint thin cracks across the holes. Built from warped cellular (Worley) noise,
/// periodic over the tile. Channels:
///   R — the coarse net (8 cells a side); G — a finer net (15 cells, another seed), mixed in where the surface is
///   compressed hardest (fresh, folding foam is finer); B — soft value fbm (patches: where foam is thick or thin).
/// About 0.1 s for 512² on start-up; deterministic.
/// </summary>
public static class FoamNet
{
    public static ImageTexture Create(int size = 512, uint seed = 6121)
    {
        var data = new byte[size * size * 4];
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float r = Net(u, v, 8, seed, .11f);
                float g = Net(u + .37f, v + .61f, 15, seed + 101, .13f);
                float b = Fbm(u, v, 4, 4, seed + 211);
                int i = (y * size + x) * 4;
                data[i] = (byte)(r * 255); data[i + 1] = (byte)(g * 255); data[i + 2] = (byte)(b * 255); data[i + 3] = 255;
            }
        });
        var image = Image.CreateFromData(size, size, false, Image.Format.Rgba8, data);
        image.GenerateMipmaps();
        return ImageTexture.CreateFromImage(image);
    }

    /// <summary>Foam 0..1 at (u, v): round clear holes of different sizes in warped cells, the foam between them
    /// thick and fluffy, small holes in the thickest parts, faint thin cracks across the holes.</summary>
    private static float Net(float u, float v, int cells, uint salt, float warp)
    {
        // Warp: no straight runs, cells of uneven size and shape.
        float wu = u + (Fbm(u, v, 4, 3, salt + 7) - .5f) * warp * 2;
        float wv = v + (Fbm(u, v, 4, 3, salt + 13) - .5f) * warp * 2;
        var (f1, f2, size) = Worley(wu, wv, cells, salt);
        float edge = f2 - f1; // 0 on the line between two cells
        // A round hole in each cell, its size random per cell, pinched shut where the cell is narrow.
        float r = .4f + .5f * size;
        float hole = (1 - Smooth(r * .4f, r * 1.05f, f1)) * Smooth(0, .3f, edge);
        float foam = 1 - hole;
        // Small holes in the thick foam.
        var (s1, _, ss) = Worley(wu + .31f, wv + .77f, cells * 3, salt + 51);
        float small = (1 - Smooth(.12f + .2f * ss, .4f + .2f * ss, s1)) * Smooth(.6f, 1, foam) * Smooth(.3f, .7f, Fbm(u, v, 8, 2, salt + 61));
        foam *= 1 - .85f * small;
        // Fluffy grain.
        foam *= .6f + .4f * Smooth(.2f, .8f, Fbm(u, v, 32, 3, salt + 29));
        // Faint thin cracks across the holes.
        var (g1, g2, _) = Worley(wu + .5f, wv + .25f, cells * 3, salt + 41);
        float crack = (1 - Smooth(0, .05f, g2 - g1)) * .22f * hole;
        return Math.Clamp(Math.Max(foam, crack), 0, 1);
    }

    private static float Smooth(float a, float b, float x)
    {
        float t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Nearest and second-nearest feature distance (in cells) and a random 0..1 of the nearest cell, periodic
    /// over the unit square.</summary>
    private static (float F1, float F2, float Id) Worley(float u, float v, int cells, uint salt)
    {
        float x = u * cells, y = v * cells;
        int cx = (int)MathF.Floor(x), cy = (int)MathF.Floor(y);
        float b1 = 9, b2 = 9, id = 0;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int ix = cx + dx, iy = cy + dy;
                int wx = ((ix % cells) + cells) % cells, wy = ((iy % cells) + cells) % cells;
                uint h = Hash(wx, wy, salt);
                float px = ix + .1f + .8f * (h & 1023) / 1023f, py = iy + .1f + .8f * ((h >> 10) & 1023) / 1023f;
                float d = MathF.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
                if (d < b1) { b2 = b1; b1 = d; id = ((h >> 20) & 1023) / 1023f; } else if (d < b2) b2 = d;
            }
        return (b1, b2, id);
    }

    private static float Fbm(float u, float v, int period, int octaves, uint salt)
    {
        float sum = 0, amp = 1, norm = 0;
        for (int o = 0; o < octaves; o++) { sum += amp * Value(u, v, period << o, salt + (uint)o); norm += amp; amp *= .5f; }
        return sum / norm;
    }

    private static float Value(float u, float v, int period, uint salt)
    {
        float x = u * period, y = v * period;
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float fx = x - x0, fy = y - y0;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float L(int a, int b) => (Hash(((a % period) + period) % period, ((b % period) + period) % period, salt) & 65535) / 65535f;
        float a0 = L(x0, y0) + (L(x0 + 1, y0) - L(x0, y0)) * fx;
        float a1 = L(x0, y0 + 1) + (L(x0 + 1, y0 + 1) - L(x0, y0 + 1)) * fx;
        return a0 + (a1 - a0) * fy;
    }

    private static uint Hash(int x, int y, uint salt)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + salt * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        return h ^ (h >> 16);
    }
}
