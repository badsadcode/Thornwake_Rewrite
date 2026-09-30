using System;
using System.Collections.Generic;

namespace Thornwake.Core;

public sealed class WorldState
{
    public int SaveVersion { get; set; } = 10;
    public GenerationSettings Generation {get;set;}=new();
    public EarthworkTerrain Earthworks {get;set;}=new();
    public EarthworkJob? EarthworkJob {get;set;}
    public int GeneratorVersion { get; set; } = 1;
    public int Seed { get; set; } = 73421;
    public double Time { get; set; }
    public double CalendarMinutes {get;set;}=480;
    public SandboxSettings Settings {get;set;}=new();
    public List<VegetationState> Vegetation {get;set;}=new();
    public HashSet<string> RemovedStumps {get;set;}=new();
    public PlayerState Player { get; set; } = new();
    public HashSet<string> RemovedResources { get; set; } = new();
    public List<StructureState> Structures { get; set; } = new();
    public List<TerrainEdit> TerrainEdits { get; set; } = new();
    public List<CropState> Crops { get; set; } = new();
    public bool EnemyDefeated { get; set; }
    public SettlementState Settlement { get; set; } = new();
    public List<string> Journal { get; set; } = new();
    public CraftJob? CraftJob { get; set; }
    public List<CraftQueueEntry> CraftQueue { get; set; } = new();
    public HashSet<string> CompletedQuests { get; set; } = new();
}

public sealed class PlayerState
{
    public float X { get; set; }
    public float Z { get; set; } = 8;
    public float? Y { get; set; }
    public float Health { get; set; } = 100;
    public float Stamina { get; set; } = 100;
    public Dictionary<string, int> Inventory { get; set; } = new()
    {
        ["wood"] = 6, ["stone"] = 4, ["berries"] = 3, ["seeds"] = 4, ["coins"] = 12,
        ["basic_axe"] = 1, ["hammer"] = 1, ["hoe"] = 1, ["basic_sword"] = 1
    };
    public Dictionary<string, int> Skills { get; set; } = new();
    public List<HotbarBinding?> Hotbar { get; set; } = HotbarRules.DefaultBindings();
    public string EquippedItem { get; set; } = "basic_axe";
    public Dictionary<string,float> Cooldowns { get; set; } = new();
}

public sealed class StructureState
{
    public string Id { get; set; } = "";
    public string Kind { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public float Rotation { get; set; }
    public float? Elevation { get; set; }
    public string SupportId { get; set; } = "";
    public string RoofConnectionId { get; set; } = "";
    public BuildSelection? Selection { get; set; }
    public bool Modular { get; set; }
    public bool DoorOpen { get; set; }
    public int Level { get; set; }
    public Dictionary<string,int> PaidCosts { get; set; } = new();
}

public sealed class CraftJob
{
    public string QueueEntryId { get; set; } = "";
    public string RecipeId { get; set; } = "";
    public string StationId { get; set; } = "hand";
    public float Remaining { get; set; }
    public float Duration { get; set; }
    public Dictionary<string,int> PaidCosts { get; set; } = new();
}

public sealed class CraftQueueEntry
{
    public string Id { get; set; } = "";
    public string RecipeId { get; set; } = "";
    public string StationId { get; set; } = "hand";
    // This is the number still waiting to start; the active batch is stored in CraftJob.
    public int RemainingCount { get; set; }
    public bool UntilMaterials { get; set; }
}

public sealed class TerrainEdit
{
    public float X { get; set; }
    public float Z { get; set; }
    public float Radius { get; set; } = 3;
    public float Delta { get; set; }
}

public sealed class CropState
{
    public string Id { get; set; } = "";
    public float X { get; set; }
    public float Z { get; set; }
    public double PlantedAt { get; set; }
}

public sealed class SettlementState
{
    public int Food { get; set; } = 28;
    public int Timber { get; set; } = 14;
    public int Prosperity { get; set; } = 42;
    public int CaravanArrivals { get; set; }
}

public readonly record struct ActionResult(bool Success, string Message);
public sealed record ResourceSpawn(string Id, string Kind, float X, float Z, float Scale);
