using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace Thornwake.Core;

public static class RegionalDrainageMap
{
    /// <summary>Fixed one-tile diagnostic view; red segments expose uphill sampled terrain, not flowing water.</summary>
    public static MacroMapResult Build(MacroWorldGenerator world, RegionalDrainageTile tile, int resolution, CancellationToken cancellation = default)
    {
        var watch = Stopwatch.StartNew();
        var map = MacroWorldMap.Build(world, tile.Min.X + tile.Span * .5, tile.Min.Z + tile.Span * .5, tile.Span, resolution,
            MacroMapLayer.Elevation, cancellation);
        void Paint(int x, int z, byte r, byte g, byte b, double alpha = 1)
        {
            x = Math.Clamp(x, 0, resolution - 1); z = Math.Clamp(z, 0, resolution - 1);
            int i = (z * resolution + x) * 4;
            map.Pixels[i] = (byte)(map.Pixels[i] * (1 - alpha) + r * alpha);
            map.Pixels[i + 1] = (byte)(map.Pixels[i + 1] * (1 - alpha) + g * alpha);
            map.Pixels[i + 2] = (byte)(map.Pixels[i + 2] * (1 - alpha) + b * alpha);
        }
        var wet = new HashSet<(int X, int Z)>();
        if (tile.Lake != null) foreach (var cell in tile.Lake.InteriorCells) wet.Add((cell.X, cell.Z));
        for (int z = 0; z < resolution; z++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < resolution; x++)
                if (wet.Contains(((int)((x + .5) / resolution * (tile.Side - 1)), (int)((z + .5) / resolution * (tile.Side - 1)))))
                    Paint(x, z, 147, 110, 224, .6);
        }
        foreach (var link in tile.Links)
            {
                cancellation.ThrowIfCancellationRequested();
                var a = link.From; var b = link.To;
                double ax = (a.X - tile.Min.X) / tile.Span * resolution, az = (a.Z - tile.Min.Z) / tile.Span * resolution;
                double bx = (b.X - tile.Min.X) / tile.Span * resolution, bz = (b.Z - tile.Min.Z) / tile.Span * resolution;
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(bz - az))));
                bool uphill = b.BedElevation > a.BedElevation;
                for (int k = 0; k <= steps; k++)
                    Paint((int)Math.Round(ax + (bx - ax) * k / steps), (int)Math.Round(az + (bz - az) * k / steps), 255, (byte)(uphill ? 75 : 226), (byte)(uphill ? 85 : 100));
            }
        void Port(DrainagePort port, byte r, byte g, byte b)
        {
            int x = (int)Math.Round((port.Position.X - tile.Min.X) / tile.Span * resolution);
            int z = (int)Math.Round((port.Position.Z - tile.Min.Z) / tile.Span * resolution);
            for (int dz = -3; dz <= 3; dz++) for (int dx = -3; dx <= 3; dx++) Paint(x + dx, z + dz, r, g, b);
        }
        foreach (var port in tile.Incoming) Port(port, 80, 255, 170);
        if (tile.Outgoing.HasValue) Port(tile.Outgoing.Value, 75, 235, 255);
        return map with { Milliseconds = watch.Elapsed.TotalMilliseconds };
    }
}

