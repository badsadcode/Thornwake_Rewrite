using System;
using System.Collections.Generic;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// World-generation parameters shown in the "New world" tab. Shape values start from the chosen preset (absolute
/// numbers); terrain and water values are multipliers on the preset's defaults (1 = as designed). The same preset,
/// seed and parameters always give the same world.
/// </summary>
public sealed class WorldGenOptions
{
    public readonly List<Setting> All = new();
    private Setting Add(Setting s) { All.Add(s); return s; }

    // ---- world shape (profile)
    public readonly Setting Size, Continents, Islands, ContinentSize, CoastRoughness, Ranges, MountainHeight, HillHeight, Classic;
    // ---- terrain
    public readonly Setting Hills, Plains, Rock, CoastDetail, Erosion, Shores;
    // ---- water
    public readonly Setting Rivers, RiverWidth, Meanders, Wander, WidthVariation, Banks, Lakes, Deltas;
    // ---- vegetation and objects
    public readonly Setting ForestCover, ForestDensity, Rocks, Props;

    public WorldGenOptions()
    {
        const string S = "World shape", T = "Terrain", W = "Rivers and lakes";
        Classic = Add(new("gen.version", S, "Terrain version", 0, 2, 1, 0,
            "Generate the terrain as in an earlier build, to compare or go back: Preview 7 (before smooth river mouths), Build 5 (before natural banks and steeper shores).",
            options: new[] { "current", "Preview 7", "Build 5" }));
        Size = Add(new("gen.size", S, "World size", .5, 2, .05, 1, "Scales the whole world: continents, islands, hills and mountain ranges.", "×"));
        Continents = Add(new("gen.continents", S, "Continents", 1, 8, 1, 4, "Number of large landmasses."));
        Islands = Add(new("gen.islands", S, "Islands", 0, 128, 1, 40, "Number of small islands."));
        ContinentSize = Add(new("gen.continent_size", S, "Continent size", .6, 1.6, .05, 1, "Continent radius relative to the preset.", "×"));
        CoastRoughness = Add(new("gen.coast", S, "Coastline roughness", 0, .3, .01, .2, "Large bays, peninsulas and capes."));
        Ranges = Add(new("gen.ranges", S, "Mountain ranges per continent", 0, 6, 1, 2, "Number of mountain belts on each continent."));
        MountainHeight = Add(new("gen.mountain_height", S, "Mountain height", 0, 6000, 50, 2500, "Height of the highest peaks above the lowland.", "m"));
        HillHeight = Add(new("gen.hill_height", S, "Hill height", 0, 800, 10, 200, "Height of the broad hills between the mountains.", "m"));

        Hills = Add(new("gen.hills", T, "Rolling ground", 0, 3, .05, 1, "Walking-scale hills and hollows on the lowland.", "×"));
        Plains = Add(new("gen.plains", T, "Plains flatness", 0, 1, .05, .6, "How flat the plains between hill country are (1 = very flat)."));
        Rock = Add(new("gen.rock", T, "Mountain crags", 0, 3, .05, 1, "Rocky ridged detail on mountains.", "×"));
        CoastDetail = Add(new("gen.coast_detail", T, "Coastline detail", 0, 3, .05, 1, "Small-scale wiggles of the shoreline.", "×"));
        Erosion = Add(new("gen.erosion", T, "Erosion", 0, 3, .05, 1, "How deeply rivers have carved their valleys.", "×"));
        Shores = Add(new("gen.shores", T, "Shore steepness", 0, 3, .05, 1, "How quickly the sea deepens and the land rises at the coast (0 = very gentle, wide shallows).", "×"));

        Rivers = Add(new("gen.rivers", W, "Number of rivers", .25, 4, .05, 1, "More rivers means smaller streams are shown too.", "×"));
        RiverWidth = Add(new("gen.river_width", W, "River width", .3, 3, .05, 1, "Channel width for a given catchment.", "×"));
        Meanders = Add(new("gen.meanders", W, "Meanders", 0, 3, .05, 1, "Tight river loops.", "×"));
        Wander = Add(new("gen.wander", W, "Wandering", 0, 3, .05, 1, "Large bends and swings across the valley floor.", "×"));
        WidthVariation = Add(new("gen.width_variation", W, "Width variety", 0, 3, .05, 1, "Pools, narrows and broad stretches along each river.", "×"));
        Banks = Add(new("gen.banks", W, "Bank irregularity", 0, 3, .05, 1, "Uneven shorelines: each bank widens, narrows and wobbles on its own, with bulges, shallow shelves and cuts.", "×"));
        const string V = "Forests and objects";
        ForestCover = Add(new("gen.forest_cover", V, "Forest cover", 0, 2, .05, 1.15, "How much of the land is forest (patch size); 0 = open land.", "×"));
        ForestDensity = Add(new("gen.forest_density", V, "Forest density", .2, 1.6, .05, 1.2, "Trees per area inside forests.", "×"));
        Rocks = Add(new("gen.rocks", V, "Stones, rocks and boulders", 0, 2, .05, 1, "Amount of stones, rocks, boulders and outcrops.", "×"));
        Props = Add(new("gen.props", V, "Bushes, stumps and logs", 0, 2, .05, 1, "Amount of bushes, berry bushes, stumps and fallen logs.", "×"));
        Lakes = Add(new("gen.lakes", W, "Lakes", 0, 4, .05, 1, "Share of land that may be lake (largest basins first).", "×"));
        Deltas = Add(new("gen.deltas", W, "Deltas", 0, 4, .05, 1, "How readily large rivers split into deltas on flat coasts (0 = none).", "×"));
    }

    /// <summary>Show a preset's own values (shape) and reset the multipliers.</summary>
    public void LoadPreset(WorldProfile p)
    {
        void Set(Setting s, double v) { s.Default = Math.Clamp(v, s.Min, s.Max); s.Value = s.Default; }
        Set(Size, 1); Set(Continents, p.Continents); Set(Islands, p.Islands); Set(ContinentSize, 1); Set(CoastRoughness, p.CoastRoughness);
        Set(Ranges, p.RangesPerContinent); Set(MountainHeight, p.MountainHeight); Set(HillHeight, p.HillHeight);
        var style = TerrainStyle.For(p);
        Set(Plains, 1 - style.PlainsRoughness);
        Set(Classic, 0);
        Set(ForestCover, 1.15); Set(ForestDensity, 1.2); Set(Rocks, 1); Set(Props, 1);
        foreach (var s in new[] { Hills, Rock, CoastDetail, Erosion, Shores, Rivers, RiverWidth, Meanders, Wander, WidthVariation, Banks, Lakes, Deltas }) Set(s, 1);
    }

    public void Reset() { foreach (var s in All) s.Value = s.Default; }

    /// <summary>True when every parameter equals the preset (the world is then exactly the preset world).</summary>
    public bool IsPreset => All.TrueForAll(s => s.Value == s.Default);

    /// <summary>The profile and terrain style these parameters describe, starting from <paramref name="preset"/>.</summary>
    public (WorldProfile Profile, TerrainStyle Style) Build(WorldProfile preset)
    {
        double f = Size.Value;
        double world = Math.Clamp(preset.WorldSize * f, 16000, 4096000);
        double radius = Math.Clamp(preset.ContinentRadius * f * ContinentSize.Value, world * .035, world * .28);
        var profile = preset with
        {
            WorldSize = world,
            Continents = Continents.Index,
            Islands = Islands.Index,
            ContinentRadius = radius,
            IslandRadius = Math.Clamp(preset.IslandRadius * f, world * .002, world * .025),
            CoastRoughness = CoastRoughness.Value,
            HillHeight = HillHeight.Value,
            HillScale = Math.Clamp(preset.HillScale * f, 500, world * .1),
            RangesPerContinent = Ranges.Index,
            MountainHeight = MountainHeight.Value,
            MountainWidth = Math.Clamp(preset.MountainWidth * f, 500, radius * .5),
        };
        profile.Validate();
        var b = TerrainStyle.For(profile);
        var style = b with
        {
            LowlandDetail = b.LowlandDetail * Hills.Value,
            PlainsRoughness = 1 - Plains.Value,
            MountainDetail = b.MountainDetail * Rock.Value,
            CoastDetailShift = b.CoastDetailShift * CoastDetail.Value,
            ErosionStrength = b.ErosionStrength * Erosion.Value,
            RiverMinCatchmentKm2 = b.RiverMinCatchmentKm2 / Rivers.Value,
            RiverWidthPerSqrtKm2 = b.RiverWidthPerSqrtKm2 * RiverWidth.Value,
            MeanderStrength = Meanders.Value,
            WanderStrength = Wander.Value,
            WidthVariation = WidthVariation.Value,
            BankVariation = Classic.Index == 2 ? 0 : Banks.Value,
            ShoreSlope = Classic.Index == 2 ? 0 : Shores.Value,
            MouthBlend = Classic.Index >= 1 ? 0 : 1,
            ForestCover = ForestCover.Value, ForestDensity = ForestDensity.Value, RockAmount = Rocks.Value, PropAmount = Props.Value,
            LakeShare = b.LakeShare * Lakes.Value,
            DeltaMinCatchmentKm2 = Deltas.Value <= 0 ? double.PositiveInfinity : b.DeltaMinCatchmentKm2 / Deltas.Value,
        };
        return (profile, style);
    }
}
