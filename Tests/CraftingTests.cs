using System.Text.Json;
using System.Text.Json.Nodes;
using Thornwake.Core;

internal static class CraftingTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception("CRAFTING: "+message);checks++;}
        void Reject(Action action,string message)
        {try{action();}catch(InvalidDataException){checks++;return;}throw new Exception("CRAFTING: "+message);}
        WorldState Fresh()
        {
            var state=new WorldState{Seed=7319};
            foreach(var id in catalog.Items.Keys)state.Player.Inventory[id]=100;
            foreach(var resource in WorldGenerator.Generate(state.Seed))state.RemovedResources.Add(resource.Id);
            return state;
        }
        void At(WorldState state,float x,float z){state.Player.X=x;state.Player.Z=z;state.Player.Y=null;}
        string Inventory(WorldState state)=>JsonSerializer.Serialize(state.Player.Inventory);
        var state=Fresh();var sim=new Simulation(state,catalog);
        var hand=catalog.Recipes["stone_axe"];
        int initialAxe=sim.Count(hand.Output);string before=Inventory(state);
        Check(sim.Craft(hand.Id).Success,"hand recipe starts without a bench");
        Check(state.CraftJob is {StationId:"hand"}&&state.CraftJob.Remaining==hand.Duration,"job keeps duration and hand station");
        Check(sim.Count(hand.Output)==initialAxe,"starting does not produce an item immediately");
        foreach(var cost in hand.Costs)Check(sim.Count(cost.Key)==100-cost.Value,"materials reserved once at start: "+cost.Key);
        string reserved=Inventory(state);
        Check(!sim.Craft("flint_blade").Success&&Inventory(state)==reserved,"duplicate job cannot reserve another batch");
        Check(sim.TickCraft(hand.Duration/2)==null&&sim.Count(hand.Output)==initialAxe,"partial work has no output");
        Check(sim.TickCraft(hand.Duration/2) is {Success:true}&&state.CraftJob==null,"completion clears the job");
        Check(sim.Count(hand.Output)==initialAxe+hand.Quantity,"completion produces exactly one batch");
        Check(sim.TickCraft(100)==null&&sim.Count(hand.Output)==initialAxe+hand.Quantity,"completed job cannot pay out twice");
        Check(!sim.CancelCraft().Success,"completed job cannot refund materials");
        before=Inventory(state);int skillBefore=state.Player.Skills.GetValueOrDefault("crafting");
        Check(sim.Craft("flint_blade").Success,"another job can start after completion");
        sim.TickCraft(1);
        Check(sim.CancelCraft().Success&&state.CraftJob==null&&Inventory(state)==before,"cancelling partial work refunds exact reserved materials");
        Check(state.Player.Skills.GetValueOrDefault("crafting")==skillBefore,"cancelled work grants no crafting experience");
        Check(!sim.CancelCraft().Success&&Inventory(state)==before,"repeated cancellation cannot duplicate refund");

        before=Inventory(state);
        At(state,6,1);
        Check(!sim.Craft("planks").Success&&Inventory(state)==before,"station recipe is unavailable in hand crafting");
        Check(!sim.Craft("planks","village:forge").Success&&Inventory(state)==before,"wrong station cannot start recipe");
        At(state,40,40);
        Check(!sim.Craft("planks","village:carpenter").Success&&Inventory(state)==before,"remote station cannot start recipe");
        At(state,6,1);state.Player.Y=Construction.Ground(state,6,1)+Construction.Storey;
        Check(!sim.Craft("planks","village:carpenter").Success,"station on another floor is out of reach");
        At(state,6,1);int initialPlanks=sim.Count("plank");
        Check(sim.Craft("planks","village:carpenter").Success,"nearby matching station starts recipe");
        Check(sim.TickCraft(1)==null,"station task advances incrementally");
        float remaining=state.CraftJob!.Remaining;
        At(state,20,20);sim.TickCraft(100);
        Check(state.CraftJob!.Remaining==remaining&&sim.Count("plank")==initialPlanks,"walking away pauses work without output");

        string folder=Path.Combine(Path.GetTempPath(),"thornwake-crafting-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            string activePath=Path.Combine(folder,"active.json");SaveStore.Save(activePath,state);
            var resumed=SaveStore.Load(activePath);var resumedSim=new Simulation(resumed,catalog);
            Check(JsonSerializer.Serialize(resumed.CraftJob)==JsonSerializer.Serialize(state.CraftJob),"active job timing, costs and station survive save/load");
            Check(Inventory(resumed)==Inventory(state),"loading neither refunds nor charges reserved materials again");
            resumedSim.TickCraft(100);
            Check(resumed.CraftJob!.Remaining==remaining,"loaded task stays paused while away from station");
            At(resumed,6,1);
            Check(resumedSim.TickCraft(remaining) is {Success:true}&&resumedSim.Count("plank")==initialPlanks+1,"returning resumes saved task to one completion");
            Check(resumedSim.TickCraft(remaining)==null&&resumedSim.Count("plank")==initialPlanks+1,"saved task still completes only once");
            SaveStore.Save(activePath,resumed);
            Check(SaveStore.Load(activePath).CraftJob==null,"completed job remains cleared after another save/load");

            var v2=Fresh();v2.SaveVersion=2;
            v2.Structures.Add(new StructureState{Id="saved-floor",Kind="foundation",X=-15,Z=23,Elevation=.15f,Modular=true,PaidCosts=new(){{"wood",4},{"stone",2}}});
            v2.Structures.Add(new StructureState{Id="saved-door",Kind="door",X=-15,Z=21.5f,Elevation=.43f,SupportId="saved-floor",Modular=true,DoorOpen=true,PaidCosts=new(){{"wood",4},{"plank",1}}});
            var oldJson=JsonNode.Parse(JsonSerializer.Serialize(v2))!.AsObject();oldJson.Remove("CraftJob");oldJson["Player"]!.AsObject().Remove("Y");
            foreach(var piece in oldJson["Structures"]!.AsArray())piece!.AsObject().Remove("Level");
            string oldPath=Path.Combine(folder,"version2.json");string original=oldJson.ToJsonString();File.WriteAllText(oldPath,original);
            var upgraded=SaveStore.Load(oldPath);
            Check(upgraded.SaveVersion==10&&upgraded.CraftJob==null&&upgraded.Player.Y==null,"actual v2-shaped document migrates to current defaults");
            Check(upgraded.Structures.All(s=>s.Level==0)&&upgraded.Structures[1].SupportId=="saved-floor"&&upgraded.Structures[1].DoorOpen,"v2 support metadata and open doors survive migration");
            Check(upgraded.Structures[0].Elevation==.15f&&upgraded.Structures[0].PaidCosts["wood"]==4,"v2 elevation and historical costs survive migration");
            SaveStore.Save(oldPath,upgraded);
            Check(File.ReadAllText(oldPath+".v2")==original&&File.ReadAllText(oldPath+".bak")==original,"first migrated save preserves original v2 bytes");
            SaveStore.Save(oldPath,upgraded);
            Check(File.ReadAllText(oldPath+".v2")==original&&JsonNode.Parse(File.ReadAllText(oldPath+".bak"))!["SaveVersion"]!.GetValue<int>()==10,"permanent v2 backup survives subsequent autosave rotation");

            WorldState WithJob()=>new(){CraftJob=new CraftJob{RecipeId="stone_axe",StationId="hand",Duration=4,Remaining=2,PaidCosts=new(){{"wood",3},{"stone",2}}}};
            string invalidPath=Path.Combine(folder,"invalid.json");
            foreach(float badRemaining in new[]{-1f,0f,5f,float.NaN,float.PositiveInfinity})
            {
                var broken=WithJob();broken.CraftJob!.Remaining=badRemaining;
                Reject(()=>SaveStore.Save(invalidPath,broken),"invalid remaining time is rejected");
            }
            foreach(float badDuration in new[]{0f,6001f,float.NaN})
            {
                var broken=WithJob();broken.CraftJob!.Duration=badDuration;
                Reject(()=>SaveStore.Save(invalidPath,broken),"invalid job duration is rejected");
            }
            var badCost=WithJob();badCost.CraftJob!.PaidCosts["wood"]=-1;
            Reject(()=>SaveStore.Save(invalidPath,badCost),"negative reserved materials are rejected");
            var missingRecipe=WithJob();missingRecipe.CraftJob!.RecipeId="missing-recipe";
            Reject(()=>new Simulation(missingRecipe,catalog),"unavailable saved recipe is rejected before play");
            var missingStation=WithJob();missingStation.CraftJob!.RecipeId="planks";missingStation.CraftJob.StationId="removed-bench";
            Reject(()=>new Simulation(missingStation,catalog),"missing saved workstation is rejected before play");
            var missingMaterial=WithJob();missingMaterial.CraftJob!.PaidCosts["missing-material"]=1;
            Reject(()=>new Simulation(missingMaterial,catalog),"unknown reserved material is rejected before refund");
        }
        finally{foreach(var file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}

        state=Fresh();sim=new Simulation(state,catalog);At(state,-15,20.5f);
        Check(sim.Build("carpenter",-15,23).Success,"player can build a carpentry station");var station=state.Structures[^1];
        before=Inventory(state);
        Check(sim.Craft("planks",station.Id).Success,"owned workstation accepts matching recipe");
        Check(!sim.Dismantle(station.Id).Success&&state.Structures.Contains(station),"active station cannot be dismantled");
        Check(sim.CancelCraft().Success&&Inventory(state)==before,"cancelling at owned station restores all inputs");
        Check(sim.Dismantle(station.Id).Success,"station can be dismantled after cancellation");

        foreach(string decor in new[]{"chair","table","planter","painting","lantern","rug"})
        {
            var recipe=catalog.Recipes[decor];string stationId="hand";
            if(recipe.Station!="hand")
            {
                var site=Workstations.Village.First(s=>s.Kind==recipe.Station);stationId=site.Id;At(state,site.X,site.Z);
            }
            int count=sim.Count(decor);
            Check(sim.Craft(decor,stationId).Success,$"{decor} recipe starts at its required station");
            Check(sim.TickCraft(recipe.Duration) is {Success:true}&&sim.Count(decor)==count+1,$"{decor} completes into an inventory item");
            At(state,-15,20.5f);
            var result=sim.Build(decor,-15,23);
            Check(result.Success&&sim.Count(decor)==count,$"{decor} placement consumes the crafted item: {result.Message}");
            var placed=state.Structures[^1];
            Check(sim.Dismantle(placed.Id).Success&&sim.Count(decor)==count+1,$"{decor} pickup returns the intact item");
            Check(!sim.Dismantle(placed.Id).Success&&sim.Count(decor)==count+1,$"{decor} cannot be picked up twice");
        }
        At(state,-15,20.5f);Check(sim.Build("rug",-15,23).Success,"rug can be placed beneath furniture");
        Check(sim.Build("table",-15,23).Success,"table can share the rug footprint");
        return checks;
    }
}


