using System.Text.Json;
using Thornwake.Core;

internal static class CraftActivityTests
{
    public static int Run(ContentCatalog catalog)
    {
        int passed=0;
        void Check(bool yes,string why){if(!yes)throw new Exception("Craft activity: "+why);passed++;}
        Check(CraftActivity.Action(catalog.Recipes["planks"])=="saw","planks use explicit sawing");
        Check(CraftActivity.Action(catalog.Recipes["chair"])=="join","joinery has a separate action");
        foreach(var recipe in catalog.Recipes.Values)Check(CraftActivity.IsValid(recipe),"supported action for "+recipe.Id);
        foreach(var station in new[]{"hand","carpenter","forge","kiln","cooking","oven"})
        {
            var recipe=new RecipeDefinition{Station=station};Check(CraftActivity.IsValid(recipe)&&CraftActivity.Action(recipe)!="","old content gets a default");
        }
        Check(!CraftActivity.IsValid(new RecipeDefinition{Station="hand",Activity="smith"}),"mismatched station/action rejected");
        var state=new WorldState();state.Player.X=6;state.Player.Z=2.5f;state.Player.Y=Construction.Ground(state,6,2.5f);state.Player.Inventory["wood"]=10;
        var sim=new Simulation(state,catalog);Check(sim.EnqueueCraft("planks","village:carpenter",2).Success,"queued sawing");
        sim.TickCraft(2);var job=state.CraftJob!;float elapsed=CraftActivity.Elapsed(job);string serialized=JsonSerializer.Serialize(job);
        Check(elapsed==2&&CraftActivity.Progress(job)>0&&sim.IsCraftInReach,"activity follows paid job clock");
        var restored=JsonSerializer.Deserialize<CraftJob>(serialized)!;Check(CraftActivity.Elapsed(restored)==elapsed,"saved job resumes animation phase");
        state.Player.Z=9;sim.TickCraft(20);Check(!sim.IsCraftInReach&&CraftActivity.Elapsed(job)==elapsed,"unreachable station does not animate progress");
        state.Player.Z=2.5f;sim.TickCraft(4);Check(state.CraftJob!=null&&CraftActivity.Elapsed(state.CraftJob)==0,"new batch resets action clock");
        string before=JsonSerializer.Serialize(state);for(int i=0;i<20;i++){CraftActivity.Stage(catalog.Recipes["planks"],state.CraftJob!);CraftActivity.Elapsed(state.CraftJob!);}
        Check(JsonSerializer.Serialize(state)==before,"presentation queries never mutate inventory or work");
        sim.CancelCraft();Check(!sim.IsCraftInReach,"cancelled job has no work activity");
        return passed;
    }
}
