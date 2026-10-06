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
    int WaterBody,       // lake id or river reach id (-1 for sea / none)
    double Plume = 0);   // 0..1 river water mixing into a lake or the sea near a mouth (shading)

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
    /// <summary>Player edits on top of the generated ground (empty for a fresh world: no cost, identical output).</summary>
    public TerrainEdits Edits { get; } = new();
    private readonly WorldProfile _p;
    private readonly TerrainStyle _style;
    private readonly MacroRaster _macro;
    private readonly Hydrology _hydrology;
    private readonly double _ox, _oz;
    private readonly ulong _detailSeed, _rockSeed, _coastSeed, _regionSeed, _bankSeed, _poolSeed, _landformSeed;
    private readonly double _duneAngle;
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
        (_poolQuantiles, _poolStd) = Quantiles((u, v) => PoolNoise(u, v, 1, 0));
        (_channelQuantiles, _channelStd) = Quantiles((u, v) => ChannelNoise(u, v, 1, 0));
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
        double coastDistance = double.PositiveInfinity; // metres from the detailed coastline (estimate)
        if (land > -_coastBand && land < _coastBand)
        {
            _macro.SampleGradient(WorldPlan.LandChannel, x, z, out double gx, out double gz);
            double gradient = Math.Sqrt(gx * gx + gz * gz);
            double shift = _style.CoastDetailShift * Noise.Fbm(lx, lz, _macro.Cell * 2.5, 7, minWavelength, _coastSeed, .55);
            land += shift * gradient * DetMath.Smooth(1 - Math.Abs(land) / _coastBand); // fades out: no step at the band edge
            coastDistance = Math.Abs(land) / Math.Max(gradient, 1e-12);
        }
        // Shore profile (ShoreSlope > 0): the macro ramp alone left hundreds of metres of ankle-deep water and flat
        // beach at this world scale. A beach-like slope (~3.5 %) takes the sea floor to ~25 m within a few hundred
        // metres and lifts the land a few metres within ~150 m; beyond, the macro shape takes over.
        double shore = _style.ShoreSlope;

        if (land <= 0)
        {
            double depth = _p.OceanDepth * DetMath.Smooth(-land / .6);
            if (shore > 0 && coastDistance < 1e7) depth = Math.Max(depth, 25 * (1 - DetMath.Exp(-coastDistance * .035 * shore / 25)));
            double seabed = _p.SeaLevel - depth, plume = 0;
            if (_style.MouthBlend > 0) seabed = MouthChannel(x, z, seabed, -1, out plume);
            seabed += Edits.Delta(x, z);
            return seabed < _p.SeaLevel ? new(seabed, land, 0, 0, WaterKind.Ocean, _p.SeaLevel, -1, plume)
                : new(seabed, land, 0, 0, WaterKind.None, double.NaN, -1);
        }

        // Eroded macro height; the coastal ramp follows the detailed coastline rather than the raster's.
        double baseHeight = macroHeight + _p.LowlandHeight * (DetMath.Smooth(land / .4) - DetMath.Smooth(rasterLand / .4));
        baseHeight = Math.Max(_p.LowlandHeight * DetMath.Smooth(land / .08) * .05, baseHeight);
        if (shore > 0 && coastDistance < 1e7) baseHeight = Math.Max(baseHeight, 3.5 * (1 - DetMath.Exp(-coastDistance * .025 * shore / 3.5)));
        mountainShare = DetMath.Clamp(mountainShare, 0, 1);

        // Walking-scale relief. Fades out at the shoreline so beaches and shelves stay smooth.
        double inland = DetMath.Smooth(land / .03);
        double detail = 0, roughness = 1;
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
        // Wetlands (revision 11): biomes with standing-water pools or flattened ground (swamps, marshes, fens, bayous).
        bool wetland = false;
        double poolCover = 0, poolDepth = 0, poolSize = 16, poolChannels = 0, flatten = 0, riverDamp = 1;
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
            detail = Landforms(x, z, lx, lz, baseHeight, detail, inland, minWavelength, mods);
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
                // Side and distance along the reach from the nearest segment (averaging them mixed in other segments'
                // clamped end points and made the bank noise jitter).
                double dmin = double.PositiveInfinity, along = 0; int side = 0;
                for (int j = i; j < nearby; j++)
                    if (rivers[j].Reach == reach && rivers[j].Distance < dmin) { dmin = rivers[j].Distance; side = rivers[j].Side; along = rivers[j].Along; }
                double tau = 6 + .45 * dmin, wsum = 0, surface = 0, width = 0, depth = 0, catchment = 0, alongSum = 0, alongWeight = 0;
                for (int j = i; j < nearby; j++)
                {
                    if (rivers[j].Reach != reach) continue;
                    double q = (rivers[j].Distance - dmin) / tau, w = 1 / (1 + q * q * (1 + q));
                    wsum += w; surface += w * rivers[j].Surface; width += w * rivers[j].Width; depth += w * rivers[j].Depth; catchment += w * rivers[j].Catchment;
                    // Along: only segments about as near as the nearest count, with a weight reaching exactly 0 (far
                    // segments have very different along values and enter the hit list at spatial-grid borders).
                    double wa = 1 - DetMath.Smooth((rivers[j].Distance - dmin) / (3 + .2 * dmin));
                    alongSum += wa * rivers[j].Along; alongWeight += wa;
                }
                // Revision 9: along is blended like the other attributes; the nearest segment's along jumps where the
                // nearest segment switches (inside bends), and the bank noise drew streaks along those lines.
                if (_style.Revision >= 9 && alongWeight > 0) along = alongSum / alongWeight;
                reaches[count++] = new RiverHit(reach, dmin, surface / wsum, width / wsum, depth / wsum, catchment / wsum, along, side);
            }
            double rise = Math.Max(baseHeight, floor), damp = 1;
            for (int i = 0; i < count; i++) damp = Math.Min(damp, DetailDamp(reaches[i]));
            riverDamp = damp;
            double terrain = Math.Max(baseHeight + detail * damp, floor);
            // Revision 13: a river crossing a bigger river's lower valley floor (a tributary running out over the main
            // river's floodplain) flows at that floor, a little below it, instead of keeping its own level 2–3 m above
            // the dry floodplain (Tom's desert confluence: water walls along both banks). The floor is what the other
            // rivers carve here; the cap only bites where they carve (smooth weight) and never lifts a river.
            if (_style.Revision >= 13 && count > 1)
            {
                Span<double> deltas = stackalloc double[16];
                for (int i = 0; i < count; i++) deltas[i] = Profile(reaches[i], terrain, rise, minWavelength * .5, out _, out _) - terrain;
                for (int i = 0; i < count; i++)
                {
                    double other = 0; int deepest = -1;
                    for (int j = 0; j < count; j++) if (j != i && deltas[j] < other) { other = deltas[j]; deepest = j; }
                    if (deepest < 0) continue;
                    // Never below the other river's own water (inside its channel the "floor" is its bed).
                    double cap = Math.Max(_p.SeaLevel + terrain + other - .3, reaches[deepest].Surface), excess = reaches[i].Surface - cap;
                    if (excess <= 0) continue;
                    reaches[i] = reaches[i] with { Surface = reaches[i].Surface - excess * DetMath.Smooth(-other / .5) };
                }
            }
            double carve = 0, raise = 0, wetInside = 0;
            for (int i = 0; i < count; i++)
            {
                double delta = Profile(reaches[i], terrain, rise, minWavelength * .5, out _, out double inside) - terrain;
                if (delta > 0 && _style.MouthBlend > 0) delta *= LeveeFactor(reaches[i]);
                carve = Math.Min(carve, delta);
                raise = Math.Max(raise, delta);
                if (inside > wetInside) { wetInside = inside; water = WaterKind.River; level = reaches[i].Surface; body = reaches[i].Reach; }
            }
            // Inside a lake basin (ground below the lake surface) rivers carve but never raise banks: their levees
            // ringed river mouths with dry land at the lake edge.
            int basin = _style.BankVariation > 0 ? _hydrology.LakeNear(x, z) : -1; // off in the build-5 terrain
            if (basin >= 0)
            {
                double below = _hydrology.Lakes[basin].Level - _p.SeaLevel - terrain;
                raise *= DetMath.Smooth((-.05 - below) / .4); // none once the ground is under the lake, full 0.35 m above it
            }
            // Inside a channel, another river's raised bank never lifts the bed above this water.
            height = wetInside > 0 ? Math.Min(terrain + carve, level - _p.SeaLevel - .05) : terrain + carve + raise;
        }
        else height = Math.Max(baseHeight + detail, floor); // generated land never dips below the sea

        double lakePlume = 0, lakeCells = double.PositiveInfinity;
        if (water == WaterKind.None && _style.Revision >= 10)
        {
            // Revision 10: the lake fills every point below its surface within ~1 cell of its cells (round reach);
            // between 1 and 1.6 cells the ground rises to a low shore just above the water, so no dry ground below
            // the surface is left where the reach ends (build 11: lakes cut off along straight cell lines).
            int lake = _hydrology.LakeDistance(x, z, out double cells);
            if (lake >= 0) lakeCells = cells;
            if (lake >= 0 && cells < 1.6)
            {
                double surface = _hydrology.Lakes[lake].Level - _p.SeaLevel;
                if (cells > 1) height = Math.Max(height, height + (surface + .4 - height) * DetMath.Smooth((cells - 1) / .6));
                if (height < surface)
                {
                    water = WaterKind.Lake; level = _hydrology.Lakes[lake].Level; body = lake;
                    if (_style.MouthBlend > 0) height = MouthChannel(x, z, height, lake, out lakePlume);
                }
            }
        }
        else if (water == WaterKind.None)
        {
            int lake = _hydrology.LakeNear(x, z);
            if (lake >= 0 && _p.SeaLevel + height < _hydrology.Lakes[lake].Level)
            {
                water = WaterKind.Lake; level = _hydrology.Lakes[lake].Level; body = lake;
                if (_style.MouthBlend > 0) height = MouthChannel(x, z, height, lake, out lakePlume);
            }
        }
        if (wetland && poolCover > 0 && water == WaterKind.None)
        {
            // Pool water level: the ground without its short relief (the long waves only, flattened like the rest).
            double low = Noise.ErodedFbm(lx, lz, _style.DetailWavelength, 11, Math.Max(minWavelength, PoolLevelWavelength), _detailSeed, _style.DetailGain)
                * _style.LowlandDetail * roughness * (1 + 2 * mountainShare) * inland * (1 - flatten);
            double smooth = Math.Max(baseHeight + low, floor);
            _macro.SampleGradient(WorldPlan.HeightChannel, x, z, out double gx, out double gz);
            // No pools at lake shores (they would dig below the lake's surface where its reach ends). The fade must end
            // within the lake search radius (2 cells); revision 11 ended it at 2.5 (a faint seam), kept for its worlds.
            double cover = poolCover * riverDamp * DetMath.Smooth(land / .015) * (1 - DetMath.Smooth(mountainShare / .3)) * DetMath.Smooth((lakeCells - 1.6) / (_style.Revision >= 12 ? .38 : .9))
                * (1 - DetMath.Smooth((Math.Sqrt(gx * gx + gz * gz) - .015) / .03));
            if (cover > .005) height = Pool(lx, lz, height, smooth, Math.Min(cover, .85), poolDepth, poolSize, poolChannels, minWavelength, ref water, ref level);
        }
        double edit = Edits.Delta(x, z);
        if (edit != 0)
        {
            height += edit;
            // Ground raised out of a river or lake is dry (lowered ground does not fill by itself: the local water
            // simulation does that).
            if (water != WaterKind.None && _p.SeaLevel + height >= level - .02) { water = WaterKind.None; level = double.NaN; body = -1; }
        }
        return new(_p.SeaLevel + height, land, mountainShare, detail, water, level, body, lakePlume);
    }

    /// <summary>
    /// Biome landforms (revision 12) on top of the walking-scale detail, before rivers carve (rivers cut through them:
    /// canyons through mesas, gaps through dune fields). Faded at lake shores so lakes keep their basins.
    /// <list type="bullet">
    /// <item>Dunes: transverse ridges across this world's sand-moving wind, a long gentle windward slope and a steeper
    /// slip face (~35°), crests wandering and breaking up; 4–12 m high, ~90 m apart; the rest of the relief is
    /// smoothed (sand seas).</item>
    /// <item>Mesas: flat-topped uplands 40–110 m high raised out of the plain, then the ground terraced into steps of
    /// 14–24 m with cliff-like risers and nearly flat treads; the steps wander, so they read as mesas, buttes and
    /// benches. Rivers cut canyons through them.</item>
    /// <item>Spires: needle rocks (Thousand Needles, hoodoos): columns 3–9 m across and 12–55 m tall with near-vertical
    /// sides on a small talus flare, on a jittered 36 m grid.</item>
    /// </list>
    /// </summary>
    private double Landforms(double x, double z, double lx, double lz, double baseHeight, double detail, double inland, double minWavelength, in BiomeTerrainMods mods)
    {
        int lake = _hydrology.LakeDistance(x, z, out double cells);
        double fade = inland * (lake >= 0 ? DetMath.Smooth((cells - 1.6) / .38) : 1); // done within the 2-cell lake search
        if (fade <= 0) return detail;
        if (mods.Dunes > 0)
        {
            double s = Math.Min(1, mods.Dunes) * fade;
            detail = detail * (1 - .6 * s) + Dunes(lx, lz, minWavelength) * s;
        }
        // Spire height fades out across the edge of spire biomes (a faint trace of one gave rare full-size needles).
        if (mods.Spires > 0) detail += Spires(lx, lz, minWavelength, Math.Min(1.5, mods.Spires)) * fade * DetMath.Smooth(mods.SpirePresence / .6);
        if (mods.Mesas > 0)
        {
            // Table-lands raised out of the land (mesas, buttes), stepped into cliff-sided tiers. Only the uplift is
            // terraced: terracing the regional slope itself gave rice-paddy contour benches.
            double s = Math.Min(1, mods.Mesas) * fade;
            double up = MesaUplift(lx, lz, minWavelength);
            // (× a ramp over the first 4 m: the terrace of a near-zero uplift is not exactly zero; no step at the foot.)
            if (up > 0) detail += Math.Max(0, Terrace(up, lx, lz, minWavelength)) * DetMath.Smooth(up / 4) * s;
        }
        return detail;
    }

    private double Dunes(double lx, double lz, double minWavelength)
    {
        const double spacing = 90;
        // Distant tiles cannot carry ridges this fine: fade them out rather than alias.
        double lod = 1 - DetMath.Smooth((minWavelength - spacing * .35) / (spacing * .25));
        if (lod <= 0) return 0;
        ulong seed = _landformSeed + 100;
        // A fixed heading per world (rotating far-from-origin coordinates by a varying angle would squeeze the ridges);
        // the crests curve and fork through the phase warp instead: broad swings over kilometres, wobble across the wind.
        double ux = Math.Cos(_duneAngle), uz = Math.Sin(_duneAngle);
        double along = lx * ux + lz * uz, across = -lx * uz + lz * ux;
        double phase = along / spacing + 6 * Noise.Fbm(lx, lz, 6000, 2, minWavelength, seed)
            + .9 * Noise.Fbm(across, along * .25, spacing * 5, 2, minWavelength, seed + 1)
            + .25 * Noise.Fbm(lx, lz, spacing * 1.6, 2, minWavelength, seed + 2);
        double f = phase - Math.Floor(phase);
        const double windward = .68;
        double profile = f < windward ? DetMath.Smooth(f / windward) : 1 - DetMath.Smooth((f - windward) / (1 - windward));
        // Dune fields and calmer sand sheets (km), crests breaking into separate dunes along their length.
        double field = DetMath.Smooth(.55 + 1.1 * Noise.Fbm(lx, lz, 4000, 2, minWavelength, seed + 3));
        double crest = .45 + .55 * DetMath.Smooth(.5 + 1.6 * Noise.Fbm(across, along, spacing * 3.5, 2, minWavelength, seed + 4));
        return (4 + 8 * field) * crest * profile * lod;
    }

    // Flat-topped uplands 40–110 m high with steep edges: patches where a noise exceeds a threshold, so they break into
    // mesas, buttes and long benches; ~half the land stays plain.
    private double MesaUplift(double lx, double lz, double minWavelength)
    {
        // Outline noise with all its octaves at every detail level: far tiles must show the same mesas (no shifting
        // cliffs as you approach).
        minWavelength = Math.Min(minWavelength, 150);
        ulong seed = _landformSeed + 210;
        double n = Noise.Fbm(lx, lz, 1400, 3, minWavelength, seed, .5);
        double height = 40 + 70 * DetMath.Smooth(.5 + Noise.Fbm(lx, lz, 6000, 2, minWavelength, seed + 1));
        return height * DetMath.Smooth((n - .02) / .07);
    }

    private double Terrace(double h, double lx, double lz, double minWavelength)
    {
        minWavelength = Math.Min(minWavelength, 150); // tiers stay put at every detail level
        ulong seed = _landformSeed + 200;
        double step = 12 + 9 * DetMath.Smooth(.5 + Noise.Fbm(lx, lz, 5000, 2, minWavelength, seed));
        double offset = step * .35 * Noise.Fbm(lx, lz, 700, 3, minWavelength, seed + 1); // tier heights wander
        if (h <= 0) return h;
        double t = (h + offset) / step, k = Math.Floor(t), f = t - k;
        // Tread rises only 15 % of a step over its width; the riser (last 18 % of the height range) takes the rest.
        const double riser = .18;
        double g = .15 * f + .85 * DetMath.Smooth((f - (1 - riser)) / riser);
        return (k + g) * step - offset;
    }

    private double Spires(double lx, double lz, double minWavelength, double density)
    {
        const double cell = 36;
        ulong seed = _landformSeed + 300;
        long ci = (long)Math.Floor(lx / cell), cj = (long)Math.Floor(lz / cell);
        double best = 0;
        for (long dj = -1; dj <= 1; dj++)
            for (long di = -1; di <= 1; di++)
            {
                ulong h = Hashing.Lattice(ci + di, cj + dj, seed);
                if (Hashing.Unit(h) > .45 * density) continue;
                double cx = (ci + di + .2 + .6 * Hashing.Unit(Hashing.Mix(h + 1))) * cell, cz = (cj + dj + .2 + .6 * Hashing.Unit(Hashing.Mix(h + 2))) * cell;
                double r = 3 + 6 * Hashing.Unit(Hashing.Mix(h + 3));
                double u = Hashing.Unit(Hashing.Mix(h + 4)), height = Math.Max(12 + 43 * u * u, r * 4.2); // needles, not stumps
                // Coarse tiles see a wider, equally tall spire (never vanishing or aliasing far away).
                double re = Math.Max(r, minWavelength * .6);
                double d = Math.Sqrt((lx - cx) * (lx - cx) + (lz - cz) * (lz - cz));
                if (d > re * 1.8) continue;
                double side = DetMath.Smooth((re - d) / (re * .2 + .6));      // near-vertical wall
                double talus = DetMath.Smooth((re * 1.8 - d) / (re * .8)) * .12; // a small flare of scree at the foot
                double top = .94 + .06 * DetMath.Smooth(1 - d / re);          // slightly rounded cap
                best = Math.Max(best, height * Math.Max(side * top, talus));
            }
        return best;
    }

    /// <summary>Wavelength below which relief is left out of the pool water level (shorter relief is flattened at pools).</summary>
    private const double PoolLevelWavelength = 500;

    /// <summary>
    /// Standing-water pools (wetland biomes, revision 11). A noise mask (round pools, or long winding channels) above a
    /// threshold set by the coverage share marks the water. Near and in the pools the ground follows a profile around the
    /// flat water level <paramref name="smooth"/>: a low bank (0.25 m) rising from the shore, the bed falling to
    /// <paramref name="depth"/> over a few metres, plus a little of the local relief (bumps on the bed, an uneven
    /// shoreline). Further out it blends back to the terrain. Water only where the shaped ground is below the level and
    /// next to the mask, so no dry ground below the water surface is left beside a pool.
    /// </summary>
    private double Pool(double lx, double lz, double height, double smooth, double cover, double depth, double size, double channels,
        double minWavelength, ref WaterKind water, ref double level)
    {
        double minW = Math.Min(minWavelength, size * .5); // at least the pools themselves, even on coarse distant tiles
        // Pools and channels share the coverage; the water is their union (the larger excess wins). The mask is a
        // little larger than the water (the shore takes the edge of it), hence ×1.3.
        double coverIn = cover;
        cover = Math.Min(.9, cover * 1.3);
        double e = double.NegativeInfinity;
        if (channels < .999) e = PoolNoise(lx, lz, size, minW) / _poolStd - Quantile(_poolQuantiles, cover * (1 - channels));
        if (channels > .001) e = Math.Max(e, ChannelNoise(lx, lz, size, minW) / _channelStd - Quantile(_channelQuantiles, cover * channels));
        // e is in noise standard deviations; the pool noise changes by ~0.25 per metre, so soft ≈ 1.5 m of shore.
        const double soft = .4, bank = .25;
        // How far out the pool shapes the ground (in noise deviations). Revision 12 stretches the blend back to the
        // terrain with the height difference to cover, so it never makes a cliff on hilly ground (revision 11: fixed).
        double dev = _style.Revision >= 12 ? Math.Abs(height - smooth) * 1.5 : 0;
        double below = Math.Max(0, smooth + .05 - height), drop = _style.Revision >= 12 ? below * 1.5 : 0;
        double nearRange = soft * 3 + dev, floorRange = soft * 2 + drop;
        if (-e > Math.Max(soft + nearRange, soft * 2 + floorRange)) return height;
        double fine = DetMath.Clamp(.3 * (height - smooth), -.8 * bank, .8 * bank);
        // The bed reaches full depth ~2 noise deviations in (≥ ~3 m): walkable, never a cliff into the water.
        double profile = e >= 0 ? -depth * DetMath.Smooth(e / (soft * 5)) : bank * DetMath.Smooth(-e / soft);
        double near = 1 - DetMath.Smooth((-e - soft) / nearRange);
        double shaped = height + (smooth + profile + fine - height) * near;
        // Revision 12: the shaping fades in with the coverage (from 0.5 % to 4 %), so where a pool biome only just reaches
        // the ground does not jump when its coverage passes the cut-off; water only once the shaping is complete.
        double grow = _style.Revision >= 12 ? DetMath.Smooth((coverIn - .005) / .035) : 1;
        if (e > -soft)
        {
            if (shaped < smooth && grow >= 1) { water = WaterKind.Pond; level = _p.SeaLevel + smooth; }
            // Revision 12: dry ground at the shore stands a few cm clear of the water (the surface tilts by up to ~2 cm
            // per metre, so a neighbouring pool could otherwise lie just above it); hidden under the waterline.
            else if (_style.Revision >= 12) shaped = Math.Max(shaped, smooth + .04);
        }
        else
        {
            // Dry ground near a pool stays above its surface; only further out may it fall back into lower terrain
            // (reaching exactly the terrain where the pool's influence ends).
            shaped = Math.Max(shaped, smooth + .05 - below * DetMath.Smooth((-e - 2 * soft) / floorRange));
        }
        if (grow < 1) shaped = height + (shaped - height) * grow;
        return shaped;
    }

    // Round pools: three octaves at about twice the pool size.
    private double PoolNoise(double x, double z, double size, double minWavelength) =>
        Noise.Fbm(x, z, size * 2.2, 3, minWavelength, _poolSeed, .42);

    // Channels: lines where a domain-warped noise crosses zero (high = near the line), winding like fen and bayou leads.
    private double ChannelNoise(double x, double z, double size, double minWavelength)
    {
        double wx = x + size * 3 * Noise.Fbm(x, z, size * 12, 2, minWavelength, _poolSeed + 8, .5);
        double wz = z + size * 3 * Noise.Fbm(x + 71.3, z - 19.7, size * 12, 2, minWavelength, _poolSeed + 9, .5);
        return -Math.Abs(Noise.Fbm(wx, wz, size * 9, 3, minWavelength, _poolSeed + 7, .42));
    }

    private static (double[] Sorted, double Std) Quantiles(Func<double, double, double> noise)
    {
        const int N = 6000;
        var v = new double[N];
        double sum = 0, sq = 0;
        for (int i = 0; i < N; i++)
        {
            // Spread over ~80 × 80 noise wavelengths (scale-free: the noise is evaluated at unit size).
            double u = Hashing.Unit(Hashing.Mix((ulong)i * 2 + 11)) * 80, w = Hashing.Unit(Hashing.Mix((ulong)i * 2 + 12)) * 80;
            v[i] = noise(u, w); sum += v[i]; sq += v[i] * v[i];
        }
        double mean = sum / N, std = Math.Sqrt(Math.Max(1e-12, sq / N - mean * mean));
        Array.Sort(v);
        for (int i = 0; i < N; i++) v[i] /= std;
        return (v, std);
    }

    /// <summary>The value exceeded by a share <paramref name="cover"/> of the samples.</summary>
    private static double Quantile(double[] sorted, double cover)
    {
        double f = DetMath.Clamp(1 - cover, 0, 1) * (sorted.Length - 1);
        int i = (int)f; if (i >= sorted.Length - 1) return sorted[^1];
        return sorted[i] + (sorted[i + 1] - sorted[i]) * (f - i);
    }

    /// <summary>
    /// Raised banks fade out over a river's last stretch into the sea (the estuary, incl. the part continuing out to
    /// sea) or a lake: levees there stood as dykes between river and sea, with a hard step at the mouth.
    /// </summary>
    private double LeveeFactor(in RiverHit r)
    {
        var reach = _hydrology.Reaches[r.Reach];
        if (!reach.EndsInSea && reach.EndLake < 0) return 1;
        double stretch = (reach.EndsInSea ? 880 : 0) + r.Width * 6 + 80;
        return DetMath.Smooth((reach.Length - r.Along - stretch * .6) / (stretch * .4));
    }

    /// <summary>
    /// Where rivers flow into the sea or a lake (<paramref name="lake"/> −1 = sea): the channel continues as a
    /// shallowing groove into the bed, so the river bed meets the sea/lake floor smoothly; and how much river water
    /// colours the water there (<paramref name="plume"/>).
    /// </summary>
    private double MouthChannel(double x, double z, double bed, int lake, out double plume)
    {
        plume = 0;
        Span<RiverHit> hits = stackalloc RiverHit[16];
        int n = _hydrology.NearbyRivers(x, z, hits);
        double result = bed;
        for (int i = 0; i < n; i++)
        {
            var r = hits[i];
            var reach = _hydrology.Reaches[r.Reach];
            if (lake < 0 ? !reach.EndsInSea : reach.EndLake != lake) continue;
            double half = r.Width * .5, bank = Hydrology.Bank(r.Width), surface = r.Surface - _p.SeaLevel;
            // Groove depth fades over the last stretch (sea mouths continue ~880 m out to sea).
            double tail = lake < 0 ? 880 : r.Width * 3 + 60;
            double fade = DetMath.Smooth((reach.Length - r.Along) / tail);
            double groove;
            if (_style.Revision >= 9)
            {
                // Same shape as the estuary it continues (widened channel, gentle banks), shallowing out to sea.
                half *= 1.7; bank *= 3;
                double u = r.Distance / half;
                groove = r.Distance < half ? surface - r.Depth * fade * (1 - u * u)
                    : surface + (bed - surface) * DetMath.Smooth((r.Distance - half) / bank);
            }
            else
            {
                double edge = surface - .3 * r.Depth * fade;
                groove = r.Distance < half
                    ? surface - r.Depth * fade * (1 - (r.Distance / half) * (r.Distance / half) * .7)
                    : edge + (bed - edge) * DetMath.Smooth((r.Distance - half) / bank);
            }
            result = Math.Min(result, Math.Max(groove, bed - 50));
            plume = Math.Max(plume, (1 - DetMath.Smooth((r.Distance - half) / (r.Width * 1.5 + 60))) * (.4 + .6 * fade));
        }
        return result;
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
    /// <summary>
    /// Where the water ends on this side of the river: the nominal half width varied per bank (left and right
    /// independently) by a slow widening/narrowing over hundreds of metres, a small wobble of a few metres and
    /// occasional bulges (with a shallow shelf, <paramref name="shelf"/> 0..1) or cuts into the bank. Faded out near
    /// the reach ends so junctions and mouths stay joined. The centreline itself stays smooth.
    /// </summary>
    /// <summary>
    /// Width (m) of a low sand/gravel/mud beach between the water and the rising bank on this side: present in
    /// irregular patches (each bank on its own), up to ~60 % of the bank width, none near the reach ends.
    /// </summary>
    public double BeachWidth(in RiverHit r)
    {
        if (_style.BankVariation <= 0 || r.Side == 0 || r.Reach < 0) return 0;
        var reach = _hydrology.Reaches[r.Reach];
        double margin = r.Width * 2 + 25;
        double ends = DetMath.Smooth(r.Along / margin) * DetMath.Smooth((reach.Length - r.Along) / margin);
        if (ends <= 0) return 0;
        ulong seed = _bankSeed ^ Hashing.Mix((ulong)r.Reach * 2 + (r.Side > 0 ? 1UL : 0UL) + 0xBEAC4UL);
        double w = reach.Width[reach.Width.Length / 2];
        double n = 2.5 * Noise.Gradient(r.Along / Math.Max(60, w * 6) + 2.3, 8.1, seed)
            + .5 * 2.5 * Noise.Gradient(r.Along / Math.Max(15, w * 1.5) + .7, 3.3, seed + 1);
        double amount = DetMath.Smooth((n + .1) / .9);
        return amount * Hydrology.Bank(r.Width) * .6 * Math.Min(1, _style.BankVariation) * ends;
    }

    public double WaterHalfWidth(in RiverHit r, out double shelf) => WaterHalfWidth(r, out shelf, out _);

    /// <param name="slow">The part of the returned widening that varies only over long distances (the rest is wobble,
    /// bulges and cuts).</param>
    public double WaterHalfWidth(in RiverHit r, out double shelf, out double slow)
    {
        double half = r.Width * .5;
        shelf = 0; slow = 0;
        if (_style.BankVariation <= 0 || r.Side == 0 || r.Reach < 0) return half;
        var reach = _hydrology.Reaches[r.Reach];
        double margin = r.Width * 2 + 25;
        double ends = DetMath.Smooth(r.Along / margin) * DetMath.Smooth((reach.Length - r.Along) / margin);
        if (ends <= 0) return half;
        ulong seed = _bankSeed ^ Hashing.Mix((ulong)r.Reach * 2 + (r.Side > 0 ? 1UL : 0UL));
        double s = r.Along;
        // Noise scales from one width per reach (not the local width: at tens of km along a reach, a tiny width
        // change would jump to a different part of the noise).
        double w = reach.Width[reach.Width.Length / 2];
        // ±~30 % over long distances, a wobble of ~0.4 m + 9 % of the width with a finer ripple on it, and bulges (+)
        // or cuts (−) every hundred metres or so.
        double broad = 2.5 * Noise.Gradient(s / Math.Max(300, w * 40) + 3.1, 1.7, seed) * .3;
        double wobble = 2.5 * Noise.Gradient(s / Math.Max(20, w * 2) + 5.3, 2.9, seed + 1) * (.4 + .09 * r.Width)
            + 2.5 * Noise.Gradient(s / Math.Max(7, w * .7) + 1.9, 6.1, seed + 3) * (.12 + .03 * r.Width);
        double b = 2.5 * Noise.Gradient(s / Math.Max(40, w * 4.5) + 7.7, 4.4, seed + 2);
        double bump = Math.Sign(b) * DetMath.Smooth((Math.Abs(b) - .35) / .6) * (b > 0 ? .7 : .35);
        double factor = 1 + (broad + wobble / half + bump) * _style.BankVariation * ends;
        factor = DetMath.Clamp(factor, .5, 2.1);
        slow = DetMath.Clamp(broad * _style.BankVariation * ends, -.5, 1.1) * half;
        shelf = DetMath.Smooth((factor - 1.1) / .35);
        return half * factor;
    }

    /// <summary>
    /// 0..1 over a river's last stretch before the sea or a lake (revision 9): the estuary, where the channel widens
    /// and the banks become low and gentle instead of a trench cut into the coastal plain.
    /// </summary>
    private double Estuary(in RiverHit r)
    {
        if (_style.Revision < 9 || r.Reach < 0) return 0;
        var reach = _hydrology.Reaches[r.Reach];
        if (!reach.EndsInSea && reach.EndLake < 0) return 0;
        double toMouth = (reach.EndsInSea ? CoastAlong(r.Reach) : reach.Length) - r.Along;
        double stretch = reach.Width[^1] * 8 + 200;
        return 1 - DetMath.Smooth(toMouth / stretch);
    }

    private double[]? _coastAlong;

    /// <summary>Distance along a sea-bound reach where it crosses the (raster) coastline; reaches continue out to sea.</summary>
    private static double[] NewNaN(int count) { var a = new double[count]; Array.Fill(a, double.NaN); return a; }

    private double CoastAlong(int index)
    {
        var cache = _coastAlong ??= NewNaN(_hydrology.Reaches.Count);
        double v = cache[index];
        if (!double.IsNaN(v)) return v;
        var reach = _hydrology.Reaches[index];
        int k = reach.Points.Length - 1;
        while (k > 0)
        {
            _macro.Sample(reach.Points[k].X, reach.Points[k].Z, WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out double land, out _, out _);
            if (land > 0) break;
            k--;
        }
        return cache[index] = reach.Along[k];
    }

    private double Profile(RiverHit r0, double terrain, double rise, double spacing, out double weight, out double inside)
    {
        // Estuary: the water widens and the bank flattens; the outer blend shrinks to match (ChannelProfile), so the
        // river's footprint never exceeds what the spatial index returns hits for.
        bool rev9 = _style.Revision >= 9;
        double estuary = Estuary(r0);
        // Bank variation moves the channel edge and bank on this side by `shift`, fading out across the bank, so the
        // floodplain and valley side keep their smooth shape (no streaks running away from the river).
        double nominal = r0.Width * .5 * (1 + .7 * estuary), shift = WaterHalfWidth(r0, out double shelf, out double slow) - r0.Width * .5;
        double bankWidth = Hydrology.Bank(r0.Width) * (1 + 2 * estuary);
        // Revision 9: only the slow widening and the cuts move the bank (with a fade that widens with the shift, so the
        // bank is never squeezed into a cliff); bulges are a shallow ledge carved into the lower bank below (moving the
        // bank for fast wobbles drew ribs down the valley sides).
        double bulge = 0;
        if (rev9)
        {
            double fast = DetMath.Clamp(shift - slow, -bankWidth * .6, bankWidth);
            bulge = Math.Max(fast, 0);
            shift = slow + Math.Min(fast, 0);
        }
        double fade = bankWidth * 1.5 + (rev9 ? 2 * Math.Abs(shift) : 0);
        double carry = 1 - DetMath.Smooth((r0.Distance - nominal - Math.Max(shift, 0) - bankWidth) / fade);
        var r = r0 with { Distance = Math.Max(0, r0.Distance - shift * carry) };
        double half = nominal;
        double beach = BeachWidth(r0);
        if (spacing <= 2.5 || spacing > 40)
        {
            double h = ChannelProfile(r, half, terrain, rise, out weight, out inside, -1, beach, estuary);
            if (!rev9 && shelf > 0 && inside > 0 && shift > 0) h = Shelf(h, r0, nominal, nominal + shift, shelf);
            if (bulge > 0) h = Ledge(h, r, half, bulge, ref inside);
            return h;
        }
        // Coarse LOD tiles: a mesh with vertex spacing s interpolates heights linearly, so a channel edge that is a
        // straight ramp in distance (the same slope inside and outside the waterline, ±1.5 s around it) is drawn
        // exactly where the water edge really is: smooth, not a saw-tooth of whole triangles. Channels narrower than
        // the spacing are drawn at least that wide so they stay continuous in the distance.
        half = Math.Max(half, spacing * .5);
        double band = spacing * 1.5, d = r.Distance, surface = r.Surface - _p.SeaLevel;
        double outer = ChannelProfile(r, half, terrain, rise, out weight, out inside, half + band, beach, estuary);
        inside = (half - d) / half;
        if (Math.Abs(d - half) >= band) return ChannelProfile(r, half, terrain, rise, out weight, out _, -1, beach, estuary);
        double g = Math.Max(.05, (outer - surface) / band);
        if (d >= half) return surface + g * (d - half);
        double bed = ChannelProfile(r, half, terrain, rise, out _, out _);
        return Math.Max(bed, surface - g * (half - d));
    }

    /// <summary>
    /// A bulge of the water's edge (revision 9): the lower bank out to <paramref name="bulge"/> metres beyond the
    /// channel edge sinks to a shallow ledge (wading depth) and is water; the bank behind it is unchanged.
    /// </summary>
    private double Ledge(double h, in RiverHit r, double half, double bulge, ref double inside)
    {
        double surface = r.Surface - _p.SeaLevel;
        double m = 1 - DetMath.Smooth((r.Distance - half - bulge) / 3 + .5);
        if (m <= 0) return h;
        double ledge = surface - (.2 + .08 * r.Depth) * DetMath.Smooth((half + bulge - r.Distance) / 2 + .3);
        double result = h - m * Math.Max(0, h - ledge);
        if (result < surface - .02 && inside <= 0) inside = .02 * m;
        return result;
    }

    /// <summary>A bulging bank's extra width is a shallow shelf (wading depth), not deep channel.</summary>
    private double Shelf(double bed, in RiverHit r, double nominal, double half, double shelf)
    {
        double surface = r.Surface - _p.SeaLevel, d = r.Distance;
        double ledge = surface - (.22 + .12 * r.Depth) * (1 - DetMath.Smooth((d - half * .85) / (half * .15 + .5)));
        double onShelf = DetMath.Smooth((d - nominal * .8) / Math.Max(1, nominal * .25)) * shelf;
        return DetMath.Lerp(bed, Math.Max(bed, ledge), onShelf);
    }

    private double ChannelProfile(in RiverHit r, double half, double terrain, double rise, out double weight, out double inside, double distance = -1,
        double beach = 0, double estuary = 0)
    {
        double bank = Hydrology.Bank(r.Width) * (1 + 2 * estuary), d = distance >= 0 ? distance : r.Distance, surface = r.Surface - _p.SeaLevel;
        double bankTop = surface + .6 + .15 * r.Depth; // (kept in this exact form: earlier terrain versions are bit-exact)
        if (estuary > 0) bankTop -= (.6 + .15 * r.Depth) * .6 * estuary;
        double extra = estuary > 0 ? Math.Max(0, half - r.Width * .5) + (bank - Hydrology.Bank(r.Width)) : 0; // estuary widening
        double flood = Math.Min(Hydrology.Floodplain(r.Width, rise - bankTop), Hydrology.MaxFloodplain(r.Width) - beach - extra);
        // A beach: a low, gently sloping strip (to ~0.3 m above the water) before the bank rises; everything beyond
        // the waterline moves outward by its width.
        double lift = 0;
        if (beach > 0 && d > half)
        {
            lift = .3 * DetMath.Smooth((d - half) / beach);
            // Revision 9: the beach pushes only the bank outward, fading out behind it (the beach width changes every
            // few tens of metres; shifting the whole valley side drew ribs down it).
            double take = _style.Revision >= 9 ? beach * (1 - DetMath.Smooth((d - half - beach - bank) / (bank * 1.5 + 2 * beach))) : beach;
            d = Math.Max(half, d - take);
            if (d == half) { weight = 1; inside = 0; return surface + lift; }
            bankTop = Math.Max(bankTop, surface + lift + .05);
        }
        weight = 1 - DetMath.Smooth((d - half - bank) / flood);
        inside = (half - d) / half;
        if (d < half) { double u = d / half; return surface - r.Depth * (1 - u * u); }
        // Bank: rises to just above the water, quickly where the ground beside is high, as a gentle levee ramp where
        // it is low; blended by that ground height (no max(): it creased the banks wherever the two curves crossed).
        if (d < half + bank)
        {
            double gentle = DetMath.Smooth((d - half) / bank), quick = DetMath.Smooth((d - half) / Math.Min(bank, 2.5));
            double high = DetMath.Clamp((Math.Min(terrain, bankTop) - surface) / (bankTop - surface), 0, 1) * (1 - estuary);
            double from = surface + lift;
            return from + (bankTop - from) * DetMath.Lerp(gentle, quick, high);
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
