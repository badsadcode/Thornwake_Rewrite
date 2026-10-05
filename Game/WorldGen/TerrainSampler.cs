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
    private readonly ulong _detailSeed, _rockSeed, _coastSeed, _regionSeed, _bankSeed;
    private readonly double _coastBand;

    public TerrainSampler(WorldPlan plan)
    {
        Plan = plan; _p = plan.Profile; _style = plan.Style; _macro = plan.Macro; _hydrology = plan.Hydrology;
        _ox = plan.Manifest.OriginX; _oz = plan.Manifest.OriginZ;
        var m = plan.Manifest;
        ulong Seed(string layer) => Hashing.FeatureSeed(m.Seed, 100 + WorldManifest.CurrentGenerator, m.ProfileDigest, layer);
        _detailSeed = Seed("detail"); _rockSeed = Seed("rock"); _coastSeed = Seed("coast"); _regionSeed = Seed("region"); _bankSeed = Seed("banks");
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
            double terrain = Math.Max(baseHeight + detail * damp, floor);
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

        double lakePlume = 0;
        if (water == WaterKind.None && _style.Revision >= 10)
        {
            // Revision 10: the lake fills every point below its surface within ~1 cell of its cells (round reach);
            // between 1 and 1.6 cells the ground rises to a low shore just above the water, so no dry ground below
            // the surface is left where the reach ends (build 11: lakes cut off along straight cell lines).
            int lake = _hydrology.LakeDistance(x, z, out double cells);
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
