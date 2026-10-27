using System;

namespace Thornwake.WorldGen;

/// <summary>
/// The biome landforms of terrain revision 12 (dunes, mesas, needle spires): the ground modules that follow the biomes'
/// terrain features (<see cref="LandformRule.Biome"/>). Moved out of <see cref="TerrainSampler"/> unchanged (D227), split
/// into modules with their own amount and size (revision 17, D229): at the designed settings every value is the same as
/// before (a setting of 1 multiplies or adds nothing).
/// </summary>
public sealed class BiomeLandforms
{
    private readonly Hydrology _hydrology;
    private readonly DuneField _dunes;
    private readonly MesaLands _mesas;
    private readonly SpireRocks _spires;

    public BiomeLandforms(Hydrology hydrology, ulong landformSeed, double duneAngle, TerrainStyle style)
    {
        _hydrology = hydrology;
        _dunes = new DuneField(style.Landform(LandformCatalog.Dunes.Id), landformSeed, duneAngle);
        _mesas = new MesaLands(style.Landform(LandformCatalog.Mesas.Id), landformSeed);
        _spires = new SpireRocks(style.Landform(LandformCatalog.Spires.Id), landformSeed);
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
        if (mods.Dunes > 0 && !_dunes.Off)
        {
            double s = Math.Min(1, mods.Dunes) * fade;
            detail = detail * (1 - .6 * s) + _dunes.Height(lx, lz, minWavelength) * s;
        }
        // Spire height fades out across the edge of spire biomes (a faint trace of one gave rare full-size needles).
        if (mods.Spires > 0 && !_spires.Off)
            detail += _spires.Height(lx, lz, minWavelength, Math.Min(1.5, mods.Spires) * _spires.Setting.Amount) * fade * DetMath.Smooth(mods.SpirePresence / .6);
        if (mods.Mesas > 0 && !_mesas.Off)
        {
            // Table-lands raised out of the land (mesas, buttes), stepped into cliff-sided tiers. Only the uplift is
            // terraced: terracing the regional slope itself gave rice-paddy contour benches.
            double s = Math.Min(1, mods.Mesas) * fade;
            double up = _mesas.Uplift(lx, lz, minWavelength);
            // (× a ramp over the first 4 m: the terrace of a near-zero uplift is not exactly zero; no step at the foot.)
            if (up > 0) detail += Math.Max(0, _mesas.Terrace(up, lx, lz, minWavelength)) * DetMath.Smooth(up / 4) * s;
        }
        return detail;
    }
}

/// <summary>Dunes (Biome rule: "dunes" feature). Amount: more or fewer dune fields between calm sand sheets; size: the
/// spacing and height of the ridges.</summary>
public sealed class DuneField : GroundLandform
{
    private readonly ulong _landformSeed;
    private readonly double _duneAngle, _spacing, _fieldBias, _size;

    public DuneField(LandformSetting setting, ulong landformSeed, double duneAngle) : base(setting)
    {
        _landformSeed = landformSeed; _duneAngle = duneAngle;
        _size = setting.Size;
        _spacing = 90 * setting.Size;
        _fieldBias = .55 + .5 * (setting.Amount - 1);
    }

    public override LandformInfo Info => LandformCatalog.Dunes;

    public double Height(double lx, double lz, double minWavelength)
    {
        double spacing = _spacing;
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
        double field = DetMath.Smooth(_fieldBias + 1.1 * Noise.Fbm(lx, lz, 4000, 2, minWavelength, seed + 3));
        double crest = .45 + .55 * DetMath.Smooth(.5 + 1.6 * Noise.Fbm(across, along, spacing * 3.5, 2, minWavelength, seed + 4));
        return (4 + 8 * field) * crest * profile * lod * _size;
    }
}

/// <summary>Mesas and buttes (Biome rule: "mesas" feature). Amount: how much of the land is raised into table-lands;
/// size: their height and the height of their tiers.</summary>
public sealed class MesaLands : GroundLandform
{
    private readonly ulong _landformSeed;
    private readonly double _threshold, _size;

    public MesaLands(LandformSetting setting, ulong landformSeed) : base(setting)
    {
        _landformSeed = landformSeed;
        _size = setting.Size;
        _threshold = .02 - .5 * (setting.Amount - 1);
    }

    public override LandformInfo Info => LandformCatalog.Mesas;

    // Flat-topped uplands 40–110 m high with steep edges: patches where a noise exceeds a threshold, so they break into
    // mesas, buttes and long benches; ~half the land stays plain.
    public double Uplift(double lx, double lz, double minWavelength)
    {
        // Outline noise with all its octaves at every detail level: far tiles must show the same mesas (no shifting
        // cliffs as you approach).
        minWavelength = Math.Min(minWavelength, 150);
        ulong seed = _landformSeed + 210;
        double n = Noise.Fbm(lx, lz, 1400, 3, minWavelength, seed, .5);
        double height = (40 + 70 * DetMath.Smooth(.5 + Noise.Fbm(lx, lz, 6000, 2, minWavelength, seed + 1))) * _size;
        return height * DetMath.Smooth((n - _threshold) / .07);
    }

    public double Terrace(double h, double lx, double lz, double minWavelength)
    {
        minWavelength = Math.Min(minWavelength, 150); // tiers stay put at every detail level
        ulong seed = _landformSeed + 200;
        double step = (12 + 9 * DetMath.Smooth(.5 + Noise.Fbm(lx, lz, 5000, 2, minWavelength, seed))) * _size;
        double offset = step * .35 * Noise.Fbm(lx, lz, 700, 3, minWavelength, seed + 1); // tier heights wander
        if (h <= 0) return h;
        double t = (h + offset) / step, k = Math.Floor(t), f = t - k;
        // Tread rises only 15 % of a step over its width; the riser (last 18 % of the height range) takes the rest.
        const double riser = .18;
        double g = .15 * f + .85 * DetMath.Smooth((f - (1 - riser)) / riser);
        return (k + g) * step - offset;
    }
}

/// <summary>Needle spires (Biome rule: "spires" feature). Amount: how many cells hold a spire; size: their width,
/// height and spacing.</summary>
public sealed class SpireRocks : GroundLandform
{
    private readonly ulong _landformSeed;
    private readonly double _size, _cell;

    public SpireRocks(LandformSetting setting, ulong landformSeed) : base(setting)
    {
        _landformSeed = landformSeed;
        _size = setting.Size;
        _cell = 36 * setting.Size;
    }

    public override LandformInfo Info => LandformCatalog.Spires;

    public double Height(double lx, double lz, double minWavelength, double density)
    {
        double cell = _cell;
        ulong seed = _landformSeed + 300;
        long ci = (long)Math.Floor(lx / cell), cj = (long)Math.Floor(lz / cell);
        double best = 0;
        for (long dj = -1; dj <= 1; dj++)
            for (long di = -1; di <= 1; di++)
            {
                ulong h = Hashing.Lattice(ci + di, cj + dj, seed);
                if (Hashing.Unit(h) > .45 * density) continue;
                double cx = (ci + di + .2 + .6 * Hashing.Unit(Hashing.Mix(h + 1))) * cell, cz = (cj + dj + .2 + .6 * Hashing.Unit(Hashing.Mix(h + 2))) * cell;
                double r = (3 + 6 * Hashing.Unit(Hashing.Mix(h + 3))) * _size;
                double u = Hashing.Unit(Hashing.Mix(h + 4)), height = Math.Max((12 + 43 * u * u) * _size, r * 4.2); // needles, not stumps
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
