using System;
using System.Threading.Tasks;

namespace Thornwake.WorldGen;

/// <summary>Terrain facts at one position.</summary>
public readonly record struct TerrainSample(
    double Height,       // metres, absolute (sea level = profile SeaLevel)
    double Land,         // land influence after coast detail: > 0 land, ≤ 0 sea
    double Mountain,     // 0..1 share of profile mountain height
    double Detail,       // metres of walking-scale relief included in Height
    WaterKind Water,     // water body covering this point (None if dry)
    double WaterLevel,   // absolute surface elevation of that water (NaN if dry)
    int WaterBody);      // lake id or river reach id (-1 for sea / none)

/// <summary>
/// The terrain authority for generated (unedited) ground: a pure, thread-safe function of world position.
/// <para>height = macro raster (A4 land influence, hills + mountain relief, mountain share) + sub-raster coast
/// detail + walking-scale eroded detail (lowland) and ridged rock detail (mountains).</para>
/// <para><c>minWavelength</c> is the LOD knob: detail octaves shorter than it are skipped (and the last one
/// faded), so a coarse distant tile is the near terrain minus small bumps. Use about twice the vertex spacing.</para>
/// </summary>
public sealed class TerrainSampler
{
    public WorldPlan Plan { get; }
    private readonly WorldProfile _p;
    private readonly TerrainStyle _style;
    private readonly MacroRaster _macro;
    private readonly Hydrology _hydrology;
    private readonly double _ox, _oz;
    private readonly ulong _detailSeed, _rockSeed, _coastSeed, _regionSeed;
    private readonly double _coastBand;

    public TerrainSampler(WorldPlan plan)
    {
        Plan = plan; _p = plan.Profile; _style = plan.Style; _macro = plan.Macro; _hydrology = plan.Hydrology;
        _ox = plan.Manifest.OriginX; _oz = plan.Manifest.OriginZ;
        var m = plan.Manifest;
        ulong Seed(string layer) => Hashing.FeatureSeed(m.Seed, 100 + WorldManifest.CurrentGenerator, m.ProfileDigest, layer);
        _detailSeed = Seed("detail"); _rockSeed = Seed("rock"); _coastSeed = Seed("coast"); _regionSeed = Seed("region");
        // Coast detail is only evaluated where it can change the land/sea decision or the shore profile.
        _coastBand = .25;
    }

    public double Height(double x, double z, double minWavelength = 2) => Sample(x, z, minWavelength).Height;

    public TerrainSample Sample(double x, double z, double minWavelength = 2)
    {
        _macro.Sample(x, z, WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out double land, out double macroHeight, out double mountainShare);
        double rasterLand = land;
        double lx = x - _ox, lz = z - _oz;

        // Sub-raster coastline: shift the coast sideways by a fractal distance (metres), converted to land
        // influence through the local gradient so continents and small islands get the same metric wiggle.
        if (land > -_coastBand && land < _coastBand)
        {
            _macro.SampleGradient(WorldPlan.LandChannel, x, z, out double gx, out double gz);
            double gradient = Math.Sqrt(gx * gx + gz * gz);
            double shift = _style.CoastDetailShift * Noise.Fbm(lx, lz, _macro.Cell * 2.5, 7, minWavelength, _coastSeed, .55);
            land += shift * gradient;
        }

        if (land <= 0)
        {
            double depth = _p.OceanDepth * DetMath.Smooth(-land / .6);
            return new(_p.SeaLevel - depth, land, 0, 0, WaterKind.Ocean, _p.SeaLevel, -1);
        }

        // Eroded macro height; the coastal ramp follows the detailed coastline rather than the raster's.
        double baseHeight = macroHeight + _p.LowlandHeight * (DetMath.Smooth(land / .4) - DetMath.Smooth(rasterLand / .4));
        baseHeight = Math.Max(_p.LowlandHeight * DetMath.Smooth(land / .08) * .05, baseHeight);
        mountainShare = DetMath.Clamp(mountainShare, 0, 1);

        // Walking-scale relief. Fades out at the shoreline so beaches and shelves stay smooth.
        double inland = DetMath.Smooth(land / .03);
        double detail = 0;
        if (inland > 0)
        {
            // Regional character: broad plains in some places, rougher hill country in others (Supplement §3),
            // varying over tens of kilometres so it never reads as a repeating texture.
            double character = DetMath.Smooth(.5 + .9 * Noise.Fbm(lx, lz, _style.RegionWavelength, 3, minWavelength, _regionSeed));
            double roughness = _style.PlainsRoughness + (1 - _style.PlainsRoughness) * character;
            double rolling = Noise.ErodedFbm(lx, lz, _style.DetailWavelength, 11, minWavelength, _detailSeed, _style.DetailGain);
            detail = rolling * _style.LowlandDetail * roughness * (1 + 2 * mountainShare);
            if (mountainShare > .02)
            {
                double ridges = Noise.Ridged(lx, lz, 900, 8, minWavelength, _rockSeed, .5) - .3;
                detail += ridges * _style.MountainDetail * _p.MountainHeight * DetMath.Smooth(mountainShare / .6);
            }
            detail *= inland;
        }
        // Rivers: channel, banks and floodplain replace the terrain near the river; detail fades toward it.
        var water = WaterKind.None; double level = double.NaN; int body = -1;
        double height;
        if (_hydrology.NearestRiver(x, z, 0, out var river))
        {
            double half = river.Width * .5, bank = Math.Max(8, river.Width * .8);
            double d = river.Distance, surface = river.Surface - _p.SeaLevel;
            double bankTop = surface + .6 + .15 * river.Depth;
            double rough = Math.Max(baseHeight, .2 * inland + .05);
            double flood = Hydrology.Floodplain(river.Width, rough - bankTop);
            double terrain = Math.Max(baseHeight + detail * DetMath.Smooth((d - half) / (bank + flood)), .2 * inland + .05);
            if (d < half)
            {
                double u = d / half;
                height = surface - river.Depth * (1 - u * u);
                water = WaterKind.River; level = river.Surface; body = river.Reach;
            }
            else if (d < half + bank) height = Math.Max(surface + (bankTop - surface) * DetMath.Smooth((d - half) / bank),
                Math.Min(terrain, bankTop));
            else height = bankTop + (terrain - bankTop) * DetMath.Smooth((d - half - bank) / flood);
        }
        else height = Math.Max(baseHeight + detail, .2 * inland + .05); // generated land never dips below the sea

        if (water == WaterKind.None)
        {
            int lake = _hydrology.LakeNear(x, z);
            if (lake >= 0 && _p.SeaLevel + height < _hydrology.Lakes[lake].Level)
            { water = WaterKind.Lake; level = _hydrology.Lakes[lake].Level; body = lake; }
        }
        return new(_p.SeaLevel + height, land, mountainShare, detail, water, level, body);
    }

    /// <summary>Terrain without walking-scale detail (macro shape only), for comparisons and maps.</summary>
    public double MacroHeight(double x, double z) => Sample(x, z, double.PositiveInfinity).Height;

    /// <summary>
    /// Fill a regular grid (row-major, <paramref name="nx"/> fastest) with heights. Each value is bit-identical
    /// to <see cref="Height"/> at that point, so neighbouring tiles always share exact edges.
    /// </summary>
    public void HeightGrid(double x0, double z0, double spacing, int nx, int nz, Span<double> output, double minWavelength = -1)
    {
        if (output.Length < nx * nz) throw new ArgumentException("Output too small.", nameof(output));
        double mw = minWavelength < 0 ? spacing * 2 : minWavelength;
        for (int j = 0; j < nz; j++)
        {
            double z = z0 + j * spacing;
            for (int i = 0; i < nx; i++) output[j * nx + i] = Sample(x0 + i * spacing, z, mw).Height;
        }
    }

    /// <summary>Parallel <see cref="HeightGrid"/> for large requests (maps, raster previews).</summary>
    public void HeightGridParallel(double x0, double z0, double spacing, int nx, int nz, double[] output, double minWavelength = -1)
    {
        double mw = minWavelength < 0 ? spacing * 2 : minWavelength;
        Parallel.For(0, nz, j =>
        {
            double z = z0 + j * spacing;
            for (int i = 0; i < nx; i++) output[j * nx + i] = Sample(x0 + i * spacing, z, mw).Height;
        });
    }
}
