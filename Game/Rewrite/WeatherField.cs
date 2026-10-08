using System;
using System.Collections.Generic;
using Thornwake.WorldGen;
using Noise = Thornwake.WorldGen.Noise;

namespace Thornwake.Rewrite;

/// <summary>
/// The weather over the whole world (round 27, Tom: "proper semi-realistic weather simulation"): not a clock any more
/// but a field in space and game time, so a storm is somewhere, moves, and can be seen coming, sailed into or left
/// behind. Deterministic (seed, position, game hours), evaluated on demand — nothing is stored or stepped:
/// <list type="bullet">
/// <item>Steering: everything drifts with a prevailing upper wind (~25–45 km per game hour), its heading meandering
/// over days; <see cref="Steering"/> gives it, and the surface wind follows it (Atmosphere).</item>
/// <item>Weather systems: a large-scale field (lows ~600 km across) made of two layers drifting at different speeds,
/// so systems deepen and fill instead of sliding by unchanged; clouds where it is high, rain where it is higher.</item>
/// <item>Fronts: long ridged bands (~30 km wide) inside the systems — the classic belt of rain sweeping across.</item>
/// <item>Thunderstorm cells: one candidate per 30 km grid cell (in a slower-drifting frame, so cells move relative to
/// the fronts), more likely where it is warm and humid, inside unsettled systems and in the afternoon; each grows,
/// matures and dies within ~3 game hours, 4–12 km of heavy rain under a tall cloud with an anvil downwind.</item>
/// </list>
/// The local climate decides how much falls (deserts need strong systems, rainforests rain often) and, at the
/// player, whether it is snow.
/// </summary>
public sealed class WeatherField
{
    private readonly ulong _seed;
    private readonly Climate _climate;
    private readonly double _prevailing;

    public WeatherField(ulong seed, Climate climate)
    {
        _seed = Hashing.Mix(seed * 0x9E3779B97F4A7C15UL + 4111);
        _climate = climate;
        // Prevailing heading of the weather (toward), mostly eastward (westerlies), varying by world.
        _prevailing = (Hashing.Unit(_seed) - .5) * 1.4;
    }

    public readonly record struct Sample(double Cloud, double Precip, double Convective, double Front);
    public readonly record struct Cell(double X, double Z, double Radius, double Intensity, bool Thunder, double Age);

    /// <summary>Steering wind (toward, unit vector x/z) and speed (m/s) at a game time.</summary>
    public (double X, double Z, double Speed) Steering(double gameHours)
    {
        double a = _prevailing + .9 * Noise.Value(gameHours / 40, 0, _seed + 1) + .3 * Noise.Value(gameHours / 11, 5, _seed + 2);
        double speed = 9 + 3.5 * Noise.Value(gameHours / 23, 9, _seed + 3);
        return (Math.Cos(a), Math.Sin(a), speed);
    }

    /// <summary>How far the weather has drifted by a game time (m): the integral of the steering, in closed form
    /// (mean heading × mean speed, plus a slow sideways meander).</summary>
    private (double X, double Z) Drift(double gameHours, double factor)
    {
        double metres = gameHours * 3600 * 10 * factor; // ~36 km per game hour
        double side = 120000 * Noise.Value(gameHours / 60, 3, _seed + 4) * factor;
        double ca = Math.Cos(_prevailing), sa = Math.Sin(_prevailing);
        return (ca * metres - sa * side, sa * metres + ca * side);
    }

    private static double Smooth(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }

    /// <summary>Rain threshold from the yearly rainfall: deserts need strong systems, wet climates rain often.</summary>
    private static double Threshold(double annualRain) =>
        Math.Clamp(.8 - annualRain / 3000, .36, .78) / Math.Max(.3, GameSettings.RainFrequency.Value);

    /// <summary>Large-scale systems and frontal bands at a point (0..1 each).</summary>
    private (double Systems, double Band) Synoptic(double x, double z, double gameHours)
    {
        var (dx, dz) = Drift(gameHours, 1);
        var (ex, ez) = Drift(gameHours, .62);
        double a = Noise.Fbm(x - dx, z - dz, 600000, 3, 60000, _seed + 10);
        double b = Noise.Fbm(x - ex + 3.1e6, z - ez - 1.7e6, 450000, 3, 60000, _seed + 11);
        double systems = .5 + .55 * (.6 * a + .4 * b);
        // Fronts: ridges of a field stretched along the steering (bands lie across the direction of travel).
        double ca = Math.Cos(_prevailing), sa = Math.Sin(_prevailing);
        double qx = x - dx, qz = z - dz;
        double along = qx * ca + qz * sa, across = -qx * sa + qz * ca;
        double ridge = 1 - Math.Abs(Noise.Value(along / 260000, across / 900000, _seed + 12));
        double band = Math.Pow(ridge, 6) * Smooth(.45, .75, systems);
        return (systems, band);
    }

    /// <summary>Thunderstorm cells whose influence reaches within <paramref name="range"/> m of a point.</summary>
    public void Cells(double x, double z, double gameHours, double range, List<Cell> into)
    {
        into.Clear();
        const double size = 30000;
        var (dx, dz) = Drift(gameHours, .8);
        double qx = x - dx, qz = z - dz;
        int r = (int)Math.Ceiling(range / size) + 1;
        long ci = (long)Math.Floor(qx / size), cj = (long)Math.Floor(qz / size);
        double hourOfDay = ((gameHours % 24) + 24) % 24;
        double afternoon = .35 + .65 * Smooth(10, 14, hourOfDay) * Smooth(22, 17, hourOfDay);
        for (long i = ci - r; i <= ci + r; i++)
            for (long j = cj - r; j <= cj + r; j++)
            {
                ulong h = Hashing.Lattice(i, j, _seed + 20);
                double u1 = Hashing.Unit(h), u2 = Hashing.Unit(Hashing.Mix(h + 1)), u3 = Hashing.Unit(Hashing.Mix(h + 2));
                double u4 = Hashing.Unit(Hashing.Mix(h + 3)), u5 = Hashing.Unit(Hashing.Mix(h + 4));
                // Life cycle: a cell fires once per period (8–16 h) at its own phase and lives ~3 game hours.
                double period = 8 + 8 * u4, life = 2.2 + 1.6 * u5;
                double age = ((gameHours + u3 * period) % period + period) % period;
                if (age > life) continue;
                double cx = (i + .15 + .7 * u1) * size + dx, cz = (j + .15 + .7 * u2) * size + dz;
                var (temperature, rain) = _climate.At(cx, cz, 0);
                var (systems, _) = Synoptic(cx, cz, gameHours);
                // Fuel: warm, humid air in an unsettled system, best in the afternoon.
                double fuel = Smooth(6, 24, temperature) * Smooth(120, 1300, rain) * Smooth(.3, .62, systems) * afternoon;
                double chance = fuel * 2.2 * Math.Max(.3, GameSettings.RainFrequency.Value);
                if (Hashing.Unit(Hashing.Mix(h + (ulong)Math.Floor((gameHours + u3 * period) / period) * 977)) > chance) continue;
                double t = age / life;
                double intensity = Math.Sin(Math.PI * Math.Pow(t, .7)) * (.55 + .45 * u2) * Math.Min(1, fuel * 2.5 + .3);
                double radius = (4500 + 7500 * u1) * (.6 + .4 * Math.Sin(Math.PI * t));
                double d = Math.Sqrt((cx - x) * (cx - x) + (cz - z) * (cz - z));
                if (d - radius * 3 > range) continue;
                into.Add(new Cell(cx, cz, radius, intensity, intensity > .45 && temperature > 6, t));
            }
    }

    private readonly List<Cell> _scratch = new();

    /// <summary>The weather at a world point (sea-level climate; the caller corrects for snow by height).</summary>
    public Sample At(double x, double z, double gameHours, bool withCells = true)
    {
        var (_, rain) = _climate.At(x, z, 0);
        var (systems, band) = Synoptic(x, z, gameHours);
        double threshold = Threshold(rain);
        double humid = Smooth(200, 1600, rain);
        double cloud = Math.Clamp((systems - .3) * 2.2 + band * .6 + .15 * humid + (GameSettings.Clouds.Value - .45) * 1.3, 0, 1);
        double front = Smooth(threshold, threshold + .25, systems + band * .45);
        double precip = front * (.3 + .55 * band + .15 * humid);
        double convective = 0;
        if (withCells)
        {
            Cells(x, z, gameHours, 0, _scratch);
            foreach (var c in _scratch)
            {
                double d2 = ((c.X - x) * (c.X - x) + (c.Z - z) * (c.Z - z)) / (c.Radius * c.Radius);
                double core = Math.Exp(-d2) * c.Intensity;
                convective = Math.Max(convective, core);
                // Anvil and outflow cloud reach ~3 radii.
                cloud = Math.Max(cloud, Math.Exp(-d2 / 9) * Math.Min(1, c.Intensity * 1.6));
            }
            precip = Math.Max(precip, convective * 1.05);
        }
        return new Sample(cloud, Math.Clamp(precip, 0, 1), convective, front);
    }
}
