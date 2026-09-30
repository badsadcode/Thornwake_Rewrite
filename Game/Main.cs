using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Thornwake.Core;
using Thornwake.World;
using Thornwake.UI;
using Thornwake.Camera;
using System.Globalization;

namespace Thornwake;

/// <summary>Local session adapter. The persistent simulation is independent of rendering and input.</summary>
public partial class Main : Node3D
{
    private WorldState _state = null!;
    private Simulation _sim = null!;
    private ContentCatalog _catalog = null!;
    private Valley _valley = null!;
    private Node3D _player = null!, _ghost = null!;
    private Camera3D _camera = null!;
    private OrbitCamera _cameraRig = null!;
    private GameHud _hud = null!;
    private Soundscape _sound = null!;
    private ValleyMap _map = null!;
    private int _slot;
    private float _attackCooldown, _dodge, _enemyHealth=80, _enemyWindup, _enemyCooldown;
    private float _rotation, _walkPhase, _stepTime, _autosave, _uiTimer;
    private string _buildKind="foundation", _nearest="", _capture="", _view="";
    private bool _started, _test, _testComplete, _quitting;
    private int _frames;
    private int _queueBeforePointerTest;
    private readonly Dictionary<string,int> _strikes = new();
    private bool _dismantling;
    private string _ghostKind="",_placementHint="";
    private int _buildHeightOffset;
    private int BuildLevel=>Math.Clamp(CurrentLevel()+_buildHeightOffset,0,2);
    private Vector3? _heightAnchor;
    private Vector3? _lastPlacementAim;
    private string BuildKind=>_buildSelection!=null?BuildingVariants.Resolve(_catalog,_buildSelection,BuildLevel).Id:_buildKind is "foundation" or "upper_floor"?(BuildLevel==0?"foundation":"upper_floor"):_buildKind is "foundation_triangle" or "upper_floor_triangle"?(BuildLevel==0?"foundation_triangle":"upper_floor_triangle"):_buildKind;
    private BuildSelection? _buildSelection;
    private readonly Dictionary<string,BuildSelection> _toolSelections=new();
    private readonly Dictionary<string,float> _toolSlopes=new();
    private readonly Dictionary<MeshInstance3D,(StandardMaterial3D[] Valid,StandardMaterial3D[] Invalid)> _variantGhostMaterials=new();
    private BuildSnapMode _snapMode=BuildSnapMode.Object;
    private string _craftUiStamp="";
    private string _stationId="hand";
    private readonly StandardMaterial3D _validGhost=new(){AlbedoColor=new Color(.58f,.8f,.55f,.5f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
    private readonly StandardMaterial3D _invalidGhost=new(){AlbedoColor=new Color(.85f,.3f,.23f,.5f),Transparency=BaseMaterial3D.TransparencyEnum.Alpha,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded};
    private Vector3 _velocity;
    private float _fallSpeed;
    private CraftAnimation? _craftAnimation;
    private string SavePath => ProjectSettings.GlobalizePath(_test||_capture!=""?"user://smoke-world.json":"user://world.json");
    private string SettingsPath => _test||_capture!=""?"user://smoke-settings.cfg":"user://settings.cfg";

    public override void _Ready()
    {
        try
        {
            _catalog = ContentCatalog.LoadFromJson(file => Godot.FileAccess.GetFileAsString("res://Content/"+file));
            foreach(string diagnostic in Visuals.ValidateBuildingAssets(_catalog))GD.PushWarning(diagnostic);
            _camera=new Camera3D {Current=true,Fov=48,Near=.1f,Far=240};AddChild(_camera);
            _cameraRig=new OrbitCamera();AddChild(_cameraRig);
            _sound=new Soundscape();AddChild(_sound);
            _hud=new GameHud();AddChild(_hud);_hud.StartRequested+=StartNew;_hud.Command+=Command;
            var mapLayer=new CanvasLayer{Layer=9};AddChild(mapLayer);var mapRoot=new Control{MouseFilter=Control.MouseFilterEnum.Ignore};mapRoot.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);mapLayer.AddChild(mapRoot);_map=new ValleyMap();mapRoot.AddChild(_map);_map.Visible=false;
            GetTree().AutoAcceptQuit=false;
            foreach(string arg in OS.GetCmdlineUserArgs()) {if(arg=="--smoke-test")_test=true;if(arg.StartsWith("--capture="))_capture=arg[10..];if(arg.StartsWith("--view="))_view=arg[7..];}
            LoadSettings();
            CreateWorld(new WorldState{Seed=7319});
            _hud.ShowTitle(File.Exists(SavePath));
            if(_test)StartWorld(7319,true);else if(_capture!=""&&_view!="title")StartNew(7319);
            if(_view=="craft")ShowCrafting();if(_view=="inventory")ShowInventory();if(_view=="build")ShowBuilding();
            if(_view=="options")_hud.ShowPause();
            GD.Print("THORNWAKE_READY");
        }
        catch(Exception ex) {GD.PushError(ex.ToString());QuitGame(1);}
    }

    private void CreateWorld(WorldState state)
    {
        var simulation=new Simulation(state,_catalog);
        _craftAnimation?.Clear();
        ResetEarthworkPresentation();
        if(_valley!=null) {RemoveChild(_valley);_valley.QueueFree();}
        if(_player!=null) {RemoveChild(_player);_player.QueueFree();}
        if(_ghost!=null) {RemoveChild(_ghost);_ghost.QueueFree();_ghost=null!;}
        _state=state;_sim=simulation;_strikes.Clear();
        _valley=new Valley();AddChild(_valley);_valley.Generate(state,_catalog);
        _player=Visuals.Person(new Color("526b61"));AddChild(_player);_player.Position=new Vector3(state.Player.X,_valley.WalkHeight(state.Player.X,state.Player.Z,state.Player.Y??_valley.WalkHeight(state.Player.X,state.Player.Z)),state.Player.Z);state.Player.Y=_player.Position.Y;ApplyEquipment();_craftAnimation=new CraftAnimation(_player);
        _enemyHealth=80;_enemyCooldown=0;_enemyWindup=0;_attackCooldown=0;_dodge=0;_autosave=0;_fallSpeed=0;
        _cameraRig.SnapFocus(_player.Position);_cameraRig.Follow(_camera,_player.Position,0);
        _map.Seed=state.Seed;_map.SetWorld(state);
    }

    private void StartNew(int seed)=>StartWorld(seed,false);
    private void StartWorld(int seed,bool legacyTest)
    {
        // Keep the previous world recoverable when the player deliberately starts another seed.
        if(!_test&&_capture==""&&File.Exists(SavePath)) File.Copy(SavePath,SavePath+".previous",true);
        _toolSelections.Clear();_toolSlopes.Clear();_refinishing=false;_buildSelection=null;_buildKind="foundation";
        CreateWorld(legacyTest?new WorldState{Seed=seed}:LandscapeGenerator.Create(seed,_hud.NewWorldGeneration,new()));_started=true;_hud.ShowGame();_map.Visible=true;
        _hud.Notify("Welcome to Harthwood. E gathers nearby resources. C opens your recipes.");
    }

    public override void _Process(double delta)
    {
        if(_state==null)return;
        float dt=(float)Math.Min(delta,.05);_frames++;
        SyncCameraControl();
        _hud.SetBuildOptions(_catalog,_started&&_slot==1&&!_dismantling&&!_hud.IsPanelOpen?_buildSelection:null,_rotation,BuildLevel,_refinishing);
        _cameraRig.StepInput(dt,_slot==1);
        DispatchPointer();
        if(_started&&_cameraRig.IsFocused&&(!_hud.IsPanelOpen||_hud.IsWorkshopOpen))
        {
            var finished=_sim.TickCraft(dt);if(finished.HasValue)Report(finished.Value);
            string stamp=(_state.CraftJob?.RecipeId??"")+":"+(_state.CraftJob?.QueueEntryId??"")+string.Join("|",_state.CraftQueue.Select(q=>q.Id+":"+q.RemainingCount));
            if(finished.HasValue||stamp!=_craftUiStamp){_craftUiStamp=stamp;if(_hud.IsWorkshopOpen)_hud.ShowCrafting(_state,_catalog,_hud.ActiveCraftStation);}
            var job=_state.CraftJob;_hud.UpdateCraftProgress(job==null?"":_catalog.Recipes[job.RecipeId].Name,job?.Remaining??0,job?.Duration??1);
            _hud.SetCraftQueueStatus(_sim.CraftQueueStatus);

        }
        if(_started&&!_hud.IsPanelOpen&&_cameraRig.CanControl)
        {
            _sim.Tick(dt);if(_state.CraftJob==null||!_sim.IsCraftInReach)MovePlayer(dt);TickEarthworkPresentation(dt);TickEnemy(dt);_valley.Animate(_state.Time,dt);
            _attackCooldown=Mathf.Max(0,_attackCooldown-dt);_dodge=Mathf.Max(0,_dodge-dt);
            UpdateGhost();UpdateContext();
            _valley.UpdateShelterVisibility(_player.Position);
            _autosave+=dt;if(_autosave>45&&!_test){_autosave=0;Save(false);}
        }
        UpdateCraftPresentation(dt);
        UpdateEarthworkPresentation();
        UpdateCamera(dt);
        _valley.UpdateForest(_camera.GlobalPosition,dt);WoodlandAssets.UpdateCanopyFocus(_camera,_player.Position);
        _uiTimer+=dt;if(_uiTimer>.15){_uiTimer=0;UpdateHud();}
        if(_test && _frames==30) RunSmokeTest();
        if(_testComplete&&_view.StartsWith("work-")&&_frames>=45&&_frames<=47)VerifyWatchWorkInput();
        if(_testComplete&&_view=="queue"&&_frames is 45 or 46 or 47) VerifyRecipePointerInput();
        if(_testComplete&&_view=="variants"&&_frames is 45 or 46 or 47) VerifyBuildOptionInput();
        if(_testComplete&&_view=="hotbar"&&_frames>=45&&_frames<=58)VerifyHotbarPointerInput();
        if(_testComplete&&_view=="finishes"&&_frames>=45&&_frames<=49)VerifyFinishPointerInput();
        if(_testComplete&&_view=="sandbox"&&_frames>=45&&_frames<=50)VerifySandboxPointerInput();
        if(_testComplete&&_view=="finishes"&&_frames>49&&_ghost!=null)_ghost.Hide();
        if((_testComplete||_capture!="")&&_frames==75) CaptureAndFinish();
    }

    private void MovePlayer(float dt,Vector2? testInput=null)
    {
        Vector2 input=new((Input.IsPhysicalKeyPressed(Key.D)?1:0)-(Input.IsPhysicalKeyPressed(Key.A)?1:0),(Input.IsPhysicalKeyPressed(Key.S)?1:0)-(Input.IsPhysicalKeyPressed(Key.W)?1:0));
        if(testInput.HasValue)input=testInput.Value;
        if(input.Length()>1)input=input.Normalized();
        if(input.LengthSquared()>.001f)_heightAnchor=null;
        Vector3 direction=new Vector3(input.X,0,input.Y).Rotated(Vector3.Up,_cameraRig.Orbit.Yaw);
        bool sprint=Input.IsPhysicalKeyPressed(Key.Shift)&&_state.Player.Stamina>8&&direction.LengthSquared()>.1;
        float speed=_dodge>0?13:sprint?7:4.2f;
        if(sprint)_state.Player.Stamina=Mathf.Max(0,_state.Player.Stamina-dt*18);
        _velocity=_velocity.MoveToward(direction*speed,dt*35);
        Vector3 pos=_player.Position;
        var nx=pos+new Vector3(_velocity.X*dt,0,0);if(_valley.CanWalk(nx))pos.X=nx.X;
        var nz=pos+new Vector3(0,0,_velocity.Z*dt);if(_valley.CanWalk(nz))pos.Z=nz.Z;
        float surface=_valley.WalkHeight(pos.X,pos.Z,pos.Y);
        if(pos.Y-surface>.45f){_fallSpeed+=dt*18;pos.Y=Mathf.Max(surface,pos.Y-_fallSpeed*dt);}else{pos.Y=surface;_fallSpeed=0;}
        if(pos.X>23&&pos.X<31&&Mathf.Abs(pos.Z)<1.5f)pos.Y=.4f;
        _player.Position=pos;_state.Player.X=pos.X;_state.Player.Z=pos.Z;_state.Player.Y=pos.Y;
        if(direction.LengthSquared()>.01) _player.Rotation=new Vector3(0,Mathf.LerpAngle(_player.Rotation.Y,Mathf.Atan2(direction.X,direction.Z),dt*15),0);
        _walkPhase+=dt*(_velocity.Length()*2.1f);Valley.AnimatePerson(_player,_walkPhase,_velocity.Length()>.2f);
        if(_velocity.Length()>1) {_stepTime+=dt;if(_stepTime>.38f){_stepTime=0;_sound.Play("step");}}
        var arm=_player.GetNodeOrNull<Node3D>("RightArm");if(arm!=null)arm.Rotation=new Vector3(_attackCooldown>0?-Mathf.Sin(_attackCooldown/.6f*Mathf.Pi)*1.8f:0,0,0);
    }

    private void UpdateCraftPresentation(float dt)
    {
        bool running=_started&&_cameraRig.IsFocused&&(!_hud.IsPanelOpen||_hud.IsWorkshopOpen);
        var job=_state.CraftJob;
        if(job==null)_craftAnimation?.Clear();
        else _craftAnimation?.Update(_catalog.Recipes[job.RecipeId],job,_valley.StationNode(job.StationId),_sim.IsCraftInReach,running,dt);
        _hud.UpdateCraftActivity(_state,_catalog,_sim.IsCraftInReach,running);
        if(job!=null&&_ghost!=null)_ghost.Hide();
    }

    private void UpdateCamera(float dt)
    {
        Vector3 target=_started?_player.Position:new Vector3(0,0,0);
        _cameraRig.Follow(_camera,target,dt);
        _hud.SetCameraState(_cameraRig.Captured,_cameraRig.AlwaysRotate);
    }

    private void SyncCameraControl() => _cameraRig.SetControl(_started&&!_hud.IsPanelOpen&&!_hud.IsTitleVisible,_slot>=1&&_slot<=3||_slot==5);
    public override void _Input(InputEvent e)
    {
        if(_cameraRig==null||_hud==null)return;
        SyncCameraControl();
        if(e is InputEventMouseMotion motion)
        {
            if(motion.ScreenRelative.LengthSquared()>.01f)_heightAnchor=null;
            _cameraRig.Motion(motion);
            if(_cameraRig.Captured)GetViewport().SetInputAsHandled();
        }
        // Releases must reach the gesture even when the pointer ends over a HUD card.
        if(e is InputEventMouseButton button&&(_cameraRig.Captured||(!button.Pressed&&(_cameraRig.Pointer.Left||_cameraRig.Pointer.Right||_cameraRig.Pointer.Middle))))
        {
            if(HandleBuildWheel(button)||_cameraRig.HandleButton(button)){DispatchPointer();GetViewport().SetInputAsHandled();}
        }
    }
    private bool HandleBuildWheel(InputEventMouseButton button)
    {
        if(HandleEarthworkWheel(button))return true;
        if(!_cameraRig.CanControl||_slot!=1||!button.AltPressed||button.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown))return false;
        if(!button.Pressed)return true;
        _heightAnchor??=MouseWorld();
        int desired=BuildLevel+(button.ButtonIndex==MouseButton.WheelUp?1:-1);
        _buildHeightOffset=Math.Clamp(desired,0,2)-CurrentLevel();
        UpdateGhost();UpdateContext();return true;
    }
    private void ResetBuildHeight(){_buildHeightOffset=0;_heightAnchor=null;_lastPlacementAim=null;}
    private void DispatchPointer()
    {
        bool primary=_cameraRig.Pointer.TakePrimary(),secondary=_cameraRig.Pointer.TakeSecondary();
        if(!_cameraRig.CanControl)return;
        if(primary)UseTool();
        if(secondary&&_state.EarthworkJob!=null)Report(_sim.CancelEarthwork());
        else if(secondary&&_slot!=4)SelectSlot(0);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if(e is InputEventKey k&&k.Pressed&&!k.Echo)
        {
            if(k.Keycode==Key.F11) {DisplayServer.WindowSetMode(DisplayServer.WindowGetMode()==DisplayServer.WindowMode.Fullscreen?DisplayServer.WindowMode.Windowed:DisplayServer.WindowMode.Fullscreen);return;}
            if(k.Keycode==Key.Escape&&_hud.IsWorldSetupOpen){_hud.HideWorldSetup();return;}
            if(!_started)return;
            if(k.Keycode==Key.Escape){if(_hud.IsSandboxOpen)_hud.ShowPause();else if(_hud.IsPanelOpen)_hud.HidePanel();else _hud.ShowPause();SyncCameraControl();return;}
            if(HandlePlayerMenuKey(k.Keycode))return;
            if(k.Keycode==Key.B&&_slot==1&&_hud.IsBuildingOpen||k.Keycode==Key.I&&_hud.IsInventoryOpen||k.Keycode==Key.C&&_hud.IsWorkshopOpen){_hud.HidePanel();SyncCameraControl();return;}
            if(_hud.IsPanelOpen)return;
            if(k.Keycode>=Key.Key1&&k.Keycode<=Key.Key9){ActivateHotbar((int)(k.Keycode-Key.Key1));return;}
            if(k.Keycode==Key.Key0){ActivateHotbar(9);return;}
            switch(k.Keycode)
            {
                case Key.E: Interact();break;
                case Key.X: if(_nearest!=""&&_state.CraftJob==null){Report(_sim.UprootPlant(_nearest));_valley.RefreshVegetation();UpdateContext();}break;
                case Key.I: ShowInventory();break;
                case Key.C: ShowCrafting();break;
                case Key.P: ShowPlayerScreen("character");break;
                case Key.K: ShowPlayerScreen("skills");break;
                case Key.J: ShowPlayerScreen("journal");break;
                case Key.B: if(_slot==1)ShowBuilding();else _hud.Notify("Equip your hammer before opening build mode [B].");break;
                case Key.H: if(_slot==5){_earthworkRadius=1.5f;_earthworkTarget=_player.Position.Y;UpdateEarthworkPreview();break;}if(_slot==1){ResetBuildHeight();UpdateGhost();UpdateContext();}break;
                case Key.M: _map.Expanded=!_map.Expanded;break;
                case Key.R: if(_slot==1)RotateBuild(k.ShiftPressed?-1:1);break;
                case Key.V: if(_slot==1){_snapMode=(BuildSnapMode)(((int)_snapMode+1)%3);_heightAnchor=null;UpdateGhost();UpdateContext();SaveSettings();}break;
                case Key.T: if(_slot==5){SetEarthworkMode((EarthworkMode)(((int)_earthworkMode+1)%4));break;}if(_slot==1)CycleBuildOption("shape",k.ShiftPressed?-1:1);break;
                case Key.Y: if(_slot==1)CycleBuildOption("slope",k.ShiftPressed?-1:1);break;
                case Key.N: if(_slot==1)CycleBuildOption("material",k.ShiftPressed?-1:1);break;
                case Key.U: if(_slot==1)CycleBuildOption("appearance",k.ShiftPressed?-1:1);break;
                case Key.Z: ToggleFinishBrush();break;
                case Key.G: if(_slot==5){_earthworkTarget=MouseWorld().Y;UpdateEarthworkPreview();break;}if(_slot==1)CycleBuildOption("mirror");break;
                case Key.Home: _cameraRig.Orbit.Reset();break;
                case Key.F5: Save();break;
                case Key.F9: _hud.Notify(_valley.CycleTerrainDebug());UpdateContext();break;
                case Key.F: Report(_sim.Eat());break;
                case Key.Space: UseAbility("dodge");break;
            }
            SyncCameraControl();
        }
        if(e is InputEventMouseButton m&&_started&&!_hud.IsPanelOpen)
        {
            SyncCameraControl();
            if(HandleBuildWheel(m)||_cameraRig.HandleButton(m)){DispatchPointer();GetViewport().SetInputAsHandled();}
        }
    }

    private void SelectSlot(int slot)
    {
        string id=slot switch{0=>"basic_axe",1=>"hammer",2=>"hoe",3=>"seeds",4=>"basic_sword",_=>""};
        if(!_sim.EquipItem(id).Success)return;
        ApplyEquipment();
    }
    private void ApplyEquipment()
    {
        int slot=HotbarRules.ToolMode(_catalog,_state.Player.EquippedItem);
        if(slot==1&&_slot!=1)ResetBuildHeight();
        if(slot==5&&_slot!=5){_earthworkTarget=_state.Player.Y??0;_earthworkAim=null;}
        if(slot!=5&&_state.EarthworkJob!=null)_sim.CancelEarthwork();
        _slot=slot;if(slot!=1){_refinishing=false;_dismantling=false;_hud.SetBuildHeight("");}_hud.UpdateHotbar(_state,_catalog);if(_ghost!=null){RemoveChild(_ghost);_ghost.QueueFree();_ghost=null!;}_ghostKind="";
        if(_player!=null){var arm=_player.GetNodeOrNull<Node3D>("RightArm");if(arm!=null){var old=arm.GetNodeOrNull<Node3D>("HeldTool");if(old!=null){arm.RemoveChild(old);old.QueueFree();}if(slot>=0)arm.AddChild(Visuals.Tool(slot));}}
        SyncCameraControl();
    }
    private void UpdateContext()
    {
        _nearest="";float best=3.2f;
        foreach(var pair in _valley.Resources) {float d=FlatDistance(_player.Position,pair.Value.Position);if(d<best){best=d;_nearest=pair.Key;}}
        foreach(var pair in _valley.Stumps){if(!pair.Value.Visible)continue;float d=FlatDistance(_player.Position,pair.Value.Position);if(d<best){best=d;_nearest=pair.Key;}}
        string text=_nearest!=""?$"E   {(_valley.Spawns[_nearest].Kind=="tree"?"Chop "+_valley.TreeName(_nearest):_valley.Spawns[_nearest].Kind=="stone"?"Gather stone":"Gather berries")}":"Explore the valley  •  E interact  •  Scroll to zoom";
        if(_valley.Stumps.ContainsKey(_nearest))text=_slot==0?"E   Remove tree stump":"Equip an axe to remove this stump";
        var growing=_state.Vegetation.FirstOrDefault(p=>p.Id==_nearest&&!p.IsStump);
        if(growing!=null)text="E   "+Vegetation.Status(_state,growing)+(growing.Kind=="berries"||growing.Growth<1?" · X remove with axe / hoe":"");
        var site=NearbyStation();if(site!=null)text="E   Use "+site.Kind+" station";
        if(Near(-3,0,2.5f))text="E   Harthwood market & village news";
        if(Near(-4,6,2.5f))text="E   Rest by the fire";
        foreach(var c in _state.Crops)if(Near(c.X,c.Z,2)){text=_state.Time-c.PlantedAt>=_sim.CropDuration?"E   Harvest your crop":"Your crop is growing";break;}
        var door=NearbyDoor();if(door!=null)text=door.DoorOpen?"E   Close your door":"E   Open your door";
        if(_slot==1)text=_placementHint;
        if(_slot==2)text="Left click: cultivate soil  ·  Equip a shovel to dig or fill";
        if(_slot==5)text=_state.EarthworkJob!=null?"Working the ground  ·  Move or right click to cancel":"Left click: work  ·  T: mode  ·  Ctrl + wheel: size  ·  G: sample height  ·  Alt + wheel: height";
        if(_slot==3)text=IsPlantingVegetation?_plantingHint:$"Left click prepared soil to sow  •  Crops mature in {_state.Settings.CropGrowthMinutes:0.#} active minutes";
        if(_slot==4)text="Left click: strike  •  Hold right mouse: block  •  Space + movement: dodge";
        if(!_state.EnemyDefeated&&FlatDistance(_player.Position,_valley.Enemy.Position)<9)text=_enemyWindup>0?"The roadwarden raises his blade. Block or dodge!":"Road bandit nearby  •  Equip a blade  •  Right mouse to block";
        if(_state.CraftJob!=null)text=_sim.IsCraftInReach?"E   Open work queue  ·  Orbit the camera to watch":"Return to your station to resume crafting  ·  E view queue";
        if(_valley.TerrainDebug!=TerrainDebugView.None)
        {
            string legend=_valley.TerrainDebug switch {TerrainDebugView.Delta=>"blue: cut · gold: fill",TerrainDebugView.Regions=>"alternating 16 m regions",TerrainDebugView.Slope=>"green: gentle · red: steep",TerrainDebugView.Material=>"brown: soil · pale: sand · grey: rock",_=>"tan: dry · blue: wet"};
            text=$"Terrain: {_valley.TerrainDebug} · {legend} · F9 next";
        }
        _hud.SetContext(text);
    }

    private void Interact()
    {
        if(_state.EarthworkJob!=null){_hud.Notify("Finish or cancel the shovel stroke first.");return;}
        if(_state.CraftJob!=null){ShowCrafting(_state.CraftJob.StationId);return;}
        var door=NearbyDoor();if(door!=null){var result=_sim.ToggleDoor(door.Id);Report(result);if(result.Success){_valley.RebuildStructures();_sound.Play("wood");}return;}
        foreach(var c in _state.Crops.ToArray()) if(Near(c.X,c.Z,2)) {Report(_sim.Harvest(c.Id));_valley.RefreshCrops();return;}
        var site=NearbyStation();if(site!=null){ShowCrafting(site.Id);return;}
        if(Near(-3,0,2.5f)){ShowMarket();return;}
        if(Near(-4,6,2.5f)){_state.Player.Health=100;_state.Player.Stamina=100;_hud.Notify("The hearth is warm. You feel rested.");_sound.Play("craft");return;}
        foreach(var s in _state.Structures) if(Near(s.X,s.Z,2.5f)) {if(s.Kind=="campfire"){_state.Player.Health=100;_hud.Notify("Rested at your own hearth.");return;}if(s.Kind=="workbench"){ShowCrafting();return;}}
        if(_nearest!="")Gather(_nearest);else _hud.Notify("Move closer to a tree, stone, berry bush, crop, well or workbench.");
    }

    private void Gather(string id)
    {
        if(_attackCooldown>0)return;
        if(_valley.Stumps.ContainsKey(id)){ClearStump(id);return;}
        if(!_valley.Resources.TryGetValue(id,out var node))return;
        var growing=_state.Vegetation.FirstOrDefault(p=>p.Id==id);
        if(growing!=null&&(growing.Growth<1||growing.Kind=="berries"&&growing.Fruit<1)){_hud.Notify(Vegetation.Status(_state,growing));return;}
        if(_valley.Spawns[id].Kind=="tree"&&_slot!=0){_hud.Notify("Equip an axe to chop a tree.");return;}
        if(_state.Player.Stamina<7){_hud.Notify("Catch your breath.");return;}
        var spawn=_valley.Spawns[id];_state.Player.Stamina-=7;_attackCooldown=.55f;Face(node.Position);
        _sound.Play(spawn.Kind=="tree"?"wood":"stone");Burst(node.Position+Vector3.Up,spawn.Kind=="tree"?new Color("bfaa71"):new Color("9c9c84"));
        int hits=_strikes.GetValueOrDefault(id)+1;_strikes[id]=hits;
        int needed=_state.Player.EquippedItem=="axe"?2:3;
        if(spawn.Kind=="tree"&&hits<needed){_hud.Notify($"{_valley.TreeName(id)}  •  {hits}/{needed} strokes");var shake=CreateTween();shake.TweenProperty(node,"rotation:z",.035f,.08);shake.TweenProperty(node,"rotation:z",0f,.16);return;}
        var result=_sim.Gather(id,spawn.Kind);Report(result);
        if(result.Success)
        {
            _strikes.Remove(id);
            if(_state.Vegetation.Any(p=>p.Id==id))_valley.RefreshVegetation();
            else{_valley.Resources.Remove(id);if(spawn.Kind=="tree"){_valley.AddStump(spawn);var t=node.CreateTween();t.TweenProperty(node,"rotation:z",1.5f,.65);t.TweenProperty(node,"scale",Vector3.Zero,.2);t.TweenCallback(Callable.From(()=>{if(IsInstanceValid(node))node.QueueFree();}));}else node.QueueFree();}
        }
    }

    private void UseTool()
    {
        if(_state.CraftJob!=null){_hud.Notify("Finish the current task or cancel it in the crafting menu.");return;}
        if(_attackCooldown>0)return;
        if(_slot==5){StartShoveling();return;}
        if(_state.EarthworkJob!=null)return;
        if(_slot==4){Attack();return;}
        Vector3 p=MouseWorld();
        if(_slot==0){if(_nearest!="")Gather(_nearest);else {_attackCooldown=.55f;_sound.Play("wood");}return;}
        if(_slot==1)
        {
            if(_refinishing){ApplyFinishBrush();return;}
            if(_dismantling)
            {
                var piece=PointedStructure();if(piece==null){_hud.Notify("Point at one of your building pieces.");return;}
                var removed=_sim.Dismantle(piece.Id);Report(removed);if(removed.Success)_valley.RebuildStructures();_attackCooldown=.3f;return;
            }
            var result=_sim.Build(BuildKind,p.X,p.Z,_rotation,BuildLevel,_snapMode,_buildSelection);Report(result);if(result.Success)_valley.RebuildStructures();_attackCooldown=.35f;return;
        }
        if(FlatDistance(_player.Position,p)>5.5f){_hud.Notify("Move closer. Work within five paces.");return;}
        if(_slot==2)
        {
            if(!_valley.CanPlace(p,.4f)){_hud.Notify("Find open ground away from the village and river.");return;}
            var result=_sim.Cultivate(p.X,p.Z);Report(result);if(result.Success)_valley.RefreshTerrain(_state.TerrainEdits[^1]);
        }
        if(_slot==3){var result=IsPlantingVegetation?_sim.PlantVegetation(_state.Player.EquippedItem,p.X,p.Z):_sim.Plant(p.X,p.Z);Report(result);if(result.Success){_valley.RefreshCrops();_valley.RefreshVegetation();}}
        _attackCooldown=.45f;
    }

    private Vector3 MouseWorld()
    {
        if(_slot==1&&_heightAnchor.HasValue)return _heightAnchor.Value;
        Vector2 mouse=_cameraRig.Captured?GetViewport().GetVisibleRect().Size*new Vector2(.5f,.43f):GetViewport().GetMousePosition();Vector3 origin=_camera.ProjectRayOrigin(mouse),direction=_camera.ProjectRayNormal(mouse);
        float raised=_slot==1?BuildLevel*Construction.Storey:0;
        bool roofAim=_slot==1&&!_dismantling&&_catalog.Buildings[BuildKind].Placement=="roof";
        for(float t=1;t<160;t+=.2f)
        {
            var p=origin+direction*t;float height=_valley.WalkHeight(p.X,p.Z)+raised+(roofAim?Construction.WallHeight:0);
            if(roofAim)foreach(var roof in _state.Structures.Where(s=>Construction.IsRoof(s)&&s.Level==BuildLevel&&Construction.OnFloor(s,p.X,p.Z)))height=Mathf.Max(height,_valley.RoofHeight(roof,p.X,p.Z));
            if(p.Y<=height)return new Vector3(p.X,height,p.Z);
        }
        return _player.Position+new Vector3(0,0,-3);
    }
    private void UpdateGhost()
    {
        int relative=BuildLevel-CurrentLevel();string height=relative==0?"At your floor":$"{Math.Abs(relative)} {(Math.Abs(relative)==1?"storey":"storeys")} {(relative>0?"above":"below")} you";
        _hud.SetBuildHeight(_slot==1?height+$"  ·  {_snapMode} snap [V]  ·  Alt + wheel: height  ·  H: reset":"");
        if(_earthworkMarker!=null)_earthworkMarker.Visible=_slot==5;
        if(_slot==5){if(_ghost!=null)_ghost.Hide();UpdateEarthworkPreview();return;}
        if(IsPlantingVegetation){UpdatePlantGhost();return;}
        if(_slot!=1){if(_ghost!=null)_ghost.Visible=false;return;}
        if(_refinishing){UpdateFinishGhost();return;}
        if(_dismantling)
        {
            var piece=PointedStructure();
            if(piece==null){if(_ghost!=null)_ghost.Visible=false;_placementHint="Dismantle: point at your structure  •  Right click cancels";return;}
            EnsureGhost(piece.Kind,piece.Selection,RoofGeometry.GableBottom(_state,piece));_ghost.Visible=true;_ghost.Position=new Vector3(piece.X,Construction.Base(_state,piece),piece.Z);_ghost.Rotation=new Vector3(0,piece.Rotation,0);TintGhost(_ghost,_invalidGhost);
            _placementHint="Reclaim "+_catalog.Buildings[piece.Kind].Name+"  •  Left click  •  Returns "+string.Join(", ",_sim.ReclaimYield(piece).Select(c=>$"{c.Value} {_catalog.Items[c.Key].Name}"));return;
        }
        Vector3 raw=_hud.IsBuildOptionsHovered&&_lastPlacementAim.HasValue?_lastPlacementAim.Value:MouseWorld();_lastPlacementAim=raw;
        var plan=_sim.PreviewBuild(BuildKind,raw.X,raw.Z,_rotation,BuildLevel,_snapMode,_buildSelection);
        var previewPiece=new StructureState{Kind=BuildKind,Selection=plan.Selection??_buildSelection,Elevation=plan.Elevation,SupportId=plan.SupportId,Level=plan.Level};
        EnsureGhost(BuildKind,previewPiece.Selection,RoofGeometry.GableBottom(_state,previewPiece));_ghost.Visible=true;_ghost.Position=new Vector3(plan.X,plan.Elevation,plan.Z);_ghost.Rotation=new Vector3(0,plan.Rotation,0);
        TintGhost(_ghost,plan.Valid?_validGhost:_invalidGhost);_placementHint=plan.Message;
        if(plan.Valid)_placementHint+="  •  "+plan.SnapLabel;
        if(_buildSelection!=null)_placementHint+="  •  "+Costs(BuildingVariants.Resolve(_catalog,_buildSelection,BuildLevel).Costs);
        else if(_buildKind is "foundation" or "upper_floor" or "foundation_triangle" or "upper_floor_triangle")_placementHint+="  •  "+Costs(_catalog.Buildings[BuildKind].Costs);
    }
    private void EnsureGhost(string kind,BuildSelection? selection=null,float gableBottom=0,bool open=false)
    {
        string key=kind+(selection==null?"":$":{selection.DefinitionId}:{selection.VariantId}:{selection.Slope}:{selection.MaterialId}:{selection.AppearanceId}:{selection.Mirrored}:{gableBottom}")+":"+open;
        if(_ghost!=null&&_ghostKind==key)return;
        if(_ghost!=null){RemoveChild(_ghost);_ghost.QueueFree();}
        foreach(var materials in _variantGhostMaterials.Values){foreach(var material in materials.Valid)material.Dispose();foreach(var material in materials.Invalid)material.Dispose();}_variantGhostMaterials.Clear();
        _ghost=_valley.StructureVisual(kind,open,selection,gableBottom);AddChild(_ghost);_ghostKind=key;
        if(selection!=null)PrepareVariantGhost(_ghost);
    }
    private bool CanBuildAt(Vector3 p)=>_sim.PreviewBuild(_buildKind,p.X,p.Z,_rotation).Valid;
    private void PrepareVariantGhost(Node root)
    {
        foreach(var child in root.GetChildren())
        {
            if(child is MeshInstance3D mesh && mesh.Mesh!=null)
            {
                StandardMaterial3D Preview(int surface,Color tint)
                {
                    var original=(mesh.MaterialOverride??mesh.GetActiveMaterial(surface)) as StandardMaterial3D;
                    var preview=original?.Duplicate() as StandardMaterial3D??new StandardMaterial3D();
                    var color=original?.AlbedoColor??Colors.White;
                    preview.AlbedoColor=new Color(color.Lerp(tint,.35f),.62f);
                    preview.Transparency=BaseMaterial3D.TransparencyEnum.Alpha;
                    preview.ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded;
                    preview.CullMode=BaseMaterial3D.CullModeEnum.Disabled;
                    return preview;
                }
                var valid=Enumerable.Range(0,mesh.Mesh.GetSurfaceCount()).Select(i=>Preview(i,new Color("91d487"))).ToArray();
                var invalid=Enumerable.Range(0,mesh.Mesh.GetSurfaceCount()).Select(i=>Preview(i,new Color("ed6257"))).ToArray();
                _variantGhostMaterials[mesh]=(valid,invalid);
                mesh.MaterialOverride=null;
            }
            PrepareVariantGhost(child);
        }
    }
    private void TintGhost(Node root,StandardMaterial3D material)
    {
        foreach(var child in root.GetChildren())
        {
            if(child is MeshInstance3D mesh)
            {
                if(_variantGhostMaterials.TryGetValue(mesh,out var pair))
                {var surfaces=material==_validGhost?pair.Valid:pair.Invalid;for(int i=0;i<surfaces.Length;i++)mesh.SetSurfaceOverrideMaterial(i,surfaces[i]);}
                else mesh.MaterialOverride=material;
            }
            if(child is Light3D light)light.Visible=false;
            TintGhost(child,material);
        }
    }
    private StationSite? NearbyStation()=>Workstations.Village.Concat(_state.Structures.Where(s=>Workstations.IsStation(s.Kind)).Select(s=>new StationSite(s.Id,Workstations.Kind(s.Kind),s.X,s.Z))).Where(s=>Workstations.InReach(_state,s)).OrderBy(s=>Construction.Distance(s.X,s.Z,_state.Player.X,_state.Player.Z)).FirstOrDefault();
    private StructureState? NearbyDoor()=>_state.Structures.Where(s=>s.Kind=="door"&&Near(s.X,s.Z,2.4f)&&Mathf.Abs(Construction.Base(_state,s)-_player.Position.Y)<1.6f).OrderBy(s=>Construction.Distance(s.X,s.Z,_state.Player.X,_state.Player.Z)).FirstOrDefault();
    private StructureState? PointedStructure()
    {
        Vector2 mouse=_cameraRig.Captured?GetViewport().GetVisibleRect().Size*new Vector2(.5f,.43f):GetViewport().GetMousePosition();Vector3 origin=_camera.ProjectRayOrigin(mouse),direction=_camera.ProjectRayNormal(mouse);
        return RaycastStructure(origin,direction);
    }
    private StructureState? RaycastStructure(Vector3 origin,Vector3 direction)
    {
        var pieces=_state.Structures.Where(s=>s.Level==BuildLevel&&Construction.Distance(s.X,s.Z,_state.Player.X,_state.Player.Z)<=Construction.Reach)
            .OrderByDescending(s=>s.Selection!=null&&RoofGeometry.IsTrim(_catalog,s.Selection)).ToArray();
        for(float t=1;t<100;t+=.15f)
        {
            Vector3 p=origin+direction*t;
            foreach(var s in pieces)
            {
                if(RoofGeometry.IsRoofModule(s))
                {
                    var local=Construction.Local(s,p.X,p.Z);string shape=BuildingVariants.Variant(_catalog,s.Selection!).Geometry;
                    if(Mathf.Abs(local.X)>1.85f||Mathf.Abs(local.Z)>1.85f)continue;
                    if(shape=="roof_eave"&&Mathf.Abs(local.Z+1.59f)>.28f)continue;
                    if(shape=="roof_gable_edge"&&Mathf.Abs(local.X-(s.Selection!.Mirrored?1.59f:-1.59f))>.28f)continue;
                    if(RoofGeometry.IsFill(shape))
                    {
                        float edge=s.Selection!.Mirrored?1.5f:-1.5f,baseY=Construction.Base(_state,s);
                        if(Mathf.Abs(local.X-edge)<.12f&&Mathf.Abs(local.Z)<=1.55f&&p.Y>=baseY+RoofGeometry.GableBottom(_state,s)&&p.Y<=baseY+RoofGeometry.GableTop(shape,s.Selection.Slope,local.Z))return s;
                        continue;
                    }
                    float surface=Construction.Base(_state,s)+RoofGeometry.SurfaceHeight(shape=="roof_eave"?"roof_slope":shape,s.Selection!.Slope,local.X,local.Z);
                    if(Mathf.Abs(p.Y-surface)<(RoofGeometry.IsTrim(shape)?.3f:.16f))return s;
                    continue;
                }
                float bottom=Construction.Base(_state,s),top=bottom+Construction.PieceHeight(s,_catalog);
                if(p.Y>=bottom-.05f&&p.Y<=top&&Construction.Occupies(s,_catalog.Buildings[s.Kind],p.X,p.Z,.1f))return s;
            }
            if(p.Y<_valley.Height(p.X,p.Z)-.15f)break;
        }
        return null;
    }

    private void Attack()
    {
        if(_state.Player.Stamina<15){_hud.Notify("Catch your breath before attacking.");return;}
        _state.Player.Stamina-=15;_attackCooldown=.6f;_sound.Play("hit");Face(MouseWorld());
        if(!_state.EnemyDefeated&&FlatDistance(_player.Position,_valley.Enemy.Position)<2.9f)
        {
            Face(_valley.Enemy.Position);_enemyHealth-=_state.Player.EquippedItem=="sword"?35:22;Burst(_valley.Enemy.Position+Vector3.Up,new Color("c79c6c"));_hud.Notify($"Road bandit  •  {Mathf.Max(0,_enemyHealth):0} / 80");
            Vector3 push=(_valley.Enemy.Position-_player.Position).Normalized();var p=_valley.Enemy.Position+push*.7f;if(_valley.CanWalk(p))_valley.Enemy.Position=p;
            if(_enemyHealth<=0){Report(_sim.DefeatEnemy());_valley.Enemy.Visible=false;}
        }
    }
    private void TickEnemy(float dt)
    {
        if(_state.EnemyDefeated)return;
        var enemy=_valley.Enemy;float distance=FlatDistance(_player.Position,enemy.Position);
        _enemyCooldown=Mathf.Max(0,_enemyCooldown-dt);
        if(distance<10&&distance>1.9f&&_enemyWindup<=0)
        {
            Vector3 direction=(_player.Position-enemy.Position);direction.Y=0;direction=direction.Normalized();Vector3 next=enemy.Position+direction*dt*2.2f;if(_valley.CanWalk(next))enemy.Position=_valley.At(next.X,next.Z);enemy.Rotation=new Vector3(0,Mathf.Atan2(direction.X,direction.Z),0);Valley.AnimatePerson(enemy,(float)_state.Time*7,true);
        }
        else if(distance>=10){var d=_valley.EnemyHome-enemy.Position;if(d.Length()>1)enemy.Position+=d.Normalized()*dt;}
        if(distance<2.5f&&_enemyCooldown<=0&&_enemyWindup<=0){_enemyWindup=.75f;_enemyCooldown=2.1f;}
        var arm=enemy.GetNodeOrNull<Node3D>("RightArm");if(arm!=null)arm.Rotation=new Vector3(_enemyWindup>0?-2.1f:0,0,0);
        if(_enemyWindup>0)
        {
            _enemyWindup-=dt;
            if(_enemyWindup<=0&&distance<3&&_dodge<=0)
            {
                bool block=_slot==4&&_cameraRig.Pointer.Blocking&&_state.Player.Stamina>=10;
                _state.Player.Health-=(block?3:19)*_state.Settings.DamageTakenMultiplier;if(block)_state.Player.Stamina-=10;_sound.Play("hurt");_hud.Notify(block?"Blocked. Hold your ground.":"A heavy blow. Space to dodge, or hold right mouse to block.");
                if(_state.Player.Health<=0){_state.Player.Health=70;_state.Player.X=0;_state.Player.Z=8;_player.Position=_valley.At(0,8);int lost=Math.Min(_sim.Count("coins"),_state.Settings.DeathCoinLoss);_state.Player.Inventory["coins"]=_sim.Count("coins")-lost;_hud.Notify($"The villagers carried you home. Lost {lost} crowns; belongings recovered.");}
            }
        }
    }

    private void Face(Vector3 target){var d=target-_player.Position;if(d.LengthSquared()>.01)_player.Rotation=new Vector3(0,Mathf.Atan2(d.X,d.Z),0);}
    private bool Near(float x,float z,float distance)=>FlatDistance(_player.Position,new Vector3(x,0,z))<distance;
    private static float FlatDistance(Vector3 a,Vector3 b)=>new Vector2(a.X-b.X,a.Z-b.Z).Length();
    private void Burst(Vector3 pos,Color color)
    {
        for(int i=0;i<8;i++){var n=new MeshInstance3D{Mesh=new BoxMesh{Size=Vector3.One*.1f},MaterialOverride=Visuals.Material(color),Position=pos};AddChild(n);Vector3 end=pos+new Vector3((float)GD.RandRange(-1,1),(float)GD.RandRange(.3,1.2),(float)GD.RandRange(-1,1));var t=n.CreateTween();t.TweenProperty(n,"position",end,.25);t.Parallel().TweenProperty(n,"scale",Vector3.Zero,.4);t.TweenCallback(Callable.From(()=>n.QueueFree()));}
    }
    private void Report(ActionResult result){_hud.Notify(result.Message);if(result.Success)_sound.Play("craft");UpdateHud();}
    private string Costs(Dictionary<string,int> costs)=>string.Join(" + ",costs.Select(x=>$"{x.Value} {_catalog.Items[x.Key].Name.ToLowerInvariant()}"));

    private void ShowInventory()
    {
        _hud.ShowInventory(_state,_catalog);
    }
    private void ShowCrafting(string stationId="hand")
    {
        _stationId=stationId;_hud.ShowCrafting(_state,_catalog,Workstations.Find(_state,stationId)?.Kind??"hand");
    }
    private void ShowBuilding(){if(_slot!=1){_hud.Notify("Equip your hammer to build.");return;}_hud.ShowBuilding(_state,_catalog);}
    private void ShowMarket()
    {
        var actions=new List<(string,string)>();foreach(string item in new[]{"wood","stone","berries","seeds","pine_cone","clay","iron_ore","grain"}){actions.Add(($"Buy {_catalog.Items[item].Name}  ·  {_sim.Price(item,true)} coins",$"trade:{item}:buy"));actions.Add(($"Sell {_catalog.Items[item].Name}  ·  {_sim.Price(item,false)} coins",$"trade:{item}:sell"));}
        _hud.ShowPanel("The village well",$"HARTHWOOD  •  {_state.Settlement.Prosperity}% prosperity\nFood in store: {_state.Settlement.Food}   Timber: {_state.Settlement.Timber}\n\n{(_state.EnemyDefeated?"The northwestern road is safe. The traders are returning.":"Mara says a bandit has claimed the old camp northwest of the village. Some caravans still slip through, but food is running low.")}\n\nYour coins: {_sim.Count("coins")}",actions);
    }
    private void Command(string command)
    {
        if(command.StartsWith("earthwork:")){HandleEarthworkCommand(command);return;}
        if(command.StartsWith("sandbox:")){HandleSandboxCommand(command);return;}
        var p=command.Split(':');
        switch(p[0])
        {
            case "continue": try{CreateWorld(SaveStore.Load(SavePath));_started=true;_hud.ShowGame();_map.Visible=true;_hud.Notify("Welcome home. The valley remembers.");}catch(Exception ex){_hud.Notify("Could not load the world: "+ex.Message);}break;
            case "resume":_hud.HidePanel();break;
            case "quit":if(_started)Save(false);QuitGame();break;
            case "save":Save();break;
            case "sound":_sound.Enabled=p.Length>1&&p[1]=="on";_hud.SetSoundEnabled(_sound.Enabled);SaveSettings();break;
            case "camera":
                bool always=p[1]=="mode"?p[2]=="always":_cameraRig.AlwaysRotate;
                bool invert=p[1]=="invert"?p[2]=="on":_cameraRig.InvertY;
                float sensitivity=p[1]=="sensitivity"&&float.TryParse(p[2],NumberStyles.Float,CultureInfo.InvariantCulture,out float value)?value:_cameraRig.Sensitivity;
                _cameraRig.Configure(always,sensitivity,invert);_hud.SetCameraOptions(always,_cameraRig.Sensitivity,invert);SaveSettings();break;
            case "slot":ActivateHotbar(int.Parse(p[1]));break;
            case "hotbar":HandleHotbarCommand(p);break;
            case "screen":ShowPlayerScreen(p[1]);break;
            case "item":if(p.Length>2&&p[1]=="use"){UseInventoryItem(p[2]);}break;
            case "eat":Report(_sim.Eat());ShowInventory();break;
            case "craft":int quantity=p.Length>2&&int.TryParse(p[2],out int amount)?amount:1;bool until=p.Length>3&&p[3]=="true";Report(_sim.EnqueueCraft(p[1],_stationId,quantity,until));_hud.ShowCrafting(_state,_catalog,_hud.ActiveCraftStation);_velocity=Vector3.Zero;break;
            case "watchwork":_hud.HidePanel();SyncCameraControl();break;
            case "cancelcraft":Report(_sim.CancelCraft());_hud.ShowCrafting(_state,_catalog,_hud.ActiveCraftStation);break;
            case "removequeue":Report(_sim.RemoveQueuedCraft(p[1]));_hud.ShowCrafting(_state,_catalog,_hud.ActiveCraftStation);break;
            case "clearqueue":Report(_sim.ClearCraftQueue());_hud.ShowCrafting(_state,_catalog,_hud.ActiveCraftStation);break;
            case "buildtool":if(_slot==1)SelectBuildTool(p[1]);break;
            case "buildshape":CycleBuildOption("shape");break;
            case "buildslope":CycleBuildOption("slope");break;
            case "buildmaterial":CycleBuildOption("material");break;
            case "buildappearance":CycleBuildOption("appearance");break;
            case "finishbrush":ToggleFinishBrush();break;
            case "buildmirror":CycleBuildOption("mirror");break;
            case "build":if(_slot!=1)break;_refinishing=false;_buildSelection=null;_buildKind=p[1];_dismantling=false;ResetBuildHeight();_hud.HidePanel();break;
            case "place":if(!_catalog.Buildings.ContainsKey(p[1]))break;_refinishing=false;_buildSelection=null;_buildKind=p[1];_dismantling=false;SelectSlot(1);ResetBuildHeight();_hud.HidePanel();break;
            case "reclaim":if(_slot!=1)break;_refinishing=false;_dismantling=true;ResetBuildHeight();_hud.HidePanel();break;
            case "trade":Report(_sim.Trade(p[1],p[2]=="buy"));ShowMarket();break;
        }
        SyncCameraControl();
    }
    private void SelectBuildTool(string id)
    {
        if(!_catalog.BuildingTools.TryGetValue(id,out var tool))return;
        if(!_toolSelections.TryGetValue(id,out var selection))_toolSelections[id]=selection=BuildingVariants.Default(tool);
        _buildSelection=selection;_refinishing=false;_dismantling=false;ResetBuildHeight();_hud.HidePanel();UpdateGhost();UpdateContext();
    }
    private void RotateBuild(int direction)
    {
        if(_buildSelection==null){_rotation+=direction*Mathf.Pi/4;return;}
        var options=_catalog.BuildingTools[_buildSelection.DefinitionId].AllowedRotations;
        float degrees=Mathf.PosMod(Mathf.RadToDeg(_rotation),360);
        int nearest=Enumerable.Range(0,options.Length).MinBy(i=>Mathf.Abs(Mathf.Wrap(options[i]-degrees,-180,180)));
        _rotation=Mathf.DegToRad(options[(nearest+direction+options.Length)%options.Length]);
        UpdateGhost();UpdateContext();
    }
    private void CycleBuildOption(string option,int direction=1)
    {
        if(_slot!=1||_dismantling||_buildSelection==null)return;
        if(!_refinishing)_heightAnchor??=_lastPlacementAim??MouseWorld();
        var tool=_catalog.BuildingTools[_buildSelection.DefinitionId];
        var variant=BuildingVariants.Variant(_catalog,_buildSelection);
        int Next(int current,int count)=>(current+direction+count)%count;
        if(_refinishing&&option!="appearance")return;
        if(option=="appearance")
        {
            var choices=new[]{""}.Concat(tool.Appearances.Select(a=>a.Id)).ToArray();
            _buildSelection.AppearanceId=choices[Next(Array.IndexOf(choices,_buildSelection.AppearanceId),choices.Length)];
        }
        else if(option=="shape")
        {
            if(_buildSelection.Slope>0)_toolSlopes[tool.Id]=_buildSelection.Slope;
            int index=tool.Variants.FindIndex(v=>v.Id==_buildSelection.VariantId);
            variant=tool.Variants[Next(index,tool.Variants.Count)];_buildSelection.VariantId=variant.Id;
            float remembered=_toolSlopes.GetValueOrDefault(tool.Id,tool.DefaultSlope);
            _buildSelection.Slope=variant.Slopes.Length==0?0:variant.Slopes.Contains(remembered)?remembered:variant.Slopes.Contains(45)?45:variant.Slopes[0];
            if(!variant.SupportsMirroring)_buildSelection.Mirrored=false;
        }
        else if(option=="slope"&&variant.Slopes.Length>0)
        {
            _buildSelection.Slope=variant.Slopes[Next(Array.IndexOf(variant.Slopes,_buildSelection.Slope),variant.Slopes.Length)];
            _toolSlopes[tool.Id]=_buildSelection.Slope;
        }
        else if(option=="material"&&tool.Materials.Count>1)
            _buildSelection.MaterialId=tool.Materials[Next(tool.Materials.FindIndex(m=>m.Id==_buildSelection.MaterialId),tool.Materials.Count)].Id;
        else if(option=="mirror"&&variant.SupportsMirroring)_buildSelection.Mirrored=!_buildSelection.Mirrored;
        UpdateGhost();UpdateContext();_hud.SetBuildOptions(_catalog,_buildSelection,_rotation,BuildLevel,_refinishing);
    }
    private int CurrentLevel()
    {
        var p=_player.Position;
        foreach(var stair in _state.Structures.Where(s=>s.Kind=="stairs"&&Construction.OnStairs(s,p.X,p.Z)))
            if(Mathf.Abs(Construction.StairHeight(_state,stair,p.X,p.Z)-p.Y)<.4f)return Math.Clamp(stair.Level+(p.Y-Construction.Base(_state,stair)>Construction.Storey*.5f?1:0),0,2);
        return _state.Structures.Where(s=>Construction.IsFloor(s)&&Construction.OnFloor(s,p.X,p.Z)&&Mathf.Abs(Construction.Base(_state,s)+Construction.Deck-p.Y)<.4f).Select(s=>s.Level).DefaultIfEmpty(0).Max();
    }
    private void UpdateHud()
    {
        if(_hud==null||_state==null)return;
        _hud.UpdateVitals(_state.Player.Health,_state.Player.Stamina);
        _hud.UpdateHotbar(_state,_catalog);
        int minutes=(int)_state.CalendarMinutes;int day=minutes/(24*60)+1;int hour=minutes/60%24;
        var nextQuest=QuestTracker.Definitions.FirstOrDefault(q=>!_state.CompletedQuests.Contains(q.Id));
        string rumor=nextQuest==null?"A foothold in the valley. Your starting quests are complete.":nextQuest.Objective+"  [J] Journal";
        _hud.UpdateWorld($"SPRING · DAY {day}   /   {hour:00}:{minutes%60:00}",$"Wood {_sim.Count("wood")}   Stone {_sim.Count("stone")}   Coins {_sim.Count("coins")}",rumor);
        _map.PlayerPosition=new Vector2(_state.Player.X,_state.Player.Z);_map.Visible=_started&&!_hud.IsTitleVisible;
    }
    private void Save(bool notify=true)
    {
        try{SaveStore.Save(SavePath,_state);if(notify)_hud.Notify("World saved. Your place in the valley is safe.");}
        catch(Exception ex){_hud.Notify("Save failed: "+ex.Message);GD.PushError(ex.ToString());}
    }
    private void LoadSettings()
    {
        var config=new ConfigFile();if(config.Load(SettingsPath)!=Error.Ok)return;
        _cameraRig.Configure(config.GetValue("camera","always_rotate",false).AsBool(),(float)config.GetValue("camera","sensitivity",1.0).AsDouble(),config.GetValue("camera","invert_y",false).AsBool());
        _sound.Enabled=config.GetValue("audio","enabled",true).AsBool();
        int mode=config.GetValue("building","snap_mode",0).AsInt32();_snapMode=Enum.IsDefined(typeof(BuildSnapMode),mode)?(BuildSnapMode)mode:BuildSnapMode.Object;
        _hud.SetCameraOptions(_cameraRig.AlwaysRotate,_cameraRig.Sensitivity,_cameraRig.InvertY);_hud.SetSoundEnabled(_sound.Enabled);
    }
    private void SaveSettings()
    {
        var config=new ConfigFile();config.SetValue("camera","always_rotate",_cameraRig.AlwaysRotate);config.SetValue("camera","sensitivity",_cameraRig.Sensitivity);config.SetValue("camera","invert_y",_cameraRig.InvertY);config.SetValue("audio","enabled",_sound.Enabled);config.SetValue("building","snap_mode",(int)_snapMode);
        if(config.Save(SettingsPath)!=Error.Ok)_hud.Notify("Could not save options. They still apply for this session.");
    }
    public override void _Notification(int what)
    {
        if(what==NotificationApplicationFocusOut&&_cameraRig!=null){_cameraRig.SetFocused(false);_velocity=Vector3.Zero;}
        if(what==NotificationApplicationFocusIn&&_cameraRig!=null)_cameraRig.SetFocused(true);
        if(what==NotificationWMCloseRequest){if(_cameraRig!=null)_cameraRig.Release();if(_started)Save(false);QuitGame();}
    }

    private async void QuitGame(int code=0)
    {
        if(_quitting)return;_quitting=true;
        SetProcess(false);SetProcessInput(false);SetProcessUnhandledInput(false);
        _craftAnimation?.Clear();
        ResetEarthworkPresentation();_craftAnimation=null;
        _cameraRig?.Release();
        foreach(var child in GetChildren())child.QueueFree();
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        _valley=null!;_player=null!;_ghost=null!;_camera=null!;_cameraRig=null!;_hud=null!;_sound=null!;_map=null!;
        foreach(var materials in _variantGhostMaterials.Values){foreach(var material in materials.Valid)material.Dispose();foreach(var material in materials.Invalid)material.Dispose();}_variantGhostMaterials.Clear();
        WoodlandAssets.Release();Visuals.ReleaseMaterials();_validGhost.Dispose();_invalidGhost.Dispose();
        // Drain managed resource finalizers while the native engine is still alive.
        GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();GC.WaitForPendingFinalizers();
        GetTree().Quit(code);
    }

    private void RunSmokeTest()
    {
        try
        {
            _hud.HidePanel();
            VerifyCameraControls();VerifyWoodland();VerifyTerrainRegions();VerifyTerrainStreaming();VerifyEarthworks();VerifyLandscapeSetup();
            // Exercise the same input handlers used by the shipped session.
            _UnhandledInput(new InputEventKey{Keycode=Key.I,Pressed=true});
            if(!_hud.IsPanelOpen)throw new Exception("Inventory shortcut failed");
            _UnhandledInput(new InputEventKey{Keycode=Key.Escape,Pressed=true});
            if(_hud.IsPanelOpen)throw new Exception("Close panel shortcut failed");
            Vector3 before=_player.Position;
            for(int step=0;step<15;step++)MovePlayer(.016f,new Vector2(1,0));
            if(FlatDistance(before,_player.Position)<.2f)throw new Exception("Movement input failed");
            _velocity=Vector3.Zero;
            _buildKind="campfire";
            if(CanBuildAt(_player.Position))throw new Exception("Building could trap the player");
            var spawn=_valley.Spawns.Values.First(x=>x.Kind=="tree");
            _player.Position=_valley.At(spawn.X+1,spawn.Z);_state.Player.X=spawn.X+1;_state.Player.Z=spawn.Z;
            for(int i=0;i<3;i++){_attackCooldown=0;Gather(spawn.Id);}
            if(!_state.RemovedResources.Contains(spawn.Id))throw new Exception("Gather interaction failed");
            _state.Player.X=6;_state.Player.Z=3;_state.Player.Y=_valley.Height(6,3);_player.Position=_valley.At(6,3);
            var craft=_sim.Craft("planks","village:carpenter");if(!craft.Success)throw new Exception(craft.Message);_sim.TickCraft(6);
            _state.Player.Inventory["wood"]=30;_state.Player.Inventory["stone"]=30;
            var site=new Vector3(-18,0,20);
            for(int x=-20;x<20;x+=3)if(_valley.CanPlace(_valley.At(x,20),2)){site=_valley.At(x,20);break;}
            _state.Player.X=site.X;_state.Player.Z=site.Z-2;_player.Position=_valley.At(_state.Player.X,_state.Player.Z);
            var build=_sim.Build("campfire",site.X,site.Z);if(!build.Success)throw new Exception(build.Message);_valley.AddStructure(_state.Structures[^1]);
            var soil=_sim.ModifyTerrain(18,20,-.2f);if(!soil.Success)throw new Exception(soil.Message);_valley.RefreshTerrain(_state.TerrainEdits[^1]);
            var plant=_sim.Plant(18,20);if(!plant.Success)throw new Exception(plant.Message);_valley.RefreshCrops();
            _sim.Tick(65);var crop=_state.Crops.First();if(!_sim.Harvest(crop.Id).Success)throw new Exception("Harvest failed");_valley.RefreshCrops();
            _UnhandledInput(new InputEventKey{Keycode=Key.Key5,Pressed=true});
            if(_slot!=4)throw new Exception("Tool shortcut failed");
            _player.Position=_valley.EnemyHome+Vector3.Back*2;
            _state.Player.Health=100;_state.Player.Stamina=100;
            SyncCameraControl();_cameraRig.HandleButton(new InputEventMouseButton{ButtonIndex=MouseButton.Middle,Pressed=true});
            if(!_cameraRig.Captured)throw new Exception("Camera should orbit during combat");
            Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=true});
            Input.FlushBufferedEvents();
            _enemyWindup=.05f;TickEnemy(.1f);
            Input.ParseInputEvent(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=false});
            Input.FlushBufferedEvents();
            if(_state.Player.Health!=97)throw new Exception($"Block failed: health={_state.Player.Health}, slot={_slot}, distance={FlatDistance(_player.Position,_valley.Enemy.Position)}");
            _dodge=.2f;_enemyWindup=.05f;TickEnemy(.1f);
            if(_state.Player.Health!=97)throw new Exception("Dodge invulnerability failed");
            _dodge=0;
            for(int hit=0;hit<4;hit++){_player.Position=_valley.Enemy.Position+Vector3.Back*2;_state.Player.Stamina=100;_attackCooldown=0;Attack();}
            if(!_state.EnemyDefeated)throw new Exception("Combat victory did not persist");
            _cameraRig.HandleButton(new InputEventMouseButton{ButtonIndex=MouseButton.Middle,Pressed=false});
            _state.Player.X=0;_state.Player.Z=8;_player.Position=_valley.At(0,8);
            SaveStore.Save(SavePath,_state);var loaded=SaveStore.Load(SavePath);
            if(loaded.Structures.Count!=1||loaded.TerrainEdits.Count!=1||!loaded.RemovedResources.Contains(spawn.Id)||!loaded.EnemyDefeated)throw new Exception("Persistent world roundtrip failed");
            _snapMode=BuildSnapMode.Object;CreateWorld(loaded);VerifyConstruction();VerifyWorkshopAndFloors();VerifyBuildHeightControls();VerifySnappingAndQueue();VerifyPlayerMenus();VerifyEcology();VerifyBuildingVariants();VerifyRoofJunctions();if(_view!="variants")SelectSlot(0);_hud.ShowGame();
            if(_view.StartsWith("work-")){VerifyCraftActions();ShowCraftActionPreview();}
            if(_view=="finishes")VerifyBuildingFinishes();
            if(_view=="forest")ShowForestPreview();
            if(_view.StartsWith("terrain"))ShowTerrainPreview();
            if(_view.StartsWith("earthwork"))ShowEarthworkPreview();
            if(_view.StartsWith("landscape")||_view=="world-setup")ShowLandscapePreview();
            if(_view is "ecology" or "sandbox")ShowEcologyShowcase();
            if(_view=="sandbox")Command("sandbox:open");
            if(_view is "character" or "skills" or "journal")ShowPlayerScreen(_view);
            if(_view=="hotbar"){CreateWorld(new WorldState());_hud.ShowGame();ShowInventory();}
            if(_view=="build"){SelectSlot(1);ShowBuilding();}if(_view=="height"){SelectSlot(1);Command("build:wall");_heightAnchor=new Vector3(-15,0,18.5f);_buildHeightOffset=1;UpdateGhost();UpdateContext();}if(_view=="inventory")ShowInventory();if(_view=="craft")ShowCrafting();if(_view=="station"){_state.Player.X=6;_state.Player.Z=3;_state.Player.Y=_valley.Height(6,3);_player.Position=_valley.At(6,3);ShowCrafting("village:carpenter");Command("craft:planks");}
            if(_view=="queue")
            {
                _state.Player.X=6;_state.Player.Z=3;_state.Player.Y=_valley.Height(6,3);_player.Position=_valley.At(6,3);
                ShowCrafting("village:carpenter");Command("craft:planks:8:false");Command("craft:chair:2:false");Command("craft:table:1:false");
            }
            _testComplete=true;_hud.Notify("Harthwood Valley  •  A place of your own");
            GD.Print("SMOKE_TEST_PASS: camera orbit, cursor lifecycle, camera settings, movement, menus, tool switching, gather, craft, placement safety, build, terrain, crop, block, dodge, combat, save/reload, rendered scene");
        }
        catch(Exception ex){GD.PushError("SMOKE_TEST_FAIL "+ex);QuitGame(2);}
    }
    private void VerifyConstruction()
    {
        void Check(bool value,string reason){if(!value)throw new Exception("Construction integration: "+reason);}
        // Use the isolated smoke world, clearing only the demonstration building site.
        foreach(var resource in WorldGenerator.Generate(_state.Seed))
            if(Construction.Distance(resource.X,resource.Z,-13.5f,23)<7)_state.RemovedResources.Add(resource.Id);
        foreach(string material in new[]{"wood","stone","plank"})_state.Player.Inventory[material]=100;
        void Stand(float x,float z){_state.Player.X=x;_state.Player.Z=z;_player.Position=new Vector3(x,_valley.WalkHeight(x,z),z);}
        StructureState Build(string kind,float x,float z,float rotation=0){var result=_sim.Build(kind,x,z,rotation);Check(result.Success,result.Message);return _state.Structures[^1];}
        Stand(-15,20);var floor=Build("foundation",-15,23);Build("foundation",-12,23);
        Build("wall",-15,21.5f);Build("wall",-12,21.5f);
        Stand(-18,23);var door=Build("door",-15,24.5f);Stand(-15,26);Build("wall",-12,24.5f);
        Stand(-15,26);Build("wall",-16.5f,23,Mathf.Pi/2);Build("wall",-10.5f,23,Mathf.Pi/2);
        var roof=Build("roof",-15,23);Build("roof",-12,23);
        _valley.RebuildStructures();
        Check(!_valley.CanWalk(new Vector3(door.X,0,door.Z)),"closed door blocks in-engine movement");
        Check(_sim.ToggleDoor(door.Id).Success,"door interaction");_valley.RebuildStructures();
        Check(_valley.CanWalk(new Vector3(door.X,0,door.Z)),"open door permits in-engine movement");
        Check(!_valley.CanWalk(new Vector3(-15,0,21.5f)),"wall blocks in-engine movement");
        Check(Mathf.Abs(_valley.WalkHeight(-15,23)-floor.Elevation!.Value-Construction.Deck)<.001f,"deck supports character");
        _valley.UpdateShelterVisibility(new Vector3(-15,0,23));Check(!_valley.StructureNodes[roof.Id].Visible,"interior roof cutaway");
        _valley.UpdateShelterVisibility(new Vector3(-15,0,29));Check(_valley.StructureNodes[roof.Id].Visible,"exterior roof restored");
        Check(!_sim.Dismantle(floor.Id).Success,"loaded floor cannot be dismantled");
        Stand(-15,27);SaveStore.Save(SavePath,_state);var restored=SaveStore.Load(SavePath);
        Check(restored.Structures.Single(s=>s.Id==door.Id).DoorOpen,"open door persists");
        Check(restored.Structures.Single(s=>s.Id==roof.Id).SupportId==floor.Id,"roof support persists");
        CreateWorld(restored);_valley.UpdateShelterVisibility(_player.Position);
        if(_view=="interior"){Stand(-14,23);_valley.UpdateShelterVisibility(_player.Position);}
        _cameraRig.Orbit.Zoom(-5);_cameraRig.Orbit.Look(-220,30,1,false);for(int i=0;i<100;i++)_cameraRig.Orbit.Step(.016f);
        _cameraRig.SnapFocus(_player.Position);_cameraRig.Follow(_camera,_player.Position,0);
        GD.Print("CONSTRUCTION_TEST_PASS: snapped shelter, raised deck, wall/door collision, door interaction, roof cutaway, safe dismantling, save/reload");
    }
    private void VerifyWorkshopAndFloors()
    {
        void Check(bool value,string reason){if(!value)throw new Exception("Workshop/floor integration: "+reason);}
        SelectSlot(0);_hud.HidePanel();_UnhandledInput(new InputEventKey{Keycode=Key.B,Pressed=true});Check(!_hud.IsPanelOpen,"B requires hammer");
        SelectSlot(1);_UnhandledInput(new InputEventKey{Keycode=Key.B,Pressed=true});Check(_hud.IsBuildingOpen,"hammer opens build grid");_hud.HidePanel();
        ShowInventory();Check(_hud.IsInventoryOpen,"inventory grid opens");_hud.HidePanel();
        _state.Player.X=6;_state.Player.Z=3;_state.Player.Y=_valley.Height(6,3);_player.Position=_valley.At(6,3);
        ShowCrafting("village:carpenter");int boards=_sim.Count("plank");Command("craft:planks");
        Check(_state.CraftJob!=null&&_sim.Count("plank")==boards,"station reserves ingredients with no instant output");
        _sim.TickCraft(2);SaveStore.Save(SavePath,_state);var saved=SaveStore.Load(SavePath);Check(saved.CraftJob?.Remaining==4,"timed job persists");
        CreateWorld(saved);Check(_sim.TickCraft(4)?.Success==true&&_sim.Count("plank")==boards+1,"saved task completes once");_hud.HidePanel();
        _state.Structures.Clear();foreach(var r in WorldGenerator.Generate(_state.Seed))if(Construction.Distance(r.X,r.Z,-14,22)<10)_state.RemovedResources.Add(r.Id);
        foreach(var id in _catalog.Items.Keys)_state.Player.Inventory[id]=100;
        void Stand(float x,float z,float? y=null){_state.Player.X=x;_state.Player.Z=z;_state.Player.Y=y??_valley.Height(x,z);_player.Position=new Vector3(x,_state.Player.Y.Value,z);}
        StructureState Build(string kind,float x,float z,int level=0,float angle=0){Stand(x+4,z);var result=_sim.Build(kind,x,z,angle,level);Check(result.Success,result.Message);return _state.Structures[^1];}
        var ground=Build("foundation",-15,23);Build("foundation",-15,20);Build("foundation",-12,20);Build("foundation",-12,23);
        Build("wall",-16.5f,23,0,Mathf.Pi/2);Build("wall",-15,18.5f);Build("wall",-12,18.5f);
        Build("stairs",-15,23,0,Mathf.Pi);
        var landing=Build("upper_floor",-15,20,1);Build("upper_floor",-12,20,1);Build("upper_floor",-12,23,1);
        Build("wall",-15,18.5f,1);Build("wall",-12,18.5f,1);Build("wall",-10.5f,20,1,Mathf.Pi/2);
        Build("roof",-15,20,1);Build("roof",-12,20,1);Build("roof",-12,23,1);
        Build("planter",-12,23,1);Build("painting",-12,20,1);
        Stand(-15,24.8f,Construction.Base(_state,ground)+Construction.Deck);CreateWorld(_state);_cameraRig.SetFocused(true);_velocity=Vector3.Zero;
        var north=new Vector2(0,-1).Rotated(_cameraRig.Orbit.Yaw);
        for(int step=0;step<64;step++)MovePlayer(.016f,north);
        _velocity=Vector3.Zero;
        Check(_player.Position.Z<21.5f&&Mathf.Abs(_player.Position.Y-Construction.Base(_state,landing)-Construction.Deck)<.05f,"movement climbs stairs onto upper landing");
        SaveStore.Save(SavePath,_state);CreateWorld(SaveStore.Load(SavePath));Check(Mathf.Abs(_player.Position.Y-Construction.Base(_state,landing)-Construction.Deck)<.05f,"reload retains upstairs position");
        if(_view!="interior"){Stand(-17,28);_player.Position=new Vector3(-17,_valley.WalkHeight(-17,28),28);}
        _valley.UpdateShelterVisibility(_player.Position);_cameraRig.SnapFocus(_player.Position);_cameraRig.Follow(_camera,_player.Position,0);
        GD.Print("WORKSHOP_FLOOR_TEST_PASS: hammer-only B, grid menus, timed station craft, task save/reload, L-shaped upper roof, stairs movement, upstairs save, decorations");
    }
    private void VerifyBuildHeightControls()
    {
        void Check(bool value,string reason){if(!value)throw new Exception("Build height: "+reason);}
        _hud.HidePanel();_cameraRig.SetFocused(true);SelectSlot(1);SyncCameraControl();
        Vector3 original=_player.Position;
        void Stand(Vector3 p){_player.Position=p;_state.Player.X=p.X;_state.Player.Z=p.Z;_state.Player.Y=p.Y;}
        void Wheel(bool up,bool alt=true)=>_UnhandledInput(new InputEventMouseButton{ButtonIndex=up?MouseButton.WheelUp:MouseButton.WheelDown,Pressed=true,AltPressed=alt});
        Stand(new Vector3(-17,_valley.WalkHeight(-17,24),24));
        foreach(string kind in new[]{"foundation","wall","door","roof","roof_flat","roof_slope","roof_corner","stairs","chair","carpenter"})
        {
            Command("build:"+kind);Check(BuildLevel==0,"new selection starts on character's floor");
            _heightAnchor=new Vector3(-12,0,23);Vector3 target=MouseWorld();float zoom=_cameraRig.Orbit.TargetDistance;
            Wheel(true);Check(BuildLevel==1&&_cameraRig.Orbit.TargetDistance==zoom,"Alt wheel raises without zoom for "+kind);
            Check(MouseWorld()==target,"height adjustment preserves horizontal aim");
            Check(_sim.PreviewBuild(BuildKind,target.X,target.Z,_rotation,BuildLevel).Level==1,"preview uses adjusted height");
            Check(kind!="foundation"||BuildKind=="upper_floor","floor adapts at raised height");
            Wheel(false);Check(BuildLevel==0,"Alt wheel lowers for "+kind);
        }
        Wheel(false);Check(BuildLevel==0,"lower bound");for(int i=0;i<4;i++)Wheel(true);Check(BuildLevel==2,"upper bound");
        _UnhandledInput(new InputEventKey{Keycode=Key.H,Pressed=true});Check(BuildLevel==0&&_heightAnchor==null,"H resets to feet and frees aim");
        float before=_cameraRig.Orbit.TargetDistance;Wheel(true,false);Check(_cameraRig.Orbit.TargetDistance<before&&BuildLevel==0,"plain wheel zooms");
        ShowBuilding();Wheel(true);Check(BuildLevel==0,"scrolling a menu cannot adjust placement");_hud.HidePanel();SyncCameraControl();
        _cameraRig.HandleButton(new InputEventMouseButton{ButtonIndex=MouseButton.Middle,Pressed=true});
        before=_cameraRig.Orbit.TargetDistance;_Input(new InputEventMouseButton{ButtonIndex=MouseButton.WheelUp,Pressed=true,AltPressed=true});
        Check(BuildLevel==1&&_cameraRig.Orbit.TargetDistance==before,"height control works during captured orbit");
        _cameraRig.HandleButton(new InputEventMouseButton{ButtonIndex=MouseButton.Middle,Pressed=false});
        var landing=_state.Structures.First(s=>s.Kind=="upper_floor");Stand(new Vector3(landing.X,Construction.Base(_state,landing)+Construction.Deck,landing.Z));
        Command("build:wall");Check(BuildLevel==1,"selection upstairs starts upstairs");Wheel(true);Check(BuildLevel==2,"raise above character upstairs");Wheel(false);Wheel(false);Check(BuildLevel==0,"lower below character upstairs");
        // Commit a floor with the same resolved kind/height used by the live preview.
        Stand(new Vector3(-17,_valley.WalkHeight(-17,24),24));Command("build:foundation");_heightAnchor=new Vector3(-12,0,23);Wheel(true);
        var existing=_state.Structures.Single(s=>s.Kind=="upper_floor"&&s.X==-12&&s.Z==23);var dependents=_state.Structures.Where(s=>s.SupportId==existing.Id).ToArray();
        foreach(var dependent in dependents)_state.Structures.Remove(dependent);_state.Structures.Remove(existing);
        var plan=_sim.PreviewBuild(BuildKind,-12,23,0,BuildLevel);Check(plan.Valid,"raised floor preview is permitted");
        int boards=_sim.Count("plank"),wood=_sim.Count("wood");_attackCooldown=0;UseTool();
        var placed=_state.Structures.Last();Check(placed.Kind=="upper_floor"&&placed.Level==1&&placed.Elevation==plan.Elevation,"click commits the preview's kind and height");
        Check(_sim.Count("plank")==boards-3&&_sim.Count("wood")==wood-2,"raised floor charges its displayed materials");
        _state.Structures.Remove(placed);_state.Structures.Add(existing);_state.Structures.AddRange(dependents);_state.Player.Inventory["plank"]=boards;_state.Player.Inventory["wood"]=wood;
        _valley.RebuildStructures();Stand(original);ResetBuildHeight();SelectSlot(0);_cameraRig.Orbit.Zoom(1);
        GD.Print("BUILD_HEIGHT_TEST_PASS: all piece families, relative floors, anchored aim, floor adaptation, preview/commit costs, bounds, reset, menu isolation, orbit and zoom");
    }
    private void VerifySnappingAndQueue()
    {
        void Check(bool value,string reason){if(!value)throw new Exception("Snapping/queue integration: "+reason);}
        Vector3 original=_player.Position;
        void Stand(float x,float z){_state.Player.X=x;_state.Player.Z=z;_state.Player.Y=_valley.WalkHeight(x,z);_player.Position=new Vector3(x,_state.Player.Y.Value,z);}
        _hud.HidePanel();SelectSlot(1);_snapMode=BuildSnapMode.Object;
        void KeyPress(Key key,bool shift=false)=>_UnhandledInput(new InputEventKey{Keycode=key,Pressed=true,ShiftPressed=shift});
        KeyPress(Key.V);Check(_snapMode==BuildSnapMode.Grid,"V selects grid");KeyPress(Key.V);Check(_snapMode==BuildSnapMode.Free,"V selects free");
        SaveSettings();_snapMode=BuildSnapMode.Object;LoadSettings();Check(_snapMode==BuildSnapMode.Free,"snap preference persists");
        _rotation=0;KeyPress(Key.R);Check(Mathf.Abs(_rotation-Mathf.Pi/4)<.001f,"R rotates 45 degrees");KeyPress(Key.R,true);Check(Mathf.Abs(_rotation)<.001f,"Shift R reverses rotation");KeyPress(Key.R);
        foreach(var r in WorldGenerator.Generate(_state.Seed))if(Construction.Distance(r.X,r.Z,-25,24)<9)_state.RemovedResources.Add(r.Id);
        foreach(var id in _catalog.Items.Keys)_state.Player.Inventory[id]=100;
        Stand(-29,23);Command("build:foundation");_heightAnchor=new Vector3(-25.13f,0,23.27f);
        var plan=_sim.PreviewBuild(BuildKind,-25.13f,23.27f,_rotation,BuildLevel,_snapMode);Check(plan.Valid,plan.Message);
        _attackCooldown=0;UseTool();var first=_state.Structures[^1];
        Check(first.Kind=="foundation"&&Mathf.Abs(first.X-plan.X)<.001f&&Mathf.Abs(first.Z-plan.Z)<.001f&&Mathf.Abs(first.Rotation-plan.Rotation)<.001f,"free preview and click agree");
        KeyPress(Key.V);Check(_snapMode==BuildSnapMode.Object,"V cycles back to object snapping");
        StructureState Build(string kind,float x,float z,float rotation=0)
        {Stand(x-4,z);var result=_sim.Build(kind,x,z,rotation,0,BuildSnapMode.Object);Check(result.Success,kind+": "+result.Message);return _state.Structures[^1];}
        var port=Construction.World(first,3,0);var second=Build("foundation",port.X+.08f,port.Z-.06f);
        Check(Construction.SharesEdge(first,second)&&Mathf.Abs(Mathf.Sin(2*(second.Rotation-first.Rotation)))<.001f,"new floor joins angled frame");
        var corner=Construction.World(first,0,3);var triangle=Build("foundation_triangle",corner.X,corner.Z,first.Rotation);
        var edge=Construction.World(first,0,-1.5f);Build("wall",edge.X,edge.Z,first.Rotation);
        Build("wall_diagonal",triangle.X,triangle.Z,triangle.Rotation+Mathf.Pi/4);
        Build("roof",first.X,first.Z);Build("roof",second.X,second.Z);Build("roof_triangle",triangle.X,triangle.Z,triangle.Rotation);
        _valley.RebuildStructures();Check(_valley.StructureNodes.ContainsKey(triangle.Id),"triangular piece rendered");
        SaveStore.Save(SavePath,_state);CreateWorld(SaveStore.Load(SavePath));Check(_state.Structures.Any(s=>s.Id==triangle.Id&&s.Kind=="foundation_triangle"),"new geometry survives save and scene rebuild");
        Stand(6,3);ShowCrafting("village:carpenter");
        var recipe=_hud.FindChild("Recipe_planks",true,false) as Button;Check(recipe!=null,"recipe card exists");
        recipe!.EmitSignal(BaseButton.SignalName.Pressed);
        var quantity=_hud.FindChild("CraftQuantity",true,false) as SpinBox;Check(quantity!=null,"batch quantity control exists");quantity!.Value=3;
        int boards=_sim.Count("plank");
        recipe.EmitSignal(Control.SignalName.GuiInput,new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,DoubleClick=true});
        Check(_state.CraftQueue.Count==1&&_state.CraftQueue[0].RemainingCount==2&&_state.CraftJob?.RecipeId=="planks","double-click queues exactly one chosen batch order");
        Check((_hud.FindChild("CraftQuantity",true,false) as SpinBox)?.Value==3,"quantity survives queue refresh");
        Command("craft:stone_axe:2:false");Check(_state.CraftQueue.Count==2,"different recipes share a queue");
        _sim.TickCraft(2);SaveStore.Save(SavePath,_state);CreateWorld(SaveStore.Load(SavePath));
        Check(_state.CraftQueue.Count==2&&_state.CraftJob?.Remaining==4,"active progress and pending batches persist");
        _sim.TickCraft(24);Check(_state.CraftJob==null&&_state.CraftQueue.Count==0&&_sim.Count("plank")==boards+3,"mixed queue completes all requested batches");
        ShowCrafting();var hand=_hud.FindChild("Recipe_stone_axe",true,false) as Button;Check(hand!=null,"hand recipe exists");hand!.EmitSignal(BaseButton.SignalName.Pressed);
        var until=_hud.FindChild("CraftUntilMaterials",true,false) as CheckButton;Check(until!=null,"until-materials control exists");until!.ButtonPressed=true;
        (_hud.FindChild("CraftEnqueue",true,false) as Button)!.EmitSignal(BaseButton.SignalName.Pressed);
        Check(_state.CraftQueue.Count==1&&_state.CraftQueue[0].UntilMaterials,"checkbox creates an until-materials order");
        Command("clearqueue");Check(_state.CraftJob==null&&_state.CraftQueue.Count==0,"clear queue cancels active and pending work");
        _hud.HidePanel();_rotation=0;ResetBuildHeight();SaveSettings();
        if(_view=="snapping")
        {
            Stand(-23.5f,29.5f);_cameraRig.Orbit.Reset();_cameraRig.Orbit.Zoom(-4);_cameraRig.Orbit.Look(-100,45,1,false);
            for(int i=0;i<100;i++)_cameraRig.Orbit.Step(.016f);
        }
        else Stand(original.X,original.Z);
        _valley.UpdateShelterVisibility(_player.Position);_cameraRig.SnapFocus(_player.Position);_cameraRig.Follow(_camera,_player.Position,0);
        GD.Print("SNAP_QUEUE_TEST_PASS: mode cycle and persistence, 45-degree rotation, free preview/commit, angled connected floors, triangular roof, scene rebuild, recipe double-click, quantity, mixed queue, save/resume, until checkbox and cancellation");
    }
    private void VerifyRecipePointerInput()
    {
        try
        {
            if(_frames==46)
            {
                var quantity=_hud.FindChild("CraftQuantity",true,false) as SpinBox??throw new Exception("Missing quantity control after first pointer click");
                quantity.Value=2;return;
            }
            var card=_hud.FindChild("Recipe_planks",true,false) as Button??throw new Exception("Missing recipe card");
            var point=card.GetGlobalRect().GetCenter();
            if(_frames==45)_queueBeforePointerTest=_state.CraftQueue.Count;
            GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true,DoubleClick=_frames==47,Position=point,GlobalPosition=point},true);
            GetViewport().PushInput(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false,Position=point,GlobalPosition=point},true);
            if(_frames==45&&_state.CraftQueue.Count!=_queueBeforePointerTest)throw new Exception("Single click must only select recipe");
            if(_frames==47)
            {
                if(_state.CraftQueue.Count!=_queueBeforePointerTest+1||_state.CraftQueue[^1].RemainingCount!=2)throw new Exception("Pointer double-click must queue exactly one order with the selected quantity");
                Command("removequeue:"+_state.CraftQueue[^1].Id);
                GD.Print("RECIPE_POINTER_TEST_PASS: actual viewport single-click selects, double-click queues chosen quantity once, release is safe");
            }
        }
        catch(Exception error){GD.PushError("RECIPE_POINTER_TEST_FAIL "+error);QuitGame(2);}
    }
    private void VerifyCameraControls()
    {
        void Check(bool value,string reason){if(!value)throw new Exception("Camera integration: "+reason);}
        _cameraRig.SetFocused(true);_cameraRig.Configure(false,1,false);SelectSlot(4);SyncCameraControl();
        _cameraRig.HandleButton(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=true});
        _cameraRig.HandleButton(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=true});
        Check(_cameraRig.Captured,"both buttons should capture cursor");
        float before=_cameraRig.Orbit.TargetYaw;
        var motion=new InputEventMouseMotion{ScreenRelative=new Vector2(40,12)};
        _cameraRig.Motion(motion);_cameraRig.Motion(motion);
        _cameraRig.StepInput(.016f,false);
        Check(_cameraRig.Orbit.TargetYaw<before,"drag should change orbit");
        Check(!_cameraRig.Pointer.TakePrimary()&&!_cameraRig.Pointer.Blocking,"orbit chord must not attack or block");
        _cameraRig.HandleButton(new InputEventMouseButton{ButtonIndex=MouseButton.Left,Pressed=false});
        _cameraRig.HandleButton(new InputEventMouseButton{ButtonIndex=MouseButton.Right,Pressed=false});
        Check(!_cameraRig.Captured&&!_cameraRig.Pointer.TakeSecondary(),"chord release should free cursor without cancellation");
        _cameraRig.Configure(true,1.7f,true);SyncCameraControl();
        Check(_cameraRig.Captured,"always mode should capture during combat");
        _hud.ShowPause();SyncCameraControl();before=_cameraRig.Orbit.TargetYaw;_cameraRig.Motion(motion);
        Check(!_cameraRig.Captured&&_cameraRig.Orbit.TargetYaw==before,"pause must release pointer and stop mouse look");
        _hud.HidePanel();SyncCameraControl();Check(_cameraRig.Captured,"resume should restore mouse look");
        Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=Key.Tab,Keycode=Key.Tab,Pressed=true});Input.FlushBufferedEvents();_cameraRig.StepInput(.016f,false);
        Check(!_cameraRig.Captured,"Tab should free the cursor");
        Input.ParseInputEvent(new InputEventKey{PhysicalKeycode=Key.Tab,Keycode=Key.Tab,Pressed=false});Input.FlushBufferedEvents();_cameraRig.StepInput(.016f,false);
        Check(_cameraRig.Captured,"releasing Tab should resume look");
        SelectSlot(1);SyncCameraControl();Check(!_cameraRig.Captured,"building should use a free placement cursor");
        SelectSlot(4);SyncCameraControl();Check(_cameraRig.Captured,"returning to combat should resume look");
        _cameraRig.SetFocused(false);Check(!_cameraRig.Captured&&!_cameraRig.Pointer.Blocking,"focus loss must clear capture and combat input");
        _cameraRig.SetFocused(true);Check(_cameraRig.Captured,"focus restoration should resume configured control");
        SaveSettings();_cameraRig.Configure(false,1,false);LoadSettings();
        Check(_cameraRig.AlwaysRotate&&Mathf.Abs(_cameraRig.Sensitivity-1.7f)<.001f&&_cameraRig.InvertY,"camera preferences must survive a reload");
        _cameraRig.Configure(false,1,false);_hud.SetCameraOptions(false,1,false);SaveSettings();SelectSlot(0);SyncCameraControl();
        _cameraRig.Orbit.Reset();for(int i=0;i<100;i++)_cameraRig.Orbit.Step(.016f);
        GD.Print("CAMERA_TEST_PASS: drag chord, no accidental actions, always look, pause/resume, Tab, placement, focus, settings persistence");
    }
    private async void CaptureAndFinish()
    {
        if(DisplayServer.GetName()=="headless"){QuitGame();return;}
        await ToSignal(RenderingServer.Singleton,RenderingServer.SignalName.FramePostDraw);
        if(DisplayServer.GetName()!="headless")
        {
            string path=_capture!=""?_capture:ProjectSettings.GlobalizePath("res://artifacts/gameplay.png");
            GetViewport().GetTexture().GetImage().SavePng(path);GD.Print("CAPTURED "+path);
        }
        if(_test||_capture!="")QuitGame();
    }
}


