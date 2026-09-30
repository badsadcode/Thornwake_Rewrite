using System.Numerics;
using Thornwake.Core;

internal static class MacroChunkTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool ok,string why) { if(!ok) throw new Exception("MACRO CHUNKS: "+why); checks++; }
        var profile=MacroProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Content","world_profiles.json")).Single(p=>p.Id=="vast-world");
        var world=new MacroWorldGenerator(MacroWorldManifest.Create(7319,profile,algorithm:3));
        Check((long)(profile.WorldSize/WorldRegion.Size)*(long)(profile.WorldSize/WorldRegion.Size)==16_384_000_000L,"vast preset addresses billions of potential chunks without constructing them");
        foreach(var land in world.LandFeatures.Take(profile.Continents))
        {
            var region=WorldRegion.At(land.Center.X,land.Center.Z);
            var a=MacroRegionSampler.Build(world,region); var b=MacroRegionSampler.Build(world,new(region.X+1,region.Z));
            var again=MacroRegionSampler.Build(world,region);
            Check(a.Elevations.SequenceEqual(again.Elevations) && a.Normals.SequenceEqual(again.Normals),"unload/recreate independent of request order");
            for(int z=0;z<a.Side;z++)
            {
                Check(a.Elevations[z*a.Side+a.Side-1]==b.Elevations[z*b.Side],"exact shared heights");
                Check(a.Normals[z*a.Side+a.Side-1]==b.Normals[z*b.Side],"exact shared normals");
            }
            var origin=region.Origin(a.Elevations[0]);
            Check(a.Vertex(0,0,origin)==Vector3.Zero && a.Vertex(a.Side-1,a.Side-1,origin).X==WorldRegion.Size,"local engine coordinates after double subtraction");
            Check(a.SampleBufferBytes==5780,"bounded one-metre chunk data");
        }
        var resident=new Dictionary<WorldRegion,MacroTerrainChunk>(); var observer=world.LandFeatures[0].Owner;
        for(int frame=0;frame<70;frame++)
        {
            if(frame==35) observer=world.LandFeatures[profile.Continents-1].Owner;
            var plan=RegionStreaming.Plan(resident.Keys.ToArray(),new[]{observer},3,4,2);
            foreach(var region in plan.Unload) resident.Remove(region);
            foreach(var region in plan.Load) resident[region]=MacroRegionSampler.Build(world,region,4);
            Check(plan.Load.Count<=2 && resident.Count<=81 && plan.DesiredCount==49,"fixed local work/residency budget after a continent teleport");
            if(frame==34 || frame==69) Check(resident.Count==49,"local neighbourhood fills without loading intervening world");
        }
        Check(resident.Values.Sum(c=>c.SampleBufferBytes)==49*500,"sample memory depends on active chunks, not world size");
        Console.WriteLine($"MACRO_CHUNKS_PASS: 2048km world, bounded residency, continental teleport, exact seams and reconstruction ({checks} checks)");
        return checks;
    }
}
