using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Thornwake.Core;

public sealed record RoofEdge(string Id,string Role,(float X,float Y,float Z) Start,(float X,float Y,float Z) End,(float X,float Y,float Z) Middle);
public sealed record RoofSnap(float X,float Z,float Elevation,string ConnectionId);

/// <summary>Shared roof profiles and connection geometry. Heights include the .17 m roof skin baseline.</summary>
public static class RoofGeometry
{
    public const float Skin=.17f;
    public static bool KnownGeometry(string geometry)=>geometry is "roof_slope" or "roof_ridge" or "roof_hip" or "roof_valley" or "roof_eave" or "roof_gable_edge" or "roof_gable_fill" or "roof_gable_ridge_fill";
    public static bool IsRoofModule(StructureState structure)=>structure.Kind=="roof_module"&&structure.Selection!=null;
    public static bool IsFill(string geometry)=>geometry is "roof_gable_fill" or "roof_gable_ridge_fill";
    public static bool IsTrim(string geometry)=>geometry is "roof_eave" or "roof_gable_edge" or "roof_gable_fill" or "roof_gable_ridge_fill";
    public static bool IsTrim(ContentCatalog catalog,BuildSelection selection)=>IsTrim(BuildingVariants.Variant(catalog,selection).Geometry);
    public static float Rise(string geometry,float slope)
        =>geometry is "roof_ridge" or "roof_gable_ridge_fill"?1.5f*Tangent(slope):geometry is "roof_slope" or "roof_gable_edge" or "roof_hip" or "roof_valley" or "roof_gable_fill"?3*Tangent(slope):0;
    private static float Tangent(float slope)=>MathF.Tan(slope*MathF.PI/180);
    public static void ValidateReferenceGraph(WorldState state)
    {
        var byId=state.Structures.ToDictionary(s=>s.Id);var done=new HashSet<string>();
        foreach(var piece in state.Structures)
        {
            var path=new HashSet<string>();var current=piece;
            while(current.RoofConnectionId!=""&&!done.Contains(current.Id))
            {
                if(!path.Add(current.Id)||!byId.TryGetValue(current.RoofConnectionId,out var next))throw new InvalidDataException("A roof connection is missing or cyclic.");
                current=next;
            }
            foreach(string id in path)done.Add(id);
        }
    }
    public static void ValidateConnections(WorldState state,ContentCatalog catalog)
    {
        ValidateReferenceGraph(state);
        foreach(var piece in state.Structures.Where(IsRoofModule))
        {
            var variant=BuildingVariants.Variant(catalog,piece.Selection!);
            if(IsTrim(variant.Geometry)&&piece.RoofConnectionId=="")throw new InvalidDataException("A roof trim is missing its parent roof.");
            if(piece.RoofConnectionId!="")
            {
                var anchor=state.Structures.FirstOrDefault(s=>s.Id==piece.RoofConnectionId);
                if(anchor==null||!Connected(catalog,piece,anchor))throw new InvalidDataException("A roof connection has mismatched pitch or geometry.");
            }
            else
            {
                var floor=state.Structures.FirstOrDefault(s=>s.Id==piece.SupportId&&Construction.IsFloor(s));
                if(floor==null||MathF.Abs(Construction.Base(state,piece)-Construction.Base(state,floor)-Construction.Storey)>.03f)throw new InvalidDataException("A raised roof is missing its height connection.");
            }
        }
    }
    public static float SurfaceHeight(string geometry,float slope,float x,float z)=>geometry switch
    {
        "roof_slope" or "roof_gable_edge" or "roof_gable_fill"=>Skin+(z+1.5f)*Tangent(slope),
        "roof_ridge" or "roof_gable_ridge_fill"=>Skin+(1.5f-MathF.Abs(z))*Tangent(slope),
        "roof_hip"=>Skin+MathF.Min(x+1.5f,z+1.5f)*Tangent(slope),
        "roof_valley"=>Skin+MathF.Max(x+1.5f,z+1.5f)*Tangent(slope),
        "roof_eave"=>Skin,
        _=>throw new ArgumentOutOfRangeException(nameof(geometry),"Unsupported roof profile.")
    };
    public static float GableTop(string geometry,float slope,float z)=>MathF.Max(0,SurfaceHeight(geometry,slope,0,z)-.10f);
    public static float GableBottom(WorldState state,StructureState piece)
    {
        var floor=state.Structures.FirstOrDefault(s=>s.Id==piece.SupportId&&Construction.IsFloor(s));
        return floor==null?0:MathF.Min(0,Construction.Base(state,floor)+Construction.Storey-Construction.Base(state,piece));
    }
    public static bool BlocksPoint(WorldState state,ContentCatalog catalog,StructureState piece,float x,float z,float feetY,float radius=.3f)
    {
        if(!IsRoofModule(piece))return false;string geometry=BuildingVariants.Variant(catalog,piece.Selection!).Geometry;
        if(!IsFill(geometry))return false;
        var local=Construction.Local(piece,x,z);float side=piece.Selection!.Mirrored?1.5f:-1.5f;
        if(MathF.Abs(local.X-side)>.09f+radius||MathF.Abs(local.Z)>1.5f+radius)return false;
        float top=0;
        foreach(float sample in new[]{local.Z-radius,local.Z,local.Z+radius})top=MathF.Max(top,GableTop(geometry,piece.Selection.Slope,Math.Clamp(sample,-1.5f,1.5f)));
        if(geometry=="roof_gable_ridge_fill"&&MathF.Abs(local.Z)<=radius)top=MathF.Max(top,GableTop(geometry,piece.Selection.Slope,0));
        float baseY=Construction.Base(state,piece),bottom=baseY+GableBottom(state,piece);return feetY<baseY+top-.025f&&feetY+1.65f>bottom+.025f;
    }
    /// <summary>Profile extrema occur at polygon vertices or where its edges cross a roof crease.</summary>
    public static (float X,float Z)[] ProfileSamples(string geometry,IReadOnlyList<(float X,float Z)> polygon)
    {
        var samples=polygon.ToList();bool diagonal=geometry is "roof_hip" or "roof_valley",ridge=geometry is "roof_ridge" or "roof_gable_ridge_fill";
        if(!diagonal&&!ridge)return samples.ToArray();
        float Side((float X,float Z) point)=>diagonal?point.X-point.Z:point.Z;
        for(int i=0;i<polygon.Count;i++)
        {
            var a=polygon[i];var b=polygon[(i+1)%polygon.Count];float first=Side(a),last=Side(b);
            if(first*last<0){float t=first/(first-last);samples.Add((a.X+(b.X-a.X)*t,a.Z+(b.Z-a.Z)*t));}
        }
        return samples.ToArray();
    }
    private static bool TrimMatchesParent(string trim,string parent)=>trim=="roof_gable_ridge_fill"?parent=="roof_ridge":parent=="roof_slope";
    public static RoofEdge[] Edges(string geometry,float slope,bool mirrored=false)
    {
        (float X,float Y,float Z) Point(float x,float z)=>(x,SurfaceHeight(geometry,slope,x,z),z);
        RoofEdge Edge(string id,float x,float z,float xx,float zz)
        {
            var start=Point(x,z);var end=Point(xx,zz);var middle=Point((x+xx)/2,(z+zz)/2);
            string role=IsTrim(geometry)?"trim":MathF.Abs(start.Y-end.Y)<.001f&&MathF.Abs(start.Y-middle.Y)<.001f?"horizontal":"profile";
            return new(id,role,start,end,middle);
        }
        if(geometry=="roof_eave")return new[]{Edge("eave",-1.5f,-1.5f,1.5f,-1.5f)};
        if(geometry=="roof_gable_edge"||IsFill(geometry))return new[]{Edge("gable",mirrored?1.5f:-1.5f,-1.5f,mirrored?1.5f:-1.5f,1.5f)};
        return new[]{Edge("north",-1.5f,-1.5f,1.5f,-1.5f),Edge("east",1.5f,-1.5f,1.5f,1.5f),Edge("south",1.5f,1.5f,-1.5f,1.5f),Edge("west",-1.5f,1.5f,-1.5f,-1.5f)};
    }
    public static RoofEdge[] WorldEdges(StructureState piece,ContentCatalog catalog)
    {
        var selection=piece.Selection!;string geometry=BuildingVariants.Variant(catalog,selection).Geometry;
        (float X,float Y,float Z) Transform((float X,float Y,float Z) p){var q=Construction.World(piece,p.X,p.Z);return(q.X,(piece.Elevation??0)+p.Y,q.Z);}
        return Edges(geometry,selection.Slope,selection.Mirrored).Select(e=>new RoofEdge(e.Id,e.Role,Transform(e.Start),Transform(e.End),Transform(e.Middle))).ToArray();
    }
    private static bool Near((float X,float Y,float Z) a,(float X,float Y,float Z) b)=>Construction.Distance(a.X,a.Z,b.X,b.Z)<.025f&&MathF.Abs(a.Y-b.Y)<.025f;
    public static bool Connected(ContentCatalog catalog,StructureState piece,StructureState anchor)
    {
        if(!IsRoofModule(piece)||!IsRoofModule(anchor)||piece.Level!=anchor.Level||MathF.Abs(piece.Selection!.Slope-anchor.Selection!.Slope)>.001f)return false;
        string geometry=BuildingVariants.Variant(catalog,piece.Selection).Geometry,anchorGeometry=BuildingVariants.Variant(catalog,anchor.Selection).Geometry;
        if(IsTrim(anchorGeometry))return false;
        if(IsTrim(geometry))return TrimMatchesParent(geometry,anchorGeometry)&&Construction.Distance(piece.X,piece.Z,anchor.X,anchor.Z)<.025f&&MathF.Abs((piece.Elevation??0)-(anchor.Elevation??0))<.025f&&MathF.Abs(MathF.IEEERemainder(piece.Rotation-anchor.Rotation,MathF.Tau))<.001f;
        return WorldEdges(piece,catalog).Any(a=>WorldEdges(anchor,catalog).Any(b=>a.Role==b.Role&&Near(a.Start,b.End)&&Near(a.End,b.Start)&&Near(a.Middle,b.Middle)));
    }
    public static StructureState? TrimParent(WorldState state,ContentCatalog catalog,BuildSelection selection,float x,float z,float rotation,int level,BuildSnapMode snapMode)
    {
        string geometry=BuildingVariants.Variant(catalog,selection).Geometry;
        return state.Structures.Where(s=>IsRoofModule(s)&&s.Level==level&&TrimMatchesParent(geometry,BuildingVariants.Variant(catalog,s.Selection!).Geometry)&&MathF.Abs(selection.Slope-s.Selection!.Slope)<.001f&&MathF.Abs(MathF.IEEERemainder(s.Rotation-rotation,MathF.Tau))<.001f&&Construction.Distance(x,z,s.X,s.Z)<(snapMode==BuildSnapMode.Object?2.1f:.025f)).OrderBy(s=>Construction.Distance(x,z,s.X,s.Z)).FirstOrDefault();
    }
    public static RoofSnap? Snap(WorldState state,ContentCatalog catalog,BuildSelection selection,float x,float z,float rotation,int level,out bool incompatiblePitch)
    {
        incompatiblePitch=false;var matches=new List<RoofSnap>();string geometry=BuildingVariants.Variant(catalog,selection).Geometry;
        var oriented=new StructureState{Kind="roof_module",Rotation=rotation,Elevation=0,Selection=selection};var local=WorldEdges(oriented,catalog);
        foreach(var anchor in state.Structures.Where(s=>IsRoofModule(s)&&s.Level==level&&!IsTrim(catalog,s.Selection!)&&Construction.Distance(x,z,s.X,s.Z)<6))
        foreach(var ae in WorldEdges(anchor,catalog))foreach(var ce in local)
        {
            if(ae.Role!=ce.Role)continue;
            float px=ae.End.X-ce.Start.X,pz=ae.End.Z-ce.Start.Z;
            if(Construction.Distance(ce.End.X+px,ce.End.Z+pz,ae.Start.X,ae.Start.Z)>.025f||Construction.Distance(x,z,px,pz)>1.8f)continue;
            if(MathF.Abs(anchor.Selection!.Slope-selection.Slope)>.001f){incompatiblePitch=true;continue;}
            float elevation=ae.End.Y-ce.Start.Y;
            if(MathF.Abs(ce.End.Y+elevation-ae.Start.Y)>.025f||MathF.Abs(ce.Middle.Y+elevation-ae.Middle.Y)>.025f)continue;
            matches.Add(new(px,pz,elevation,anchor.Id));
        }
        return matches.OrderBy(s=>Construction.Distance(x,z,s.X,s.Z)).ThenBy(s=>MathF.Abs(s.Elevation)).FirstOrDefault();
    }
}
