using System.Text.Json;
using System.Text.Json.Nodes;
using Thornwake.Core;

internal static class ConstructionTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool value,string why){if(!value)throw new Exception("CONSTRUCTION: "+why);checks++;}
        var state=new WorldState{Seed=7319};
        foreach(var r in WorldGenerator.Generate(state.Seed))state.RemovedResources.Add(r.Id);
        foreach(var id in catalog.Items.Keys)state.Player.Inventory[id]=100;
        var sim=new Simulation(state,catalog);
        void Stand(float x,float z){state.Player.X=x;state.Player.Z=z;}
        Stand(-15,20);
        Check(sim.Build("foundation",-15,23).Success,"first foundation");var a=state.Structures[^1];
        Check(sim.Build("foundation",-12.1f,23.2f).Success,"adjacent floor snaps");var b=state.Structures[^1];
        Check(b.X==-12&&b.Z==23&&b.Elevation==a.Elevation,"connected floors share elevation");
        var before=JsonSerializer.Serialize(state.Player.Inventory);
        Check(!sim.Build("foundation",-15,23).Success&&before==JsonSerializer.Serialize(state.Player.Inventory),"duplicate rejection is atomic");
        Check(!sim.Build("roof",-15,23).Success,"unsupported roof rejected");
        Check(sim.Build("wall",-15,21.5f).Success,"north wall");var north=state.Structures[^1];
        Check(!sim.Build("door",-15,21.5f).Success,"edge slot is exclusive");
        Stand(-18,23);Check(sim.Build("door",-15,24.5f).Success,"south door");var door=state.Structures[^1];
        Check(door.SupportId==a.Id&&Math.Abs(door.Elevation!.Value-a.Elevation!.Value-Construction.Deck)<.001,"door rests on deck");
        Stand(-15,26);
        Check(Construction.SolidAt(door,catalog.Buildings["door"],-15,24.5f),"closed door blocks walking");
        Check(sim.ToggleDoor(door.Id).Success&&door.DoorOpen,"door opens");
        Check(!Construction.SolidAt(door,catalog.Buildings["door"],-15,24.5f),"open doorway is walkable");
        Check(Construction.SolidAt(door,catalog.Buildings["door"],-16.05f,24.5f),"open doorway still has solid jambs");
        Stand(-15,24.5f);Check(!sim.ToggleDoor(door.Id).Success&&door.DoorOpen,"cannot close on player");
        Stand(-15,26);Check(sim.ToggleDoor(door.Id).Success&&!door.DoorOpen,"door closes when clear");
        Check(sim.Build("roof",-15,23).Success,"roof on two edges");var roof=state.Structures[^1];
        Check(Construction.CanRemove(state,north,out _),"redundant roof support can be removed");
        state.Structures.Remove(north);Check(!sim.Dismantle(door.Id).Success,"last connected roof support remains protected");state.Structures.Add(north);
        Check(!sim.Dismantle(a.Id).Success,"cannot remove loaded foundation");
        Check(!sim.ModifyTerrain(-15,23,-.2f).Success,"soil cannot undermine structures");
        Check(Math.Abs(Construction.WalkHeight(state,-15,23)-(a.Elevation!.Value+Construction.Deck))<.001,"walk height follows deck");
        Stand(-18,23);
        int timber=sim.Count("wood"),planks=sim.Count("plank");var expected=sim.ReclaimYield(roof);
        Check(sim.Dismantle(roof.Id).Success,"roof can be reclaimed first");
        Check(sim.Count("wood")==timber+expected.GetValueOrDefault("wood")&&sim.Count("plank")==planks+expected.GetValueOrDefault("plank"),"refund is half of paid cost");
        Check(!sim.Dismantle(roof.Id).Success,"repeat reclaim cannot duplicate materials");
        Check(sim.Dismantle(north.Id).Success&&sim.Dismantle(door.Id).Success&&sim.Dismantle(a.Id).Success,"safe dismantling order");
        Stand(-12,21);Check(sim.Build("workbench",-12,23).Success,"furniture on adjacent deck");
        Check(!sim.Dismantle(b.Id).Success,"furniture retains floor support");
        before=JsonSerializer.Serialize(state.Player.Inventory);Stand(0,8);
        Check(!sim.Build("fence",-40,30).Success&&before==JsonSerializer.Serialize(state.Player.Inventory),"remote build cannot consume materials");
        Stand(-9,20);Check(!sim.Build("wall",-9,20).Success,"wall requires nearby floor and a clear player");
        Stand(0,20);Check(!sim.Build("foundation",0,23).Success,"road corridor protected");

        // A real v1-shaped JSON document, rather than a v2 object marked with an older number.
        string folder=Path.Combine(Path.GetTempPath(),"thornwake-construction-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var old=new WorldState();old.Structures.Add(new StructureState{Id="old-floor",Kind="foundation",X=-9,Z=20});
            var json=JsonNode.Parse(JsonSerializer.Serialize(old))!.AsObject();json["SaveVersion"]=1;
            foreach(var piece in json["Structures"]!.AsArray())foreach(var key in new[]{"Elevation","SupportId","Modular","DoorOpen","PaidCosts"})piece!.AsObject().Remove(key);
            string path=Path.Combine(folder,"old.json");File.WriteAllText(path,json.ToJsonString());var migrated=SaveStore.Load(path);
            Check(migrated.SaveVersion==10&&migrated.Structures[0].Id=="old-floor"&&!migrated.Structures[0].Modular,"v1 migration preserves old piece");
            SaveStore.Save(path,migrated);Check(File.Exists(path+".bak")&&JsonNode.Parse(File.ReadAllText(path+".bak"))!["SaveVersion"]!.GetValue<int>()==1,"old save retained as backup");
            SaveStore.Save(path,migrated);Check(JsonNode.Parse(File.ReadAllText(path+".v1"))!["SaveVersion"]!.GetValue<int>()==1,"schema-1 backup survives later autosaves");
            string current=Path.Combine(folder,"new.json");SaveStore.Save(current,state);var loaded=SaveStore.Load(current);
            Check(JsonSerializer.Serialize(state)==JsonSerializer.Serialize(loaded),"support elevation and costs survive roundtrip");
        }
        finally{foreach(var file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder);}
        return checks;
    }
}


