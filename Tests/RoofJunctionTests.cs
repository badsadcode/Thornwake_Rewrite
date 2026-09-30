using System.Text.Json;
using Thornwake.Core;

internal static class RoofJunctionTests
{
    public static int Run(ContentCatalog catalog)
    {
        int checks=0;
        void Check(bool value,string message){if(!value)throw new Exception("ROOF JUNCTIONS: "+message);checks++;}
        bool Near(float a,float b)=>MathF.Abs(a-b)<.004f;
        WorldState Clone(WorldState state)=>JsonSerializer.Deserialize<WorldState>(JsonSerializer.Serialize(state))!;
        BuildSelection Choice(string variant,float pitch=45,bool mirrored=false)=>new()
        {DefinitionId=variant is "gable_fill" or "gable_ridge_fill"?"wall":"roof",VariantId=variant,Slope=pitch,MaterialId="timber",Mirrored=mirrored};
        StructureState Piece(string variant,float pitch,float x=0,float z=0,float yaw=0,float elevation=4,bool mirrored=false)=>new()
        {Id=Guid.NewGuid().ToString("N"),Kind="roof_module",X=x,Z=z,Rotation=yaw,Elevation=elevation,Level=0,Modular=true,Selection=Choice(variant,pitch,mirrored)};

        float[] pitches={22.5f,30,45,60};
        foreach(float pitch in pitches)
        {
            float tan=MathF.Tan(pitch*MathF.PI/180),rise=3*tan;
            foreach(string id in new[]{"slope","ridge","hip","valley","gable_edge","eave"})
                Check(BuildingVariants.Variant(catalog,Choice(id,pitch)).Slopes.Contains(pitch),$"{id} exposes {pitch} degree pitch");
            foreach(string id in new[]{"gable_fill","gable_ridge_fill"})
                Check(BuildingVariants.Variant(catalog,Choice(id,pitch)).Slopes.Contains(pitch),$"wall {id} exposes {pitch} degree pitch");
            Check(Near(RoofGeometry.Rise("roof_hip",pitch),rise)&&Near(RoofGeometry.Rise("roof_valley",pitch),rise),$"hip and valley retain full rise at {pitch}");
            Check(Near(RoofGeometry.SurfaceHeight("roof_hip",pitch,1.5f,1.5f),RoofGeometry.Skin+rise),"hip high corner");
            Check(Near(RoofGeometry.SurfaceHeight("roof_hip",pitch,-1.5f,1.5f),RoofGeometry.Skin)&&Near(RoofGeometry.SurfaceHeight("roof_hip",pitch,1.5f,-1.5f),RoofGeometry.Skin),"hip two low eaves");
            Check(Near(RoofGeometry.SurfaceHeight("roof_valley",pitch,-1.5f,-1.5f),RoofGeometry.Skin),"valley low corner");
            Check(Near(RoofGeometry.SurfaceHeight("roof_valley",pitch,-1.5f,1.5f),RoofGeometry.Skin+rise)&&Near(RoofGeometry.SurfaceHeight("roof_valley",pitch,1.5f,-1.5f),RoofGeometry.Skin+rise),"valley two high eaves");
            foreach(float along in new[]{-1.2f,-.5f,0,.8f,1.2f})
            {
                float crease=RoofGeometry.Skin+(along+1.5f)*tan;
                Check(Near(RoofGeometry.SurfaceHeight("roof_hip",pitch,along,along),crease)&&Near(RoofGeometry.SurfaceHeight("roof_valley",pitch,along,along),crease),"both profiles meet their diagonal crease");
                const float side=.1f;
                Check(Near(RoofGeometry.SurfaceHeight("roof_hip",pitch,along+side,along-side),crease-side*tan),"hip cross-section falls away from diagonal ridge");
                Check(Near(RoofGeometry.SurfaceHeight("roof_valley",pitch,along+side,along-side),crease+side*tan),"valley cross-section rises away from diagonal trough");
            }

            // These poses describe two perpendicular roof runs. Transform the whole joint together,
            // so a successful connection cannot depend on world axes or a particular base elevation.
            foreach(float yaw in new[]{0f,MathF.PI/4,MathF.PI/2,MathF.PI,MathF.PI*1.5f})
            foreach(string corner in new[]{"hip","valley"})
            {
                var junction=Piece(corner,pitch,-7,12,yaw,6.25f);
                float direction=corner=="hip"?1:-1;
                var side=Construction.World(junction,3*direction,0);var end=Construction.World(junction,0,3*direction);
                var first=Piece("slope",pitch,side.X,side.Z,yaw,6.25f);
                var second=Piece("slope",pitch,end.X,end.Z,yaw+MathF.PI/2,6.25f);
                Check(RoofGeometry.Connected(catalog,junction,first)&&RoofGeometry.Connected(catalog,first,junction),$"{corner} first run joins at pitch {pitch}, rotation {yaw}");
                Check(RoofGeometry.Connected(catalog,junction,second)&&RoofGeometry.Connected(catalog,second,junction),$"{corner} perpendicular run joins with complete edge profiles");
                second.Elevation+=.12f;Check(!RoofGeometry.Connected(catalog,junction,second),"horizontal alignment cannot hide a vertical gap");second.Elevation-=.12f;
                second.Selection!.Slope=pitch==45?30:45;Check(!RoofGeometry.Connected(catalog,junction,second),"joining profiles must have matching pitches");
            }
        }

        (WorldState State,Simulation Sim) Fixture()
        {
            var state=new WorldState{Seed=7319};foreach(string id in catalog.Items.Keys)state.Player.Inventory[id]=500;
            foreach(var spawn in WorldGenerator.Generate(state.Seed))state.RemovedResources.Add(spawn.Id);
            var sim=new Simulation(state,catalog);
            void BuildFloor(float x,float z)
            {Stand(state,x,z);var result=sim.Build("foundation",x,z,0);Check(result.Success,"fixture foundation: "+result.Message);}
            BuildFloor(-12,26);
            foreach(float z in new[]{26f,23f,20f})foreach(float x in new[]{-12f,-15f,-18f})if(x!=-12||z!=26)BuildFloor(x,z);
            Stand(state,-16.5f,20);var support=sim.Build("wall",-16.5f,20,MathF.PI/2);Check(support.Success,"fixture wall: "+support.Message);
            return(state,sim);
        }
        void Stand(WorldState state,float x,float z){state.Player.X=x+4;state.Player.Z=z;state.Player.Y=null;}
        StructureState Build(WorldState state,Simulation sim,BuildSelection selection,float x,float z,float yaw=0)
        {
            Stand(state,x,z);var result=sim.Build("roof_module",x,z,yaw,0,BuildSnapMode.Object,selection);
            Check(result.Success,$"place {selection.VariantId} {selection.Slope}°: {result.Message}");return state.Structures[^1];
        }

        foreach(float pitch in pitches)
        foreach(string corner in new[]{"hip","valley"})
        {
            var (state,sim)=Fixture();bool hip=corner=="hip";
            var first=Build(state,sim,Choice("slope",pitch),hip?-12:-18,23);
            var junction=Build(state,sim,Choice(corner,pitch),-15,23);
            var second=Build(state,sim,Choice("slope",pitch),-15,hip?26:20,MathF.PI/2);
            Check(junction.RoofConnectionId==first.Id&&second.RoofConnectionId==junction.Id,"committed L junction retains parent chain");
            Check(Near(first.Elevation!.Value,junction.Elevation!.Value)&&Near(second.Elevation!.Value,junction.Elevation.Value),"L junction carries exact edge elevation around the turn");
            Check(RoofGeometry.Connected(catalog,junction,first)&&RoofGeometry.Connected(catalog,second,junction),"committed L connections match physical edge profiles");
            Check(!Construction.CanRemove(state,first,out _)&&!Construction.CanRemove(state,junction,out _),"L junction supports cannot be removed ahead of their children");
            new Simulation(Clone(state),catalog);Check(true,"L junction validates after state reload");

            var mismatch=Clone(state);mismatch.Structures.RemoveAll(s=>s.Id==junction.Id||s.Id==second.Id);var mismatchSim=new Simulation(mismatch,catalog);Stand(mismatch,-15,23);
            string inventory=JsonSerializer.Serialize(mismatch.Player.Inventory);int count=mismatch.Structures.Count;
            var rejected=mismatchSim.Build("roof_module",-15,23,0,0,BuildSnapMode.Object,Choice(corner,pitch==45?30:45));
            Check(!rejected.Success&&rejected.Message.Contains("pitch",StringComparison.OrdinalIgnoreCase),"mismatched junction gives a pitch explanation");
            Check(count==mismatch.Structures.Count&&inventory==JsonSerializer.Serialize(mismatch.Player.Inventory),"mismatched junction changes neither materials nor structures");
        }

        // A triangular upper deck exposes an interior extremum that is absent from all three corners.
        // Hip: all sampled triangle vertices are low, but its diagonal midpoint is high.
        // Valley: all sampled triangle vertices are high, but its diagonal midpoint is low.
        foreach(float pitch in pitches)
        foreach(string corner in new[]{"hip","valley"})
        {
            var (state,sim)=Fixture();Stand(state,-15,23);var selection=Choice(corner,pitch);
            var baseline=sim.PreviewBuild("roof_module",-15,23,0,0,BuildSnapMode.Object,selection);Check(baseline.Valid,"unobstructed corner roof preview");
            float tan=MathF.Tan(pitch*MathF.PI/180);
            var slab=new StructureState{Id="crease-slab",Kind="upper_floor_triangle",X=-15,Z=23,Rotation=corner=="hip"?0:MathF.PI,Level=2,Elevation=baseline.Elevation+(corner=="hip"?tan:2*tan),Modular=true,SupportId=baseline.SupportId};
            state.Structures.Add(slab);
            var crossed=sim.PreviewBuild("roof_module",-15,23,0,0,BuildSnapMode.Object,selection);
            Check(!crossed.Valid&&crossed.Message.Contains("pitched roof profile"),$"{corner} crease {(corner=="hip"?"peak":"trough")} cannot cut a triangular upper floor at {pitch} degrees");
            string inventory=JsonSerializer.Serialize(state.Player.Inventory);
            Check(!sim.Build("roof_module",-15,23,0,0,BuildSnapMode.Object,selection).Success&&inventory==JsonSerializer.Serialize(state.Player.Inventory),"crease collision rejects committed placement atomically");
            slab.Elevation=baseline.Elevation+(corner=="hip"?3*tan+1:.1f);
            Check(sim.PreviewBuild("roof_module",-15,23,0,0,BuildSnapMode.Object,selection).Valid,"slab outside actual profile leaves roof placement available");
        }

        var (trimState,trimSim)=Fixture();
        var slopeParent=Build(trimState,trimSim,Choice("slope",45),-15,20);
        var ridgeParent=Build(trimState,trimSim,Choice("ridge",45),-15,23);
        var half=Build(trimState,trimSim,Choice("gable_fill",45),-15,20);
        var halfMirror=Build(trimState,trimSim,Choice("gable_fill",45,true),-15,20);
        var full=Build(trimState,trimSim,Choice("gable_ridge_fill",45),-15,23);
        var fullMirror=Build(trimState,trimSim,Choice("gable_ridge_fill",45,true),-15,23);
        Check(half.RoofConnectionId==slopeParent.Id&&halfMirror.RoofConnectionId==slopeParent.Id,"both partial gables attach to the slope");
        Check(full.RoofConnectionId==ridgeParent.Id&&fullMirror.RoofConnectionId==ridgeParent.Id,"both full gables attach to the ridge");
        foreach(var fill in new[]{full,fullMirror})
        {
            var support=trimState.Structures.Single(s=>s.Id==fill.SupportId);float wallTop=Construction.Base(trimState,support)+Construction.Storey;
            Check(Near(RoofGeometry.GableBottom(trimState,fill),wallTop-Construction.Base(trimState,fill))&&RoofGeometry.GableBottom(trimState,fill)<-2.9f,"raised ridge gable extends down to the supporting wall top");
            var gapPoint=Construction.World(fill,fill.Selection!.Mirrored?1.5f:-1.5f,0);
            Check(RoofGeometry.BlocksPoint(trimState,catalog,fill,gapPoint.X,gapPoint.Z,wallTop+.5f,.02f),"raised gable blocks the rectangular gap below the ridge base");
            Check(!RoofGeometry.BlocksPoint(trimState,catalog,fill,gapPoint.X,gapPoint.Z,wallTop-1.8f,.02f),"raised gable leaves the original storey below its wall-top bottom clear");
        }
        Stand(trimState,-15,23);
        var blockedFloor=trimSim.PreviewBuild("upper_floor",-15,23,0,1);
        Check(!blockedFloor.Valid&&blockedFloor.Message.Contains("gable panel"),"upper floor cannot be placed through an existing raised gable extension");
        var obstructed=Clone(trimState);obstructed.Structures.RemoveAll(s=>s.Id==full.Id||s.Id==fullMirror.Id);
        var obstructedSim=new Simulation(obstructed,catalog);var ridgeFloor=obstructed.Structures.Single(s=>s.Id==full.SupportId);
        var obstacle=new StructureState{Id="gable-slab",Kind="upper_floor",X=-15,Z=23,Level=1,Elevation=Construction.Base(obstructed,ridgeFloor)+Construction.Storey+.4f,SupportId=ridgeFloor.Id,Modular=true};obstructed.Structures.Add(obstacle);Stand(obstructed,-15,23);
        var blockedFill=obstructedSim.PreviewBuild("roof_module",-15,23,0,0,BuildSnapMode.Object,Choice("gable_ridge_fill",45));
        Check(!blockedFill.Valid&&blockedFill.Message.Contains("gable panel"),"raised gable cannot extend through an already placed upper floor");
        obstacle.Elevation=Construction.Base(obstructed,ridgeParent)+5;
        Check(obstructedSim.PreviewBuild("roof_module",-15,23,0,0,BuildSnapMode.Object,Choice("gable_ridge_fill",45)).Valid,"floor above the complete gable outline remains clear");
        foreach(var fill in new[]{half,halfMirror,full,fullMirror})
        {
            Stand(trimState,fill.X,fill.Z);string inventory=JsonSerializer.Serialize(trimState.Player.Inventory);int count=trimState.Structures.Count;
            Check(!trimSim.Build("roof_module",fill.X,fill.Z,fill.Rotation,0,BuildSnapMode.Object,fill.Selection).Success,"duplicate gable fill rejected");
            Check(count==trimState.Structures.Count&&inventory==JsonSerializer.Serialize(trimState.Player.Inventory),"duplicate gable fill has no side effects");
        }
        Stand(trimState,-15,20);Check(!trimSim.Build("roof_module",-15,20,0,0,BuildSnapMode.Object,Choice("gable_ridge_fill",45)).Success,"full gable cannot attach to a slope parent");
        Stand(trimState,-15,23);Check(!trimSim.Build("roof_module",-15,23,0,0,BuildSnapMode.Object,Choice("gable_fill",45)).Success,"partial gable cannot attach to a ridge parent");
        Check(!Construction.CanRemove(trimState,ridgeParent,out _),"ridge remains protected by its gable fills");
        foreach(var fill in new[]{full,fullMirror}){Stand(trimState,fill.X,fill.Z);Check(trimSim.Dismantle(fill.Id).Success,"gable fill can be dismantled before its roof");}
        Check(Construction.CanRemove(trimState,ridgeParent,out _),"removing both fills releases ridge parent");

        foreach(float pitch in pitches)
        foreach(string variant in new[]{"gable_fill","gable_ridge_fill"})
        foreach(bool mirror in new[]{false,true})
        foreach(float yaw in new[]{0f,MathF.PI/4,MathF.PI/2})
        {
            var fill=Piece(variant,pitch,-15,23,yaw,4,mirror);string geometry=BuildingVariants.Variant(catalog,fill.Selection!).Geometry;
            float edge=mirror?1.5f:-1.5f;
            foreach(float z in new[]{-1.1f,0,1.1f})
            {
                var point=Construction.World(fill,edge,z);float top=4+RoofGeometry.GableTop(geometry,pitch,z);
                Check(RoofGeometry.BlocksPoint(trimState,catalog,fill,point.X,point.Z,4.03f,.02f),"gable fill blocks inside its sloping outline");
                Check(!RoofGeometry.BlocksPoint(trimState,catalog,fill,point.X,point.Z,top+.15f,.02f),"empty space above gable outline is walkable");
                Check(!RoofGeometry.BlocksPoint(trimState,catalog,fill,point.X,point.Z,2,.02f),"player entirely below gable remains unobstructed");
                Check(Construction.SolidAt(trimState,fill,catalog.Buildings["roof_module"],point.X,point.Z,4.03f,.02f,catalog),"world collision uses gable geometry instead of ignoring every roof module");
            }
            var insideRoom=Construction.World(fill,0,0);Check(!RoofGeometry.BlocksPoint(trimState,catalog,fill,insideRoom.X,insideRoom.Z,4,.02f),"gable fill does not block the room interior");
            var beyond=Construction.World(fill,edge,1.85f);Check(!RoofGeometry.BlocksPoint(trimState,catalog,fill,beyond.X,beyond.Z,4,.02f),"gable collision stops beyond its physical end");
            var opposite=Construction.World(fill,-edge,0);Check(!RoofGeometry.BlocksPoint(trimState,catalog,fill,opposite.X,opposite.Z,4,.02f),"mirroring moves collision to the opposite side only");
        }

        string directory=Path.Combine(Path.GetTempPath(),"thornwake-junctions-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(directory);
        try
        {
            var (saved,sim)=Fixture();var roof=Build(saved,sim,Choice("slope",22.5f),-15,20);
            var left=Build(saved,sim,Choice("gable_fill",22.5f),-15,20);var right=Build(saved,sim,Choice("gable_fill",22.5f,true),-15,20);
            var hip=Build(saved,sim,Choice("hip",22.5f),-18,20);
            string path=Path.Combine(directory,"world.json");SaveStore.Save(path,saved);var loaded=SaveStore.Load(path);new Simulation(loaded,catalog);
            Check(JsonSerializer.Serialize(saved)==JsonSerializer.Serialize(loaded),"saved junction shapes, fractional pitches, mirroring and support graph round-trip exactly");
            Check(loaded.Structures.First(s=>s.Id==right.Id).Selection!.Mirrored&&!loaded.Structures.First(s=>s.Id==left.Id).Selection!.Mirrored,"independent gable mirror choices survive reload");
            Check(loaded.Structures.First(s=>s.Id==hip.Id).RoofConnectionId==roof.Id,"hip parent survives reload");
        }
        finally{foreach(string file in Directory.GetFiles(directory))File.Delete(file);Directory.Delete(directory);}
        return checks;
    }
}
