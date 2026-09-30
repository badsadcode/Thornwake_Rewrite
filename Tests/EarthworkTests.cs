using System.Text.Json;
using System.Text.Json.Nodes;
using Thornwake.Core;

internal static class EarthworkTests
{
    public static int Run(ContentCatalog catalog)
    {
        int count=0;
        void Check(bool ok,string why){if(!ok)throw new Exception("EARTHWORKS: "+why);count++;}
        void Reject(Action action,string why){try{action();}catch(InvalidDataException){count++;return;}throw new Exception("EARTHWORKS: "+why);}
        WorldState Fresh()
        {
            var w=new WorldState{Seed=7319};w.Player.Inventory["shovel"]=1;w.Player.EquippedItem="shovel";
            w.Player.X=-36.5f;w.Player.Z=16;w.Player.Y=null;
            foreach(var r in WorldGenerator.Generate(w.Seed)){w.RemovedResources.Add(r.Id);if(r.Kind=="tree")w.RemovedStumps.Add(r.Id);}
            return w;
        }
        var state=Fresh();var sim=new Simulation(state,catalog);
        var plan=sim.PreviewEarthwork(EarthworkMode.Dig,-32,16,1.5f,0);
        Check(plan.Valid,plan.Message);Check(plan.SoilCost<0,"dig recovers soil");
        Check(sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0).Success,"start work");
        Check(state.Earthworks.Steps.Count==0&&sim.Count("soil")==0,"start does not mutate ground or inventory");
        Check(!sim.StartEarthwork(EarthworkMode.Fill,-32,16,1.5f,0).Success,"only one stroke at a time");
        Check(sim.TickEarthwork(.5f)==null&&state.Earthworks.Revision==0&&state.Player.Stamina==100,"partial stroke spends nothing");
        string path=Path.Combine(Path.GetTempPath(),"thornwake-earthwork-"+Guid.NewGuid()+".json");
        try
        {
            SaveStore.Save(path,state);var loaded=SaveStore.Load(path);
            Check(loaded.EarthworkJob?.Remaining==state.EarthworkJob?.Remaining,"remaining work saved exactly");
            var resumed=new Simulation(loaded,catalog);Check(resumed.TickEarthwork(1.2f) is {Success:true},"saved task completes");
            Check(sim.TickEarthwork(1.2f) is {Success:true},"original task completes");
            Check(JsonSerializer.Serialize(loaded.Earthworks)==JsonSerializer.Serialize(state.Earthworks),"resume gives identical grid");
            Check(sim.Count("soil")==-plan.SoilCost&&state.Player.Stamina==92,"resources and stamina commit once");
            Check(state.Earthworks.Steps.Values.Sum()+sim.Count("soil")==0,"volume conservation");
            Check(sim.TickEarthwork(5)==null,"completed stroke cannot repeat");
            float changed=TerrainData.Height(state,-32.3f,16.3f);SaveStore.Save(path,state);var second=SaveStore.Load(path);
            Check(changed==TerrainData.Height(second,-32.3f,16.3f),"interpolated terrain reload is exact");
            var left=TerrainData.SampleRegion(state,new(-3,1),1);var right=TerrainData.SampleRegion(state,new(-2,1),1);
            for(int z=0;z<=16;z++)Check(left[16,z]==right[0,z],"edited region border matches");
            Check(sim.StartEarthwork(EarthworkMode.Fill,-32,16,1.5f,0).Success,"reuse dug soil");
            Check(sim.TickEarthwork(2) is {Success:true}&&state.Earthworks.Steps.Count==0&&sim.Count("soil")==0,"inverse fill restores original grid without gaining soil");
            Check(!sim.StartEarthwork(EarthworkMode.Fill,-32,16,1.5f,0).Success,"cannot fill without soil");
            Check(sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0).Success,"start cancellable work");
            sim.TickEarthwork(.5f);sim.CancelEarthwork();Check(state.Earthworks.Steps.Count==0&&state.Player.Stamina==84,"cancel spends nothing");
            sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0);state.Player.X-=.5f;
            Check(sim.TickEarthwork(.1f) is {Success:false}&&state.EarthworkJob==null,"movement cancels work");state.Player.X+=.5f;
            sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0);sim.EquipItem("hoe");Check(state.EarthworkJob==null,"tool switch cancels immediately");sim.EquipItem("shovel");
            state.Player.Inventory["soil"]=100;sim.StartEarthwork(EarthworkMode.Fill,-32,16,1.5f,0);state.Player.Inventory["soil"]=0;
            Check(sim.TickEarthwork(2) is {Success:false}&&state.Earthworks.Steps.Count==0,"completion revalidates materials atomically");
            sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0);state.Earthworks.Revision++;
            Check(sim.TickEarthwork(2) is {Success:false},"changed terrain cancels stale plan");
            state.Player.Stamina=0;Check(!sim.PreviewEarthwork(EarthworkMode.Dig,-32,16,1.5f,0).Valid,"tired player cannot dig");state.Player.Stamina=100;
            state.Player.Inventory["soil"]=1000;
            foreach(var mode in new[]{EarthworkMode.Level,EarthworkMode.Smooth})
            {
                state.Earthworks.Steps[TerrainGrid.Key(-32,16)]=40;
                int total=state.Earthworks.Steps.Values.Sum()+sim.Count("soil");
                Check(sim.StartEarthwork(mode,-32,16,1.5f,TerrainData.BaseHeight(state,-32,16)).Success,mode+" starts");
                Check(sim.TickEarthwork(2) is {Success:true},mode+" completes");
                Check(state.Earthworks.Steps.Values.Sum()+sim.Count("soil")==total,mode+" conserves soil including mixed cuts and fills");
            }
            state=Fresh();sim=new(state,catalog);state.Earthworks.Steps[TerrainGrid.Key(-32,16)]=-119;
            Check(!sim.PreviewEarthwork(EarthworkMode.Dig,-32,16,1.5f,0).Valid,"bedrock blocks deeper excavation");
            Check(TerrainData.MaterialAt(state,-32,16,TerrainData.BaseHeight(state,-32,16)-1.3f)==TerrainSurface.Rock,"material query and shovel agree");
            state=Fresh();sim=new(state,catalog);
            state.Structures.Add(new(){Id="protect",Kind="foundation",X=-29,Z=16});
            Check(!sim.PreviewEarthwork(EarthworkMode.Dig,-32,16,1.5f,0).Valid,"interpolation footprint protects nearby structure");state.Structures.Clear();
            state.Crops.Add(new(){Id="crop",X=-29,Z=16});Check(!sim.PreviewEarthwork(EarthworkMode.Dig,-32,16,1.5f,0).Valid,"footprint protects crops");state.Crops.Clear();
            state.Player.X=-34;Check(!sim.PreviewEarthwork(EarthworkMode.Dig,-32,16,1.5f,0).Valid,"cannot excavate under own feet");state.Player.X=-36.5f;
            Check(!sim.PreviewEarthwork(EarthworkMode.Dig,0,16,1.5f,0).Valid,"remote work denied");
            Check(!sim.PreviewEarthwork(EarthworkMode.Dig,float.NaN,16,1.5f,0).Valid,"nonfinite target denied");
            Check(!sim.PreviewEarthwork((EarthworkMode)999,-32,16,1.5f,0).Valid,"unknown mode denied");
            Check(!sim.PreviewEarthwork(EarthworkMode.Dig,-32,16,12,0).Valid,"oversized brush denied");
            sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0);
            Check(sim.EnqueueCraft("shovel","hand",1).Success&&state.CraftJob==null,"craft order waits for earthwork");
            sim.TickCraft(10);Check(state.CraftJob==null,"craft cannot run simultaneously");
            sim.CancelEarthwork();sim.TickCraft(.1f);Check(state.CraftJob!=null,"craft starts after earthwork ends");
            Check(!sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0).Success,"cannot shovel during active crafting");
            state=Fresh();state.TerrainEdits.Add(new(){X=-32,Z=16,Radius=2,Delta=.3f});
            float original=TerrainData.Height(state,-31.6f,16.4f);
            var json=JsonNode.Parse(JsonSerializer.Serialize(state))!.AsObject();json["SaveVersion"]=8;json.Remove("Earthworks");json.Remove("EarthworkJob");File.WriteAllText(path,json.ToJsonString());
            var migrated=SaveStore.Load(path);Check(migrated.SaveVersion==10&&migrated.Earthworks.Steps.Count==0&&TerrainData.Height(migrated,-31.6f,16.4f)==original,"v8 migration preserves original terrain exactly");
            SaveStore.Save(path,migrated);Check(File.Exists(path+".v8"),"original v8 save retained");
            state=Fresh();state.Earthworks.Steps[TerrainGrid.Key(-32,16)]=int.MaxValue;Reject(()=>TerrainGrid.Validate(state),"huge height rejected");
            state=Fresh();state.Earthworks.Steps[TerrainGrid.Key(400,16)]=1;Reject(()=>TerrainGrid.Validate(state),"outside samples rejected");
            state=Fresh();state.Earthworks.SampleSpacing=.5f;Reject(()=>TerrainGrid.Validate(state),"unknown grid spacing rejected");
            state=Fresh();sim=new(state,catalog);sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0);state.EarthworkJob!.Remaining=float.NaN;Reject(()=>TerrainGrid.Validate(state),"invalid saved timer rejected");
            state=Fresh();sim=new(state,catalog);sim.EquipItem("hoe");float before=TerrainData.Height(state,-32,16);
            Check(sim.Cultivate(-32,16).Success&&TerrainData.Height(state,-32,16)==before,"hoe cultivates without moving earth");
            Check(!sim.Cultivate(-32,16).Success,"duplicate cultivation rejected");
        }
        finally{foreach(string suffix in new[]{"",".bak",".v8",".tmp"})if(File.Exists(path+suffix))File.Delete(path+suffix);}
        return count;
    }
}
