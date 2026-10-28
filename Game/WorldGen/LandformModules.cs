using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

/// <summary>Big landforms shape the plan's macro raster before erosion and rivers (rivers then flow round, cut through
/// or fill them); local ones shape the ground at walking scale before the water stages (water carves through them).</summary>
public enum LandformKind { Plan, Ground }

/// <summary>What decides where a module's shapes appear: the biome's terrain features, the plan's height and slope, its
/// rivers and lakes, the coast, or a step of the world type's land shape.</summary>
public enum LandformRule { Biome, Terrain, Water, Coast, ShapeStep }

/// <summary>A world's setting for one landform module: <see cref="Amount"/> 0 = off, 1 = as designed (more = more
/// often); <see cref="Size"/> 1 = as designed. At (1, 1) a module gives exactly its designed ground.</summary>
public readonly record struct LandformSetting(double Amount, double Size)
{
    public static readonly LandformSetting Designed = new(1, 1);
    public bool Off => Amount <= 0;
}

/// <summary>A landform module as the catalogue and New world know it (docs/WORLDGEN_MODULES_DESIGN.md, D228/D229).</summary>
/// <param name="Since">Terrain revision that brought its ground (its settings work from <see cref="LandformCatalog.SettingsSince"/>).</param>
public sealed record LandformInfo(string Id, string Name, string Where, LandformKind Kind, LandformRule Rule, int Since);

/// <summary>
/// The landform modules. Rules every module keeps: it shapes the ground only inside its own areas; water wins (it never
/// fills a lake, blocks a river or rises out of the sea: one invariant test each); it draws from its own random stream,
/// so switching one off never moves anything else; its settings travel with the world; retuning it is a new terrain
/// revision.
/// </summary>
public static class LandformCatalog
{
    /// <summary>First terrain revision whose worlds read the module settings.</summary>
    public const int SettingsSince = 17;

    /// <summary>The amount steps of New world and their values.</summary>
    public static readonly string[] AmountNames = { "off", "rare", "normal", "common", "everywhere" };
    public static readonly double[] AmountValues = { 0, .4, 1, 1.6, 2.5 };
    public const int DesignedAmount = 2;

    public static readonly LandformInfo Dunes = new("dunes", "Dunes", "in sandy deserts", LandformKind.Ground, LandformRule.Biome, 12);
    public static readonly LandformInfo Mesas = new("mesas", "Mesas and buttes", "in badlands", LandformKind.Ground, LandformRule.Biome, 12);
    public static readonly LandformInfo Spires = new("spires", "Needle spires", "in needle rocks and badlands", LandformKind.Ground, LandformRule.Biome, 12);

    public static readonly LandformInfo Escarpments = new("escarpments", "Escarpments and plateaus", "inland, away from mountains", LandformKind.Plan, LandformRule.Terrain, 18);

    public static readonly LandformInfo Waterfalls = new("waterfalls", "Waterfalls", "where rivers drop over a step", LandformKind.Plan, LandformRule.Water, 20);

    public static readonly LandformInfo Canyons = new("canyons", "Gorges and canyons", "where rivers cross raised land", LandformKind.Plan, LandformRule.Water, 21);

    public static readonly LandformInfo Cirques = new("cirques", "Cirques with a tarn", "high on mountain flanks, mostly facing north", LandformKind.Plan, LandformRule.Terrain, 23);

    public static readonly LandformInfo Troughs = new("troughs", "Glacial troughs, hanging valleys", "along steep mountain rivers", LandformKind.Plan, LandformRule.Water, 24);

    public static readonly IReadOnlyList<LandformInfo> All = new[] { Escarpments, Waterfalls, Canyons, Cirques, Troughs, Dunes, Mesas, Spires };

    /// <summary>The big landforms of a world, in order, run on the macro raster before erosion (each from the terrain
    /// revision that brought it, unless switched off).</summary>
    public static IReadOnlyList<PlanLandform> PlanModules(WorldManifest manifest, TerrainStyle style)
    {
        var list = new List<PlanLandform>();
        if (style.Revision >= Escarpments.Since && !style.Landform(Escarpments.Id).Off) list.Add(new Escarpments(style.Revision));
        return list;
    }

    /// <summary>The random stream of a module (new modules; the revision-12 three keep their old streams).</summary>
    public static ulong Seed(WorldManifest manifest, LandformInfo info) => Hashing.FeatureSeed(manifest.Seed, 5, manifest.ProfileDigest, "landform." + info.Id);
}

/// <summary>A big landform: changes the plan's land, height and mountain channels inside its areas, before erosion.</summary>
public abstract class PlanLandform
{
    public abstract LandformInfo Info { get; }
    public abstract void Shape(MacroRaster macro, WorldManifest manifest, LandformSetting setting);
    /// <summary>Walking-scale relief it adds at a point (m), on top of what the raster holds; 0 by default.
    /// <paramref name="rock"/>: how bare the ground is there (0 none … 1 cliff rock), for the ground's look.</summary>
    public virtual double Ground(double x, double z, double minWavelength, out double rock) { rock = 0; return 0; }
}

/// <summary>A local landform: adds to the walking-scale relief where its rule allows, before the water stages.</summary>
public abstract class GroundLandform
{
    protected GroundLandform(LandformSetting setting) { Setting = setting; }
    public abstract LandformInfo Info { get; }
    public LandformSetting Setting { get; }
    public bool Off => Setting.Off;
}
