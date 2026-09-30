using Godot;
using System;
using System.Linq;
using Thornwake.Core;

namespace Thornwake;
public partial class Main
{
    private void VerifyEarthworks()
    {
        void Check(bool value,string message){if(!value)throw new Exception("Earthworks integration: "+message);}
        var previous=_state;
        PrepareEarthworkScene();
        bool always=_cameraRig.AlwaysRotate;
        _cameraRig.Configure(true,1,false);SyncCameraControl();Check(!_cameraRig.Captured,"shovel releases always-look for aiming");
        _cameraRig.Configure(always,1,false);
        Check(_slot==5&&_player.GetNodeOrNull<Node3D>("RightArm/HeldTool")!=null,"equipped shovel has a held model");
        _UnhandledInput(new InputEventKey{Keycode=Key.T,Pressed=true});Check(_earthworkMode==EarthworkMode.Fill,"T cycles modes");
        float zoom=_cameraRig.Orbit.TargetDistance;
        Check(HandleBuildWheel(new InputEventMouseButton{ButtonIndex=MouseButton.WheelUp,Pressed=true,CtrlPressed=true})&&_earthworkRadius==1.75f&&_cameraRig.Orbit.TargetDistance==zoom,"Ctrl wheel changes brush without zoom");
        float target=_earthworkTarget;
        HandleBuildWheel(new InputEventMouseButton{ButtonIndex=MouseButton.WheelUp,Pressed=true,AltPressed=true});
        Check(_earthworkMode==EarthworkMode.Level&&Mathf.Abs(_earthworkTarget-target-.1f)<.001f,"Alt wheel selects and changes level height");
        _UnhandledInput(new InputEventKey{Keycode=Key.H,Pressed=true});Check(_earthworkRadius==1.5f,"H resets brush");
        Command("earthwork:Dig");Check(_earthworkMode==EarthworkMode.Dig,"toolbar selects mode");
        var ids=_valley.TerrainRegions.ToDictionary(p=>p.Key,p=>p.Value.GetInstanceId());
        var result=_sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0);Check(result.Success,result.Message);
        UpdateEarthworkPreview();UpdateEarthworkPresentation();
        Check(_earthworkMarker?.Mesh.GetSurfaceCount()==1&&_hud.IsEarthworkVisible,"preview mesh and toolbar visible");
        TickEarthworkPresentation(.5f);UpdateEarthworkPresentation();
        Check(_state.Earthworks.Revision==0&&_player.GetNode<Node3D>("RightArm").Rotation.X<-.5f,"timed stroke animates before mutation");
        TickEarthworkPresentation(1.2f);
        Check(_state.Earthworks.Revision==1&&_sim.Count("soil")>0,"completed stroke commits terrain and soil");
        Check(_valley.LastTerrainRegionsRebuilt==4,"corner stroke updates four regions");
        Check(_valley.TerrainRegions.Count(p=>p.Value.GetInstanceId()!=ids[p.Key])==4,"unaffected render regions retained");
        foreach(var region in _valley.TerrainRegions.Values)
        {
            var vertices=region.Mesh.SurfaceGetArrays(0)[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            foreach(var v in vertices)Check(Mathf.Abs(v.Y-_valley.Height(v.X,v.Z))<.00001f,"actual vertices use shared changed heights");
        }
        float saved=_valley.Height(-32.2f,16.3f);SaveStore.Save(SavePath,_state);CreateWorld(SaveStore.Load(SavePath));
        Check(saved==_valley.Height(-32.2f,16.3f),"saved earthworks reconstruct rendered world");
        Check(_sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0).Success,"second stroke begins");
        _hud.ShowPause();float remaining=_state.EarthworkJob!.Remaining;
        // Presentation while paused must never advance the authoritative task.
        UpdateEarthworkPresentation();Check(_state.EarthworkJob.Remaining==remaining&&!_hud.IsEarthworkVisible,"pause hides tools and preserves task");
        _hud.HidePanel();_sim.CancelEarthwork();_cameraRig.Orbit.Reset();CreateWorld(previous);_earthworkMode=EarthworkMode.Dig;_hud.ShowGame();
        GD.Print("EARTHWORK_TEST_PASS: shovel model, cursor release, mode/size/height controls, toolbar, ring preview, timed animation, soil commit, four dirty regions, all rendered vertex heights, save reconstruction and pause");
    }
    private void PrepareEarthworkScene()
    {
        var state=new WorldState{Seed=7319,EnemyDefeated=true,CalendarMinutes=12*60};
        state.Player.X=-36.5f;state.Player.Z=16;state.Player.Inventory["shovel"]=1;state.Player.EquippedItem="shovel";state.Player.Hotbar[6]=new(){Id="shovel"};
        foreach(var r in WorldGenerator.Generate(state.Seed).Where(r=>Construction.Distance(r.X,r.Z,-32,16)<9)){state.RemovedResources.Add(r.Id);if(r.Kind=="tree")state.RemovedStumps.Add(r.Id);}
        CreateWorld(state);_hud.ShowGame();_earthworkMode=EarthworkMode.Dig;_earthworkRadius=1.5f;
        _cameraRig.Orbit.Reset();_cameraRig.Orbit.Zoom(-3);_cameraRig.Orbit.Look(100,15,1,false);
        for(int i=0;i<100;i++)_cameraRig.Orbit.Step(.016f);
        _cameraRig.SnapFocus(_player.Position);_cameraRig.Follow(_camera,_player.Position,0);_valley.Animate(state.Time,0);
    }
    private void ShowEarthworkPreview()
    {
        PrepareEarthworkScene();
        for(int i=0;i<12;i++){_state.Player.Stamina=100;var result=_sim.StartEarthwork(EarthworkMode.Dig,-32,16,1.5f,0);if(!result.Success)throw new Exception(result.Message);TickEarthworkPresentation(2);}
        _state.Player.Stamina=100;
        _earthworkTarget=_valley.Height(-32,16)+.15f;
        _earthworkMode=_view=="earthwork-level"?EarthworkMode.Level:EarthworkMode.Dig;
        var start=_sim.StartEarthwork(_earthworkMode,-32,16,1.5f,_earthworkTarget);if(!start.Success)throw new Exception(start.Message);
        UpdateGhost();UpdateEarthworkPresentation();UpdateContext();UpdateHud();
    }
}
