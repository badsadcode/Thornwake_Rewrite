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
    private float _cloudTarget = .4f, _precipTarget;

    public void Update(double realSeconds, double gameHours, double annualRain, double temperature, double groundSnow, ulong seed)
    {
        // Fronts: a slow signal over game hours (big systems ~a day, smaller showers ~a few hours).
        double front = .5 + .38 * Noise.Value(gameHours / 16, 1, seed + 21) + .17 * Noise.Value(gameHours / 4.5, 2, seed + 22);
        // Wetter climates: rain at weaker fronts. ~250 mm/yr: only the strongest fronts; ~1800 mm/yr: most of them.
        double threshold = Math.Clamp(1.02 - annualRain / 2600, .42, .97) / GameSettings.RainFrequency.Value;
        double humid = Math.Clamp(annualRain / 1500, 0, 1);
        _cloudTarget = (float)Math.Clamp((front - .25) * 1.5 + .15 * humid, 0, 1);
        double over = front - threshold;
        _precipTarget = over <= 0 ? 0 : (float)Math.Clamp(.25 + over * 3.2, 0, 1);
        Snow = temperature < 1 || (groundSnow > .45 && temperature < 4);
        Thunder = _precipTarget > .78 && temperature > 6;
        // Weather changes visibly but not abruptly: a few real seconds to minutes.
        float rate = (float)realSeconds / 25;
        Cloudiness = Mathf.MoveToward(Cloudiness, Math.Max(_cloudTarget, _precipTarget * .95f + .05f * Math.Sign(_precipTarget)), rate);
        Precipitation = Mathf.MoveToward(Precipitation, _precipTarget, rate * .8f);
    }

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
