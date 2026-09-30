using System.Text.Json;
using System.Text.Json.Nodes;
using Thornwake.Core;

internal static class HotbarTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool condition,string message){if(!condition)throw new Exception("HOTBAR: "+message);checks++;}
        void Reject(Action action,string message){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception("HOTBAR: "+message);}
        string Inventory(WorldState state)=>JsonSerializer.Serialize(state.Player.Inventory);
        WorldState Clone(WorldState state)=>JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(state))!;
        var state=new WorldState();var sim=new Simulation(state,catalog);
        Check(state.Player.Hotbar.Count==10&&state.Player.Hotbar[6]==null&&state.Player.Hotbar[8]==null&&state.Player.Hotbar[9]==null,"ten starter slots include honest empty slots");
        Check(state.Player.Hotbar[0]?.Id=="basic_axe"&&state.Player.Hotbar[4]?.Id=="basic_sword"&&state.Player.Hotbar[7]?.Kind=="skill"&&state.Player.Hotbar[7]?.Id=="dodge","starter bindings point to owned tools and the real dodge ability");
        Check(HotbarRules.StarterItems.All(id=>sim.Count(id)==1)&&sim.Count("health_potion")==0,"starter gear is owned and potion is not granted for free");
        Check(HotbarRules.ToolMode(catalog,"basic_axe")==0&&HotbarRules.ToolMode(catalog,"hammer")==1&&HotbarRules.ToolMode(catalog,"hoe")==2&&HotbarRules.ToolMode(catalog,"seeds")==3&&HotbarRules.ToolMode(catalog,"basic_sword")==4,"tool modes describe actions rather than hotbar positions");
        Check(HotbarRules.ToolMode(catalog,"berries")==-1&&HotbarRules.ToolMode(catalog,"")==-1,"consumables and empty hands are not weapon modes");
        string inventory=Inventory(state);int journal=state.Journal.Count;
        Check(sim.AssignHotbar(9,"item","basic_sword").Success&&sim.SwapHotbar(9,8).Success&&state.Player.Hotbar[8]?.Id=="basic_sword"&&state.Player.Hotbar[9]==null,"dragging to an empty slot moves the binding");
        Check(sim.SwapHotbar(8,0).Success&&state.Player.Hotbar[0]?.Id=="basic_sword"&&state.Player.Hotbar[8]?.Id=="basic_axe","dropping on an occupied slot swaps bindings");
        Check(sim.ClearHotbar(8).Success&&state.Player.Hotbar[8]==null&&Inventory(state)==inventory,"clearing and rearranging never change inventory ownership");
        Check(!sim.AssignHotbar(-1,"item","berries").Success&&!sim.AssignHotbar(10,"skill","dodge").Success&&!sim.SwapHotbar(8,9).Success,"invalid indexes and empty drag sources are rejected");
        Check(!sim.AssignHotbar(6,"item","health_potion").Success&&!sim.AssignHotbar(6,"item","missing").Success&&!sim.AssignHotbar(6,"item","wood").Success,"assignment requires an owned usable item");
        Check(!sim.AssignHotbar(6,"skill","fireball").Success&&!sim.AssignHotbar(6,"unknown","dodge").Success,"unimplemented skills and binding kinds are rejected");
        Check(sim.AssignHotbar(6,"skill","dodge").Success&&sim.ClearHotbar(6).Success,"available skills can be assigned and cleared");
        Check(sim.ActivateHotbar(0) is {Success:true,Action:"sword",Id:"basic_sword"}&&state.Player.EquippedItem=="basic_sword","activation follows the moved sword binding");
        Check(sim.ClearHotbar(0).Success&&state.Player.EquippedItem=="basic_sword","clearing a binding leaves the owned equipped weapon intact");
        Check(!sim.ActivateHotbar(0).Success&&!sim.ActivateHotbar(99).Success,"empty or invalid slots cannot activate");
        state.Player.Inventory["axe"]=1;
        Check(sim.AssignHotbar(0,"item","axe").Success&&sim.ActivateHotbar(0).Success&&state.Player.EquippedItem=="axe","crafted upgrade can be bound and equipped separately");
        Check(!sim.EquipItem("coins").Success&&!sim.EquipItem("missing").Success,"materials and unknown items cannot be equipped");
        Check(sim.EquipItem("").Success&&state.Player.EquippedItem=="","unequip produces genuinely empty hands");
        Check(state.Journal.Count==journal,"inventory administration and equipment changes do not flood activity history");

        state=new WorldState();sim=new Simulation(state,catalog);sim.EquipItem("basic_sword");state.Player.Inventory["health_potion"]=2;state.Player.Health=50;state.Player.Stamina=47;
        Check(sim.AssignHotbar(6,"item","health_potion").Success&&sim.AssignHotbar(9,"item","health_potion").Success,"one owned stack can have multiple shortcut bindings");
        Check(sim.ActivateHotbar(6) is {Success:true,Action:"consume"}&&sim.Count("health_potion")==1&&state.Player.Health==90&&state.Player.Stamina==47,"potion consumes exactly one and applies its exact data-defined effect");
        Check(state.Player.EquippedItem=="basic_sword"&&HotbarRules.Remaining(state,"item","health_potion")==8,"consuming retains the equipped sword and starts recovery");
        inventory=Inventory(state);
        Check(!sim.ActivateHotbar(9).Success&&Inventory(state)==inventory&&state.Player.Health==90,"duplicate bindings share the same item cooldown");
        sim.Tick(3.25);Check(MathF.Abs(HotbarRules.Remaining(state,"item","health_potion")-4.75f)<.001f,"cooldown advances with world time");
        sim.Tick(4.75);Check(HotbarRules.Remaining(state,"item","health_potion")==0,"finished cooldown is removed");
        Check(sim.ActivateHotbar(9).Success&&state.Player.Health==100&&sim.Count("health_potion")==0,"restoration caps at maximum while consuming only one remaining potion");
        Check(state.Player.Hotbar[9]?.Id=="health_potion"&&!HotbarRules.Available(state,catalog,state.Player.Hotbar[9])&&!sim.ActivateHotbar(9).Success,"depleted bindings remain visible but unavailable");
        Check(!sim.AssignHotbar(8,"item","health_potion").Success,"depleted item cannot create a new binding");
        state.Player.Inventory["health_potion"]=1;sim.Tick(8);state.Player.Stamina=0;inventory=Inventory(state);
        Check(!sim.ConsumeItem("health_potion").Success&&Inventory(state)==inventory&&!state.Player.Cooldowns.ContainsKey("item:health_potion"),"health-only potion is not wasted when only stamina is missing");
        int berries=sim.Count("berries");Check(sim.ConsumeItem("berries").Success&&sim.Count("berries")==berries-1&&state.Player.Stamina==30,"food can restore stamina at full health");
        Check(!sim.Eat().Success&&sim.Count("berries")==berries-1,"Eat uses the same item cooldown and cannot bypass hotbar rules");
        sim.Tick(2);state.Player.Health=100;state.Player.Stamina=100;inventory=Inventory(state);
        Check(!sim.ConsumeItem("berries").Success&&Inventory(state)==inventory,"full meters do not consume food");
        Check(!sim.ConsumeItem("stone").Success&&!sim.ConsumeItem("missing").Success,"non-consumables cannot apply fabricated effects");
        state.Player.Inventory["chair"]=1;Check(sim.AssignHotbar(8,"item","chair").Success&&sim.ActivateHotbar(8) is {Success:true,Action:"place",Id:"chair"}&&sim.Count("chair")==1&&state.Player.EquippedItem=="basic_sword","decoration activation requests placement without prematurely consuming it or replacing gear");

        state=new WorldState();sim=new Simulation(state,catalog);state.Player.Stamina=23;journal=state.Journal.Count;
        Check(!sim.ActivateHotbar(7).Success&&state.Player.Stamina==23&&state.Player.Cooldowns.Count==0,"insufficient stamina cannot activate dodge or start a cooldown");
        state.Player.Stamina=100;
        Check(sim.ActivateHotbar(7) is {Success:true,Action:"dodge",Id:"dodge"}&&state.Player.Stamina==76&&HotbarRules.Remaining(state,"skill","dodge")==1,"skill hotkey pays stamina once and returns the real engine action");
        Check(!sim.UseAbility("dodge").Success&&state.Player.Stamina==76,"keyboard ability path shares the hotbar cooldown");
        sim.Tick(.5);Check(MathF.Abs(HotbarRules.Remaining(state,"skill","dodge")-.5f)<.001f,"ability cooldown advances fractionally");
        sim.Tick(.5);float before=state.Player.Stamina;
        Check(sim.UseAbility("dodge").Success&&state.Player.Stamina==before-24&&state.Player.EquippedItem=="basic_axe","ready ability activates without replacing equipped gear");
        Check(!sim.UseAbility("missing").Success&&state.Journal.Count==journal,"abilities reject unknown actions and do not bury meaningful history");
        var depleted=Clone(state);depleted.Player.Inventory[depleted.Player.EquippedItem]=0;new Simulation(depleted,catalog);
        Check(depleted.Player.EquippedItem=="","loading an unavailable equipped item normalizes to empty hands");
        var badEquipment=Clone(state);badEquipment.Player.EquippedItem="unknown-tool";Reject(()=>new Simulation(badEquipment,catalog),"unknown equipped content is rejected");

        var quests=new WorldState();var questSim=new Simulation(quests,catalog);
        Check(quests.CompletedQuests.Count==0,"new player has no fabricated quest completions");
        var resource=WorldGenerator.Generate(quests.Seed).First();questSim.Gather(resource.Id,resource.Kind);
        Check(quests.CompletedQuests.Contains("gather_resources"),"actual gathering completes its milestone");
        quests.Player.Inventory["axe"]=1;quests.Player.Skills["crafting"]=5;QuestTracker.Refresh(quests);
        Check(!quests.CompletedQuests.Contains("craft_axe"),"owning an axe and unrelated crafting practice do not fake crafting it");
        quests.Player.Inventory["wood"]=10;quests.Player.Inventory["stone"]=10;
        Check(questSim.Craft("stone_axe").Success&&!quests.CompletedQuests.Contains("craft_axe"),"starting an axe job is not completing it");
        questSim.CancelCraft();Check(!quests.CompletedQuests.Contains("craft_axe"),"cancelling a job does not grant its milestone");
        questSim.Craft("stone_axe");questSim.TickCraft(catalog.Recipes["stone_axe"].Duration);
        Check(quests.CompletedQuests.Contains("craft_axe"),"completed crafting event grants the axe milestone");
        foreach(var id in catalog.Items.Keys)quests.Player.Inventory[id]=100;
        foreach(var spawn in WorldGenerator.Generate(quests.Seed))quests.RemovedResources.Add(spawn.Id);
        quests.Player.X=-18;quests.Player.Z=23;
        Check(questSim.Build("foundation",-15,23).Success&&questSim.Build("wall",-15,21.5f).Success&&!quests.CompletedQuests.Contains("build_shelter"),"unfinished frame does not count as a shelter");
        Check(questSim.Build("roof",-15,23).Success&&quests.CompletedQuests.Contains("build_shelter"),"supported roof completes a real connected shelter");
        var roof=quests.Structures[^1];Check(questSim.Dismantle(roof.Id).Success&&quests.CompletedQuests.Contains("build_shelter"),"completion survives later shelter changes");
        Check(questSim.DefeatEnemy().Success&&quests.CompletedQuests.Contains("clear_road"),"actual enemy defeat completes road milestone");
        quests.Player.Inventory["axe"]=0;quests.RemovedResources.Clear();inventory=Inventory(quests);QuestTracker.Refresh(quests);QuestTracker.Refresh(quests);
        Check(quests.CompletedQuests.Count==4&&Inventory(quests)==inventory,"completed milestones stay earned without repeated rewards or resource requirements");

        string directory=Path.Combine(Path.GetTempPath(),"thornwake-hotbar-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var saved=new WorldState();var savedSim=new Simulation(saved,catalog);savedSim.AssignHotbar(9,"item","berries");savedSim.SwapHotbar(9,0);savedSim.ClearHotbar(1);
            saved.Player.Inventory["berries"]=0;saved.Player.Cooldowns["item:health_potion"]=7.125f;saved.Player.Cooldowns["skill:dodge"]=.75f;saved.CompletedQuests.UnionWith(quests.CompletedQuests);
            string path=Path.Combine(directory,"current.json");SaveStore.Save(path,saved);var loaded=SaveStore.Load(path);new Simulation(loaded,catalog);
            Check(JsonSerializer.Serialize(saved)==JsonSerializer.Serialize(loaded),"custom and depleted bindings, gear, cooldowns and quests roundtrip exactly");
            Check(loaded.Player.Hotbar[0]?.Id=="berries"&&loaded.Player.Hotbar[1]==null&&loaded.Player.Hotbar[9]?.Id=="basic_axe","loading current schema never restores default bindings over customization");
            var malformed=Clone(saved);malformed.Player.Hotbar.RemoveAt(0);Reject(()=>SaveStore.Save(path,malformed),"malformed hotbar length is rejected");
            malformed=Clone(saved);malformed.Player.Hotbar[0]=new(){Kind="magic",Id="dodge"};Reject(()=>SaveStore.Save(path,malformed),"unknown binding kind is rejected");
            malformed=Clone(saved);malformed.Player.Hotbar[0]=new(){Kind="item",Id="missing"};Reject(()=>new Simulation(malformed,catalog),"unknown saved bound item is rejected before play");
            malformed=Clone(saved);malformed.Player.Cooldowns["skill:dodge"]=float.NaN;Reject(()=>SaveStore.Save(path,malformed),"nonfinite cooldown is rejected");
            malformed=Clone(saved);malformed.Player.Cooldowns["item:health_potion"]=-1;Reject(()=>SaveStore.Save(path,malformed),"negative cooldown is rejected");
            malformed=Clone(saved);malformed.Player.Cooldowns["skill:missing"]=1;Reject(()=>new Simulation(malformed,catalog),"unknown cooldown action is rejected");
            malformed=Clone(saved);malformed.CompletedQuests.Add("imaginary-reward");Reject(()=>SaveStore.Save(path,malformed),"unknown quest completion is rejected");
            Check(SaveStore.Load(path).Player.Hotbar[0]?.Id=="berries","failed writes preserve prior customization");
            var missingFields=JsonNode.Parse(File.ReadAllText(path))!.AsObject();missingFields["Player"]!.AsObject().Remove("Hotbar");string broken=Path.Combine(directory,"missing.json");File.WriteAllText(broken,missingFields.ToJsonString());
            Reject(()=>SaveStore.Load(broken),"missing current-schema hotbar is not silently replaced with defaults");

            var old=new WorldState{SaveVersion=5};old.Player.Inventory["axe"]=2;var json=JsonNode.Parse(JsonSerializer.Serialize(old))!.AsObject();
            json.Remove("CompletedQuests");var oldPlayer=json["Player"]!.AsObject();foreach(string field in new[]{"Hotbar","EquippedItem","Cooldowns"})oldPlayer.Remove(field);
            foreach(string starter in HotbarRules.StarterItems)oldPlayer["Inventory"]!.AsObject().Remove(starter);
            string oldPath=Path.Combine(directory,"old.json"),original=json.ToJsonString();File.WriteAllText(oldPath,original);var migrated=SaveStore.Load(oldPath);
            Check(migrated.SaveVersion==10&&HotbarRules.StarterItems.All(id=>migrated.Player.Inventory.GetValueOrDefault(id)==1)&&migrated.Player.Inventory["axe"]==2,"v5 migration grants formerly implicit tools without losing crafted upgrades");
            Check(migrated.Player.Hotbar.Count==10&&migrated.Player.EquippedItem=="basic_axe"&&migrated.Player.Cooldowns.Count==0,"older world receives valid initial bindings and equipment");
            SaveStore.Save(oldPath,migrated);SaveStore.Save(oldPath,migrated);
            Check(File.ReadAllText(oldPath+".v5")==original,"original v5 backup survives subsequent saves");
            var current=SaveStore.Load(oldPath);current.Player.Inventory["hammer"]=0;current.Player.Hotbar[1]=null;SaveStore.Save(oldPath,current);var reload=SaveStore.Load(oldPath);
            Check(reload.Player.Inventory["hammer"]==0&&reload.Player.Hotbar[1]==null,"current saves never regrant sold tools or cleared bindings");
        }
        finally{foreach(var file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory);}

        var brewing=new WorldState();var brewSim=new Simulation(brewing,catalog);brewing.Player.X=-7;brewing.Player.Z=4;brewing.Player.Inventory["berries"]=3;brewing.Player.Inventory["grain"]=1;
        Check(brewSim.Craft("health_potion","village:cooking").Success&&brewSim.Count("health_potion")==0,"tonic uses a real cooking-station recipe and takes time");
        brewSim.TickCraft(6);Check(brewSim.Count("health_potion")==1&&brewSim.Count("berries")==0&&brewSim.Count("grain")==0,"finished tonic consumes its stated ingredients and yields one usable potion");
        return checks;
    }
}
