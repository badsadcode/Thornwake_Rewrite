using System;

namespace Thornwake.Core;

/// <summary>Presentation cues derived from the authoritative job clock; never awards items or advances work.</summary>
public static class CraftActivity
{
    public static string Action(RecipeDefinition recipe) => recipe.Activity != "" ? recipe.Activity : recipe.Station switch
    {
        "carpenter" => "join", "forge" => "smith", "kiln" => "fire", "cooking" => "stir", "oven" => "bake", _ => "assemble"
    };
    public static bool IsValid(RecipeDefinition recipe) => recipe.Activity == "" || (recipe.Station, recipe.Activity) is
        ("hand", "assemble") or ("carpenter", "saw") or ("carpenter", "join") or ("forge", "smith") or
        ("kiln", "fire") or ("cooking", "stir") or ("oven", "bake");
    public static string Label(RecipeDefinition recipe) => Action(recipe) switch
    {
        "saw" => "Sawing timber", "join" => "Fitting and fastening", "smith" => "Hammering hot metal",
        "fire" => "Tending the kiln", "stir" => "Stirring and preparing", "bake" => "Tending the oven", _ => "Working by hand"
    };
    public static float Progress(CraftJob job) => job.Duration > 0 ? Math.Clamp(1 - job.Remaining / job.Duration, 0, 1) : 0;
    public static float Elapsed(CraftJob job) => Math.Max(0, job.Duration - job.Remaining);
    public static string Stage(RecipeDefinition recipe, CraftJob job) => Progress(job) switch
    {
        < .12f => "Preparing materials", > .9f => "Finishing up", _ => Label(recipe)
    };
}
