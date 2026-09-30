using Godot;
using System;
using System.Linq;
using Thornwake.Core;

namespace Thornwake;

public partial class Main
{
    private void VerifyCraftActions()
    {
        void Check(bool condition,string message){if(!condition)throw new Exception("Craft actions: "+message);}
        var state=new WorldState{Seed=7319};foreach(var id in _catalog.Items.Keys)state.Player.Inventory[id]=50;CreateWorld(state);_hud.ShowGame();
        var recipes=new[]{"planks","chair","iron","brick","stew","bread","stone_axe"};
        foreach(string id in recipes)
        {
            var recipe=_catalog.Recipes[id];var site=Workstations.Village.FirstOrDefault(s=>s.Kind==recipe.Station);
            _player.Position=_valley.At(site?.X??0,(site?.Z??8)+1.5f);_state.Player.X=_player.Position.X;_state.Player.Z=_player.Position.Z;_state.Player.Y=_player.Position.Y;
            Check(_sim.Craft(id,site?.Id??"hand").Success,id+" starts");
            _sim.TickCraft(.75f);UpdateCraftPresentation(.1f);
            Check(_craftAnimation!.CurrentAction==CraftActivity.Action(recipe),id+" dispatches its action");
            Check(_hud.IsActivityVisible,id+" shows world progress");
            var arm=_player.GetNode<Node3D>("RightArm");Check(!arm.GetNode<Node3D>("HeldTool").Visible,id+" stows equipped tool");
            Vector3 rotation=arm.Rotation;float remaining=_state.CraftJob!.Remaining;
            _cameraRig.SetFocused(false);UpdateCraftPresentation(.5f);
            Check(arm.Rotation==rotation&&_state.CraftJob.Remaining==remaining,id+" freezes when focus is lost");
            _cameraRig.SetFocused(true);_hud.ShowPause();UpdateCraftPresentation(.5f);
            Check(arm.Rotation==rotation&&!_hud.IsActivityVisible,id+" pauses behind another menu");_hud.HidePanel();
            if(site!=null)
            {
                Check(_valley.StationNode(site.Id)!.GetNodeOrNull<Node3D>("CraftWork")!=null,id+" has station workpiece");
                if(id=="planks")
                {
                    _valley.RebuildStructures();UpdateCraftPresentation(.1f);
                    _player.Position=_valley.At(6,5);_state.Player.Z=5;_state.Player.Y=_player.Position.Y;
                    _sim.TickCraft(1);UpdateCraftPresentation(.1f);
                    Check(_state.CraftJob.Remaining==remaining&&!_sim.IsCraftInReach,"out-of-reach work pauses");
                    Check(_hud.ActivityStatus.Contains("return"),"paused card explains recovery");
                    Vector3 old=_player.Position;MovePlayer(.1f,new Vector2(0,1));Check(_player.Position!=old,"paused work can walk back");_velocity=Vector3.Zero;
                    _player.Position=_valley.At(site.X,site.Z+1.5f);_state.Player.X=site.X;_state.Player.Z=site.Z+1.5f;_state.Player.Y=_player.Position.Y;UpdateCraftPresentation(.1f);
                }
            }
            Check(_sim.CancelCraft().Success,id+" cancels");UpdateCraftPresentation(.1f);
            Check(_craftAnimation.CurrentAction==""&&arm.GetNode<Node3D>("HeldTool").Visible,id+" restores equipment");
            Check(arm.GetNodeOrNull<Node3D>("CraftTool")==null,id+" removes temporary hand props");
            if(site!=null)Check(_valley.StationNode(site.Id)!.GetNodeOrNull<Node3D>("CraftWork")==null,id+" removes temporary workpiece");
        }
        GD.Print("CRAFT_ACTIONS_TEST_PASS: seven actions, station workpieces, world progress, focus/menu pauses, out-of-reach recovery, cancellation and equipment restoration");
    }
    private void VerifyWatchWorkInput()
    {
        try
        {
            if(_frames==45){_UnhandledInput(new InputEventKey{Keycode=Key.E,Pressed=true});if(!_hud.IsWorkshopOpen)throw new Exception("E did not open the active work queue");}
            if(_frames==46)
            {
                var button=_hud.FindChild("WatchWork",true,false) as Button??throw new Exception("Missing Watch work control");
                if(!button.IsVisibleInTree())throw new Exception("Watch work is not visible for an active job");
                var point=button.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,Position=point,GlobalPosition=point},true);
                GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point,GlobalPosition=point},true);
            }
            if(_frames==47)
            {
                if(_hud.IsPanelOpen||!_hud.IsActivityVisible||_state.CraftJob==null)throw new Exception("Watch work did not return to the world with the job intact");
                GD.Print("WATCH_WORK_INPUT_PASS: E opens active queue; native pointer Watch work returns to the world without cancelling");
            }
        }
        catch(Exception error){GD.PushError("WATCH_WORK_INPUT_FAIL "+error);QuitGame(2);}
    }
    private void ShowCraftActionPreview()
    {
        string id=_view switch{"work-smith"=>"iron","work-cook"=>"stew","work-hand"=>"stone_axe",_=>"planks"};
        var recipe=_catalog.Recipes[id];var site=Workstations.Village.FirstOrDefault(s=>s.Kind==recipe.Station);
        _player.Position=_valley.At(site?.X??0,(site?.Z??8)+1.5f);_state.Player.X=_player.Position.X;_state.Player.Z=_player.Position.Z;_state.Player.Y=_player.Position.Y;
        _state.Settings.CraftDurationMultiplier=5;
        ShowCrafting(site?.Id??"hand");Command("craft:"+id+":3:false");_sim.TickCraft(_state.CraftJob!.Duration*.3f);Command("watchwork");
        _cameraRig.Orbit.Reset();_cameraRig.Orbit.Zoom(-20);_cameraRig.Orbit.Look(200,-30,1,false);
        for(int i=0;i<100;i++)_cameraRig.Orbit.Step(.016f);
        _cameraRig.SnapFocus(_player.Position);_cameraRig.Follow(_camera,_player.Position,0);UpdateCraftPresentation(.1f);
    }
}
