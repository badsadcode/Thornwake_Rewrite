using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;

namespace Thornwake.WorldGen;

/// <summary>
/// World-shape preset (Supplement §2.1.1). Same JSON schema and property order as A4's <c>MacroWorldProfile</c>
/// (<c>Content/world_profiles.json</c>), so the digest — and therefore every seed derived from it — is identical.
/// Do not reorder or add properties here: new tuning belongs in <see cref="TerrainStyle"/>. The one exception is
/// <see cref="Landforms"/>, which is left out of the JSON when null so every existing digest stays the same.
/// </summary>
public sealed record WorldProfile
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required double WorldSize { get; init; }
    public required int Continents { get; init; }
    public required int Islands { get; init; }
    public required double ContinentRadius { get; init; }
    public required double IslandRadius { get; init; }
    public required double CoastRoughness { get; init; }
    public required double SeaLevel { get; init; }
    public required double OceanDepth { get; init; }
    public required double LowlandHeight { get; init; }
    public required double HillHeight { get; init; }
    public required double HillScale { get; init; }
    public required int RangesPerContinent { get; init; }
    public required double MountainHeight { get; init; }
    public required double MountainWidth { get; init; }
    /// <summary>Optional landform template of a world type (D36); null = none (A4-compatible digest).</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public Landform[]? Landforms { get; init; }
    /// <summary>Optional heightmap template ("natural" world types, D38): replaces the placed continents; null = none.</summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public string[]? Heightmap { get; init; }

    public void Validate()
    {
        static bool Range(double x, double min, double max) => double.IsFinite(x) && x >= min && x <= max;
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64 || Id.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.')))
            throw new InvalidDataException("Invalid world profile id.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80) throw new InvalidDataException("Invalid world profile name.");
        if (!Range(WorldSize, 16000, 4096000) || Continents < (Landforms is { Length: > 0 } || Heightmap is { Length: > 0 } ? 0 : 1) || Continents > 16 || Islands < 0 || Islands > 400 ||
            !Range(ContinentRadius, WorldSize * .035, WorldSize * .28) || !Range(IslandRadius, WorldSize * .002, WorldSize * .025) ||
            !Range(CoastRoughness, 0, .3) || !Range(SeaLevel, -1000, 1000) || !Range(OceanDepth, 50, 8000) ||
            !Range(LowlandHeight, 10, 800) || !Range(HillHeight, 0, 800) || !Range(HillScale, 500, WorldSize * .1) ||
            RangesPerContinent < 0 || RangesPerContinent > 6 || !Range(MountainHeight, 0, 6000) ||
            !Range(MountainWidth, 500, ContinentRadius * .5))
            throw new InvalidDataException("Unsupported world profile dimensions.");
        WorldGen.Landforms.Validate(Landforms);
        WorldGen.Heightmap.Validate(Heightmap);
    }

    /// <summary>SHA-256 of the canonical JSON (A4-compatible).</summary>
    public string Digest()
    {
        Validate();
        return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this))).ToLowerInvariant();
    }

    public static IReadOnlyList<WorldProfile> LoadCatalog(string path) => ParseCatalog(File.ReadAllText(path));

    /// <summary>Parse catalogue JSON (the game reads it through Godot's FileAccess so it works inside a .pck).</summary>
    public static IReadOnlyList<WorldProfile> ParseCatalog(string json)
    {
        try
        {
            var profiles = JsonSerializer.Deserialize<WorldProfile[]>(json) ?? throw new InvalidDataException("Empty profile catalogue.");
            if (profiles.Length is < 1 or > 64 || profiles.Select(p => p.Id).Distinct().Count() != profiles.Length)
                throw new InvalidDataException("Invalid or duplicate world profiles.");
            foreach (var profile in profiles) profile.Validate();
            return profiles;
        }
        catch (JsonException error) { throw new InvalidDataException("Damaged profile catalogue.", error); }
    }
}

/// <summary>
/// Terrain tuning that is not part of the frozen A4 profile schema. Derived from the profile by default;
/// becomes data-driven (landform library, Supplement §3.2) in M3.
/// </summary>
public sealed record TerrainStyle
{
    /// <summary>Amplitude (m) of walking-scale rolling relief on lowland at full roughness.</summary>
    public double LowlandDetail { get; init; } = 30;
    /// <summary>Wavelength (m) of the largest detail octave.</summary>
    public double DetailWavelength { get; init; } = 1600;
    /// <summary>Amplitude ratio between detail octaves (higher = rougher small-scale ground).</summary>
    public double DetailGain { get; init; } = .6;
    /// <summary>Roughness multiplier in the flattest plains (1 = hill country everywhere).</summary>
    public double PlainsRoughness { get; init; } = .4;
    /// <summary>Wavelength (m) over which plains and hill country alternate.</summary>
    public double RegionWavelength { get; init; } = 28000;
    /// <summary>Fraction of mountain height added as rocky ridged detail at full mountain influence.</summary>
    public double MountainDetail { get; init; } = .05;
    /// <summary>Typical sideways shift (m) of the coastline by sub-raster coast noise.</summary>
    public double CoastDetailShift { get; init; } = 260;

    // ---- erosion and hydrology (M2)
    /// <summary>Stream-power coefficient K·dt (per √m² of catchment per metre of flow length).</summary>
    public double ErosionStrength { get; init; } = .006;
    public int ErosionIterations { get; init; } = 10;
    /// <summary>Depressions shallower than this (m) are filled instead of becoming lakes.</summary>
    public double LakeMinDepth { get; init; } = 8;
    /// <summary>Depressions smaller than this many raster cells are filled instead of becoming lakes.</summary>
    public int LakeMinCells { get; init; } = 4;
    /// <summary>Maximum share of land covered by lakes (largest by volume kept first).</summary>
    public double LakeShare { get; init; } = .03;
    /// <summary>Catchment (km²) at which a river starts (smaller streams are later regional/local detail).</summary>
    public double RiverMinCatchmentKm2 { get; init; } = 30;
    /// <summary>Channel width = this × √(catchment km²), clamped.</summary>
    public double RiverWidthPerSqrtKm2 { get; init; } = .9;
    public double RiverMinWidth { get; init; } = 4;
    public double RiverMaxWidth { get; init; } = 320;
    /// <summary>Minimum water-surface fall per metre of river (keeps flow direction unambiguous).</summary>
    public double RiverMinSlope { get; init; } = .00005;
    /// <summary>Width multiplier at a river mouth (estuary).</summary>
    public double RiverMouthWidening { get; init; } = 3;
    /// <summary>Rivers with at least this catchment (km²) form a delta on flat coasts.</summary>
    public double DeltaMinCatchmentKm2 { get; init; } = 1500;

    // ---- vegetation and objects (world data: what exists in the world, not a graphics option)
    /// <summary>How much of the land is forest (patch size) relative to the default; 0 = open land.</summary>
    public double ForestCover { get; init; } = 1.15;
    /// <summary>Trees per area inside forests relative to the default.</summary>
    public double ForestDensity { get; init; } = 1.2;
    /// <summary>Amount of stones, rocks, boulders and outcrops.</summary>
    public double RockAmount { get; init; } = 1;
    /// <summary>Amount of bushes, berry bushes, stumps and fallen logs.</summary>
    public double PropAmount { get; init; } = 1;

    /// <summary>Terrain revision for changes without their own knob: 8 = as in build 8; 9 = estuaries at mouths and
    /// banks that stay walkable where they bulge (round 5); 10 = lakes reach round and close with a shore instead of
    /// ending on straight cell lines (round 7); 11 = wetland biomes carve real standing-water pools and flatten their
    /// ground (round 8); 12 = biome landforms: dunes, mesas, needle spires (round 9).</summary>
    public int Revision { get; init; } = 12;

    /// <summary>Climate of the world (latitudes, warmer/colder, wetter/drier): drives biomes (D42).</summary>
    public ClimateSettings Climate { get; init; } = new();

    /// <summary>River mouths blend into sea and lake beds (groove continues into the bed, no levees in the
    /// estuary). 0 = as in preview 7.</summary>
    public double MouthBlend { get; init; } = 1;

    /// <summary>Steepness of the shore (sea floor and beach) relative to ~3.5 %; 0 = the macro ramp only (build 5).</summary>
    public double ShoreSlope { get; init; } = 1;
    /// <summary>River bank irregularity (left and right banks vary independently: broad widening and narrowing, small
    /// wobble, occasional bulges with shallow shelves and cuts). 0 = clean banks.</summary>
    public double BankVariation { get; init; } = 1;
    /// <summary>Meander amplitude multiplier (0 = straight between bends).</summary>
    public double MeanderStrength { get; init; } = 1;
    /// <summary>Multiplier on the valley-scale bends and kilometre-scale wander of rivers.</summary>
    public double WanderStrength { get; init; } = 1;
    /// <summary>Multiplier on along-river width variation (pools, narrows, broad stretches).</summary>
    public double WidthVariation { get; init; } = 1;

    public static TerrainStyle For(WorldProfile profile) => new()
    {
        LowlandDetail = 20 + profile.HillHeight * .05,
    };
}

/// <summary>Immutable header saved with a world. Generator version gates every change in generated output.</summary>
public sealed record WorldManifest
{
    /// <summary>Rewrite generator family. Version 1 = M1 (A4 layout + raster + walking-scale detail).</summary>
    public const int CurrentGenerator = 1;
    public required int Format { get; init; }
    public required int Generator { get; init; }
    public required string WorldId { get; init; }
    public required int Seed { get; init; }
    public required double OriginX { get; init; }
    public required double OriginZ { get; init; }
    public required WorldProfile Profile { get; init; }
    public required string ProfileDigest { get; init; }

    public static WorldManifest Create(int seed, WorldProfile profile, double originX = 0, double originZ = 0, string? worldId = null)
    {
        var manifest = new WorldManifest
        {
            Format = 1, Generator = CurrentGenerator, WorldId = worldId ?? Guid.NewGuid().ToString("N"), Seed = seed,
            OriginX = originX, OriginZ = originZ, Profile = profile, ProfileDigest = profile.Digest(),
        };
        manifest.Validate();
        return manifest;
    }

    public void Validate()
    {
        if (Format != 1 || Generator != CurrentGenerator || !Guid.TryParseExact(WorldId, "N", out _) || Profile == null)
            throw new InvalidDataException("Unsupported or damaged world manifest.");
        Profile.Validate();
        if (ProfileDigest != Profile.Digest()) throw new InvalidDataException("Frozen world profile digest does not match.");
        double limit = 1.5e10;
        if (!double.IsFinite(OriginX) || !double.IsFinite(OriginZ) || Math.Abs(OriginX) > limit || Math.Abs(OriginZ) > limit)
            throw new InvalidDataException("World origin out of range.");
    }
}
