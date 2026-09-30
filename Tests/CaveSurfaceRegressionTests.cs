using System.Numerics;
using Thornwake.Core;

internal static class CaveSurfaceRegressionTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool condition,string message) { if(!condition) throw new Exception("CAVE SURFACE: "+message); checks++; }
        var state=CaveSpikeStore.Load("Tests/Fixtures/cave-player-shore-cuts-v2.json");
        var field=new CaveSpikeField(state);
        foreach(float spacing in new[]{1f,.5f})
        {
            var left=VolumeMesher.Build(field,new(-1,0),spacing,true);
            var right=VolumeMesher.Build(field,new(0,0),spacing,true);
            var normals=new Dictionary<Vector3,Vector3>();
            foreach(var mesh in new[]{left,right}) for(int i=0;i<mesh.Vertices.Count;i++)
            {
                var point=mesh.Vertices[i]; var normal=mesh.Normals[i];
                Check(float.IsFinite(normal.X) && Math.Abs(normal.Length()-1)<.0001f,"finite unit normal");
                if(normals.TryGetValue(point,out var prior)) Check(Vector3.Distance(prior,normal)<.0001f,"shared vertices have continuous normals, including region seam");
                normals[point]=normal;
            }
            var water=LocalWaterSimulation.ForCave(field);
            var skin=LocalWaterMesher.Build(field,water,spacing);
            var buckets=new Dictionary<(int,int),List<int>>();
            for(int i=0;i<skin.Vertices.Count;i+=3)
            {
                var a=skin.Vertices[i];var b=skin.Vertices[i+1];var c=skin.Vertices[i+2];
                for(int z=(int)MathF.Floor(Math.Min(a.Z,Math.Min(b.Z,c.Z)));z<=MathF.Floor(Math.Max(a.Z,Math.Max(b.Z,c.Z)));z++)
                    for(int x=(int)MathF.Floor(Math.Min(a.X,Math.Min(b.X,c.X)));x<=MathF.Floor(Math.Max(a.X,Math.Max(b.X,c.X)));x++)
                    { if(!buckets.TryGetValue((x,z),out var bucket)) buckets[(x,z)]=bucket=new(); bucket.Add(i); }
            }
            int overlaps=0;
            foreach(var (tile,triangles) in buckets)
                for(float z=tile.Item2+.071f;z<tile.Item2+1;z+=.137f) for(float x=tile.Item1+.091f;x<tile.Item1+1;x+=.137f)
                {
                    var hits=new List<float>();
                    foreach(int i in triangles)
                    {
                        var a=skin.Vertices[i];var b=skin.Vertices[i+1];var c=skin.Vertices[i+2];
                        float Cross(Vector3 p,Vector3 q)=>(p.X-x)*(q.Z-z)-(p.Z-z)*(q.X-x);
                        float wa=Cross(b,c),wb=Cross(c,a),wc=Cross(a,b),sum=wa+wb+wc;
                        if(Math.Abs(sum)<1e-12f || wa/sum<=.00001f || wb/sum<=.00001f || wc/sum<=.00001f) continue;
                        float y=(wa*a.Y+wb*b.Y+wc*c.Y)/sum;
                        if(hits.Any(prior=>Math.Abs(prior-y)<.0001f)) overlaps++;
                        hits.Add(y);
                    }
                    checks++;
                }
            Check(overlaps==0,$"transparent water has no overlapping coplanar triangles at {spacing} m: {overlaps}");
        }
        // A small amount entering an empty neighbour must taper, not end as a tile-height shelf.
        var frontField=new CaveSpikeField(new());
        var empty=LocalWaterSimulation.ForCave(frontField);
        int source=Enumerable.Range(0,empty.Topology.Cells.Count).First(i=>empty.Topology.Cells[i].X==0 && empty.Topology.Cells[i].Z==-8 && empty.Topology.Cells[i].Bed<0);
        var donor=empty.Topology.Cells[source];
        frontField.State.Water=new LocalWaterSave { Cells=new() { new(donor.X,donor.Z,donor.Bed,donor.Roof,.5) } };
        var advancing=LocalWaterSimulation.ForCave(frontField);
        var front=LocalWaterMesher.Build(frontField,advancing,.5f);
        float px=donor.X+.9f,pz=donor.Z+.5f,frontHeight=float.NegativeInfinity;
        for(int i=0;i<front.Vertices.Count;i+=3)
        {
            var a=front.Vertices[i];var b=front.Vertices[i+1];var c=front.Vertices[i+2];
            float Cross(Vector3 p,Vector3 q)=>(p.X-px)*(q.Z-pz)-(p.Z-pz)*(q.X-px);
            float wa=Cross(b,c),wb=Cross(c,a),wc=Cross(a,b),sum=wa+wb+wc;
            if(Math.Abs(sum)<1e-12f || wa/sum<-.00001f || wb/sum<-.00001f || wc/sum<-.00001f) continue;
            frontHeight=Math.Max(frontHeight,(wa*a.Y+wb*b.Y+wc*c.Y)/sum);
        }
        Check(float.IsFinite(frontHeight) && frontHeight<donor.Bed+.49f,"water front slopes into the dry receiver instead of retaining the full source height");
        Check(Math.Abs(advancing.TotalVolume-.5)<.000001,"surface taper does not create stored water");
        Console.WriteLine($"CAVE_SURFACE_PASS: saved shoreline excavation, shared normals and water overlap ({checks} checks)");
        return checks;
    }
}

