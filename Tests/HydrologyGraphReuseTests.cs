using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Thornwake.Core;

internal static class HydrologyGraphReuseTests
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
    private static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    public static int Run()
    {
        int checks=HydrologyTopologyTests.Run(verify:false,includeWorlds:false,algorithm:3,compareReuse:true);
        void Check(bool ok,string why){if(!ok)throw new Exception("GRAPH_REUSE: "+why);checks++;}
        var worlds=JsonSerializer.Deserialize<AcceptedWorldTests.TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
        var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(worlds.Cases.Single(c=>c.Name=="mountainous/42").World));
        var recipe=new HydrologyTopologyRecipe(1,3,new(1,1,2,regional.Recipe),3,.03125,1,.01,new(16,100000,100000,200000,8192,10000));
        ReconciliationDomain Domain(double[] faces,bool opening=false,double receiver=0)
        {
            double Terrain(MacroPoint p)
            {
                double sill=3*Math.Max(0,1-Math.Abs(p.X-16));
                if(opening)sill*=Math.Clamp((Math.Abs(p.Z-5.5)-.25)/.25,0,1);
                double bed=1+40*Math.Max(0,Math.Abs(p.Z-6)-1)+40*Math.Max(0,Math.Max(5-p.X,p.X-18))+sill;
                foreach(double face in faces)bed+=19*Math.Max(0,1-Math.Abs(p.X-face)/.25)*Math.Max(0,1-Math.Abs(p.Z-5.25)/.125)+
                    5*Math.Max(0,1-Math.Abs(p.X-face-.5)/.5)*Math.Max(0,1-Math.Abs(p.Z-5.5)/.5);
                return bed;
            }
            return new("graph-reuse","basin",3,0,0,4,5,3,4,Terrain,
                patch=>patch==9?new[]{new ReconciliationExit("receiver",new(9,5),new(17.5,5.5),receiver)}:Array.Empty<ReconciliationExit>(),
                (a,b)=>Array.Empty<DrainagePort>(),patch=>Array.Empty<DrainagePort>());
        }
        ProfiledHydrologyGraph Compare(ReconciliationDomain d,HydrologyTopologyRecipe? r=null,int patch=6)
        {
            r??=recipe;
            var reuse=AdaptiveBasinTopology.ProfileGraph(d,r,patch,5,reuse:true);
            var raw=AdaptiveBasinTopology.RunGraphExperiment(d,r,patch,5,reuse:false);
            Check(Hash(raw)==Hash(reuse.Result),"identical complete result after generation invalidation and full chain reconstruction");
            Check(reuse.Graph.PeakCachedCells<=Math.Min(131072,r.Limits.Cells) && reuse.Graph.PeakCachedEdges<=1048576,"bounded local dependencies");
            return reuse;
        }
        var d=Domain(new[]{8d,12d,16d});var multi=Compare(d);
        Check(multi.Graph.Passes.Count(p=>p.RestartPhase!=null)>1,"multiple corrections require fresh graph passes");
        Check(multi.Graph.Passes.Sum(p=>p.ReusedExpansions)>0 && multi.Graph.Passes.Sum(p=>p.InvalidatedExpansions)>0,"reuse and invalidation both exercised");
        Check(multi.Graph.Passes.Sum(p=>p.InvalidatedPreviouslyReusedExpansions)>0,"previously reused state invalidates on later correction");
        Check(multi.Graph.Passes.Max(p=>p.MaximumDistanceMetres)>1,"unchanged remote branch expansions are reusable");
        var other=Compare(d,patch:8);
        Check(other.Result.FinalComponentId==multi.Result.FinalComponentId && Hash(other.Result.SpillProposal)==Hash(multi.Result.SpillProposal),"alternate tributary preserves canonical result");
        var spill=Compare(Domain(new[]{16d}));
        Check(spill.Graph.Passes.Any(p=>p.RestartPhase=="spill") && spill.Graph.Passes.Sum(p=>p.CorrectedSpillParents)>0,"corrected minimax parent chain is discarded");
        var opening=Compare(Domain(new[]{8d,12d},opening:true));
        Check(opening.Result.SpillProposal!.RequiredElevation==1,"narrow lower opening remains reachable with reuse");
        var receiver=Compare(Domain(new[]{8d,12d},opening:true,receiver:7));
        Check(receiver.Result.SpillProposal!.RequiredElevation==7,"receiver level remains binding");
        foreach(var limits in new[]{recipe.Limits with{Passes=1},recipe.Limits with{Samples=30},recipe.Limits with{Splits=0},
            recipe.Limits with{NodeVisits=1},recipe.Limits with{Patches=1,FrontierEntries=1}})
        {
            var limited=Compare(d,recipe with{Limits=limits});
            Check(limited.Result.FinalComponentId==null && limited.Result.Continuation!=null,"work exhaustion never certifies cached connectivity");
            var token=JsonSerializer.Deserialize<HydrologyTopologyContinuation>(JsonSerializer.Serialize(limited.Result.Continuation))!.Expand(recipe.Limits);
            Check(Hash(AdaptiveBasinTopology.RunGraphExperiment(d,token.Recipe,token.SeedPatch,token.SeedQuad,true))==Hash(multi.Result),"expanded replay equals one-shot, cache never persisted");
        }
        foreach(string moment in new[]{"reuse","invalidate"})
        {
            using var cancel=new CancellationTokenSource();bool observed=false,cancelled=false;
            try{AdaptiveBasinTopology.ProfileGraph(d,recipe,6,5,true,cancel.Token,eventName=>{if(eventName==moment){observed=true;cancel.Cancel();}});}
            catch(OperationCanceledException){cancelled=true;}
            Check(observed && cancelled,"prompt cancellation during "+moment);
        }
        var parallel=Task.WhenAll(Enumerable.Range(0,3).Select(_=>Task.Run(()=>Hash(AdaptiveBasinTopology.ProfileGraph(d,recipe,6,5,true))))).GetAwaiter().GetResult();
        Check(parallel.All(h=>h==Hash(multi)),"parallel query-local generations and profiles deterministic");
        Directory.CreateDirectory("artifacts/hydrology-graph");
        File.WriteAllText("artifacts/hydrology-graph/analytic.json",JsonSerializer.Serialize(new{multi,spill,opening},Json));
        Console.WriteLine($"GRAPH_REUSE_PASS: {checks} checks; reused {multi.Graph.Passes.Sum(p=>p.ReusedExpansions)}, invalidated {multi.Graph.Passes.Sum(p=>p.InvalidatedExpansions)}");return checks;
    }
    public static int Measure(bool baselineOnly=false)
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("GRAPH_REUSE: "+why);checks++;}
        var rows=new List<object>();
        var worlds=JsonSerializer.Deserialize<AcceptedWorldTests.TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
        using var frozen=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/hydrology-work-coverage-v1.json")));
        foreach(var group in new[]{(World:"mountainous/42",Sizes:new[]{"large","small"}),
            (World:"mountainous/42/far",Sizes:new[]{"large"}),(World:"vast-world/7319/far",Sizes:new[]{"small","large","medium"})})
        {
            var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(worlds.Cases.Single(c=>c.Name==group.World).World));
            foreach(string size in group.Sizes)
            {
                var expected=frozen.RootElement.EnumerateArray().Single(r=>r.GetProperty("World").GetString()==group.World && r.GetProperty("Size").GetString()==size && r.GetProperty("Algorithm").GetInt32()==3);
                int patch=expected.GetProperty("Patch").GetInt32();
                var recipe=new HydrologyTopologyRecipe(1,3,new(1,1,2,regional.Recipe),3,2,64,.25,new(32,2000000,800000,2000000,16384,180000));
                foreach(bool reuse in baselineOnly?new[]{false}:new[]{false,true})
                {
                    var domain=HydrologyReconciler.Create(regional).CreateDomain(patch,CancellationToken.None);
                    var profile=AdaptiveBasinTopology.ProfileGraph(domain,recipe,patch,null,reuse);
                    Check(Hash(profile.Result)==expected.GetProperty("Fingerprint").GetString(),"profile preserves frozen complete result");
                    // Benchmark without the diagnostic shadow cache/visit ledger. Fresh domain: no port cache advantage.
                    domain=HydrologyReconciler.Create(regional).CreateDomain(patch,CancellationToken.None);
                    var watch=Stopwatch.StartNew();var result=AdaptiveBasinTopology.RunGraphExperiment(domain,recipe,patch,null,reuse);watch.Stop();
                    Check(Hash(result)==Hash(profile.Result),"instrumentation and reuse preserve all result bytes");
                    Check(profile.Graph.PeakCachedCells<=131072 && profile.Graph.PeakCachedEdges<=1048576,"cache bounds");
                    Check(profile.Graph.Passes.Sum(p=>p.WetVisits+p.SpillVisits)<=result.Metrics.NodeVisits,"balancing excluded from graph-only visit ledger");
                    Check(profile.Graph.Passes.Sum(p=>p.ComputedExpansions+p.ReusedExpansions)==profile.Graph.Passes.Sum(p=>p.WetVisits+p.SpillVisits),"every visited graph expansion accounted, including interrupted walks");
                    rows.Add(new{group.World,Size=size,Patch=patch,Reuse=reuse,Milliseconds=watch.Elapsed.TotalMilliseconds,result.Metrics,
                        result.ComponentStop,result.SpillStop,ComponentCells=result.ComponentCells.Count,
                        Reasons=result.Frontier.Select(f=>f.Reason).Distinct().Order().ToArray(),Frontiers=result.Frontier.GroupBy(f=>f.Reason).OrderBy(g=>g.Key).ToDictionary(g=>g.Key,g=>g.Count()),profile.Graph});
                    Console.WriteLine($"GRAPH {group.World}/{size} reuse={reuse}: {watch.Elapsed.TotalMilliseconds:F0}ms, visits {result.Metrics.NodeVisits}, " +
                        $"repeated {profile.Graph.Passes.Sum(p=>p.RepeatedWetVisits+p.RepeatedSpillVisits)}, unchanged expansions {profile.Graph.Passes.Sum(p=>p.UnchangedRepeatExpansions)}, " +
                        $"reused {profile.Graph.Passes.Sum(p=>p.ReusedExpansions)}, invalidated {profile.Graph.Passes.Sum(p=>p.InvalidatedExpansions)}");
                }
            }
        }
        Directory.CreateDirectory("artifacts/hydrology-graph");
        File.WriteAllText($"artifacts/hydrology-graph/{(baselineOnly?"baseline":"comparison")}.json",JsonSerializer.Serialize(rows,Json));
        Console.WriteLine($"GRAPH_MEASUREMENT_PASS: {checks} checks; {rows.Count} queries");return checks;
    }
}
