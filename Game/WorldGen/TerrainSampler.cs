using System;
using System.Runtime.CompilerServices;
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
    int WaterBody,       // lake id or river reach id (-1 for sea / none)
    double Plume = 0);   // 0..1 river water mixing into a lake or the sea near a mouth (shading)

/// <summary>
/// The terrain authority for generated (unedited) ground: a pure, thread-safe function of world position.
/// <para>height = macro raster (A4 land influence, hills + mountain relief, mountain share) + sub-raster coast
/// detail + walking-scale eroded detail (lowland) and ridged rock detail (mountains).</para>
/// <para><c>minWavelength</c> is the LOD knob: detail octaves shorter than it are skipped (and the last one
/// faded), so a coarse distant tile is the near terrain minus small bumps. Use about twice the vertex spacing.</para>
/// <para>The height pipeline (D227), one method per stage, water last so it carves through everything before it:</para>
/// <code>
/// stage                      file                       terrain revisions that changed it
/// 1a Coast                   TerrainSampler.cs          -
/// 1b Sea (ends the walk)     TerrainSampler.cs          16 (a sea pocket inside a lake's reach is lake)
/// 1c Continent               TerrainSampler.cs          -  (ShoreSlope, MouthBlend: build 5 / preview 7 options)
/// 2  Detail                  TerrainSampler.cs          -
/// 3  BiomeTerrain            TerrainSampler.cs          11 (wetlands), 12 (all biomes; landforms: BiomeLandforms.cs)
/// 4  Rivers                  TerrainSampler.Water.cs    9 (blended along, river profiles), 13 (tributaries on floodplains)
/// 5  Lakes                   TerrainSampler.Water.cs    10 (fill within reach, low shore), 15 (a river in a lake is lake)
/// 6  Pools                   TerrainSampler.Water.cs    11, 12 (shore stretch, seam fade, grow), 14 (mask is water, size ladder)
/// 7  PlayerEdits             TerrainSampler.Water.cs    -
/// </code>
/// <para>Each stage takes and gives plain values (a shared struct cost ~2.5 % of the speed).</para>
/// </summary>
public sealed partial class TerrainSampler
{
    public WorldPlan Plan { get; }
    /// <summary>Player edits on top of the generated ground (empty for a fresh world: no cost, identical output).</summary>
    public TerrainEdits Edits { get; } = new();
    /// <summary>Generated objects taken out of the world (felled trees), by their lattice cell.</summary>
    public ObjectRemovals Removals { get; } = new();
    /// <summary>Generated objects this PC draws itself for a while (a tree shaking and falling): left out of the batches.
    /// Local only, never sent.</summary>
    public ObjectRemovals Hidden { get; } = new();
    private readonly WorldProfile _p;
    private readonly TerrainStyle _style;
    private readonly MacroRaster _macro;
    private readonly Hydrology _hydrology;
    private readonly double _ox, _oz;
    private readonly ulong _detailSeed, _rockSeed, _coastSeed, _regionSeed, _bankSeed, _poolSeed, _landformSeed;
    private readonly double _duneAngle;
    private readonly BiomeLandforms _landforms;
    // Wetland pools: sorted samples of the pool and channel noise (unit standard deviation), to turn a coverage share
    // into a threshold.
    private readonly double[] _poolQuantiles, _channelQuantiles;
    private readonly double _poolStd, _channelStd;
    private readonly double _coastBand;

    public TerrainSampler(WorldPlan plan)
    {
        Plan = plan; _p = plan.Profile; _style = plan.Style; _macro = plan.Macro; _hydrology = plan.Hydrology;
        _ox = plan.Manifest.OriginX; _oz = plan.Manifest.OriginZ;
        var m = plan.Manifest;
        ulong Seed(string layer) => Hashing.FeatureSeed(m.Seed, 100 + WorldManifest.CurrentGenerator, m.ProfileDigest, layer);
        _detailSeed = Seed("detail"); _rockSeed = Seed("rock"); _coastSeed = Seed("coast"); _regionSeed = Seed("region"); _bankSeed = Seed("banks");
        _poolSeed = Seed("pools");
        _landformSeed = Seed("landforms");
        _duneAngle = Hashing.Unit(Hashing.Mix(_landformSeed)) * Math.Tau; // prevailing sand-moving wind of this world
        _landforms = new BiomeLandforms(_hydrology, _landformSeed, _duneAngle);
        (_poolQuantiles, _poolStd) = Quantiles((u, v) => PoolNoise(u, v, 1, 0));
        (_channelQuantiles, _channelStd) = Quantiles((u, v) => ChannelNoise(u, v, 1, 0));
        // Coast detail is only evaluated where it can change the land/sea decision or the shore profile.
        _coastBand = .25;
    }

    public double Height(double x, double z, double minWavelength = 2) => Sample(x, z, minWavelength).Height;

    public TerrainSample Sample(double x, double z, double minWavelength = 2)
    {
        // The height pipeline (D227): each stage adds to the point and hands it on; water comes last, so rivers and lakes
        // carve through everything before them ("water always wins"). The sea ends the walk at the coast stage.
        // (each stage takes and gives plain values — a shared struct cost ~2.5 % of the speed)
        double lx = x - _ox, lz = z - _oz;
        Coast(x, z, lx, lz, minWavelength, out double land, out double macroHeight, out double mountainShare, out double rasterLand, out double coastDistance);
        if (land <= 0) return Sea(x, z, land, coastDistance);
        double baseHeight = Continent(land, rasterLand, macroHeight, coastDistance, ref mountainShare);
        Detail(lx, lz, minWavelength, land, mountainShare, out double inland, out double detail, out double roughness);
        BiomeTerrain(x, z, lx, lz, minWavelength, baseHeight, inland, ref detail, out bool wetland, out double poolCover, out double poolDepth,
            out double poolSize, out double poolChannels, out double flatten);
        var water = WaterKind.None; double level = double.NaN; int body = -1; double riverDamp = 1;
        double height = Rivers(x, z, minWavelength, baseHeight, inland, detail, ref riverDamp, ref water, ref level, ref body);
        double lakePlume = 0, lakeCells = double.PositiveInfinity;
        Lakes(x, z, ref height, ref water, ref level, ref body, ref lakePlume, ref lakeCells);
        if (wetland && poolCover > 0 && water == WaterKind.None)
            Pools(lx, lz, x, z, minWavelength, baseHeight, roughness, mountainShare, inland, flatten, land, riverDamp, lakeCells, poolCover,
                poolDepth, poolSize, poolChannels, ref height, ref water, ref level);
        PlayerEdits(x, z, ref height, ref water, ref level, ref body);
        return new(_p.SeaLevel + height, land, mountainShare, detail, water, level, body, lakePlume);
    }

    /// <summary>Stage 1a, coast: the macro raster (land, eroded height, mountain share) and the sub-raster coastline wiggle.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] // (the split must not cost speed, D227)
    private void Coast(double x, double z, double lx, double lz, double minWavelength, out double land, out double macroHeight, out double mountainShare,
        out double rasterLand, out double coastDistance)
    {
        _macro.Sample(x, z, WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out land, out macroHeight, out mountainShare);
        rasterLand = land;
        // Sub-raster coastline: shift the coast sideways by a fractal distance (metres), converted to land
        // influence through the local gradient so continents and small islands get the same metric wiggle.
        coastDistance = double.PositiveInfinity; // metres from the detailed coastline (estimate)
        if (land > -_coastBand && land < _coastBand)
        {
            _macro.SampleGradient(WorldPlan.LandChannel, x, z, out double gx, out double gz);
            double gradient = Math.Sqrt(gx * gx + gz * gz);
            double shift = _style.CoastDetailShift * Noise.Fbm(lx, lz, _macro.Cell * 2.5, 7, minWavelength, _coastSeed, .55);
            land += shift * gradient * DetMath.Smooth(1 - Math.Abs(land) / _coastBand); // fades out: no step at the band edge
            coastDistance = Math.Abs(land) / Math.Max(gradient, 1e-12);
        }
    }

    /// <summary>Stage 1b, the sea: the sea floor (deepening off the coast, river mouths' channels) — the walk ends here.</summary>
    private TerrainSample Sea(double x, double z, double land, double coastDistance)
    {
        // Shore profile (ShoreSlope > 0): the macro ramp alone left hundreds of metres of ankle-deep water and flat
        // beach at this world scale. A beach-like slope (~3.5 %) takes the sea floor to ~25 m within a few hundred
        // metres and lifts the land a few metres within ~150 m; beyond, the macro shape takes over.
        double shore = _style.ShoreSlope;

        double depth = _p.OceanDepth * DetMath.Smooth(-land / .6);
        if (shore > 0 && coastDistance < 1e7) depth = Math.Max(depth, 25 * (1 - DetMath.Exp(-coastDistance * .035 * shore / 25)));
        double seabed = _p.SeaLevel - depth, plume = 0;
        if (_style.MouthBlend > 0) seabed = MouthChannel(x, z, seabed, -1, out plume);
        seabed += Edits.Delta(x, z);
        // Revision 16: the coastline wiggle can cut a small pocket of "sea" out of a lake basin near the coast; the
        // sea's level then punched a hole in the lake, a crater with 13 m walls of water in Tom's dump 182351
        // (benchmark lake, level 13 m, ground -2.1 m). Inside a lake's reach the pocket is part of the lake.
        if (_style.Revision >= 16)
        {
            int lake = _hydrology.LakeDistance(x, z, out double cells);
            if (lake >= 0 && cells < 1.6 && _hydrology.Lakes[lake].Level > _p.SeaLevel + .05 && seabed < _hydrology.Lakes[lake].Level)
                return new(seabed, land, 0, 0, WaterKind.Lake, _hydrology.Lakes[lake].Level, lake, 0);
        }
        return seabed < _p.SeaLevel ? new(seabed, land, 0, 0, WaterKind.Ocean, _p.SeaLevel, -1, plume)
            : new(seabed, land, 0, 0, WaterKind.None, double.NaN, -1);
    }

    /// <summary>Stage 1c, continent: the land's base height from the eroded macro height and the shore ramp.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] // (the split must not cost speed, D227)
    private double Continent(double land, double rasterLand, double macroHeight, double coastDistance, ref double mountainShare)
    {
        double shore = _style.ShoreSlope;
        // Eroded macro height; the coastal ramp follows the detailed coastline rather than the raster's.
        double baseHeight = macroHeight + _p.LowlandHeight * (DetMath.Smooth(land / .4) - DetMath.Smooth(rasterLand / .4));
        baseHeight = Math.Max(_p.LowlandHeight * DetMath.Smooth(land / .08) * .05, baseHeight);
        if (shore > 0 && coastDistance < 1e7) baseHeight = Math.Max(baseHeight, 3.5 * (1 - DetMath.Exp(-coastDistance * .025 * shore / 3.5)));
        mountainShare = DetMath.Clamp(mountainShare, 0, 1);
        return baseHeight;
    }

    /// <summary>Stage 2, detail: walking-scale relief (rolling hills by regional character, rock ridges in the mountains),
    /// fading out at the shoreline.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] // (the split must not cost speed, D227)
    private void Detail(double lx, double lz, double minWavelength, double land, double mountainShare, out double inland, out double detail,
        out double roughness)
    {
        // Walking-scale relief. Fades out at the shoreline so beaches and shelves stay smooth.
        inland = DetMath.Smooth(land / .03);
        detail = 0; roughness = 1;
        if (inland > 0)
        {
            // Regional character: broad plains in some places, rougher hill country in others (Supplement §3),
            // varying over tens of kilometres so it never reads as a repeating texture.
            double character = DetMath.Smooth(.5 + .9 * Noise.Fbm(lx, lz, _style.RegionWavelength, 3, minWavelength, _regionSeed));
            roughness = _style.PlainsRoughness + (1 - _style.PlainsRoughness) * character;
            double rolling = Noise.ErodedFbm(lx, lz, _style.DetailWavelength, 11, minWavelength, _detailSeed, _style.DetailGain);
            detail = rolling * _style.LowlandDetail * roughness * (1 + 2 * mountainShare);
            if (mountainShare > .02)
            {
                double ridges = Noise.Ridged(lx, lz, 900, 8, minWavelength, _rockSeed, .5) - .3;
                detail += ridges * _style.MountainDetail * _p.MountainHeight * DetMath.Smooth(mountainShare / .6);
            }
            detail *= inland;
        }
    }

    /// <summary>Stage 3, biome terrain and landforms: wetland flattening and pool settings, then the biome landforms
    /// (dunes, mesas, spires — <see cref="BiomeLandforms"/>).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)] // (the split must not cost speed, D227)
    private void BiomeTerrain(double x, double z, double lx, double lz, double minWavelength, double baseHeight, double inland, ref double detail,
        out bool wetland, out double poolCover, out double poolDepth, out double poolSize, out double poolChannels, out double flatten)
    {
        // Wetlands (revision 11): biomes with standing-water pools or flattened ground (swamps, marshes, fens, bayous).
        wetland = false;
        poolCover = 0; poolDepth = 0; poolSize = 16; poolChannels = 0; flatten = 0;
        BiomeTerrainMods mods = default;
        if (_style.Revision >= 11 && inland > 0 && Plan.Biomes is { } biomes)
        {
            wetland = biomes.TerrainAt(x, z, out mods, allBiomes: _style.Revision >= 12);
            poolCover = mods.Pools; poolDepth = mods.PoolDepth; poolSize = mods.PoolSize; poolChannels = mods.PoolChannels; flatten = mods.Flatten;
            // Where there are pools the ground is flatter still (to ~8 % of the relief), so their surfaces stay level.
            if (poolCover > 0) flatten = Math.Max(flatten, Math.Min(.92, flatten + poolCover));
            if (wetland) detail *= 1 - flatten;
        }
        // Landforms (revision 12, round 9): dunes, mesas and needle spires from the biomes' Terrain.Features.
        if (_style.Revision >= 12 && wetland && mods.Dunes + mods.Mesas + mods.Spires > 0)
            detail = _landforms.Apply(x, z, lx, lz, baseHeight, detail, inland, minWavelength, mods);
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
