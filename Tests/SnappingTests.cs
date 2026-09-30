using Thornwake.Core;

internal static class SnappingTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception("SNAPPING: "+message);checks++;}
        bool Near(float a,float b)=>MathF.Abs(a-b)<.002f;
        WorldState Fresh()
        {
            var state=new WorldState{Seed=7319};foreach(var item in catalog.Items.Keys)state.Player.Inventory[item]=100;
            return state;
        }
        BuildPlan Preview(WorldState state,string kind,float x,float z,float yaw=0,int level=0,BuildSnapMode mode=BuildSnapMode.Object)
        {
            state.Player.X=x+4;state.Player.Z=z;state.Player.Y=null;
            return Construction.Plan(state,catalog,Array.Empty<ResourceSpawn>(),kind,x,z,yaw,level,mode);
        }
        StructureState Place(WorldState state,string kind,float x,float z,float yaw=0,int level=0,BuildSnapMode mode=BuildSnapMode.Object)
        {
            var plan=Preview(state,kind,x,z,yaw,level,mode);Check(plan.Valid,$"{kind} at {x},{z}: {plan.Message}");
            var piece=new StructureState{Id=Guid.NewGuid().ToString("N"),Kind=kind,X=plan.X,Z=plan.Z,Rotation=plan.Rotation,Elevation=plan.Elevation,SupportId=plan.SupportId,Level=plan.Level,Modular=true};
            state.Structures.Add(piece);return piece;
        }

        var free=Preview(Fresh(),"foundation",-25.13f,20.27f,.8f,0,BuildSnapMode.Free);
        Check(free.Valid&&Near(free.X,-25.13f)&&Near(free.Z,20.27f),"free mode preserves cursor position");
        Check(Near(free.Rotation,MathF.PI/4)&&free.SnapLabel=="Free placement","free mode retains 45-degree rotation and identifies mode");
        var grid=Preview(Fresh(),"foundation",-25.26f,20.26f,.8f,0,BuildSnapMode.Grid);
        Check(grid.Valid&&Near(grid.X,-25.5f)&&Near(grid.Z,20.5f),"world grid uses half-metre increments");
        var loose=Preview(Fresh(),"foundation",-25.13f,20.27f);
        Check(loose.Valid&&Near(loose.X,-25.25f)&&Near(loose.Z,20.25f),"first object-mode foundation has a fine quarter grid");
        Check(!Preview(Fresh(),"foundation",-25,20,0,0,(BuildSnapMode)42).Valid,"invalid snap mode is rejected");

        var angled=Fresh();var first=Place(angled,"foundation",-18,20,MathF.PI/4);
        var port=Construction.World(first,3,0);var second=Place(angled,"foundation",port.X+.1f,port.Z-.08f);
        Check(Near(second.X,port.X)&&Near(second.Z,port.Z),"floor follows rotated edge port instead of world axes");
        Check(Construction.SharesEdge(first,second)&&Near(Construction.Base(angled,first),Construction.Base(angled,second)),"rotated adjoining floors share a level edge");
        var inside=Construction.World(first,1.4f,1.4f);var outside=Construction.World(first,1.7f,0);
        Check(Construction.OnFloor(first,inside.X,inside.Z)&&!Construction.OnFloor(first,outside.X,outside.Z),"walkable square footprint rotates with floor");
        var north=Construction.World(first,0,-1.5f);var wall=Place(angled,"wall",north.X,north.Z);
        Check(Near(wall.X,north.X)&&Near(wall.Z,north.Z),"wall attaches to rotated boundary");
        Check(Construction.Supports(angled,first)==1&&Construction.HasFrameSupport(angled,second),"rotated shared-edge frame transfers support");
        Place(angled,"roof",second.X,second.Z);
        Check(!Construction.CanRemove(angled,wall,out _),"last support of rotated connected roof is protected");
        var chairRaw=Construction.World(first,.37f,.62f);var chair=Preview(angled,"chair",chairRaw.X,chairRaw.Z);
        var chairLocal=Construction.Local(first,chair.X,chair.Z);
        Check(chair.Valid&&Near(chairLocal.X,.25f)&&Near(chairLocal.Z,.5f),"furniture uses the rotated floor's quarter grid");

        var triangleHouse=Fresh();var triangle=Place(triangleHouse,"foundation_triangle",-15,23);
        Check(Construction.OnFloor(triangle,-16,22)&&!Construction.OnFloor(triangle,-14,24),"triangle only occupies its actual half-square");
        Check(!Construction.Occupies(triangle,catalog.Buildings[triangle.Kind],-14,24),"resource and crop tests ignore missing triangle half");
        var mate=Place(triangleHouse,"foundation_triangle",-15,23,MathF.PI);
        Check(Near(mate.X,triangle.X)&&Near(mate.Z,triangle.Z)&&Construction.SharesEdge(triangle,mate),"complementary triangles mate along the diagonal without overlap");
        Check(!Preview(triangleHouse,"foundation_triangle",-15,23,0,0,BuildSnapMode.Free).Valid,"duplicate triangular footprint is rejected");
        var diagonal=Place(triangleHouse,"wall_diagonal",-15,23,MathF.PI/4);
        Check(Construction.Supports(triangleHouse,triangle)==1&&Construction.Supports(triangleHouse,mate)==1,"diagonal wall supports both adjoining triangular frames");
        var upper=Place(triangleHouse,"upper_floor",-15,23,0,1);
        Check(Construction.CoveredByFloors(upper,new[]{triangle,mate}),"two half floors support one complete upper floor");
        var nonReference=upper.SupportId==triangle.Id?mate:triangle;
        Check(!Construction.CanRemove(triangleHouse,nonReference,out _),"removal protects every supporting half, including non-reference floor");
        Check(!Construction.CanRemove(triangleHouse,diagonal,out _),"diagonal wall remains a real upper-floor support");

        var roofs=Fresh();var ta=Place(roofs,"foundation_triangle",-15,23);var tb=Place(roofs,"foundation_triangle",-15,23,MathF.PI);
        Place(roofs,"wall_diagonal",-15,23,MathF.PI/4);
        var ra=Place(roofs,"roof_triangle",-15,23);var rb=Place(roofs,"roof_triangle",-15,23,MathF.PI);
        Check(Construction.SharesEdge(ra,rb)&&Near(ra.Elevation!.Value,rb.Elevation!.Value),"triangular roof halves cover the same room without collision");
        Check(!Preview(roofs,"roof_triangle",-15,23,0,0,BuildSnapMode.Free).Valid,"duplicate triangular roofs cannot overlap");
        Check(!Construction.OnFloor(ra,-14,24)&&Construction.OnFloor(rb,-14,24),"triangle roof footprint selects the intended half");

        var partial=Fresh();var half=Place(partial,"foundation_triangle",-15,23);
        Place(partial,"wall_diagonal",-15,23,MathF.PI/4);
        Check(!Preview(partial,"upper_floor",-15,23,0,1).Valid,"half footprint cannot support a full upper square");
        Place(partial,"upper_floor_triangle",-15,23,0,1);
        Check(!Preview(partial,"roof",-15,23).Valid,"square roof cannot cover an unsupported missing half");

        var open=Fresh();Place(open,"foundation",-15,23);Place(open,"foundation",-12,23);Place(open,"wall",-15,21.5f);
        var offsetRoof=Preview(open,"roof",-14,23,0,0,BuildSnapMode.Free);
        Check(offsetRoof.Valid&&Near(offsetRoof.X,-14),"free roof can span supported neighboring floors without center snapping");
        Check(!Preview(open,"roof",-14,21.8f,0,0,BuildSnapMode.Free).Valid,"free mode preserves physical support requirements");
        var interior=Place(open,"wall",-12,23,MathF.PI/4,0,BuildSnapMode.Free);
        Check(Near(interior.Rotation,MathF.PI/4),"free mode permits supported 45-degree interior walls");
        Check(!Preview(open,"wall",-12,23,-MathF.PI/4,0,BuildSnapMode.Free).Valid,"free interior walls cannot cross through each other");

        var steps=Fresh();var stepFloor=Place(steps,"foundation",-18,20,MathF.PI/4);var stairs=Place(steps,"stairs",stepFloor.X,stepFloor.Z);
        Check(Near(stairs.Rotation,stepFloor.Rotation),"object stairs align to rotated floor frame");
        float height=Construction.Base(steps,stairs);
        for(float z=-1.49f;z<=1.49f;z+=.05f)
        {
            var p=Construction.World(stairs,0,z);float surface=Construction.WalkHeight(steps,p.X,p.Z,height);
            Check(surface>=height-.002f&&surface-height<Construction.StepHeight,"rotated stair surface ascends continuously");height=surface;
        }
        var under=Construction.World(stairs,.9f,0);
        Check(!Construction.OnStairs(stairs,under.X,under.Z),"rotated stairs keep their narrow width");

        var spanning=Fresh();var left=Place(spanning,"foundation",-15,23);var right=Place(spanning,"foundation",-12,23);
        var across=Place(spanning,"stairs",-13.5f,23,0,0,BuildSnapMode.Free);
        var secondary=across.SupportId==left.Id?right:left;
        Check(!Construction.CanRemove(spanning,secondary,out _),"free staircase protects both supporting floors, not only its saved reference");
        var spareWall=Place(spanning,"wall",-16.5f,23,MathF.PI/2);
        Check(Construction.CanRemove(spanning,spareWall,out _),"stairs do not unnecessarily require wall support");

        // Quarter-turn and diagonal transforms preserve every triangle vertex and reject its missing corner.
        for(int turn=0;turn<8;turn++)
        {
            var shape=new StructureState{Kind="foundation_triangle",X=-25,Z=25,Rotation=turn*MathF.PI/4};
            foreach(var vertex in Construction.Polygon(shape))Check(Construction.OnFloor(shape,vertex.X,vertex.Z,.002f),"rotated triangle vertex remains supported");
            var absent=Construction.World(shape,1,1);Check(!Construction.OnFloor(shape,absent.X,absent.Z),"missing triangle half rotates correctly");
        }
        return checks;
    }
}
