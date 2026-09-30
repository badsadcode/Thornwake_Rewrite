using System.Text.Json;
using System.Text.Json.Nodes;
using Thornwake.Core;

internal static class CraftingQueueTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception("CRAFT QUEUE: "+message);checks++;}
        void Reject(Action action,string message){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception("CRAFT QUEUE: "+message);}
        WorldState Fresh(int amount=100)
        {
            var world=new WorldState();foreach(var id in catalog.Items.Keys)world.Player.Inventory[id]=amount;
            world.Player.X=6;world.Player.Z=1;return world;
        }
        string Inventory(WorldState world)=>JsonSerializer.Serialize(world.Player.Inventory);
        var state=Fresh();var sim=new Simulation(state,catalog);
        Check(sim.EnqueueCraft("stone_axe","hand",3).Success,"numeric batch request starts");
        Check(state.CraftQueue.Count==1&&state.CraftQueue[0].RemainingCount==2&&state.CraftJob?.QueueEntryId==state.CraftQueue[0].Id,"pending count excludes active batch");
        Check(sim.Count("wood")==97&&sim.Count("stone")==98,"only first batch reserves ingredients");
        Check(sim.EnqueueCraft("berry_meal","hand",2).Success&&state.CraftQueue.Count==2&&sim.Count("berries")==100,"second recipe waits without reserving inputs");
        Check(!sim.RemoveQueuedCraft(state.CraftQueue[0].Id).Success,"active request uses explicit cancellation");
        Check(sim.TickCraft(6) is {Success:true}&&sim.Count("axe")==101&&state.CraftJob?.Remaining==2&&state.CraftQueue[0].RemainingCount==1,"leftover time advances next batch");
        Check(sim.TickCraft(14) is {Success:true}&&sim.Count("axe")==103&&sim.Count("meal")==102&&state.CraftJob==null&&state.CraftQueue.Count==0,"mixed queue completes exact batch counts");
        string finished=Inventory(state);
        Check(sim.TickCraft(100)==null&&Inventory(state)==finished,"finished requests cannot repeat output");

        var whole=Fresh();var divided=Fresh();var wholeSim=new Simulation(whole,catalog);var dividedSim=new Simulation(divided,catalog);
        foreach(var model in new[]{wholeSim,dividedSim}){model.EnqueueCraft("stone_axe","hand",3);model.EnqueueCraft("berry_meal","hand",4);}
        wholeSim.TickCraft(17.25f);for(int frame=0;frame<69;frame++)dividedSim.TickCraft(.25f);
        Check(Inventory(whole)==Inventory(divided)&&whole.CraftJob?.RecipeId==divided.CraftJob?.RecipeId&&whole.CraftJob?.Remaining==divided.CraftJob?.Remaining&&whole.CraftQueue[0].RemainingCount==divided.CraftQueue[0].RemainingCount,"large and split frame durations produce the same work, reservation and progress");

        state=Fresh(0);state.Player.Inventory["wood"]=8;state.Player.Inventory["stone"]=20;state.Player.Inventory["berries"]=3;sim=new Simulation(state,catalog);
        Check(sim.EnqueueCraft("stone_axe","hand",0,true).Success,"until-materials request accepts zero fixed quantity");
        Check(sim.EnqueueCraft("berry_meal","hand",1).Success,"another request can follow until-materials mode");
        sim.TickCraft(100);
        Check(sim.Count("axe")==2&&sim.Count("wood")==2&&sim.Count("meal")==1&&state.CraftQueue.Count==0,"until-materials mode stops without partial payment and proceeds to next recipe");
        Check(!sim.EnqueueCraft("stone_axe","hand",0,true).Success&&state.CraftQueue.Count==0,"unaffordable idle until-materials request fails clearly");

        state=Fresh(0);state.Player.Inventory["wood"]=3;state.Player.Inventory["stone"]=2;sim=new Simulation(state,catalog);
        sim.EnqueueCraft("stone_axe","hand",3);sim.TickCraft(30);
        Check(sim.Count("axe")==1&&state.CraftJob==null&&state.CraftQueue[0].RemainingCount==2,"numeric request waits after resources run out");
        Check(sim.CraftQueueStatus.Contains("Waiting for ingredients"),"waiting state explains missing ingredients");
        state.Player.Inventory["wood"]=6;state.Player.Inventory["stone"]=4;sim.TickCraft(8);
        Check(sim.Count("axe")==3&&state.CraftQueue.Count==0,"adding ingredients resumes exact remaining amount");

        state=Fresh();sim=new Simulation(state,catalog);string before=Inventory(state);
        sim.EnqueueCraft("stone_axe","hand",5);sim.EnqueueCraft("berry_meal","hand",2);sim.TickCraft(1);
        string pendingId=state.CraftQueue[1].Id;
        Check(sim.CancelCraft().Success&&state.CraftJob==null&&state.CraftQueue.Count==1&&Inventory(state)==before,"cancel active refunds once and removes its remaining batches only");
        Check(!sim.CancelCraft().Success&&Inventory(state)==before,"repeated cancellation cannot duplicate ingredients");
        Check(sim.RemoveQueuedCraft(pendingId).Success&&state.CraftQueue.Count==0&&Inventory(state)==before,"pending request removal has no inventory effect");
        Check(!sim.RemoveQueuedCraft(pendingId).Success,"removed pending request cannot be removed twice");
        sim.EnqueueCraft("stone_axe","hand",5);sim.EnqueueCraft("berry_meal","hand",3);
        Check(sim.ClearCraftQueue().Success&&state.CraftJob==null&&state.CraftQueue.Count==0&&Inventory(state)==before,"clear refunds active materials and drops all unreserved requests");
        Check(!sim.ClearCraftQueue().Success&&Inventory(state)==before,"clear repeated is harmless");

        state=Fresh();sim=new Simulation(state,catalog);
        Check(!sim.EnqueueCraft("planks","village:forge",3).Success,"wrong station does not queue");
        Check(sim.EnqueueCraft("planks","village:carpenter",3).Success,"matching nearby station queues work");
        sim.TickCraft(1);float remaining=state.CraftJob!.Remaining;state.Player.X=30;state.Player.Z=30;sim.TickCraft(60);
        Check(state.CraftJob.Remaining==remaining&&sim.Count("plank")==100&&sim.CraftQueueStatus.Contains("return to the station"),"station batch pauses out of reach");
        state.Player.X=6;state.Player.Z=1;sim.TickCraft(17);
        Check(sim.Count("plank")==103&&state.CraftQueue.Count==0,"station queue resumes across batch boundaries");

        state=Fresh();sim=new Simulation(state,catalog);
        Check(!sim.EnqueueCraft("stone_axe","hand",0).Success&&!sim.EnqueueCraft("stone_axe","hand",1000).Success,"fixed quantities bounded 1 through 999");
        for(int i=0;i<50;i++)Check(sim.EnqueueCraft("stone_axe","hand",1).Success,"queue slot accepted "+i);
        Check(!sim.EnqueueCraft("stone_axe","hand",1).Success&&state.CraftQueue.Count==50,"queue limit cannot overflow");
        before=Inventory(state);try{sim.TickCraft(float.NaN);throw new Exception("CRAFT QUEUE: invalid elapsed time accepted");}catch(ArgumentOutOfRangeException){checks++;}
        Check(Inventory(state)==before,"invalid elapsed time is mutation free");

        string folder=Path.Combine(Path.GetTempPath(),"thornwake-queue-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            state=Fresh();sim=new Simulation(state,catalog);sim.EnqueueCraft("stone_axe","hand",3);sim.EnqueueCraft("berry_meal","hand",2);sim.TickCraft(1);
            string path=Path.Combine(folder,"queue.json");SaveStore.Save(path,state);
            var loaded=SaveStore.Load(path);var loadedSim=new Simulation(loaded,catalog);
            Check(Inventory(loaded)==Inventory(state)&&JsonSerializer.Serialize(loaded.CraftQueue)==JsonSerializer.Serialize(state.CraftQueue)&&JsonSerializer.Serialize(loaded.CraftJob)==JsonSerializer.Serialize(state.CraftJob),"queue, paid materials and partial timing survive save/load");
            loadedSim.TickCraft(19);sim.TickCraft(19);
            Check(Inventory(loaded)==Inventory(state)&&loaded.CraftQueue.Count==0,"resumed mixed queue produces exactly the same output");

            var v3=Fresh();v3.SaveVersion=3;v3.Player.Inventory["wood"]-=3;v3.Player.Inventory["stone"]-=2;
            v3.CraftJob=new CraftJob{RecipeId="stone_axe",StationId="hand",Duration=4,Remaining=2,PaidCosts=new(){{"wood",3},{"stone",2}}};
            var old=JsonNode.Parse(JsonSerializer.Serialize(v3))!.AsObject();old.Remove("CraftQueue");old["CraftJob"]!.AsObject().Remove("QueueEntryId");
            string oldPath=Path.Combine(folder,"v3.json"),oldBytes=old.ToJsonString();File.WriteAllText(oldPath,oldBytes);
            var upgraded=SaveStore.Load(oldPath);var upgradedSim=new Simulation(upgraded,catalog);
            Check(upgraded.SaveVersion==10&&upgraded.CraftQueue.Count==0&&upgraded.CraftJob?.QueueEntryId==""&&upgraded.CraftJob.Remaining==2,"schema3 active work migrates without losing timing");
            upgradedSim.EnqueueCraft("berry_meal","hand",1);upgradedSim.TickCraft(6);
            Check(upgradedSim.Count("axe")==101&&upgradedSim.Count("meal")==101&&upgraded.CraftQueue.Count==0,"legacy active job finishes then queued requests follow");
            SaveStore.Save(oldPath,upgraded);SaveStore.Save(oldPath,upgraded);
            Check(File.ReadAllText(oldPath+".v3")==oldBytes,"permanent schema3 backup survives subsequent autosaves");

            WorldState Queued(){var world=Fresh();new Simulation(world,catalog).EnqueueCraft("stone_axe","hand",3);return world;}
            var invalid=Queued();invalid.CraftQueue[0].RemainingCount=1000;Reject(()=>SaveStore.Save(path,invalid),"invalid saved quantities rejected");
            invalid=Queued();invalid.CraftQueue.Add(invalid.CraftQueue[0]);Reject(()=>SaveStore.Save(path,invalid),"duplicate request ids rejected");
            invalid=Queued();invalid.CraftJob!.QueueEntryId="missing";Reject(()=>SaveStore.Save(path,invalid),"orphan active request rejected");
            invalid=Queued();invalid.CraftQueue[0].RecipeId="missing";invalid.CraftJob!.RecipeId="missing";Reject(()=>new Simulation(invalid,catalog),"unknown queue recipe rejected");
            invalid=Queued();invalid.CraftQueue[0].StationId="missing";invalid.CraftJob!.StationId="missing";Reject(()=>new Simulation(invalid,catalog),"invalid hand station rejected");
            invalid=Queued();invalid.CraftQueue[0].UntilMaterials=true;Reject(()=>SaveStore.Save(path,invalid),"until mode cannot carry finite pending amount");
        }
        finally{foreach(var file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}
        return checks;
    }
}

