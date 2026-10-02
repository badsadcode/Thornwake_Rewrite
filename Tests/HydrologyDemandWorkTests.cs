using System.Security.Cryptography;
using System.Text.Json;
using Thornwake.Core;

internal static class HydrologyDemandWorkTests
{
    private static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value)));
    public static int Run()
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("DEMAND_WORK: "+why);checks++;}
        var worlds=JsonSerializer.Deserialize<AcceptedWorldTests.TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
        var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(worlds.Cases.Single(c=>c.Name=="mountainous/42").World));
        var r=new HydrologyTopologyRecipe(1,3,new(1,1,2,regional.Recipe),3,.03125,1,.01,new(16,100000,100000,200000,8192,10000));
        var d=new ReconciliationDomain("small-query","isolated",3,0,0,16,3,3,16,
            p=>1+40*Math.Max(0,Math.Abs(p.X-21)-1)+40*Math.Max(0,Math.Abs(p.Z-21)-1),
            patch=>patch==4?new[]{new ReconciliationExit("receiver",new(4,68),new(20.5,20.5),6)}:Array.Empty<ReconciliationExit>(),
            (a,b)=>Array.Empty<DrainagePort>(),patch=>Array.Empty<DrainagePort>());
        var eager=AdaptiveBasinTopology.Profile(d,r with {Algorithm=2},4,68);
        var lazy=AdaptiveBasinTopology.Profile(d,r,4,68);
        Check(lazy.Result.SpillStop==TopologyStop.SupportedAtSampleSpacing && lazy.Result.SpillProposal!.RequiredElevation==6,"sampled enclosing rim justifies not exploring distant dry interiors");
        Check(eager.Result.ComponentCells.SequenceEqual(lazy.Result.ComponentCells) && eager.Result.SpillProposal!.Cells.SequenceEqual(lazy.Result.SpillProposal!.Cells),"identical supported topology and minimax route");
        Check(lazy.Result.Metrics.TerrainSamples*5<eager.Result.Metrics.TerrainSamples,"materially fewer samples for a localized component");
        Check(lazy.Result.Metrics.CompletedPatches==0 && eager.Result.Metrics.CompletedPatches==1,"partial patch metadata is not a dry/complete terrain claim");
        foreach(var algorithm in new[]{2,3})
        {
            var recipe=r with {Algorithm=algorithm};var profiled=AdaptiveBasinTopology.Profile(d,recipe,4,68);
            Check(Hash(profiled.Result)==Hash(AdaptiveBasinTopology.Run(d,recipe,4,68)),"optional work attribution does not change result bytes");
            Check(profiled.Work.WetGraphVisits+profiled.Work.SpillGraphVisits==profiled.Result.Metrics.NodeVisits,"every graph visit attributed");
            Check(profiled.Work.SamplesInComponentRoots+profiled.Work.SamplesOutsideVisitedRoots<=profiled.Result.Metrics.TerrainSamples,"disjoint selected/outside attribution does not double charge");
        }
        var blocked=AdaptiveBasinTopology.Run(d,r with {Limits=r.Limits with {Samples=12}},4,68);
        Check(blocked.ComponentStop==TopologyStop.WorkBudget && blocked.FinalComponentId==null,"unavailable neighbour root remains unknown, never a dry wall");
        var token=JsonSerializer.Deserialize<HydrologyTopologyContinuation>(JsonSerializer.Serialize(blocked.Continuation))!.Expand(r.Limits);
        Check(Hash(AdaptiveBasinTopology.Run(d,token.Recipe,token.SeedPatch,token.SeedQuad))==Hash(lazy.Result),"continuation reconstructs cells omitted under the old limit");
        var parallel=Task.WhenAll(Enumerable.Range(0,3).Select(_=>Task.Run(()=>Hash(AdaptiveBasinTopology.Profile(d,r,4,68))))).GetAwaiter().GetResult();
        Check(parallel.All(h=>h==Hash(lazy)),"profile and topology deterministic under parallel execution");
        var defaultQuery=AdaptiveBasinTopology.Run(d,r,4,null);
        Check(defaultQuery.FinalComponentId==lazy.Result.FinalComponentId && defaultQuery.SpillProposal!.RequiredElevation==6,"default query still establishes canonical seed across its whole patch");
        double Terrain(MacroPoint p)
        {
            double bed=1+40*Math.Max(0,Math.Abs(p.Z-6)-1)+40*Math.Max(0,Math.Max(5-p.X,p.X-18))+3*Math.Max(0,1-Math.Abs(p.X-16));
            foreach(double face in new[]{8d,12d})bed+=19*Math.Max(0,1-Math.Abs(p.X-face)/.25)*Math.Max(0,1-Math.Abs(p.Z-5.25)/.125)+
                5*Math.Max(0,1-Math.Abs(p.X-face-.5)/.5)*Math.Max(0,1-Math.Abs(p.Z-5.5)/.5);
            return bed;
        }
        var twoFaces=new ReconciliationDomain("two-faces","basin",3,0,0,4,5,3,4,Terrain,
            patch=>patch==9?new[]{new ReconciliationExit("downstream",new(9,5),new(17.5,5.5),0)}:Array.Empty<ReconciliationExit>(),
            (a,b)=>Array.Empty<DrainagePort>(),patch=>Array.Empty<DrainagePort>());
        var multi=AdaptiveBasinTopology.Profile(twoFaces,r,6,5);
        Check(multi.Result.ComponentStop==TopologyStop.SupportedAtSampleSpacing && multi.Result.SpillStop==TopologyStop.SupportedAtSampleSpacing && multi.Result.SpillProposal!.RequiredElevation==4,"separated shared-face corrections converge without stale graph certification");
        Check(multi.Work.SharedFaceVisits>0 && multi.Result.Metrics.Passes>=2,"bounded face balance executes before a fresh stable pass");
        var dirty=AdaptiveBasinTopology.Run(twoFaces,r with {Limits=r.Limits with {Passes=1}},6,5);
        Check(dirty.ComponentStop==TopologyStop.WorkBudget && dirty.FinalComponentId==null && dirty.SpillProposal==null,"an unfinished correction pass is never returned as a certified graph");
        var continued=dirty.Continuation!.Expand(r.Limits);
        Check(Hash(AdaptiveBasinTopology.Run(twoFaces,continued.Recipe,continued.SeedPatch,continued.SeedQuad))==Hash(multi.Result),"batched corrections reproduce under expanded continuation");
        var other=AdaptiveBasinTopology.Run(twoFaces,r,8,5);
        Check(other.FinalComponentId==multi.Result.FinalComponentId && Hash(other.SpillProposal)==Hash(multi.Result.SpillProposal),"batching multiple faces preserves caller-independent topology");
        Console.WriteLine($"HYDROLOGY_DEMAND_WORK_PASS: {checks} checks; isolated samples {eager.Result.Metrics.TerrainSamples} -> {lazy.Result.Metrics.TerrainSamples}");return checks;
    }
}
