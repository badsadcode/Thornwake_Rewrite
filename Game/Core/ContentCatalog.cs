using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Thornwake.Core;

public sealed class ItemDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public int BuyPrice { get; set; }
    public int SellPrice { get; set; }
    public string Category { get; set; } = "Materials";
    public string Description { get; set; } = "A useful resource from the valley.";
    public string UseAction { get; set; } = "";
    public float RestoreHealth { get; set; }
    public float RestoreStamina { get; set; }
    public float Cooldown { get; set; }
}

public sealed class RecipeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, int> Costs { get; set; } = new();
    public string Output { get; set; } = "";
    public int Quantity { get; set; } = 1;
    public string Station { get; set; } = "hand";
    public float Duration { get; set; } = 4;
    public string Activity { get; set; } = "";
    public string Description { get; set; } = "";
}

public sealed class BuildingDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public Dictionary<string, int> Costs { get; set; } = new();
    public float Radius { get; set; } = 1;
    public string Placement { get; set; } = "ground";
    public float Width { get; set; } = 1.5f;
    public float Depth { get; set; } = 1.5f;
    public string Category { get; set; } = "Structure";
    public string Description { get; set; } = "";
}

public sealed class ResourceDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Skill { get; set; } = "foraging";
    public Dictionary<string, int> Rewards { get; set; } = new();
}

public sealed class ContentCatalog
{
    public Dictionary<string, ItemDefinition> Items { get; private set; } = new();
    public Dictionary<string, RecipeDefinition> Recipes { get; private set; } = new();
    public Dictionary<string, BuildingDefinition> Buildings { get; private set; } = new();
    public Dictionary<string, BuildingToolDefinition> BuildingTools { get; private set; } = new();
    public Dictionary<string, ResourceDefinition> Resources { get; private set; } = new();
    public Dictionary<string, AbilityDefinition> Abilities { get; private set; } = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public static ContentCatalog Load(string directory) => LoadFromJson(file => File.ReadAllText(Path.Combine(directory, file)));

    public static ContentCatalog LoadFromJson(Func<string, string> readFile)
    {
        var catalog = new ContentCatalog
        {
            Items = Read<ItemDefinition>(readFile, "items.json").ToDictionary(x => x.Id),
            Recipes = Read<RecipeDefinition>(readFile, "recipes.json").ToDictionary(x => x.Id),
            Buildings = Read<BuildingDefinition>(readFile, "buildings.json").ToDictionary(x => x.Id),
            BuildingTools = Read<BuildingToolDefinition>(readFile, "building_tools.json").ToDictionary(x => x.Id),
            Resources = Read<ResourceDefinition>(readFile, "resources.json").ToDictionary(x => x.Id),
            Abilities = Read<AbilityDefinition>(readFile, "abilities.json").ToDictionary(x => x.Id)
        };
        catalog.Validate();
        return catalog;
    }

    private static List<T> Read<T>(Func<string, string> readFile, string file) =>
        JsonSerializer.Deserialize<List<T>>(readFile(file), JsonOptions)
        ?? throw new InvalidDataException($"Content file {file} is empty.");

    private void Validate()
    {
        if (Items.Count == 0 || Recipes.Count == 0 || Buildings.Count == 0 || Resources.Count == 0)
            throw new InvalidDataException("A content catalog must contain items, recipes, buildings, and resources.");
        foreach (var item in Items.Values)
            if (string.IsNullOrWhiteSpace(item.Id) || string.IsNullOrWhiteSpace(item.Name) || item.BuyPrice < 0 || item.SellPrice < 0 || item.SellPrice > item.BuyPrice || !new[]{"","axe","hammer","hoe","shovel","seeds","sword","consume","place"}.Contains(item.UseAction)||!float.IsFinite(item.RestoreHealth)||item.RestoreHealth<0||item.RestoreHealth>100||!float.IsFinite(item.RestoreStamina)||item.RestoreStamina<0||item.RestoreStamina>100||!float.IsFinite(item.Cooldown)||item.Cooldown<0||item.Cooldown>3600||item.UseAction=="consume"&&item.RestoreHealth+item.RestoreStamina<=0||item.UseAction=="place"&&!Buildings.ContainsKey(item.Id))
                throw new InvalidDataException($"Invalid item: {item.Id}.");
        foreach (var recipe in Recipes.Values)
        {
            ValidateCosts(recipe.Costs);
            if (string.IsNullOrWhiteSpace(recipe.Id) || !Items.ContainsKey(recipe.Output) || recipe.Quantity <= 0 || !float.IsFinite(recipe.Duration) || recipe.Duration<1 || recipe.Duration>300 || !CraftActivity.IsValid(recipe) || !new[]{"hand","carpenter","forge","kiln","cooking","oven"}.Contains(recipe.Station))
                throw new InvalidDataException($"Invalid recipe: {recipe.Id}.");
        }
        foreach (var building in Buildings.Values)
        {
            ValidateCosts(building.Costs);
            if (string.IsNullOrWhiteSpace(building.Id) || !float.IsFinite(building.Radius) || building.Radius <= 0 || !float.IsFinite(building.Width) || !float.IsFinite(building.Depth) || building.Width<=0 || building.Depth<=0 || building.Width>8 || building.Depth>8 || !new[]{"ground","floor","edge","roof","stairs"}.Contains(building.Placement))
                throw new InvalidDataException($"Invalid building: {building.Id}.");
        }
        foreach (var resource in Resources.Values) ValidateCosts(resource.Rewards);
        BuildingVariants.ValidateCatalog(this);
        foreach(var ability in Abilities.Values)
            if(string.IsNullOrWhiteSpace(ability.Id)||string.IsNullOrWhiteSpace(ability.Name)||ability.Action!="dodge"||!float.IsFinite(ability.StaminaCost)||ability.StaminaCost<0||ability.StaminaCost>100||!float.IsFinite(ability.Cooldown)||ability.Cooldown<0||ability.Cooldown>3600)throw new InvalidDataException("Invalid ability definition.");
    }

    private void ValidateCosts(Dictionary<string, int> values)
    {
        if (values is null || values.Count == 0 || values.Any(x => !Items.ContainsKey(x.Key) || x.Value <= 0))
            throw new InvalidDataException("Content references an unknown item or a non-positive quantity.");
    }
}
