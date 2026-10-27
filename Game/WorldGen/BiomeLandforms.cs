using System;

namespace Thornwake.WorldGen;

/// <summary>
/// The biome landforms of terrain revision 12 (dunes, mesas, needle spires), moved out of <see cref="TerrainSampler"/>
/// unchanged (D227): the first of the landform modules to come (docs/WORLDGEN_PLAN.md).
/// </summary>
public sealed class BiomeLandforms
{
    private readonly Hydrology _hydrology;
    private readonly ulong _landformSeed;
    private readonly double _duneAngle;

    public BiomeLandforms(Hydrology hydrology, ulong landformSeed, double duneAngle)
    {
        _hydrology = hydrology; _landformSeed = landformSeed; _duneAngle = duneAngle;
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
    public double Apply(double x, double z, double lx, double lz, double baseHeight, double detail, double inland, double minWavelength, in BiomeTerrainMods mods)
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
}
