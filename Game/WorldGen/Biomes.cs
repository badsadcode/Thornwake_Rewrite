using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Thornwake.WorldGen;

/// <summary>
/// A biome (D42): where it occurs in climate space (annual mean temperature °C, precipitation mm/year) and what it
/// looks and grows like. Data, not code: the defaults below are also the JSON format for custom biomes.
/// </summary>
public sealed record Biome
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    /// <summary>Temperature range (°C, annual mean); the edges blend over ~2.5 °C.</summary>
    public required double[] Temperature { get; init; }
    /// <summary>Precipitation range (mm/year); the edges blend over ~20 %.</summary>
    public required double[] Precipitation { get; init; }
    /// <summary>Ground colours (hex): grass/low plants, dry patches, bare soil, forest floor.</summary>
    public required string Grass { get; init; }
    public required string Dry { get; init; }
    public required string Soil { get; init; }
    public required string ForestFloor { get; init; }
    /// <summary>Forest amount (0 = none, 1 = the world's normal forest cover).</summary>
    public double Forest { get; init; } = 1;
    /// <summary>Tree mix: share of conifers (rest broadleaf); 0..1.</summary>
    public double Conifers { get; init; } = .4;
    /// <summary>Share of autumn-coloured broadleaf trees.</summary>
    public double Autumn { get; init; } = .15;
    /// <summary>Grass and small plants (0 = bare, 1 = normal).</summary>
    public double Grassland { get; init; } = 1;
    /// <summary>Ground covered in snow and ice (0..1).</summary>
    public double Snow { get; init; }
    /// <summary>Colour on the creation screen's biome map (hex).</summary>
    public required string Map { get; init; }
}

/// <summary>The biome catalogue and the climate → biome weights.</summary>
public static class Biomes
{
    /// <summary>The single temperate look of build 9 and earlier (biomes off).</summary>
    public static readonly Biome Classic = new()
    {
        Id = "classic", Name = "Classic temperate", Temperature = new[] { -80.0, 60 }, Precipitation = new[] { 0.0, 20000 },
        Grass = "566b3f", Dry = "7b8059", Soil = "5c4a35", ForestFloor = "3c4b2b", Forest = 1, Conifers = -1, Map = "6e9a4e",
    };

    public static readonly Biome[] Defaults =
    {
        new() { Id = "ice", Name = "Ice and glaciers", Temperature = new[] { -60.0, -9 }, Precipitation = new[] { 0.0, 9000 },
            Grass = "e8eef2", Dry = "dfe7ec", Soil = "c9d3d9", ForestFloor = "dfe7ec", Forest = 0, Grassland = 0, Snow = 1, Map = "f2f6f8" },
        new() { Id = "tundra", Name = "Tundra", Temperature = new[] { -9.0, -1.5 }, Precipitation = new[] { 0.0, 9000 },
            Grass = "6c7350", Dry = "8a8266", Soil = "6a5f50", ForestFloor = "5e604a", Forest = .05, Conifers = 1, Grassland = .55, Snow = .12, Map = "a8ab8e" },
        new() { Id = "boreal", Name = "Boreal forest (taiga)", Temperature = new[] { -1.5, 5 }, Precipitation = new[] { 300.0, 9000 },
            Grass = "5d6e45", Dry = "7c7b56", Soil = "5a4d3b", ForestFloor = "3a4430", Forest = 1.4, Conifers = .9, Autumn = .08, Grassland = .8, Snow = .1, Map = "4f6b4a" },
        new() { Id = "steppe", Name = "Steppe and grassland", Temperature = new[] { -1.5, 18 }, Precipitation = new[] { 250.0, 560 },
            Grass = "8a8f55", Dry = "a8a067", Soil = "7c6a4c", ForestFloor = "5f6640", Forest = .12, Conifers = .3, Grassland = 1.1, Map = "c2b871" },
        new() { Id = "temperate", Name = "Temperate forest", Temperature = new[] { 5.0, 15 }, Precipitation = new[] { 560.0, 1900 },
            Grass = "566b3f", Dry = "7b8059", Soil = "5c4a35", ForestFloor = "3c4b2b", Forest = 1, Conifers = .3, Autumn = .2, Map = "6e9a4e" },
        new() { Id = "rainforest-temperate", Name = "Temperate rainforest", Temperature = new[] { 5.0, 15 }, Precipitation = new[] { 1900.0, 9000 },
            Grass = "456b37", Dry = "5f7a45", Soil = "4b3d2c", ForestFloor = "2e4226", Forest = 1.6, Conifers = .7, Autumn = .05, Map = "3f7a45" },
        new() { Id = "mediterranean", Name = "Mediterranean scrub", Temperature = new[] { 15.0, 20 }, Precipitation = new[] { 300.0, 900 },
            Grass = "7f8350", Dry = "a59a63", Soil = "8a6a48", ForestFloor = "5c6240", Forest = .45, Conifers = .45, Autumn = 0, Grassland = .8, Map = "a7a35d" },
        new() { Id = "savanna", Name = "Savanna", Temperature = new[] { 20.0, 40 }, Precipitation = new[] { 560.0, 1500 },
            Grass = "9a944f", Dry = "b8a65f", Soil = "8f6640", ForestFloor = "6e7140", Forest = .2, Conifers = 0, Autumn = 0, Grassland = 1.1, Map = "c9b35a" },
        new() { Id = "tropical", Name = "Tropical forest", Temperature = new[] { 20.0, 40 }, Precipitation = new[] { 1500.0, 9000 },
            Grass = "3f6a30", Dry = "57793b", Soil = "6e4a30", ForestFloor = "2b4421", Forest = 1.7, Conifers = 0, Autumn = 0, Map = "2f7a3a" },
        new() { Id = "subtropical", Name = "Subtropical forest", Temperature = new[] { 15.0, 20 }, Precipitation = new[] { 900.0, 9000 },
            Grass = "4f6e38", Dry = "6c7d45", Soil = "6a4c33", ForestFloor = "334a28", Forest = 1.3, Conifers = .25, Autumn = 0, Map = "4c8a44" },
        new() { Id = "desert", Name = "Hot desert", Temperature = new[] { 15.0, 40 }, Precipitation = new[] { 0.0, 300 },
            Grass = "a08d5c", Dry = "b99d6c", Soil = "a8825a", ForestFloor = "8f7d55", Forest = 0, Grassland = .15, Map = "e3cc8f" },
        new() { Id = "cold-desert", Name = "Cold desert", Temperature = new[] { -1.5, 15 }, Precipitation = new[] { 0.0, 250 },
            Grass = "9b9370", Dry = "b3a882", Soil = "8f8068", ForestFloor = "807a5e", Forest = 0, Grassland = .25, Map = "bdb29a" },
    };

    public static IReadOnlyList<Biome> Parse(string json)
    {
        var list = JsonSerializer.Deserialize<Biome[]>(json) ?? throw new InvalidDataException("Empty biome list.");
        foreach (var b in list) Validate(b);
        return list;
    }

    public static void Validate(Biome b)
    {
        bool Range(double[] r, double min, double max) => r is { Length: 2 } && r[0] >= min && r[1] <= max && r[0] < r[1];
        if (string.IsNullOrWhiteSpace(b.Id) || !Range(b.Temperature, -80, 60) || !Range(b.Precipitation, 0, 20000) ||
            new[] { b.Grass, b.Dry, b.Soil, b.ForestFloor, b.Map }.Any(c => c is not { Length: 6 } || !c.All(Uri.IsHexDigit)))
            throw new InvalidDataException($"Biome '{b.Id}' has an invalid value.");
    }

    /// <summary>How well a climate fits a biome (0..1), with soft edges.</summary>
    public static double Fit(Biome b, double temperature, double precipitation)
    {
        double t = Edge(temperature, b.Temperature[0], b.Temperature[1], 2.5);
        double lp = Math.Log(Math.Max(1, precipitation));
        double p = Edge(lp, Math.Log(Math.Max(1, b.Precipitation[0])), Math.Log(Math.Max(1, b.Precipitation[1])), .2);
        return t * p;

        static double Edge(double v, double lo, double hi, double soft) =>
            DetMath.Smooth((v - lo) / soft + .5) * DetMath.Smooth((hi - v) / soft + .5);
    }

    /// <summary>The two best-fitting biomes and the share of the first (for blending at borders).</summary>
    public static (int A, int B, double ShareA) Classify(IReadOnlyList<Biome> biomes, double temperature, double precipitation)
    {
        int a = 0, b = 0; double fa = -1, fb = -1;
        for (int i = 0; i < biomes.Count; i++)
        {
            double f = Fit(biomes[i], temperature, precipitation);
            if (f > fa) { b = a; fb = fa; a = i; fa = f; }
            else if (f > fb) { b = i; fb = f; }
        }
        fb = Math.Max(fb, 0); fa = Math.Max(fa, 1e-9);
        return (a, b, fa / (fa + fb));
    }
}
