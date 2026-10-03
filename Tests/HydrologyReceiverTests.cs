using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Thornwake.Core;

internal static class HydrologyReceiverTests
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
    private static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    private const string Output="artifacts/hydrology-receivers";
    private sealed record Frozen(string Name,string Hash);
    private static AcceptedWorldTests.TerrainFixture Worlds()=>JsonSerializer.Deserialize<AcceptedWorldTests.TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
    public static int Run(bool record=false)
    {
        int checks=HydrologyTopologyTests.Run(verify:false,includeWorlds:false,algorithm:5);
        void Check(bool ok,string why){if(!ok)throw new Exception("RECEIVER: "+why);checks++;}
        void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidDataException or OperationCanceledException){checks++;return;}throw new Exception("RECEIVER: expected rejection");}
        var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(Worlds().Cases.Single(c=>c.Name=="mountainous/42").World));
        var recipe=new HydrologyTopologyRecipe(1,5,new(1,1,2,regional.Recipe),4,.03125,1,.01,new(24,200000,100000,200000,8192,20000));
        ReconciliationDomain Domain(double ox=0,double oz=0,bool sill=false)
        {
            double Terrain(MacroPoint p)
            {
                double x=p.X-ox,z=p.Z-oz;
                return -2+40*Math.Max(0,Math.Abs(z-6)-1)*Math.Clamp(16-x,0,1)+40*Math.Max(0,5-x)+
                    (sill?6*Math.Max(0,1-Math.Abs(x-12)):0);
            }
            var d=new ReconciliationDomain("receiver-fixture","basin",1,ox,oz,4,5,3,4,Terrain,
                p=>p==9?new[]{new ReconciliationExit("ocean-z",new(9,5),new(ox+17.5,oz+5.5),0)}:Array.Empty<ReconciliationExit>(),
                (a,b)=>Array.Empty<DrainagePort>(),p=>Array.Empty<DrainagePort>());
            return d with {ReceiverCertificate=p=>p==9?new(1,5,d.Identity,d.CandidateId,p,"ocean-z",0,"whole-patch-ocean-v1"):null};
        }
        var frozen=new List<Frozen>();var reports=new List<object>();
        ProfiledReceiverHydrology Solve(string name,ReconciliationDomain d,HydrologyTopologyRecipe? r=null,int seed=6)
        {
            r??=recipe;var p=AdaptiveBasinTopology.ProfileReceivers(d,r,seed,5);var a=p.Result;
            Check(Hash(a)==Hash(AdaptiveBasinTopology.Run(d,r,seed,5)),name+" profile neutrality");
            Check(Hash(a)==Hash(AdaptiveBasinTopology.RunGraphExperiment(d,r,seed,5,reuse:false)),name+" fresh neighbour traversal equality");
            Check(p.Attribution.Passes.Sum(x=>x.WetVisits+x.SpillVisits+x.BalanceVisits)==a.Metrics.NodeVisits,name+" exclusive visit accounting");
            Check(a.Metrics.Patches<=r.Limits.Patches && a.Metrics.NodeVisits<=r.Limits.NodeVisits && a.Metrics.TerrainSamples<=r.Limits.Samples && p.Receivers.MetadataEntries<=256,name+" unchanged limits");
            if(a.SpillProposal is {} spill)foreach(var point in spill.SampledPath)Check(point.BedElevation==d.Sample(new(point.X,point.Z)),name+" real sampled anchors");
            frozen.Add(new(name,Hash(p)));reports.Add(new{name,profile=p});
            Console.WriteLine($"RECEIVER_CASE {name}: {a.ComponentStop}/{a.SpillStop}; receiver={a.SpillProposal?.DestinationId}; elevation={a.SpillProposal?.RequiredElevation}");return p;
        }
        var d=Domain();var simple=Solve("ocean-via-wet-corridor",d);
        Check(simple.Result.FinalComponentId!=null && simple.Result.SpillProposal!.DestinationId=="ocean-z","certified ocean closes component and spill");
        Check(simple.Receivers.Boundaries.Single(p=>p.Patch==9).ComponentTransitions>0 && simple.Result.Metrics.Patches<9,"direct component transitions avoid target admission");
        var noCertificate=Solve("ocean-like-without-proof",d with {ReceiverCertificate=null},recipe with {Limits=recipe.Limits with {Patches=3}});
        Check(noCertificate.Result.ComponentStop!=TopologyStop.SupportedAtSampleSpacing && noCertificate.Result.Frontier.Any(f=>f.Reason=="patch-budget"),"missing ocean-like patch stays unknown");
        var edge=Solve("undeclared-query-edge",d with {Sample=p=>-2,ReceiverCertificate=null,Exits=p=>Array.Empty<ReconciliationExit>()});
        Check(edge.Result.ComponentStop==TopologyStop.DomainBoundary,"arbitrary edges never drain");
        var inland=Solve("enclosed-no-receiver",d with {Sample=p=>-2+40*Math.Max(0,Math.Abs(p.Z-6)-1)+40*Math.Max(0,Math.Max(5-p.X,p.X-14)),ReceiverCertificate=null,Exits=p=>Array.Empty<ReconciliationExit>()});
        Check(inland.Result.FinalComponentId==null && inland.Result.SpillStop!=TopologyStop.SupportedAtSampleSpacing,"no manufactured inland outlet");
        var returning=Solve("returning-declaration-rejected",d with {Exits=p=>Array.Empty<ReconciliationExit>()});
        Check(returning.Receivers.RejectedCertificates>0 && returning.Result.FinalComponentId==null,"returning or absent external declaration cannot certify");
        var lower=Solve("lower-inland-alternate",d with {Exits=p=>p==6?new[]{new ReconciliationExit("inland",new(6,5),new(5.5,5.5),-1)}:d.Exits(p)});
        Check(lower.Result.SpillProposal!.DestinationId=="inland" && lower.Result.SpillProposal.RequiredElevation==-1,"lower alternate still wins");
        var tieDomain=d with {Exits=p=>p==6?new[]{new ReconciliationExit("a-receiver",new(6,5),new(5.5,5.5),0),new ReconciliationExit("y-receiver",new(6,5),new(5.5,5.5),0)}:d.Exits(p)};
        var tie=Solve("equal-receiver-tie",tieDomain);
        Check(tie.Result.SpillProposal!.DestinationId=="a-receiver","canonical ID not first external arrival");
        Check(Hash(tie.Result)==Hash(AdaptiveBasinTopology.Run(tieDomain with {Exits=p=>tieDomain.Exits(p).Reverse().ToArray()},recipe,6,5)),"caller exit enumeration cannot change tie");
        var mixed=Solve("certified-and-unknown",d with {Sample=p=>-2});
        Check(mixed.Receivers.Boundaries.Any(p=>p.ComponentTransitions>0) && mixed.Result.ComponentStop==TopologyStop.DomainBoundary,"certified receiver cannot erase competing domain uncertainty");
        var spill=Solve("spill-only-transition",Domain(sill:true));
        Check(spill.Result.SpillStop==TopologyStop.SupportedAtSampleSpacing && spill.Result.SpillProposal!.RequiredElevation==4 && spill.Receivers.Boundaries.Any(p=>p.SpillTransitions>0),"sill crossed only by minimax");
        var unresolved=Solve("early-receiver-unresolved-competitor",d with {Sample=p=>d.Sample(p)+.4*Math.Exp(-Math.Pow((p.X-7.5)/.04,2))},recipe with {MaximumDepth=1});
        Check(unresolved.Result.FinalComponentId==null && unresolved.Result.Frontier.Any(f=>f.Reason=="resolution-limit"),"unresolved competing evidence not erased");
        // All four target patches independently cover their complete rectangles. The centre patch
        // is the only interior; missing one proof leaves a real unknown side of the wet component.
        var multiple=d with {Width=3,Depth=3,Sample=p=>-2,Exits=p=>new[]{new ReconciliationExit("ocean-"+p,new(p,5),new((p%3)*4+1.5,(p/3)*4+1.5),0)}};
        multiple=multiple with {ReceiverCertificate=p=>p!=4?new(1,5,multiple.Identity,multiple.CandidateId,p,"ocean-"+p,0,"whole-patch-ocean-v1"):null};
        var multi=Solve("four-independent-certificates",multiple,seed:4);
        Check(multi.Result.FinalComponentId!=null && multi.Receivers.Boundaries.Count(p=>p.ComponentTransitions>0)==4 && multi.Result.SpillProposal!.DestinationId=="ocean-1","all four proofs required; canonical equal tie");
        var partial=Solve("one-of-four-proof-missing",multiple with {ReceiverCertificate=p=>p!=1?multiple.ReceiverCertificate!(p):null},seed:4);
        Check(partial.Result.FinalComponentId==null,"one missing proof still unknown");
        var feedback=d with {Sample=p=>d.Sample(p)+19*Math.Max(0,1-Math.Abs(p.X-8)/.25)*Math.Max(0,1-Math.Abs(p.Z-5.25)/.125)+
            5*Math.Max(0,1-Math.Abs(p.X-8.5)/.5)*Math.Max(0,1-Math.Abs(p.Z-5.5)/.5)};
        var corrected=Solve("receiver-with-shared-face-correction",feedback);
        Check(corrected.Result.Metrics.Passes>1 && corrected.Result.FinalComponentId!=null,"receiver does not suppress shared-face correction");
        var contradiction=Solve("certificate-contradicts-probe",d with {Sample=p=>1,Stage=3});
        Check(contradiction.Result.FinalComponentId==null && contradiction.Receivers.RejectedCertificates>0 && contradiction.Result.Frontier.Any(f=>f.Reason=="receiver-invalid"),"contradictory certificate stays unknown");
        var far=Solve("far-origin",Domain(8e9,-7e9));
        Check(far.Result.SpillStop==simple.Result.SpillStop && far.Result.SpillProposal!.RequiredElevation==simple.Result.SpillProposal!.RequiredElevation,"far origin preserves receiver contract");
        Check(Hash(far)==Hash(AdaptiveBasinTopology.ProfileReceivers(Domain(8e9,-7e9),recipe,6,5)),"far exact replay");
        foreach(var limits in new[]{recipe.Limits with {NodeVisits=1},recipe.Limits with {Samples=1},recipe.Limits with {Cells=1},recipe.Limits with {Patches=1},recipe.Limits with {Patches=1,FrontierEntries=1}})
        {
            var limited=Solve("budget-"+Hash(limits)[..8],d,recipe with {Limits=limits});
            Check(limited.Result.Continuation!=null,"budget continuation explicit");
            var token=JsonSerializer.Deserialize<HydrologyTopologyContinuation>(JsonSerializer.Serialize(limited.Result.Continuation))!.Expand(recipe.Limits);
            Check(token.Recipe.Algorithm==5 && Hash(AdaptiveBasinTopology.Run(d,token.Recipe,token.SeedPatch,token.SeedQuad))==Hash(simple.Result),"V5 replay exact");
        }
        foreach(var invalid in new[]{d.ReceiverCertificate!(9)! with {Algorithm=3},d.ReceiverCertificate!(9)! with {Domain="foreign"},d.ReceiverCertificate!(9)! with {Candidate="other"},d.ReceiverCertificate!(9)! with {Patch=8},d.ReceiverCertificate!(9)! with {Evidence="point-only"},d.ReceiverCertificate!(9)! with {Terminal=false},d.ReceiverCertificate!(9)! with {Level=double.NaN}})
        {
            var bad=Solve("invalid-"+frozen.Count,d with {ReceiverCertificate=p=>p==9?invalid:null});
            Check(bad.Receivers.RejectedCertificates==1 && bad.Result.FinalComponentId==null,"invalid dependency fails closed");
        }
        Reject(()=>(recipe with {Algorithm=4}).Validate());Reject(()=>AdaptiveHydrologyReconciler.Restore(recipe with {InspectionDependency=recipe.InspectionDependency with {Algorithm=9}}));
        Reject(()=>AdaptiveBasinTopology.ProfileReceivers(d,recipe with {Algorithm=3},6,5));
        Reject(()=>AdaptiveBasinTopology.Run(d,recipe,6,5,new CancellationToken(true)));
        using(var cancel=new CancellationTokenSource())Reject(()=>AdaptiveBasinTopology.Run(d with {ReceiverCertificate=p=>{cancel.Cancel();return d.ReceiverCertificate!(p);}},recipe,6,5,cancel.Token));
        var parallel=Task.WhenAll(Enumerable.Range(0,3).Select(_=>Task.Run(()=>Hash(AdaptiveBasinTopology.ProfileReceivers(d,recipe,6,5))))).GetAwaiter().GetResult();
        Check(parallel.All(h=>h==Hash(simple)),"parallel query-local state");
        var other=AdaptiveBasinTopology.ProfileReceivers(d,recipe,7,5);
        Check(Hash(other.Result.SpillProposal)==Hash(simple.Result.SpillProposal) && other.Result.FinalComponentId==simple.Result.FinalComponentId,"query entry canonical result");
        Check(Hash(AdaptiveBasinTopology.ProfileReceivers(d,recipe,6,5))==Hash(simple),"caller order no retained state");
        var old=recipe with {Algorithm=3};
        Check(Hash(AdaptiveBasinTopology.Run(d with {ReceiverCertificate=_=>throw new Exception("A3 invoked V5 provider")},old,6,5))==Hash(AdaptiveBasinTopology.Run(d with {ReceiverCertificate=null},old,6,5)),"A3 never consumes certificate provider");
        // Falsification probes of the analytic envelope, across all accepted worlds and far origins.
        foreach(var world in Worlds().Cases)
        {
            var gen=new MacroWorldGenerator(world.World);double span=world.World.Profile.WorldSize/128;
            int proved=0;
            for(int z=0;z<128;z+=7)for(int x=0;x<128;x+=7)
            {
                double px=world.World.OriginX-world.World.Profile.WorldSize*.5+x*span,pz=world.World.OriginZ-world.World.Profile.WorldSize*.5+z*span;
                if(!OceanPatchProof.Covers(gen,px,pz,span))continue;proved++;
                for(int j=0;j<=4;j++)for(int i=0;i<=4;i++)Check(gen.Sample(px+span*i/4,pz+span*j/4).Elevation<=world.World.Profile.SeaLevel,"proved rectangle contains no land: "+world.Name);
            }
            Check(proved>0,"nonempty conservative ocean proof "+world.Name);
            foreach(var f in gen.LandFeatures)Check(!OceanPatchProof.Covers(gen,f.Center.X-span/2,f.Center.Z-span/2,span),"land core never certified");
        }
        Directory.CreateDirectory(Output);File.WriteAllText(Output+"/analytic.json",JsonSerializer.Serialize(reports,Json));
        string fixture="Tests/Fixtures/hydrology-receiver-v5.json";
        if(record)File.WriteAllText(fixture,JsonSerializer.Serialize(frozen,Json));
        else
        {
            var expected=JsonSerializer.Deserialize<Frozen[]>(File.ReadAllText(fixture))!;Check(expected.Length==frozen.Count,"separate V5 fixture count");
            foreach(var row in frozen)Check(expected.Single(e=>e.Name==row.Name).Hash==row.Hash,"frozen V5 "+row.Name);
        }
        Console.WriteLine($"RECEIVER_PASS: {checks} checks; {frozen.Count} receiver cases plus 24 inherited analytic controls");return checks;
    }
    public static int Measure()
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("RECEIVER_MATRIX: "+why);checks++;}
        var rows=new List<object>();var worlds=Worlds();
        using var frozen=JsonDocument.Parse(File.ReadAllText("Tests/Fixtures/hydrology-work-coverage-v1.json"));
        using var prior=JsonDocument.Parse(File.ReadAllText("docs/experiments/hydrology-spill-attribution.json"));
        foreach(var (world,size) in new[]{("vast-world/7319/far","small"),("mountainous/42","small"),("vast-world/7319/far","medium"),("mountainous/42","large"),("mountainous/42/far","large"),("vast-world/7319/far","large")})
        {
            var expected=frozen.RootElement.EnumerateArray().Single(r=>r.GetProperty("World").GetString()==world && r.GetProperty("Size").GetString()==size && r.GetProperty("Algorithm").GetInt32()==3);
            int patch=expected.GetProperty("Patch").GetInt32();var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(worlds.Cases.Single(c=>c.Name==world).World));
            var recipe=new HydrologyTopologyRecipe(1,3,new(1,1,2,regional.Recipe),3,2,64,.25,new(32,2000000,800000,2000000,16384,180000));
            ReconciliationDomain Domain()=>HydrologyReconciler.Create(regional).CreateDomain(patch,CancellationToken.None);
            var clock=Stopwatch.StartNew();var control=AdaptiveBasinTopology.ProfileSpill(Domain(),recipe,patch,null);double beforeMs=clock.Elapsed.TotalMilliseconds;
            Check(Hash(control.Result)==expected.GetProperty("Fingerprint").GetString(),"frozen A3 complete result");
            clock.Restart();var p=AdaptiveBasinTopology.ProfileReceivers(Domain(),recipe with {Algorithm=5},patch,null);double afterMs=clock.Elapsed.TotalMilliseconds;
            Check(p.Attribution.Passes.Sum(x=>x.WetVisits+x.SpillVisits+x.BalanceVisits)==p.Result.Metrics.NodeVisits,"exclusive visits");
            Check(p.Result.Metrics.NodeVisits<=recipe.Limits.NodeVisits && p.Result.Metrics.Patches<=recipe.Limits.Patches,"unchanged work caps");
            Check(Hash(p.Result)==Hash(AdaptiveBasinTopology.Run(Domain(),recipe with {Algorithm=5},patch,null)),"deterministic independent reconstruction");
            Check(Hash(p.Result)==Hash(AdaptiveBasinTopology.RunGraphExperiment(Domain(),recipe with {Algorithm=5},patch,null,reuse:false)),"clean neighbour traversal equality");
            Check(p.Receivers.RejectedCertificates==0 && p.Receivers.MetadataRefusals==0,"all encountered production certificates valid and bounded");
            if(world=="vast-world/7319/far" && size=="small")
                Check(p.Result.FinalComponentId==null && (p.Receivers.FinalCandidateResolved==false || p.Result.SpillProposal==null) && p.Result.Frontier.Any(f=>f.Reason=="resolution-limit"),"Vast reference cannot hide unresolved sampling");
            var previous=prior.RootElement.GetProperty("Cases").EnumerateArray().Single(r=>r.GetProperty("World").GetString()==world && r.GetProperty("Size").GetString()==size);
            var missing=previous.GetProperty("Summary").GetProperty("MissingAdjacentPatches").EnumerateArray().Select(x=>x.GetInt32()).ToArray();
            // Re-evaluate ONLY the explicitly recorded prior obligations, through the identical proof
            // adapter. These are proven counterfactual patch obligations, not fabricated traversals.
            var proofDomain=Domain();var obligations=missing.Select(id=>new{Patch=id,DischargedByAreaProof=proofDomain.ReceiverCertificate!(id)!=null,
                DirectlyEncountered=p.Receivers.Boundaries.Any(b=>b.Patch==id && b.ComponentTransitions+b.SpillTransitions>0),
                StillFrontier=p.Result.Frontier.Any(f=>f.Patch==id),Status=proofDomain.ReceiverCertificate!(id)!=null?"certified-external":"unproved-still-required"}).ToArray();
            var summary=HydrologySpillAttributionTests.Summary(new(p.Result,p.Graph,p.Attribution));
            rows.Add(new{World=world,Size=size,Patch=patch,BeforeMilliseconds=beforeMs,AfterMilliseconds=afterMs,Before=HydrologySpillAttributionTests.Summary(control),After=summary,
                BeforeMetrics=control.Result.Metrics,AfterMetrics=p.Result.Metrics,ResultHash=Hash(p.Result),p.Receivers,PriorObligations=obligations,
                p.Attribution.Complete,p.Attribution.Truncation,
                ActualCandidate=p.Result.SpillProposal is {} proposal?new {proposal.DestinationId,proposal.ReceiverLevel,proposal.RequiredElevation,Endpoint=proposal.SampledPath.Last()}:null,
                FinalReceiver=p.Result.FinalComponentId!=null?p.Result.SpillProposal?.DestinationId:null,
                SolverFrontiers=p.Result.Frontier.GroupBy(f=>f.Reason).OrderBy(g=>g.Key,StringComparer.Ordinal).ToDictionary(g=>g.Key,g=>g.Count()),
                SolverMissingPatches=p.Result.Frontier.Where(f=>f.Reason=="patch-budget" && f.Patch!=null).Select(f=>f.Patch!.Value).Distinct().Order().ToArray(),
                SolverUnresolvedSampling=p.Result.Frontier.Any(f=>f.Reason=="resolution-limit"),
                UnresolvedExamples=p.Result.Frontier.Where(f=>f.Reason=="resolution-limit").Take(8).ToArray(),
                Passes=p.Attribution.Passes.Select(x=>x with {Frontiers=x.Frontiers.Take(12).ToArray()}).ToArray(),p.Graph});
            Directory.CreateDirectory(Output);File.WriteAllText(Output+"/matrix.json",JsonSerializer.Serialize(rows,Json));
            Console.WriteLine($"RECEIVER_MATRIX {world}/{size}: {p.Result.ComponentStop}/{p.Result.SpillStop}, {p.Result.Metrics.Patches} patches, {p.Result.Metrics.NodeVisits} visits, {beforeMs:F0}/{afterMs:F0} ms, {obligations.Count(o=>o.DischargedByAreaProof)}/{missing.Length} old obligations proved");
        }
        Console.WriteLine($"RECEIVER_MATRIX_PASS: {checks} checks; 6 controls");return checks;
    }
}
