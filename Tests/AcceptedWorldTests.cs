using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Thornwake.Core;

internal static class AcceptedWorldTests
{
    internal sealed record TerrainCase(string Name, MacroWorldManifest World, string Fingerprint);
    internal sealed record TerrainFixture(int Format, TerrainCase[] Cases);
    internal sealed record WaterCase(string TerrainCase, int Resolution, string CoarseFingerprint, int BasinCount, int OutletCount, Dictionary<int,string> Tiles);
    internal sealed record WaterFixture(int Format, int TerrainAlgorithm, int DrainageAlgorithm, int RegionalAlgorithm, int Subdivisions, WaterCase[] Cases);
    private static readonly JsonSerializerOptions Json = new() { WriteIndented=true };
    private static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    internal static string TileHash(RegionalDrainageTile tile)=>Hash(JsonSerializer.SerializeToUtf8Bytes(tile));

    // Explicit fixture sampling format 1: freeze geometry, whole-world samples, every mainland,
    // all belt knots/cross-sections and metre-scale render-region seam probes. Never tied to live catalogue.
    internal static string TerrainHash(MacroWorldManifest manifest)
    {
        var world=new MacroWorldGenerator(manifest); using var bytes=new MemoryStream(); using var w=new BinaryWriter(bytes,Encoding.UTF8,true);
        w.Write(1);w.Write(JsonSerializer.Serialize(manifest));w.Write(JsonSerializer.Serialize(world.LandFeatures));w.Write(JsonSerializer.Serialize(world.Ranges));
        void Sample(double x,double z) { var s=world.Sample(x,z);w.Write(x);w.Write(z);w.Write(s.Elevation);w.Write(s.LandInfluence);w.Write(s.MountainInfluence);w.Write(s.LandId??""); }
        for(int z=0;z<64;z++)for(int x=0;x<64;x++)Sample(manifest.OriginX+((x+.5)/64-.5)*manifest.Profile.WorldSize,manifest.OriginZ+((z+.5)/64-.5)*manifest.Profile.WorldSize);
        foreach(var land in world.LandFeatures.Take(manifest.Profile.Continents))
            for(int z=0;z<25;z++)for(int x=0;x<25;x++)Sample(land.Center.X+(x/24d-.5)*land.RadiusX*2.5,land.Center.Z+(z/24d-.5)*land.RadiusX*2.5);
        foreach(var belt in world.Ranges)foreach(var p in belt.Points)
        {
            foreach(double offset in new[]{-1d,-.5,0,.5,1}) {Sample(p.X+offset*belt.Width,p.Z);Sample(p.X,p.Z+offset*belt.Width);}
            double bx=Math.Floor(p.X/WorldRegion.Size)*WorldRegion.Size,bz=Math.Floor(p.Z/WorldRegion.Size)*WorldRegion.Size;
            foreach(double delta in new[]{-.125,0,.125}) {Sample(bx+delta,p.Z);Sample(p.X,bz+delta);}
        }
        w.Flush();return Hash(bytes.ToArray());
    }

    // Explicit recording command writes NEW files only. Normal test runs never rewrite expectations.
    public static void Record()
    {
        string terrainFile="Tests/Fixtures/terrain-a4-v1.json",waterFile="Tests/Fixtures/drainage-a4-v1.json";
        if(File.Exists(terrainFile)||File.Exists(waterFile))throw new IOException("A4 acceptance fixtures already exist; refusing to overwrite.");
        var profiles=MacroProfileCatalog.Load("Content/world_profiles.json");var terrain=new List<TerrainCase>();var water=new List<WaterCase>();
        foreach(var profile in profiles)
        foreach(int seed in new[]{-9,0,42,7319})Add(profile,seed,0,0);
        Add(profiles.Single(p=>p.Id=="vast-world"),7319,8_000_000_000,-7_000_000_000);
        Add(profiles.Single(p=>p.Id=="mountainous"),42,-8_000_000_000,7_000_000_000);
        void Add(MacroWorldProfile profile,int seed,double x,double z)
        {
            var world=MacroWorldManifest.Create(seed,profile,originX:x,originZ:z,algorithm:4) with { WorldId="0123456789abcdef0123456789abcdef" };
            string name=profile.Id+"/"+seed.ToString(CultureInfo.InvariantCulture)+(x==0?"":"/far");
            terrain.Add(new(name,world,TerrainHash(world)));
            var refiner=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(world));var d=refiner.Drainage;
            var cells=Enumerable.Range(0,d.Grid.Count).Where(c=>d.Downstream[c]>=0).OrderByDescending(c=>d.CatchmentArea[c]).Take(2)
                .Concat(d.Basins.OrderByDescending(b=>b.CapacityEstimate).Take(2).Select(b=>b.Cells[0])).Distinct().ToArray();
            var tiles=cells.Concat(cells.Select(c=>d.Downstream[c]).Where(c=>c>=0)).Distinct().Order().ToDictionary(c=>c,c=>TileHash(refiner.Build(c)));
            water.Add(new(name,128,d.Fingerprint,d.Basins.Count,d.Outlets.Count,tiles));
            Console.WriteLine($"A4_RECORDED {name}: {d.Basins.Count} basins, {tiles.Count} tiles");
        }
        File.WriteAllText(terrainFile,JsonSerializer.Serialize(new TerrainFixture(1,terrain.ToArray()),Json));
        File.WriteAllText(waterFile,JsonSerializer.Serialize(new WaterFixture(1,4,1,1,64,water.ToArray()),Json));
    }

    public static int Run()
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("ACCEPTED_A4: "+why);checks++;}
        void Reject(Action action){try{action();}catch(InvalidDataException){checks++;return;}throw new Exception("ACCEPTED_A4: mixed terrain dependency accepted");}
        var terrain=JsonSerializer.Deserialize<TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
        var water=JsonSerializer.Deserialize<WaterFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/drainage-a4-v1.json")))!;
        Check(terrain.Format==1 && terrain.Cases.Length==22 && water.Cases.Length==22 && water.Format==1 && water.TerrainAlgorithm==4 && water.DrainageAlgorithm==1 && water.RegionalAlgorithm==1,"fixture versions and matrix");
        foreach(var t in terrain.Cases)
        {
            Check(t.World.Algorithm==4,"frozen terrain version explicit");
            Check(TerrainHash(t.World)==t.Fingerprint,"frozen terrain "+t.Name);
            var restored=JsonSerializer.Deserialize<MacroWorldManifest>(JsonSerializer.Serialize(t.World))!;
            Check(TerrainHash(restored)==t.Fingerprint,"terrain reconstruction "+t.Name);
            var f=water.Cases.Single(c=>c.TerrainCase==t.Name);var recipe=MacroDrainageRecipe.Create(t.World,f.Resolution);
            var refiner=RegionalDrainageRefiner.Create(recipe,water.Subdivisions);var d=refiner.Drainage;
            Check(d.Fingerprint==f.CoarseFingerprint && d.Basins.Count==f.BasinCount && d.Outlets.Count==f.OutletCount,"dedicated drainage baseline "+t.Name);
            var copy=RegionalDrainageRefiner.Restore(JsonSerializer.Deserialize<RegionalDrainageRecipe>(JsonSerializer.Serialize(refiner.Recipe))!);
            Check(copy.Drainage.Fingerprint==d.Fingerprint && copy.LayerId==refiner.LayerId,"full recipe reconstruction");
            var roots=d.Outlets.Select(o=>o.Cell).ToHashSet();double area=d.Grid.Spacing*d.Grid.Spacing;
            Check(Math.Abs(d.Outlets.Sum(o=>d.CatchmentArea[o.Cell])-d.Grid.Count*area)<1,"all contributing area reaches explicit outlets");
            for(int c=0;c<d.Grid.Count;c++)
            {
                int next=d.Downstream[c]; Check(roots.Contains(d.OutletCell[c]),"valid destination");
                Check(next<0?roots.Contains(c):d.OutletCell[c]==d.OutletCell[next] && d.SpillHeight[next]<=d.SpillHeight[c] && d.CatchmentArea[next]>=d.CatchmentArea[c],"downstream metadata consistent");
                if(next>=0)Check(Math.Abs(c%d.Grid.Width-next%d.Grid.Width)+Math.Abs(c/d.Grid.Width-next/d.Grid.Width)==1,"no jumps across cell faces");
                else Check(d.Grid.Bed[c]<=t.World.Profile.SeaLevel,"only actual sampled ocean outlets");
            }
            foreach(var basin in d.Basins)
            {
                Check(basin.Cells.All(c=>d.SpillHeight[c]==basin.SpillLevel && d.Grid.Bed[c]<basin.SpillLevel),"basin level/bed identity");
                Check(Math.Abs(basin.CapacityEstimate-basin.Cells.Sum(c=>(basin.SpillLevel-d.Grid.Bed[c])*area))<1,"candidate capacity consistent");
                Check(basin.Exits.All(e=>basin.Cells.Contains(e.From)&&!basin.Cells.Contains(e.To)&&d.Downstream[e.From]==e.To),"basin outlet links");
            }
            foreach(var frozen in f.Tiles.Reverse())
            {
                var tile=refiner.Build(frozen.Key);Check(TileHash(tile)==frozen.Value,"frozen regional output "+t.Name+"/"+frozen.Key);
                Check(TileHash(copy.Build(frozen.Key))==frozen.Value,"regional reverse-order reconstruction");
                if(tile.Outgoing is not { } port)continue;
                var next=copy.Build(port.ToCell);Check(next.Incoming.Contains(port),"exact continuation including far origins");
                int dx=next.Cell%d.Grid.Width-tile.Cell%d.Grid.Width,dz=next.Cell/d.Grid.Width-tile.Cell/d.Grid.Width;
                for(int i=0;i<tile.Side;i++)
                {
                    int a=dx!=0?i*tile.Side+(dx>0?tile.Side-1:0):(dz>0?tile.Side-1:0)*tile.Side+i;
                    int b=dx!=0?i*tile.Side+(dx>0?0:tile.Side-1):(dz>0?0:tile.Side-1)*tile.Side+i;
                    Check(tile.Bed[a]==next.Bed[b],"shared boundary terrain");
                }
                foreach(var route in tile.Routes)Check(route.Points[^1]==next.Routes.Single(r=>r.IncomingPortId==port.Id).Points[0],"exact intertile route endpoint");
            }
            // Same world id/profile/seed with different terrain version cannot reuse this layer/checkpoint.
            var swapped=recipe with { World=t.World with { Algorithm=3 } };
            Check(swapped.Digest()!=recipe.Digest(),"terrain version participates in drainage identity");
            Reject(()=>RegionalDrainageRefiner.Restore(refiner.Recipe with { Coarse=swapped }));
        }
        string folder=Path.Combine(Path.GetTempPath(),"thornwake-a4-accepted-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(folder);
        try
        {
            var t=terrain.Cases[^1];var recipe=MacroDrainageRecipe.Create(t.World,32);string file=Path.Combine(folder,"drainage.json");
            MacroDrainageStore.Create(file,recipe);Check(MacroDrainageStore.Load(file).Recipe==recipe,"disk checkpoint preserves A4 terrain");
            var checkpoint=JsonSerializer.Deserialize<MacroDrainageCheckpoint>(File.ReadAllText(file))!;
            foreach(int version in new[]{3,4})
            {
                var baseline=recipe with { World=t.World with { Algorithm=version } };
                var mismatch=new MacroDrainageCheckpoint { Recipe=baseline with { World=baseline.World with { Algorithm=version==3?4:3 } },ExpectedFingerprint=MacroDrainage.Build(baseline).Fingerprint };
                File.WriteAllText(file,JsonSerializer.Serialize(mismatch));Reject(()=>MacroDrainageStore.Load(file));
            }
        }
        finally { foreach(string file in Directory.GetFiles(folder))File.Delete(file);Directory.Delete(folder); }
        Console.WriteLine($"ACCEPTED_A4_PASS: {checks} checks, 22 frozen terrains/drainage worlds, {water.Cases.Sum(c=>c.Tiles.Count)} regional baselines");return checks;
    }
}
