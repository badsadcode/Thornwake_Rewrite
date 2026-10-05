using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

public enum BrushShape { Round, RoundHard, Square, Directional }
public enum BrushMode { Dig, Raise, Flatten, Smooth }

/// <summary>Result of one tool use: touched rectangle, volume moved (m³, + added), and how hard the ground was.</summary>
public readonly record struct SculptResult(double MinX, double MinZ, double MaxX, double MaxZ, double Volume, double Hardness)
{
    public bool Changed => Volume != 0;
}

/// <summary>
/// Terrain tools as operations on <see cref="TerrainEdits"/> (engine-free, testable): dig, raise, flatten and smooth
/// with round (soft or hard), square or directional brushes. One call is one tool use; its effect is limited by the
/// tool's strength, by the ground's hardness (rock resists soft tools) and by a volume budget (soil the player can
/// carry or has to place). The directional brush cuts or builds along the aiming ray (a capsule), so the player can
/// dig into a slope or build out from it. The ground is a height field: a directional cut into a slope is an open
/// notch, not a tunnel (caves need the volumetric layer of milestone M10).
/// </summary>
public static class TerrainSculpt
{
    public static SculptResult Apply(TerrainSampler sampler, BrushMode mode, BrushShape shape, double radius, double strength,
        (double X, double Y, double Z) hit, (double X, double Y, double Z) direction, double rockFactor = 1, double maxVolume = double.PositiveInfinity)
    {
        var edits = sampler.Edits;
        // Hardness at the hit: mountains and steep ground are rock (soft tools barely scratch it).
        var t = sampler.Sample(hit.X, hit.Z);
        double gx = sampler.Height(hit.X + 1, hit.Z) - sampler.Height(hit.X - 1, hit.Z);
        double gz = sampler.Height(hit.X, hit.Z + 1) - sampler.Height(hit.X, hit.Z - 1);
        double slope = Math.Sqrt(gx * gx + gz * gz) / 2;
        double hardness = Math.Max(DetMath.Smooth((t.Mountain - .15) / .3), DetMath.Smooth((slope - .55) / .4));
        double effect = strength * DetMath.Lerp(1, rockFactor, hardness);

        double reachOut = shape == BrushShape.Directional ? radius * 2.5 : 0;
        double minX = hit.X - radius - reachOut, maxX = hit.X + radius + reachOut, minZ = hit.Z - radius - reachOut, maxZ = hit.Z + radius + reachOut;
        double dl = Math.Sqrt(direction.X * direction.X + direction.Y * direction.Y + direction.Z * direction.Z);
        var dir = dl > 0 ? (direction.X / dl, direction.Y / dl, direction.Z / dl) : (0.0, -1.0, 0.0);

        // Desired change per edit vertex.
        var changes = new Dictionary<(long, long), double>();
        for (long vz = (long)Math.Floor(minZ); vz <= (long)Math.Ceiling(maxZ); vz++)
            for (long vx = (long)Math.Floor(minX); vx <= (long)Math.Ceiling(maxX); vx++)
            {
                double dx = vx - hit.X, dz = vz - hit.Z, change = 0;
                if (shape == BrushShape.Directional)
                {
                    double h = sampler.Height(vx, vz);
                    // Capsule along the ray: into the ground from the hit when digging, out toward the viewer when building.
                    double sign = mode == BrushMode.Raise ? -1 : 1, best = double.NaN;
                    for (int s = 0; s <= 10; s++)
                    {
                        double u = s / 10.0 * reachOut * sign;
                        double cx = hit.X + dir.Item1 * u, cy = hit.Y + dir.Item2 * u, cz = hit.Z + dir.Item3 * u;
                        double dxz2 = (vx - cx) * (vx - cx) + (vz - cz) * (vz - cz);
                        if (dxz2 >= radius * radius) continue;
                        double half = Math.Sqrt(radius * radius - dxz2);
                        double v = mode == BrushMode.Raise ? cy + half : cy - half;
                        if (double.IsNaN(best) || (mode == BrushMode.Raise ? v > best : v < best)) best = v;
                    }
                    if (double.IsNaN(best)) continue;
                    change = mode == BrushMode.Raise ? Math.Clamp(best - h, 0, effect) : Math.Clamp(best - h, -effect, 0);
                }
                else
                {
                    double r = shape == BrushShape.Square ? Math.Max(Math.Abs(dx), Math.Abs(dz)) / radius : Math.Sqrt(dx * dx + dz * dz) / radius;
                    if (r >= 1) continue;
                    double w = shape == BrushShape.Round ? DetMath.Smooth(1 - r) : 1 - DetMath.Smooth((r - .7) / .3);
                    switch (mode)
                    {
                        case BrushMode.Dig: change = -effect * w; break;
                        case BrushMode.Raise: change = effect * w; break;
                        case BrushMode.Flatten:
                            change = Math.Clamp(hit.Y - sampler.Height(vx, vz), -effect * w, effect * w);
                            break;
                        case BrushMode.Smooth:
                        {
                            double h = sampler.Height(vx, vz), sum = 0;
                            for (int b = -1; b <= 1; b++) for (int a = -1; a <= 1; a++) sum += sampler.Height(vx + a, vz + b);
                            change = Math.Clamp((sum / 9 - h) * .6, -effect * w, effect * w);
                            break;
                        }
                    }
                }
                if (Math.Abs(change) > 1e-5) changes[(vx, vz)] = change;
            }
        double total = 0;
        foreach (var c in changes.Values) total += c;
        double scale = Math.Abs(total) > maxVolume ? maxVolume / Math.Abs(total) : 1;
        double volume = 0;
        if (changes.Count > 0 && scale > 0)
            volume = edits.Modify(minX, minZ, maxX, maxZ, (x, z, current) => changes.TryGetValue((x, z), out var c) ? current + c * scale : current);
        return new SculptResult(minX - 1, minZ - 1, maxX + 1, maxZ + 1, volume, hardness);
    }
}
