using System;
using System.Diagnostics;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Development overlays only. Raster cells and lines are not river/lake geometry.</summary>
public static class MacroDrainageMap
{
    public static MacroMapResult Build(MacroWorldGenerator world, DrainageAnalysis drainage, double x, double z,
        double span, int resolution, MacroMapLayer layer, CancellationToken cancellation = default)
    {
        if (layer is not (MacroMapLayer.DrainageRoutes or MacroMapLayer.DrainageCatchments or MacroMapLayer.DrainageBasins))
            throw new ArgumentOutOfRangeException(nameof(layer));
        var watch = Stopwatch.StartNew();
        var map = MacroWorldMap.Build(world, x, z, span, resolution, MacroMapLayer.Elevation, cancellation);
        var pixels = map.Pixels;
        double cellArea = drainage.Grid.Spacing * drainage.Grid.Spacing;
        void Paint(int px, int pz, byte r, byte g, byte b, double alpha = 1)
        {
            if (px < 0 || pz < 0 || px >= resolution || pz >= resolution) return;
            int offset = (pz * resolution + px) * 4;
            pixels[offset] = (byte)(pixels[offset] * (1 - alpha) + r * alpha);
            pixels[offset + 1] = (byte)(pixels[offset + 1] * (1 - alpha) + g * alpha);
            pixels[offset + 2] = (byte)(pixels[offset + 2] * (1 - alpha) + b * alpha);
        }
        (double X, double Z) Pixel(MacroPoint p) => ((p.X - x) / span * resolution + resolution * .5, (p.Z - z) / span * resolution + resolution * .5);
        // Clip before rasterizing so extreme zoom never causes unbounded line iteration.
        void Line((double X, double Z) a, (double X, double Z) b, byte r, byte g, byte blue)
        {
            double dx = b.X - a.X, dz = b.Z - a.Z, lo = 0, hi = 1;
            bool Clip(double p, double q)
            {
                if (p == 0) return q >= 0;
                double t = q / p;
                if (p < 0) { if (t > hi) return false; lo = Math.Max(lo, t); }
                else { if (t < lo) return false; hi = Math.Min(hi, t); }
                return true;
            }
            if (!Clip(-dx, a.X) || !Clip(dx, resolution - 1 - a.X) || !Clip(-dz, a.Z) || !Clip(dz, resolution - 1 - a.Z)) return;
            double ax = a.X + dx * lo, az = a.Z + dz * lo, bx = a.X + dx * hi, bz = a.Z + dz * hi;
            int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(bx - ax), Math.Abs(bz - az))));
            for (int i = 0; i <= steps; i++) Paint((int)Math.Round(ax + (bx - ax) * i / steps), (int)Math.Round(az + (bz - az) * i / steps), r, g, blue);
        }
        void Mark(MacroPoint p, byte r, byte g, byte b)
        {
            var at = Pixel(p);
            if (at.X < -3 || at.Z < -3 || at.X > resolution + 3 || at.Z > resolution + 3) return;
            int px = (int)Math.Floor(at.X), pz = (int)Math.Floor(at.Z);
            for (int dz = -1; dz <= 1; dz++) for (int dx = -1; dx <= 1; dx++) Paint(px + dx, pz + dz, r, g, b);
        }
        for (int pz = 0; pz < resolution; pz++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int px = 0; px < resolution; px++)
            {
                if (!drainage.Grid.TryCell(x + ((px + .5) / resolution - .5) * span, z + ((pz + .5) / resolution - .5) * span, out int cell)) continue;
                if (drainage.Downstream[cell] < 0) continue;
                if (layer == MacroMapLayer.DrainageCatchments)
                {
                    uint h = unchecked((uint)drainage.OutletCell[cell] * 2654435761u);
                    Paint(px, pz, (byte)(70 + h % 170), (byte)(70 + (h >> 8) % 170), (byte)(70 + (h >> 16) % 170), .65);
                }
                else if (layer == MacroMapLayer.DrainageBasins && drainage.BasinIndex[cell] >= 0)
                    Paint(px, pz, (byte)(100 + drainage.BasinIndex[cell] * 37 % 100), 95, 225, .75);
                else if (layer == MacroMapLayer.DrainageRoutes)
                    Paint(px, pz, 35, 195, 240, Math.Clamp(Math.Log2(drainage.CatchmentArea[cell] / cellArea + 1) / 8, .08, .8));
            }
        }
        for (int cell = 0; cell < drainage.Grid.Count; cell++)
        {
            if ((cell & 255) == 0) cancellation.ThrowIfCancellationRequested();
            int next = drainage.Downstream[cell];
            if (next < 0)
            {
                if (drainage.CatchmentArea[cell] > cellArea) Mark(drainage.Grid.Center(cell), 70, 245, 255);
                continue;
            }
            bool inBasin = drainage.BasinIndex[cell] >= 0 && drainage.BasinIndex[cell] == drainage.BasinIndex[next];
            if (inBasin || drainage.CatchmentArea[cell] < 4 * cellArea) continue;
            var a = Pixel(drainage.Grid.Center(cell)); var b = Pixel(drainage.Grid.Center(next));
            Line(a, b, 245, 222, 120);
            double dx = b.X - a.X, dz = b.Z - a.Z, length = Math.Sqrt(dx * dx + dz * dz);
            if (length >= 9)
            {
                var tip = (X: a.X + dx * .7, Z: a.Z + dz * .7);
                Line(tip, (tip.X - dx / length * 4 + dz / length * 3, tip.Z - dz / length * 4 - dx / length * 3), 245, 222, 120);
                Line(tip, (tip.X - dx / length * 4 - dz / length * 3, tip.Z - dz / length * 4 + dx / length * 3), 245, 222, 120);
            }
        }
        if (layer == MacroMapLayer.DrainageBasins)
            foreach (var basin in drainage.Basins)
                foreach (var spill in basin.Exits) Mark(drainage.Grid.Center(spill.From), 255, 115, 160);
        return map with { Milliseconds = watch.Elapsed.TotalMilliseconds };
    }
}
