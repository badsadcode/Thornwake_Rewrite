using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Disposable validation renderer policy. A tile batches 16x16 WorldRegions; it is not a save address.</summary>
public static class PlayableWorldPolicy
{
    public const int TileSize = 256, Spacing = 8, Radius = 20, RetainRadius = 21;
    public const int MaximumTiles = 1849;
    public static WorldRegion TileAt(WorldPoint p) { p.Validate(); return new((long)Math.Floor(p.X/TileSize),(long)Math.Floor(p.Z/TileSize)); }
    public static WorldPoint OriginFor(WorldPoint p) => new(Math.Floor(p.X/TileSize)*TileSize,Math.Floor(p.Y/256)*256,Math.Floor(p.Z/TileSize)*TileSize);
    public static bool NeedsRebase(WorldPoint p,WorldPoint origin) => Math.Max(Math.Abs(p.X-origin.X),Math.Max(Math.Abs(p.Y-origin.Y),Math.Abs(p.Z-origin.Z)))>512;
    public static bool Wanted(WorldRegion tile,WorldPoint observer,int radius=RetainRadius)
    { var c=TileAt(observer); return Math.Max(Math.Abs(tile.X-c.X),Math.Abs(tile.Z-c.Z))<=radius; }
    public static bool Accept(long jobRevision,long currentRevision,WorldRegion tile,WorldPoint observer)
        => jobRevision==currentRevision && Wanted(tile,observer);
    public static WorldPoint FromLocal(Vector3 local,WorldPoint origin)
    { var p=new WorldPoint(origin.X+local.X,origin.Y+local.Y,origin.Z+local.Z);p.Validate();return p; }
    public static WorldPoint Teleport(MacroWorldGenerator world,double x,double z,double? y=null)
    { var p=new WorldPoint(x,y??world.Sample(x,z).Elevation+1,z); p.Validate(); return p; }
    // Sweep admission, not just endpoint admission, prevents fast debug walking through an unloaded tile.
    public static bool CanTraverse(WorldPoint from,WorldPoint to,Func<WorldRegion,bool> resident)
    {
        double distance=Math.Max(Math.Abs(to.X-from.X),Math.Abs(to.Z-from.Z));
        if(distance>4096)return false;
        int steps=Math.Max(1,(int)Math.Ceiling(distance/32));
        for(int i=0;i<=steps;i++)foreach(double dx in new[]{-.65,.65})foreach(double dz in new[]{-.65,.65})
            if(!resident(TileAt(new(from.X+(to.X-from.X)*i/steps+dx,from.Y,from.Z+(to.Z-from.Z)*i/steps+dz))))return false;
        return true;
    }
}

/// <summary>One immutable CPU-only height mesh, anchored near its elevations before float conversion.</summary>
public sealed record PlayableTerrainPatch(WorldRegion Tile,WorldPoint Anchor,double[] Heights,Vector3[] Vertices,Vector3[] Normals,int[] Indices,Vector3[] SeaVertices,int[] SeaIndices)
{
    public bool Detailed { get; init; } = true;
    public const int Side=PlayableWorldPolicy.TileSize/PlayableWorldPolicy.Spacing+1;
    public static PlayableTerrainPatch Build(MacroWorldGenerator world,WorldRegion tile,CancellationToken token=default,bool detailed=true)
    {
        tile.Validate(); int side=Side,n=side*side; var heights=new double[n];var normals=new Vector3[n];
        double x0=tile.X*(double)PlayableWorldPolicy.TileSize,z0=tile.Z*(double)PlayableWorldPolicy.TileSize;
        _=WorldRegion.At(x0+PlayableWorldPolicy.TileSize,z0+PlayableWorldPolicy.TileSize);
        // Reuse the tested 16 m regional sampler. Duplicate shared samples are deterministic; no new terrain authority.
        if(detailed) for(int z=0;z<16;z++)for(int x=0;x<16;x++)
        {
            token.ThrowIfCancellationRequested();var chunk=MacroRegionSampler.Build(world,new(tile.X*16+x,tile.Z*16+z),8,token);
            for(int dz=0;dz<3;dz++)for(int dx=0;dx<3;dx++)
            {int i=(z*2+dz)*side+x*2+dx;heights[i]=chunk.Elevations[dz*3+dx];normals[i]=chunk.Normals[dz*3+dx];}
        }
        else
        {
            // Coarse interior fan, exact 8 m boundary: unlike ordinary mixed grids this cannot crack at a detailed neighbour.
            for(int z=0;z<side;z++)for(int x=0;x<side;x++)
            {
                if(x!=0 && z!=0 && x!=side-1 && z!=side-1 && !(x==16 && z==16))continue;
                token.ThrowIfCancellationRequested();double wx=x0+x*8,wz=z0+z*8;
                heights[z*side+x]=world.Sample(wx,wz).Elevation;
                double dx=world.Sample(wx+.5,wz).Elevation-world.Sample(wx-.5,wz).Elevation;
                double dz=world.Sample(wx,wz+.5).Elevation-world.Sample(wx,wz-.5).Elevation;
                normals[z*side+x]=Vector3.Normalize(new((float)-dx,1,(float)-dz));
            }
        }
        var anchor=new WorldPoint(x0,Math.Floor(heights[0]/256)*256,z0);var vertices=new Vector3[n];
        for(int z=0;z<side;z++)for(int x=0;x<side;x++)
            if(detailed || x==0 || z==0 || x==side-1 || z==side-1 || x==16 && z==16)
                vertices[z*side+x]=new WorldPoint(x0+x*8,heights[z*side+x],z0+z*8).RelativeTo(anchor);
        var indices=new List<int>();var sea=new List<int>();var seaVertices=new List<Vector3>();double level=world.Manifest.Profile.SeaLevel;
        void Triangle(int a,int b,int c)
        {
            indices.AddRange(new[]{a,b,c});
            // Clip to the SAME sampled mesh used by collision, then check the macro ocean context.
            // A depth cutoff would incorrectly dry hundreds of metres of the very shallow A4 coastal shelf.
            // Render-only depth separation belongs in the material, never in the geographic mask.
            var polygon=new List<(Vector3 P,double H)>();double shore=level;
            var ids=new[]{a,b,c};
            for(int j=0;j<3;j++)
            {
                int from=ids[j],to=ids[(j+1)%3];bool wet=heights[from]<shore,nextWet=heights[to]<shore;
                if(wet)polygon.Add((vertices[from],heights[from]));
                if(wet!=nextWet)
                {
                    double t=(shore-heights[from])/(heights[to]-heights[from]);
                    var p=vertices[from]+(vertices[to]-vertices[from])*(float)t;
                    polygon.Add((p,shore));
                }
            }
            for(int j=1;j+1<polygon.Count;j++)
            {
                var points=new[]{polygon[0].P,polygon[j].P,polygon[j+1].P};
                double cx=points.Average(p=>(double)p.X)+x0,cz=points.Average(p=>(double)p.Z)+z0;
                if(world.Sample(cx,cz).LandInfluence>0 || points.Any(p=>world.Sample(x0+p.X,z0+p.Z).LandInfluence>0))continue;
                foreach(var p in points){sea.Add(seaVertices.Count);seaVertices.Add(new(p.X,(float)(level-anchor.Y),p.Z));}
            }
        }
        if(detailed)for(int z=0;z<side-1;z++)for(int x=0;x<side-1;x++)
        {int a=z*side+x,b=a+1,c=a+side,d=c+1;Triangle(a,b,c);Triangle(b,d,c);}
        else
        {
            int center=16*side+16;
            for(int i=0;i<side-1;i++)
            {Triangle(center,i,i+1);Triangle(center,i*side+side-1,(i+1)*side+side-1);Triangle(center,(side-1)*side+i+1,(side-1)*side+i);Triangle(center,(i+1)*side,i*side);}
        }
        return new(tile,anchor,heights,vertices,normals,indices.ToArray(),seaVertices.ToArray(),sea.ToArray()){Detailed=detailed};
    }
}

public enum LabWaterStatus { RepresentativeSea, ProvisionalReach, Unresolved }
public sealed record LabRiverWindow(string DescriptorId,LabWaterStatus Status,IReadOnlyList<WorldPoint> Left,IReadOnlyList<WorldPoint> Right,string Reason);
public sealed record LabLandmark(string Name,WorldPoint Position);

/// <summary>Presentation checks only: no flow, carving, topology promotion or persistent water state.</summary>
public static class PlayableWaterPresentation
{
    public static LabRiverWindow InspectReach(InspectedCorridor corridor,Func<double,double,double> height,CancellationToken token=default)
    {
        var left=new List<WorldPoint>();var right=new List<WorldPoint>();
        LabRiverWindow Reject(string reason)=>new(corridor.Id,LabWaterStatus.Unresolved,Array.Empty<WorldPoint>(),Array.Empty<WorldPoint>(),reason);
        if(corridor.Assessment.Compatibility!=CorridorCompatibility.DownhillAtSampleSpacing)return Reject("Regional profile has a barrier or only hypothetical submergence.");
        var a=corridor.Points[0];var b=corridor.Points[1];double dx=b.X-a.X,dz=b.Z-a.Z,length=Math.Sqrt(dx*dx+dz*dz);
        if(length<16)return Reject("Reach too short.");
        dx/=length;dz/=length;double previous=double.PositiveInfinity;
        // One bounded 64 m window, 2 m checks; exact source link identity is retained. No fabricated whole river.
        for(int i=0;i<=32;i++)
        {
            token.ThrowIfCancellationRequested();double along=Math.Min(length,64)*i/32,x=a.X+dx*along,z=a.Z+dz*along;
            double bed=height(x,z),stage=bed+.15;
            if(!double.IsFinite(bed)||stage>previous+1e-8)return Reject("Fine terrain rises along reach.");
            // Surface must fit in a sampled 1 m half-width trough with banks at 2 m.
            var cross=new[]{height(x-dz,z+dx),height(x+dz,z-dx),height(x-dz*2,z+dx*2),height(x+dz*2,z-dx*2)};
            if(cross.Any(v=>!double.IsFinite(v)) || cross[0]>=stage || cross[1]>=stage || cross[2]<stage || cross[3]<stage)return Reject("No terrain-supported banks at walking scale.");
            previous=stage;left.Add(new(x-dz,stage,z+dx));right.Add(new(x+dz,stage,z-dx));
        }
        return new(corridor.Id,LabWaterStatus.ProvisionalReach,left.AsReadOnly(),right.AsReadOnly(),"PROVISIONAL: 64 m maximum, 2 m checks, static; storage/flow/connectivity unknown.");
    }
    public static IReadOnlyList<LabLandmark> Landmarks(MacroWorldGenerator world,RegionalDrainageRefiner regional)
    {
        var result=new List<LabLandmark>();
        void Add(string name,double x,double z,double lift=1)=>result.Add(new(name,PlayableWorldPolicy.Teleport(world,x,z,world.Sample(x,z).Elevation+lift)));
        foreach(var land in world.LandFeatures.Where(l=>!l.Island))Add("Continent "+(result.Count+1),land.Center.X,land.Center.Z);
        var mountain=world.Ranges.SelectMany(r=>r.Points).OrderByDescending(p=>world.Sample(p.X,p.Z).Elevation).FirstOrDefault();
        Add("High mountain (descriptor sample)",mountain.X,mountain.Z);
        var first=world.LandFeatures[0];double low=0,high=world.Manifest.Profile.WorldSize;
        // Bounded bracket on a known continent, not a world-wide fine search.
        for(int i=0;i<48;i++){double mid=(low+high)*.5;if(world.Sample(first.Center.X+mid,first.Center.Z).Elevation>world.Manifest.Profile.SeaLevel)low=mid;else high=mid;}
        Add("Coast (sampled ocean/land boundary)",first.Center.X+low-24,first.Center.Z);
        var d=regional.Drainage;
        int valley=Enumerable.Range(0,d.Grid.Count).Where(i=>d.Grid.Bed[i]>world.Manifest.Profile.SeaLevel+20).OrderByDescending(i=>d.CatchmentArea[i]).First();
        var v=d.Grid.Center(valley);Add("Valley / major drainage cell",v.X,v.Z);
        if(d.Basins.Count>0){var basin=d.Basins.OrderByDescending(b=>b.Cells.Count).First();var p=d.Grid.Center(basin.Cells[0]);Add("Unresolved basin candidate",p.X,p.Z);}
        Add("World reference",world.Manifest.OriginX,world.Manifest.OriginZ);
        return result.AsReadOnly();
    }
}
