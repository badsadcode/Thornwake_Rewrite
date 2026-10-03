using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Thornwake.Core;

internal static class HydrologyLocalityTests
{
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true,Converters={new System.Text.Json.Serialization.JsonStringEnumConverter()}};
    private static string Hash<T>(T value)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(value))).ToLowerInvariant();
    public static int Run()
    {
        int checks=HydrologyTopologyTests.Run(verify:false,includeWorlds:false,algorithm:3,compareLocality:true);
        void Check(bool ok,string why){if(!ok)throw new Exception("LOCALITY: "+why);checks++;}
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
        var d=Domain(new[]{8d,12d,16d});var profile=AdaptiveBasinTopology.ProfileLocality(d,recipe,6,5);
        var plain=AdaptiveBasinTopology.ProfileGraph(d,recipe,6,5,reuse:true);
        Check(Hash(profile.Result)==Hash(plain.Result) && Hash(profile.Graph)==Hash(plain.Graph),"solver and graph observations unchanged");
        Check(profile.Locality.Complete && profile.Locality.Corrections.Count==profile.Graph.Passes.Sum(p=>p.CorrectedLeaves),"every correction observed including balancing");
        Check(profile.Locality.Corrections.Count>1 && profile.Locality.Corrections.Any(c=>c.NewlyAffected>0),"real descendant invalidation observed");
        Check(Hash(profile)==Hash(AdaptiveBasinTopology.ProfileLocality(d,recipe,6,5)),"repeat profile deterministic");
        var spill=AdaptiveBasinTopology.ProfileLocality(Domain(new[]{16d}),recipe,6,5);
        Check(spill.Locality.Corrections.Any(c=>c.Phase=="spill"),"spill correction counted without spill dependency reuse");
        foreach(var cap in new[]{(0,32000000L,4096),(524288,0L,4096),(524288,32000000L,0),(6,32000000L,4096),(524288,30L,4096),(524288,32000000L,1)})
        {
            var limited=AdaptiveBasinTopology.ProfileLocality(d,recipe,6,5,nodeLimit:cap.Item1,operationLimit:cap.Item2,correctionLimit:cap.Item3);
            Check(!limited.Locality.Complete && limited.Locality.Truncation!=null,"diagnostic cap explicit");
            Check(Hash(limited.Result)==Hash(plain.Result),"diagnostic exhaustion never changes solver result");
        }
        foreach(string moment in new[]{"correction","descendant","boundary"})
        {
            using var cancel=new CancellationTokenSource();bool reached=false,cancelled=false;
            try{AdaptiveBasinTopology.ProfileLocality(d,recipe,6,5,cancel.Token,observer:e=>{if(e==moment){reached=true;cancel.Cancel();}});}
            catch(OperationCanceledException){cancelled=true;}
            Check(reached && cancelled,"cancellation during "+moment);
        }
        var parallel=Task.WhenAll(Enumerable.Range(0,3).Select(_=>Task.Run(()=>Hash(AdaptiveBasinTopology.ProfileLocality(d,recipe,6,5))))).GetAwaiter().GetResult();
        Check(parallel.All(h=>h==Hash(profile)),"concurrent profiles deterministic");
        checks+=Model();
        Directory.CreateDirectory("artifacts/hydrology-locality");File.WriteAllText("artifacts/hydrology-locality/analytic.json",JsonSerializer.Serialize(new{profile,spill},Json));
        Console.WriteLine($"LOCALITY_PASS: {checks} checks");return checks;
    }
    private static int Model()
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("LOCALITY_MODEL: "+why);checks++;}
        var keys=new[]{new HydrologyCell(0,0,8),new(8,0,8),new(16,0,8),new(24,0,8),new(0,8,8),new(8,8,8),new(16,8,8)};
        bool Face(HydrologyCell a,HydrologyCell b)=>
            ((a.X+a.Size==b.X || b.X+b.Size==a.X) && Math.Min(a.Z+a.Size,b.Z+b.Size)>Math.Max(a.Z,b.Z)) ||
            ((a.Z+a.Size==b.Z || b.Z+b.Size==a.Z) && Math.Min(a.X+a.Size,b.X+b.Size)>Math.Max(a.X,b.X));
        var retired=new HashSet<HydrologyCell>();bool missing=false,invalid=false;
        IEnumerable<LocalityFace> Faces(HydrologyCell from)
        {
            if(missing){yield return new(null,false,false,1,0);yield break;}
            foreach(var to in keys.Where(k=>!retired.Contains(k) && Face(from,k)))
                yield return new(to,!retired.Contains(from) && !invalid,true,1,0);
        }
        var graph=new HydrologyGraphProfile(Array.Empty<HydrologyGraphPass>(),0,0,0);
        var collector=new HydrologyLocalityCollector(1,Faces,CancellationToken.None);
        void Seed()
        {
            collector.BeginPass();collector.Discover(keys[0],null);
            foreach(var (child,parent) in new[]{(1,0),(2,1),(3,2),(4,0),(5,4),(6,5)})collector.Discover(keys[child],keys[parent]);
        }
        Seed();retired.Add(keys[1]);collector.Corrected(keys[1],0,1,false,false);var c=collector.Finish(graph).Corrections.Last();
        Check(c.DiscoveredBefore==7 && c.DirectChildren==1 && c.Descendants==2 && c.NewlyAffected==3,"exact transitive closure");
        Check(c.SurvivingBoundaryCells==3 && c.BoundaryFaces==3,"all positive boundary faces counted");
        Check(c.AlternateValidFaces==1 && c.ReconnectableOrphanCells==1,"unchanged alternate face into orphan is included");
        Check(c.DescendantDistance.MaximumMetres==16 && c.AlternateFaceDistance.Count==1,"distances measured");
        Check(Math.Abs(c.AffectedPercent-300d/7)<1e-9,"affected percentage");
        collector.Corrected(keys[2],0,1,false,true);c=collector.Finish(graph).Corrections.Last();
        Check(c.PreviouslyRemoved && c.NewlyAffected==0,"cascade does not double-count removed descendants");
        retired.Clear();Seed();retired.Add(keys[0]);collector.Corrected(keys[0],0,2,false,false);c=collector.Finish(graph).Corrections.Last();
        Check(c.NewlyAffected==7 && c.AffectedPercent==100 && c.BoundaryFaces==0,"seed retirement invalidates entire component");
        retired.Clear();Seed();invalid=true;retired.Add(keys[1]);collector.Corrected(keys[1],0,3,false,false);c=collector.Finish(graph).Corrections.Last();
        Check(c.AlternateValidFaces==0 && c.BoundaryFaces>0,"invalid faces cannot prove reconnection");
        invalid=false;missing=true;Seed();collector.Corrected(keys[1],0,4,false,false);c=collector.Finish(graph).Corrections.Last();
        Check(c.UnknownSegments==3 && c.AlternateValidFaces==0,"missing roots remain unknown");
        missing=false;retired.Clear();Seed();collector.Corrected(new(24,8,8),0,5,true,false);c=collector.Finish(graph).Corrections.Last();
        Check(!c.DiscoveryDependency && c.NewlyAffected==0 && c.SurvivingBoundaryCells==2 && c.EstimatedLocalOperations>0,"outside-tree correction has nonzero boundary work");
        retired.Clear();Seed();retired.Add(keys[3]);collector.Corrected(keys[3],0,6,false,false);c=collector.Finish(graph).Corrections.Last();
        Check(c.Descendants==0 && c.NewlyAffected==1 && c.BoundaryFaces==1 && c.EstimatedLocalOperations>0,"leaf with zero descendants still needs boundary work");
        var final=collector.Finish(graph);
        Check(final.PeakNodes==7 && final.PeakReverseEdges==6 && final.UniqueDiscoveredNodes==7,"index counts and unique admissions");
        Check(final.EstimatedIndexBytes>7*24,"storage includes reverse links and scratch");
        Console.WriteLine($"LOCALITY_MODEL_PASS: {checks} checks");return checks;
    }
    public static int Measure()
    {
        int checks=0;void Check(bool ok,string why){if(!ok)throw new Exception("LOCALITY_MATRIX: "+why);checks++;}
        var rows=new List<object>();
        var worlds=JsonSerializer.Deserialize<AcceptedWorldTests.TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
        using var frozen=JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/hydrology-work-coverage-v1.json")));
        using var archived=JsonDocument.Parse(File.ReadAllText("docs/experiments/hydrology-reachability-v4-comparison.json"));
        foreach(var group in new[]{(World:"mountainous/42",Sizes:new[]{"large","small"}),
            (World:"mountainous/42/far",Sizes:new[]{"large"}),(World:"vast-world/7319/far",Sizes:new[]{"small","large","medium"})})
        {
            var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(worlds.Cases.Single(c=>c.Name==group.World).World));
            foreach(string size in group.Sizes)
            {
                var expected=frozen.RootElement.EnumerateArray().Single(r=>r.GetProperty("World").GetString()==group.World && r.GetProperty("Size").GetString()==size && r.GetProperty("Algorithm").GetInt32()==3);
                int patch=expected.GetProperty("Patch").GetInt32();
                var recipe=new HydrologyTopologyRecipe(1,3,new(1,1,2,regional.Recipe),3,2,64,.25,new(32,2000000,800000,2000000,16384,180000));
                var domain=HydrologyReconciler.Create(regional).CreateDomain(patch,CancellationToken.None);
                var clock=Stopwatch.StartNew();var plain=AdaptiveBasinTopology.Run(domain,recipe,patch,null);clock.Stop();double plainMs=clock.Elapsed.TotalMilliseconds;
                domain=HydrologyReconciler.Create(regional).CreateDomain(patch,CancellationToken.None);
                clock.Restart();var profile=AdaptiveBasinTopology.ProfileLocality(domain,recipe,patch,null);clock.Stop();
                Check(Hash(plain)==expected.GetProperty("Fingerprint").GetString() && Hash(profile.Result)==Hash(plain),"full frozen result unchanged");
                Check(profile.Locality.Complete,"complete bounded observation");
                Check(profile.Locality.Corrections.Count==profile.Graph.Passes.Sum(p=>p.CorrectedLeaves),"every correction represented");
                var baseline=archived.RootElement.EnumerateArray().Single(r=>r.GetProperty("World").GetString()==group.World && r.GetProperty("Size").GetString()==size && r.GetProperty("Mode").GetInt32()==1);
                var rejected=archived.RootElement.EnumerateArray().Single(r=>r.GetProperty("World").GetString()==group.World && r.GetProperty("Size").GetString()==size && r.GetProperty("Mode").GetInt32()==2);
                Check(Hash(profile.Graph)==Hash(baseline.GetProperty("Graph")),"graph observations unchanged from reviewed baseline");
                var c=profile.Locality.Corrections;var percentages=c.Select(x=>x.AffectedPercent).Order().ToArray();
                long work=c.Sum(x=>x.EstimatedLocalOperations),replay=profile.Graph.Passes.Where(p=>p.Pass>1).Sum(p=>(long)p.WetVisits);
                var summary=new{Corrections=c.Count,OutsideDiscovery=c.Count(x=>!x.DiscoveryDependency),Descendants=c.Sum(x=>x.Descendants),PeakDescendants=c.Max(x=>x.Descendants),NewlyAffected=c.Sum(x=>x.NewlyAffected),
                    MeanAffectedPercent=percentages.Average(),MedianAffectedPercent=(percentages[(percentages.Length-1)/2]+percentages[percentages.Length/2])*.5,MaxAffectedPercent=percentages.Max(),
                    BoundaryCells=c.Sum(x=>x.SurvivingBoundaryCells),BoundaryFaces=c.Sum(x=>x.BoundaryFaces),
                    AffectedOnlyOperations=work,FullComponentReplayVisits=replay,RepeatedWetVisits=profile.Graph.Passes.Sum(p=>p.RepeatedWetVisits),
                    LocalToReplayRatio=replay==0?0:(double)work/replay,RejectedWholeTreeChecks=rejected.GetProperty("Reachability").GetProperty("RepairWork").GetInt64(),
                    CheapCorrections=c.Count(x=>x.AffectedPercent<=10 && x.EstimatedLocalOperations<.1*x.ReplayWetVisits && x.UnknownSegments==0),
                    MajorityAffectedCorrections=c.Count(x=>x.AffectedPercent>=50),AlternateClosureCorrections=c.Count(x=>x.AlternateValidFaces>0),
                    AlternateFaces=c.Sum(x=>x.AlternateValidFaces),UnknownSegments=c.Sum(x=>x.UnknownSegments),UntrackedWetFaces=c.Sum(x=>x.UntrackedWetFaces)};
                rows.Add(new{group.World,Size=size,Patch=patch,PlainMilliseconds=plainMs,ProfileMilliseconds=clock.Elapsed.TotalMilliseconds,
                    Fingerprint=Hash(profile.Result),profile.Result.Metrics,profile.Result.ComponentStop,profile.Result.SpillStop,
                    ComponentCells=profile.Result.ComponentCells.Count,Frontiers=profile.Result.Frontier.GroupBy(f=>f.Reason).ToDictionary(g=>g.Key,g=>g.Count()),
                    Summary=summary,profile.Locality,profile.Graph});
                Directory.CreateDirectory("artifacts/hydrology-locality");File.WriteAllText("artifacts/hydrology-locality/matrix.json",JsonSerializer.Serialize(rows,Json));
                Console.WriteLine($"LOCALITY {group.World}/{size}: corrections {c.Count}, affected {summary.NewlyAffected}, alternate {summary.AlternateClosureCorrections}, local ops {work} / replay {replay}, index {profile.Locality.EstimatedIndexBytes} bytes");
            }
        }
        Console.WriteLine($"LOCALITY_MATRIX_PASS: {checks} checks; {rows.Count} queries with separate controls");return checks;
    }
}
