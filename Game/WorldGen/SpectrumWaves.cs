using System;

namespace Thornwake.WorldGen;

/// <summary>
/// Ocean waves for the "experimental 2" water (round 10, Tom's Sea of Thieves references): 32 Gerstner waves drawn from a
/// JONSWAP spectrum (energy peaked around one wavelength, sharp peak enhancement, ω⁻⁵ tail) instead of 12 hand-placed
/// waves. Significant wave height grows steeply with the sea state (0.4 m calm … ~7 m storm, steep), directions fan out around
/// the wind — wider for short waves and much wider in storms (confused, chaotic seas) — and a swell runs in from another
/// heading. Crests are sharp (Gerstner choppiness up to ~0.95 of folding). The water shader evaluates exactly these waves
/// (uniforms waves2 / wave2_qp) with the same group modulation, so swimming and boats can use <see cref="Height"/>.
/// The classic <see cref="OceanWaves"/> is unchanged.
/// </summary>
public sealed class SpectrumWaves
{
    public const int Count = 32;
    private const double G = 9.81;

    /// <summary>Per wave: direction x, direction z, wavenumber k, amplitude (m).</summary>
    public readonly (double Dx, double Dz, double K, double A)[] Waves = new (double, double, double, double)[Count];
    /// <summary>Per wave: Gerstner choppiness q and phase (rad).</summary>
    public readonly (double Q, double Phase)[] QP = new (double, double)[Count];
    public double SeaState { get; private set; }
    /// <summary>Significant wave height (m): crest-to-trough height of the highest third of the waves.</summary>
    public double Hs { get; private set; }
    public double WindAngle { get; private set; }

    public SpectrumWaves(double seaState = .25, double windAngle = .64) => Configure(seaState, windAngle);

    public void Configure(double seaState, double windAngle)
    {
        double s = SeaState = Math.Clamp(seaState, 0, 1);
        WindAngle = windAngle;
        Hs = .4 + 6.8 * Math.Pow(s, 1.5);
        // Peak wavelength: short chop … ~70 m storm seas. Stylised: about twice as steep as real storm seas (whose
        // 200 m swells look like slow hills even at 8 m), in the spirit of Sea of Thieves.
        double lambdaPeak = 9 + 62 * Math.Pow(s, 1.2);
        double omegaPeak = Math.Sqrt(G * 2 * Math.PI / lambdaPeak);
        double lambdaMax = lambdaPeak * 1.8, lambdaMin = Math.Max(1.2, lambdaPeak / 34);
        var energy = new double[Count];
        double total = 0;
        for (int i = 0; i < Count; i++)
        {
            double t = i / (double)(Count - 1);                     // 0 longest … 1 shortest
            double lambda = lambdaMax * Math.Pow(lambdaMin / lambdaMax, t);
            double k = 2 * Math.PI / lambda, omega = Math.Sqrt(G * k);
            // JONSWAP (γ = 3.3) in ω, times Δω ∝ ω for geometric spacing.
            double r = omegaPeak / omega;
            double sigma = omega <= omegaPeak ? .07 : .09;
            double peak = Math.Pow(3.3, Math.Exp(-(omega - omegaPeak) * (omega - omegaPeak) / (2 * sigma * sigma * omegaPeak * omegaPeak)));
            double e = Math.Pow(omega, -5) * Math.Exp(-1.25 * r * r * r * r) * peak * omega;
            energy[i] = e; total += e;
            // Direction: spread around the wind; short waves and storms spread wider. The longest wave is a swell from
            // another heading (a sea is never only the local wind).
            double spread = (.3 + .55 * s) * (.6 + .9 * t);
            double u = Hash(i, 1) * 2 - 1;
            double angle = i == 0 ? windAngle + .85 : windAngle + Math.Sign(u) * Math.Pow(Math.Abs(u), .8) * spread * 1.6;
            Waves[i] = (Math.Cos(angle), Math.Sin(angle), k, 0);
            QP[i] = (0, Hash(i, 2) * 2 * Math.PI);
        }
        // Scale amplitudes so that Hs = 4 √(Σ A²/2); cap each wave's steepness (kA ≤ 0.3).
        double target = Hs / 4 * (Hs / 4);
        double choppy = .6 + .36 * s;
        for (int i = 0; i < Count; i++)
        {
            var w = Waves[i];
            double a = Math.Sqrt(2 * target * energy[i] / total);
            a = Math.Min(a, .3 / w.K);
            Waves[i] = (w.Dx, w.Dz, w.K, a);
        }
        double sumKA = 0;
        for (int i = 0; i < Count; i++) sumKA += Waves[i].K * Waves[i].A;
        // Choppiness: Σ q k A = choppy (< 1: crests sharpen as waves line up, but never loop over).
        for (int i = 0; i < Count; i++) QP[i] = (choppy / Math.Max(sumKA, 1e-6), QP[i].Phase);
    }

    /// <summary>Wave groups: sets of higher waves drifting with the wind (same as the shader's group()).</summary>
    public static double Group(double x, double z, double t, double windX, double windZ, double seaState)
    {
        double n = Noise(x * .0035 - windX * t * .012, z * .0035 - windZ * t * .012);
        return .55 + (.5 + .45 * seaState) * n * 1.4 - .2;
    }

    /// <summary>Displacement of the water particle at rest position (x, z), each wave scaled by <paramref name="factor"/>(λ).</summary>
    public (double X, double Y, double Z) Displacement(double x, double z, double t, Func<double, double> factor, double group)
    {
        double dx = 0, dy = 0, dz = 0;
        for (int i = 0; i < Count; i++)
        {
            var (wx, wz, k, a) = Waves[i];
            double amp = a * factor(2 * Math.PI / k) * group;
            if (amp <= 0) continue;
            double theta = k * (wx * x + wz * z) - Math.Sqrt(G * k) * t + QP[i].Phase;
            double c = Math.Cos(theta), q = QP[i].Q;
            dx += q * amp * wx * c; dz += q * amp * wz * c; dy += amp * Math.Sin(theta);
        }
        return (dx, dy, dz);
    }

    /// <summary>Water surface height above the mean level at a fixed point (inverts the horizontal motion).</summary>
    public double Height(double x, double z, double t, Func<double, double> factor, double windX, double windZ)
    {
        double group = Group(x, z, t, windX, windZ, SeaState);
        double px = x, pz = z;
        for (int it = 0; it < 10; it++)
        {
            var d = Displacement(px, pz, t, factor, group);
            px = x - d.X; pz = z - d.Z;
        }
        return Displacement(px, pz, t, factor, group).Y;
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
