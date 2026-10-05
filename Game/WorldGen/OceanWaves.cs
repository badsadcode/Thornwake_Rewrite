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
    public const int Count = 12;
    private const double G = 9.81;
    // Ten wind-sea waves (wavelength ratio 0.72, so ~1/20 of the longest at the end) spread around the wind, and two
    // long swells from other directions (a sea is never only the local wind: D41).
    private static readonly double[] Spread = { 0, .35, -.4, .7, -.75, .2, -.25, 1.0, -1.1, .55, .5, -.3 };
    private static readonly double[] Phases = { 0, 1.7, 4.1, 2.6, 5.3, .9, 3.3, 5.9, 1.1, 4.6, 2.2, .4 };

    /// <summary>Per wave: direction x, direction z, wavenumber k, amplitude (m).</summary>
    public readonly (double Dx, double Dz, double K, double A)[] Waves = new (double, double, double, double)[Count];
    /// <summary>Gerstner horizontal factor per wave (sharper crests in storms; Σ q·k·A stays below 1: no folds).</summary>
    public readonly double[] Q = new double[Count];
    /// <summary>Horizontal choppiness of the Gerstner waves (sharper crests in storms).</summary>
    public double Choppiness { get; private set; }
    public double SeaState { get; private set; }
    public double WaveHeight { get; private set; }
    public static readonly double[] Phase = Phases;

    public OceanWaves(double seaState = .25, double windAngle = .64) => Configure(seaState, windAngle);

    public void Configure(double seaState, double windAngle)
    {
        SeaState = Math.Clamp(seaState, 0, 1);
        double longest = 8 + 64 * Math.Pow(SeaState, 1.3);     // 8 m ripples … 72 m storm seas
        double steepness = .008 + .026 * SeaState;              // amplitude / wavelength
        double aligned = 1.2 - .5 * SeaState;                   // storms line the waves up with the wind
        Choppiness = .45 + .5 * SeaState;
        double total = 0;
        for (int i = 0; i < Count; i++)
        {
            double lambda, amplitude, a;
            if (i < 10)
            {
                lambda = longest * Math.Pow(.72, i);
                amplitude = lambda * steepness * (i == 0 ? 1 : .62);
                a = windAngle + Spread[i] * aligned;
            }
            else
            {
                lambda = 90 + 110 * SeaState + (i - 10) * 37; // swell
                amplitude = lambda * (.0008 + .011 * SeaState) * (i == 10 ? 1 : .6);
                a = windAngle + Spread[i];
            }
            Waves[i] = (Math.Cos(a), Math.Sin(a), 2 * Math.PI / lambda, amplitude);
            total += amplitude;
        }
        WaveHeight = total;
        for (int i = 0; i < Count; i++) Q[i] = Choppiness / (Waves[i].K * Waves[i].A * Count + 1e-9);
    }

    /// <summary>Amplitude factor of wave <paramref name="i"/> in water of the given depth; lakes keep only short waves.</summary>
    public double Factor(int i, double depth, bool lake)
    {
        double lambda = 2 * Math.PI / Waves[i].K;
        double shallow = DetMath.Smooth(depth / (lambda * .12 + .3));
        return lake ? shallow * (i >= 10 ? 0 : i >= 4 ? .45 : .05) : shallow; // lakes: no swell, short waves only
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
            double q = Q[i];
            double c = Math.Cos(theta);
            dx += q * amp * wx * c; dz += q * amp * wz * c; dy += amp * Math.Sin(theta);
        }
        return (dx, dy, dz);
    }

    /// <summary>Water surface height above the mean level at a fixed world point (inverts the horizontal motion).</summary>
    public double Height(double x, double z, double t, double depth = 1000, bool lake = false)
    {
        double px = x, pz = z;
        for (int iteration = 0; iteration < 10; iteration++)
        {
            var d = Displacement(px, pz, t, depth, lake);
            px = x - d.X; pz = z - d.Z;
        }
        return Displacement(px, pz, t, depth, lake).Y;
    }
}
