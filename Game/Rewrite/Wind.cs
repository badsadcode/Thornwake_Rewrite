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
        RenderingServer.GlobalShaderParameterSet("wind", new Vector4(Direction.X, Math.Min(2.5f, Speed / 8), Direction.Y, Gust));
    }

    /// <summary>"from NW" etc. (where the wind comes from).</summary>
    public string Compass()
    {
        // Direction is where it blows to; north = −Z.
        double from = (Mathf.RadToDeg(Math.Atan2(-Direction.X, Direction.Y)) + 360) % 360; // 0 = from north
        string[] names = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        return names[(int)Math.Round(from / 45) % 8];
    }
}
