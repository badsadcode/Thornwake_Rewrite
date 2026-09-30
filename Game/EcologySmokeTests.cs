using Godot;
using System;
using System.Linq;
using Thornwake.Core;

namespace Thornwake;

public partial class Main
{
    private void VerifyEcology()
    {
        void Check(bool ok,string message){if(!ok)throw new Exception("Ecology: "+message);}
        var previous=_state;
        var state=new WorldState{Seed=7319,EnemyDefeated=true};
        foreach(var r in WorldGenerator.Generate(state.Seed)){state.RemovedResources.Add(r.Id);if(r.Kind=="tree")state.RemovedStumps.Add(r.Id);}
        state.Player.Inventory["pine_sapling"]=3;state.Player.Inventory["bush_seedling"]=3;
        state.Player.X=-17;state.Player.Z=23;CreateWorld(state);_hud.ShowGame();
        Check(_sim.PlantVegetation("pine_sapling",-15,23).Success,"plant tree on open ground");
        var tree=state.Vegetation[^1];_valley.RefreshVegetation();
        Check(_valley.Resources.ContainsKey(tree.Id),"sapling visible and targetable");float initial=_valley.Resources[tree.Id].Scale.X;
        _sim.Tick(4*3600);_valley.Animate(_state.Time,0);
        Check(Math.Abs(tree.Growth-.5)<.001&&_valley.Resources[tree.Id].Scale.X>initial,"continuous growth changes mesh size");
        _sim.Tick(4*3600);SelectSlot(0);
        for(int i=0;i<3;i++){_attackCooldown=0;_state.Player.Stamina=100;Gather(tree.Id);}
        Check(tree.IsStump&&_valley.Stumps.ContainsKey(tree.Id)&&!_valley.Resources.ContainsKey(tree.Id),"chopped planted tree becomes a visible stump");
        _attackCooldown=0;Gather(tree.Id);Check(!_valley.Stumps.ContainsKey(tree.Id)&&!state.Vegetation.Contains(tree),"normal axe interaction removes stump");
        Check(_sim.PlantVegetation("bush_seedling",-15,23).Success,"plant perennial bush");
        var bush=state.Vegetation[^1];_valley.RefreshVegetation();_sim.Tick(2*3600);_valley.Animate(_state.Time,0);
        _attackCooldown=0;_state.Player.Stamina=100;Gather(bush.Id);
        Check(bush.Fruit==0&&_valley.Resources.ContainsKey(bush.Id),"harvesting leaves a living bare bush");
        _sim.Tick(3600);_valley.Animate(_state.Time,0);Check(bush.Fruit==1,"berries regrow after configured active hour");
        Command("hotbar:assign:9:item:pine_sapling");ActivateHotbar(9);UpdateGhost();
        Check(IsPlantingVegetation&&_ghost!=null&&_ghost.Visible&&!_cameraRig.Captured,"sapling shortcut provides free-cursor planting preview");
        var natural=WorldGenerator.Generate(state.Seed).First(r=>r.Kind=="tree");state.RemovedStumps.Remove(natural.Id);
        CreateWorld(state);SelectSlot(0);_player.Position=_valley.At(natural.X+1,natural.Z);state.Player.X=natural.X+1;state.Player.Z=natural.Z;
        Check(_valley.Stumps.ContainsKey(natural.Id),"old felled tree has stump");_attackCooldown=0;Gather(natural.Id);
        Check(state.RemovedStumps.Contains(natural.Id)&&!_valley.Stumps.ContainsKey(natural.Id),"existing natural stump can be cleared");
        SaveStore.Save(SavePath,state);CreateWorld(SaveStore.Load(SavePath));
        Check(!_valley.Stumps.ContainsKey(natural.Id)&&_valley.Resources.ContainsKey(bush.Id),"removed stump and living bush survive scene reload");
        CreateWorld(previous);_hud.HidePanel();
        GD.Print("ECOLOGY_TEST_PASS: smooth sapling rendering, mature tree harvest, planted/natural stump removal, persistent bare bush and fruit regrowth, planting hotbar/ghost, save reconstruction");
    }
    private void ShowEcologyShowcase()
    {
        var state=new WorldState{Seed=7319,EnemyDefeated=true};
        foreach(var r in WorldGenerator.Generate(state.Seed).Where(r=>Construction.Distance(r.X,r.Z,-15,24)<14)){state.RemovedResources.Add(r.Id);if(r.Kind=="tree")state.RemovedStumps.Add(r.Id);}
        state.Vegetation.AddRange(new[]{new VegetationState{Id="demo-young",X=-20,Z=22,Growth=.05},new VegetationState{Id="demo-half",X=-16,Z=22,Growth=.45},new VegetationState{Id="demo-tree",X=-12,Z=22,Growth=1},new VegetationState{Id="demo-bush",Kind="berries",X=-17,Z=27,Growth=1},new VegetationState{Id="demo-bare",Kind="berries",X=-13,Z=27,Growth=1,Fruit=.2}});
        state.Player.X=-18;state.Player.Z=29;state.Player.Inventory["pine_sapling"]=2;state.Player.Inventory["bush_seedling"]=2;
        CreateWorld(state);_hud.ShowGame();SelectSlot(0);_cameraRig.SnapFocus(_player.Position);_cameraRig.Orbit.Reset();_cameraRig.Follow(_camera,_player.Position,0);
    }
    private void VerifySandboxPointerInput()
    {
        try
        {
            void Check(bool ok,string message){if(!ok)throw new Exception(message);}
            void Click(string name)
            {
                var control=_hud.FindChildren(name,"",true,false).OfType<Control>().First(c=>c.IsVisibleInTree());var at=control.GetGlobalRect().GetCenter();
                GetViewport().PushInput(new InputEventMouseButton{Position=at,GlobalPosition=at,ButtonIndex=MouseButton.Left,Pressed=true},true);
                GetViewport().PushInput(new InputEventMouseButton{Position=at,GlobalPosition=at,ButtonIndex=MouseButton.Left,Pressed=false},true);
            }
            if(_frames==45)Click("SandboxPreset_Builder");
            if(_frames==46){Check(_state.Settings.TreeGrowthHours==8,"preset must only change draft");Click("SandboxApply");}
            if(_frames==47){Check(Math.Abs(_state.Settings.TreeGrowthHours-.1f)<.001&&_state.Settings.CraftDurationMultiplier==.25f&&!_hud.IsSandboxOpen,"Apply commits selected rules");Command("sandbox:open");}
            if(_frames==48){var field=_hud.FindChild("Sandbox_TreeGrowthHours",true,false) as SpinBox??throw new Exception("Missing growth control");field.Value=480;}
            if(_frames==49){_UnhandledInput(new InputEventKey{Keycode=Key.Escape,Pressed=true});Check(Math.Abs(_state.Settings.TreeGrowthHours-.1f)<.001&&!_hud.IsSandboxOpen,"Escape discards unapplied edit");}
            if(_frames==50)
            {
                _sim.UpdateSandbox(new SandboxSettings());SaveStore.Save(SavePath,_state);CreateWorld(SaveStore.Load(SavePath));Command("sandbox:open");
                Check(_state.Settings.TreeGrowthHours==8&&_hud.IsSandboxOpen,"saved slow defaults reopen correctly");
                GD.Print("SANDBOX_POINTER_TEST_PASS: native preset/apply clicks, draft isolation, growth edit cancel, saved rules and reopened menu");
            }
        }
        catch(Exception ex){GD.PushError("SANDBOX_POINTER_TEST_FAIL "+ex);QuitGame(2);}
    }
}
