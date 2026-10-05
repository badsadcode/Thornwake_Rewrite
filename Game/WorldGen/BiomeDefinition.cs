using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

// Biome definitions (D42, docs/BIOMES_PLAN.md): data, not code. Every section and field is optional; the defaults
// make a plain temperate biome. Built-in definitions live in Content/biomes.json, custom ones (World Site Editor,
// or hand-written) in the user folder's biomes/*.json; both use exactly this format. Asset references name ASSET
// SETS (Content/asset_sets.json), never single assets, so adding a tree to a set needs no biome edit.

/// <summary>A biome: identity, where it generates, how it looks, how it shapes local terrain, hooks for later.</summary>
public sealed record BiomeDefinition
{
    // ---- identity
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string[] Tags { get; init; } = Array.Empty<string>();
    /// <summary>Included in the procedural generation pool (off: defined, usable by hand, never generated).</summary>
    public bool InPool { get; init; } = true;

    public BiomePlacement Placement { get; init; } = new();
    public BiomeAppearance Appearance { get; init; } = new();
    public BiomeVegetation Vegetation { get; init; } = new();
    public BiomeProps Props { get; init; } = new();
    public BiomeTerrain Terrain { get; init; } = new();
    public BiomeWater Water { get; init; } = new();
    public BiomeAtmosphere Atmosphere { get; init; } = new();
    public BiomeGameplay Gameplay { get; init; } = new();
}

/// <summary>
/// Where the biome generates. Ranges are preferred intervals with soft edges (null = anything). Fitness is the
/// product of the memberships times <see cref="Weight"/>; the best few biomes blend.
/// </summary>
public sealed record BiomePlacement
{
    /// <summary>Procedural weight / rarity (1 = normal, 0.2 = rare, 2 = common).</summary>
    public double Weight { get; init; } = 1;
    /// <summary>Annual mean temperature, °C (edges blend over ~3 °C).</summary>
    public double[]? Temperature { get; init; }
    /// <summary>Precipitation, mm/year (edges blend over ~25 %).</summary>
    public double[]? Moisture { get; init; }
    /// <summary>Height above sea level, m (edges blend over ~150 m).</summary>
    public double[]? Elevation { get; init; }
    /// <summary>Ground slope (rise per metre over ~1 km) (edges blend over 0.03).</summary>
    public double[]? Slope { get; init; }
    /// <summary>Share of the large-scale mountain height (0 lowland … 1 mountain core).</summary>
    public double[]? Mountain { get; init; }
    /// <summary>Latitude, degrees (edges blend over 4°).</summary>
    public double[]? Latitude { get; init; }
    /// <summary>−1 avoids … +1 seeks the sea coast (within ~30 km).</summary>
    public double Coast { get; init; }
    /// <summary>−1 avoids … +1 seeks rivers and lakes (within ~5 km).</summary>
    public double River { get; init; }
    /// <summary>Typical region size range, km: sets the scale of the noise that shapes this biome's regions.</summary>
    public double[] RegionSize { get; init; } = { 30, 250 };
    /// <summary>A rare patch biome inside others (e.g. Ancient forest inside temperate forest); null = climate biome.</summary>
    public BiomePatch? Patch { get; init; }
    /// <summary>Biome ids this one likes next to it (slight boost near them).</summary>
    public string[] PreferNeighbours { get; init; } = Array.Empty<string>();
    /// <summary>Biome ids this one must not touch (suppressed near them).</summary>
    public string[] ForbiddenNeighbours { get; init; } = Array.Empty<string>();
}

/// <summary>A biome that appears as scattered patches inside host biomes (fantasy and special biomes).</summary>
public sealed record BiomePatch
{
    /// <summary>Host biome ids (empty = any land that passes this biome's own preferences).</summary>
    public string[] Within { get; init; } = Array.Empty<string>();
    /// <summary>Share of the hosts' area the patches cover (0..1).</summary>
    public double Coverage { get; init; } = .15;
}

/// <summary>How the ground looks (colour palettes now; textures/material sets later).</summary>
public sealed record BiomeAppearance
{
    /// <summary>Ground material set id (Content/asset_sets.json, kind "ground").</summary>
    public string Ground { get; init; } = "TemperateGround";
    /// <summary>Bare rock showing through (0 = only on steep slopes … 1 = rocky everywhere).</summary>
    public double RockExposure { get; init; }
    /// <summary>Darker, moodier ground and canopy (0 … 1).</summary>
    public double Darkness { get; init; }
    /// <summary>Colour on the biome maps (hex rrggbb).</summary>
    public string MapColor { get; init; } = "6e9a4e";
}

public sealed record BiomeVegetation
{
    /// <summary>Tree sets with weights (set id → weight).</summary>
    public Dictionary<string, double> Trees { get; init; } = new() { ["BroadleafTrees"] = 1 };
    /// <summary>Forest amount (0 none … 1 normal … 2 dense old growth).</summary>
    public double TreeDensity { get; init; } = 1;
    /// <summary>Tree size range (scale).</summary>
    public double[] TreeSize { get; init; } = { .8, 1.25 };
    /// <summary>Grass and low plants (0 bare … 1 normal … 1.5 lush).</summary>
    public double Grass { get; init; } = 1;
    public string? Bushes { get; init; }
    public string? Flowers { get; init; }
    public string? GroundPlants { get; init; }
    public string? Vines { get; init; }
    public double Moss { get; init; }
    public string? AquaticPlants { get; init; }
    public double DeadVegetation { get; init; }
}

public sealed record BiomeProps
{
    /// <summary>Stones, rocks and boulders (multiplier).</summary>
    public double Rocks { get; init; } = 1;
    /// <summary>Bushes, stumps, fallen logs, branches (multiplier).</summary>
    public double Logs { get; init; } = 1;
    public string? Special { get; init; }
}

/// <summary>Local terrain character (never the large-scale shape). 1 = unchanged.</summary>
public sealed record BiomeTerrain
{
    /// <summary>Walking-scale roughness (rolling ground) multiplier.</summary>
    public double Roughness { get; init; } = 1;
    /// <summary>0 … 1: flattens local relief (swamps, wetlands).</summary>
    public double Flatten { get; init; }
    /// <summary>Small bumps and hummocks multiplier.</summary>
    public double Microterrain { get; init; } = 1;
    /// <summary>Rocky crags on slopes multiplier.</summary>
    public double Crags { get; init; } = 1;
    /// <summary>Landform features with strengths (later phases: "spires", "ravines", "dunes", "mesas", "volcano"…).</summary>
    public Dictionary<string, double> Features { get; init; } = new();
}

public sealed record BiomeWater
{
    /// <summary>Ground wetness (mud, puddles, damp colours) 0 … 1.</summary>
    public double Wetness { get; init; }
    public double LakeAffinity { get; init; }
    public double MarshAffinity { get; init; }
    /// <summary>Water tint (hex) for rivers and lakes in this biome; null = default.</summary>
    public string? WaterTint { get; init; }
    /// <summary>How strongly <see cref="WaterTint"/> colours the water (0 … 1).</summary>
    public double WaterTintStrength { get; init; } = .65;
}

public sealed record BiomeAtmosphere
{
    /// <summary>Biome skies option: multiplies the sky colours (hex rrggbb, null = the realistic sky).</summary>
    public string? SkyTint { get; init; }
    /// <summary>Biome skies option: extra haze toward the horizon and denser distance fog (0 … 1).</summary>
    public double Haze { get; init; }
    public double Fog { get; init; }
    public double Mist { get; init; }
    public string? AmbientTint { get; init; }
    public string? Sky { get; init; }
    public string? Wind { get; init; }
    public double PrecipitationBias { get; init; } = 1;
    public string? Audio { get; init; }
}

/// <summary>Hooks for later systems (spawns, resources, settlements, roads, points of interest).</summary>
public sealed record BiomeGameplay
{
    public string[] Creatures { get; init; } = Array.Empty<string>();
    public string[] Monsters { get; init; } = Array.Empty<string>();
    public string[] Resources { get; init; } = Array.Empty<string>();
    public double SettlementAffinity { get; init; } = 1;
    public double RoadAffinity { get; init; } = 1;
    public string[] Poi { get; init; } = Array.Empty<string>();
}

/// <summary>
/// A reusable named set of assets (trees, bushes, rocks, ground materials …). Items are asset names with weights;
/// for kind "ground" the colours describe the material palette until real ground materials exist.
/// </summary>
public sealed record AssetSet
{
    public string Id { get; init; } = "";
    /// <summary>trees, bushes, grass, flowers, rocks, boulders, logs, aquatic, props, ground …</summary>
    public string Kind { get; init; } = "";
    public Dictionary<string, double> Items { get; init; } = new();
    // Ground palettes (hex rrggbb).
    public string? Grass { get; init; }
    public string? Dry { get; init; }
    public string? Soil { get; init; }
    public string? ForestFloor { get; init; }
    public string? Rock { get; init; }
    /// <summary>Ground snow cover 0..1 (ice and glacier sets).</summary>
    public double Snow { get; init; }
}
