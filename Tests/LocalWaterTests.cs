using System.Numerics;
using Thornwake.Core;

internal static class LocalWaterTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool ok,string why) { if(!ok) throw new Exception("FLOW: "+why); checks++; }
        // One-metre-wide descending trench with a bank at x=2. Includes an isolated lower chamber.
        float Bed(float x)=>x<2?3:Math.Max(0,3-(x-2)*.65f);
        float Density(Vector3 p,bool breached)
        {
            float solid=Bed(p.X)-p.Y;
            if(!breached && p.X>1.9f && p.X<2.1f) solid=Math.Max(solid,5-p.Y);
            if(p.Z<0 || p.Z>1) solid=10-p.Y;
            return solid;
        }
        LocalWaterTopology Build(bool breached,bool source=true,bool drain=true)=>LocalWaterTopology.Build(p=>Density(p,breached),0,0,8,1,-1,6,
            source?new[]{new WaterSource("lake",.5f,.5f,4,2)}:Array.Empty<WaterSource>(),
            outlets:drain?new[]{new WaterOutlet(7,0,0,8)}:Array.Empty<WaterOutlet>());
        var before=new LocalWaterSimulation(Build(false));
        Check(before.Volume(7)==0,"intact bank keeps downstream trench dry");
        var flowing=new LocalWaterSimulation(Build(true),before.Save());
        double initial=flowing.TotalVolume, supplied=0, drained=0;
        flowing.Step(.1f); supplied+=flowing.LastSuppliedVolume; drained+=flowing.LastDrainedVolume;
        Check(flowing.Volume(7)==0,"no instant flooding across the whole channel");
        for(int i=0;i<600;i++) { flowing.Step(.1f); supplied+=flowing.LastSuppliedVolume; drained+=flowing.LastDrainedVolume; }
        Check(drained>.001,"breach carries water to distant lower trench");
        Check(flowing.Level(7)<flowing.Level(0)-.5f,"outflow surface descends rather than extending source plane");
        Check(Math.Abs(flowing.TotalVolume-initial-supplied+drained)<.000001,"water changes only by tracked source input");
        var restored=new LocalWaterSimulation(Build(true),flowing.Save());
        Check(Math.Abs(restored.TotalVolume-flowing.TotalVolume)<.000001 && restored.Tick==flowing.Tick,"water persists exactly");
        var unsupplied=new LocalWaterSimulation(Build(true,false,false),flowing.Save());
        double stored=unsupplied.TotalVolume;
        for(int i=0;i<100;i++) unsupplied.Step(.1f);
        Check(Math.Abs(unsupplied.TotalVolume-stored)<.000001,"finite downstream water conserved without source");
        for(int i=0;i<flowing.Topology.Cells.Count;i++) Check(flowing.Volume(i)>=0 && flowing.Level(i)<=flowing.Topology.Cells[i].Roof+.00001,"bounded storage");
        // Separate stacked spaces and a roofed sideways passage: no vertical-clearance shortcut.
        float Cave(Vector3 p)
        {
            bool upper=p.Y>3 && p.Y<5 && p.X<2;
            bool tunnel=p.Y>0 && p.Y<2 && p.X>1;
            bool connector=p.X>1 && p.X<2 && p.Y>0 && p.Y<5;
            bool sealedVoid=p.X<1 && p.Y>-3 && p.Y<-1;
            return upper||tunnel||connector||sealedVoid?-1:1;
        }
        var connected=new LocalWaterSimulation(LocalWaterTopology.Build(Cave,0,0,6,1,-4,6,new[]{new WaterSource("roofed",.5f,.5f,4,2)}));
        Check(connected.Topology.Cells.Select((c,i)=>(c,i)).Where(p=>p.c.Bed< -1).All(p=>connected.Volume(p.i)==0),"sealed stacked chamber stays dry");
        Check(connected.Topology.Cells.Select((c,i)=>(c,i)).Any(p=>p.c.X>3 && p.c.Roof<3 && connected.Volume(p.i)>0),"water reaches sideways passage beneath a roof");
        LocalWaterSimulation Opening(float height)
        {
            float Aperture(Vector3 p)
            {
                if(p.X>1.9f && p.X<2.1f && !(p.Y>3 && p.Y<3+height)) return 1;
                return Density(p,true);
            }
            return new(LocalWaterTopology.Build(Aperture,0,0,8,1,-1,6,new[]{new WaterSource("lake",.5f,.5f,4,2)}),before.Save());
        }
        var narrow=Opening(.25f); var wide=Opening(1);
        narrow.Step(.1f); wide.Step(.1f);
        Check(wide.Volume(2)>narrow.Volume(2),"larger opening carries more water per tick");
        float Roofed(Vector3 p,bool breach)
        {
            bool lake=p.X<2 && p.Y>3 && p.Y<5;
            bool shaft=breach && p.X>1 && p.X<2 && p.Y>0 && p.Y<5;
            bool pipe=p.X>=2 && p.X<4 && p.Y>0 && p.Y<2;
            bool room=p.X>=4 && p.Y>0 && p.Y<5;
            return lake||shaft||pipe||room?-1:1;
        }
        LocalWaterTopology RoofTopology(bool breach)=>LocalWaterTopology.Build(p=>Roofed(p,breach),0,0,6,1,-1,6,new[]{new WaterSource("high",.5f,.5f,4,2)});
        var intact=new LocalWaterSimulation(RoofTopology(false));
        var flooding=new LocalWaterSimulation(RoofTopology(true),intact.Save());
        int roomIndex=Enumerable.Range(0,flooding.Topology.Cells.Count).First(i=>flooding.Topology.Cells[i].X==5);
        Check(flooding.Volume(roomIndex)==0,"roofed receiving chamber starts dry when breached");
        for(int i=0;i<1000;i++) flooding.Step(.1f);
        Check(flooding.Level(roomIndex)>2.5f,"saturated low passage transmits head into higher receiving chamber");
        var closed=new LocalWaterSimulation(Build(false,false,false),flowing.Save());
        double downstream=closed.Topology.Cells.Select((c,i)=>(c,i)).Where(p=>p.c.X>=2).Sum(p=>closed.Volume(p.i));
        for(int i=0;i<100;i++) closed.Step(.1f);
        Check(Math.Abs(closed.Topology.Cells.Select((c,i)=>(c,i)).Where(p=>p.c.X>=2).Sum(p=>closed.Volume(p.i))-downstream)<.000001,"closing a connection stops upstream transfer");
        for(int i=0;i<100;i++) restored=restored.Copy();
        Check(Math.Abs(restored.TotalVolume-flowing.TotalVolume)<.000001,"worker snapshots do not lose stored water");
        var field=new CaveSpikeField(new()); var original=LocalWaterSimulation.ForCave(field);
        double lakeVolume=original.TotalVolume; field.State.Water=original.Save();
        Check(field.TryCut(new(0,-2.5f,0,1.5f)),"hydrology terrain edit accepted");
        var edited=LocalWaterSimulation.ForCave(field);
        Check(Math.Abs(edited.TotalVolume-lakeVolume)<.00001,"digging remaps rather than creates water");
        var save=edited.Save(); save.Validate();
        var snapshot=field.Snapshot(); snapshot.State.Water!.Cells.Clear();
        Check(field.State.Water.Cells.Count>0,"worker water snapshots cannot mutate live save");
        foreach(float spacing in new[]{1f,.5f})
        {
            var mesh=LocalWaterMesher.Build(field,edited,spacing); var terrain=new CaveTerrainSampler(field,spacing);
            for(int i=0;i<mesh.Vertices.Count;i+=3)
            {
                var p=(mesh.Vertices[i]+mesh.Vertices[i+1]+mesh.Vertices[i+2])/3;
                Check(float.IsFinite(p.Y) && terrain.Density(p)<=.0001f,"dynamic water triangles are clipped to visible terrain");
            }
        }
        // Coverage matters as well as triangle validity: valid triangles alone missed the bank gaps.
        var shoreField=new CaveSpikeField(new()); var basin=LocalWaterSimulation.ForCave(shoreField);
        foreach(float spacing in new[]{1f,.5f})
        {
            var skin=LocalWaterMesher.Build(shoreField,basin,spacing); var terrain=new CaveTerrainSampler(shoreField,spacing);
            var buckets=new Dictionary<(int,int),List<int>>();
            for(int i=0;i<skin.Vertices.Count;i+=3)
            {
                var a=skin.Vertices[i];var b=skin.Vertices[i+1];var c=skin.Vertices[i+2];
                for(int z=(int)MathF.Floor(Math.Min(a.Z,Math.Min(b.Z,c.Z)));z<=MathF.Floor(Math.Max(a.Z,Math.Max(b.Z,c.Z)));z++)
                    for(int x=(int)MathF.Floor(Math.Min(a.X,Math.Min(b.X,c.X)));x<=MathF.Floor(Math.Max(a.X,Math.Max(b.X,c.X)));x++)
                    { if(!buckets.TryGetValue((x,z),out var bucket)) buckets[(x,z)]=bucket=new(); bucket.Add(i); }
            }
            bool Covers(Vector3 point)
            {
                if(!buckets.TryGetValue(((int)MathF.Floor(point.X),(int)MathF.Floor(point.Z)),out var triangles)) return false;
                float Side(Vector3 a,Vector3 b)=> (b.X-a.X)*(point.Z-a.Z)-(b.Z-a.Z)*(point.X-a.X);
                foreach(int i in triangles)
                {
                    var a=skin.Vertices[i];var b=skin.Vertices[i+1];var c=skin.Vertices[i+2];
                    if(Math.Abs((a.Y+b.Y+c.Y)/3-point.Y)>.01f) continue;
                    if(Side(a,b)>=-.00001f && Side(b,c)>=-.00001f && Side(c,a)>=-.00001f) return true;
                }
                return false;
            }
            int uncovered=0, expected=0;
            foreach(var reservoir in shoreField.Reservoirs)
                for(float x=-5.83f;x<14;x+=.23f) for(float z=-19.79f;z<5;z+=.23f)
                {
                    var p=new Vector3(x,reservoir.Level,z);
                    if(terrain.Density(p)>=-.025f) continue;
                    expected++; if(!Covers(p)) uncovered++;
                }
            Check(expected>100 && uncovered==0,$"no missing shoreline water at {spacing}m: {uncovered}/{expected}");
        }
        Console.WriteLine($"LOCAL_WATER_PASS: descending channel, gradual front, conserved storage, save/reload, stacked isolation ({checks} checks)");
        return checks;
    }
}





