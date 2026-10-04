using System;

namespace Thornwake.WorldGen;

/// <summary>
/// Deterministic 2D noise in double precision (world coordinates reach ±1,000 km, where float lattices break).
/// Only + − × and floor are used, so results are identical on every platform.
/// </summary>
public static class Noise
{
    /// <summary>A4-compatible value noise in [-1, 1] with quintic interpolation.</summary>
    public static double Value(double x, double z, ulong seed)
    {
        double fx = Math.Floor(x), fz = Math.Floor(z);
        long ix = (long)fx, iz = (long)fz;
        double u = Fade(x - fx), v = Fade(z - fz);
        double a = Corner(ix, iz, seed), b = Corner(ix + 1, iz, seed), c = Corner(ix, iz + 1, seed), d = Corner(ix + 1, iz + 1, seed);
        double low = a * (1 - u) + b * u, high = c * (1 - u) + d * u;
        return low * (1 - v) + high * v;
    }

    private static double Corner(long x, long z, ulong seed) => Hashing.Unit(Hashing.Lattice(x, z, seed)) * 2 - 1;

    /// <summary>
    /// Gradient (Perlin-style) noise with analytic derivatives, roughly in [-1, 1].
    /// Derivatives are with respect to the input coordinates (divide by your frequency scale yourself).
    /// </summary>
    public static double Gradient(double x, double z, ulong seed, out double dx, out double dz)
    {
        double fx0 = Math.Floor(x), fz0 = Math.Floor(z);
        long ix = (long)fx0, iz = (long)fz0;
        double fx = x - fx0, fz = z - fz0;
        Grad(ix, iz, seed, out double gax, out double gaz);
        Grad(ix + 1, iz, seed, out double gbx, out double gbz);
        Grad(ix, iz + 1, seed, out double gcx, out double gcz);
        Grad(ix + 1, iz + 1, seed, out double gdx, out double gdz);
        double va = gax * fx + gaz * fz;
        double vb = gbx * (fx - 1) + gbz * fz;
        double vc = gcx * fx + gcz * (fz - 1);
        double vd = gdx * (fx - 1) + gdz * (fz - 1);
        double ux = Fade(fx), uz = Fade(fz);
        double dux = FadeDerivative(fx), duz = FadeDerivative(fz);
        double k = va - vb - vc + vd;
        double value = va + ux * (vb - va) + uz * (vc - va) + ux * uz * k;
        dx = gax + ux * (gbx - gax) + uz * (gcx - gax) + ux * uz * (gax - gbx - gcx + gdx) + dux * (uz * k + (vb - va));
        dz = gaz + ux * (gbz - gaz) + uz * (gcz - gaz) + ux * uz * (gaz - gbz - gcz + gdz) + duz * (ux * k + (vc - va));
        // Random gradients in [-1,1]^2 give a standard deviation of about 0.18; scale to about 0.4 so that
        // amplitudes passed by callers mean roughly "typical peak height" (values stay mostly within ±1).
        const double Scale = 2.2;
        dx *= Scale; dz *= Scale;
        return value * Scale;
    }

    public static double Gradient(double x, double z, ulong seed) => Gradient(x, z, seed, out _, out _);

    private static void Grad(long x, long z, ulong seed, out double gx, out double gz)
    {
        // Cheaper than the SplitMix lattice hash (three multiplies instead of five); quality is ample for gradients.
        ulong h = unchecked((ulong)x * 0x9E3779B97F4A7C15UL ^ (ulong)z * 0xC2B2AE3D27D4EB4FUL ^ seed);
        h = unchecked((h ^ (h >> 31)) * 0xD6E8FEB86659FD93UL);
        h ^= h >> 32;
        gx = (h >> 43) * (2.0 / 2097152.0) - 1;          // top 21 bits
        gz = ((h >> 22) & 0x1FFFFF) * (2.0 / 2097152.0) - 1; // next 21 bits
    }

    private static double Fade(double t) => t * t * t * (t * (t * 6 - 15) + 10);
    private static double FadeDerivative(double t) => 30 * t * t * (t * (t - 2) + 1);

    /// <summary>
    /// "Eroded" fractal noise: each octave is damped where the accumulated slope is steep, so detail gathers
    /// in valleys and on ridges the way weathered terrain does (after Inigo Quilez's derivative fBm).
    /// Returns roughly [-1, 1] (standard deviation ≈ 0.3).
    /// <para>LOD: octaves shorter than <paramref name="minWavelength"/> are skipped, and the last one fades in
    /// over one octave. Normalisation always counts all octaves, so a coarse LOD is the fine result minus small
    /// detail, never a rescaled version of it.</para>
    /// </summary>
    public static double ErodedFbm(double x, double z, double wavelength, int octaves, double minWavelength, ulong seed,
        double gain = 0.5, double erosion = 0.35)
    {
        double sum = 0, amplitude = 1, sx = 0, sz = 0;
        double frequency = 1 / wavelength;
        // Rotate each octave to hide the lattice axes.
        const double C = 0.8, S = 0.6;
        double px = x, pz = z;
        for (int i = 0; i < octaves; i++)
        {
            double fade = OctaveFade(wavelength, minWavelength);
            if (fade <= 0) break;
            double n = Gradient(px * frequency, pz * frequency, seed + (ulong)i * 0x632BE59BD9B4E019UL, out double dx, out double dz);
            sx += dx * amplitude * fade; sz += dz * amplitude * fade;
            sum += fade * amplitude * n / (1 + erosion * (sx * sx + sz * sz));
            amplitude *= gain; frequency *= 2; wavelength *= 0.5;
            double rx = C * px - S * pz, rz = S * px + C * pz; px = rx; pz = rz;
        }
        return sum / GeometricNorm(gain, octaves);
    }

    /// <summary>Plain fractal sum of gradient noise, roughly [-1, 1]. Same LOD rules as <see cref="ErodedFbm"/>.</summary>
    public static double Fbm(double x, double z, double wavelength, int octaves, double minWavelength, ulong seed, double gain = 0.5)
    {
        double sum = 0, amplitude = 1, frequency = 1 / wavelength;
        for (int i = 0; i < octaves; i++)
        {
            double fade = OctaveFade(wavelength, minWavelength);
            if (fade <= 0) break;
            sum += fade * amplitude * Gradient(x * frequency + i * 17.17, z * frequency - i * 9.31, seed + (ulong)i * 0x9E3779B97F4A7C15UL);
            amplitude *= gain; frequency *= 2; wavelength *= 0.5;
        }
        return sum / GeometricNorm(gain, octaves);
    }

    /// <summary>
    /// Ridged multifractal in about [0, 1]: sharp crests where the noise crosses zero, each octave weighted by the
    /// previous crest so detail concentrates on ridges (rocky mountain relief).
    /// </summary>
    public static double Ridged(double x, double z, double wavelength, int octaves, double minWavelength, ulong seed, double gain = 0.5)
    {
        double sum = 0, amplitude = 1, frequency = 1 / wavelength, weight = 1;
        const double C = 0.6, S = 0.8;
        double px = x, pz = z;
        for (int i = 0; i < octaves; i++)
        {
            double fade = OctaveFade(wavelength, minWavelength);
            if (fade <= 0) break;
            double n = 1 - Math.Abs(Gradient(px * frequency, pz * frequency, seed + (ulong)i * 0xD1B54A32D192ED03UL));
            n *= n;
            sum += fade * amplitude * n * weight;
            weight = DetMath.Clamp(n * 1.6, 0, 1);
            amplitude *= gain; frequency *= 2; wavelength *= 0.5;
            double rx = C * px - S * pz, rz = S * px + C * pz; px = rx; pz = rz;
        }
        return sum / GeometricNorm(gain, octaves);
    }

    /// <summary>1 for octaves at least two samples long, fading to 0 over the next octave down.</summary>
    public static double OctaveFade(double wavelength, double minWavelength) =>
        minWavelength <= 0 ? 1 : DetMath.Clamp(wavelength / minWavelength - 1, 0, 1);

    // RMS normalisation keeps the standard deviation of a fractal sum close to that of one octave,
    // independent of gain and octave count (a plain sum of amplitudes would shrink rough sums toward zero).
    private static double GeometricNorm(double gain, int octaves)
    {
        double norm = 0, a = 1;
        for (int i = 0; i < octaves; i++) { norm += a * a; a *= gain; }
        return Math.Sqrt(norm);
    }
}
