using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.Core;

namespace Thornwake;

public partial class Main
{
    private void VerifyBuildingVariants()
    {
        void Check(bool condition,string message){if(!condition)throw new Exception("Building variants: "+message);}
        var previous=_state;
        Check(Thornwake.World.Visuals.ValidateBuildingAssets(_catalog).Length==0,"all shipped visual assets resolve");
        var state=new WorldState{Seed=7319,EnemyDefeated=true,Time=75};
        foreach(var resource in WorldGenerator.Generate(state.Seed))state.RemovedResources.Add(resource.Id);
        foreach(var item in _catalog.Items.Keys)state.Player.Inventory[item]=500;
        CreateWorld(state);_hud.HidePanel();SelectSlot(1);_snapMode=BuildSnapMode.Object;_rotation=0;
        void Stand(float x,float z)
        {_state.Player.X=x;_state.Player.Z=z;_state.Player.Y=_valley.WalkHeight(x,z);_player.Position=new(x,_state.Player.Y.Value,z);}
        StructureState Place(string kind,float x,float z,float angle=0,BuildSelection? selected=null)
        {
            Stand(x-4,z);var result=_sim.Build(kind,x,z,angle,0,BuildSnapMode.Object,selected);
            Check(result.Success,kind+" at "+x+","+z+": "+result.Message);return _state.Structures[^1];
        }
        var floorSites=(from z in new[]{20f,23f,26f} from x in new[]{-18f,-15f,-12f} select (X:x,Z:z))
            .OrderByDescending(p=>Construction.Polygon(new StructureState{X=p.X,Z=p.Z}).Max(c=>Construction.Ground(_state,c.X,c.Z))).ToList();
        while(floorSites.Count>0)
        {
            var site=_state.Structures.Count==0?floorSites[0]:floorSites.First(p=>_state.Structures.Any(f=>Mathf.Abs(Construction.Distance(p.X,p.Z,f.X,f.Z)-3)<.01f));
            Place("foundation",site.X,site.Z);floorSites.Remove(site);
        }
        foreach(float x in new[]{-18f,-15f,-12f}){Place("wall",x,18.5f);Place(x==-15?"door":"wall",x,27.5f);}
        foreach(float z in new[]{20f,23f,26f}){Place("wall",-19.5f,z,Mathf.Pi/2);Place("wall",-10.5f,z,Mathf.Pi/2);}
        _valley.RebuildStructures();Stand(-22,20);Command("buildtool:roof");
        Check(_buildSelection?.VariantId=="slope"&&_buildSelection.Slope==45,"roof defaults to slope at 45 degrees");
        _heightAnchor=new Vector3(-18,0,20);UpdateGhost();
        var preview=_sim.PreviewBuild(BuildKind,-18,20,0,0,_snapMode,_buildSelection);
        Check(preview.Valid,preview.Message);Check(_ghost!=null&&Mathf.Abs(_ghost.Position.Y-preview.Elevation)<.001f,"ghost matches variant elevation");
        ulong ghostId=_ghost!.GetInstanceId();Command("buildslope");
        Check(_buildSelection!.Slope==60&&_ghost.GetInstanceId()!=ghostId,"pitch changes rebuild the ghost without ending placement");
        Command("buildslope");Check(_buildSelection.Slope==22.5f,"pitch cycles to shallow porch angle");
        Command("buildslope");Check(_buildSelection.Slope==30,"pitch cycles to 30 degrees");
        Command("buildslope");Check(_buildSelection.Slope==45,"pitch cycles back to 45 degrees");
        Command("buildmaterial");string material=_buildSelection.MaterialId;
        Check(material!=_catalog.BuildingTools["roof"].Materials[0].Id,"material cycles");
        var selected=_buildSelection.Copy();var costs=BuildingVariants.Resolve(_catalog,selected).Costs;
        int stone=_sim.Count("stone");_attackCooldown=0;UseTool();var first=_state.Structures[^1];
        Check(first.Selection?.MaterialId==material&&first.Selection.Slope==45,"placement persists selected material and pitch");
        Check(_sim.Count("stone")==stone-costs.GetValueOrDefault("stone"),"placement charges selected material costs");
        foreach(float x in new[]{-15f,-12f})Place("roof_module",x,20,0,selected);
        var ridge=selected.Copy();ridge.VariantId="ridge";
        foreach(float x in new[]{-18f,-15f,-12f})Place("roof_module",x,23,0,ridge);
        foreach(float x in new[]{-18f,-15f,-12f})Place("roof_module",x,26,Mathf.Pi,selected);
        var middle=_state.Structures.First(s=>s.Selection?.VariantId=="ridge");
        Check(Mathf.Abs(Construction.Base(_state,middle)-Construction.Base(_state,first)-3)<.01f,"ridge snaps at the slope's upper edge height");
        Stand(-22,20);Check(!_sim.Dismantle(first.Id).Success,"roof connections protect their supporting piece");
        var eave=selected.Copy();eave.VariantId="eave";var eavePiece=Place("roof_module",-18,20,0,eave);
        var gable=selected.Copy();gable.VariantId="gable_edge";Place("roof_module",-18,20,0,gable);
        Check(RaycastStructure(new Vector3(-18,Construction.Base(_state,first)+10,18.32f),Vector3.Down)?.Id==eavePiece.Id,"dismantling can select trim separately from its parent roof");
        var alternate=selected.Copy();alternate.Slope=30;
        Place("foundation",-18,29);
        Stand(-18,30);var wrong=_sim.PreviewBuild("roof_module",-18,29,0,0,BuildSnapMode.Object,alternate);
        Check(!wrong.Valid,"incompatible roof pitch cannot silently connect");
        // Tool settings survive selecting another family, and every family uses the same shape control.
        Command("buildshape");string remembered=_buildSelection.VariantId;
        Command("buildtool:floor");Command("buildshape");Check(_buildSelection!.VariantId=="triangle","floor uses generic shape cycling");
        Command("buildtool:wall");Command("buildshape");Check(_buildSelection!.VariantId!="solid","wall uses generic shape cycling");
        Command("buildtool:roof");Check(_buildSelection!.VariantId==remembered&&_buildSelection.Slope==45&&_buildSelection.MaterialId==material,"returning to roof remembers its properties");
        while(_buildSelection.VariantId!="gable_edge")Command("buildshape");
        Command("buildmirror");Check(_buildSelection.Mirrored,"G-compatible mirror changes gable side");
        _valley.RebuildStructures();SaveStore.Save(SavePath,_state);CreateWorld(SaveStore.Load(SavePath));
        Check(_state.Structures.Single(s=>s.Id==first.Id).Selection?.MaterialId==material,"save/reload retains variant configuration");
        Check(_state.Structures.Single(s=>s.Id==middle.Id).RoofConnectionId!="","save/reload retains roof connection");
        Check(_valley.StructureNodes[first.Id].GetChildCount()>0,"configured roof rebuilds its mesh");
        if(_view is "variants" or "roof")
        {
            Stand(-15,33);_cameraRig.Orbit.Reset();_cameraRig.Orbit.Zoom(-1);_cameraRig.Orbit.Look(35,-15,1,false);
            for(int i=0;i<100;i++)_cameraRig.Orbit.Step(.016f);
            if(_view=="variants")
            {
                Command("buildtool:roof");while(_buildSelection!.VariantId!="slope")Command("buildshape");
                _heightAnchor=new Vector3(-12,0,29);UpdateGhost();
            }
            else SelectSlot(0);
        }
        else {CreateWorld(previous);_buildSelection=null;SelectSlot(0);}
        _cameraRig.SnapFocus(_player.Position);_cameraRig.Follow(_camera,_player.Position,0);
        GD.Print("BUILD_VARIANTS_TEST_PASS: generic tools, live pitch/material ghost, 45-degree gable roof, semantic ridge height, eave/gable trim, saved configuration/connections, remembered settings and floor/wall cycling");
    }
    private void VerifyBuildOptionInput()
    {
        try
        {
            void Check(bool value,string message){if(!value)throw new Exception(message);}
            int pieces=_state.Structures.Count;
            void Click(string name)
            {
                var button=_hud.FindChild(name,true,false) as Button??throw new Exception("Missing live build control: "+name);
                var point=button.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point,GlobalPosition=point},true);
                GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point,GlobalPosition=point},true);
            }
            void KeyPress(Key key,bool shift=false)
            {
                GetViewport().PushInput(new InputEventKey{Keycode=key,PhysicalKeycode=key,Pressed=true,ShiftPressed=shift},true);
                GetViewport().PushInput(new InputEventKey{Keycode=key,PhysicalKeycode=key,Pressed=false,ShiftPressed=shift},true);
            }
            if(_frames==45)
            {
                Click("BuildSlope");Check(_buildSelection?.Slope==60,"Pitch button must change the live preview");
            }
            else if(_frames==46)
            {
                KeyPress(Key.T);Check(_buildSelection?.VariantId=="ridge","T must work after clicking a live control");
                KeyPress(Key.T,true);Check(_buildSelection?.VariantId=="slope","Shift T must reverse shape cycling");
            }
            else
            {
                Click("BuildMaterial");Check(_buildSelection?.MaterialId=="timber","Material button must select timber");
                Check(_ghost!=null&&_ghost.Visible,"Live ghost must remain visible");
                GD.Print("BUILD_OPTIONS_INPUT_TEST_PASS: viewport pitch/material clicks, keyboard shape cycling after click, reverse cycling and no accidental placement");
            }
            Check(_slot==1&&!_hud.IsPanelOpen&&_state.Structures.Count==pieces,"Option controls must keep placement active without building a piece");
        }
        catch(Exception error){GD.PushError("BUILD_OPTIONS_INPUT_TEST_FAIL "+error);QuitGame(2);}
    }
}
