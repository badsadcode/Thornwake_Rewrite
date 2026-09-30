using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Continuous free-surface patches clipped against the same tetrahedra as terrain.</summary>
public static class LocalWaterMesher
{
    public static ReservoirMeshData Build(CaveSpikeField field, LocalWaterSimulation water, float spacing, CancellationToken token=default)
    {
        var mesh=new ReservoirMeshData(); var sampler=new CaveTerrainSampler(field,spacing);
        var columns=water.Topology.Cells.Select((c,i)=>(c,i)).GroupBy(p=>(p.c.X,p.c.Z)).ToDictionary(g=>g.Key,g=>g.ToArray());
        var attributes=new Dictionary<Vector3,(float Depth,float Shore)>();
        var tiles=new Dictionary<(int X,int Z),List<int>>();
        bool Wet(int i)=>water.Volume(i)>=.002 && water.Level(i)<water.Topology.Cells[i].Roof-.001f;
        bool SameSpace(WaterColumn a,WaterColumn b)=>Math.Min(a.Roof,b.Roof)>Math.Max(a.Bed,b.Bed)+.02f;
        void AddTile(int x,int z,int reference)
        {
            if(!tiles.TryGetValue((x,z),out var refs)) tiles[(x,z)]=refs=new();
            if(!refs.Any(i=>SameSpace(water.Topology.Cells[i],water.Topology.Cells[reference]))) refs.Add(reference);
        }
        // Use storage centres as surface nodes. The advancing edge tapers to a dry receiver's
        // bed instead of ending in a full-height vertical cut along a one-metre tile boundary.
        for(int i=0;i<water.Topology.Cells.Count;i++) if(Wet(i))
        {
            var cell=water.Topology.Cells[i];
            for(int dz=-1;dz<=0;dz++) for(int dx=-1;dx<=0;dx++) AddTile(cell.X+dx,cell.Z+dz,i);
        }
        foreach(var (tile,references) in tiles) foreach(int i in references)
        {
            token.ThrowIfCancellationRequested(); var reference=water.Topology.Cells[i];
            float Node(int x,int z)
            {
                // Ghost bank nodes use a deterministic nearest wet head, shared by every patch
                // touching this node. These values only survive clipping where terrain is air.
                int closest=i, distance=int.MaxValue;
                for(int dz=-1;dz<=1;dz++) for(int dx=-1;dx<=1;dx++)
                    if(columns.TryGetValue((x+dx,z+dz),out var neighbours)) foreach(var (other,index) in neighbours)
                        if(Wet(index) && SameSpace(reference,other))
                        {
                            int d=dx*dx+dz*dz;
                            if(d<distance || d==distance && index<closest) { closest=index; distance=d; }
                        }
                float head=water.Level(closest);
                if(columns.TryGetValue((x,z),out var own)) foreach(var (cell,index) in own)
                    if(SameSpace(reference,cell))
                    {
                        if(Wet(index)) return water.Level(index);
                        if(cell.Bed<head && cell.Roof>head) return cell.Bed;
                    }
                return head;
            }
            var a=new Vector3(tile.X+.5f,Node(tile.X,tile.Z),tile.Z+.5f);
            var b=new Vector3(tile.X+1.5f,Node(tile.X+1,tile.Z),tile.Z+.5f);
            var d=new Vector3(tile.X+1.5f,Node(tile.X+1,tile.Z+1),tile.Z+1.5f);
            var e=new Vector3(tile.X+.5f,Node(tile.X,tile.Z+1),tile.Z+1.5f);
            Patch(a,b,d); Patch(a,d,e);
        }
        return mesh;

        void Patch(Vector3 a,Vector3 b,Vector3 c)
        {
            var min=Vector3.Min(a,Vector3.Min(b,c))/spacing; var max=Vector3.Max(a,Vector3.Max(b,c))/spacing;
            int x0=(int)MathF.Floor(min.X), x1=Math.Max(x0,(int)MathF.Ceiling(max.X)-1);
            int y0=(int)MathF.Floor(min.Y), y1=Math.Max(y0,(int)MathF.Ceiling(max.Y)-1);
            int z0=(int)MathF.Floor(min.Z), z1=Math.Max(z0,(int)MathF.Ceiling(max.Z)-1);
            for(int z=z0;z<=z1;z++) for(int x=x0;x<=x1;x++) for(int y=y0;y<=y1;y++)
            {
                var cube=CaveTerrainSampler.Corners.Select(p=>new Vector3((x+p.X)*spacing,(y+p.Y)*spacing,(z+p.Z)*spacing)).ToArray();
                for(int t=0;t<6;t++)
                {
                    var tetra=new Vector3[4]; for(int k=0;k<4;k++) tetra[k]=cube[CaveTerrainSampler.Tetrahedra[t,k]];
                    var polygon=new List<Vector3> { a,b,c };
                    for(int face=0;face<4 && polygon.Count>=3;face++)
                    {
                        var p=tetra[(face+1)%4]; var q=tetra[(face+2)%4]; var r=tetra[(face+3)%4];
                        var normal=Vector3.Cross(q-p,r-p); if(Vector3.Dot(normal,tetra[face]-p)<0) normal=-normal;
                        polygon=Clip(polygon,v=>Vector3.Dot(normal,v-p));
                    }
                    if(polygon.Count<3) continue;
                    polygon=Clip(polygon,v=>-sampler.Density(v));
                    for(int k=1;k<polygon.Count-1;k++)
                    {
                        var p=polygon[0]; var q=polygon[k]; var r=polygon[k+1];
                        if(Vector3.Cross(q-p,r-p).LengthSquared()==0) continue;
                        Vertex(p); Vertex(q); Vertex(r);
                    }
                }
            }
        }
        void Vertex(Vector3 p)
        {
            if(!attributes.TryGetValue(p,out var sample))
            {
                float depth=0;
                for(float d=.1f;p.Y-d>=-10;d+=.1f)
                { if(sampler.Density(p-Vector3.UnitY*d)>=0) break; depth=d; }
                sample=(depth,Math.Max(0,-sampler.Density(p))); attributes[p]=sample;
            }
            mesh.Vertices.Add(p); mesh.Depths.Add(sample.Depth); mesh.ShoreDistances.Add(sample.Shore);
        }
    }
    private static List<Vector3> Clip(List<Vector3> points,Func<Vector3,float> distance)
    {
        var result=new List<Vector3>(8);
        for(int i=0;i<points.Count;i++)
        {
            var a=points[i]; var b=points[(i+1)%points.Count]; float da=distance(a),db=distance(b);
            if(da>=0) result.Add(a);
            if((da>=0)==(db>=0)) continue;
            if(a.X>b.X || a.X==b.X && (a.Y>b.Y || a.Y==b.Y && a.Z>b.Z)) { (a,b)=(b,a); (da,db)=(db,da); }
            result.Add(a+(b-a)*(da/(da-db)));
        }
        return result;
    }
}
