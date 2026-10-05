using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace Thornwake.WorldGen;

/// <summary>Player choices on the world creation screen; null / 1.0 = Auto (the recipe decides).</summary>
public sealed record WorldChoices
{
    public double SizeKm { get; init; }
    public int? Continents { get; init; }
    public int? Islands { get; init; }
    public int? Ranges { get; init; }
    public double ContinentSize { get; init; } = 1;
    public double CoastRoughness { get; init; } = 1;
    public double MountainHeight { get; init; } = 1;
    public double MountainWidth { get; init; } = 1;
    public double Hills { get; init; } = 1;
}

/// <summary>
/// A world type ("Balanced continents", "Archipelago", …) as a relative recipe, independent of world size
/// (decision D35). Each recipe is anchored on a base profile at its native size: at that size with every choice on
/// Auto it gives exactly that profile (so existing worlds are unchanged). At other sizes proportions scale with the
/// world, but counts follow land coverage: a bigger world gets more landmasses and islands rather than a few giant
/// ones (count ∝ size^0.8, sizes adjusted so the land share stays about the same); a recipe with one dominant
/// landmass keeps one. Mountain ranges narrow (and lower a little) on small worlds so slopes stay sane.
/// </summary>
public sealed class WorldRecipe
{
    public string Id { get; }
    public string Name { get; }
    public string Description { get; }
    public WorldProfile Base { get; }
    public double NativeKm => Base.WorldSize / 1000;
    /// <summary>One dominant landmass: its count never grows with the world.</summary>
    public bool Mainland { get; }
    /// <summary>Parameter values this type starts with (creation-screen keys such as "gen.rivers"); others default.</summary>
    public IReadOnlyDictionary<string, double> Settings { get; }
    /// <summary>A type the player saved (user folder) rather than a built-in one.</summary>
    public bool Custom { get; }

    public static readonly double[] Sizes = { 32, 64, 128, 256, 512, 768, 1024, 2048, 4096 };
    public const int MaxContinents = 16, MaxIslands = 400;

    private WorldRecipe(string id, string name, string description, WorldProfile @base, bool mainland = false,
        IReadOnlyDictionary<string, double>? settings = null, bool custom = false)
    {
        Id = id; Name = name; Description = description; Base = @base; Mainland = mainland;
        Settings = settings ?? new Dictionary<string, double>(); Custom = custom;
    }

    /// <summary>File format of a world type (Content/world_types or the player's user://world_types folder).</summary>
    public sealed record Definition(string Id, string Name, string Description, bool Mainland, WorldProfile Profile, Dictionary<string, double>? Settings);

    public static WorldRecipe FromJson(string json, bool custom = true)
    {
        var d = JsonSerializer.Deserialize<Definition>(json) ?? throw new System.IO.InvalidDataException("Empty world type.");
        d.Profile.Validate();
        if (string.IsNullOrWhiteSpace(d.Id) || string.IsNullOrWhiteSpace(d.Name)) throw new System.IO.InvalidDataException("World type needs an id and a name.");
        return new WorldRecipe(d.Id, d.Name, d.Description ?? "", d.Profile, d.Mainland, d.Settings, custom);
    }

    public string ToJson() => JsonSerializer.Serialize(new Definition(Id, Name, Description, Mainland, Base, new Dictionary<string, double>(Settings)),
        new JsonSerializerOptions { WriteIndented = true });

    /// <summary>A new world type from a resolved world (its size becomes the type's native size) and parameter values.</summary>
    public static WorldRecipe Create(string name, string description, WorldProfile profile, bool mainland, IReadOnlyDictionary<string, double> settings)
    {
        var id = new string(name.ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        if (id.Length == 0) id = "custom";
        if (id.Length > 48) id = id[..48];
        var p = profile with { Id = id, Name = name.Length > 80 ? name[..80] : name };
        p.Validate();
        return new WorldRecipe(id, p.Name, description, p, mainland, settings, custom: true);
    }

    /// <summary>The world types: the catalogue's presets (unchanged) plus recipes defined here.</summary>
    public static IReadOnlyList<WorldRecipe> Catalog(IReadOnlyList<WorldProfile> profiles, IEnumerable<WorldRecipe>? custom = null)
    {
        WorldProfile P(string id) => profiles.First(p => p.Id == id);
        var balanced = P("balanced");
        var list = new List<WorldRecipe>
        {
            new("balanced", "Balanced continents", "Three-ish large continents, moderate islands and coasts, two mountain systems each.", balanced),
            new("continental", "Great mainland", "One dominant landmass with a few peripheral islands.", P("continental"), mainland: true),
            new("archipelago", "Archipelago", "Many small and medium landmasses, many islands, lots of coast.", P("archipelago")),
            new("mountainous", "Mountain kingdoms", "Continents dominated by high mountain ranges.", P("mountainous")),
            new("many-continents", "Many small continents", "Eight or more medium landmasses separated by narrow seas.", balanced with
            {
                Id = "many-continents", Name = "Many small continents", Continents = 8, Islands = 40, ContinentRadius = 48000, IslandRadius = 6000,
                CoastRoughness = .25, RangesPerContinent = 1, MountainWidth = 7000,
            }),
            new("islands-mainland", "Islands + mainland", "One large mainland surrounded by many islands and island chains.", balanced with
            {
                Id = "islands-mainland", Name = "Islands + mainland", Continents = 1, Islands = 90, ContinentRadius = 115000, IslandRadius = 7000,
                CoastRoughness = .24, RangesPerContinent = 3, MountainWidth = 11000,
            }, mainland: true),
        };
        if (profiles.Any(p => p.Id == "vast-world"))
            list.Add(new("vast-world", "Vast continents (classic)", "The original 2,048 km 'Vast world' preset.", P("vast-world")));
        if (custom != null)
            foreach (var r in custom)
                if (list.All(x => x.Id != r.Id)) list.Add(r);
        return list;
    }

    /// <summary>The profile for a world of <see cref="WorldChoices.SizeKm"/> with these choices.</summary>
    public WorldProfile Resolve(WorldChoices c)
    {
        var b = Base;
        double size = c.SizeKm > 0 ? c.SizeKm * 1000 : b.WorldSize;
        bool auto = c.Continents == null && c.Islands == null && c.Ranges == null && c.ContinentSize == 1 && c.CoastRoughness == 1
            && c.MountainHeight == 1 && c.MountainWidth == 1 && c.Hills == 1;
        if (auto && Math.Abs(size - b.WorldSize) < 1) return b; // native size, all Auto: exactly the preset
        double f = size / b.WorldSize;

        // Landmasses: counts follow coverage; radii keep the land share (count × radius² ∝ world area).
        int continents = c.Continents ?? (Mainland ? b.Continents : (int)Math.Round(b.Continents * Math.Pow(f, .8)));
        continents = Math.Clamp(continents, 1, MaxContinents);
        double radius = b.ContinentRadius * f * Math.Sqrt(b.Continents / (double)continents) * c.ContinentSize;
        radius = Math.Clamp(radius, size * .036, size * .279);
        int islands = c.Islands ?? (int)Math.Round(b.Islands * Math.Pow(f, .8));
        islands = Math.Clamp(islands, 0, MaxIslands);
        double islandRadius = b.IslandRadius * f * Math.Sqrt(Math.Max(1, b.Islands) / (double)Math.Max(1, islands));
        islandRadius = Math.Clamp(islandRadius, size * .0021, size * .0249);

        // Mountains: width follows the continents (capped at half their radius), height drops a little where narrow.
        double widthScale = radius / b.ContinentRadius;
        double mountainWidth = Math.Clamp(b.MountainWidth * Math.Pow(widthScale, .7) * c.MountainWidth, 501, radius * .499);
        double mountainHeight = Math.Clamp(b.MountainHeight * c.MountainHeight * DetMath.Clamp(Math.Sqrt(mountainWidth / b.MountainWidth), .35, 1), 0, 6000);
        double hillScale = Math.Clamp(b.HillScale * Math.Pow(f, .6), 501, size * .099);
        double hillHeight = Math.Clamp(b.HillHeight * c.Hills * DetMath.Clamp(Math.Pow(f, .25), .5, 1), 0, 800);
        double oceanDepth = Math.Clamp(b.OceanDepth * DetMath.Clamp(Math.Pow(f, .3), .3, 1), 50, 8000);

        var profile = b with
        {
            Id = $"{Id}-{size / 1000:0}km",
            Name = $"{Name} ({size / 1000:0} km)",
            WorldSize = size,
            Continents = continents,
            Islands = islands,
            ContinentRadius = radius,
            IslandRadius = islandRadius,
            CoastRoughness = Math.Clamp(b.CoastRoughness * c.CoastRoughness, 0, .3),
            RangesPerContinent = Math.Clamp(c.Ranges ?? b.RangesPerContinent, 0, 6),
            MountainHeight = mountainHeight,
            MountainWidth = mountainWidth,
            HillHeight = hillHeight,
            HillScale = hillScale,
            OceanDepth = oceanDepth,
        };
        profile.Validate();
        return profile;
    }
}
