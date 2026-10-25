using System;
using Godot;
using Noise = Thornwake.WorldGen.Noise;

namespace Thornwake.Rewrite;

/// <summary>
/// The wind (Tom, round 7; sailing later): a direction that veers slowly over hours of game time and a speed with
/// gusts, scaled by the Wind setting and raised by storms. Drives cloud drift, waves, grass, plants and tree sway and
/// mist drift; shaders read it from the global uniform <c>wind</c> (x, z = direction, y = strength 0..~2, w = gust 0..1).
/// </summary>
public sealed class Wind
{
    /// <summary>Unit vector the wind blows toward (world x, z).</summary>
    public Vector2 Direction { get; private set; } = new(.8f, .6f);
    /// <summary>Wind speed at ~10 m above the ground (m/s), gusts included.</summary>
    public float Speed { get; private set; } = 6;
    public float Gust { get; private set; }
    private double _time;
    /// <summary>The gusts' own clock (real seconds): multiplayer clients take the host's, so the gusts match.</summary>
    public double Time { get => _time; set => _time = value; }

    /// <summary>Advance with game time (hours) and real time; <paramref name="storm"/> 0..1 strengthens and steadies it.</summary>
    public void Update(double realSeconds, double gameHours, float storm, ulong seed)
    {
        _time += realSeconds;
        // Direction: a slow random walk over game hours (prevailing westerly, ±70°).
        double veer = Noise.Value(gameHours / 9, 0, seed + 1) * 1.2 + Noise.Value(gameHours / 2.5, 3, seed + 2) * .35;
        double angle = Math.Atan2(.6, .8) + veer;
        Direction = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle));
        // Speed: calm to fresh over hours, gusts over seconds; storms blow hard.
        double baseSpeed = 5.5 + 4.5 * Noise.Value(gameHours / 6, 7, seed + 3) + storm * 9;
        Gust = (float)Math.Clamp(.5 + .5 * Noise.Value(_time / 4, 11, seed + 4) + .3 * Noise.Value(_time / 1.3, 13, seed + 5), 0, 1);
        Speed = (float)(Math.Max(.5, baseSpeed) * (0.85 + .35 * Gust) * GameSettings.CloudSpeed.Value);
        SteadySpeed = (float)(Math.Max(.5, baseSpeed) * GameSettings.CloudSpeed.Value);
        Forced();
        RenderingServer.GlobalShaderParameterSet("wind_field", Vector4.Zero); // classic sway
        RenderingServer.GlobalShaderParameterSet("wind", new Vector4(Direction.X, Math.Min(2.5f, Speed / 8), Direction.Y, Gust));
    }

    /// <summary>Tests only (<c>--wind=m/s</c>, D190): the wind's speed fixed for captures (gusts still vary). Never a setting.</summary>
    public static float? ForceSpeed { get; set; }
    private void Forced() { if (ForceSpeed is { } f) { SteadySpeed = f; Speed = f * (.85f + .35f * Gust); } }

    /// <summary>The speed without gusts (m/s): what builds the sea and bends the plants.</summary>
    public float SteadySpeed { get; private set; } = 6;

    /// <summary>
    /// Simulated weather (round 27/28): the regional surface wind at the player (WeatherField.WindAt: systems, ridges,
    /// lee, valleys, coastal breezes), plus gust fronts blowing out of nearby storms.
    /// </summary>
    public void UpdateSimulated(double realSeconds, double gameHours, Vector2 steer, float steerSpeed, Vector2 outflowDir,
        float outflowSpeed, float storm, ulong seed)
    {
        _time += realSeconds;
        // (round 28: steer is already the surface wind at the player — WeatherField.WindAt)
        var dir = steer;
        float baseSpeed = (float)(steerSpeed + 1.5 * Noise.Value(gameHours / 5, 7, seed + 3) + storm * 6);
        var v = dir * Math.Max(.5f, baseSpeed) + outflowDir * outflowSpeed;
        float steady = v.Length();
        Direction = Direction.Slerp(steady > .01f ? v / steady : Direction, Math.Clamp((float)realSeconds * .5f, 0, 1));
        // Gusts: stronger and more frequent in storms and near outflow.
        Gust = (float)Math.Clamp(.5 + .5 * Noise.Value(_time / 4, 11, seed + 4) + .3 * Noise.Value(_time / 1.3, 13, seed + 5), 0, 1);
        // (not scaled by the Cloud speed setting: that speeds the clouds up; a Cloud speed of 4 made 63 m/s gales here)
        SteadySpeed = steady;
        Speed = SteadySpeed * (0.85f + (.35f + .25f * storm) * Gust);
        Forced();
        RenderingServer.GlobalShaderParameterSet("wind", new Vector4(Direction.X, Math.Min(2.5f, SteadySpeed / 8), Direction.Y, Gust));
        // Plants (WindSway.gdshaderinc): gusts carried downwind at ~70 % of the wind speed, gustier in storms.
        _gustTravel = (_gustTravel + realSeconds * SteadySpeed * .7) % 100000;
        RenderingServer.GlobalShaderParameterSet("wind_field", new Vector4((float)_gustTravel, Math.Min(3f, SteadySpeed / 8), .55f + .45f * storm, 1));
    }
    private double _gustTravel;

    /// <summary>"from NW" etc. (where the wind comes from).</summary>
    public string Compass()
    {
        // Direction is where it blows to; north = −Z.
        double from = (Mathf.RadToDeg(Math.Atan2(-Direction.X, Direction.Y)) + 360) % 360; // 0 = from north
        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        return names[(int)Math.Round(from / 45) % 8];
    }
}
