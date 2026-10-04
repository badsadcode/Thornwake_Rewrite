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
/// Do not reorder or add properties here: new tuning belongs in <see cref="TerrainStyle"/>.
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

    public void Validate()
    {
        static bool Range(double x, double min, double max) => double.IsFinite(x) && x >= min && x <= max;
        if (string.IsNullOrWhiteSpace(Id) || Id.Length > 64 || Id.Any(c => !(c is >= 'a' and <= 'z' or >= '0' and <= '9' or '-' or '_' or '.')))
            throw new InvalidDataException("Invalid world profile id.");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 80) throw new InvalidDataException("Invalid world profile name.");
        if (!Range(WorldSize, 16000, 4096000) || Continents < 1 || Continents > 8 || Islands < 0 || Islands > 128 ||
            !Range(ContinentRadius, WorldSize * .035, WorldSize * .28) || !Range(IslandRadius, WorldSize * .002, WorldSize * .025) ||
            !Range(CoastRoughness, 0, .3) || !Range(SeaLevel, -1000, 1000) || !Range(OceanDepth, 50, 8000) ||
            !Range(LowlandHeight, 10, 800) || !Range(HillHeight, 0, 800) || !Range(HillScale, 500, WorldSize * .1) ||
            RangesPerContinent < 0 || RangesPerContinent > 6 || !Range(MountainHeight, 0, 6000) ||
            !Range(MountainWidth, 500, ContinentRadius * .5))
            throw new InvalidDataException("Unsupported world profile dimensions.");
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
