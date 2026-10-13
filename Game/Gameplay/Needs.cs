using System;

namespace Thornwake.Gameplay;

/// <summary>What the body feels this moment (the player's own PC measures it: the weather, wetness and fires around).</summary>
/// <param name="Air">Air temperature (°C), from <see cref="Needs.AirTemperature"/>.</param>
/// <param name="Wind">Wind speed (m/s) where the player stands.</param>
/// <param name="Rain">0 dry … 1 heavy rain falling on the player.</param>
/// <param name="Swimming">In water (soaked at once).</param>
/// <param name="Fire">Warmth (°C) from lit fires within reach.</param>
/// <param name="Effort">0 resting … 1 running or chopping (hungrier, warmer).</param>
public readonly record struct BodyInput(double Air, double Wind, double Rain, bool Swimming, double Fire, double Effort);

/// <summary>
/// Needs (GAMEPLAY_DESIGN.md §2, §6; step 1 placeholders, D110): food, body warmth and wetness, and health that suffers
/// when the others run out. All on a 0–100 scale; rates are per game hour and scaled by the sandbox rates (1 = normal).
/// Engine-free: stepped with the game hours that passed and what the body feels.
/// </summary>
public sealed class Needs
{
    public double Food { get; set; } = 80;
    /// <summary>Body warmth: 70 and up comfortable, under 40 cold, under 15 freezing.</summary>
    public double Warmth { get; set; } = 72;
    /// <summary>0 dry … 100 soaked.</summary>
    public double Wet { get; set; }
    public double Health { get; set; } = 100;

    public double HungerRate { get; set; } = 1;
    public double ColdRate { get; set; } = 1;

    /// <summary>The temperature the body feels (°C): the air, wind chill, wetness and fires.</summary>
    public double FeelsLike(in BodyInput b) => b.Air + b.Fire - Math.Min(b.Wind, 20) * .55 - Wet / 100 * 7;

    /// <summary>
    /// Air temperature (°C, placeholder): the place's yearly mean (climate), the season (stronger toward the poles),
    /// the hour (coldest before dawn, warmest mid-afternoon) and rain cooling the air.
    /// </summary>
    /// <param name="season">0 spring, 1 summer, 2 autumn, 3 winter (GameSettings' time.season).</param>
    public static double AirTemperature(double yearlyMean, double latitude, int season, double hour, double precipitation)
    {
        // summer − winter: ~6 °C in the tropics, ~21 °C at 50° (Prague: mean 9 °C, January −1, July 19), 24 °C far north
        double swing = 6 + 18 * Math.Clamp(Math.Abs(latitude) / 60, 0, 1);
        double seasonal = season switch { 1 => .5, 3 => -.5, _ => 0 } * swing * (latitude < 0 ? -1 : 1);
        double daily = 4.5 * Math.Cos((hour - 15) / 24 * Math.Tau);                  // ±4.5 °C, warmest at 15:00
        return yearlyMean + seasonal + daily - 3 * Math.Clamp(precipitation, 0, 1);
    }

    /// <summary>Advance by <paramref name="hours"/> of game time.</summary>
    public void Step(double hours, in BodyInput b)
    {
        if (hours <= 0) return;
        hours = Math.Min(hours, 1); // (a long pause or a jump in time: at most an hour at once)
        // Food: about two game days from full to empty at rest, faster when working hard.
        Food = Math.Max(0, Food - hours * 2 * (1 + .6 * b.Effort) * HungerRate);
        // Wet: soaked in water, wetter in rain, drying in the air (faster near a fire and when warm).
        double dry = 6 + Math.Max(0, FeelsLike(b) - 10) * .8 + b.Fire * 4;
        Wet = b.Swimming ? 100 : Math.Clamp(Wet + hours * (b.Rain * 60 - dry), 0, 100);
        // Warmth follows what the body feels: comfortable from ~16 °C, losing heat below; working warms a little.
        double feels = FeelsLike(b) + 6 * b.Effort;
        double target = Math.Clamp(72 + (feels - 16) * 3.2, 0, 85);
        double rate = target < Warmth ? 18 * ColdRate : 25 + b.Fire * 2;
        Warmth = Warmth + Math.Clamp(target - Warmth, -rate * hours, rate * hours);
        // Health: starving or freezing hurts; fed and warm, it comes back slowly.
        double harm = (Food <= 0 ? 4 : 0) + (Warmth < 25 ? (25 - Warmth) / 25 * 12 : 0);
        double heal = Food > 40 && Warmth > 50 ? 2.5 : 0;
        Health = Math.Clamp(Health + hours * (heal - harm), 0, 100);
    }

    public void Eat(double food) => Food = Math.Min(100, Food + food);

    public string Describe() =>
        $"Food {Food:0} · Warmth {Warmth:0}{(Warmth < 25 ? " (freezing)" : Warmth < 45 ? " (cold)" : "")}" +
        (Wet > 10 ? $" · Wet {Wet:0}" : "") + $" · Health {Health:0}";
}
