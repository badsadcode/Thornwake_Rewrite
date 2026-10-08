using System;
using Godot;
using Noise = Thornwake.WorldGen.Noise;

namespace Thornwake.Rewrite;

/// <summary>
/// Simulated weather (Tom, round 7): weather fronts pass every several game hours (so the pace follows the day length,
/// never flipping every minute); how often they bring rain, and how hard, comes from the local climate — deserts stay
/// dry, rainforests get frequent downpours. Strong fronts become storms (gusts, lightning). Where it is freezing, or
/// the ground is snowy, it snows instead of raining.
/// </summary>
public sealed class Weather
{
    /// <summary>0 clear … 1 overcast.</summary>
    public float Cloudiness { get; private set; } = .4f;
    /// <summary>0 none, ~0.3 light, ~0.6 heavy, ~0.9 storm.</summary>
    public float Precipitation { get; private set; }
    public bool Snow { get; private set; }
    public bool Thunder { get; private set; }
    /// <summary>
    /// Cloud types (round 17, docs/SKY_IDEAS.md item 3), each 0..1, changing as slowly as the weather:
    /// <see cref="Cumulus"/> — towers building on warm, humid afternoons; <see cref="DistantStorm"/> — a thunderstorm
    /// on the horizon (a storm front a few hours away, or an afternoon storm in a hot, wet climate), on the upwind side
    /// when it is coming (<see cref="StormComing"/>), downwind when it has passed; <see cref="Mackerel"/> — rows of
    /// small high cloudlets as a rain front approaches.
    /// </summary>
    public float Cumulus { get; private set; }
    public float DistantStorm { get; private set; }
    public bool StormComing { get; private set; } = true;
    public float Mackerel { get; private set; }
    /// <summary>
    /// The next rain front (round 19, Noor: "sailing toward a giant wall of rain"): game hours until its rain reaches
    /// here (NaN: none within 8 h, or it is already raining) and how strong it is (0..1; > ~0.75 thunderstorms).
    /// </summary>
    public double FrontHours { get; private set; } = double.NaN;
    public float FrontStrength { get; private set; }
    private float _cloudTarget = .4f, _precipTarget;

    public void Update(double realSeconds, double gameHours, double annualRain, double temperature, double groundSnow, ulong seed)
    {
        // Fronts: a slow signal over game hours (big systems ~a day, smaller showers ~a few hours).
        double front = .5 + .38 * Noise.Value(gameHours / 16, 1, seed + 21) + .17 * Noise.Value(gameHours / 4.5, 2, seed + 22);
        // Wetter climates: rain at weaker fronts. ~250 mm/yr: only the strongest fronts; ~1800 mm/yr: most of them.
        double threshold = Math.Clamp(1.02 - annualRain / 2600, .42, .97) / GameSettings.RainFrequency.Value;
        double humid = Math.Clamp(annualRain / 1500, 0, 1);
        // The Cloud cover setting biases the weather: 0.45 (default) as the climate says, lower = mostly clear,
        // higher = mostly grey.
        double bias = (GameSettings.Clouds.Value - .45) * 1.3;
        _cloudTarget = (float)Math.Clamp((front - .25) * 1.5 + .15 * humid + bias, 0, 1);
        double over = front - threshold;
        _precipTarget = over <= 0 ? 0 : (float)Math.Clamp(.25 + over * 3.2, 0, 1);
        Snow = temperature < 1 || (groundSnow > .45 && temperature < 4);
        Thunder = _precipTarget > .78 && temperature > 6;
        // Weather changes visibly but not abruptly: a few real seconds to minutes.
        float rate = (float)realSeconds / 25;
        Cloudiness = Mathf.MoveToward(Cloudiness, Math.Max(_cloudTarget, _precipTarget * .95f + .05f * Math.Sign(_precipTarget)), rate);
        Precipitation = Mathf.MoveToward(Precipitation, _precipTarget, rate * .8f);

        // Cloud types. Fronts a few hours ahead / behind: the same signal at another time.
        double Front(double h) => .5 + .38 * Noise.Value(h / 16, 1, seed + 21) + .17 * Noise.Value(h / 4.5, 2, seed + 22);
        double hour = ((gameHours % 24) + 24) % 24;
        bool warm = temperature > 8 && !Snow;
        // Afternoon convection: builds from late morning, tallest mid-afternoon, collapses by evening; needs warmth
        // and moisture, and broken (not overcast) skies.
        double afternoon = SmoothStep(10, 14, hour) * SmoothStep(20, 16.5, hour);
        double heat = SmoothStep(10, 24, temperature);
        double cumulus = warm ? afternoon * heat * (.35 + .65 * humid) * SmoothStep(.95, .6, Cloudiness) * (1 - Math.Min(1, Precipitation * 3)) : 0;
        // A storm front 2–6 h away (ahead: coming, upwind) or 2–5 h gone (behind: downwind).
        double ahead = 0, behind = 0;
        for (double dh = 2; dh <= 6; dh += 1) ahead = Math.Max(ahead, Front(gameHours + dh) - threshold);
        for (double dh = 2; dh <= 5; dh += 1) behind = Math.Max(behind, Front(gameHours - dh) - threshold);
        double stormAhead = SmoothStep(.12, .28, ahead), stormBehind = SmoothStep(.12, .28, behind) * .8;
        // Hot, wet afternoons breed isolated storms somewhere on the horizon (not every day).
        double dayHash = Fract(Math.Sin(Math.Floor(gameHours / 24) * 78.233 + seed % 1000) * 43758.5453);
        double airMass = cumulus * SmoothStep(18, 28, temperature) * SmoothStep(700, 1600, annualRain) * (dayHash < .55 ? 1 : 0);
        double distant = warm ? Math.Max(Math.Max(stormAhead, stormBehind), airMass) : 0;
        distant *= 1 - Math.Min(1, Precipitation * 2.5); // in the rain the horizon is hidden
        if (stormAhead >= stormBehind) StormComing = true; else if (stormBehind > .05) StormComing = false;
        // Mackerel sky: rain coming within ~3–9 h, still dry here.
        double rainAhead = 0;
        for (double dh = 3; dh <= 9; dh += 2) rainAhead = Math.Max(rainAhead, Front(gameHours + dh) - threshold);
        double mackerel = SmoothStep(-.02, .12, rainAhead) * (1 - Math.Min(1, Precipitation * 4)) * SmoothStep(.9, .5, Cloudiness);
        // The first time in the next 8 h the front brings rain here (the rain edge, with its strength there).
        FrontHours = double.NaN;
        double strength = 0;
        if (Precipitation < .05f && !Snow)
            for (double dh = .25; dh <= 8; dh += .25)
            {
                double over2 = Front(gameHours + dh) - threshold;
                if (over2 <= 0) continue;
                FrontHours = dh;
                for (double k = dh; k <= dh + 3; k += .5) strength = Math.Max(strength, Front(gameHours + k) - threshold);
                break;
            }
        FrontStrength = Mathf.MoveToward(FrontStrength, double.IsNaN(FrontHours) ? 0 : (float)Math.Clamp(.35 + strength * 3.2, 0, 1), (float)realSeconds / 40);
        float slow = (float)realSeconds / 40;
        Cumulus = Mathf.MoveToward(Cumulus, (float)cumulus, slow);
        DistantStorm = Mathf.MoveToward(DistantStorm, (float)distant, slow);
        Mackerel = Mathf.MoveToward(Mackerel, (float)mackerel, slow);
    }

    /// <summary>Thunderstorm cells near the player (simulated model): lightning and the sky read them.</summary>
    public readonly System.Collections.Generic.List<WeatherField.Cell> NearCells = new();
    private double _forecastTimer;

    /// <summary>
    /// Simulated model (round 27): the weather here is the world field at the player — clouds and rain from the
    /// systems, fronts and thunderstorm cells passing over — with the same easing as the classic model. Distant storms
    /// and the mackerel sky come from real cells and the field ahead, not from the clock.
    /// </summary>
    public void UpdateFromField(WeatherField field, double realSeconds, double gameHours, double x, double z, double temperature,
        double groundSnow, bool settleNow)
    {
        var here = field.At(x, z, gameHours);
        field.Cells(x, z, gameHours, 70000, NearCells);
        _cloudTarget = (float)here.Cloud;
        _precipTarget = (float)here.Precip;
        Snow = temperature < 1 || (groundSnow > .45 && temperature < 4);
        Thunder = here.Convective > .35 && temperature > 6 && !Snow;
        float rate = settleNow ? 1 : (float)realSeconds / 25;
        Cloudiness = Mathf.MoveToward(Cloudiness, Math.Max(_cloudTarget, _precipTarget * .95f + .05f * Math.Sign(_precipTarget)), rate);
        Precipitation = Mathf.MoveToward(Precipitation, _precipTarget, rate * .8f);

        // Distant storms: the strongest cell 8–70 km away; coming when it lies upwind (the weather drifts toward us).
        var (sx, sz, _) = field.Steering(gameHours);
        double distant = 0, side = 0;
        foreach (var c in NearCells)
        {
            double ox = c.X - x, oz = c.Z - z, d = Math.Sqrt(ox * ox + oz * oz);
            double w = c.Intensity * SmoothStep(4000, 9000, d - c.Radius) * SmoothStep(70000, 35000, d);
            if (w > distant) { distant = w; side = -(ox * sx + oz * sz); }
        }
        distant *= 1 - Math.Min(1, Precipitation * 2.5);
        if (distant > .05) StormComing = side > 0;

        // Forecast (every few real seconds): when rain arrives, and the mackerel sky ahead of it.
        _forecastTimer -= realSeconds;
        double mackerel = Mackerel;
        if (_forecastTimer <= 0 || settleNow)
        {
            _forecastTimer = 3;
            FrontHours = double.NaN;
            double strength = 0, ahead = 0;
            if (Precipitation < .05f && !Snow)
                for (double dh = .25; dh <= 8; dh += .25)
                {
                    double p = field.At(x, z, gameHours + dh, withCells: dh < 3).Precip;
                    if (dh >= 3) ahead = Math.Max(ahead, p);
                    if (p <= .08 || !double.IsNaN(FrontHours)) continue;
                    FrontHours = dh;
                    strength = p;
                }
            FrontStrength = (float)Math.Clamp(strength * 1.2, 0, 1);
            mackerel = SmoothStep(.05, .3, ahead) * (1 - Math.Min(1, Precipitation * 4)) * SmoothStep(.9, .5, Cloudiness);
        }

        // Afternoon towers wherever cells are building, and on warm humid afternoons in general.
        double hour = ((gameHours % 24) + 24) % 24;
        double building = 0;
        foreach (var c in NearCells) building = Math.Max(building, c.Age < .4 ? c.Intensity : 0);
        double cumulus = temperature > 8 && !Snow
            ? Math.Max(building, SmoothStep(10, 14, hour) * SmoothStep(20, 16.5, hour) * SmoothStep(12, 26, temperature) * .6)
              * SmoothStep(.95, .6, Cloudiness) * (1 - Math.Min(1, Precipitation * 3)) : 0;
        float slow = settleNow ? 1 : (float)realSeconds / 40;
        Cumulus = Mathf.MoveToward(Cumulus, (float)cumulus, slow);
        DistantStorm = Mathf.MoveToward(DistantStorm, (float)distant, slow);
        Mackerel = Mathf.MoveToward(Mackerel, (float)mackerel, slow);
    }

    private static double SmoothStep(double a, double b, double x)
    {
        double t = Math.Clamp((x - a) / (b - a), 0, 1);
        return t * t * (3 - 2 * t);
    }
    private static double Fract(double x) => x - Math.Floor(x);

    public string Describe()
    {
        if (Precipitation > .02f)
        {
            string kind = Snow ? (Precipitation > .75f ? "blizzard" : Precipitation > .45f ? "heavy snow" : "snow")
                : Thunder ? "thunderstorm" : Precipitation > .55f ? "heavy rain" : Precipitation > .25f ? "rain" : "drizzle";
            return kind;
        }
        return Cloudiness > .8f ? "overcast" : Cloudiness > .5f ? "cloudy" : Cloudiness > .25f ? "partly cloudy" : "clear";
    }
}
