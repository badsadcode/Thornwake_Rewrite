using System;
using System.IO;

namespace Thornwake.Core;

/// <summary>Per-world rules. Durations use active real-world time, independent of the calendar.</summary>
public sealed class SandboxSettings
{
    public float TreeGrowthHours {get;set;}=8;
    public float BushGrowthHours {get;set;}=2;
    public float BushRegrowHours {get;set;}=1;
    public float CropGrowthMinutes {get;set;}=1;
    public float CraftDurationMultiplier {get;set;}=1;
    public float ResourceYieldMultiplier {get;set;}=1;
    public float DayLengthMinutes {get;set;}=8;
    public float StaminaRegenMultiplier {get;set;}=1;
    public float DamageTakenMultiplier {get;set;}=1;
    public int DeathCoinLoss {get;set;}=4;
    public int StumpWoodYield {get;set;}=2;
    public float PlantSpacing {get;set;}=3;
    public bool FreezeVegetation {get;set;}
    public SandboxSettings Copy()=>(SandboxSettings)MemberwiseClone();
    public void Validate()
    {
        bool Range(float n,float min,float max)=>float.IsFinite(n)&&n>=min&&n<=max;
        if(!Range(TreeGrowthHours,.016f,720)||!Range(BushGrowthHours,.016f,720)||!Range(BushRegrowHours,.016f,720)||!Range(CropGrowthMinutes,.1f,10080)||!Range(CraftDurationMultiplier,.1f,20)||!Range(ResourceYieldMultiplier,.25f,5)||!Range(DayLengthMinutes,2,180)||!Range(StaminaRegenMultiplier,0,5)||!Range(DamageTakenMultiplier,0,5)||DeathCoinLoss<0||DeathCoinLoss>100||StumpWoodYield<0||StumpWoodYield>20||!Range(PlantSpacing,2,8))
            throw new InvalidDataException("One or more sandbox settings are outside their supported range.");
    }
}

public sealed partial class Simulation
{
    public float CropDuration=>State.Settings.CropGrowthMinutes*60;
    public float CraftDuration(RecipeDefinition recipe)=>Math.Clamp(recipe.Duration*State.Settings.CraftDurationMultiplier,.1f,6000);
    public int ResourceYield(int amount)=>Math.Max(1,(int)MathF.Round(amount*State.Settings.ResourceYieldMultiplier,MidpointRounding.AwayFromZero));
    public ActionResult UpdateSandbox(SandboxSettings settings)
    {
        if(settings==null)return Fail("Choose valid world settings.");
        try{settings.Validate();}catch(InvalidDataException error){return Fail(error.Message);}
        State.Settings=settings.Copy();return Done("World sandbox settings updated.");
    }
}
