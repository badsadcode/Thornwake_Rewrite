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
    /// <summary>A thunderstorm cell. <see cref="Id"/> is the same for every player (grid cell + life period): lightning
    /// derives its strikes from it, so all clients see the same bolts.</summary>
    public readonly record struct Cell(double X, double Z, double Radius, double Intensity, bool Thunder, double Age, ulong Id);

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

    /// <summary>
    /// The surface wind at a world point (round 28, Tom: is the wind the same everywhere?): the steering wind slowed
    /// near the ground and turned by the weather systems (it curves around them, stronger in some, weaker in others),
    /// then the land: faster on ridges and summits, calm in the lee of higher ground upwind, turned along valleys, and
    /// on coasts a sea breeze by day and a land breeze at night. Deterministic (seed, place, game time) like the rest,
    /// so every player computes the same wind. Storm outflow is added by the caller (nearby cells).
    /// </summary>
    public (double X, double Z, double Speed) WindAt(TerrainSampler terrain, double x, double z, double gameHours)
    {
        var (sx, sz, steer) = Steering(gameHours);
        var (dx, dz) = Drift(gameHours, 1);
        double a = Math.Atan2(sz, sx) + .6 * Noise.Value(gameHours / 7, 0, _seed + 31)
            + .9 * Noise.Value((x - dx) / 350000, (z - dz) / 350000, _seed + 32);
        double speed = steer * .45 * (.6 + .4 * (Noise.Value((x - dx) / 280000 + 7.1, (z - dz) / 280000, _seed + 33) + 1));
        double sea = terrain.Plan.Profile.SeaLevel;
        double H(double px, double pz) => Math.Max(terrain.Height(px, pz, 1200), sea);
        double h0 = H(x, z);
        // Exposure: height above the land around (4 km).
        double mean = 0;
        for (int i = 0; i < 8; i++) { double b = i * Math.PI / 4; mean += H(x + Math.Cos(b) * 4000, z + Math.Sin(b) * 4000); }
        mean /= 8;
        double relative = h0 - mean;
        speed *= 1 + .7 * Smooth(0, 900, relative) - .35 * Smooth(0, 600, -relative);
        // Lee: higher ground 3–6 km upwind blocks the wind.
        double ux = Math.Cos(a), uz = Math.Sin(a);
        double upwind = Math.Max(H(x - ux * 3000, z - uz * 3000), H(x - ux * 6000, z - uz * 6000));
        speed *= 1 - .6 * Smooth(0, 800, upwind - h0 - 250);
        // Valleys: the wind turns along the valley (perpendicular to the slope of the land around).
        double gx = (H(x + 2000, z) - H(x - 2000, z)) / 4000, gz = (H(x, z + 2000) - H(x, z - 2000)) / 4000;
        double slope = Math.Sqrt(gx * gx + gz * gz);
        double channel = Smooth(0, 500, -relative) * Smooth(.02, .08, slope) * .7;
        if (channel > 0 && slope > 1e-6)
        {
            double ax = -gz / slope, az = gx / slope;
            if (ax * ux + az * uz < 0) { ax = -ax; az = -az; }
            ux = ux * (1 - channel) + ax * channel; uz = uz * (1 - channel) + az * channel;
            double l = Math.Sqrt(ux * ux + uz * uz); ux /= l; uz /= l;
        }
        double vx = ux * speed, vz = uz * speed;
        // Sea and land breezes: toward the land by day (up to ~3.5 m/s), toward the sea at night (~1.5 m/s).
        double L(double px, double pz) => terrain.Sample(px, pz, 1500).Land > 0 ? 1 : 0;
        double lx = L(x + 3000, z) - L(x - 3000, z), lz = L(x, z + 3000) - L(x, z - 3000);
        double coast = Math.Sqrt(lx * lx + lz * lz);
        if (coast > 0)
        {
            double hour = ((gameHours % 24) + 24) % 24;
            double day = Smooth(9, 13, hour) * Smooth(19, 16, hour), night = Smooth(21, 23, hour) + Smooth(6, 4, hour);
            double breeze = 3.5 * day - 1.5 * Math.Min(1, night);
            vx += lx / coast * breeze; vz += lz / coast * breeze;
        }
        double v = Math.Sqrt(vx * vx + vz * vz);
        return v > 1e-6 ? (vx / v, vz / v, v) : (Math.Cos(a), Math.Sin(a), 0);
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
                ulong life_id = Hashing.Mix(h + (ulong)Math.Floor((gameHours + u3 * period) / period) * 977);
                if (Hashing.Unit(life_id) > chance) continue;
                double t = age / life;
                double intensity = Math.Sin(Math.PI * Math.Pow(t, .7)) * (.55 + .45 * u2) * Math.Min(1, fuel * 2.5 + .3);
                double radius = (4500 + 7500 * u1) * (.6 + .4 * Math.Sin(Math.PI * t));
                double d = Math.Sqrt((cx - x) * (cx - x) + (cz - z) * (cz - z));
                if (d - radius * 3 > range) continue;
                into.Add(new Cell(cx, cz, radius, intensity, intensity > .45 && temperature > 6, t, life_id));
            }
    }

    private readonly List<Cell> _scratch = new();

    /// <summary>The weather at a world point (sea-level climate; the caller corrects for snow by height).</summary>
    public Sample At(double x, double z, double gameHours, bool withCells = true) => At(x, z, gameHours, withCells, _scratch);

    /// <summary>As <see cref="At(double,double,double,bool)"/>, with the caller's own list (thread-safe).</summary>
    public Sample At(double x, double z, double gameHours, bool withCells, List<Cell> scratch)
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
            Cells(x, z, gameHours, 0, scratch);
            foreach (var c in scratch)
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
