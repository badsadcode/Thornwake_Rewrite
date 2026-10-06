using System;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Shore data and the experimental water's wave surface on the CPU (WaterExperimental.gdshader evaluates the same
/// formulas), so swimming — and boats later — follow the surface the player sees.
/// </summary>
public static class WaterShore
{
    private const double G = 9.81;

    /// <summary>
    /// Near a coast (sea under ~25 m deep): the bed's uphill gradient over ≥ 8 m (toward the shore; length = slope)
    /// and how open the sea is around (macro land in 8 directions at 0.6, 2 and 6 km: a straight coast is fully exposed,
    /// a bay sheltered). Pure function of world position, so neighbouring tiles agree.
    /// </summary>
    public static (float Gx, float Gz, float Exposure) Coast(TerrainSampler sampler, double x, double z, double spacing = 1)
    {
        double d = Math.Max(8, spacing * 2);
        float gx = (float)((sampler.Height(x + d, z, d) - sampler.Height(x - d, z, d)) / (2 * d));
        float gz = (float)((sampler.Height(x, z + d, d) - sampler.Height(x, z - d, d)) / (2 * d));
        double open = 0;
        var macro = sampler.Plan.Macro;
        for (int a = 0; a < 8; a++)
        {
            double ang = a * Math.PI / 4, cx = Math.Cos(ang), cz = Math.Sin(ang), dirOpen = 1;
            foreach (double r in Radii)
            {
                macro.Sample(x + cx * r, z + cz * r, WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out double land, out _, out _);
                if (land > 0) dirOpen *= r < 1000 ? 0 : r < 3000 ? .35 : .7;
            }
            open += dirOpen;
        }
        return (gx, gz, (float)Math.Clamp(open / 8 / .45, 0, 1)); // a straight coast (~half open) counts as fully exposed
    }

    private static readonly double[] Radii = { 600, 2000, 6000 };

    /// <summary>Experimental amplitude factor of Gerstner wave <paramref name="i"/> (shader wave_factor).</summary>
    /// <param name="wide">Experimental 2: the hand-over to the shore waves starts at 19 m instead of 14 m.</param>
    public static double Factor(int i, double lambda, double depth, bool sea, bool lake, double fetch, double exposure, bool wide = false)
    {
        double f = DetMath.Smooth(depth / (lambda * .12 + .3));
        if (sea) return f * (.3 + .7 * exposure) * (wide ? .4 + .6 * Smoothstep(4, 19, depth) : .35 + .65 * Smoothstep(3, 14, depth));
        if (!lake) return 0;
        double longest = Math.Clamp(fetch * .04, 2.5, 45);
        return f * (1 - Smoothstep(longest, longest * 1.6, lambda)) * Math.Clamp(.18 + fetch / 6000, .18, .85) * (i >= 10 ? 0 : 1)
            * Smoothstep(0, 2.5, depth);
    }

    /// <summary>
    /// Experimental water surface height above the mean level at scene-local (lx, lz) — the shader's coordinates — at
    /// time t: the Gerstner sea cut to the body (lakes: fetch; seas: exposure, shallows) plus the shore wave.
    /// Ignores the small horizontal shift of the shore wave.
    /// </summary>
    public static double Height(OceanWaves waves, double lx, double lz, double t, double depth, bool sea, bool lake,
        double fetch, double exposure, double gx, double gz)
    {
        // Gerstner part: invert the horizontal motion as OceanWaves.Height does.
        double px = lx, pz = lz;
        for (int it = 0; it < 8; it++)
        {
            var (dx, _, dz) = Gerstner(waves, px, pz, t, depth, sea, lake, fetch, exposure);
            px = lx - dx; pz = lz - dz;
        }
        double y = Gerstner(waves, px, pz, t, depth, sea, lake, fetch, exposure).Y;
        if (sea) y += Shore(lx, lz, t, depth, gx, gz, exposure, waves.SeaState);
        return y;
    }

    /// <summary>The "experimental 2" surface: the spectrum sea (with its wave groups) cut to the body, plus the shore wave.</summary>
    public static double Height2(SpectrumWaves waves, double lx, double lz, double t, double depth, bool sea, bool lake,
        double fetch, double exposure, double gx, double gz, double windX, double windZ)
    {
        double y = waves.Height(lx, lz, t, lambda => Factor(0, lambda, depth, sea, lake, fetch, exposure, wide: true), windX, windZ, lake, depth, gx, gz);
        if (sea) y += Shore(lx, lz, t, depth, gx, gz, exposure, waves.SeaState, wide: true);
        return y;
    }

    private static (double X, double Y, double Z) Gerstner(OceanWaves waves, double x, double z, double t, double depth, bool sea, bool lake, double fetch, double exposure)
    {
        double dx = 0, dy = 0, dz = 0;
        for (int i = 0; i < OceanWaves.Count; i++)
        {
            var (wx, wz, k, a) = waves.Waves[i];
            double amp = a * Factor(i, 2 * Math.PI / k, depth, sea, lake, fetch, exposure);
            if (amp <= 0) continue;
            double theta = k * (wx * x + wz * z) - Math.Sqrt(G * k) * t + OceanWaves.Phase[i];
            double c = Math.Cos(theta), q = waves.Q[i];
            dx += q * amp * wx * c; dz += q * amp * wz * c; dy += amp * Math.Sin(theta);
        }
        return (dx, dy, dz);
    }

    /// <summary>Shore wave height (shader shore_wave, vertical part).</summary>
    public static double Shore(double lx, double lz, double t, double depth, double gx, double gz, double exposure, double seaState, bool wide = false)
    {
        double s = Math.Sqrt(gx * gx + gz * gz);
        double active = (wide ? Smoothstep(19, 9, depth) : Smoothstep(14, 8, depth)) * Smoothstep(.003, .012, s) * (1 - Smoothstep(.22, .45, s));
        if (active <= 0) return 0;
        double period = 7 + 4 * seaState, omega = 2 * Math.PI / period;
        const double SRef = .03; // reference bed slope: the phase depends on depth only (see the shader)
        double d = Math.Max(depth, .05);
        double theta = -(2 * omega * Math.Sqrt(d) / (SRef * Math.Sqrt(G)) + omega * t) + 3 * Noise(lx * .004, lz * .004)
            + (wide ? 1.5 * Noise(lx * .013 + 7.7, lz * .013 + 7.7) + .9 * Noise(lx * .035 + 2.3, lz * .035 + 2.3) : 0); // experimental 2: crest segments run ahead or lag
        double h0 = (.12 + 1.15 * seaState) * exposure * (.75 + .5 * Noise(lx * .01 + t * .02, lz * .01 + t * .02));
        // Experimental 2: some crest segments break sooner than others (an irregular foam line).
        double h = h0 * Math.Pow(10 / Math.Max(d, .4), .25), limit = (wide ? .62 + .2 * Noise(lx * .02 + 3.1, lz * .02 + 3.1) + .12 * Noise(lx * .06 + 9.4, lz * .06 + 9.4) : .75) * d + .03;
        double breaking = Smoothstep(.55, .95, h / limit) * active;
        h = Math.Min(h, limit);
        double amp = .5 * h * active;
        return amp * (Math.Sin(theta) + .22 * breaking * Math.Sin(2 * theta - .6));
    }

    private static double Smoothstep(double e0, double e1, double x)
    {
        double u = Math.Clamp((x - e0) / (e1 - e0), 0, 1);
        return u * u * (3 - 2 * u);
    }

    // The shader's value noise (float hash, same constants).
    private static float Fract(float v) => v - MathF.Floor(v);

    private static float Hash(float x, float y)
    {
        float p3x = Fract(x * .1031f), p3y = Fract(y * .1031f), p3z = Fract(x * .1031f);
        float dd = p3x * (p3y + 33.33f) + p3y * (p3z + 33.33f) + p3z * (p3x + 33.33f);
        p3x += dd; p3y += dd; p3z += dd;
        return Fract((p3x + p3y) * p3z);
    }

    public static double Noise(double x, double y)
    {
        float fx0 = (float)x, fy0 = (float)y;
        float ix = MathF.Floor(fx0), iy = MathF.Floor(fy0), fx = fx0 - ix, fy = fy0 - iy;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy);
        float a = Hash(ix, iy), b = Hash(ix + 1, iy), c = Hash(ix, iy + 1), d = Hash(ix + 1, iy + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }
}
