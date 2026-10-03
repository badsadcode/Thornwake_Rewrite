using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Thornwake.Core;

internal static class HydrologySpillAttributionTests
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
    private static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    private static readonly string DirectoryName="artifacts/hydrology-spill-attribution";
    public static int Run()
    {
        int checks=HydrologyTopologyTests.Run(verify:false,includeWorlds:false,algorithm:3,compareSpill:true);
        void Check(bool ok,string why){if(!ok)throw new Exception("SPILL_ATTRIBUTION: "+why);checks++;}
        Check(SpillAttributionCollector.Classify(3,7)==SpillFrontierBinding.PotentiallyBinding,"lower route can beat receiver");
        Check(SpillAttributionCollector.Classify(9,7)==SpillFrontierBinding.NonBindingGivenCurrentBounds,"strictly higher proved bound");
        Check(SpillAttributionCollector.Classify(7,7)==SpillFrontierBinding.PotentiallyBinding,"equal elevation may change receiver identity");
        Check(SpillAttributionCollector.Classify(3,null)==SpillFrontierBinding.UnknownBinding,"no supported candidate");
        Check(SpillAttributionCollector.Classify(null,7)==SpillFrontierBinding.UnknownBinding,"no valid bound");
        Check(SpillAttributionCollector.Classify(9,7,false)==SpillFrontierBinding.UnknownBinding,"discarded topology cannot certify nonbinding");
        var key=new HydrologyCell(0,0,8);var source=new SpillCellEvidence(key,0,0,8,0,0,false);
        var model=new SpillAttributionCollector(1,3,CancellationToken.None);model.Begin(1);model.Spill(source,10);
        model.Candidate(new("receiver",new(0,0),new(4,4),7),7,key,new Dictionary<HydrologyCell,HydrologyCell>(),k=>source);
        model.Step(9,1);model.Frontier(source,1,"patch-budget",9,true,false,_=>key);
        Check(model.Finish().Passes[0].Frontiers[0].EndOfPassBinding==SpillFrontierBinding.NonBindingGivenCurrentBounds,"higher side frontier with no competing uncertainty");
        model.Step(4,2);model.Frontier(source with {Unresolved=true,Low=20,High=40},0,"resolution-limit",4,true,false,_=>key);
        var snapshot=model.Finish();
        Check(snapshot.Passes[0].Frontiers.All(f=>f.EndOfPassBinding==SpillFrontierBinding.PotentiallyBinding),"later low uncertainty weakens earlier side-branch proof");
        Check(snapshot.Passes[0].Frontiers[1].ConservativeLowerBound==4,"unresolved sample minima never dismiss a lower opening");
        model.Restart();Check(model.Finish().Passes[0].Frontiers.All(f=>f.EndOfPassBinding==SpillFrontierBinding.UnknownBinding),"restart invalidates proof");
        var unresolved=new SpillAttributionCollector(1,3,CancellationToken.None);unresolved.Begin(1);unresolved.Spill(source,1);
        unresolved.Candidate(new("uncertain",new(0,0),new(4,4),7),7,key,new Dictionary<HydrologyCell,HydrologyCell>(),k=>source with {Unresolved=true});
        Check(unresolved.Finish().Passes[0].BestCandidate!=null && unresolved.Finish().Passes[0].BestResolvedCandidate==null,"uncertain candidate path is not supported upper bound");
        Check(unresolved.Finish().Passes[0].BestCandidate!.FirstUnresolvedPathCell?.Unresolved==true,"candidate-path uncertainty located");
        model.Begin(2);model.Frontier(source,1,"domain-boundary",0,true,false,_=>key);
        Check(model.Finish().Passes.Last().BestCandidate==null && model.Finish().Passes.Last().Frontiers[0].Binding==SpillFrontierBinding.UnknownBinding,"new pass does not inherit obsolete candidate");
        var worlds=JsonSerializer.Deserialize<AcceptedWorldTests.TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
        var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(worlds.Cases.Single(c=>c.Name=="mountainous/42").World));
        var recipe=new HydrologyTopologyRecipe(1,3,new(1,1,2,regional.Recipe),4,.03125,1,.01,new(24,200000,100000,200000,8192,20000));
        double Terrain(MacroPoint p)=>1+40*Math.Max(0,Math.Abs(p.Z-6)-1)+40*Math.Max(0,Math.Max(5-p.X,p.X-18))+3*Math.Max(0,1-Math.Abs(p.X-16));
        var domain=new ReconciliationDomain("spill-attribution","basin",3,0,0,4,5,3,4,Terrain,
            patch=>patch==9?new[]{new ReconciliationExit("receiver",new(9,5),new(17.5,5.5),7)}:Array.Empty<ReconciliationExit>(),
            (a,b)=>Array.Empty<DrainagePort>(),patch=>Array.Empty<DrainagePort>());
        var normal=AdaptiveBasinTopology.ProfileSpill(domain,recipe,6,5);
        var pass=normal.Attribution.Passes.Last();
        Check(pass.BestResolvedCandidate?.ReceiverLevel==7 && pass.BestResolvedCandidate.Elevation==7,"actual sampled receiver route inspected");
        Check(normal.Attribution.Complete && Hash(normal.Result)==Hash(AdaptiveBasinTopology.Run(domain,recipe,6,5)),"normal complete result unchanged");
        var nodeRecipe=recipe with {Limits=recipe.Limits with {NodeVisits=normal.Result.ComponentCells.Count+1}};
        var node=AdaptiveBasinTopology.ProfileSpill(domain,nodeRecipe,6,5);
        Check(node.Result.ComponentStop==TopologyStop.SupportedAtSampleSpacing && node.Attribution.Passes.Last().Frontiers.All(f=>f.Reason=="node-budget"),"isolated minimax visit exhaustion");
        Check(node.Attribution.Passes.Sum(p=>p.WetVisits+p.SpillVisits+p.BalanceVisits)==node.Result.Metrics.NodeVisits,"all charged visits accounted");
        var patchRecipe=recipe with {Limits=recipe.Limits with {Patches=1}};
        var patch=AdaptiveBasinTopology.ProfileSpill(domain,patchRecipe,6,5);
        Check(patch.Attribution.Passes.SelectMany(p=>p.Frontiers).Any(f=>f.Reason=="patch-budget" && f.Phase=="component" && f.EndOfPassBinding==SpillFrontierBinding.UnknownBinding),"component patch uncertainty lacks canonical spill proof");
        var limitDomain=domain with {Sample=p=>1,Exits=p=>Array.Empty<ReconciliationExit>()};
        var limitControl=AdaptiveBasinTopology.Run(limitDomain,patchRecipe,6,5);
        foreach(var limits in new[]{(0,2000000L),(65536,0L),(1,2000000L),(65536,2L)})
        {
            var limited=AdaptiveBasinTopology.ProfileSpill(limitDomain,patchRecipe,6,5,recordLimit:limits.Item1,operationLimit:limits.Item2);
            Check(!limited.Attribution.Complete && limited.Attribution.Truncation!=null,"diagnostic exhaustion explicit");
            Check(Hash(limited.Result)==Hash(limitControl),"diagnostic exhaustion preserves solver result");
        }
        var capacity=AdaptiveBasinTopology.ProfileSpill(limitDomain,patchRecipe with {Limits=patchRecipe.Limits with {FrontierEntries=1}},6,5);
        Check(capacity.Attribution.Passes.Last().Frontiers.Count(f=>!f.RetainedBySolver)==capacity.Result.Metrics.FrontierEntriesOmitted && capacity.Result.Metrics.FrontierEntriesOmitted>0,"solver frontier omissions independently observed");
        Check(capacity.Attribution.Passes.Last().Frontiers.Any(f=>f.Reason=="domain-boundary" || f.Reason=="patch-budget"),"explicit boundary reason retained");
        Check(Hash(normal)==Hash(AdaptiveBasinTopology.ProfileSpill(domain,recipe,6,5)),"repeated observation deterministic");
        var parallel=Task.WhenAll(Enumerable.Range(0,3).Select(_=>Task.Run(()=>Hash(AdaptiveBasinTopology.ProfileSpill(domain,recipe,6,5))))).GetAwaiter().GetResult();
        Check(parallel.All(h=>h==Hash(normal)),"parallel profiles independent");
        foreach(string moment in new[]{"frontier","candidate","path"})
        {
            using var cancel=new CancellationTokenSource();bool reached=false,cancelled=false;
            try{AdaptiveBasinTopology.ProfileSpill(domain,moment=="frontier"?patchRecipe:recipe,6,5,cancel.Token,observer:m=>{if(m==moment){reached=true;cancel.Cancel();}});}
            catch(OperationCanceledException){cancelled=true;}
            Check(reached && cancelled,"cancellation during "+moment);
        }
        Directory.CreateDirectory(DirectoryName);File.WriteAllText(DirectoryName+"/analytic.json",JsonSerializer.Serialize(new{normal,node,patch},Json));
        Console.WriteLine($"SPILL_ATTRIBUTION_PASS: {checks} checks");return checks;
    }
    public static object Summary(ProfiledHydrologySpill p)
    {
        var a=p.Attribution;var final=a.Passes.Last();var events=final.Frontiers;
        // Counts align with the solver's distinct final frontier entries, not repeated encounters.
        var f=events.Where(x=>x.RetainedBySolver).DistinctBy(x=>(x.Cell?.Cell,x.Patch,x.Reason,x.SolverLowerBound)).ToArray();
        object Counts(SpillFrontierBinding binding)=>f.Where(x=>x.EndOfPassBinding==binding).GroupBy(x=>x.Reason).OrderBy(g=>g.Key,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Count());
        var reasons=f.Select(x=>x.Reason).Distinct().Order().ToArray();
        return new{p.Result.ComponentStop,p.Result.SpillStop,TotalVisits=p.Result.Metrics.NodeVisits,WetVisits=a.Passes.Sum(x=>x.WetVisits),
            SpillVisits=a.Passes.Sum(x=>x.SpillVisits),BalanceVisits=a.Passes.Sum(x=>x.BalanceVisits),Passes=a.Passes.Count,
            ComponentRestarts=p.Graph.Passes.Count(x=>x.RestartPhase=="component"),SpillRestarts=p.Graph.Passes.Count(x=>x.RestartPhase=="spill"),
            a.FirstComponentCompletedAtVisit,VisitsAfterFirstSupportedComponent=a.FirstComponentCompletedAtVisit is int v?p.Result.Metrics.NodeVisits-v:(int?)null,
            final.BestCandidate,final.BestResolvedCandidate,final.LastMinimaxCost,final.LastHeapCount,
            LowestSolverFrontierBound=f.Length==0?(double?)null:f.Min(x=>x.SolverLowerBound),
            LowestConservativeBound=f.Select(x=>x.ConservativeLowerBound).Min(),
            PotentiallyBinding=Counts(SpillFrontierBinding.PotentiallyBinding),NonBinding=Counts(SpillFrontierBinding.NonBindingGivenCurrentBounds),UnknownBinding=Counts(SpillFrontierBinding.UnknownBinding),
            ObservedReasons=reasons,MissingAdjacentPatches=f.Where(x=>x.Reason=="patch-budget" && x.Patch!=null).Select(x=>x.Patch!.Value).Distinct().Order().ToArray(),
            FrontierObservations=events.Count,DistinctRetainedFrontiers=f.Length,
            OmittedFrontierEvents=events.Count(x=>!x.RetainedBySolver),p.Result.Metrics.FrontierEntriesOmitted,
            FrontierCapacityBinding=p.Result.Metrics.FrontierEntriesOmitted>0?SpillFrontierBinding.UnknownBinding:(SpillFrontierBinding?)null};
    }
    public static int Measure()
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("SPILL_MATRIX: "+why);checks++;}
        var rows=new List<object>();var worlds=JsonSerializer.Deserialize<AcceptedWorldTests.TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
        using var frozen=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/hydrology-work-coverage-v1.json")));
        using var previous=JsonDocument.Parse(File.ReadAllText("docs/experiments/hydrology-locality-assessment.json"));
        foreach(var (world,size) in new[]{("vast-world/7319/far","small"),("mountainous/42","small"),("vast-world/7319/far","medium"),
            ("mountainous/42","large"),("mountainous/42/far","large"),("vast-world/7319/far","large")})
        {
            var expected=frozen.RootElement.EnumerateArray().Single(r=>r.GetProperty("World").GetString()==world && r.GetProperty("Size").GetString()==size && r.GetProperty("Algorithm").GetInt32()==3);
            int patch=expected.GetProperty("Patch").GetInt32();
            var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(worlds.Cases.Single(c=>c.Name==world).World));
            var recipe=new HydrologyTopologyRecipe(1,3,new(1,1,2,regional.Recipe),3,2,64,.25,new(32,2000000,800000,2000000,16384,180000));
            var d=HydrologyReconciler.Create(regional).CreateDomain(patch,CancellationToken.None);
            var clock=Stopwatch.StartNew();var plain=AdaptiveBasinTopology.Run(d,recipe,patch,null);clock.Stop();double plainMs=clock.Elapsed.TotalMilliseconds;
            d=HydrologyReconciler.Create(regional).CreateDomain(patch,CancellationToken.None);clock.Restart();
            var profile=AdaptiveBasinTopology.ProfileSpill(d,recipe,patch,null);clock.Stop();
            Check(Hash(plain)==expected.GetProperty("Fingerprint").GetString() && Hash(profile.Result)==Hash(plain),"complete frozen result equality");
            Check(profile.Attribution.Complete,"bounded observation complete");
            var old=previous.RootElement.EnumerateArray().Single(r=>r.GetProperty("World").GetString()==world && r.GetProperty("Size").GetString()==size);
            Check(Hash(profile.Graph)==Hash(old.GetProperty("Graph")),"graph accounting unchanged");
            Check(profile.Attribution.Passes.Sum(p=>p.WetVisits+p.SpillVisits+p.BalanceVisits)==plain.Metrics.NodeVisits,"full visit partition");
            Check(profile.Attribution.Passes.Last().Frontiers.Count(f=>!f.RetainedBySolver)==plain.Metrics.FrontierEntriesOmitted,"frontier-capacity observations match solver");
            var summary=Summary(profile);
            rows.Add(new{World=world,Size=size,Patch=patch,PlainMilliseconds=plainMs,ProfileMilliseconds=clock.Elapsed.TotalMilliseconds,Fingerprint=Hash(plain),Summary=summary,profile.Attribution,profile.Graph});
            Directory.CreateDirectory(DirectoryName);File.WriteAllText(DirectoryName+"/matrix.json",JsonSerializer.Serialize(rows,Json));
            Console.WriteLine($"SPILL_MATRIX {world}/{size}: "+JsonSerializer.Serialize(summary));
        }
        Console.WriteLine($"SPILL_MATRIX_PASS: {checks} checks; 6 independent controls");return checks;
    }
}
