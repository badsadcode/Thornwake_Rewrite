using System.Text.Json;
using System.Text.Json.Nodes;
using Thornwake.Core;

internal static class VegetationSandboxTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception("VEGETATION SANDBOX: "+message);checks++;}
        bool Near(double a,double b,double tolerance=1e-8)=>Math.Abs(a-b)<tolerance;
        string Inventory(WorldState state)=>JsonSerializer.Serialize(state.Player.Inventory);
        void At(WorldState state,float x,float z){state.Player.X=x;state.Player.Z=z;state.Player.Y=null;}
        WorldState Fresh()
        {
            var state=new WorldState{Seed=7319};
            foreach(string item in catalog.Items.Keys)state.Player.Inventory[item]=100;
            foreach(var resource in WorldGenerator.Generate(state.Seed)){state.RemovedResources.Add(resource.Id);if(resource.Kind=="tree")state.RemovedStumps.Add(resource.Id);}
            At(state,-15,23);return state;
        }
        VegetationState Plant(WorldState state,Simulation sim,string item="pine_sapling",float x=-15,float z=23)
        {
            At(state,x-1,z);int before=sim.Count(item);Check(sim.PlantVegetation(item,x,z).Success,"valid seedling plants on open ground");
            Check(sim.Count(item)==before-1,"planting consumes exactly one seedling");return state.Vegetation[^1];
        }
        void RejectPlant(WorldState state,Simulation sim,float x,float z,string reason)
        {
            string before=Inventory(state);int count=state.Vegetation.Count;
            Check(!sim.PlantVegetation("pine_sapling",x,z).Success,reason);
            Check(Inventory(state)==before&&state.Vegetation.Count==count,"invalid planting is atomic: "+reason);
        }

        var state=Fresh();var sim=new Simulation(state,catalog);var pine=Plant(state,sim);
        Check(state.Settings.TreeGrowthHours==8,"default pine maturity takes eight active real hours");
        sim.Tick(60);Check(Near(pine.Growth,1d/480)&&pine.Growth<1,"one minute produces only one 480th of pine growth");
        sim.Tick(8*3600-61);Check(pine.Growth<1,"pine is still immature before eight hours");
        sim.Tick(1.001);Check(pine.Growth==1,"pine matures after eight active hours");
        int wood=sim.Count("wood"),cone=sim.Count("pine_cone");
        state.Player.EquippedItem="hammer";Check(!sim.Gather(pine.Id,"tree").Success&&!pine.IsStump&&sim.Count("wood")==wood,"mature planted tree requires an axe");
        state.Player.EquippedItem="basic_axe";At(state,-5,23);Check(!sim.Gather(pine.Id,"tree").Success,"distant planted tree cannot be harvested");
        At(state,pine.X,pine.Z);Check(sim.Gather(pine.Id,"tree").Success&&pine.IsStump,"chopping a mature planted tree leaves a stump");
        Check(sim.Count("wood")==wood+4&&sim.Count("pine_cone")==cone+1,"planted tree produces timber and a renewable cone");
        wood=sim.Count("wood");Check(!sim.Gather(pine.Id,"tree").Success&&sim.Count("wood")==wood,"stump cannot pay tree harvest twice");
        Check(sim.RemoveStump(pine.Id).Success&&!state.Vegetation.Contains(pine),"planted stump can be removed");
        Check(sim.Count("wood")==wood+state.Settings.StumpWoodYield,"stump yields configured timber once");
        wood=sim.Count("wood");Check(!sim.RemoveStump(pine.Id).Success&&sim.Count("wood")==wood,"removed planted stump cannot duplicate timber");

        var slowState=Fresh();var slowSim=new Simulation(slowState,catalog);var slow=Plant(slowState,slowSim);
        var settings=slowState.Settings.Copy();settings.TreeGrowthHours=720;Check(slowSim.UpdateSandbox(settings).Success,"maximum month-long tree duration is supported");
        slow.Growth=.5;double growth=slow.Growth;
        for(int i=0;i<120;i++)slowSim.Tick(1d/60);
        Check(slow.Growth>growth&&Near(slow.Growth,growth+2d/(720*3600),1e-11),"frame-sized ticks retain progress at 720 hours instead of float stalling");
        growth=slow.Growth;settings=slowState.Settings.Copy();settings.TreeGrowthHours=8;Check(slowSim.UpdateSandbox(settings).Success&&slow.Growth==growth,"duration changes preserve existing percentage");
        slowSim.Tick(60);Check(Near(slow.Growth,growth+60d/(8*3600)),"subsequent progress uses updated duration");
        settings=slowState.Settings.Copy();settings.FreezeVegetation=true;slowSim.UpdateSandbox(settings);growth=slow.Growth;double calendar=slowState.CalendarMinutes;
        slowSim.Tick(60);Check(slow.Growth==growth&&slowState.CalendarMinutes>calendar,"vegetation freeze does not freeze the world clock");

        var invalidState=Fresh();var invalidSim=new Simulation(invalidState,catalog);
        At(invalidState,0,23);RejectPlant(invalidState,invalidSim,0,23,"road planting rejected");
        At(invalidState,-15,23);invalidState.Structures.Add(new(){Id="plant-obstacle",Kind="foundation",X=-15,Z=23});RejectPlant(invalidState,invalidSim,-15,23,"building footprint rejected");invalidState.Structures.Clear();
        invalidState.Crops.Add(new(){Id="plant-crop",X=-15,Z=23});RejectPlant(invalidState,invalidSim,-15,23,"crop spacing rejected");invalidState.Crops.Clear();
        invalidState.Player.Y=Construction.Ground(invalidState,-15,23)+3;RejectPlant(invalidState,invalidSim,-15,23,"planting from an upper floor rejected");invalidState.Player.Y=null;
        Plant(invalidState,invalidSim);RejectPlant(invalidState,invalidSim,-13,23,"plant spacing rejected");
        RejectPlant(invalidState,invalidSim,float.NaN,23,"nonfinite planting coordinates rejected");

        var wildState=new WorldState{Seed=7319};var wildSim=new Simulation(wildState,catalog);
        var wildTree=WorldGenerator.Generate(wildState.Seed).First(r=>r.Kind=="tree");At(wildState,wildTree.X,wildTree.Z);
        Check(wildSim.Gather(wildTree.Id,"tree").Success,"wild tree can be felled");
        wood=wildSim.Count("wood");Check(wildSim.RemoveStump(wildTree.Id).Success&&wildState.RemovedStumps.Contains(wildTree.Id),"wild stump removal records its identity");
        Check(wildSim.Count("wood")==wood+2,"wild stump pays configured default timber");wood=wildSim.Count("wood");
        Check(!wildSim.RemoveStump(wildTree.Id).Success&&wildSim.Count("wood")==wood,"wild stump cannot pay twice");
        var wildBush=WorldGenerator.Generate(wildState.Seed).First(r=>r.Kind=="berries");At(wildState,wildBush.X,wildBush.Z);
        Check(wildSim.Gather(wildBush.Id,"berries").Success,"first wild berry harvest succeeds");
        var retained=wildState.Vegetation.Single(p=>p.Id==wildBush.Id);
        Check(wildState.RemovedResources.Contains(wildBush.Id)&&retained.Growth==1&&retained.Fruit==0,"harvest replaces wild spawn with persistent empty mature bush");
        int berries=wildSim.Count("berries");Check(!wildSim.Gather(wildBush.Id,"berries").Success&&wildSim.Count("berries")==berries,"empty bush cannot immediately harvest again");
        wildSim.Tick(60);Check(Near(retained.Fruit,1d/60)&&retained.Fruit<1,"wild berries regrow slowly over one hour");
        wildSim.Tick(3540.001);Check(wildSim.Gather(wildBush.Id,"berries").Success&&retained.Fruit==0&&wildSim.Count("berries")==berries+3,"regrown wild bush harvests and restarts fruit timer");

        var bushState=Fresh();var bushSim=new Simulation(bushState,catalog);var bush=Plant(bushState,bushSim,"bush_seedling");
        Check(!bushSim.Gather(bush.Id,"berries").Success,"young planted bush cannot be harvested");
        bushSim.Tick(3600);Check(Near(bush.Growth,.5)&&bush.Fruit==1,"planted bush takes two hours and has first crop when mature");
        bushSim.Tick(3600);Check(bush.Growth==1&&bushSim.Gather(bush.Id,"berries").Success&&bush.Fruit==0,"mature planted bush bears berries");
        settings=bushState.Settings.Copy();settings.FreezeVegetation=true;bushSim.UpdateSandbox(settings);bushSim.Tick(3600);Check(bush.Fruit==0,"freeze also pauses harvested bush fruit");
        settings.FreezeVegetation=false;bushSim.UpdateSandbox(settings);bushSim.Tick(3600);Check(bush.Fruit==1&&bushSim.Gather(bush.Id,"berries").Success,"planted bush regrows another harvest");

        var rulesState=Fresh();var rulesSim=new Simulation(rulesState,catalog);
        foreach(Action<SandboxSettings> corrupt in new Action<SandboxSettings>[]
        {
            s=>s.TreeGrowthHours=0,s=>s.TreeGrowthHours=721,s=>s.BushGrowthHours=float.NaN,s=>s.BushRegrowHours=0,
            s=>s.CropGrowthMinutes=0,s=>s.CraftDurationMultiplier=21,s=>s.ResourceYieldMultiplier=0,s=>s.DayLengthMinutes=1,
            s=>s.StaminaRegenMultiplier=-1,s=>s.DamageTakenMultiplier=6,s=>s.DeathCoinLoss=-1,s=>s.StumpWoodYield=21,s=>s.PlantSpacing=1
        })
        {
            string before=JsonSerializer.Serialize(rulesState.Settings);settings=rulesState.Settings.Copy();corrupt(settings);
            Check(!rulesSim.UpdateSandbox(settings).Success&&JsonSerializer.Serialize(rulesState.Settings)==before,"invalid sandbox setting rejected atomically");
        }
        settings=rulesState.Settings.Copy();settings.TreeGrowthHours=12;Check(rulesSim.UpdateSandbox(settings).Success,"valid settings accepted");settings.TreeGrowthHours=99;
        Check(rulesState.Settings.TreeGrowthHours==12,"accepted settings are copied, not aliased to caller");
        var recipe=catalog.Recipes["stone_axe"];Check(rulesSim.EnqueueCraft(recipe.Id,"hand",2).Success,"two crafting batches queued");
        float firstDuration=rulesState.CraftJob!.Duration;settings=rulesState.Settings.Copy();settings.CraftDurationMultiplier=2;rulesSim.UpdateSandbox(settings);
        Check(rulesState.CraftJob.Duration==firstDuration&&rulesState.CraftJob.Remaining==firstDuration,"craft multiplier leaves active reserved batch unchanged");
        rulesSim.TickCraft(firstDuration);Check(rulesState.CraftJob!=null&&rulesState.CraftJob.Duration==recipe.Duration*2,"next queued batch captures updated crafting duration");
        calendar=rulesState.CalendarMinutes;double time=rulesState.Time;settings=rulesState.Settings.Copy();settings.DayLengthMinutes=16;rulesSim.UpdateSandbox(settings);
        Check(rulesState.CalendarMinutes==calendar&&rulesState.Time==time,"changing day length does not jump calendar or active time");
        rulesSim.Tick(60);Check(Near(rulesState.CalendarMinutes,calendar+90)&&rulesState.Time==time+60,"subsequent calendar progression uses new day duration");
        var yieldState=new WorldState{Seed=7319};var yieldSim=new Simulation(yieldState,catalog);settings=yieldState.Settings.Copy();settings.ResourceYieldMultiplier=2;yieldSim.UpdateSandbox(settings);
        var ore=WorldGenerator.Generate(yieldState.Seed).First(r=>r.Kind=="stone");At(yieldState,ore.X,ore.Z);int stone=yieldSim.Count("stone");
        Check(yieldSim.Gather(ore.Id,"stone").Success&&yieldSim.Count("stone")==stone+6,"gather yield multiplier changes actual resource payout");

        string directory=Path.Combine(Path.GetTempPath(),"thornwake-vegetation-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            string path=Path.Combine(directory,"growth.json");SaveStore.Save(path,slowState);var loaded=SaveStore.Load(path);var loadedSim=new Simulation(loaded,catalog);
            Check(loaded.SaveVersion==10&&JsonSerializer.Serialize(loaded.Settings)==JsonSerializer.Serialize(slowState.Settings),"schema7 round-trips sandbox settings");
            Check(loaded.Vegetation.Single().Growth==slow.Growth&&loaded.RemovedStumps.SetEquals(slowState.RemovedStumps),"double growth and removed stumps round-trip exactly");
            settings=loaded.Settings.Copy();settings.FreezeVegetation=false;loadedSim.UpdateSandbox(settings);growth=loaded.Vegetation.Single().Growth;loadedSim.Tick(60);
            Check(loaded.Vegetation.Single().Growth>growth,"loaded vegetation resumes growth");
            string wildPath=Path.Combine(directory,"wild.json");SaveStore.Save(wildPath,wildState);var wildLoaded=SaveStore.Load(wildPath);var wildReloadSim=new Simulation(wildLoaded,catalog);At(wildLoaded,wildTree.X,wildTree.Z);wood=wildReloadSim.Count("wood");
            Check(wildLoaded.RemovedStumps.Contains(wildTree.Id)&&!wildReloadSim.RemoveStump(wildTree.Id).Success&&wood==wildReloadSim.Count("wood"),"wild stump removal survives reload without payout exploit");
            string clearedPath=Path.Combine(directory,"cleared-planted.json");SaveStore.Save(clearedPath,state);var cleared=SaveStore.Load(clearedPath);var clearedSim=new Simulation(cleared,catalog);wood=clearedSim.Count("wood");
            Check(cleared.Vegetation.All(p=>p.Id!=pine.Id)&&!clearedSim.RemoveStump(pine.Id).Success&&clearedSim.Count("wood")==wood,"removed planted stump remains absent after reload");

            var old=Fresh();old.Time=1234;old.SaveVersion=6;old.Player.Inventory["wood"]=17;old.Player.Hotbar[0]=new(){Kind="item",Id="basic_sword"};old.Player.Hotbar[1]=null;old.Player.EquippedItem="basic_sword";
            old.Structures.Add(new(){Id="saved-floor",Kind="foundation",X=-15,Z=23,Elevation=.2f,Modular=true});
            old.Structures.Add(new(){Id="saved-roof",Kind="roof_module",X=-15,Z=23,Elevation=.2f+Construction.Storey,SupportId="saved-floor",Modular=true,Selection=new(){DefinitionId="roof",VariantId="slope",Slope=45,MaterialId="slate"}});
            var document=JsonNode.Parse(JsonSerializer.Serialize(old))!.AsObject();foreach(string field in new[]{"Settings","Vegetation","RemovedStumps","CalendarMinutes"})document.Remove(field);
            string oldPath=Path.Combine(directory,"v6.json"),original=document.ToJsonString();File.WriteAllText(oldPath,original);var migrated=SaveStore.Load(oldPath);new Simulation(migrated,catalog);
            Check(migrated.SaveVersion==10&&migrated.Settings.TreeGrowthHours==8&&migrated.Vegetation.Count==0,"actual schema6 document receives vegetation defaults");
            Check(migrated.CalendarMinutes==480+old.Time*3,"schema6 calendar retains legacy time of day");
            Check(Inventory(migrated)==Inventory(old)&&JsonSerializer.Serialize(migrated.Player.Hotbar)==JsonSerializer.Serialize(old.Player.Hotbar)&&migrated.Player.EquippedItem==old.Player.EquippedItem,"schema6 migration preserves custom inventory, hotbar and equipment");
            Check(JsonSerializer.Serialize(migrated.Structures)==JsonSerializer.Serialize(old.Structures),"schema6 roof variants and support state are preserved");
            SaveStore.Save(oldPath,migrated);SaveStore.Save(oldPath,migrated);Check(File.ReadAllText(oldPath+".v6")==original,"second schema7 save retains original schema6 backup");
        }
        finally{foreach(string file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory);}
        var clearing=Fresh();var clearingSim=new Simulation(clearing,catalog);var young=Plant(clearing,clearingSim);
        int seedlings=clearingSim.Count("pine_sapling");clearing.Player.EquippedItem="hammer";
        Check(!clearingSim.UprootPlant(young.Id).Success&&clearing.Vegetation.Contains(young),"uprooting requires axe or hoe");
        clearing.Player.EquippedItem="hoe";Check(clearingSim.UprootPlant(young.Id).Success&&clearing.Vegetation.Count==0&&clearingSim.Count("pine_sapling")==seedlings,"young plant can be cleared without duplicating seedlings");
        Check(!clearingSim.UprootPlant(young.Id).Success,"uprooting an absent plant is rejected");
        return checks;
    }
}
