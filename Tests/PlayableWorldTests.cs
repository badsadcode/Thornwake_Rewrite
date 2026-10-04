using System.Numerics;
using System.Text.Json;
using Thornwake.Core;

internal static class PlayableWorldTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool ok,string reason){if(!ok)throw new Exception("PLAYABLE WORLD: "+reason);checks++;}
        void Reject(Action action,string reason){try{action();}catch(ArgumentException){checks++;return;}throw new Exception(reason);}
        var root=new DirectoryInfo(AppContext.BaseDirectory);
        while(root!=null && !File.Exists(Path.Combine(root.FullName,"project.godot")))root=root.Parent;
        if(root==null)throw new Exception("Project settings not found for renderer contract check.");
        string project=File.ReadAllText(Path.Combine(root.FullName,"project.godot"));
        Check(project.Contains("renderer/rendering_method=\"forward_plus\""),"desktop Forward+ must remain the primary renderer");
        Check(project.Contains("\"Forward Plus\"") && project.Contains("run/main_scene=\"res://Game/Main.tscn\""),"renderer metadata and preserved gameplay entry point");
        foreach(string file in Directory.GetFiles(Path.Combine(root.FullName,"Game","Development"),"Playable*"))
        {new System.Text.UTF8Encoding(false,true).GetString(File.ReadAllBytes(file));checks++;}
        var profiles=MacroProfileCatalog.Load(Path.Combine(AppContext.BaseDirectory,"Content","world_profiles.json"));
        foreach(var config in new[]{("mountainous",42), ("vast-world",7319)})
        foreach(bool far in new[]{false,true})
        {
            var manifest=MacroWorldManifest.Create(config.Item2,profiles.Single(p=>p.Id==config.Item1),originX:far?8e9:0,originZ:far?-8e9:0,algorithm:4);
            var world=new MacroWorldGenerator(manifest);var restore=new MacroWorldGenerator(JsonSerializer.Deserialize<MacroWorldManifest>(JsonSerializer.Serialize(manifest))!);
            var location=world.LandFeatures[0].Center;var position=PlayableWorldPolicy.Teleport(world,location.X,location.Z);var tile=PlayableWorldPolicy.TileAt(position);
            var a=PlayableTerrainPatch.Build(world,tile);var b=PlayableTerrainPatch.Build(world,new(tile.X+1,tile.Z));
            var c=PlayableTerrainPatch.Build(world,new(tile.X,tile.Z+1),detailed:false);var replay=PlayableTerrainPatch.Build(restore,tile);
            Check(a.Heights.SequenceEqual(replay.Heights)&&a.Normals.SequenceEqual(replay.Normals)&&a.Indices.SequenceEqual(replay.Indices),"A4 manifest reload / mesh reconstruction");
            int side=PlayableTerrainPatch.Side;
            for(int i=0;i<side;i++)
            {
                Check(a.Heights[i*side+side-1]==b.Heights[i*side],"east/west detailed seam");
                Check(a.Normals[i*side+side-1]==b.Normals[i*side],"east/west normals");
                Check(a.Heights[(side-1)*side+i]==c.Heights[i],"fine/coarse north/south seam");
                Check(a.Normals[(side-1)*side+i]==c.Normals[i],"fine/coarse normals");
            }
            foreach(var patch in new[]{a,c})
            {
                Check(patch.Indices.Length==(patch.Detailed?6144:384),"bounded geometry for both details");
                for(int i=0;i<patch.Indices.Length;i+=3)
                {
                    var v=patch.Indices.Skip(i).Take(3).Select(k=>patch.Vertices[k]).ToArray();
                    Check(Vector3.Cross(v[1]-v[0],v[2]-v[0]).Y<0,"Godot clockwise nondegenerate surface winding");
                }
                foreach(var v in patch.SeaVertices)Check(world.Sample(patch.Anchor.X+v.X,patch.Anchor.Z+v.Z).LandInfluence<=0,"no unresolved candidate can become sea");
            }
            var origin=PlayableWorldPolicy.OriginFor(position);var local=position.RelativeTo(origin);var returned=PlayableWorldPolicy.FromLocal(local,origin);
            Check(Math.Abs(returned.X-position.X)<.0001 && Math.Abs(returned.Z-position.Z)<.0001,"far teleport subtracts double before float");
            double initial=position.X;
            for(int i=0;i<4000;i++)
            {
                position=position with{X=position.X+.125};if(PlayableWorldPolicy.NeedsRebase(position,origin))origin=PlayableWorldPolicy.OriginFor(position);
                var again=PlayableWorldPolicy.FromLocal(position.RelativeTo(origin),origin);Check(Math.Abs(again.X-position.X)<.0001,"repeated origin roundtrip");
            }
            Check(position.X==initial+500,"exact binary-fraction global movement no accumulated rebase drift");
            Check(!PlayableWorldPolicy.Accept(1,2,tile,position),"world-switch stale worker rejected");
            Check(!PlayableWorldPolicy.Accept(1,1,new(tile.X+1000,tile.Z),position),"distant teleport rejects obsolete worker");
            Check(PlayableWorldPolicy.Accept(2,2,PlayableWorldPolicy.TileAt(position),position),"current worker admitted");
            using var cancel=new CancellationTokenSource();cancel.Cancel();
            try{PlayableTerrainPatch.Build(world,tile,cancel.Token);throw new Exception("Cancellation ignored");}catch(OperationCanceledException){checks++;}
        }
        var coastalWorld=new MacroWorldGenerator(MacroWorldManifest.Create(42,profiles.Single(p=>p.Id=="mountainous"),algorithm:4));
        var land=coastalWorld.LandFeatures[0];double lo=0,hi=coastalWorld.Manifest.Profile.WorldSize;
        for(int i=0;i<48;i++){double mid=(lo+hi)*.5;if(coastalWorld.Sample(land.Center.X+mid,land.Center.Z).LandInfluence>0)lo=mid;else hi=mid;}
        var shoreTile=PlayableWorldPolicy.TileAt(new(land.Center.X+lo,0,land.Center.Z));
        foreach(bool detailed in new[]{false,true})
        {
            var shore=PlayableTerrainPatch.Build(coastalWorld,shoreTile,detailed:detailed);
            Check(shore.SeaVertices.Length>0,"real A4 coast has a representative surface");
            if(detailed)Check(shore.SeaVertices.Any(v=>Math.Pow(shore.Anchor.X+v.X-(land.Center.X+lo),2)+Math.Pow(shore.Anchor.Z+v.Z-land.Center.Z,2)<24*24),"shallow ocean shelf is not dried by a rendering depth cutoff");
            foreach(var v in shore.SeaVertices)
            {
                Check(coastalWorld.Sample(shore.Anchor.X+v.X,shore.Anchor.Z+v.Z).LandInfluence<=0,"shore clip stays in sampled ocean context");
                Check(Math.Abs(v.Y+shore.Anchor.Y-coastalWorld.Manifest.Profile.SeaLevel)<.0001,"static sea at declared level");
            }
        }
        var loaded=new HashSet<WorldRegion>();
        for(int step=0;step<120;step++)
        {
            var center=new WorldRegion(step<60?step:1000000+step,-step);
            var plan=RegionStreaming.Plan(loaded.ToArray(),new[]{center},PlayableWorldPolicy.Radius,PlayableWorldPolicy.RetainRadius,256);
            foreach(var t in plan.Unload)loaded.Remove(t);foreach(var t in plan.Load)loaded.Add(t);
            Check(loaded.Count<=PlayableWorldPolicy.MaximumTiles && plan.Load.Count<=256,"fixed residency after traversal/continental teleport");
        }
        Check(PlayableWorldPolicy.TileAt(new(-.01,0,-256)).Equals(new WorldRegion(-1,-1)),"negative and exact tile boundaries");
        Check(!PlayableWorldPolicy.CanTraverse(new(1,0,1),new(800,0,1),t=>t.X!=1),"sweep detects missing intermediate tile");
        Check(!PlayableWorldPolicy.CanTraverse(new(255.8,0,8),new(255.8,0,8),t=>t.X==0),"capsule guard straddles missing neighbour");
        Check(PlayableWorldPolicy.CanTraverse(new(1,0,1),new(800,0,1),_=>true),"prepared collision sweep accepted");
        Reject(()=>PlayableWorldPolicy.TileAt(new(double.NaN,0,0)),"NaN teleport rejected");
        Reject(()=>PlayableWorldPolicy.FromLocal(new(0,float.PositiveInfinity,0),new()),"invalid local physics rejected");
        var down=new InspectedCorridor("fixture-reach",new[]{"link"},new[]{new CorridorPoint(0,0,10),new CorridorPoint(64,0,9)},false,new(CorridorCompatibility.DownhillAtSampleSpacing,0,10,null));
        double Valley(double x,double z)=>10-x*.01+Math.Abs(z)*.1;
        var window=PlayableWaterPresentation.InspectReach(down,Valley);
        Check(window.Status==LabWaterStatus.ProvisionalReach && window.Left.Count==33,"bounded checked trough allows provisional window only");
        Check(PlayableWaterPresentation.InspectReach(down,(_,_)=>10).Status==LabWaterStatus.Unresolved,"flat plane without banks is unresolved");
        Check(PlayableWaterPresentation.InspectReach(down,(x,z)=>10+x*.1+Math.Abs(z)*.1).Status==LabWaterStatus.Unresolved,"fine uphill rejects coarse downhill proposal");
        Check(PlayableWaterPresentation.InspectReach(down with{Assessment=new(CorridorCompatibility.SubmergedAtCandidateStage,0,10,11)},Valley).Status==LabWaterStatus.Unresolved,"hypothetical candidate lake submergence never becomes confirmed water");
        Check(PlayableWaterPresentation.InspectReach(down with{Assessment=new(CorridorCompatibility.TerrainBarrier,1,10,null)},Valley).Status==LabWaterStatus.Unresolved,"barrier remains uncertainty");
        Check(PlayableWaterPresentation.InspectReach(down,(x,z)=>z==0?Valley(x,z):double.NaN).Status==LabWaterStatus.Unresolved,"invalid cross-section evidence fails closed");
        Console.WriteLine($"PLAYABLE_WORLD_CORE_PASS: {checks} checks (A4, mixed-detail seams, reconstruction, large coordinates, admission, water status)");return checks;
    }
}
