using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Separate opt-in recipe: the existing macro manifest and algorithm-3 terrain stay unchanged.</summary>
public sealed record MacroDrainageRecipe
{
    public required int Format { get; init; }
    public required int DrainageVersion { get; init; }
    public required int Resolution { get; init; }
    public required MacroWorldManifest World { get; init; }
    public static MacroDrainageRecipe Create(MacroWorldManifest world, int resolution = 128)
    {
        var recipe = new MacroDrainageRecipe { Format = 1, DrainageVersion = DrainageAnalysis.Algorithm, Resolution = resolution, World = world };
        recipe.Validate(); return recipe;
    }
    public void Validate()
    {
        if (Format != 1 || DrainageVersion != DrainageAnalysis.Algorithm || Resolution < 16 || Resolution > 512 || World == null)
            throw new InvalidDataException("Unsupported drainage recipe.");
        World.Validate();
    }
    public string Digest()
    {
        Validate(); return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this))).ToLowerInvariant();
    }
}

public static class MacroDrainage
{
    /// <summary>Version 1: whole-world, cell-centred, ocean outlets, uniform contributing area, four-neighbour flood.</summary>
    public static DrainageAnalysis Build(MacroDrainageRecipe recipe, CancellationToken cancellation = default)
    {
        recipe.Validate(); cancellation.ThrowIfCancellationRequested();
        var world = new MacroWorldGenerator(recipe.World); int side = recipe.Resolution;
        double spacing = recipe.World.Profile.WorldSize / side;
        double minX = recipe.World.OriginX - recipe.World.Profile.WorldSize * .5;
        double minZ = recipe.World.OriginZ - recipe.World.Profile.WorldSize * .5;
        var heights = new double[side * side]; var outlets = new List<DrainageOutlet>();
        for (int z = 0; z < side; z++)
        {
            cancellation.ThrowIfCancellationRequested();
            for (int x = 0; x < side; x++)
            {
                int i = z * side + x;
                heights[i] = world.Sample(minX + (x + .5) * spacing, minZ + (z + .5) * spacing).Elevation;
                if (heights[i] <= recipe.World.Profile.SeaLevel) outlets.Add(new(i, recipe.World.Profile.SeaLevel));
            }
        }
        if (outlets.Count == 0) throw new InvalidDataException("Coarse world contains no sampled ocean outlet. Refine the domain; do not invent edge drains.");
        return DrainageAnalysis.Build(new(minX, minZ, spacing, side, side, heights), outlets, recipe.Digest(), cancellation);
    }
}

/// <summary>Immutable reconstruction recipe with an expected output digest. Not a local-water save or migration.</summary>
public sealed record MacroDrainageCheckpoint
{
    public required MacroDrainageRecipe Recipe { get; init; }
    public required string ExpectedFingerprint { get; init; }
}
public static class MacroDrainageStore
{
    public static void Create(string path, MacroDrainageRecipe recipe, CancellationToken cancellation = default)
    {
        // Recompute from frozen inputs; accepting an arbitrary caller-provided digest could save a mismatched result.
        var analysis = MacroDrainage.Build(recipe, cancellation);
        var checkpoint = new MacroDrainageCheckpoint { Recipe = recipe, ExpectedFingerprint = analysis.Fingerprint };
        string target = Path.GetFullPath(path), temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        try
        {
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            { JsonSerializer.Serialize(stream, checkpoint, new JsonSerializerOptions { WriteIndented = true }); stream.Flush(true); }
            cancellation.ThrowIfCancellationRequested(); File.Move(temporary, target, false);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
    public static (MacroDrainageRecipe Recipe, DrainageAnalysis Analysis) Load(string path, CancellationToken cancellation = default)
    {
        if (new FileInfo(path).Length > 65536) throw new InvalidDataException("Drainage checkpoint too large.");
        try
        {
            var checkpoint = JsonSerializer.Deserialize<MacroDrainageCheckpoint>(File.ReadAllText(path));
            if (checkpoint?.Recipe == null || checkpoint.ExpectedFingerprint == null || checkpoint.ExpectedFingerprint.Length != 64)
                throw new InvalidDataException("Damaged drainage checkpoint.");
            var analysis = MacroDrainage.Build(checkpoint.Recipe, cancellation);
            if (analysis.Fingerprint != checkpoint.ExpectedFingerprint) throw new InvalidDataException("Drainage reconstruction differs from the saved version.");
            return (checkpoint.Recipe, analysis);
        }
        catch (JsonException error) { throw new InvalidDataException("Damaged drainage checkpoint.", error); }
    }
}
