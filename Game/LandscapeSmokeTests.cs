using Godot;
using System;
using System.Linq;
using Thornwake.Core;
using Thornwake.UI;

namespace Thornwake;
public partial class Main
{
    private void VerifyLandscapeSetup()
    {
        void Check(bool ok,string reason){if(!ok)throw new Exception("Landscape integration: "+reason);}
        var previous=_state;
        _hud.ShowTitle(false,7319);
        (_hud.FindChild("CustomizeNewWorld",true,false) as Button)!.EmitSignal(BaseButton.SignalName.Pressed);
        Check(_hud.IsWorldSetupOpen&&_hud.IsPanelOpen,"customize opens modal");
        (_hud.FindChild("LandscapePreset_Rugged",true,false) as Button)!.EmitSignal(BaseButton.SignalName.Pressed);
        Check((_hud.FindChild("Landscape_Relief",true,false) as HSlider)!.Value==1.5,"preset updates visible controls");
        Check(_hud.NewWorldGeneration.Relief==1,"draft leaves committed settings unchanged");
        _UnhandledInput(new InputEventKey{Keycode=Key.Escape,Pressed=true});Check(!_hud.IsWorldSetupOpen&&_state==previous,"cancel preserves existing world");
        _hud.ShowWorldSetup();(_hud.FindChild("LandscapePreset_Rugged",true,false) as Button)!.EmitSignal(BaseButton.SignalName.Pressed);
        (_hud.FindChild("LandscapeSeed",true,false) as LineEdit)!.Text="7319";
        (_hud.FindChild("LandscapeCreate",true,false) as Button)!.EmitSignal(BaseButton.SignalName.Pressed);
        Check(_state.GeneratorVersion==2&&_state.Generation.Relief==1.5f&&_state.Generation.ForestDensity==.95f,"create button starts real generator-2 world with selected settings");
        Check(!_hud.IsWorldSetupOpen&&!_hud.IsTitleVisible,"new world closes setup and title");
        Check(_valley.Spawns.Values.OrderBy(r=>r.Id).SequenceEqual(WorldGenerator.Generate(_state).OrderBy(r=>r.Id)),"rendered resources match generated IDs and settings");
        foreach(var pair in _valley.Resources)Check(Mathf.Abs(pair.Value.Position.Y-TerrainData.Height(_state,pair.Value.Position.X,pair.Value.Position.Z))<.0001f,"resources stand on new ground");
        foreach(var region in _valley.TerrainRegions.Values)
        {
            var vertices=region.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            for(int i=0;i<vertices.Length;i+=97){var p=vertices[i];Check(Mathf.Abs(p.Y-TerrainData.Height(_state,p.X,p.Z))<.00001f,"rendered region matches generator 2");}
        }
        foreach(var p in new[]{new Vector2(-45,0),new(0,45),new(-20,0),new(33,0)})
        {var pos=_valley.At(p.X,p.Y);Check(_valley.CanWalk(pos),"roads remain traversable");}
        var first=WorldGenerator.Generate(_state).First(r=>r.Kind=="stone");_state.Player.X=first.X;_state.Player.Z=first.Z;_state.Player.Y=TerrainData.Height(_state,first.X,first.Z);
        Check(_sim.Gather(first.Id,"stone").Success,"generated resource can be harvested");
        SaveStore.Save(SavePath,_state);CreateWorld(SaveStore.Load(SavePath));
        Check(_state.GeneratorVersion==2&&_state.Generation.Relief==1.5f&&!_valley.Resources.ContainsKey(first.Id),"generator settings and harvested resources reload correctly");
        _hud.ShowTitle(false,7319);_hud.ShowWorldSetup();(_hud.FindChild("LandscapePreset_Harthwood",true,false) as Button)!.EmitSignal(BaseButton.SignalName.Pressed);
        (_hud.FindChild("LandscapeCreate",true,false) as Button)!.EmitSignal(BaseButton.SignalName.Pressed);
        CreateWorld(previous);_hud.ShowGame();_cameraRig.Orbit.Reset();
        GD.Print("LANDSCAPE_TEST_PASS: customization modal, draft/preset/cancel, real create-world route, versioned settings, authoritative resource placement, sampled rendered heights, road traversal, gathering and save reconstruction");
    }
    private void ShowLandscapePreview()
    {
        var world=LandscapeGenerator.Create(7319,new(){Relief=1.5f,Roughness=.85f,ForestDensity=.7f},new());
        world.CalendarMinutes=12*60;world.EnemyDefeated=true;world.Player.X=-23;world.Player.Z=27;
        CreateWorld(world);_hud.ShowGame();
        _cameraRig.Orbit.Reset();_cameraRig.Orbit.Zoom(3);_cameraRig.Orbit.Look(160,-20,1,false);
        for(int i=0;i<100;i++)_cameraRig.Orbit.Step(.016f);
        _cameraRig.SnapFocus(_player.Position);_cameraRig.Follow(_camera,_player.Position,0);_valley.Animate(0,0);UpdateContext();UpdateHud();
        if(_view=="landscape-map")_map.Expanded=true;
        if(_view=="world-setup")
        {
            _hud.ShowTitle(false,7319);_hud.ShowWorldSetup();
            (_hud.FindChild("LandscapePreset_Rugged",true,false) as Button)!.EmitSignal(BaseButton.SignalName.Pressed);
        }
    }
}
