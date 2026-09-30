using System.Text.Json;
using System.Security.Cryptography;
using Thornwake.Core;

internal static class HydrologyInspectionTests
{
    private sealed record Case(string World,string Kind,int Cell,string Fingerprint);
    private sealed record PairCase(string World,int First,int Second,string Fingerprint);
    private sealed record Fixture(int Algorithm,int Factor,Case[] Cases,PairCase[] Pairs);
    private static string Hash<T>(T item)=>Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(item))).ToLowerInvariant();
    private static string Serialize<T>(T item)=>JsonSerializer.Serialize(item);
    private static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
    public static int Run(bool record=false)
    {
        int checks=0;void Check(bool value,string why){if(!value)throw new Exception("HYDROLOGY_INSPECTION: "+why);checks++;}
        void Reject(Action action){try{action();}catch(Exception e)when(e is ArgumentException or InvalidDataException or OperationCanceledException){checks++;return;}throw new Exception("HYDROLOGY_INSPECTION: invalid input accepted");}
        CorridorPoint P(double x,double z,double y)=>new(x,z,y);
        RegionalCorridorLink L(string id,CorridorPoint a,CorridorPoint b)=>new(id,a,b,true,true);
        var straight=new[]{L("a",P(0,0,10),P(2,0,8)),L("b",P(2,0,8),P(4,0,6))};
        var down=RegionalHydrologyInspector.RefineCorridors(straight,p=>10-p.X,4,null,"analytic");
        Check(down.Count==1 && down[0].Assessment.Compatibility==CorridorCompatibility.DownhillAtSampleSpacing,"clearly downhill route");
        Check(down[0].Points[0]==straight[0].From && down[0].Points[^1]==straight[^1].To,"continuation endpoints retained");
        // The coarse endpoints descend, but a narrow ridge between them blocks the finer proposed corridor.
        double Ridge(MacroPoint p)=>Math.Abs(p.X-1)<.2?20:10-p.X;
        var blocked=RegionalHydrologyInspector.RefineCorridors(straight,Ridge,4,null,"analytic");
        Check(blocked[0].Assessment.Compatibility==CorridorCompatibility.TerrainBarrier && blocked[0].Assessment.RequiredStage==20,"finer ridge contradiction exposed");
        Check(RegionalHydrologyInspector.Assess(new[]{P(0,0,2),P(1,0,5),P(2,0,1)},6).Compatibility==CorridorCompatibility.SubmergedAtCandidateStage,"submerged rise is not a channel barrier");
        Check(RegionalHydrologyInspector.Assess(new[]{P(0,0,2),P(1,0,5),P(2,0,1)},5).Compatibility==CorridorCompatibility.TerrainBarrier,"a dry sill at candidate stage remains blocked");
        double[] bowl={12,12,12,12,12, 12,2,5,1,0, 12,12,12,12,12};
        var spill=RegionalHydrologyInspector.InspectSpill(new DrainageGrid(0,0,1,5,3,bowl),new[]{9},6,"analytic-basin");
        Check(spill.Level==5 && spill.Exit==9 && spill.Level!=3,"refined minimax spill differs from coarse candidate 3");
        var farSpill=RegionalHydrologyInspector.InspectSpill(new DrainageGrid(8e9,-7e9,1,5,3,bowl),new[]{9},6,"analytic-basin");
        Check(farSpill==spill,"analytic far-origin sill invariant");
        // Cheapest face minimum need not be reachable at that height: the interior sill still controls escape.
        Check(spill.Level>bowl[9],"cannot mistake face minimum for accessible whole-basin spill");
        var junction=P(0,0,10);
        var branches=new[]{L("a",P(-2,0,12),junction),L("b",P(0,-2,12),junction),L("c",P(2,0,12),junction),L("d",junction,P(0,2,8)),L("e",P(0,2,8),P(0,4,6))};
        double Slope(MacroPoint p)=>10+Math.Max(Math.Abs(p.X),-p.Z)-Math.Max(0,p.Z);
        var merge=RegionalHydrologyInspector.RefineCorridors(branches,Slope,4,null,"analytic");
        Check(merge.Count(c=>c.Points[^1]==junction)==3 && merge.Count(c=>c.Points[0]==junction)==1,"three tributaries retain one exact junction and shared trunk");
        Check(merge.SelectMany(c=>c.SourceLinks).Order().SequenceEqual(branches.Select(l=>l.Id).Order()),"each source link retained exactly once");
        Check(Serialize(merge)==Serialize(RegionalHydrologyInspector.RefineCorridors(branches.Reverse().ToArray(),Slope,4,null,"analytic")),"query order cannot change smoothing groups or identity");
        var corner=new[]{L("a",P(0,0,10),P(2,0,8)),L("b",P(2,0,8),P(2,2,6))};
        var smooth=RegionalHydrologyInspector.RefineCorridors(corner,p=>10-p.X-p.Z,4,null,"analytic");
        Check(smooth[0].CornerRefined && smooth[0].Points.Any(p=>p.X<2 && p.Z>0),"guarded curve removes a grid corner on compatible terrain");
        var unsafeCurve=RegionalHydrologyInspector.RefineCorridors(corner,p=>p.X<1.99 && p.Z>0.01?100:10-p.X-p.Z,4,null,"analytic");
        Check(!unsafeCurve[0].CornerRefined && unsafeCurve[0].Points.All(p=>p.BedElevation<100),"reject smoothing that cuts through a newly sampled ridge");
        var a=new double[25];var b=new double[25];for(int x=0;x<5;x++){a[2*5+x]=10;b[2*5+x]=10;}
        var connection=RegionalHydrologyInspector.ConnectPatches(a,b,4,true,5);
        Check(connection.FirstComponents==2 && connection.SecondComponents==2 && connection.JoinedComponents==2 && connection.Links.Count==2,"four local components join as two candidate pools across shared face");
        var overtopped=RegionalHydrologyInspector.ConnectPatches(a,b,4,true,11);
        Check(overtopped.JoinedComponents==1,"changed hypothetical stage connects previously separated pools");
        for(int z=0;z<5;z++){a[z*5+4]=10;b[z*5]=10;}
        var sealedFace=RegionalHydrologyInspector.ConnectPatches(a,b,4,true,5);
        Check(sealedFace.Links.Count==0 && sealedFace.JoinedComponents==4,"intact face blocks exchange despite shared candidate-lake identity");
        // Wet interiors meet only through the shared face; the paired external boundary is sealed.
        var sealedA=Enumerable.Repeat(10d,25).ToArray();var sealedB=Enumerable.Repeat(10d,25).ToArray();
        for(int z=1;z<4;z++)for(int x=1;x<5;x++)sealedA[z*5+x]=0;
        for(int z=1;z<4;z++)for(int x=0;x<4;x++)sealedB[z*5+x]=0;
        Check(!RegionalHydrologyInspector.ConnectPatches(sealedA,sealedB,4,true,5).OpenBoundary,"shared face is not an unresolved outer boundary");
        double[] Transpose(double[] values)=>Enumerable.Range(0,25).Select(i=>values[(i%5)*5+i/5]).ToArray();
        Check(Serialize(RegionalHydrologyInspector.ConnectPatches(Transpose(sealedA),Transpose(sealedB),4,false,5))==
            Serialize(RegionalHydrologyInspector.ConnectPatches(sealedA,sealedB,4,true,5)),"north/south faces have identical sealed connectivity semantics");
        Check(RegionalHydrologyInspector.ConnectPatches(Transpose(a),Transpose(b),4,false,5).Links.Count==0,"north/south intact ridge blocks exchange");
        var empty=Enumerable.Repeat(10d,25).ToArray();
        Check(RegionalHydrologyInspector.ConnectPatches(empty,empty,4,true,5).JoinedComponents==0,"dry patch pair creates no lake");
        // Two wet quads sharing only a single low vertex remain separate conservative components.
        var cornerTouch=Enumerable.Repeat(10d,25).ToArray();
        foreach(int i in new[]{0,1,5,6,7,11,12})cornerTouch[i]=0;
        Check(RegionalHydrologyInspector.ConnectPatches(cornerTouch,empty,4,true,5).FirstComponents==2,"corner-only contact is not a hydraulic connection");
        Reject(()=>RegionalHydrologyInspector.RefineCorridors(Array.Empty<RegionalCorridorLink>(),p=>0,2,double.NaN,"invalid-stage"));
        b[0]=999;Reject(()=>RegionalHydrologyInspector.ConnectPatches(a,b,4,true,5));
        Reject(()=>RegionalHydrologyInspector.Assess(new[]{P(0,0,double.NaN),P(1,0,0)}));
        Reject(()=>RegionalHydrologyInspector.RefineCorridors(straight,p=>0,16,null,"invalid"));
        Reject(()=>RegionalHydrologyInspector.RefineCorridors(straight.Concat(straight).ToArray(),p=>0,2,null,"duplicate"));
        Reject(()=>RegionalHydrologyInspector.InspectSpill(new DrainageGrid(0,0,1,5,3,bowl),Array.Empty<int>(),6,"no-exits"));

        string fixturePath=record?"Tests/Fixtures/hydrology-inspection-v1.json":Path.Combine(AppContext.BaseDirectory,"Fixtures/hydrology-inspection-v1.json");
        if(record && File.Exists(fixturePath))throw new IOException("Refusing to overwrite hydrology acceptance fixtures.");
        Fixture? expected=record?null:JsonSerializer.Deserialize<Fixture>(File.ReadAllText(fixturePath))!;
        if(expected!=null)Check(expected.Algorithm==1 && expected.Factor==2,"inspection version pinned");
        var terrains=JsonSerializer.Deserialize<AcceptedWorldTests.TerrainFixture>(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Fixtures/terrain-a4-v1.json")))!;
        var cases=new List<Case>();var pairs=new List<PairCase>();var reports=new List<object>();
        foreach(string name in new[]{"mountainous/42","mountainous/42/far","vast-world/7319/far"})
        {
            var terrain=terrains.Cases.Single(c=>c.Name==name);var world=new MacroWorldGenerator(terrain.World);
            var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(terrain.World));var inspector=RegionalHydrologyInspector.Create(regional);
            var restored=RegionalHydrologyInspector.Restore(JsonSerializer.Deserialize<HydrologyInspectionRecipe>(Serialize(inspector.Recipe))!);
            var d=regional.Drainage;var belt=world.Ranges[0];var target=new MacroPoint(belt.Points[8].X+belt.Width*.6,belt.Points[8].Z);
            Check(world.Sample(target.X,target.Z).MountainInfluence>0,"selected A4 foothill/pass field");
            Check(d.Grid.TryCell(target.X,target.Z,out int foothill),"foothill cell in world");
            var basin=d.Basins.OrderByDescending(v=>v.CapacityEstimate).First(v=>v.Cells.Any(c=>v.Cells.Contains(c+1) && c%d.Grid.Width<d.Grid.Width-1));
            int first=basin.Cells.First(c=>basin.Cells.Contains(c+1) && c%d.Grid.Width<d.Grid.Width-1),second=first+1;
            foreach(var selection in new[]{(Kind:"foothill-pass",Cell:foothill),(Kind:"basin-outlet",Cell:basin.Exits[0].From)})
            {
                var watch=System.Diagnostics.Stopwatch.StartNew();var result=inspector.Inspect(selection.Cell);watch.Stop();var original=regional.Build(selection.Cell);
                Check(Serialize(result)==Serialize(restored.Inspect(selection.Cell)),"inspection frozen recipe reconstruction including far origin");
                string hash=Hash(result);cases.Add(new(name,selection.Kind,selection.Cell,hash));
                if(expected!=null)Check(expected.Cases.Single(c=>c.World==name && c.Kind==selection.Kind).Fingerprint==hash,"fixed inspection fingerprint");
                Check(result.Corridors.SelectMany(c=>c.SourceLinks).Order().SequenceEqual(original.Links.Select(l=>l.Id).Order()),"all original network links accounted for");
                Check(result.Corridors.Select(c=>c.Id).Distinct().Count()==result.Corridors.Count,"stable unique refined segments");
                foreach(var corridor in result.Corridors)
                {
                    Check(corridor.Points.Count<=9,"bounded per-segment profile");
                    var source=original.Links.Single(l=>l.Id==corridor.SourceLinks[0]);var end=original.Links.Single(l=>l.Id==corridor.SourceLinks[^1]);
                    Check(corridor.Points[0]==source.From && corridor.Points[^1]==end.To,"junction and handoff anchors preserved");
                    foreach(var point in corridor.Points)Check(point.BedElevation==world.Sample(point.X,point.Z).Elevation,"finer profile samples real unmodified A4 terrain");
                }
                if(result.Outlet is { } outlet)
                {
                    var neighbour=regional.Build(outlet.OriginalPort.ToCell);
                    Check(neighbour.Incoming.Contains(outlet.OriginalPort),"original shared continuation exact");
                    Check(outlet.FaceSamples.Count==127 && outlet.FaceMinimum.BedElevation<=outlet.OriginalPort.BedElevation,"denser canonical sill profile retains original sampling subset");
                    Check(outlet.LocalSpill>=outlet.LocalLowPoint.BedElevation && outlet.LocalSpill>=outlet.ReachableCrossing.BedElevation,"minimax spill includes internal barriers");
                    Check(outlet.FaceSamples.Contains(outlet.ReachableCrossing),"spill goes only to declared downstream face");
                }
                reports.Add(new{name,selection.Kind,selection.Cell,milliseconds=watch.Elapsed.TotalMilliseconds,result});
            }
            var pair=inspector.InspectLakePair(first,second);
            Check(Serialize(pair)==Serialize(restored.InspectLakePair(second,first)),"pair order and reconstruction independent");
            string pairHash=Hash(pair);pairs.Add(new(name,first,second,pairHash));
            if(expected!=null)Check(expected.Pairs.Single(p=>p.World==name).Fingerprint==pairHash,"fixed cross-patch connectivity fingerprint");
            Check(pair.CandidateLakeId==regional.LakeId(d.BasinIndex[first]) && pair.JoinedComponents<=pair.FirstComponents+pair.SecondComponents,"conditional shared lake identity and component union");
            Reject(()=>RegionalHydrologyInspector.Restore(inspector.Recipe with { Factor=8 }));
            Reject(()=>RegionalHydrologyInspector.Restore(inspector.Recipe with { Regional=regional.Recipe with { Coarse=regional.Recipe.Coarse with { World=terrain.World with { Algorithm=3 } } } }));
            Reject(()=>inspector.Inspect(foothill,new CancellationToken(true)));
            Reject(()=>inspector.InspectLakePair(first,first));
            var concurrent=Task.WhenAll(Task.Run(()=>Serialize(inspector.Inspect(foothill))),Task.Run(()=>Serialize(inspector.Inspect(foothill)))).GetAwaiter().GetResult();
            Check(concurrent[0]==concurrent[1],"parallel requests deterministic");reports.Add(new{name,pair});
        }
        if(record)File.WriteAllText(fixturePath,JsonSerializer.Serialize(new Fixture(1,2,cases.ToArray(),pairs.ToArray()),Json));
        Directory.CreateDirectory("artifacts/a4-acceptance");File.WriteAllText("artifacts/a4-acceptance/hydrology-inspection.json",JsonSerializer.Serialize(new{checks,reports},Json));
        Console.WriteLine($"HYDROLOGY_INSPECTION_PASS: {checks} checks; downhill, barrier, submerged rise, revised spill, tributaries, shared patches, far origins and A4 foothills");return checks;
    }
}
