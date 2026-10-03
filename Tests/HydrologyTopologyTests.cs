using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Thornwake.Core;

internal static class HydrologyTopologyTests
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
    private sealed record FrozenCase(string Name,string Hash);
    private sealed record Fixture(int Format,int Algorithm,FrozenCase[] Cases);
    private static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    public static int Run(bool verify=true,bool includeWorlds=true,int algorithm=2,bool compareReuse=false,bool compareLocality=false)
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("TOPOLOGY: "+why);checks++;}
        void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidDataException or OperationCanceledException){checks++;return;}throw new Exception("TOPOLOGY: expected rejection");}
        var worlds=JsonSerializer.Deserialize<AcceptedWorldTests.TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
        var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(worlds.Cases.Single(c=>c.Name=="mountainous/42").World));
        var recipe=new HydrologyTopologyRecipe(1,algorithm,new(1,1,2,regional.Recipe),4,.03125,1,.01,new(24,200000,100000,200000,8192,20000));
        var reports=new List<object>();var cases=new List<FrozenCase>();
        void Bounds(HydrologyTopologyResult r,HydrologyTopologyRecipe p)
        {
            var m=r.Metrics;var b=p.Limits;
            Check(m.CompletedPatches<=m.Patches && m.Patches<=b.Patches && m.TerrainSamples<=b.Samples && m.AllocatedCells<=b.Cells && m.Splits<=b.Splits &&
                m.NodeVisits<=b.NodeVisits && m.Passes<=b.Passes && r.Frontier.Count<=b.FrontierEntries && m.MaximumDepthReached<=p.MaximumDepth,"all frozen limits enforced");
            Check(r.FinalComponentId==null || r.ComponentStop==TopologyStop.SupportedAtSampleSpacing && r.SpillStop==TopologyStop.SupportedAtSampleSpacing,"partial component has no final identity");
            Check(r.Continuation!=null || r.ComponentStop!=TopologyStop.WorkBudget && r.SpillStop!=TopologyStop.WorkBudget,"budget exhaustion supplies replay state");
        }
        HydrologyTopologyResult Solve(string name,ReconciliationDomain d,HydrologyTopologyRecipe? p=null,int patch=6,int? quad=5)
        {
            p??=recipe;var clock=Stopwatch.StartNew();var r=AdaptiveBasinTopology.Run(d,p,patch,quad);clock.Stop();Bounds(r,p);
            if(compareLocality)
            {
                var diagnostic=AdaptiveBasinTopology.ProfileLocality(d,p,patch,quad);
                Check(Hash(r)==Hash(diagnostic.Result),"locality diagnostics preserve complete serialized result: "+name);
                Check(diagnostic.Locality.Complete,"analytic locality profile complete: "+name);
            }
            if(compareReuse)
            {
                var cached=AdaptiveBasinTopology.ProfileGraph(d,p,patch,quad,reuse:true);
                var plain=AdaptiveBasinTopology.RunGraphExperiment(d,p,patch,quad,reuse:false);
                Check(Hash(cached.Result)==Hash(plain) && Hash(r)==Hash(plain),"generation-validated adjacency reuse equals complete full restart: "+name);
            }
            if(r.SpillProposal is {} spill)
            {
                Check(spill.RequiredElevation>=spill.ReceiverLevel,"receiver constraint in every proposal");
                foreach(var point in spill.SampledPath)Check(point.BedElevation==d.Sample(new(point.X,point.Z)),"every reported path anchor is authoritative terrain");
                foreach(var c in spill.Crossings)Check(c.FirstPatch<c.SecondPatch && c.Point.BedElevation==d.Sample(new(c.Point.X,c.Point.Z)),"every crossing canonical and authoritative");
                for(int i=1;i<spill.Cells.Count;i++)
                {
                    var a=spill.Cells[i-1];var b=spill.Cells[i];
                    bool face=((a.X+a.Size==b.X || b.X+b.Size==a.X) && Math.Min(a.Z+a.Size,b.Z+b.Size)>Math.Max(a.Z,b.Z)) ||
                        ((a.Z+a.Size==b.Z || b.Z+b.Size==a.Z) && Math.Min(a.X+a.Size,b.X+b.Size)>Math.Max(a.X,b.X));
                    Check(face,"minimax path uses positive shared faces, including mixed refinement depths");
                }
            }
            cases.Add(new(name,Hash(r)));reports.Add(new{name,milliseconds=clock.Elapsed.TotalMilliseconds,result=r});
            Console.WriteLine($"TOPOLOGY_CASE {name}: {r.ComponentStop}/{r.SpillStop}, {r.SpillProposal?.RequiredElevation}, {r.Metrics.TerrainSamples} samples, {clock.Elapsed.TotalMilliseconds:F1}ms");return r;
        }
        ReconciliationDomain Domain(bool barrier=false,bool opening=false,double ox=0,double oz=0,double scale=1,DrainagePort? old=null)
        {
            double Bed(MacroPoint p)
            {
                double x=(p.X-ox)/scale,z=(p.Z-oz)/scale;
                double walls=40*Math.Max(0,Math.Abs(z-6)-1)+40*Math.Max(0,Math.Max(5-x,x-18));
                double sill=3*Math.Max(0,1-Math.Abs(x-16));
                if(opening)sill*=Math.Clamp((Math.Abs(z-5.5)-.25)/.25,0,1);
                return 1+walls+sill+(barrier?19*Math.Max(0,1-Math.Abs(x-7.5)/.25):0);
            }
            return new("analytic-v2","basin",3,ox,oz,4*scale,5,3,4,Bed,
                patch=>patch==9?new[]{new ReconciliationExit("downstream",new(9,5),new(ox+17.5*scale,oz+5.5*scale),0)}:Array.Empty<ReconciliationExit>(),
                (a,b)=>old.HasValue && Math.Min(old.Value.FromCell,old.Value.ToCell)==a && Math.Max(old.Value.FromCell,old.Value.ToCell)==b?new[]{old.Value}:Array.Empty<DrainagePort>(),
                patch=>old.HasValue && patch==old.Value.FromCell?new[]{old.Value}:Array.Empty<DrainagePort>());
        }
        var oldRecipe=new HydrologyReconciliationRecipe(1,1,recipe.InspectionDependency,PatchBudget:24,SampleBudget:500000,NodeBudget:500000,AdaptiveSamplingBudget:10000,Adaptive:new(8,.03125,1,.01));
        var oldBarrier=BasinReconciliationSearch.Run(Domain(barrier:true),oldRecipe,6,5);
        var oldOpening=BasinReconciliationSearch.Run(Domain(opening:true),oldRecipe,6,5);
        Check(oldBarrier.ComponentStatus==ComponentDiscovery.Contradictory && oldOpening.SpillStatus==SpillResolution.Contradictory,"same analytic terrain is contradictory in preserved algorithm 1");
        var complete=Solve("complete",Domain());
        Check(complete.ComponentStop==TopologyStop.SupportedAtSampleSpacing && complete.SpillStop==TopologyStop.SupportedAtSampleSpacing && complete.SpillProposal!.RequiredElevation==4,"original 4m reachable sill supported");
        var barrier=Solve("narrow-barrier-left",Domain(barrier:true));
        var barrierRight=Solve("narrow-barrier-right",Domain(barrier:true),patch:8);
        Check(barrier.FinalComponentId!=null && barrierRight.FinalComponentId!=null && barrier.FinalComponentId!=barrierRight.FinalComponentId,"adaptive ridge actually separates obsolete component");
        Check(barrier.SpillProposal!.RequiredElevation==20 && barrierRight.SpillProposal!.RequiredElevation==4,"barrier updates reachable minimax cost, not only contradiction");
        var opening=Solve("narrow-opening",Domain(opening:true));
        Check(opening.SpillStop==TopologyStop.SupportedAtSampleSpacing && opening.SpillProposal!.RequiredElevation==1,"opening participates in lower corrected topology");
        Check(opening.Metrics.Splits>0 && opening.ComponentCells.Count>complete.ComponentCells.Count,"adaptive evidence changes cell graph");
        Check(barrier.RefinementEvidence.Any(e=>e.SampledMaximum>e.CornerMaximum && e.Split),"ridge evidence replaced parent topology");
        Check(opening.RefinementEvidence.Any(e=>e.SampledMinimum<e.CornerMaximum && e.Split),"opening evidence replaced parent topology");
        var d=Domain(barrier:true);var alternative=d with {Exits=patch=>patch==6?new[]{new ReconciliationExit("higher-but-reachable",new(6,5),new(5.5,5.5),6)}:d.Exits(patch)};
        var selected=Solve("reachable-alternative",alternative);
        Check(selected.SpillProposal!.DestinationId=="higher-but-reachable" && selected.SpillProposal.RequiredElevation==6,"higher destination beats low face behind 20m ridge");
        var low=Domain(opening:true);var receiver=low with {Exits=patch=>patch==9?new[]{new ReconciliationExit("receiver",new(9,5),new(17.5,5.5),7)}:Array.Empty<ReconciliationExit>()};
        var constrained=Solve("receiver-level",receiver);Check(constrained.SpillProposal!.RequiredElevation==7,"explicit receiver remains binding");
        var crossings=opening.SpillProposal!.Crossings;
        var crossing=crossings.Last();var original=new DrainagePort("old-port",crossing.FirstPatch,crossing.SecondPatch,new(crossing.Point.X,crossing.Point.Z),crossing.Point.BedElevation);
        var confirmed=Solve("confirmed-port",Domain(opening:true,old:original));
        Check(confirmed.SpillProposal!.Crossings.Any(c=>c.Id==original.Id && c.OriginalPortId==original.Id),"exact old crossing retained");
        var wrong=original with {Id="obsolete-port",Position=new(original.Position.X,original.Position.Z+1),BedElevation=40};
        var changed=Solve("bilateral-replacement",Domain(opening:true,old:wrong));
        Check(changed.SpillProposal!.OutletAgreement=="alternative-crossing-proposed" && changed.OriginalOutlets.Contains(wrong),"old proposal retained, replacement explicit");
        foreach(int patch in new[]{6,7,8})
        {
            var entered=AdaptiveBasinTopology.Run(Domain(opening:true,old:wrong),recipe,patch,5);
            Check(entered.FinalComponentId==changed.FinalComponentId && Hash(entered.SpillProposal)==Hash(changed.SpillProposal),"tributary entry order preserves full final spill and bilateral identity");
        }
        foreach(var limited in new[]{recipe.Limits with {Patches=1},recipe.Limits with {Samples=30},recipe.Limits with {Cells=10},
            recipe.Limits with {Splits=0},recipe.Limits with {NodeVisits=1},recipe.Limits with {FrontierEntries=1,Patches=1}})
        {
            string name=limited.Patches==1?(limited.FrontierEntries==1?"frontier":"patch"):limited.Samples==30?"sample":limited.Cells==10?"cell":limited.Splits==0?"split":"node";
            var truncated=Solve("budget-"+name,Domain(opening:true),recipe with {Limits=limited});
            Check(truncated.Continuation!=null && truncated.FinalComponentId==null,"truncation never certifies closure");
            var token=JsonSerializer.Deserialize<HydrologyTopologyContinuation>(JsonSerializer.Serialize(truncated.Continuation))!.Expand(recipe.Limits);
            var resumed=AdaptiveBasinTopology.Run(Domain(opening:true),token.Recipe,token.SeedPatch,token.SeedQuad);
            Check(Hash(resumed)==Hash(opening),"serialized expanded replay equals sufficiently-budgeted one-shot including metrics");
            Check(truncated.LayerId==resumed.LayerId && truncated.RequestId!=resumed.RequestId,"work limits change request, not geographic identity");
        }
        var spillLimited=Solve("spill-frontier-continuation",Domain(),recipe with {Limits=recipe.Limits with {NodeVisits=complete.ComponentCells.Count+1}});
        Check(spillLimited.ComponentStop==TopologyStop.SupportedAtSampleSpacing && spillLimited.SpillStop==TopologyStop.WorkBudget && spillLimited.FinalComponentId==null,"spill frontier withholds final component identity pending topology convergence");
        var spillToken=spillLimited.Continuation!.Expand(recipe.Limits);
        Check(Hash(AdaptiveBasinTopology.Run(Domain(),spillToken.Recipe,spillToken.SeedPatch,spillToken.SeedQuad))==Hash(complete),"continuation after wet discovery reproduces one-shot minimax result");
        var boundary=Solve("domain-boundary",Domain() with {Sample=p=>1,Exits=patch=>Array.Empty<ReconciliationExit>()});
        Check(boundary.ComponentStop==TopologyStop.DomainBoundary && boundary.FinalComponentId==null && boundary.SpillProposal==null,"world boundaries are unresolved, not drains");
        var insufficient=Solve("resolution-limit",Domain(barrier:true),recipe with {MaximumDepth=1});
        Check(insufficient.ComponentStop==TopologyStop.ResolutionLimit && insufficient.FinalComponentId==null,"insufficient refinement cannot certify a component");
        foreach(double scale in new[]{1d,1.01234567})
        {
            var farDomain=Domain(opening:true,ox:8e9,oz:-7e9,scale:scale);
            var far=Solve("far-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture),farDomain);
            Check(far.SpillStop==TopologyStop.SupportedAtSampleSpacing && Math.Abs(far.SpillProposal!.RequiredElevation-1)<.001,"far-origin nonbinary opening reconstructed");
            Check(Hash(far)==Hash(AdaptiveBasinTopology.Run(farDomain,recipe,6,5)),"far-origin exact reconstruction");
            foreach(var c in far.SpillProposal!.Crossings)Check(c.Point.BedElevation==farDomain.Sample(new(c.Point.X,c.Point.Z)),"crossings use authoritative terrain");
        }
        var parallel=Task.WhenAll(Enumerable.Range(0,3).Select(_=>Task.Run(()=>Hash(AdaptiveBasinTopology.Run(Domain(opening:true),recipe,6,5))))).GetAwaiter().GetResult();
        Check(parallel.All(h=>h==Hash(opening)),"independent parallel queries identical");
        Reject(()=>AdaptiveBasinTopology.Run(Domain(),recipe,6,5,new CancellationToken(true)));
        using(var cancel=new CancellationTokenSource())
        {
            int n=0;var originalDomain=Domain();var interrupted=originalDomain with {Sample=p=>{if(++n==11)cancel.Cancel();return originalDomain.Sample(p);}};
            Reject(()=>AdaptiveBasinTopology.Run(interrupted,recipe,6,5,cancel.Token));Check(n==11,"cancellation checked on every terrain sample");
        }
        Reject(()=>(recipe with {Algorithm=1}).Validate());
        Reject(()=>new HydrologyTopologyContinuation(1,recipe,6,5).Expand(recipe.Limits with {Samples=1}));
        Reject(()=>AdaptiveHydrologyReconciler.Restore(recipe with {InspectionDependency=recipe.InspectionDependency with {Algorithm=99}}));
        var restoredRecipe=JsonSerializer.Deserialize<HydrologyTopologyRecipe>(JsonSerializer.Serialize(recipe))!;
        Check(restoredRecipe.LayerId()==recipe.LayerId() && restoredRecipe.RequestId()==recipe.RequestId(),"recipe round trip");
        var shared=Domain();
        var sharedEvidence=shared with {Sample=p=>shared.Sample(p)+19*Math.Max(0,1-Math.Abs(p.X-8)/.25)*Math.Max(0,1-Math.Abs(p.Z-5.25)/.125)+
            5*Math.Max(0,1-Math.Abs(p.X-8.5)/.5)*Math.Max(0,1-Math.Abs(p.Z-5.5)/.5)};
        var propagated=Solve("shared-face-feedback",sharedEvidence);
        Check(propagated.Metrics.Passes>1 && propagated.ComponentStop==TopologyStop.SupportedAtSampleSpacing,"finer neighbour evidence rebuilds coarse topology and restarts obsolete search");
        var fromOther=AdaptiveBasinTopology.Run(sharedEvidence,recipe,8,5);
        Check(fromOther.FinalComponentId==propagated.FinalComponentId && Hash(fromOther.SpillProposal)==Hash(propagated.SpillProposal),"cross-face refinement converges independent of tributary entry");
        var simultaneous=Task.WhenAll(Enumerable.Range(0,3).Select(_=>Task.Run(()=>Hash(AdaptiveBasinTopology.Run(sharedEvidence,recipe,6,5))))).GetAwaiter().GetResult();
        Check(simultaneous.All(h=>h==Hash(propagated)),"iterative shared-face correction is concurrent and deterministic");
        var onePass=Solve("budget-passes",sharedEvidence,recipe with {Limits=recipe.Limits with {Passes=1}});
        Check(onePass.SpillStop==TopologyStop.WorkBudget && onePass.FinalComponentId==null,"pass budget does not certify stale topology");
        var passToken=JsonSerializer.Deserialize<HydrologyTopologyContinuation>(JsonSerializer.Serialize(onePass.Continuation))!.Expand(recipe.Limits);
        Check(Hash(AdaptiveBasinTopology.Run(sharedEvidence,passToken.Recipe,passToken.SeedPatch,passToken.SeedQuad))==Hash(propagated),"pass-budget continuation converges to one-shot");
        var corners=Domain() with {Width=2,Depth=2,Sample=p=>(p.X>=3&&p.X<=4&&p.Z>=3&&p.Z<=4)||(p.X>=4&&p.X<=5&&p.Z>=4&&p.Z<=5)?0:40,Exits=p=>Array.Empty<ReconciliationExit>()};
        var cornerA=Solve("corner-first",corners,patch:0,quad:15);var cornerB=Solve("corner-second",corners,patch:3,quad:0);
        Check(!cornerA.ComponentCells.Intersect(cornerB.ComponentCells).Any(),"corner-only pools never share adaptive graph cells");
        var rough=Domain(barrier:true) with {Sample=p=>1+19*Math.Exp(-Math.Pow((p.X-7.5)/.02,2))};
        var unresolved=Solve("curvature-unresolved",rough,recipe with {MaximumDepth=1},quad:7);
        Check(unresolved.FinalComponentId==null && unresolved.SpillStop!=TopologyStop.SupportedAtSampleSpacing,"unresolved nonlinear evidence is never certified");
        using(var cancel=new CancellationTokenSource())
        {
            var withCancellation=Domain() with {Exits=p=>{cancel.Cancel();return Array.Empty<ReconciliationExit>();}};
            Reject(()=>AdaptiveBasinTopology.Run(withCancellation,recipe,6,5,cancel.Token));
        }
        foreach(var invalid in new[]{recipe with {Format=2},recipe with {MaximumDepth=7},recipe with {MinimumSpacing=double.NaN},recipe with {Limits=recipe.Limits with {Cells=0}}})Reject(()=>invalid.Validate());
        Reject(()=>AdaptiveBasinTopology.Run(Domain() with {Sample=p=>double.NaN},recipe,6,5));
        Reject(()=>AdaptiveBasinTopology.Run(Domain(),recipe,6,999));
        Reject(()=>AdaptiveHydrologyReconciler.Restore(recipe with {InspectionDependency=recipe.InspectionDependency with {Regional=regional.Recipe with {Coarse=regional.Recipe.Coarse with {World=regional.Recipe.Coarse.World with {Algorithm=3}}}}}));
        if(includeWorlds)foreach(string name in new[]{"mountainous/42","mountainous/42/far","vast-world/7319/far"})
        {
            var world=worlds.Cases.Single(c=>c.Name==name).World;var r=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(world));
            int patch=r.Drainage.Basins.Where(b=>b.Cells.Count<=4).OrderByDescending(b=>b.CapacityEstimate).First().Cells[0];
            var baseline=HydrologyReconciler.Create(r);var domain=baseline.CreateDomain(patch,CancellationToken.None);
            var initial=new HydrologyTopologyRecipe(1,algorithm,baseline.Recipe.InspectionDependency,3,2,64,.25,new(8,300000,180000,500000,8192,40000));
            var first=Solve("world-"+name+"-initial",domain,initial,patch,null);
            var larger=initial with {Limits=new(32,2000000,800000,2000000,16384,180000)};
            HydrologyTopologyResult expanded;
            var clock=Stopwatch.StartNew();
            if(first.Continuation is {} continuation)
            {
                var token=JsonSerializer.Deserialize<HydrologyTopologyContinuation>(JsonSerializer.Serialize(continuation))!.Expand(larger.Limits);
                expanded=AdaptiveHydrologyReconciler.Continue(token);
            }
            else expanded=AdaptiveHydrologyReconciler.Restore(larger).Reconcile(patch);
            clock.Stop();Bounds(expanded,larger);cases.Add(new("world-"+name+"-expanded",Hash(expanded)));
            reports.Add(new{name="world-"+name+"-expanded",milliseconds=clock.Elapsed.TotalMilliseconds,result=expanded});
            if(expanded.SpillProposal is {} realSpill)
            {
                var authoritative=new MacroWorldGenerator(world);
                foreach(var point in realSpill.SampledPath)Check(point.BedElevation==authoritative.Sample(point.X,point.Z).Elevation,"real world path retains authoritative terrain, including endpoint");
                foreach(var port in realSpill.Crossings)Check(port.Point.BedElevation==authoritative.Sample(port.Point.X,port.Point.Z).Elevation,"real world canonical crossing retains authoritative terrain");
            }
            var direct=AdaptiveHydrologyReconciler.Restore(larger).Reconcile(patch);
            Check(Hash(expanded)==Hash(direct),"real-world serialized expansion equals fresh reconstruction");
            Console.WriteLine($"TOPOLOGY_WORLD {name}: {expanded.ComponentStop}/{expanded.SpillStop}, {expanded.Metrics.Patches} patches, {expanded.Metrics.TerrainSamples} samples, {clock.Elapsed.TotalMilliseconds:F1}ms");
        }
        string directory=compareLocality?"artifacts/hydrology-locality/topology-analytic":compareReuse?"artifacts/hydrology-graph/topology-analytic":algorithm==2?"artifacts/hydrology-topology":"artifacts/hydrology-topology-v3";
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory,"report.json"),JsonSerializer.Serialize(new{checks,reports},Json));
        var fixture=new Fixture(1,algorithm,cases.ToArray());File.WriteAllText(Path.Combine(directory,"candidate-fixture.json"),JsonSerializer.Serialize(fixture,Json));
        if(verify)
        {
            var expected=JsonSerializer.Deserialize<Fixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,$"Fixtures/hydrology-topology-v{algorithm}.json")))!;
            Check(expected.Format==1 && expected.Algorithm==algorithm && expected.Cases.Length==cases.Count,"fixture version/matrix");
            foreach(var c in cases)Check(expected.Cases.Single(e=>e.Name==c.Name).Hash==c.Hash,"frozen topology "+c.Name);
        }
        Console.WriteLine($"HYDROLOGY_TOPOLOGY_{algorithm}_PASS: {checks} checks; {cases.Count} cases");return checks;
    }
}
