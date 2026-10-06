using System;

namespace Thornwake.WorldGen;

/// <summary>
/// Ocean waves for the "experimental 2" water (rounds 10–11, Tom's Sea of Thieves references and feedback).
///
/// * Fixed wave set: 52 wind-sea waves on a fixed ladder of wavelengths (1 … 260 m) whose directions cover the whole
///   compass (golden-angle steps, so neighbouring wavelengths point far apart), plus 12 swell waves 30° apart. Only
///   the amplitudes depend on the sea state, so storms grow and calm down smoothly (re-picking wavelengths made the
///   whole sea jump), and wind turns never pop.
/// * Sea state: wind-sea height 0.15 m (calm: a long soft swell carries the surface) … ~14 m (sea state 1), peak
///   wavelength 9 … ~140 m (moderate seas stay short and lively: λ grows with the square of the sea state); stylised
///   steep (about twice real storm steepness) in the spirit of Sea of Thieves. JONSWAP spectrum (γ = 3.3).
/// * Direction depends on the place: each point weights the waves by how close they run to its local heading — the
///   wind turned by a slow regional field (sea: ±~1 rad over ~8 km; lakes: their own valley winds, ±~1.2 rad over
///   ~3 km) with a spread that is narrow in calm weather and wide (confused) in storms. Swells come from far storms:
///   their heading follows a separate field over ~20 km, independent of the local wind. Weights are renormalised
///   per point, so the wave height stays the same wherever you are.
/// * Crisp crests: Gerstner choppiness goes mostly to the biggest steep waves (share ∝ k²A³), so the dominant waves
///   get sharp, trochoid crests; the total never reaches folding.
/// * World anchored: phases include the float origin, so waves do not jump when the scene re-centres, and the regional
///   fields use world coordinates.
/// The water shader (WaterExperimental2.gdshader) evaluates exactly these formulas (uniforms waves2, wave2_qp,
/// world_origin), so swimming and boats can use <see cref="Height"/>. The classic <see cref="OceanWaves"/> is unchanged.
/// </summary>
public sealed class SpectrumWaves
{
    public const int Count = 64, WindCount = 52;
    private const double G = 9.81;
    public const double LambdaMax = 260, LambdaMin = 1.0, CapKA = .34;

    /// <summary>Per wave: direction x, direction z, wavenumber k, base amplitude (m, before the direction weighting).</summary>
    public readonly (double Dx, double Dz, double K, double A)[] Waves = new (double, double, double, double)[Count];
    /// <summary>Per wave: phase (rad) at the world origin, and the phase including the current float origin.</summary>
    public readonly double[] Phase = new double[Count], ShiftedPhase = new double[Count];
    public double SeaState { get; private set; }
    /// <summary>Significant wave height (m) of the wind sea and of the swell.</summary>
    public double Hs { get; private set; }
    public double SwellHs { get; private set; }
    /// <summary>Total choppiness Σ q k A at a point (&lt; 1: never folds).</summary>
    public double Chop { get; private set; }
    public double OriginX { get; private set; }
    public double OriginZ { get; private set; }

    public SpectrumWaves(double seaState = .25)
    {
        for (int i = 0; i < Count; i++)
        {
            double lambda, angle;
            if (i < WindCount)
            {
                double t = i / (double)(WindCount - 1);
                lambda = LambdaMax * Math.Pow(LambdaMin / LambdaMax, t) * (1 + .04 * (Hash(i, 3) - .5));
                angle = i * 2.39996323 + .3 * (Hash(i, 1) - .5);
            }
            else
            {
                int j = i - WindCount;
                lambda = 120 * Math.Pow(1.45, j % 3);
                angle = j * Math.PI / 6;
            }
            Waves[i] = (Math.Cos(angle), Math.Sin(angle), 2 * Math.PI / lambda, 0);
            Phase[i] = ShiftedPhase[i] = Hash(i, 2) * 2 * Math.PI;
        }
        Configure(seaState);
    }

    public static double PeakWavelength(double s) => 9 + 130 * s * s;

    public void Configure(double seaState)
    {
        double s = SeaState = Math.Clamp(seaState, 0, 1);
        // Calm: mostly a long, soft swell (the whole surface slowly rising and falling); storms: huge wind seas over it.
        Hs = .15 + 14.1 * s * s;
        SwellHs = .45 + 1.6 * s;
        Chop = .62 + .3 * s;
        double omegaPeak = Math.Sqrt(G * 2 * Math.PI / PeakWavelength(s));
        var energy = new double[Count];
        double total = 0;
        for (int i = 0; i < WindCount; i++)
        {
            double omega = Math.Sqrt(G * Waves[i].K), r = omegaPeak / omega;
            double sigma = omega <= omegaPeak ? .07 : .09;
            double peak = Math.Pow(3.3, Math.Exp(-(omega - omegaPeak) * (omega - omegaPeak) / (2 * sigma * sigma * omegaPeak * omegaPeak)));
            energy[i] = Math.Pow(omega, -5) * Math.Exp(-1.25 * r * r * r * r) * peak * omega; // × Δω ∝ ω (geometric ladder)
            total += energy[i];
        }
        // Base amplitudes: Hs = 4 √(Σ A²/2) over the whole compass; the per-point direction weighting renormalises.
        for (int i = 0; i < Count; i++)
        {
            var w = Waves[i];
            double a = i < WindCount
                ? Math.Sqrt(2 * (Hs / 4) * (Hs / 4) * energy[i] / total)
                : Math.Sqrt(2 * (SwellHs / 4) * (SwellHs / 4) / (Count - WindCount));
            Waves[i] = (w.Dx, w.Dz, w.K, a);
        }
    }

    /// <summary>The float origin (world position of scene (0, 0)): phases shift so the waves stay put in the world.</summary>
    public void SetOrigin(double originX, double originZ)
    {
        OriginX = originX; OriginZ = originZ;
        for (int i = 0; i < Count; i++)
        {
            var w = Waves[i];
            double p = Phase[i] + w.K * (w.Dx * originX + w.Dz * originZ);
            ShiftedPhase[i] = p - Math.Floor(p / (2 * Math.PI)) * 2 * Math.PI;
        }
    }

    /// <summary>Wave groups: sets of higher waves drifting with the wind (same as the shader's group()); world coordinates.</summary>
    public double Group(double lx, double lz, double t, double windX, double windZ)
    {
        double x = lx + OriginX, z = lz + OriginZ;
        double n = Noise(x * .0035 - windX * t * .012, z * .0035 - windZ * t * .012);
        return .65 + (.45 + .35 * SeaState) * n * 1.2 - .15;
    }

    /// <summary>
    /// Local wave headings at scene point (lx, lz) (shader headings()): wind sea (wind turned by the regional field;
    /// lakes have their own) and swell (independent of the wind). Unit vectors.
    /// </summary>
    public (double Wx, double Wz, double Sx, double Sz) Headings(double lx, double lz, double windX, double windZ, bool lake)
    {
        double x = lx + OriginX, z = lz + OriginZ;
        double wind = Math.Atan2(windZ, windX);
        double turn = lake
            ? 2.4 * (Noise(x / 3000 + 57.1, z / 3000 - 13.7) - .5) + .8 * (Noise(x / 900 + 5.3, z / 900 + 41.9) - .5)
            : 2.0 * (Noise(x / 8000, z / 8000) - .5) + .7 * (Noise(x / 2500 + 31.7, z / 2500 + 8.9) - .5);
        double swell = 1.5 + 5.2 * (Noise(x / 20000 + 91.3, z / 20000 - 27.1) - .5);
        return (Math.Cos(wind + turn), Math.Sin(wind + turn), Math.Cos(swell), Math.Sin(swell));
    }

    /// <summary>Direction weight of wave i for the local headings (shader dir_weight()).</summary>
    public double Weight(int i, (double Wx, double Wz, double Sx, double Sz) h, bool lake)
    {
        var w = Waves[i];
        if (i >= WindCount) return lake ? 0 : Math.Pow(Math.Max(0, w.Dx * h.Sx + w.Dz * h.Sz), 24);
        double m = 10 - 8.4 * SeaState;
        return Math.Pow(Math.Max(0, w.Dx * h.Wx + w.Dz * h.Wz), m);
    }

    /// <summary>
    /// Per-point wave amplitudes (direction-weighted and renormalised, before the depth/fetch factor) and choppiness:
    /// the shader computes the same in its first loop.
    /// </summary>
    public (double[] Amp, double[] Q) Local(double lx, double lz, double windX, double windZ, bool lake)
    {
        var h = Headings(lx, lz, windX, windZ, lake);
        var weight = new double[Count];
        double ea = 0, ew = 0, sa = 0, sw = 0;
        for (int i = 0; i < Count; i++)
        {
            weight[i] = Weight(i, h, lake);
            double a2 = Waves[i].A * Waves[i].A;
            if (i < WindCount) { ea += a2; ew += a2 * weight[i]; } else { sa += a2; sw += a2 * weight[i]; }
        }
        double nw = Math.Sqrt(ea / Math.Max(ew, 1e-12)), ns = Math.Sqrt(sa / Math.Max(sw, 1e-12));
        var amp = new double[Count];
        double crisp = 0;
        for (int i = 0; i < Count; i++)
        {
            amp[i] = Waves[i].A * Math.Sqrt(weight[i]) * (i < WindCount ? nw : ns);
            double k = Waves[i].K;
            crisp += k * k * amp[i] * amp[i] * amp[i];
        }
        var q = new double[Count];
        for (int i = 0; i < Count; i++) q[i] = Chop * Waves[i].K * amp[i] * amp[i] / Math.Max(crisp, 1e-12); // Σ q k A = Chop
        return (amp, q);
    }

    /// <summary>
    /// Displacement of the water particle at rest position (lx, lz): amplitudes from <see cref="Local"/>, each wave
    /// scaled by <paramref name="factor"/>(λ) (depth, fetch), capped at kA ≤ 0.34, times the group factor.
    /// </summary>
    public (double X, double Y, double Z) Displacement(double lx, double lz, double t, Func<double, double> factor,
        (double[] Amp, double[] Q) local, double group) => Evaluate(lx, lz, t, factor, local, group).D;

    /// <summary>Displacement and the derivatives of its horizontal part (xx, zz, xz) for the inversion.</summary>
    private ((double X, double Y, double Z) D, double Jxx, double Jzz, double Jxz) Evaluate(double lx, double lz, double t,
        Func<double, double> factor, (double[] Amp, double[] Q) local, double group)
    {
        double dx = 0, dy = 0, dz = 0, jxx = 0, jzz = 0, jxz = 0;
        for (int i = 0; i < Count; i++)
        {
            var (wx, wz, k, _) = Waves[i];
            if (local.Amp[i] <= 1e-5) continue;
            double amp = Math.Min(local.Amp[i], CapKA / k) * factor(2 * Math.PI / k) * group;
            if (amp <= 0) continue;
            double theta = k * (wx * lx + wz * lz) - Math.Sqrt(G * k) * t + ShiftedPhase[i];
            double c = Math.Cos(theta), sn = Math.Sin(theta), q = local.Q[i] / Math.Max(group, 1); // higher wave sets must not fold either
            dx += q * amp * wx * c; dz += q * amp * wz * c; dy += amp * sn;
            double qa = q * amp * k * sn;
            jxx -= qa * wx * wx; jzz -= qa * wz * wz; jxz -= qa * wx * wz;
        }
        return ((dx, dy, dz), jxx, jzz, jxz);
    }

    /// <summary>Water surface height above the mean level at a fixed scene point (inverts the horizontal motion).</summary>
    public double Height(double lx, double lz, double t, Func<double, double> factor, double windX, double windZ, bool lake = false)
    {
        // Regional weights vary over kilometres: sampled at the query point (the particle is metres away). Wave groups
        // (~300 m) are taken at the rest position, as the shader does.
        var local = Local(lx, lz, windX, windZ, lake);
        // Find the rest position p with p + D(p) = (lx, lz): Newton steps (sharp crests make plain iteration crawl).
        double px = lx, pz = lz;
        for (int it = 0; it < 12; it++)
        {
            var e = Evaluate(px, pz, t, factor, local, Group(px, pz, t, windX, windZ));
            double fx = px + e.D.X - lx, fz = pz + e.D.Z - lz;
            if (fx * fx + fz * fz < 1e-8) break;
            double a = 1 + e.Jxx, b = e.Jxz, d = 1 + e.Jzz, det = a * d - b * b;
            if (det < .05) { px -= fx * .5; pz -= fz * .5; continue; } // near a fold: a damped step
            px -= (d * fx - b * fz) / det; pz -= (a * fz - b * fx) / det;
        }
        return Evaluate(px, pz, t, factor, local, Group(px, pz, t, windX, windZ)).D.Y;
    }

    private static double Hash(int i, int salt)
    {
        ulong h = (ulong)(i * 7919 + salt * 104729 + 12345);
        h ^= h >> 33; h *= 0xff51afd7ed558ccdUL; h ^= h >> 33; h *= 0xc4ceb9fe1a85ec53UL; h ^= h >> 33;
        return (h >> 11) / (double)(1UL << 53);
    }

    // The shaders' value noise (float hash, same constants as WaterExperimental*.gdshader).
    private static float Fract(float v) => v - MathF.Floor(v);

    private static float HashF(float x, float y)
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
        float a = HashF(ix, iy), b = HashF(ix + 1, iy), c = HashF(ix, iy + 1), d = HashF(ix + 1, iy + 1);
        return a + (b - a) * fx + (c - a) * fy + (a - b - c + d) * fx * fy;
    }
}
