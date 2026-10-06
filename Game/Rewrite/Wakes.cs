using System;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Wakes (round 15, Noor; docs/RIVERS_PLAN.md step 4): a short trail of disturbances left by whatever stands or moves
/// in the water — the player now, creatures, boats and floating things later. Each disturbance remembers where and when
/// it was made, how strongly (the speed of the object relative to the water: object velocity − local current) and the
/// local current. The water shader (experimental 2) draws each as a ring of ripples that spreads while drifting with
/// the current and fades: a source moving relative to the water lays its rings along its path, whose envelope is a
/// V-shaped wake; standing still in a river the rings are carried downstream into a downstream V; on still water a
/// slow walker leaves near-circular rings. No fluid simulation.
/// </summary>
public sealed class Wakes
{
    public const int Count = 24;
    private readonly Vector4[] _points = new Vector4[Count]; // scene x, z, birth time, strength
    private readonly Vector4[] _flow = new Vector4[Count];   // relative velocity x, z; current x, z
    private int _next;
    private double _sinceLast, _lastX, _lastZ;

    /// <summary>
    /// One frame: emit a disturbance every ~0.15 s (more often when fast) while the source is in the water.
    /// <paramref name="current"/> is the local current at the source (m/s).
    /// </summary>
    public void Update(double delta, double time, Vector3 position, Vector3 velocity, bool inWater, Vector2 current, float size = 1)
    {
        _sinceLast += delta;
        if (!inWater) return;
        var relative = new Vector2(velocity.X, velocity.Z) - current;
        float speed = relative.Length();
        double moved = Math.Sqrt((position.X - _lastX) * (position.X - _lastX) + (position.Z - _lastZ) * (position.Z - _lastZ));
        double interval = speed > 1.5f ? .1 : .16;
        if (_sinceLast < interval && moved < .6) return;
        if (speed < .08f && moved < .05) { if (_sinceLast < .6) return; } // standing in still water: an occasional ring
        _sinceLast = 0; _lastX = position.X; _lastZ = position.Z;
        float strength = Math.Clamp(.12f + speed * .45f, 0, 1.4f) * size;
        _points[_next] = new Vector4(position.X, position.Z, (float)(time % 100000), strength);
        _flow[_next] = new Vector4(relative.X, relative.Y, current.X, current.Y);
        _next = (_next + 1) % Count;
    }

    /// <summary>The scene re-centred: move the disturbances with it.</summary>
    public void Shift(Vector3 shift)
    {
        for (int i = 0; i < Count; i++) _points[i] = new Vector4(_points[i].X + shift.X, _points[i].Y + shift.Z, _points[i].Z, _points[i].W);
        _lastX += shift.X; _lastZ += shift.Z;
    }

    public void Apply(ShaderMaterial water, Vector3 source)
    {
        water.SetShaderParameter("wake_points", _points);
        water.SetShaderParameter("wake_flow", _flow);
        water.SetShaderParameter("wake_centre", new Vector2(source.X, source.Z));
    }

    /// <summary>
    /// River current at a world point (m/s), the way the water mesh computes it: down the water surface's slope, speed
    /// growing with its square root. Zero on lakes, the sea and dry ground.
    /// </summary>
    public static Vector2 CurrentAt(TerrainSampler sampler, double x, double z)
    {
        var t = sampler.Sample(x, z);
        if (t.Water != WaterKind.River) return Vector2.Zero;
        const double d = 1.5;
        double Level(double sx, double sz) { var s = sampler.Sample(sx, sz); return s.Water == WaterKind.River ? s.WaterLevel : t.WaterLevel; }
        double gx = (Level(x + d, z) - Level(x - d, z)) / (2 * d), gz = (Level(x, z + d) - Level(x, z - d)) / (2 * d);
        double g = Math.Sqrt(gx * gx + gz * gz);
        if (g < 1e-6) return Vector2.Zero;
        double speed = Math.Clamp(Math.Sqrt(g) * 6, .3, 3.5);
        return new Vector2((float)(-gx / g * speed), (float)(-gz / g * speed));
    }
}
