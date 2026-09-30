using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Thornwake.Core;

public enum BuildSnapMode { Object, Grid, Free }
public sealed record BuildPlan(string Kind,float X,float Z,float Rotation,float Elevation,string SupportId,bool Valid,string Message,int Level=0,string SnapLabel="",BuildSelection? Selection=null,Dictionary<string,int>? Costs=null,string RoofConnectionId="");

/// <summary>Shared placement and occupancy rules; previews and committed builds use the same plan.</summary>
public static class Construction
{
    public const float Tile=3, Deck=.28f, WallHeight=2.6f, Storey=WallHeight+Deck, Reach=6, StepHeight=.45f;
    public static readonly (float X,float Z,float Radius)[] VillageLots={(-9,-8,4.2f),(9,-9,4.2f),(-11,9,3.6f),(10,10,3.6f),(-3,0,1.2f),(6,1,1.5f),(-4,6,1.2f)};
    public static float Distance(float x,float z,float xx,float zz)=>MathF.Sqrt((x-xx)*(x-xx)+(z-zz)*(z-zz));
    public static float Ground(WorldState state,float x,float z)=>TerrainData.Height(state,x,z);
    public static float Base(WorldState state,StructureState s)=>s.Elevation??Ground(state,s.X,s.Z);
    public static bool IsFloor(StructureState s)=>s.Kind is "foundation" or "upper_floor" or "foundation_triangle" or "upper_floor_triangle";
    public static bool IsRoof(StructureState s)=>s.Kind is "roof" or "roof_flat" or "roof_slope" or "roof_corner" or "roof_triangle" or "roof_module";
    public static bool IsEdge(StructureState s)=>s.Kind is "wall" or "door" or "wall_diagonal";
    public static bool IsTriangle(StructureState s)=>s.Kind.EndsWith("_triangle",StringComparison.Ordinal);
    private static bool IsFoundation(string kind)=>kind is "foundation" or "foundation_triangle";
    private static bool IsUpper(string kind)=>kind is "upper_floor" or "upper_floor_triangle";
    public static bool OnFloor(StructureState s,float x,float z,float margin=0)
    {var p=Local(s,x,z);return MathF.Abs(p.X)<=Tile/2+margin&&MathF.Abs(p.Z)<=Tile/2+margin&&(!IsTriangle(s)||p.X+p.Z<=margin*MathF.Sqrt(2)+.0001f);}
    public static (float X,float Z) Local(StructureState s,float x,float z)
    {
        float c=MathF.Cos(s.Rotation),sin=MathF.Sin(s.Rotation),dx=x-s.X,dz=z-s.Z;
        return(c*dx-sin*dz,sin*dx+c*dz);
    }
    public static (float X,float Z) World(StructureState s,float x,float z)
    {float c=MathF.Cos(s.Rotation),sin=MathF.Sin(s.Rotation);return(s.X+c*x+sin*z,s.Z-sin*x+c*z);}
    public static (float X,float Z)[] Polygon(StructureState s,float width=Tile,float depth=Tile)
    {
        var points=IsTriangle(s)?new[]{(-width/2,-depth/2),(width/2,-depth/2),(-width/2,depth/2)}:
            new[]{(-width/2,-depth/2),(width/2,-depth/2),(width/2,depth/2),(-width/2,depth/2)};
        return points.Select(p=>World(s,p.Item1,p.Item2)).ToArray();
    }
    public static bool Occupies(StructureState s,BuildingDefinition definition,float x,float z,float margin=0)
    {
        var p=Local(s,x,z);return MathF.Abs(p.X)<definition.Width/2+margin&&MathF.Abs(p.Z)<definition.Depth/2+margin&&(!IsTriangle(s)||p.X/definition.Width+p.Z/definition.Depth<margin*MathF.Sqrt(2)/Tile+.0001f);
    }
    public static bool SolidAt(StructureState s,BuildingDefinition definition,float x,float z,float radius=.3f)
    {
        if(s.Kind=="rug"||definition.Placement is "floor" or "roof" or "stairs")return false;
        var p=Local(s,x,z);
        if(s.Kind=="door"&&s.DoorOpen)
            return MathF.Abs(p.Z)<.13f+radius&&MathF.Abs(p.X)<1.5f+radius&&MathF.Abs(p.X)>.65f-radius;
        return Occupies(s,definition,x,z,radius);
    }
    public static float WalkHeight(WorldState state,float x,float z)
    {
        float height=Ground(state,x,z);
        foreach(var s in state.Structures)if(IsFloor(s)&&s.Level==0&&OnFloor(s,x,z))height=MathF.Max(height,Base(state,s)+Deck);
        return height;
    }
    public static float StairHeight(WorldState state,StructureState stairs,float x,float z)
        =>Base(state,stairs)+Math.Clamp((Local(stairs,x,z).Z+Tile/2)/Tile,0,1)*Storey;
    public static bool OnStairs(StructureState stairs,float x,float z)
    {var local=Local(stairs,x,z);return MathF.Abs(local.X)<=.625f&&MathF.Abs(local.Z)<=Tile/2;}
    /// <summary>Select reachable surfaces, so walking under an upstairs room never teleports the player onto it.</summary>
    public static float WalkHeight(WorldState state,float x,float z,float currentY)
    {
        float height=Ground(state,x,z);
        foreach(var s in state.Structures)
        {
            float surface;
            if(IsFloor(s)&&OnFloor(s,x,z))surface=Base(state,s)+Deck;
            else if(s.Kind=="stairs"&&OnStairs(s,x,z))surface=StairHeight(state,s,x,z);
            else continue;
            if(surface<=currentY+StepHeight)height=MathF.Max(height,surface);
        }
        return height;
    }
    public static float PieceHeight(StructureState s,ContentCatalog? catalog=null)=>RoofGeometry.IsRoofModule(s)?RoofGeometry.Rise(catalog==null?"roof_slope":BuildingVariants.Variant(catalog,s.Selection!).Geometry,s.Selection!.Slope)+.3f:IsFloor(s)?Deck:IsEdge(s)?WallHeight:s.Kind switch{"stairs"=>Storey,"roof_slope" or "roof_corner"=>1.2f,"roof" or "roof_flat" or "roof_triangle"=>.25f,"rug"=>.025f,_=>1.3f};
    public static bool SolidAt(WorldState state,StructureState s,BuildingDefinition definition,float x,float z,float feetY,float radius=.3f,ContentCatalog? catalog=null)
    {
        if(s.Kind=="rug")return false;
        if(catalog!=null&&RoofGeometry.IsRoofModule(s)&&RoofGeometry.IsFill(BuildingVariants.Variant(catalog,s.Selection!).Geometry))return RoofGeometry.BlocksPoint(state,catalog,s,x,z,feetY,radius);
        float bottom=Base(state,s),top=bottom+PieceHeight(s);
        if(feetY>=top-.04f||feetY+1.65f<=bottom+.04f)return false;
        if(s.Kind=="stairs")return Occupies(s,definition,x,z,0)&&StairHeight(state,s,x,z)>feetY+StepHeight;
        if(IsFloor(s))return Occupies(s,definition,x,z,radius)&&top>feetY+StepHeight;
        if(IsRoof(s))return false;
        return SolidAt(s,definition,x,z,radius);
    }
    private static float EdgeWidth(StructureState s)=>s.Kind=="wall_diagonal"?Tile*MathF.Sqrt(2):Tile;
    public static int Supports(WorldState state,StructureState floor,string except="")=>state.Structures.Count(s=>s.Id!=except&&IsEdge(s)&&s.Modular&&MathF.Abs(Base(state,s)-(Base(state,floor)+Deck))<.05f&&OnFloor(floor,s.X,s.Z,.06f));
    public static bool SharesEdge(StructureState a,StructureState b)
    {
        var ap=Polygon(a);var bp=Polygon(b);
        for(int i=0;i<ap.Length;i++)for(int j=0;j<bp.Length;j++)
            if(SharedSegment(ap[i],ap[(i+1)%ap.Length],bp[j],bp[(j+1)%bp.Length])>.2f)return true;
        return false;
    }
    private static float SharedSegment((float X,float Z) a,(float X,float Z) b,(float X,float Z) c,(float X,float Z) d)
    {
        float length=Distance(a.X,a.Z,b.X,b.Z);if(length<.001f)return 0;
        float ux=(b.X-a.X)/length,uz=(b.Z-a.Z)/length;
        if(MathF.Abs((c.X-a.X)*uz-(c.Z-a.Z)*ux)>.025f||MathF.Abs((d.X-a.X)*uz-(d.Z-a.Z)*ux)>.025f)return 0;
        float p=(c.X-a.X)*ux+(c.Z-a.Z)*uz,q=(d.X-a.X)*ux+(d.Z-a.Z)*uz;
        return MathF.Max(0,MathF.Min(length,MathF.Max(p,q))-MathF.Max(0,MathF.Min(p,q)));
    }

    // Support propagates through connected floor frames, including L/T-shaped rooms and courtyards.
    // Upper floors always refer downward, never to their peers, preventing circular dependencies.
    public static bool HasFrameSupport(WorldState state,StructureState floor,string except="")
    {
        var pending=new Queue<StructureState>();var seen=new HashSet<string>();pending.Enqueue(floor);
        while(pending.Count>0)
        {
            var next=pending.Dequeue();if(next.Id==except||!seen.Add(next.Id))continue;
            if(Supports(state,next,except)>0)return true;
            foreach(var s in state.Structures)
                if(s.Id!=except&&IsFloor(s)&&s.Level==floor.Level&&MathF.Abs(Base(state,s)-Base(state,next))<.05f&&
                   SharesEdge(next,s))pending.Enqueue(s);
        }
        return false;
    }
    public static bool CanRemove(WorldState state,StructureState piece,out string reason)
    {
        reason="";
        if(state.Structures.Any(s=>s.Id!=piece.Id&&(s.SupportId==piece.Id||s.RoofConnectionId==piece.Id)))
        {reason="Remove the pieces resting on this support first.";return false;}
        foreach(var dependent in state.Structures.Where(s=>s.Id!=piece.Id&&(IsRoof(s)||IsUpper(s.Kind)||s.Kind=="stairs")))
        {
            var floor=state.Structures.FirstOrDefault(s=>s.Id==dependent.SupportId);
            if(floor!=null&&dependent.Kind!="stairs"&&HasFrameSupport(state,floor)&&!HasFrameSupport(state,floor,piece.Id))
            {reason="This is the last connected support for a roof or upper floor.";return false;}
            if(floor!=null&&IsFloor(piece)&&!CoveredByFloors(dependent,state.Structures.Where(s=>s.Id!=piece.Id&&IsFloor(s)&&s.Level==floor.Level&&MathF.Abs(Base(state,s)-Base(state,floor))<.05f),dependent.Kind=="stairs"?1.25f:Tile,Tile))
            {reason="Remove the roof, upper floor or stairs above this footprint first.";return false;}
        }
        return true;
    }

    public static BuildPlan Plan(WorldState state,ContentCatalog catalog,IEnumerable<ResourceSpawn> resources,string kind,float rawX,float rawZ,float angle,int level=0,BuildSnapMode snapMode=BuildSnapMode.Object,BuildSelection? selection=null)
    {
        var plan=new BuildPlan(kind,rawX,rawZ,angle,0,"",false,"Choose a building piece.",level);
        if(!catalog.Buildings.TryGetValue(kind,out var def)&&selection==null)return plan;
        if(selection!=null)
        {
            try{def=BuildingVariants.Resolve(catalog,selection,level);kind=def.Id;selection=selection.Copy();plan=plan with{Kind=kind,Selection=selection,Costs=new(def.Costs)};}
            catch(InvalidDataException error){return plan with{Message=error.Message};}
        }
        if(def==null)return plan;
        if(kind=="roof_module"&&selection==null)return plan with{Message="Choose a roof shape and pitch."};
        if(!float.IsFinite(rawX)||!float.IsFinite(rawZ)||!float.IsFinite(angle))return plan with{Message="Choose ground within the valley."};
        if(level<0||level>2)return plan with{Message="Choose ground floor, first floor or second floor."};
        if(!Enum.IsDefined(snapMode))return plan with{Message="Choose object, grid or free placement."};
        if(IsFoundation(kind)&&level!=0)return plan with{Message="Foundations go on the ground floor. Choose an upper floor panel."};
        if(IsUpper(kind)&&level==0)return plan with{Message="Choose first or second floor for an upper floor panel."};
        if(kind=="stairs"&&level==2)return plan with{Message="Stairs can connect ground to first floor, or first to second floor."};
        float grid=snapMode==BuildSnapMode.Grid?.5f:.25f;
        float x=snapMode==BuildSnapMode.Free?rawX:MathF.Round(rawX/grid)*grid,z=snapMode==BuildSnapMode.Free?rawZ:MathF.Round(rawZ/grid)*grid,rotation=Quantize(angle);
        if(selection!=null)rotation=catalog.BuildingTools[selection.DefinitionId].AllowedRotations.Select(a=>a*MathF.PI/180).OrderBy(a=>AngleDistance(a,angle)).First();
        bool roofModule=kind=="roof_module";string geometry=roofModule?BuildingVariants.Variant(catalog,selection!).Geometry:"";bool trim=roofModule&&RoofGeometry.IsTrim(geometry);
        string snapLabel=snapMode switch{BuildSnapMode.Free=>"Free placement",BuildSnapMode.Grid=>"World grid · 0.5 m",_=>"Object snap · 0.25 m"};
        var floors=state.Structures.Where(s=>IsFloor(s)&&s.Level==level).ToArray();
        StructureState? floor=null;
        if(IsUpper(kind)||def.Placement=="roof")
        {
            var supporting=IsUpper(kind)?state.Structures.Where(s=>IsFloor(s)&&s.Level==level-1).ToArray():floors;
            floor=supporting.OrderBy(s=>Distance(rawX,rawZ,s.X,s.Z)+AngleDistance(rotation,s.Rotation)*.02f).FirstOrDefault(s=>Distance(rawX,rawZ,s.X,s.Z)<3.7f);
            if(floor==null)return plan with{X=x,Z=z,Elevation=Ground(state,x,z)+level*Storey,Message="Point at a supporting floor on the level below this piece.",SnapLabel=snapLabel};
            if(snapMode==BuildSnapMode.Object)
            {
                x=floor.X;z=floor.Z;
                if(!roofModule)rotation=IsTriangle(floor)?floor.Rotation:IsTriangle(new StructureState{Kind=kind})?floor.Rotation+MathF.Round((rotation-floor.Rotation)/(MathF.PI/2))*MathF.PI/2:floor.Rotation;
                snapLabel="Floor footprint";
            }
        }
        else if(def.Placement=="edge")
        {
            if(snapMode==BuildSnapMode.Object)
            {
                var snapped=SnapEdge(state,floors,def.Width,rawX,rawZ,rotation,level);
                if(snapped.HasValue){x=snapped.Value.X;z=snapped.Value.Z;rotation=snapped.Value.Rotation;snapLabel="Wall / floor connection";}
            }
            floor=floors.Where(s=>OnFloor(s,x,z,.1f)).OrderBy(s=>Distance(x,z,s.X,s.Z)).FirstOrDefault();
            if(floor==null)return plan with{X=x,Z=z,Rotation=rotation,Elevation=Ground(state,x,z)+level*Storey,Message="Place walls on a floor or along its edge.",SnapLabel=snapLabel};
        }
        else if(IsFoundation(kind))
        {
            if(snapMode==BuildSnapMode.Object)
            {
                var snapped=SnapFloor(kind,floors,rawX,rawZ,rotation);
                if(snapped.HasValue){x=snapped.Value.X;z=snapped.Value.Z;rotation=snapped.Value.Rotation;floor=snapped.Value.Anchor;snapLabel="Floor edge connection";}
            }
            var footprint=new StructureState{Kind=kind,X=x,Z=z,Rotation=rotation};
            floor??=floors.FirstOrDefault(s=>SharesEdge(s,footprint));
        }
        else
        {
            floor=floors.FirstOrDefault(s=>OnFloor(s,x,z));
            if(kind=="stairs"&&snapMode==BuildSnapMode.Object)
            {
                floor=floors.OrderBy(s=>Distance(rawX,rawZ,s.X,s.Z)).FirstOrDefault(s=>Distance(rawX,rawZ,s.X,s.Z)<3.7f);
                if(floor!=null){x=floor.X;z=floor.Z;rotation=floor.Rotation+Quantize(angle);snapLabel="Stair / floor connection";}
            }
            else if(snapMode==BuildSnapMode.Object)
            {
                if(floor!=null)
                {
                    var p=Local(floor,rawX,rawZ);var w=World(floor,MathF.Round(p.X*4)/4,MathF.Round(p.Z*4)/4);x=w.X;z=w.Z;rotation=floor.Rotation+Quantize(angle);snapLabel="Floor grid · 0.25 m";
                }
                else
                {
                    var near=state.Structures.Where(s=>s.Level==level&&!IsFloor(s)&&!IsRoof(s)&&Distance(rawX,rawZ,s.X,s.Z)<3).OrderBy(s=>Distance(rawX,rawZ,s.X,s.Z)).FirstOrDefault();
                    if(near!=null)
                    {
                        var p=Local(near,rawX,rawZ);float lx=p.X,lz=p.Z;
                        if(MathF.Abs(lx)<.2f)lx=0;if(MathF.Abs(lz)<.2f)lz=0;
                        if(lx!=p.X||lz!=p.Z){var w=World(near,lx,lz);x=w.X;z=w.Z;rotation=near.Rotation+Quantize(angle);snapLabel="Object alignment";}
                    }
                }
            }
            if(kind=="stairs"&&floor==null)return plan with{X=x,Z=z,Rotation=rotation,Message="Place stairs on a supporting floor.",SnapLabel=snapLabel};
            if(level>0&&floor==null)return plan with{X=x,Z=z,Elevation=Ground(state,x,z)+level*Storey,Message="Place furniture on a floor at the selected level."};
        }
        float elevation=Ground(state,x,z);
        if(floor!=null)elevation=Base(state,floor)+(def.Placement=="floor"?0:Deck)+(def.Placement=="roof"?WallHeight:0);
        if(IsUpper(kind))elevation=Base(state,floor!)+Storey;
        if(def.Placement=="floor"&&floor==null)
            elevation=Polygon(new StructureState{Kind=kind,X=x,Z=z,Rotation=rotation}).Max(p=>Ground(state,p.X,p.Z));
        string roofConnection="";
        if(roofModule)
        {
            if(trim)
            {
                var parent=RoofGeometry.TrimParent(state,catalog,selection!,snapMode==BuildSnapMode.Object?rawX:x,snapMode==BuildSnapMode.Object?rawZ:z,rotation,level,snapMode);
                if(parent==null)return plan with{X=x,Z=z,Rotation=rotation,Elevation=elevation,Message="Attach this piece to a matching roof shape, pitch and rotation.",SnapLabel=snapLabel};
                x=parent.X;z=parent.Z;elevation=Base(state,parent);roofConnection=parent.Id;floor=state.Structures.FirstOrDefault(s=>s.Id==parent.SupportId);snapLabel="Roof trim connection";
            }
            else if(snapMode==BuildSnapMode.Object)
            {
                var snapped=RoofGeometry.Snap(state,catalog,selection!,rawX,rawZ,rotation,level,out bool incompatible);
                if(snapped!=null)
                {
                    x=snapped.X;z=snapped.Z;elevation=snapped.Elevation;roofConnection=snapped.ConnectionId;
                    floor=floors.OrderBy(s=>Distance(x,z,s.X,s.Z)).FirstOrDefault(s=>OnFloor(s,x,z,.02f));snapLabel="Matching roof edge · height carried";
                }
                else if(incompatible)return plan with{X=x,Z=z,Rotation=rotation,Elevation=elevation,Message="Choose the same pitch as the roof edge you are joining.",SnapLabel=snapLabel};
            }
            if(floor==null)return plan with{X=x,Z=z,Rotation=rotation,Elevation=elevation,Message="Keep a supporting floor below this roof."};
            if(elevation<Base(state,floor)+Storey-.025f)return plan with{X=x,Z=z,Rotation=rotation,Elevation=elevation,Message="This connection would drop below the supporting wall tops."};
        }
        plan=plan with{X=x,Z=z,Rotation=rotation,Elevation=elevation,SupportId=IsFoundation(kind)?"":floor?.Id??"",SnapLabel=snapLabel,Costs=new(def.Costs),RoofConnectionId=roofConnection};
        BuildPlan Fail(string message)=>plan with{Message=message};
        if(selection!=null&&!catalog.BuildingTools[selection.DefinitionId].AllowedRotations.Any(a=>AngleDistance(a*MathF.PI/180,rotation)<.001f))return Fail("This tool cannot use that connection angle; choose an allowed rotation.");
        if(MathF.Abs(x)>50||MathF.Abs(z)>50)return Fail("Stay inside the valley boundary.");
        if(Distance(x,z,state.Player.X,state.Player.Z)>Reach)return Fail("Move closer. Build within six paces.");
        float span=def.Placement is "edge"? .25f:MathF.Min(def.Width,def.Depth)/2;
        if(MathF.Abs(x)<2+span||MathF.Abs(z)<2+span||MathF.Abs(x-27)<5+span)return Fail("Keep roads and the river clear.");
        if(VillageLots.Any(o=>Distance(x,z,o.X,o.Z)<o.Radius+MathF.Max(def.Width,def.Depth)/2))return Fail("Leave room around the village buildings.");
        if(Workstations.Village.Any(o=>Distance(x,z,o.X,o.Z)<1.3f+MathF.Max(def.Width,def.Depth)/2))return Fail("Leave room around the village workstations.");
        if(x>14&&x<25&&z>4&&z<17)return Fail("The village fields are already in use.");
        var candidate=new StructureState{Kind=kind,X=x,Z=z,Rotation=rotation,Elevation=elevation,Modular=true,SupportId=plan.SupportId,Level=level,Selection=selection,RoofConnectionId=roofConnection};
        float playerY=state.Player.Y??WalkHeight(state,state.Player.X,state.Player.Z);
        if(SolidAt(state,candidate,def,state.Player.X,state.Player.Z,playerY,.4f,catalog))return Fail("Step clear of the piece before placing it.");
        if(resources.Any(r=>!state.RemovedResources.Contains(r.Id)&&Occupies(candidate,def,r.X,r.Z,r.Kind=="tree"?.9f:.65f)))return Fail("Clear the tree, stone or bush from this spot first.");
        if(level==0&&state.Crops.Any(c=>Occupies(candidate,def,c.X,c.Z,.5f)))return Fail("Harvest the crop before building here.");
        if((def.Placement=="roof"||IsUpper(kind))&&!HasFrameSupport(state,floor!))return Fail("Add a wall or doorway to this connected floor frame for support.");
        var supportFloors=floor==null?Array.Empty<StructureState>():state.Structures.Where(s=>IsFloor(s)&&s.Level==floor.Level&&MathF.Abs(Base(state,s)-Base(state,floor))<.05f).ToArray();
        if((IsUpper(kind)||IsRoof(candidate)||kind=="stairs")&&!CoveredByFloors(candidate,supportFloors,def.Width,def.Depth))return Fail("Keep this piece over its supporting floor footprint.");
        if(IsEdge(candidate))
        {
            for(int i=0;i<=8;i++){var p=World(candidate,-def.Width/2+def.Width*i/8,0);if(!supportFloors.Any(s=>OnFloor(s,p.X,p.Z,.06f)))return Fail("Both ends of the wall need a supporting floor.");}
        }
        if(IsFoundation(kind))
        {
            foreach(var corner in Polygon(candidate))
            {float h=Ground(state,corner.X,corner.Z);if(h>elevation+.08f||elevation-h>.6f)return Fail("Level the soil first; the foundation must meet the ground.");}
        }
        foreach(var other in state.Structures)
        {
            var od=other.Selection==null?catalog.Buildings[other.Kind]:BuildingVariants.Resolve(catalog,other.Selection,other.Level);
            bool overlapping=Overlap(candidate,def,other,od,.025f);
            bool otherTrim=RoofGeometry.IsRoofModule(other)&&RoofGeometry.IsTrim(catalog,other.Selection!);
            bool fill=roofModule&&RoofGeometry.IsFill(geometry),otherFill=RoofGeometry.IsRoofModule(other)&&RoofGeometry.IsFill(BuildingVariants.Variant(catalog,other.Selection!).Geometry);
            if((fill&&(IsFloor(other)||IsEdge(other)||otherFill)&&GableIntersects(candidate,other,od,state,catalog))||
               (otherFill&&(IsFloor(candidate)||IsEdge(candidate))&&GableIntersects(other,candidate,def,state,catalog)))
                return Fail("Keep floors, walls and other gables clear of this gable panel.");
            if(overlapping&&((roofModule&&!trim&&(IsFloor(other)||IsEdge(other))&&RoofIntersects(candidate,def,other,od,state,catalog))||
               (RoofGeometry.IsRoofModule(other)&&!otherTrim&&(IsFloor(candidate)||IsEdge(candidate))&&RoofIntersects(other,od,candidate,def,state,catalog))))
                return Fail("Keep floors and walls clear of the pitched roof profile.");
            if((IsUpper(kind)&&other.Kind=="stairs"&&other.Level==level-1&&overlapping)||
               (kind=="stairs"&&IsFloor(other)&&other.Level==level+1&&overlapping))return Fail("Leave the floor above the stairs open; place a landing beside the high end.");
            if((kind=="stairs"&&IsRoof(other)&&!otherTrim&&other.Level==level&&overlapping)||
               (IsRoof(candidate)&&!trim&&other.Kind=="stairs"&&other.Level==level&&overlapping))return Fail("Leave the stair opening clear of roofs.");
            if(MathF.Abs(elevation-Base(state,other))<.05f&&overlapping&&
               ((IsRoof(candidate)&&IsFloor(other))||(IsFloor(candidate)&&IsRoof(other))))return Fail("A roof and an upper floor cannot occupy the same height.");
            if(other.Level!=level)continue;
            if((kind=="rug"&&od.Placement=="ground"&&other.Kind!="rug")||(other.Kind=="rug"&&def.Placement=="ground"&&kind!="rug"))continue;
            if(def.Placement=="roof"||od.Placement=="roof")
            {
                if(def.Placement==od.Placement&&overlapping)
                {
                    if(trim||otherTrim)
                    {
                        if(trim&&otherTrim&&roofConnection==other.RoofConnectionId&&geometry==BuildingVariants.Variant(catalog,other.Selection!).Geometry&&selection!.Mirrored==other.Selection!.Mirrored)return Fail("That roof trim is already attached.");
                    }
                    else return Fail("Roof pieces cannot overlap. Try a triangle or another connection.");
                }
                continue;
            }
            if(def.Placement=="edge"&&od.Placement=="floor"||def.Placement=="floor"&&od.Placement=="edge")continue;
            if((def.Placement=="ground"||def.Placement=="stairs")&&od.Placement=="floor"&&MathF.Abs(elevation-Base(state,other)-Deck)<.05f)continue;
            if(def.Placement=="stairs"&&od.Placement=="edge"||def.Placement=="edge"&&od.Placement=="stairs")
            {
                var stair=kind=="stairs"?candidate:other;var edge=kind=="stairs"?other:candidate;
                foreach(float end in new[]{-Tile/2,Tile/2}){var p=World(stair,0,end);if(Occupies(edge,kind=="stairs"?od:def,p.X,p.Z,.2f))return Fail("Keep both ends of the stairs clear for walking.");}
                continue;
            }
            if(def.Placement=="floor"&&od.Placement=="floor")
            {if(overlapping)return Fail("Floor footprints overlap. Connect their edges or choose a triangle.");continue;}
            if(def.Placement=="edge"&&od.Placement=="edge")
            {if(overlapping&&!WallEndsMeet(candidate,other))return Fail("That wall connection is occupied.");continue;}
            if(overlapping)return Fail("This piece overlaps another structure.");
        }
        foreach(var cost in def.Costs)if(state.Player.Inventory.GetValueOrDefault(cost.Key)<cost.Value)
            return Fail("Need "+string.Join(", ",def.Costs.Where(c=>state.Player.Inventory.GetValueOrDefault(c.Key)<c.Value).Select(c=>$"{c.Value-state.Player.Inventory.GetValueOrDefault(c.Key)} {_catalogName(catalog,c.Key)}"))+".");
        return plan with{Valid=true,Message=def.Placement switch{"floor"=>"Floor ready. Left click to place.","edge"=>"Supported wall. R rotates by 45 degrees.","roof"=>"Roof supported by connected frame. Left click to place.","stairs"=>"Stairs rise toward the narrow top edge. R rotates.",_=>"Clear placement. Left click to build."}};
    }
    private static string _catalogName(ContentCatalog c,string id)=>c.Items[id].Name.ToLowerInvariant();
    private static (float X,float Z)[] GablePolygon(StructureState piece)
    {
        float side=piece.Selection!.Mirrored?Tile/2:-Tile/2;
        return new[]{World(piece,side-.09f,-Tile/2),World(piece,side+.09f,-Tile/2),World(piece,side+.09f,Tile/2),World(piece,side-.09f,Tile/2)};
    }
    private static bool GableIntersects(StructureState fill,StructureState solid,BuildingDefinition solidDefinition,WorldState state,ContentCatalog catalog)
    {
        bool otherFill=RoofGeometry.IsRoofModule(solid)&&RoofGeometry.IsFill(BuildingVariants.Variant(catalog,solid.Selection!).Geometry);
        var overlap=Clip(GablePolygon(fill),otherFill?GablePolygon(solid):Polygon(solid,solidDefinition.Width,solidDefinition.Depth));
        if(overlap.Length<3||Area(overlap)<.0001f)return false;
        float Top(StructureState panel)
        {
            string shape=BuildingVariants.Variant(catalog,panel.Selection!).Geometry;
            var samples=RoofGeometry.ProfileSamples(shape,overlap.Select(p=>Local(panel,p.X,p.Z)).ToArray());
            return Base(state,panel)+samples.Max(p=>RoofGeometry.GableTop(shape,panel.Selection!.Slope,Math.Clamp(p.Z,-Tile/2,Tile/2)));
        }
        float bottom=Base(state,fill)+RoofGeometry.GableBottom(state,fill),top=Top(fill);
        float solidBottom=Base(state,solid)+(otherFill?RoofGeometry.GableBottom(state,solid):0),solidTop=otherFill?Top(solid):Base(state,solid)+PieceHeight(solid,catalog);
        return top>solidBottom+.025f&&bottom<solidTop-.025f;
    }
    private static bool RoofIntersects(StructureState roof,BuildingDefinition roofDefinition,StructureState solid,BuildingDefinition solidDefinition,WorldState state,ContentCatalog catalog)
    {
        var overlap=Clip(Polygon(roof,roofDefinition.Width,roofDefinition.Depth),Polygon(solid,solidDefinition.Width,solidDefinition.Depth));
        if(overlap.Length<3||Area(overlap)<.0001f)return false;
        string geometry=BuildingVariants.Variant(catalog,roof.Selection!).Geometry;
        var local=RoofGeometry.ProfileSamples(geometry,overlap.Select(p=>Local(roof,p.X,p.Z)).ToArray());
        float baseY=Base(state,roof),bottom=Base(state,solid),top=bottom+PieceHeight(solid,catalog);
        float min=local.Min(p=>baseY+RoofGeometry.SurfaceHeight(geometry,roof.Selection!.Slope,p.X,p.Z));
        float max=local.Max(p=>baseY+RoofGeometry.SurfaceHeight(geometry,roof.Selection!.Slope,p.X,p.Z));
        return max>bottom+.025f&&min-.08f<top-.025f;
    }
    private static float Quantize(float angle)=>MathF.Round(angle/(MathF.PI/4))*(MathF.PI/4);
    private static float AngleDistance(float a,float b)=>MathF.Abs(MathF.IEEERemainder(a-b,MathF.PI*2));
    private static bool WallEndsMeet(StructureState a,StructureState b)
    {
        if(MathF.Abs(MathF.Sin(a.Rotation-b.Rotation))<.02f&&Distance(a.X,a.Z,b.X,b.Z)<(EdgeWidth(a)+EdgeWidth(b))/2-.08f)return false;
        foreach(float sa in new[]{-1f,1f})foreach(float sb in new[]{-1f,1f})
        {var ap=World(a,sa*EdgeWidth(a)/2,0);var bp=World(b,sb*EdgeWidth(b)/2,0);if(Distance(ap.X,ap.Z,bp.X,bp.Z)<.08f)return true;}
        return false;
    }
    private static (float X,float Z,float Rotation)? SnapEdge(WorldState state,StructureState[] floors,float width,float x,float z,float rotation,int level)
    {
        var candidates=new List<(float X,float Z,float Rotation)>();
        foreach(var floor in floors.Where(s=>Distance(x,z,s.X,s.Z)<4.9f))
        {
            var polygon=Polygon(floor);
            for(int i=0;i<polygon.Length;i++)
            {
                var a=polygon[i];var b=polygon[(i+1)%polygon.Length];
                if(MathF.Abs(Distance(a.X,a.Z,b.X,b.Z)-width)>.05f)continue;
                float yaw=Quantize(-MathF.Atan2(b.Z-a.Z,b.X-a.X));
                candidates.Add(((a.X+b.X)/2,(a.Z+b.Z)/2,yaw));
            }
        }
        foreach(var edge in state.Structures.Where(s=>s.Level==level&&IsEdge(s)&&Distance(x,z,s.X,s.Z)<6))
        foreach(float end in new[]{-1f,1f})foreach(float direction in new[]{-1f,1f})
        {
            var p=World(edge,end*EdgeWidth(edge)/2,0);var extension=new StructureState{X=p.X,Z=p.Z,Rotation=rotation};var c=World(extension,direction*width/2,0);
            if(floors.Any(f=>OnFloor(f,c.X,c.Z,.06f)))candidates.Add((c.X,c.Z,rotation));
        }
        candidates=candidates.Where(c=>Distance(x,z,c.X,c.Z)<2.1f).ToList();
        if(candidates.Count==0)return null;
        return candidates.OrderBy(c=>Distance(x,z,c.X,c.Z)+MathF.Min(AngleDistance(c.Rotation,rotation),AngleDistance(c.Rotation+MathF.PI,rotation))*.03f).First();
    }
    private static (float X,float Z,float Rotation,StructureState Anchor)? SnapFloor(string kind,StructureState[] floors,float x,float z,float rotation)
    {
        var candidates=new List<(float X,float Z,float Rotation,StructureState Anchor)>();
        var prototype=new StructureState{Kind=kind};var localPolygon=Polygon(prototype);
        foreach(var anchor in floors.Where(s=>Distance(x,z,s.X,s.Z)<5))
        {
            // Preserve an occupied square slot as an invalid preview instead of jumping away from the cursor.
            if(!IsTriangle(prototype)&&!IsTriangle(anchor))candidates.Add((anchor.X,anchor.Z,anchor.Rotation,anchor));
            var target=Polygon(anchor);
            for(int a=0;a<target.Length;a++)for(int b=0;b<localPolygon.Length;b++)
            {
                var start=target[a];var end=target[(a+1)%target.Length];var ls=localPolygon[b];var le=localPolygon[(b+1)%localPolygon.Length];
                if(MathF.Abs(Distance(start.X,start.Z,end.X,end.Z)-Distance(ls.X,ls.Z,le.X,le.Z))>.025f)continue;
                float yaw=Quantize(MathF.Atan2(le.Z-ls.Z,le.X-ls.X)-MathF.Atan2(start.Z-end.Z,start.X-end.X));
                var origin=new StructureState{Kind=kind,Rotation=yaw};var translated=World(origin,ls.X,ls.Z);origin.X=end.X-translated.X;origin.Z=end.Z-translated.Z;
                var polygon=Polygon(origin);
                if(floors.Any(s=>PolygonsOverlap(polygon,Polygon(s),.025f)))continue;
                candidates.Add((origin.X,origin.Z,yaw,anchor));
            }
        }
        candidates=candidates.Where(c=>Distance(x,z,c.X,c.Z)<2.1f).ToList();
        if(candidates.Count==0)return null;
        return candidates.OrderBy(c=>Distance(x,z,c.X,c.Z)+AngleDistance(c.Rotation,rotation)*.005f).First();
    }
    public static bool CoveredByFloors(StructureState piece,IEnumerable<StructureState> floors,float width=Tile,float depth=Tile)
    {
        var polygon=Polygon(piece,width,depth);float area=Area(polygon);float covered=0;
        foreach(var floor in floors)covered+=Area(Clip(polygon,Polygon(floor)));
        return covered>=area-.015f;
    }
    private static float Cross((float X,float Z) a,(float X,float Z) b,(float X,float Z) c)=>(b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X);
    private static float Area(IReadOnlyList<(float X,float Z)> polygon)
    {
        float sum=0;for(int i=0;i<polygon.Count;i++){var a=polygon[i];var b=polygon[(i+1)%polygon.Count];sum+=a.X*b.Z-b.X*a.Z;}return MathF.Abs(sum)/2;
    }
    private static (float X,float Z)[] Clip((float X,float Z)[] subject,(float X,float Z)[] clip)
    {
        var output=subject.ToList();
        for(int i=0;i<clip.Length&&output.Count>0;i++)
        {
            var a=clip[i];var b=clip[(i+1)%clip.Length];var input=output;output=new();var previous=input[^1];float old=Cross(a,b,previous);
            foreach(var current in input)
            {
                float side=Cross(a,b,current);
                if((side>=-.00001f)!=(old>=-.00001f))
                {float t=old/(old-side);output.Add((previous.X+(current.X-previous.X)*t,previous.Z+(current.Z-previous.Z)*t));}
                if(side>=-.00001f)output.Add(current);previous=current;old=side;
            }
        }
        return output.ToArray();
    }
    // Convex polygon SAT handles rotated square and half-square footprints using their actual edges.
    private static bool Overlap(StructureState a,BuildingDefinition ad,StructureState b,BuildingDefinition bd,float inset)
        =>PolygonsOverlap(Polygon(a,ad.Width,ad.Depth),Polygon(b,bd.Width,bd.Depth),inset);
    private static bool PolygonsOverlap((float X,float Z)[] a,(float X,float Z)[] b,float inset)
    {
        foreach(var polygon in new[]{a,b})for(int i=0;i<polygon.Length;i++)
        {
            var p=polygon[i];var q=polygon[(i+1)%polygon.Length];float length=Distance(p.X,p.Z,q.X,q.Z);float nx=-(q.Z-p.Z)/length,nz=(q.X-p.X)/length;
            float amin=a.Min(v=>v.X*nx+v.Z*nz),amax=a.Max(v=>v.X*nx+v.Z*nz),bmin=b.Min(v=>v.X*nx+v.Z*nz),bmax=b.Max(v=>v.X*nx+v.Z*nz);
            if(amax<=bmin+inset||bmax<=amin+inset)return false;
        }
        return true;
    }
}
