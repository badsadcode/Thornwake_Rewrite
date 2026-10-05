using System;

namespace Thornwake.WorldGen;

/// <summary>
/// Ocean and lake waves: a sum of <see cref="Count"/> Gerstner waves defined by a sea state (0 calm … 1 storm) and a
/// wind direction. The water shader evaluates exactly these waves (same parameters, same clock), so gameplay — boats,
/// swimming, buoyancy — can ask <see cref="Height"/> for the water surface the player sees. Deep-water dispersion
/// (ω² = g k); waves shrink in shallow water (below ~12 % of their wavelength) and lakes only carry the short waves.
/// </summary>
public sealed class OceanWaves
{
    public const int Count = 6;
    private const double G = 9.81;
    private static readonly double[] Lengths = { 1, .62, .41, .29, .21, .15 };
    private static readonly double[] Angles = { 0, .45, -.55, .9, -.85, .3 };
    private static readonly double[] Phases = { 0, 1.7, 4.1, 2.6, 5.3, .9 };

    /// <summary>Per wave: direction x, direction z, wavenumber k, amplitude (m).</summary>
    public readonly (double Dx, double Dz, double K, double A)[] Waves = new (double, double, double, double)[Count];
    /// <summary>Horizontal choppiness of the Gerstner waves (sharper crests in storms).</summary>
    public double Choppiness { get; private set; }
    public double SeaState { get; private set; }
    public double WaveHeight { get; private set; }
    public static readonly double[] Phase = Phases;

    public OceanWaves(double seaState = .25, double windAngle = .64) => Configure(seaState, windAngle);

    public void Configure(double seaState, double windAngle)
    {
        SeaState = Math.Clamp(seaState, 0, 1);
        double longest = 8 + 72 * Math.Pow(SeaState, 1.3);     // 8 m ripples … 80 m storm swell
        double steepness = .006 + .028 * SeaState;              // amplitude / wavelength
        Choppiness = .35 + .55 * SeaState;
        double total = 0;
        for (int i = 0; i < Count; i++)
        {
            double lambda = longest * Lengths[i], a = windAngle + Angles[i];
            double amplitude = lambda * steepness * (i == 0 ? 1 : .75);
            Waves[i] = (Math.Cos(a), Math.Sin(a), 2 * Math.PI / lambda, amplitude);
            total += amplitude;
        }
        WaveHeight = total;
    }

    /// <summary>Amplitude factor of wave <paramref name="i"/> in water of the given depth; lakes keep only short waves.</summary>
    public double Factor(int i, double depth, bool lake)
    {
        double lambda = 2 * Math.PI / Waves[i].K;
        double shallow = DetMath.Smooth(depth / (lambda * .12 + .3));
        return lake ? shallow * (i >= 3 ? .45 : .05) : shallow;
    }

    /// <summary>Displacement (dx, dy, dz) of the water particle whose rest position is (x, z) at time t.</summary>
    public (double X, double Y, double Z) Displacement(double x, double z, double t, double depth = 1000, bool lake = false)
    {
        double dx = 0, dy = 0, dz = 0;
        for (int i = 0; i < Count; i++)
        {
            var (wx, wz, k, a) = Waves[i];
            double amp = a * Factor(i, depth, lake);
            if (amp <= 0) continue;
            double theta = k * (wx * x + wz * z) - Math.Sqrt(G * k) * t + Phases[i];
            double q = Choppiness / (k * WaveHeight * Count + 1e-9);
            double c = Math.Cos(theta);
            dx += q * amp * wx * c; dz += q * amp * wz * c; dy += amp * Math.Sin(theta);
        }
        return (dx, dy, dz);
    }

    /// <summary>Water surface height above the mean level at a fixed world point (inverts the horizontal motion).</summary>
    public double Height(double x, double z, double t, double depth = 1000, bool lake = false)
    {
        double px = x, pz = z;
        for (int iteration = 0; iteration < 3; iteration++)
        {
            var d = Displacement(px, pz, t, depth, lake);
            px = x - d.X; pz = z - d.Z;
        }
        return Displacement(px, pz, t, depth, lake).Y;
    }
}
