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
    /// <summary>How landforms scale with the world: 9 = mountains and hills keep close to their real size on small
    /// worlds (D40); 8 = as in build 8 (scaled down with the world: smooth, blobby small worlds).</summary>
    public int ScaleRevision { get; init; } = 9;
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

    /// <summary>
    /// The land shapes a world can take apart from its type (New world → Land shape, D230): the built-in types' layouts,
    /// natural first. Worlds store a shape by its place in this list, so it only ever grows at the end.
    /// </summary>
    public static readonly string[] ShapeIds =
    {
        "natural-continents", "natural-mainland", "natural-archipelago", "natural-mountains", "natural-many-continents",
        "natural-islands-mainland", "natural-inland-sea", "natural-shattered", "natural-rift", "natural-twin-continents",
        "natural-peninsula",
        "balanced", "continental", "archipelago", "mountainous", "many-continents", "islands-mainland",
        "inland-sea", "shattered", "rift", "twin-continents", "peninsula", "vast-world",
    };

    /// <summary>Group of a type in the New world list: "Natural", "Classic" or "Your types".</summary>
    public string Group => Custom ? "Your types" : Id.StartsWith("natural-", StringComparison.Ordinal) ? "Natural" : "Classic";

    /// <summary>The name without its group's prefix ("Natural · Archipelago" → "Archipelago").</summary>
    public string ShortName => Name.StartsWith("Natural · ", StringComparison.Ordinal) ? Name[10..] : Name;

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
        // Template types: the land comes from a landform template (D36) rather than placed continents.
        static double[] R(params double[] v) => v;
        static Landform L(string step) => new() { Step = step };
        WorldProfile Template(string id, string name, int islands, params Landform[] steps) => balanced with
        {
            Id = id, Name = name, Continents = 0, Islands = islands, IslandRadius = 5000, RangesPerContinent = 0, Landforms = steps,
        };
        list.AddRange(new WorldRecipe[]
        {
            new("inland-sea", "Inland sea", "A ring of land around a great inner sea with islands, open to the ocean through one strait.",
                Template("inland-sea", "Inland sea", 24,
                    L("Raise") with { X = R(.5), Z = R(.5), Size = R(.47, .5), Land = 3.2, Relief = .4 },
                    L("Mask") with { Shape = "Centre", Size = R(.26, .3), Strength = 1.7 },
                    L("Strait") with { X = R(.5), Z = R(.5), Angle = R(-30, 30), Length = R(.5), Size = R(.007, .01) },
                    L("Range") with { Count = R(2, 3), X = R(.2, .8), Z = R(.2, .8), Size = R(1, 1.4) },
                    L("Islands") with { X = R(.4, .45), Z = R(.42, .58), Angle = R(-25, 25), Length = R(.12, .18), Size = R(.006, .01) }), mainland: true),
            new("shattered", "Shattered lands", "One great landmass broken into many pieces by narrow sea channels.",
                Template("shattered", "Shattered lands", 50,
                    L("Raise") with { X = R(.5), Z = R(.5), Size = R(.4, .43), Land = 2.4, Relief = .3 },
                    L("Mask") with { Shape = "Edges", Strength = .5 },
                    L("Strait") with { Count = R(6, 9), X = R(.25, .75), Z = R(.25, .75), Length = R(.25, .5), Size = R(.008, .014) },
                    L("Range") with { Count = R(3, 4) },
                    L("Hills") with { Count = R(4, 7) }), mainland: true),
            new("rift", "Rift continent", "A great continent split by a rift valley full of long lakes, walled by mountains.",
                Template("rift", "Rift continent", 20,
                    L("Raise") with { X = R(.5), Z = R(.5), Size = R(.38, .41), Land = 2.3, Relief = .5 },
                    L("Trough") with { X = R(.24), Z = R(.48, .52), Angle = R(-6, 6), Length = R(.52), Size = R(.012, .016), Relief = -3.5 },
                    L("Basin") with { Count = R(3, 4), X = R(.3, .7), Z = R(.47, .53), Size = R(.015, .025), Relief = -3 },
                    L("Range") with { X = R(.25), Z = R(.42), Angle = R(-8, 8), Length = R(.5), Size = R(.8) },
                    L("Range") with { X = R(.25), Z = R(.58), Angle = R(-8, 8), Length = R(.5), Size = R(.8) }), mainland: true),
            new("twin-continents", "Twin continents", "Two large continents facing each other across an ocean with an island chain.",
                Template("twin-continents", "Twin continents", 36,
                    L("Raise") with { X = R(.25, .3), Z = R(.42, .58), Size = R(.23, .26), Land = 2.2, Relief = .4 },
                    L("Raise") with { X = R(.7, .75), Z = R(.42, .58), Size = R(.21, .25), Land = 2.2, Relief = .4 },
                    L("Range") with { X = R(.2, .3), Z = R(.3, .38), Angle = R(70, 110), Length = R(.25, .35) },
                    L("Range") with { X = R(.68, .78), Z = R(.3, .38), Angle = R(70, 110), Length = R(.25, .35) },
                    L("Islands") with { X = R(.5), Z = R(.2, .25), Angle = R(80, 100), Length = R(.5, .6), Size = R(.004, .008) })),
            new("peninsula", "Great peninsula", "A long mountainous peninsula reaching from a mainland into the ocean.",
                Template("peninsula", "Great peninsula", 30,
                    L("Raise") with { X = R(.82, .86), Z = R(.4, .6), Size = R(.26, .3), Land = 2.2, Relief = .4 },
                    L("Raise") with { X = R(.82), Z = R(.48, .52), Angle = R(165, 195), Length = R(.55, .62), Size = R(.06, .08), Land = 2.0, Relief = .6 },
                    L("Range") with { X = R(.75), Z = R(.48, .52), Angle = R(170, 190), Length = R(.5), Size = R(.8) },
                    L("Islands") with { X = R(.2, .25), Z = R(.4, .6), Angle = R(150, 210), Length = R(.15, .2), Size = R(.003, .005) }), mainland: true),
        });
        // Natural types (D38): heightmap templates, land grown cell by cell (new types; the ones above are unchanged).
        WorldProfile Natural(WorldProfile source, string id, string name, params string[] steps) => source with
        {
            Id = id, Name = name, Continents = 0, Islands = 0, IslandRadius = Math.Max(source.WorldSize * .0021, 4000), RangesPerContinent = 0,
            ContinentRadius = Math.Max(source.ContinentRadius, source.WorldSize * .036), Heightmap = steps,
        };
        WorldRecipe N(string id, string name, string description, WorldProfile source, params string[] steps) =>
            new("natural-" + id, "Natural · " + name, description, Natural(source, "natural-" + id, "Natural · " + name, steps));
        list.AddRange(new[]
        {
            N("continents", "Balanced continents", "Two to four continents of different sizes with ragged coasts, bays, peninsulas and offshore islands.", balanced,
                "Hill 1 60-75 18-32 30-70", "Hill 1 55-70 62-80 25-75", "Hill 1-2 35-50 35-65 15-85",
                "Hill 6-9 18-30 10-90 10-90", "Pit 3-5 15-25 15-85 15-85",
                "Range 4-6 25-40 15-85 15-85", "Trough 2-4 15-25 15-85 15-85",
                "Hill 10-16 8-14 5-95 5-95", "Strait 2-3 vertical", "Mask 3", "Smooth 2"),
            N("mainland", "Great mainland", "One huge landmass with deep bays, long peninsulas and a few islands.", P("continental"),
                "Hill 1 85-95 40-60 40-60", "Hill 6-8 30-45 20-80 20-80", "Pit 3-4 12-20 25-75 25-75",
                "Range 5-7 25-40 20-80 20-80", "Trough 2-3 15-22 25-75 25-75", "Hill 8-12 8-14 5-95 5-95",
                "Mask 3", "Smooth 2"),
            N("archipelago", "Archipelago", "Dozens of islands of every size, from small continents to rocks.", P("archipelago"),
                "Add 13 all", "Range 4-5 30-45 15-85 15-85", "Hill 8-10 14-22 10-90 15-85", "Smooth 2",
                "Trough 10-14 18-28 5-95 5-95", "Strait 2 vertical", "Strait 1-2 horizontal", "Mask 4", "Smooth 2"),
            N("mountains", "Mountain kingdoms", "Continents crossed by many high mountain chains with deep valleys between.", P("mountainous"),
                "Hill 1 60-70 20-40 30-70", "Hill 1 55-70 60-80 30-70", "Hill 3-5 25-35 15-85 15-85",
                "Range 8-12 35-55 15-85 15-85", "Trough 4-6 15-25 15-85 15-85", "Hill 8-12 8-12 5-95 5-95", "Mask 3", "Smooth 2"),
            N("many-continents", "Many small continents", "Seven to ten medium landmasses separated by seas and straits.", balanced,
                "Hill 7-9 42-55 12-88 12-88", "Pit 4-6 12-18 15-85 15-85", "Range 4-6 20-30 10-90 10-90",
                "Hill 10-15 8-12 5-95 5-95", "Trough 3-5 12-18 15-85 15-85", "Mask 4", "Smooth 2"),
            N("islands-mainland", "Islands + mainland", "A large mainland ringed by islands and island chains.", balanced,
                "Hill 1 70-85 30-60 35-65", "Hill 3-5 20-30 25-65 25-75", "Range 3-5 25-40 25-65 25-75",
                "Hill 25-35 12-20 5-95 5-95", "Pit 4-6 15-20 20-80 20-80", "Mask 3", "Smooth 2"),
            N("inland-sea", "Inland sea", "Lands around a great inner sea that opens to the ocean.", balanced,
                "Hill 4-6 32-42 18-82 15-25", "Hill 4-6 32-42 18-82 75-85", "Hill 3-4 32-42 15-25 25-75", "Hill 3-4 32-42 75-85 25-75",
                "Range 4-5 20-32 15-85 15-85", "Mask -2", "Mask 3", "Strait 1 horizontal", "Hill 5-8 10-16 35-65 35-65", "Smooth 2"),
            N("shattered", "Shattered lands", "A broken continent: many large islands separated by winding straits.", balanced,
                "Hill 1 90-99 40-60 40-60", "Hill 5-7 35-50 15-85 15-85", "Trough 12-16 20-30 15-85 15-85",
                "Strait 2 vertical", "Strait 2 horizontal", "Pit 6-8 12-18 15-85 15-85", "Range 3-4 25-35 20-80 20-80",
                "Mask 3", "Smooth 2"),
            N("rift", "Rift continent", "A great continent split by rift valleys with long lakes, walled by mountains.", balanced,
                "Hill 1 90-99 40-60 40-60", "Hill 6-8 30-40 20-80 20-80", "Trough 2-3 22-32 25-75 25-75",
                "Range 3-4 35-45 25-75 25-75", "Pit 3-4 15-22 30-70 30-70", "Mask 3", "Smooth 2"),
            N("twin-continents", "Twin continents", "Two large continents facing each other across an ocean dotted with islands.", balanced,
                "Hill 1 75-85 20-30 35-65", "Hill 1 75-85 70-80 35-65", "Hill 3-4 25-35 10-40 15-85", "Hill 3-4 25-35 60-90 15-85",
                "Range 2 30-45 15-35 25-75", "Range 2 30-45 65-85 25-75", "Hill 6-10 8-14 40-60 10-90", "Mask 3", "Smooth 2"),
            N("peninsula", "Great peninsula", "A mainland with a long, mountainous peninsula reaching into the ocean.", balanced,
                "Hill 1 80-90 72-80 35-65", "Hill 2-3 30-40 65-85 20-80", "Range 1 45-55 60-68 40-60 10-20 35-65",
                "Range 1 25-35 55-65 40-60 15-25 35-65", "Hill 6-8 10-16 5-35 20-80", "Mask 3", "Smooth 2"),
        });
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
        // A landform template may build all land itself (no continents).
        int minContinents = b.Landforms is { Length: > 0 } || b.Heightmap is { Length: > 0 } ? 0 : 1;
        int continents = c.Continents ?? (Mainland || b.Continents == 0 ? b.Continents : (int)Math.Round(b.Continents * Math.Pow(f, .8)));
        continents = Math.Clamp(continents, minContinents, MaxContinents);
        double radius = b.ContinentRadius * f * (continents > 0 && b.Continents > 0 ? Math.Sqrt(b.Continents / (double)continents) : 1) * c.ContinentSize;
        radius = Math.Clamp(radius, size * .036, size * .279);
        int islands = c.Islands ?? (int)Math.Round(b.Islands * Math.Pow(f, .8));
        islands = Math.Clamp(islands, 0, MaxIslands);
        double islandRadius = b.IslandRadius * f * Math.Sqrt(Math.Max(1, b.Islands) / (double)Math.Max(1, islands));
        islandRadius = Math.Clamp(islandRadius, size * .0021, size * .0249);

        // Mountains: width follows the continents (capped at half their radius), height drops a little where narrow.
        double widthScale = radius / b.ContinentRadius;
        bool real = c.ScaleRevision >= 9;
        // Revision 9: landforms keep close to their real size on small worlds (a small world is a region of a big one,
        // not a shrunken copy: shrinking drew mountains as smooth cones and hills as round bumps).
        double mountainWidth = Math.Clamp(b.MountainWidth * Math.Pow(widthScale, real ? .35 : .7) * c.MountainWidth, 501, radius * .499);
        double mountainHeight = Math.Clamp(b.MountainHeight * c.MountainHeight * DetMath.Clamp(Math.Sqrt(mountainWidth / b.MountainWidth), real ? .7 : .35, 1), 0, 6000);
        double hillScale = real ? Math.Clamp(b.HillScale * Math.Pow(f, .3), Math.Min(4000, size * .099), size * .099)
            : Math.Clamp(b.HillScale * Math.Pow(f, .6), 501, size * .099);
        double hillHeight = Math.Clamp(b.HillHeight * c.Hills * (real ? DetMath.Clamp(Math.Pow(f, .15), .7, 1) : DetMath.Clamp(Math.Pow(f, .25), .5, 1)), 0, 800);
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
