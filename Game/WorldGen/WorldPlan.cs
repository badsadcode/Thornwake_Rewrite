using System;
using System.Diagnostics;

namespace Thornwake.WorldGen;

/// <summary>
/// Everything about a world that is computed once: the layout and the whole-world macro rasters.
/// Immutable after construction and safe to share between threads. Deterministic: the same manifest always
/// gives bit-identical rasters, independent of thread count and platform.
/// </summary>
public sealed class WorldPlan
{
    /// <summary>Raster channels: A4 land influence; eroded macro height (metres above sea level, negative at sea);
    /// mountain share 0..1 of profile mountain height.</summary>
    public const int LandChannel = 0, HeightChannel = 1, MountainChannel = 2;
    public WorldManifest Manifest { get; }
    public WorldProfile Profile => Manifest.Profile;
    public TerrainStyle Style { get; }
    public WorldLayout Layout { get; }
    public MacroRaster Macro { get; }
    public Hydrology Hydrology { get; }
    public double BuildMilliseconds { get; }
    /// <summary>Time spent on the layout raster alone (before erosion/hydrology).</summary>
    public double RasterMilliseconds { get; }

    /// <summary>Raster resolution: cells of at most ~750 m, between 512 and 2048 cells across.</summary>
    public static int RasterSizeFor(WorldProfile profile)
    {
        int size = (int)Math.Ceiling(profile.WorldSize / 750 / 256) * 256;
        return Math.Clamp(size, 512, 2048);
    }

    public WorldPlan(WorldManifest manifest, int? rasterSize = null, TerrainStyle? style = null)
    {
        var timer = Stopwatch.StartNew();
        Manifest = manifest;
        Layout = new WorldLayout(manifest);
        Style = style ?? TerrainStyle.For(manifest.Profile);
        int size = rasterSize ?? RasterSizeFor(manifest.Profile);
        double world = manifest.Profile.WorldSize, cell = world / size;
        Macro = new MacroRaster(size, manifest.OriginX - world * .5, manifest.OriginZ - world * .5, cell, 3);
        var layout = Layout;
        var p = manifest.Profile;
        double ox = manifest.OriginX, oz = manifest.OriginZ;
        Macro.Fill((i, j, channels) =>
        {
            double x = Macro.CellX(i), z = Macro.CellZ(j), lx = x - ox, lz = z - oz;
            int k = j * size + i;
            double land = layout.LandInfluence(x, z);
            channels[LandChannel][k] = (float)land;
            if (land < -.3) return; // deep sea: relief is never used
            double hs = p.HillScale;
            double hills = (Noise.Value(lx / hs, lz / hs, layout.ReliefNoise) * .7 +
                Noise.Value(lx / (hs * .35), lz / (hs * .35), layout.ReliefNoise + 1) * .3) * p.HillHeight;
            double mountain = 0;
            layout.MountainEnvelope(lx, lz, out double core, out double shoulder);
            if (core > 1e-6 || shoulder > 1e-6)
            {
                // A4's finest relief octave (n3, ~0.16 mountain widths) is replaced by its mean here; the sampler's
                // ridged rock detail supersedes it at walking scale.
                layout.MountainWarp(lx, lz, out double wx, out double wz);
                double rugged = layout.RuggedBroad(wx, wz) * .92;
                mountain = Math.Min(1, core * (.28 + .72 * rugged) + shoulder * (.08 + .24 * rugged));
            }
            channels[HeightChannel][k] = (float)(hills + mountain * p.MountainHeight); // relief; converted below
            channels[MountainChannel][k] = (float)mountain;
        });
        RasterMilliseconds = timer.Elapsed.TotalMilliseconds;

        // Macro height above sea level: the A4 lowland ramp from the coast plus faded relief; sea floor below 0.
        var landRaster = Macro.Channel(LandChannel); var heightRaster = Macro.Channel(HeightChannel);
        var height = new double[size * size];
        for (int k = 0; k < height.Length; k++)
        {
            double land = landRaster[k];
            if (land <= 0) { height[k] = -p.OceanDepth * DetMath.Smooth(-land / .6); continue; }
            double h = p.LowlandHeight * DetMath.Smooth(land / .4) + DetMath.Smooth(land / .18) * heightRaster[k];
            h = Math.Max(p.LowlandHeight * DetMath.Smooth(land / .08) * .05, h);
            height[k] = Math.Max(h, .5); // every land cell stays above the sea for routing
        }
        var hydroTimer = Stopwatch.StartNew();
        ulong riverSeed = Hashing.FeatureSeed(manifest.Seed, 100 + WorldManifest.CurrentGenerator, manifest.ProfileDigest, "rivers");
        Hydrology = new Hydrology(Macro, height, p, Style, riverSeed);
        Hydrology.BuildMilliseconds = hydroTimer.Elapsed.TotalMilliseconds;
        for (int k = 0; k < height.Length; k++) heightRaster[k] = (float)height[k];
        BuildMilliseconds = timer.Elapsed.TotalMilliseconds;
    }
}
