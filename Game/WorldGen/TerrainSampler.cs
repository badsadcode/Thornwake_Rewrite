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
            land += shift * gradient * DetMath.Smooth(1 - Math.Abs(land) / _coastBand); // fades out: no step at the band edge
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
        // Rivers: channel, banks and slope-limited valley sides replace the terrain near a river; walking-scale detail
        // fades toward it. Two nearby rivers (a confluence) combine continuously: deepest carving wins (softly),
        // highest bank wins, so the junction reads as one water body.
        var water = WaterKind.None; double level = double.NaN; int body = -1;
        double floor = .2 * inland + .05, height;
        Span<RiverHit> rivers = stackalloc RiverHit[32];
        int nearby = _hydrology.NearbyRivers(x, z, rivers);
        if (nearby > 0)
        {
            // One smooth profile per reach: true nearest distance, with water level/width/depth softly averaged over the
            // reach's nearby segments (exponential weights by extra distance). Continuous along the river and across
            // the midline of tight bends (rational weights; no exp in the hot path). Reaches then combine: deepest carve,
            // highest bank.
            Span<RiverHit> reaches = stackalloc RiverHit[16];
            int count = 0;
            for (int i = 0; i < nearby && count < reaches.Length; i++)
            {
                int reach = rivers[i].Reach;
                bool seen = false;
                for (int j = 0; j < count; j++) if (reaches[j].Reach == reach) { seen = true; break; }
                if (seen) continue;
                double dmin = double.PositiveInfinity;
                for (int j = i; j < nearby; j++) if (rivers[j].Reach == reach) dmin = Math.Min(dmin, rivers[j].Distance);
                double tau = 6 + .45 * dmin, wsum = 0, surface = 0, width = 0, depth = 0, catchment = 0;
                for (int j = i; j < nearby; j++)
                {
                    if (rivers[j].Reach != reach) continue;
                    double q = (rivers[j].Distance - dmin) / tau, w = 1 / (1 + q * q * (1 + q));
                    wsum += w; surface += w * rivers[j].Surface; width += w * rivers[j].Width; depth += w * rivers[j].Depth; catchment += w * rivers[j].Catchment;
                }
                reaches[count++] = new RiverHit(reach, dmin, surface / wsum, width / wsum, depth / wsum, catchment / wsum);
            }
            double rise = Math.Max(baseHeight, floor), damp = 1;
            for (int i = 0; i < count; i++) damp = Math.Min(damp, DetailDamp(reaches[i]));
            double terrain = Math.Max(baseHeight + detail * damp, floor);
            double carve = 0, raise = 0, wetInside = 0;
            for (int i = 0; i < count; i++)
            {
                double delta = Profile(reaches[i], terrain, rise, minWavelength * .5, out _, out double inside) - terrain;
                carve = Math.Min(carve, delta);
                raise = Math.Max(raise, delta);
                if (inside > wetInside) { wetInside = inside; water = WaterKind.River; level = reaches[i].Surface; body = reaches[i].Reach; }
            }
            // Inside a channel, another river's raised bank never lifts the bed above this water.
            height = wetInside > 0 ? Math.Min(terrain + carve, level - _p.SeaLevel - .05) : terrain + carve + raise;
        }
        else height = Math.Max(baseHeight + detail, floor); // generated land never dips below the sea

        if (water == WaterKind.None)
        {
            int lake = _hydrology.LakeNear(x, z);
            if (lake >= 0 && _p.SeaLevel + height < _hydrology.Lakes[lake].Level)
            { water = WaterKind.Lake; level = _hydrology.Lakes[lake].Level; body = lake; }
        }
        return new(_p.SeaLevel + height, land, mountainShare, detail, water, level, body);
    }

    // Walking-scale detail fades over a wide corridor around a river (valley floors are smoother than hillsides).
    // Depends only on the hit's geometry, and reaches 1 exactly at the river's influence edge (continuity).
    private static double DetailDamp(in RiverHit r) =>
        DetMath.Smooth((r.Distance - r.Width * .5) / (Hydrology.Bank(r.Width) + Hydrology.MaxFloodplain(r.Width)));

    /// <summary>
    /// River cross-section at the hit distance: parabolic channel, gentle banks up to just above the water, then a
    /// valley side blending back to the terrain over a width that grows with the height to climb (about 1 in 4).
    /// <paramref name="weight"/> is 1 near the river, 0 at the edge of its influence; <paramref name="inside"/> > 0 in water.
    /// </summary>
    private double Profile(in RiverHit r, double terrain, double rise, double spacing, out double weight, out double inside)
    {
        double half = r.Width * .5;
        if (spacing <= 2.5 || spacing > 40) return ChannelProfile(r, half, terrain, rise, out weight, out inside);
        // Coarse LOD tiles: a mesh with vertex spacing s interpolates heights linearly, so a channel edge that is a
        // straight ramp in distance (the same slope inside and outside the waterline, ±1.5 s around it) is drawn
        // exactly where the water edge really is: smooth, not a saw-tooth of whole triangles. Channels narrower than
        // the spacing are drawn at least that wide so they stay continuous in the distance.
        half = Math.Max(half, spacing * .5);
        double band = spacing * 1.5, d = r.Distance, surface = r.Surface - _p.SeaLevel;
        double outer = ChannelProfile(r, half, terrain, rise, out weight, out inside, half + band);
        inside = (half - d) / half;
        if (Math.Abs(d - half) >= band) return ChannelProfile(r, half, terrain, rise, out weight, out _);
        double g = Math.Max(.05, (outer - surface) / band);
        if (d >= half) return surface + g * (d - half);
        double bed = ChannelProfile(r, half, terrain, rise, out _, out _);
        return Math.Max(bed, surface - g * (half - d));
    }

    private double ChannelProfile(in RiverHit r, double half, double terrain, double rise, out double weight, out double inside, double distance = -1)
    {
        double bank = Hydrology.Bank(r.Width), d = distance >= 0 ? distance : r.Distance, surface = r.Surface - _p.SeaLevel;
        double bankTop = surface + .6 + .15 * r.Depth;
        double flood = Hydrology.Floodplain(r.Width, rise - bankTop);
        weight = 1 - DetMath.Smooth((d - half - bank) / flood);
        inside = (half - d) / half;
        if (d < half) { double u = d / half; return surface - r.Depth * (1 - u * u); }
        // Bank: rises to just above the water, quickly where the ground beside is high, as a gentle levee ramp where
        // it is low; blended by that ground height (no max(): it creased the banks wherever the two curves crossed).
        if (d < half + bank)
        {
            double gentle = DetMath.Smooth((d - half) / bank), quick = DetMath.Smooth((d - half) / Math.Min(bank, 2.5));
            double high = DetMath.Clamp((Math.Min(terrain, bankTop) - surface) / (bankTop - surface), 0, 1);
            return surface + (bankTop - surface) * DetMath.Lerp(gentle, quick, high);
        }
        // Where the ground is lower than the bank top, only a short levee is raised (long raised ramps from many
        // segments at different levels would stack into terraces along steep rivers).
        // The switch is blended over a metre of terrain height: a hard switch drew small crescent scarps wherever
        // walking-scale bumps crossed the bank-top level.
        double ramp = DetMath.Lerp(Math.Min(flood, bank * 2 + 10), flood, DetMath.Smooth(terrain - bankTop + .5));
        return bankTop + (terrain - bankTop) * DetMath.Smooth((d - half - bank) / ramp);
    }

    private static double SmoothMin(double a, double b, double k)
    {
        if (k <= 1e-9) return Math.Min(a, b);
        double h = DetMath.Clamp(.5 + .5 * (b - a) / k, 0, 1);
        return b + (a - b) * h - k * h * (1 - h);
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
