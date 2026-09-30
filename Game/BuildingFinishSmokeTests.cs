using Godot;
using System;
using System.Linq;
using System.Text.Json;
using Thornwake.Core;
using Thornwake.World;
namespace Thornwake;
public partial class Main
{
    private string _finishTestPiece="";
    private void VerifyBuildingFinishes()
    {
        void Check(bool value,string message){if(!value)throw new Exception("Building finishes: "+message);}
        var previous=_state;
        var state=new WorldState{Seed=7319,EnemyDefeated=true,CalendarMinutes=720};
        foreach(var resource in WorldGenerator.Generate(state.Seed))state.RemovedResources.Add(resource.Id);
        foreach(var item in _catalog.Items.Keys)state.Player.Inventory[item]=500;
        CreateWorld(state);_hud.HidePanel();SelectSlot(1);_buildHeightOffset=0;_refinishing=false;_dismantling=false;
        void Stand(float x,float z){state.Player.X=x;state.Player.Z=z;state.Player.Y=_valley.WalkHeight(x,z);_player.Position=new(x,state.Player.Y.Value,z);}
        StructureState Place(string kind,float x,float z,BuildSelection? choice=null,float rotation=0)
        {Stand(x+3,z);var result=_sim.Build(kind,x,z,rotation,0,BuildSnapMode.Object,choice);Check(result.Success,result.Message);return state.Structures[^1];}
        // Start from the highest corner so the full connected foundation is above ground.
        var sites=(from z in new[]{20f,23f} from x in new[]{-18f,-15f,-12f} select (X:x,Z:z)).OrderByDescending(p=>Construction.Polygon(new(){X=p.X,Z=p.Z}).Max(c=>Construction.Ground(state,c.X,c.Z))).ToList();
        while(sites.Count>0){var p=state.Structures.Count==0?sites[0]:sites.First(p=>state.Structures.Any(f=>Mathf.Abs(Construction.Distance(p.X,p.Z,f.X,f.Z)-3)<.01f));Place("foundation",p.X,p.Z);sites.Remove(p);}
        int index=0;
        foreach(float x in new[]{-18f,-15f,-12f})
        {
            var wall=BuildingVariants.Default(_catalog.BuildingTools["wall"]);wall.AppearanceId=new[]{"stone","limewash","planks"}[index];
            var back=Place("wall",x,18.5f,wall);var front=Place("wall",x,24.5f,wall);
            if(index==0)_finishTestPiece=front.Id;
            var roof=BuildingVariants.Default(_catalog.BuildingTools["roof"]);roof.AppearanceId=new[]{"charcoal","clay","thatch"}[index];
            Place("roof_module",x,20,roof);Place("roof_module",x,23,roof,Mathf.Pi);index++;
        }
        foreach(float x in new[]{-19.5f,-10.5f}) foreach(float z in new[]{20f,23f})
        {var wall=BuildingVariants.Default(_catalog.BuildingTools["wall"]);wall.AppearanceId="limewash";Place("wall",x,z,wall,Mathf.Pi/2);}
        foreach(float x in new[]{-18f,-12f})foreach(float z in new[]{20f,23f})
        {
            var fill=BuildingVariants.Default(_catalog.BuildingTools["wall"]);fill.VariantId="gable_fill";fill.Slope=45;fill.AppearanceId="limewash";
            fill.Mirrored=(x==-12)^(z==23);Place("roof_module",x,z,fill,z==23?Mathf.Pi:0);
        }
        _valley.RebuildStructures();
        Check(_valley.StructureNodes[_finishTestPiece].Name==_finishTestPiece,"model root retains structure identity");
        var imported=_valley.StructureNodes[_finishTestPiece];
        Check(imported.FindChild("Wall_Solid_Fieldstone",true,false)!=null,"external scene model resolves through appearance mapping");
        Check(!Descendants(imported).Any(n=>n is CollisionObject3D||n is Light3D||n is Camera3D),"imported art has no physical or lighting authority");
        var roofChoice=BuildingVariants.Default(_catalog.BuildingTools["roof"]);
        foreach(var appearance in _catalog.BuildingTools["roof"].Appearances)
        foreach(var shape in new[]{"slope","ridge","hip","valley"})
        foreach(float pitch in new[]{22.5f,30f,45f,60f})
        {roofChoice.AppearanceId=appearance.Id;roofChoice.VariantId=shape;roofChoice.Slope=pitch;var art=Visuals.BuildVariant(_catalog,roofChoice);Check(art.GetChildCount()>0,"all roof finishes render");art.Free();}
        Stand(-18,28);Command("buildtool:wall");Command("buildappearance");
        Check(_buildSelection!.AppearanceId=="limewash","finish cycling");
        var target=state.Structures.Single(s=>s.Id==_finishTestPiece);string metadata=JsonSerializer.Serialize(target.PaidCosts);
        Check(_sim.Refinish(target.Id,"limewash").Success,"refinish placed model");_valley.RebuildStructures();
        Check(JsonSerializer.Serialize(target.PaidCosts)==metadata&&_valley.StructureNodes[target.Id].FindChild("Wall_Solid_Fieldstone",true,false)==null,"replacement art removed while historical cost retained");
        Check(_sim.Refinish(target.Id,"stone").Success,"restore imported finish");_valley.RebuildStructures();
        SaveStore.Save(SavePath,state);CreateWorld(SaveStore.Load(SavePath));
        Check(_valley.StructureNodes[target.Id].FindChild("Wall_Solid_Fieldstone",true,false)!=null,"imported finish survives reload");
        GD.Print("BUILDING_FINISHES_TEST_PASS: procedural finishes at four pitches, model mapping, art-only scene import, repainting and save/reload");
        if(_view=="finishes")
        {
            _state.Player.X=-18;_state.Player.Z=28;_state.Player.Y=_valley.WalkHeight(-18,28);_player.Position=new(-18,_state.Player.Y.Value,28);
            _cameraRig.Orbit.Reset();_cameraRig.Orbit.Zoom(-1);_cameraRig.Orbit.Look(25,-8,1,false);
            for(int i=0;i<100;i++)_cameraRig.Orbit.Step(.016f);
            _cameraRig.SnapFocus(new(-15,_player.Position.Y+1,22));_cameraRig.Follow(_camera,new(-15,_player.Position.Y+1,22),0);
            SelectSlot(1);Command("buildtool:wall");_buildSelection!.AppearanceId="limewash";
            _cameraRig.Configure(false,1,false);SyncCameraControl();_hud.ShowGame();
        }
        else CreateWorld(previous);
    }
    private static System.Collections.Generic.IEnumerable<Node> Descendants(Node root)
    {foreach(var child in root.GetChildren()){yield return child;foreach(var descendant in Descendants(child))yield return descendant;}}
    private void VerifyFinishPointerInput()
    {
        try
        {
            void Check(bool value,string message){if(!value)throw new Exception(message);}
            void Click(string name)
            {
                var button=_hud.FindChild(name,true,false) as Button??throw new Exception("Missing finish button");var p=button.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=p,GlobalPosition=p},true);
                GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=p,GlobalPosition=p},true);
            }
            if(_frames==45){Click("BuildAppearance");Check(_buildSelection!.AppearanceId=="planks","finish button cycles");}
            if(_frames==46){Click("FinishBrush");Check(_refinishing,"brush button activates");}
            if(_frames==47)
            {
                var piece=_state.Structures.Single(s=>s.Id==_finishTestPiece);var point=_camera.UnprojectPosition(new(piece.X,Construction.Base(_state,piece)+1.25f,piece.Z+.12f));
                GetViewport().WarpMouse(point);
                GetViewport().PushInput(new InputEventMouseMotion{Position=point,GlobalPosition=point},true);
            }
            if(_frames==48)
            {
                int count=_state.Structures.Count;var before=JsonSerializer.Serialize(_state.Player.Inventory);
                UpdateGhost();Check(_ghost!=null&&_ghost.Visible,"targeted finish preview visible");
                _attackCooldown=0;UseTool();
                Check(_state.Structures.Single(s=>s.Id==_finishTestPiece).Selection!.AppearanceId=="planks","world brush changes targeted wall");
                Check(count==_state.Structures.Count&&before==JsonSerializer.Serialize(_state.Player.Inventory),"brush never builds or consumes ingredients");
            }
            if(_frames==49)
            {
                GetViewport().PushInput(new InputEventKey{Keycode=Key.Z,PhysicalKeycode=Key.Z,Pressed=true},true);
                GetViewport().PushInput(new InputEventKey{Keycode=Key.Z,PhysicalKeycode=Key.Z,Pressed=false},true);
                Check(!_refinishing,"Z exits finish brush");
                GD.Print("FINISH_INPUT_TEST_PASS: native finish/brush buttons, aimed preview, repaint without construction, keyboard return");
                Check(_sim.Refinish(_finishTestPiece,"stone").Success,"restore showcase finish");_valley.RebuildStructures();
                if(_ghost!=null)_ghost.Hide();
            }
        }
        catch(Exception error){GD.PushError("FINISH_INPUT_TEST_FAIL "+error);QuitGame(2);}
    }
}
