using System.Text.Json;
using System.Text.Json.Nodes;
using Thornwake.Core;

internal static class LandscapeTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool ok,string why){if(!ok)throw new Exception("LANDSCAPE: "+why);checks++;}
        void Reject(Action act,string why){try{act();}catch(InvalidDataException){checks++;return;}throw new Exception("LANDSCAPE: "+why);}
        var gen=new GenerationSettings();var rules=new SandboxSettings();
        var a=LandscapeGenerator.Create(7319,gen,rules);gen.Relief=1.6f;rules.TreeGrowthHours=100;
        Check(a.Generation.Relief==1&&a.Settings.TreeGrowthHours==8,"new world copies draft settings");
        var heights=new HashSet<string>();
        foreach(int seed in Enumerable.Range(-12,24).Append(int.MinValue).Append(int.MaxValue))
        {
            var w=LandscapeGenerator.Create(seed,new(),new());var clone=LandscapeGenerator.Create(seed,new(),new());
            float lo=100,hi=-100;var signature=new List<float>();
            for(int x=-52;x<=52;x+=4)for(int z=-52;z<=52;z+=4)
            {
                float h=TerrainData.Height(w,x,z);Check(float.IsFinite(h)&&h>-3&&h<15,"bounded finite terrain");
                Check(h==TerrainData.Height(clone,x,z),"deterministic seed/settings");lo=Math.Min(lo,h);hi=Math.Max(hi,h);signature.Add(h);
                var stages=LandscapeGenerator.Sample(x,z,seed,w.Generation);Check(stages.Height==h&&float.IsFinite(stages.Macro)&&float.IsFinite(stages.Prepared),"inspectable pass result matches terrain authority");
            }
            Check(hi-lo>3.5f,"landscape has meaningful relief");heights.Add(JsonSerializer.Serialize(signature));
            foreach(var p in new[]{(-9f,-8f),(9f,-9f),(-11f,9f),(10f,10f),(0f,8f),(-29f,-23f),(20f,8f)})Check(Math.Abs(TerrainData.Height(w,p.Item1,p.Item2))<.016f,"authored sites remain prepared");
            for(int x=-52;x<=52;x+=4)if(Math.Abs(x-27)>9)Check(Math.Abs(TerrainData.Height(w,x,0))<.001f,"main road grade retained");
            for(int z=-52;z<=52;z+=4)Check(TerrainData.Height(w,27,z)==-1.6f,"river channel continuous");
            var resources=WorldGenerator.Generate(w);Check(resources.SequenceEqual(WorldGenerator.Generate(clone)),"resource filtering deterministic");
            Check(resources.Count>70&&resources.Select(r=>r.Id).Distinct().Count()==resources.Count,"useful resource supply and unique IDs");
            Check(resources.All(r=>WorldGenerator.Generate(seed).Contains(r)),"stable candidate identities");
            var sparse=LandscapeGenerator.Create(seed,new(){ForestDensity=.4f},new());var dense=LandscapeGenerator.Create(seed,new(){ForestDensity=1},new());
            Check(WorldGenerator.Generate(sparse).All(r=>WorldGenerator.Generate(dense).Contains(r)),"density adds candidates without changing existing IDs");
        }
        Check(heights.Count==26,"all tested seeds produce distinct landscapes");
        var gentle=LandscapeGenerator.Create(7319,new(){Relief=.4f},new());var rugged=LandscapeGenerator.Create(7319,new(){Relief=1.6f},new());
        Check(TerrainData.Height(rugged,-36,35)>TerrainData.Height(gentle,-36,35)+2,"relief changes real height");
        string path=Path.Combine(Path.GetTempPath(),"thornwake-landscape-"+Guid.NewGuid()+".json");
        try
        {
            a=LandscapeGenerator.Create(7319,new(){Relief=1.25f,ForestDensity=.6f,Roughness=.8f},new());
            foreach(var r in WorldGenerator.Generate(a)){a.RemovedResources.Add(r.Id);if(r.Kind=="tree")a.RemovedStumps.Add(r.Id);}
            a.Player.Inventory["shovel"]=1;a.Player.EquippedItem="shovel";a.Player.X=-36.5f;a.Player.Z=16;a.Player.Y=TerrainData.Height(a,a.Player.X,a.Player.Z);
            var sim=new Simulation(a,catalog);var stroke=sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0);Check(stroke.Success,stroke.Message);
            sim.TickEarthwork(.4f);SaveStore.Save(path,a);var loaded=SaveStore.Load(path);var resumed=new Simulation(loaded,catalog);
            Check(loaded.GeneratorVersion==2&&loaded.Generation.Relief==1.25f&&loaded.Generation.ForestDensity==.6f,"generation settings survive reload");
            Check(WorldGenerator.Generate(a).SequenceEqual(WorldGenerator.Generate(loaded)),"resource identity survives saved settings");
            Check(resumed.TickEarthwork(1.3f) is {Success:true}&&sim.TickEarthwork(1.3f) is {Success:true},"earthworks resume on generator 2");
            for(float x=-34;x<-30;x+=.31f)Check(TerrainData.Height(a,x,16.2f)==TerrainData.Height(loaded,x,16.2f),"exact edited new terrain after resume");
            a.Player.Inventory["wood"]=100;a.Player.Inventory["stone"]=100;a.Player.EquippedItem="hammer";
            bool built=false;
            for(int x=-44;x<=-22&&!built;x+=3)for(int z=25;z<=44&&!built;z+=3)
            {
                a.Player.X=x;a.Player.Z=z;a.Player.Y=TerrainData.Height(a,x,z);
                built=sim.Build("foundation",x,z,0,0,BuildSnapMode.Free).Success;
            }
            Check(built,"construction finds a usable site on new terrain");SaveStore.Save(path,a);Check(SaveStore.Load(path).Structures.Count==1,"built site persists with landscape settings");
            var old=new WorldState{Seed=7319};old.TerrainEdits.Add(new(){X=-32,Z=16,Radius=3,Delta=-.3f});old.Earthworks.Steps[TerrainGrid.Key(-31,16)]=-8;
            var json=JsonNode.Parse(JsonSerializer.Serialize(old))!.AsObject();json["SaveVersion"]=9;json.Remove("Generation");File.WriteAllText(path,json.ToJsonString());
            var migrated=SaveStore.Load(path);Check(migrated.SaveVersion==10&&migrated.GeneratorVersion==1,"legacy generator never silently upgrades");
            Check(TerrainData.Height(migrated,-31.3f,16.7f)==TerrainData.Height(old,-31.3f,16.7f),"both legacy edits and shovel samples preserved exactly");
            Check(WorldGenerator.Generate(migrated).SequenceEqual(WorldGenerator.Generate(old.Seed)),"legacy resources unchanged");SaveStore.Save(path,migrated);Check(File.Exists(path+".v9"),"original schema-9 save retained");
            json=JsonNode.Parse(JsonSerializer.Serialize(a))!.AsObject();json.Remove("Generation");File.WriteAllText(path,json.ToJsonString());Reject(()=>SaveStore.Load(path),"missing generation settings rejected");
            a.Generation.Relief=float.NaN;Reject(()=>new Simulation(a,catalog),"nonfinite settings rejected before rendering");a.Generation=new(){Version=2};Reject(()=>new Simulation(a,catalog),"unknown generation settings schema rejected");
            a.Generation=new();a.GeneratorVersion=3;Reject(()=>new Simulation(a,catalog),"unknown generator rejected");
        }
        finally{foreach(string suffix in new[]{"",".bak",".v9",".tmp"})if(File.Exists(path+suffix))File.Delete(path+suffix);}
        return checks;
    }
}
