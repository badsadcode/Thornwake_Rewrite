using System.Text.Json;
using System.Security.Cryptography;
using Thornwake.Core;

internal static class HydrologyReconciliationTests
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
    private sealed record FrozenCase(string Name,string Fingerprint);
    private sealed record Fixture(int Format,int Algorithm,FrozenCase[] Cases);
    private static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    private static string Serialize<T>(T value)=>JsonSerializer.Serialize(value);
    public static int Run(bool record=false,bool verify=true)
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("RECONCILIATION: "+why);checks++;}
        void Reject(Action work){try{work();}catch(Exception e)when(e is ArgumentException or InvalidDataException or OperationCanceledException){checks++;return;}throw new Exception("RECONCILIATION: invalid input accepted");}
        var terrains=JsonSerializer.Deserialize<AcceptedWorldTests.TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
        var terrain=terrains.Cases.Single(c=>c.Name=="mountainous/42").World;
        var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(terrain));
        var recipe=new HydrologyReconciliationRecipe(1,1,new(1,1,2,regional.Recipe),PatchBudget:24,SampleBudget:500000,NodeBudget:500000,
            AdaptiveSamplingBudget:10000,Adaptive:new(8,.0625,1,.05));
        var cases=new List<FrozenCase>();var reports=new List<object>();
        void Record(string name,HydrologyReconciliationResult value,double ms)
        {
            cases.Add(new(name,Hash(value)));reports.Add(new{name,milliseconds=ms,result=value});
            Check(value.Metrics.PatchesInspected<=recipe.PatchBudget && value.Metrics.TerrainSamples<=recipe.SampleBudget,"bounded patch and sample counts");
            Check(value.Metrics.AdaptiveSamples<=recipe.AdaptiveSamplingBudget,"bounded adaptive work");
        }
        double Bed(double x,double z,bool split=false)
        {
            if(split && Math.Abs(x-10)<.1 && z>=5 && z<=7)return 8;
            if(x>=5 && x<=15 && z>=5 && z<=7)return 1;
            if(x>=15 && x<=18 && z>=5 && z<=6)return Math.Abs(x-16)<.1?4:0;
            return 12;
        }
        ReconciliationDomain Domain(bool split=false,double ox=0,double oz=0,DrainagePort? original=null)
        {
            var target=new ReconciliationExit("declared-downstream",new(9,5),new(ox+17.5,oz+5.5),0);
            return new("analytic:"+recipe.Digest(),"candidate-one",3,ox,oz,4,5,3,4,p=>Bed(p.X-ox,p.Z-oz,split),
                patch=>patch==9?new[]{target}:Array.Empty<ReconciliationExit>(),
                (a,b)=>original.HasValue && Math.Min(original.Value.FromCell,original.Value.ToCell)==a && Math.Max(original.Value.FromCell,original.Value.ToCell)==b?new[]{original.Value}:Array.Empty<DrainagePort>(),
                patch=>original.HasValue && original.Value.FromCell==patch?new[]{original.Value}:Array.Empty<DrainagePort>());
        }
        HydrologyReconciliationResult Solve(string name,ReconciliationDomain domain,int patch=6,int? quad=5,HydrologyReconciliationRecipe? custom=null)
        {
            var selected=custom??recipe;domain=domain with {Identity="analytic:"+selected.Digest()};
            var watch=System.Diagnostics.Stopwatch.StartNew();var result=BasinReconciliationSearch.Run(domain,selected,patch,quad);watch.Stop();
            Record(name,result,watch.Elapsed.TotalMilliseconds);return result;
        }
        var complete=Solve("complete-multi-patch",Domain());
        Check(complete.ComponentStatus==ComponentDiscovery.Complete && complete.Components.Count==3,"complete three-patch lake");
        Check(complete.SpillStatus==SpillResolution.ReconciledAtSampleSpacing && complete.Spill?.RequiredElevation==4,"reachable sill, not lowest face point");
        Check(complete.FinalComponentId!=null && !complete.GridReachesDeclaredOutletAtCandidateStage,"closed at hypothetical stage below sill");
        var other=BasinReconciliationSearch.Run(Domain(),recipe,8,5);
        Check(other.FinalComponentId==complete.FinalComponentId && Serialize(other.Components)==Serialize(complete.Components),"branch/caller anchor independence");
        Check(Serialize(other.Spill)==Serialize(complete.Spill),"canonical spill independent of tributary entry patch");
        var truncated=Solve("truncated-large-component",Domain(),custom:recipe with {PatchBudget=2});
        Check(truncated.ComponentStatus==ComponentDiscovery.TruncatedByBudget && truncated.FinalComponentId==null && truncated.Frontier.Count>0,"truncated is not closed or final identity");
        var left=Solve("disconnected-left",Domain(true));var right=Solve("disconnected-right",Domain(true),8,5);
        Check(left.FinalComponentId!=right.FinalComponentId && left.Components.Count==2 && right.Components.Count==2,"separate components under one coarse basin");
        Check(left.Spill?.RequiredElevation>=8 && right.Spill?.RequiredElevation==4,"unreachable attractive lower sill cannot bypass intervening ridge");
        var crossing=complete.Spill!.Crossings.Last();
        var old=new DrainagePort("original-confirmed",crossing.FirstPatch,crossing.SecondPatch,new(crossing.Point.X,crossing.Point.Z),crossing.Point.BedElevation);
        var confirmed=Solve("original-port-confirmed",Domain(original:old));
        Check(confirmed.Spill!.Crossings.Any(c=>c.Id==old.Id && c.OriginalPortId==old.Id),"confirmed original port retains identity and exact point");
        var wrong=old with { Id="original-wrong",Position=new(old.Position.X,old.Position.Z+1),BedElevation=12 };
        var moved=Solve("outlet-replaced-bilaterally",Domain(original:wrong));
        Check(moved.Spill!.OutletAgreement=="alternative-crossing-proposed" && moved.OriginalOutlets.Contains(wrong),"replacement reports contradiction and retains original proposal");
        Check(Serialize(moved.Spill.Crossings)==Serialize(BasinReconciliationSearch.Run(Domain(original:wrong),recipe,8,5).Spill!.Crossings),"both entry sides obtain canonical replacement ports");
        var far=Solve("far-origin",Domain(ox:8e9,oz:-7e9));
        Check(far.FinalComponentId==complete.FinalComponentId && far.Spill!.RequiredElevation==4,"analytic translation retains component and spill topology");
        foreach(var c in far.Spill!.Crossings)Check(complete.Spill.Crossings.Any(n=>n.FirstPatch==c.FirstPatch && n.SecondPatch==c.SecondPatch && n.Point.X+8e9==c.Point.X && n.Point.Z-7e9==c.Point.Z),"far-origin crossing exact");
        var parallel=Task.WhenAll(Enumerable.Range(0,3).Select(_=>Task.Run(()=>Serialize(BasinReconciliationSearch.Run(Domain(),recipe,6,5))))).GetAwaiter().GetResult();
        Check(parallel.All(s=>s==Serialize(complete)),"parallel queries have no mutable shared state");
        Reject(()=>BasinReconciliationSearch.Run(Domain(),recipe,6,5,new CancellationToken(true)));
        using(var cancel=new CancellationTokenSource())
        {
            int samples=0;var original=Domain();var interrupted=original with {Sample=p=>{if(++samples==11)cancel.Cancel();return original.Sample(p);}};
            Reject(()=>BasinReconciliationSearch.Run(interrupted,recipe,6,5,cancel.Token));Check(samples<=12,"mid-sampling cancellation terminates promptly");
        }
        Check(complete.Connections.Count==2 && complete.Frontier.Count==0,"shared faces are graph edges, not unresolved boundaries");
        foreach(var limited in new[]{recipe with {SampleBudget=1},recipe with {NodeBudget=1},recipe with {FrontierBudget=1,PatchBudget=1},recipe with {AdaptiveSamplingBudget=0}})
        {
            var value=Solve("budget-"+(limited.SampleBudget==1?"samples":limited.NodeBudget==1?"nodes":limited.PatchBudget==1?"frontier":"adaptive"),Domain(),custom:limited);
            Check(value.ComponentStatus==ComponentDiscovery.TruncatedByBudget || value.SpillStatus==SpillResolution.TruncatedByBudget,"every exhausted budget stays unresolved");
            Check(value.Metrics.NodeVisits<=limited.NodeBudget && value.Metrics.TerrainSamples<=limited.SampleBudget && value.Metrics.AdaptiveSamples<=limited.AdaptiveSamplingBudget,"all individual work caps enforced");
        }
        var baseDomain=Domain();
        var narrowDomain=baseDomain with {Sample=p=>p.X>=15 && p.X<=17 && Math.Abs(p.Z-5.5)<.15?0:baseDomain.Sample(p)};
        var narrow=Solve("adaptive-outlet-opening",narrowDomain);
        Check(narrow.Spill!.Crossings.Any(c=>c.FirstPatch==8 && c.SecondPatch==9 && c.Point.BedElevation==0),"new crossing detects sub-grid opening bilaterally");
        Check(narrow.Spill.AdaptiveProfile.Assessment.Compatibility==CorridorCompatibility.SubmergedAtCandidateStage,"finer narrow outlet is open at the hypothetical stage along its sampled path");
        Check(narrow.Spill.ProfileRequiredElevation<narrow.Spill.FixedGraphElevation && narrow.SpillStatus==SpillResolution.Contradictory,"lower opening does not silently certify the obsolete graph spill");
        var thinDomain=baseDomain with {Sample=p=>Math.Abs(p.X-7.5)<.05 && p.Z>=5 && p.Z<=7?20:baseDomain.Sample(p)};
        var thin=Solve("adaptive-wet-component-barrier",thinDomain);
        Check(thin.ComponentStatus==ComponentDiscovery.Contradictory && thin.FinalComponentId==null && thin.SpillStatus==SpillResolution.Contradictory,"new barrier revokes wet-component certification");
        var domainEdge=baseDomain with {Sample=p=>1,Exits=patch=>Array.Empty<ReconciliationExit>()};
        var openBoundary=Solve("open-world-boundary",domainEdge);
        Check(openBoundary.ComponentStatus==ComponentDiscovery.OpenDomainBoundary && openBoundary.SpillStatus==SpillResolution.UnresolvedDomainBoundary,"world bounds are not invented drains or closure proof");
        Check(openBoundary.FinalComponentId==null && openBoundary.Frontier.All(f=>f.Reason=="domain-boundary"),"unresolved external domain explicit");
        var sampledOutlet=baseDomain with {Exits=patch=>patch==7?new[]{new ReconciliationExit("explicit-fed-boundary",new(7,5),new(9.5,5.5),1)}:Array.Empty<ReconciliationExit>()};
        var openOutlet=Solve("explicit-outlet-at-stage",sampledOutlet);
        Check(openOutlet.GridReachesDeclaredOutletAtCandidateStage && openOutlet.ComponentStatus==ComponentDiscovery.Complete,"complete footprint may reach an explicit destination at candidate stage");
        var receiverLevel=baseDomain with {Exits=patch=>patch==9?new[]{new ReconciliationExit("declared-receiver-level",new(9,5),new(17.5,5.5),6)}:Array.Empty<ReconciliationExit>()};
        var receiver=Solve("explicit-receiver-level",receiverLevel);
        Check(receiver.SpillStatus==SpillResolution.ReconciledAtSampleSpacing && receiver.Spill!.RequiredElevation==6 && receiver.Spill.DestinationLevel==6,"receiver water level constrains escape above the terrain bed");
        Check(receiver.Spill!.ProfileRequiredElevation==6 && receiver.Spill.AdaptiveProfile.Assessment.RequiredStage==4,"boundary level is not misclassified as a lower adaptive terrain contradiction");
        var branches=new[]{new HydrologyQuad(6,5),new HydrologyQuad(7,5),new HydrologyQuad(8,5)};
        var ids=branches.Select(q=>BasinReconciliationSearch.Run(Domain(),recipe,q.Patch,q.Index).FinalComponentId).ToArray();
        Check(ids.Distinct().Count()==1 && ids.SequenceEqual(branches.Reverse().Select(q=>BasinReconciliationSearch.Run(Domain(),recipe,q.Patch,q.Index).FinalComponentId)),"three tributary entries share identity independent of caller order");
        var corners=baseDomain with {Width=2,Depth=2,Sample=p=>(p.X>=3&&p.X<=4&&p.Z>=3&&p.Z<=4)||(p.X>=4&&p.X<=5&&p.Z>=4&&p.Z<=5)?0:10,
            Exits=patch=>Array.Empty<ReconciliationExit>()};
        var cornerFirst=Solve("corner-contact-first",corners,0,15);var cornerSecond=Solve("corner-contact-second",corners,3,0);
        Check(cornerFirst.ComponentStatus==ComponentDiscovery.Complete && cornerSecond.ComponentStatus==ComponentDiscovery.Complete && cornerFirst.FinalComponentId!=cornerSecond.FinalComponentId,"corner-only contact never joins across four patch intersection");
        Check(cornerFirst.Connections.Count==0 && cornerSecond.Connections.Count==0,"no false corner graph edge");
        var dryFace=baseDomain with {Sample=p=>Math.Abs(p.X-8)<.1&&p.Z>=5&&p.Z<=7?10:baseDomain.Sample(p)};
        var sealedFirst=Solve("dry-shared-face",dryFace);
        Check(sealedFirst.Components.Count==1 && sealedFirst.Connections.Count==0,"dry shared face seals component despite common parent basin");
        var invalidDomain=baseDomain with {Sample=p=>double.NaN};Reject(()=>BasinReconciliationSearch.Run(invalidDomain,recipe,6,5));
        Reject(()=>HydrologyReconciler.Restore(recipe with {Adaptive=null}));
        var settings=new AdaptiveHydrologySettings(8,.03125,1,.01);
        var anchors=new[]{new CorridorPoint(0,0,8),new CorridorPoint(8,0,0)};
        double Ridge(MacroPoint p)=>Math.Abs(p.X-3)<.15?20:8-p.X;
        var adaptive=AdaptiveHydrologySampler.Inspect(anchors,Ridge,settings,500);
        Check(adaptive.Assessment.Compatibility==CorridorCompatibility.TerrainBarrier && adaptive.Assessment.RequiredStage==20,"adaptive mandatory probes detect narrow ridge missed by endpoint and first midpoint");
        Check(adaptive.Points[0]==anchors[0] && adaptive.Points[^1]==anchors[^1] && adaptive.Samples<=500,"exact anchors and bounded adaptive work");
        var notch=AdaptiveHydrologySampler.Inspect(new[]{new CorridorPoint(0,0,10),new CorridorPoint(8,0,10)},p=>Math.Abs(p.X-3)<.15?0:10,settings,500,5);
        Check(notch.Points.Any(p=>p.BedElevation==0),"adaptive face profile discovers narrow opening missed by fixed endpoints/midpoint");
        var shortBudget=AdaptiveHydrologySampler.Inspect(anchors,Ridge,settings,2);
        Check(shortBudget.Stop==AdaptiveStop.SampleBudget && shortBudget.Samples==2,"adaptive exhaustion explicit");
        Check(Serialize(adaptive)==Serialize(AdaptiveHydrologySampler.Inspect(anchors,Ridge,settings,500)),"adaptive deterministic reconstruction");
        reports.Add(new{name="narrow-ridge",adaptive});reports.Add(new{name="narrow-opening",adaptive=notch});
        foreach(var setting in new[]{recipe with {Format=2},recipe with {PatchBudget=0},recipe with {AdaptiveSamplingBudget=-1},recipe with {Adaptive=new(99)}})Reject(()=>setting.Validate());
        Reject(()=>HydrologyReconciler.Restore(recipe with {InspectionDependency=recipe.InspectionDependency with {Algorithm=2}}));
        Reject(()=>HydrologyReconciler.Restore(recipe with {InspectionDependency=recipe.InspectionDependency with {Regional=regional.Recipe with {Coarse=regional.Recipe.Coarse with {World=terrain with {Algorithm=3}}}}}));
        Reject(()=>AdaptiveHydrologySampler.Inspect(anchors,p=>double.NaN,settings,1));

        foreach(double origin in new[]{0d,8e9,-7e9})
        foreach(double span in new[]{6000d,6000.1234567,16000.1})
        {
            var grid=new DrainageGrid(origin,origin,span,3,3,new double[9]);const int subdivisions=128;
            foreach(int along in new[]{1,63,127})
            foreach(var point in new[]{new MacroPoint(origin+(2L*subdivisions)* (span/subdivisions),origin+(subdivisions+along)*(span/subdivisions)),
                new MacroPoint(origin+(subdivisions+along)*(span/subdivisions),origin+(2L*subdivisions)*(span/subdivisions))})
            {
                int neighbour=point.X==origin+(2L*subdivisions)*(span/subdivisions)?5:7;
                var from=HydrologyReconciler.TargetQuads(grid,subdivisions,4,point);var to=HydrologyReconciler.TargetQuads(grid,subdivisions,neighbour,point);
                Check(from.Count==2 && to.Count==2,"far-origin nonbinary span keeps both sides of explicit lattice target");
            }
        }
        // Real accepted terrain, including both frozen far-origin worlds. No mutable catalogue dependency.
        foreach(string name in new[]{"mountainous/42","mountainous/42/far","vast-world/7319/far"})
        {
            var saved=terrains.Cases.Single(t=>t.Name==name).World;
            var r=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(saved));
            var reconciler=HydrologyReconciler.Create(r);
            var frozen=reconciler.Recipe with {PatchBudget=16,SampleBudget=300000,NodeBudget=500000};
            reconciler=HydrologyReconciler.Restore(frozen);
            var restored=HydrologyReconciler.Restore(JsonSerializer.Deserialize<HydrologyReconciliationRecipe>(Serialize(frozen))!);
            Check(restored.LayerId==reconciler.LayerId,"frozen world and sampling recipe reconstruct");
            var candidates=r.Drainage.Basins.Where(b=>b.Cells.Count<=4).OrderByDescending(b=>b.CapacityEstimate).ToArray();
            Check(candidates.Length>0,"real world has small candidate basin");
            int patch=candidates[0].Cells[0];
            var watch=System.Diagnostics.Stopwatch.StartNew();var result=reconciler.Reconcile(patch);watch.Stop();
            cases.Add(new("world-"+name,Hash(result)));reports.Add(new{name="world-"+name,recipe=frozen,patch,milliseconds=watch.Elapsed.TotalMilliseconds,result});
            Check(Serialize(result)==Serialize(restored.Reconcile(patch)),"real terrain save/recipe reconstruction including far origin");
            Check(result.Metrics.PatchesInspected<=frozen.PatchBudget && result.Metrics.TerrainSamples<=frozen.SampleBudget && result.Metrics.NodeVisits<=frozen.NodeBudget && result.Metrics.AdaptiveSamples<=frozen.AdaptiveSamplingBudget,"real-world bounds");
            Check(result.Components.All(c=>c.VisitedQuads>0),"only discovered real components reported");
            if(result.Spill is { } spill)
            {
                var world=new MacroWorldGenerator(saved);
                foreach(var point in spill.AdaptiveProfile.Points)Check(point.BedElevation==world.Sample(point.X,point.Z).Elevation,"adaptive bed never flattened or invented");
                foreach(var handoff in spill.Crossings)
                {
                    Check(handoff.FirstPatch<handoff.SecondPatch,"crossing canonical bilateral ownership");
                    Check(handoff.Point.BedElevation==world.Sample(handoff.Point.X,handoff.Point.Z).Elevation,"crossing sampled on authoritative terrain");
                }
            }
            var tasks=Task.WhenAll(Task.Run(()=>Hash(reconciler.Reconcile(patch))),Task.Run(()=>Hash(reconciler.Reconcile(patch)))).GetAwaiter().GetResult();
            Check(tasks[0]==Hash(result) && tasks[1]==tasks[0],"real world concurrent reconstruction");
            Reject(()=>reconciler.Reconcile(patch,cancellation:new CancellationToken(true)));
            foreach(var changed in new[]{frozen with {InspectionDependency=frozen.InspectionDependency with {Factor=4}},frozen with {PatchBudget=8},frozen with {FaceSamplingBudget=63}})
                Check(changed.Digest()!=frozen.Digest(),"sampling/budget parameters participate in identity");
            Reject(()=>HydrologyReconciler.Restore(frozen with {InspectionDependency=frozen.InspectionDependency with {Regional=r.Recipe with {Algorithm=2}}}));
            Reject(()=>HydrologyReconciler.Restore(frozen with {InspectionDependency=frozen.InspectionDependency with {Regional=r.Recipe with {Coarse=r.Recipe.Coarse with {DrainageVersion=2}}}}));
            Reject(()=>HydrologyReconciler.Restore(frozen with {InspectionDependency=frozen.InspectionDependency with {Regional=r.Recipe with {Coarse=r.Recipe.Coarse with {World=saved with {Seed=saved.Seed+1}}}}}));
        }
        string path=record?"Tests/Fixtures/hydrology-reconciliation-v1.json":Path.Combine(AppContext.BaseDirectory,"Fixtures/hydrology-reconciliation-v1.json");
        Directory.CreateDirectory("artifacts/hydrology-reconciliation");
        File.WriteAllText("artifacts/hydrology-reconciliation/report.json",JsonSerializer.Serialize(new{checks,reports},Json));
        File.WriteAllText("artifacts/hydrology-reconciliation/candidate-fixture.json",JsonSerializer.Serialize(new Fixture(1,1,cases.ToArray()),Json));
        if(record)
        {
            if(File.Exists(path))throw new IOException("Refusing to overwrite reconciliation fixtures.");
            File.WriteAllText(path,JsonSerializer.Serialize(new Fixture(1,1,cases.ToArray()),Json));
        }
        else if(verify)
        {
            var expected=JsonSerializer.Deserialize<Fixture>(File.ReadAllText(path))!;
            Check(expected.Format==1 && expected.Algorithm==1 && expected.Cases.Length==cases.Count,"reconciliation fixture version/matrix");
            foreach(var c in cases)Check(expected.Cases.Single(e=>e.Name==c.Name).Fingerprint==c.Fingerprint,"frozen reconciliation "+c.Name);
        }
        Console.WriteLine($"HYDROLOGY_RECONCILIATION_PASS: {checks} checks; {cases.Count} cases");return checks;
    }
}
