using System.Text.Json;
using Thornwake.Core;

internal static class MacroMountainsV4Tests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool ok,string why) { if(!ok)throw new Exception("MOUNTAIN_V4: "+why); checks++; }
        void Reject(Action action) { try{action();}catch(InvalidDataException){checks++;return;}throw new Exception("MOUNTAIN_V4: unsupported version accepted"); }
        var profiles=MacroProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Content","world_profiles.json"));
        Check(MacroWorldManifest.CurrentAlgorithm==3,"experimental morphology must not change default");
        foreach(var profile in profiles)
        foreach(int seed in new[]{-9,0,42,7319,int.MinValue,int.MaxValue})
        {
            var original=MacroWorldManifest.Create(seed,profile);
            var a3=new MacroWorldGenerator(original);
            var manifest=original with { Algorithm=4 };
            var a4=new MacroWorldGenerator(manifest);
            var copy=new MacroWorldGenerator(JsonSerializer.Deserialize<MacroWorldManifest>(JsonSerializer.Serialize(manifest))!);
            Check(a3.ExperimentalMountains==null && a4.ExperimentalMountains!=null,"deliberate version dispatch");
            Check(a3.LandFeatures.SequenceEqual(a4.LandFeatures),"continent/island geometry and identities unchanged");
            Check(a4.Ranges.Count>=profile.Continents && a4.Ranges.Count<=profile.Continents*profile.RangesPerContinent,"bounded belt budget");
            Check(a4.ExperimentalMountains!.SegmentCount<=profile.Continents*MacroMountainsV4.MaximumBeltsPerContinent*MacroMountainsV4.MaximumSegmentsPerBelt,"bounded sampling budget");
            Check(a4.Ranges.Select(r=>r.Id).Distinct().Count()==a4.Ranges.Count,"unique identities");
            Check(JsonSerializer.Serialize(a4.Ranges)==JsonSerializer.Serialize(copy.Ranges),"descriptor reconstruction");
            var random=new Random(seed); var locations=new MacroPoint[512];
            var samples=new MacroTerrainSample[locations.Length];
            for(int i=0;i<locations.Length;i++)
            {
                // Half the probes target the mountain field, half sample the whole world.
                var f=a4.LandFeatures[i%profile.Continents];
                double x=i%2==0?f.Center.X+(random.NextDouble()-.5)*f.RadiusX*2:(random.NextDouble()-.5)*profile.WorldSize;
                double z=i%2==0?f.Center.Z+(random.NextDouble()-.5)*f.RadiusZ*2:(random.NextDouble()-.5)*profile.WorldSize;
                locations[i]=new(x,z); var s=a4.Sample(x,z); samples[i]=s; var old=a3.Sample(x,z);
                Check(double.IsFinite(s.Elevation) && s.Elevation>=profile.SeaLevel-profile.OceanDepth && s.Elevation<=profile.SeaLevel+profile.LowlandHeight+profile.HillHeight+profile.MountainHeight,"finite bounded terrain");
                Check(double.IsFinite(s.MountainInfluence) && s.MountainInfluence>=0 && s.MountainInfluence<=1,"bounded mountain contribution");
                Check(old.LandInfluence==s.LandInfluence && old.LandId==s.LandId,"land ownership and coastline unaffected");
                if(s.LandInfluence<=0)Check(old==s,"bathymetry unaffected");
                Check(copy.Sample(x,z)==s,"exact reconstructed sample");
                double boundary=WorldRegion.At(x,z).X*WorldRegion.Size;
                Check(Math.Abs(a4.Sample(boundary-.001,z).Elevation-a4.Sample(boundary+.001,z).Elevation)<.1,"streaming border continuity");
            }
            var reordered=new MacroTerrainSample[locations.Length];
            Parallel.For(0,locations.Length,i=>{var p=locations[locations.Length-1-i];reordered[locations.Length-1-i]=copy.Sample(p.X,p.Z);});
            Check(samples.SequenceEqual(reordered),"parallel arbitrary-order queries");
            Check(samples.Any(s=>s.MountainInfluence>0),"exercise mountain field");
            // Dense local probes straddling bent-path projections and outer support. Shrinking
            // the interval must converge, including where neighbouring segments compete.
            foreach(var range in a4.Ranges)
            foreach(var p in range.Points.Where((_,i)=>i%3==0))
            for(int ring=0;ring<3;ring++)
            for(int direction=0;direction<8;direction++)
            {
                double angle=direction*Math.PI/4, x=p.X+Math.Cos(angle)*range.Width*ring, z=p.Z+Math.Sin(angle)*range.Width*ring;
                double field=a4.ExperimentalMountains.Sample(x,z);
                Check(double.IsFinite(field) && field>=0 && field<=profile.MountainHeight,"finite full contribution envelope");
                Check(Math.Abs(a4.ExperimentalMountains.Sample(x-.0001,z)-a4.ExperimentalMountains.Sample(x+.0001,z))<.1,"continuous curved-belt projection");
                Check(Math.Abs(a4.ExperimentalMountains.Sample(x,z-.0001)-a4.ExperimentalMountains.Sample(x,z+.0001))<.1,"continuous projection second axis");
            }
            var far=new MacroWorldGenerator(manifest with { OriginX=8_000_000_000,OriginZ=-8_000_000_000 });
            foreach(var p in locations.Take(80))Check(Math.Abs(a4.Sample(p.X,p.Z).Elevation-far.Sample(p.X+far.Manifest.OriginX,p.Z+far.Manifest.OriginZ).Elevation)<.01,"far-origin translation");
            Check(a4.Sample(profile.WorldSize*.5,0)==a3.Sample(profile.WorldSize*.5,0),"world ocean perimeter unchanged");
        }
        var distinctManifest=MacroWorldManifest.Create(42,profiles[0],algorithm:4);
        var distinctA=new MacroWorldGenerator(distinctManifest); var distinctB=new MacroWorldGenerator(distinctManifest with { Seed=43 });
        Check(!distinctA.Ranges.Select(r=>r.Id).SequenceEqual(distinctB.Ranges.Select(r=>r.Id)),"seed isolates descriptor identities");
        var landCenter=distinctA.LandFeatures[0].Center;
        Check(distinctA.Sample(landCenter.X,landCenter.Z)!=distinctB.Sample(landCenter.X,landCenter.Z),"seed changes reconstructed terrain");
        foreach(var profile in new[]{profiles[0] with { MountainHeight=0 },profiles[0] with { RangesPerContinent=0 }})
        {
            var manifest=MacroWorldManifest.Create(42,profile); var old=new MacroWorldGenerator(manifest); var next=new MacroWorldGenerator(manifest with { Algorithm=4 });
            for(int i=-50;i<50;i++)Check(old.Sample(i*813,i*537)==next.Sample(i*813,i*537),"no-mountain base/hill equality");
            Check(next.Ranges.Count==0 && next.ExperimentalMountains!.SegmentCount==0,"disabled mountains allocate no belts");
        }
        var extreme=profiles[0] with { WorldSize=4096000,Continents=8,ContinentRadius=145000,IslandRadius=10000,Islands=128,RangesPerContinent=6,MountainHeight=6000,MountainWidth=500 };
        extreme.Validate();
        foreach(double width in new[]{500d,extreme.ContinentRadius*.5})
        {
            var world=new MacroWorldGenerator(MacroWorldManifest.Create(42,extreme with { MountainWidth=width },algorithm:4));
            Check(world.ExperimentalMountains!.SegmentCount<=8*6*32,"maximum supported profile budget");
            foreach(var range in world.Ranges)foreach(var p in range.Points)
            {
                var s=world.Sample(p.X,p.Z); Check(double.IsFinite(s.Elevation) && s.MountainInfluence is >=0 and <=1,"extreme-width finite ridge");
            }
        }
        var saved=MacroWorldManifest.Create(-9,profiles[3],"mountain-experiment",12345,-54321,4);
        string folder=Path.Combine(Path.GetTempPath(),"thornwake-v4-test-"+Guid.NewGuid().ToString("N")); string file=Path.Combine(folder,"manifest.json");
        try
        {
            MacroManifestStore.Create(file,saved);var loaded=MacroManifestStore.Load(file);
            Check(loaded==saved && loaded.Algorithm==4,"complete profile/seed/origin/version save roundtrip");
            var before=new MacroWorldGenerator(saved); var after=new MacroWorldGenerator(loaded);
            foreach(var f in before.LandFeatures)Check(before.Sample(f.Center.X,f.Center.Z)==after.Sample(f.Center.X,f.Center.Z),"save/reload terrain identity");
            try{MacroManifestStore.Create(file,saved with { Algorithm=3 });throw new Exception("version overwritten");}
            catch(IOException){Check(MacroManifestStore.Load(file)==saved,"no cross-version overwrite");}
            Reject(()=>(saved with { Algorithm=5 }).Validate()); Reject(()=>MacroWorldManifest.Create(1,profiles[0],algorithm:2));
        }
        finally { if(Directory.Exists(folder)){foreach(var path in Directory.GetFiles(folder))File.Delete(path);Directory.Delete(folder);} }
        Console.WriteLine($"MOUNTAIN_V4_PASS: {checks} checks; versions, reconstruction, base geography, boundaries, parallel queries, budgets, immutable saves");
        return checks;
    }
}

