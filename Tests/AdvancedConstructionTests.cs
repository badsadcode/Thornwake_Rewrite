using Thornwake.Core;

internal static class AdvancedConstructionTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool value,string reason){if(!value)throw new Exception("ADVANCED CONSTRUCTION: "+reason);checks++;}
        WorldState Fresh()
        {
            var state=new WorldState{Seed=7319};
            foreach(var resource in WorldGenerator.Generate(state.Seed))state.RemovedResources.Add(resource.Id);
            foreach(var id in catalog.Items.Keys)state.Player.Inventory[id]=100;
            return state;
        }
        BuildPlan Preview(WorldState state,string kind,float x,float z,int level=0,float rotation=0)
        {
            state.Player.X=x+4;state.Player.Z=z;
            return Construction.Plan(state,catalog,Array.Empty<ResourceSpawn>(),kind,x,z,rotation,level);
        }
        StructureState Place(WorldState state,string kind,float x,float z,int level=0,float rotation=0)
        {
            var plan=Preview(state,kind,x,z,level,rotation);
            Check(plan.Valid,$"place {kind} level {level} at {x},{z}: {plan.Message}");
            var piece=new StructureState{Id=Guid.NewGuid().ToString("N"),Kind=kind,X=plan.X,Z=plan.Z,Rotation=plan.Rotation,
                Elevation=plan.Elevation,Level=plan.Level,SupportId=plan.SupportId,Modular=true};
            state.Structures.Add(piece);return piece;
        }

        // Coverage follows an irregular floor frame without requiring two walls under every tile.
        var irregular=Fresh();
        var a=Place(irregular,"foundation",-15,23);
        var b=Place(irregular,"foundation",-12,23);
        var c=Place(irregular,"foundation",-12,20);
        Check(!Preview(irregular,"roof",c.X,c.Z).Valid,"roof still requires a real frame support");
        var wall=Place(irregular,"wall",-15,21.5f);
        Place(irregular,"roof",a.X,a.Z);
        Place(irregular,"roof_flat",b.X,b.Z);
        Place(irregular,"roof_corner",c.X,c.Z);
        Check(!Preview(irregular,"roof_slope",c.X,c.Z).Valid,"different roof shapes share one slot");
        Check(!Construction.CanRemove(irregular,wall,out _),"last wall supporting connected roofs is protected");
        var secondWall=Place(irregular,"wall",-12,18.5f);
        Check(Construction.CanRemove(irregular,wall,out _),"a redundant wall can be removed");
        irregular.Structures.Remove(wall);
        Check(!Construction.CanRemove(irregular,secondWall,out _),"remaining support is now essential");
        var separate=Place(irregular,"foundation",-18,29);
        Check(!Preview(irregular,"roof",separate.X,separate.Z).Valid,"separate frame cannot borrow remote support");
        Check(!Preview(irregular,"foundation",-15,23,1).Valid,"foundations cannot float upstairs");
        Check(!Preview(irregular,"upper_floor",-15,23,0).Valid,"upper floors need an upper level");
        Check(!Preview(irregular,"upper_floor",-15,23,3).Valid,"third upper level outside prototype limit");
        Check(!Preview(irregular,"upper_floor",a.X,a.Z,1).Valid,"roof must be removed before covering same height with upper floor");

        // Two flights are possible with a deliberate open stairwell and a landing at each high end.
        var house=Fresh();
        var south=Place(house,"foundation",-15,23);
        var middle=Place(house,"foundation",-15,20);
        var north=Place(house,"foundation",-15,17);
        var support=Place(house,"wall",-16.5f,23,0,MathF.PI/2);
        var stairs=Place(house,"stairs",-15,23,0,MathF.PI);
        Check(!Preview(house,"upper_floor",-15,23,1).Valid,"upper deck cannot seal the stairwell");
        Check(!Preview(house,"roof",-15,23).Valid,"roof cannot seal the stairwell");
        Check(!Preview(house,"wall",-15,21.5f).Valid,"wall cannot block stair exit");
        var landing=Place(house,"upper_floor",-15,20,1);
        var northUpper=Place(house,"upper_floor",-15,17,1);
        Check(landing.SupportId==middle.Id&&landing.Level==1,"upper floor has downward dependency and correct level");
        Check(MathF.Abs(Construction.Base(house,landing)-Construction.Base(house,middle)-Construction.Storey)<.001f,"storey height aligns");
        var upperWall=Place(house,"wall",-16.5f,20,1,MathF.PI/2);
        var upperStairs=Place(house,"stairs",-15,20,1,MathF.PI);
        var secondLanding=Place(house,"upper_floor",-15,17,2);
        Check(!Preview(house,"stairs",-15,17,2).Valid,"stairs cannot lead to unsupported level three");
        Check(!Preview(house,"workbench",-12,17,2).Valid,"upstairs furniture needs a real floor");
        var bench=Place(house,"workbench",-15,17,2);
        Check(bench.SupportId==secondLanding.Id,"upstairs furniture attaches to its floor");
        Check(!Construction.CanRemove(house,secondLanding,out _),"furniture protects floor support");
        Check(!Construction.CanRemove(house,support,out _),"wall supports upper frame even if its tile has no deck above");
        Check(!Construction.CanRemove(house,upperWall,out _),"upper wall supports second storey");

        float groundDeck=Construction.Base(house,middle)+Construction.Deck;
        Check(MathF.Abs(Construction.WalkHeight(house,-15,20,groundDeck)-groundDeck)<.001f,"walking under upper deck does not teleport upstairs");
        Check(MathF.Abs(Construction.WalkHeight(house,-15,20)-groundDeck)<.001f,"legacy ground-height query ignores upper decks");
        float y=Construction.Base(house,south)+Construction.Deck;
        for(float z=24.49f;z>=21.51f;z-=.04f)
        {
            Check(!Construction.SolidAt(house,stairs,catalog.Buildings["stairs"],-15,z,y),"gradual stair ascent remains walkable");
            y=Construction.WalkHeight(house,-15,z,y);
        }
        y=Construction.WalkHeight(house,-15,21.49f,y);
        Check(MathF.Abs(y-(Construction.Base(house,landing)+Construction.Deck))<.03f,"stair ramp reaches upper landing without jump");
        Check(Construction.SolidAt(house,stairs,catalog.Buildings["stairs"],-15,21.6f,groundDeck),"high end cannot be entered from ground");
        Check(MathF.Abs(Construction.WalkHeight(house,-14,22,groundDeck+Construction.Storey)-(Construction.Base(house,south)+Construction.Deck))<.001f,"stair walking surface respects narrow visual width");
        Check(!Construction.SolidAt(house,support,catalog.Buildings["wall"],support.X,support.Z,y),"ground wall does not block walking upstairs");
        Check(!Construction.SolidAt(house,upperWall,catalog.Buildings["wall"],upperWall.X,upperWall.Z,groundDeck),"upper wall does not block walking downstairs");
        Check(Construction.SolidAt(house,upperWall,catalog.Buildings["wall"],upperWall.X,upperWall.Z,y),"upper wall blocks at correct height");
        float upperY=Construction.Base(house,landing)+Construction.Deck;
        for(float z=21.49f;z>=18.51f;z-=.04f)upperY=Construction.WalkHeight(house,-15,z,upperY);
        upperY=Construction.WalkHeight(house,-15,18.49f,upperY);
        Check(MathF.Abs(upperY-(Construction.Base(house,secondLanding)+Construction.Deck))<.001f,"second flight reaches second floor");
        return checks;
    }
}
