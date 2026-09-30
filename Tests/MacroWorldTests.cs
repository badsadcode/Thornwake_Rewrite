using System.Globalization;
using System.Text.Json;
using Thornwake.Core;

internal static class MacroWorldTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool ok,string reason) { if(!ok) throw new Exception("MACRO: "+reason); checks++; }
        void Reject(Action action) { try { action(); } catch(InvalidDataException) { checks++; return; } throw new Exception("MACRO: invalid data accepted"); }
        var profiles=MacroProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Content","world_profiles.json"));
        Check(profiles.Count==5,"data-driven starter profiles");
        foreach(var profile in profiles)
        {
            Reject(()=>(profile with { WorldSize=double.NaN }).Validate());
            Reject(()=>(profile with { Continents=999 }).Validate());
            for(int seed=-8;seed<8;seed++)
            {
                var manifest=MacroWorldManifest.Create(seed,profile); var world=new MacroWorldGenerator(manifest);
                var copy=new MacroWorldGenerator(JsonSerializer.Deserialize<MacroWorldManifest>(JsonSerializer.Serialize(manifest))!);
                Check(world.LandFeatures.Count==profile.Continents+profile.Islands && world.Ranges.Count==profile.Continents*profile.RangesPerContinent,"bounded feature counts");
                Check(world.LandFeatures.Select(f=>f.Id).Distinct().Count()==world.LandFeatures.Count,"unique stable features");
                var random=new Random(seed); int dry=0,wet=0;
                for(int i=0;i<256;i++)
                {
                    double x=(random.NextDouble()-.5)*profile.WorldSize,z=(random.NextDouble()-.5)*profile.WorldSize;
                    var a=world.Sample(x,z);var b=copy.Sample(x,z);
                    Check(a==b,"manifest reconstruction and arbitrary sample order");
                    Check(double.IsFinite(a.Elevation) && a.Elevation>=profile.SeaLevel-profile.OceanDepth && a.Elevation<=profile.SeaLevel+profile.LowlandHeight+profile.HillHeight+profile.MountainHeight,"finite bounded relief");
                    if(a.Elevation>profile.SeaLevel) dry++; else wet++;
                    var region=WorldRegion.At(x,z); double boundary=region.X*WorldRegion.Size;
                    Check(Math.Abs(world.Sample(boundary-.001,z).Elevation-world.Sample(boundary+.001,z).Elevation)<.1,"no streaming-border steps");
                }
                Check(dry>5 && wet>20,"both land and ocean across many seeds");
                foreach(var feature in world.LandFeatures.Where(f=>!f.Island))
                    Check(world.Sample(feature.Center.X,feature.Center.Z).Elevation>profile.SeaLevel,"major land centres are on land");
                Check(world.Sample(profile.WorldSize*.5,0).Elevation==profile.SeaLevel-profile.OceanDepth,"world perimeter reaches deep ocean");
            }
        }
        var baseProfile=profiles[0]; var original=MacroWorldManifest.Create(7319,baseProfile);
        var near=new MacroWorldGenerator(original);
        var far=new MacroWorldGenerator(original with { OriginX=8_000_000_000,OriginZ=-8_000_000_000 });
        foreach(var feature in near.LandFeatures.Take(3)) for(int i=0;i<20;i++)
        {
            double x=feature.Center.X+i*.125,z=feature.Center.Z-i*.375;
            Check(Math.Abs(near.Sample(x,z).Elevation-far.Sample(x+far.Manifest.OriginX,z+far.Manifest.OriginZ).Elevation)<.001,"far-origin double precision");
        }
        var previous=CultureInfo.CurrentCulture;
        try { CultureInfo.CurrentCulture=new("pl-PL"); Check(original.ProfileDigest==baseProfile.Digest(),"culture-independent profile identity"); }
        finally { CultureInfo.CurrentCulture=previous; }
        string folder=Path.Combine(Path.GetTempPath(),"thornwake-macro-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(folder);
        string file=Path.Combine(folder,"manifest.json");
        try
        {
            MacroManifestStore.Create(file,original); var loaded=MacroManifestStore.Load(file);
            Check(loaded==original,"complete immutable manifest roundtrip");
            var altered=baseProfile with { MountainHeight=baseProfile.MountainHeight+100 };
            Check(MacroManifestStore.Load(file).Profile.MountainHeight!=altered.MountainHeight,"catalogue edits cannot alter existing world");
            try { MacroManifestStore.Create(file,original with { Seed=999 }); throw new Exception("overwrote manifest"); }
            catch(IOException) { Check(MacroManifestStore.Load(file)==original,"existing world never overwritten"); }
            Reject(()=>(original with { Profile=altered }).Validate());
            Reject(()=>(original with { Algorithm=99 }).Validate());
            File.WriteAllText(file,"{}"); Reject(()=>MacroManifestStore.Load(file));
            Check(Directory.GetFiles(folder,"*.tmp").Length==0,"failed create cleans temporary file");
        }
        finally { foreach(string path in Directory.GetFiles(folder)) File.Delete(path); Directory.Delete(folder); }
        foreach(var profile in profiles)
        {
            var world=new MacroWorldGenerator(MacroWorldManifest.Create(7319,profile));
            var map=MacroWorldMap.Build(world,0,0,profile.WorldSize,128);
            Check(map.Pixels.Length==128*128*4 && map.LandFraction>.03 && map.LandFraction<.75,"bounded diagnostic map");
            Check(map.SampledLandComponents>=profile.Continents,"multiple landmasses visible");
            Check(map.Pixels.SequenceEqual(MacroWorldMap.Build(world,0,0,profile.WorldSize,128).Pixels),"preview stable on reload");
            Console.WriteLine($"MACRO_MAP: {profile.Id} land={map.LandFraction:P1}, components={map.SampledLandComponents}, max={map.MaximumHeight:F0}m");
        }
        using(var cancel=new CancellationTokenSource())
        {
            cancel.Cancel();
            try { MacroWorldMap.Build(near,0,0,baseProfile.WorldSize,128,cancellation:cancel.Token); throw new Exception("preview ignored cancellation"); }
            catch(OperationCanceledException) { checks++; }
        }
        Console.WriteLine($"MACRO_WORLD_PASS: frozen profiles, global features, seams, far origins, maps and immutable saves ({checks} checks)");
        return checks;
    }
}

