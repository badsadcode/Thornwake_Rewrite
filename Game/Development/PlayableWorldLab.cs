using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Thornwake.Core;

namespace Thornwake.Development;

/// <summary>Isolated A4 validation controller; no normal WorldState, saves or gameplay activation.</summary>
public partial class PlayableWorldLab : Node3D
{
    private PlayableWorldTerrain _terrain=null!;
    private CharacterBody3D _player=null!;
    private Camera3D _camera=null!;
    private readonly OrbitState _orbit=new();
    private MacroWorldGenerator? _world;
    private RegionalDrainageRefiner? _regional;
    private IReadOnlyList<MacroWorldProfile> _profiles=null!;
    private IReadOnlyList<LabLandmark> _landmarks=Array.Empty<LabLandmark>();
    private WorldPoint _position,_origin,_previous;
    private Task<(MacroWorldGenerator World,RegionalDrainageRefiner Regional,IReadOnlyList<LabLandmark> Landmarks)>? _setup;
    private CancellationTokenSource? _setupCancel;
    private int _rebases,_worldRevision;
    private bool _fly,_noclip,_gravity=true,_hold=true,_jump,_closing,_showOrigin=true;
    private bool _boundaries,_streamOverlay,_corridors=true,_basins=true,_waterStatus=true;
    private int _terrainMode;
    private float _walkSpeed=1,_flySpeed=1,_viewPitch=.08f;
    private bool _rings;
    private Label _hud=null!,_message=null!;
    private PanelContainer _panel=null!,_hudPanel=null!;
    private bool _detailedHud;
    private string _renderer="",_fullHud="";
    private OptionButton _preset=null!,_features=null!,_bookmarks=null!;
    private LineEdit _seed=null!,_coordinates=null!,_regionInput=null!;
    private CheckButton _flyToggle=null!,_noclipToggle=null!,_gravityToggle=null!;
    private readonly List<WorldPoint> _saved=new();
    private string _notice="Preparing world...";
    private double _hudTime;

    public override void _Ready()
    {
        try
        {
            GetTree().AutoAcceptQuit=false;
            _renderer=RenderingServer.GetCurrentRenderingMethod()+" / "+RenderingServer.GetCurrentRenderingDriverName();
            GD.Print("PLAYABLE_RENDERER: "+_renderer);
            if(RenderingServer.GetCurrentRenderingMethod()!="forward_plus")throw new InvalidOperationException("Playable A4 validation requires desktop Forward+; check renderer overrides.");
            _profiles=MacroProfileCatalog.Load(ProjectSettings.GlobalizePath("res://Content/world_profiles.json"));
            _terrain=new();AddChild(_terrain);
            var skyMaterial=new ProceduralSkyMaterial{SkyTopColor=new Color("4e7599"),SkyHorizonColor=new Color("a5b6b8"),GroundBottomColor=new Color("39463e"),GroundHorizonColor=new Color("a5b6b8")};
            AddChild(new WorldEnvironment{Environment=new Godot.Environment{
                BackgroundMode=Godot.Environment.BGMode.Sky,Sky=new Sky{SkyMaterial=skyMaterial},
                AmbientLightSource=Godot.Environment.AmbientSource.Color,AmbientLightColor=new Color("b3c2ce"),AmbientLightEnergy=.55f,
                ReflectedLightSource=Godot.Environment.ReflectionSource.Sky,TonemapMode=Godot.Environment.ToneMapper.Filmic,
                FogEnabled=true,FogLightColor=new Color("a5b6b8"),FogDensity=.00016f}});
            AddChild(new DirectionalLight3D{RotationDegrees=new Vector3(-38,-35,0),LightEnergy=.95f,ShadowEnabled=true,DirectionalShadowMaxDistance=140});
            _player=new CharacterBody3D{CollisionLayer=2,CollisionMask=1,FloorSnapLength=.5f,FloorMaxAngle=Mathf.DegToRad(48),FloorStopOnSlope=true};
            _player.AddChild(new CollisionShape3D{Position=Vector3.Up*.9f,Shape=new CapsuleShape3D{Radius=.3f,Height=1.8f}});
            _player.AddChild(new MeshInstance3D{Position=Vector3.Up*.9f,Mesh=new CapsuleMesh{Radius=.3f,Height=1.8f},MaterialOverride=new StandardMaterial3D{AlbedoColor=new Color("aa663d"),Roughness=.8f}});
            AddChild(_player);_camera=new Camera3D{Current=true,Fov=72,Near=.08f,Far=7500};AddChild(_camera);
            _orbit.Look(0,-100,1,false);_orbit.Zoom(-4);
            BuildUi();
            string profile="mountainous";int seed=42;
            foreach(string arg in OS.GetCmdlineUserArgs())
            {if(arg.StartsWith("--preset="))profile=arg[9..];if(arg.StartsWith("--seed="))seed=int.Parse(arg[7..],CultureInfo.InvariantCulture);if(arg.StartsWith("--playable-smoke="))_smokeDirectory=arg[17..];}
            int selected=_profiles.ToList().FindIndex(p=>p.Id==profile);_preset.Select(selected<0?0:selected);_seed.Text=seed.ToString(CultureInfo.InvariantCulture);
            _panel.Visible=_smokeDirectory=="";Generate();
        }
        catch(Exception e){GD.PushError(e.ToString());Close(1);}
    }
    private void Generate()
    {
        if(_setup!=null){_notice="World setup is already running.";return;}
        if(!int.TryParse(_seed.Text,NumberStyles.Integer,CultureInfo.InvariantCulture,out int seed)){_notice="Seed must be a 32-bit integer.";return;}
        var profile=_profiles[_preset.Selected];
        string directory=ProjectSettings.GlobalizePath("user://playable-a4");Directory.CreateDirectory(directory);
        string path=Path.Combine(directory,$"{profile.Id}-{seed}.json");
        MacroWorldManifest manifest;
        if(File.Exists(path))manifest=MacroManifestStore.Load(path);
        else{manifest=MacroWorldManifest.Create(seed,profile,algorithm:4);MacroManifestStore.Create(path,manifest);}
        manifest.Validate();if(manifest.Algorithm!=4 || manifest.Seed!=seed || manifest.Profile.Id!=profile.Id)throw new InvalidDataException("Lab manifest identity mismatch.");
        _hold=true;_worldRevision++;CancelInspection();_setupCancel=new();var token=_setupCancel.Token;_notice="Preparing frozen A4 world and coarse metadata (one worker)...";
        _setup=Task.Run(()=>{var world=new MacroWorldGenerator(manifest);var regional=RegionalDrainageRefiner.Create(MacroDrainageRecipe.Create(manifest),cancellation:token);return(world,regional,PlayableWaterPresentation.Landmarks(world,regional));},token);
    }
    public override void _Process(double delta)
    {
        if(_closing)return;
        try
        {
            if(_setup is {IsCompleted:true})
            {
                var finished=_setup;_setup=null;_setupCancel?.Dispose();_setupCancel=null;var result=finished.GetAwaiter().GetResult();
                _world=result.World;_regional=result.Regional;_landmarks=result.Landmarks;_terrain.Configure(_world);
                _features.Clear();foreach(var point in _landmarks)_features.AddItem(point.Name);
                _saved.Clear();_bookmarks.Clear();_inspection=null;_inspectedTile=null;_windows=Array.Empty<LabRiverWindow>();_hydroSummary="Hydrology: not inspected. F5 for one selected tile.";ClearHydrology();
                Teleport(_landmarks[0].Position);_previous=_position;_notice="A4 world ready. Terrain streams outward; Tab captures mouse, F1 panel.";
            }
            if(_world==null)return;
            _terrain.Stream(_position,_origin);PollInspection();
            if(_hold && _setup==null && ((_fly && _noclip) || _terrain.CanTraverse(_position,_position)))_hold=false;
            _orbit.Step((float)delta);UpdateCamera();
            _hudTime+=delta;if(_hudTime>.3){_hudTime=0;RefreshHud();if((_streamOverlay || _rings) && (_overlayBuilds!=_terrain.Builds || _overlayTile!=PlayableWorldPolicy.TileAt(_position)))RebuildHydrology();}
            if(_smokeDirectory!="")SmokeStep(delta);
        }
        catch(Exception e){GD.PushError(e.ToString());_notice=e.Message;if(_smokeDirectory!="")Close(1);}
    }
    public override void _PhysicsProcess(double delta)
    {
        if(_world==null || _closing || _hold)return;
        float dt=(float)Math.Min(delta,.05);Vector2 input=Vector2.Zero;float vertical=0;
        if(Input.MouseMode==Input.MouseModeEnum.Captured && _smokeDirectory=="")
        {
            input=new((Input.IsPhysicalKeyPressed(Key.D)?1:0)-(Input.IsPhysicalKeyPressed(Key.A)?1:0),(Input.IsPhysicalKeyPressed(Key.S)?1:0)-(Input.IsPhysicalKeyPressed(Key.W)?1:0));
            vertical=(Input.IsPhysicalKeyPressed(Key.Space)?1:0)-(Input.IsPhysicalKeyPressed(Key.Ctrl)?1:0);
        }
        if(_smokeDirectory!=""){input=_smokeInput;vertical=_smokeVertical;}
        Move(input,vertical,_jump,dt);_jump=false;
    }
    private void Move(Vector2 input,float vertical,bool jump,float dt)
    {
        var direction=new Vector3(input.X,0,input.Y).LimitLength().Rotated(Vector3.Up,_orbit.Yaw);
        float speed=(_fly?20*_flySpeed:5*_walkSpeed);var velocity=direction*speed;
        velocity.Y=_fly?vertical*speed:(!_gravity?0:_player.IsOnFloor()?(jump?7:0):_player.Velocity.Y-20*dt);
        var proposed=new WorldPoint(_position.X+velocity.X*dt,_position.Y+velocity.Y*dt,_position.Z+velocity.Z*dt);
        if(!_noclip && !_terrain.CanTraverse(_position,proposed)){velocity=Vector3.Zero;_notice="Movement waiting for collision tiles. Fly + noclip can outrun streaming.";}
        var before=_player.Position;_player.Velocity=velocity;
        if(_fly && _noclip)_player.Position+=velocity*dt;else _player.MoveAndSlide();
        var displacement=_player.Position-before;
        // Add only the local physics displacement to authoritative doubles, never recast a continental position to float.
        var next=new WorldPoint(_position.X+displacement.X,_position.Y+displacement.Y,_position.Z+displacement.Z);
        try{next.Validate();}catch(ArgumentOutOfRangeException){_player.Position=before;_player.Velocity=Vector3.Zero;_notice="Supported coordinate limit reached.";return;}
        _position=next;
        if(PlayableWorldPolicy.NeedsRebase(_position,_origin))Rebase();
    }
    private void Rebase()
    {
        _origin=PlayableWorldPolicy.OriginFor(_position);_terrain.Rebase(_origin);_player.Position=PlayableWorldTerrain.ToGodot(_position.RelativeTo(_origin));_rebases++;RebuildHydrology();
    }
    private void Teleport(WorldPoint p)
    {
        p.Validate();_previous=_position;_position=p;_player.Velocity=Vector3.Zero;_hold=true;
        if(_world!=null && p.Y<_world.Sample(p.X,p.Z).Elevation-.1)
        {_flyToggle.ButtonPressed=true;_noclipToggle.ButtonPressed=true;_notice="Exact target is under terrain: fly/noclip enabled for safe inspection.";}
        Rebase();UpdateCamera();
    }
    private void UpdateCamera()
    {
        Vector3 focus=_player.Position+Vector3.Up*1.2f;
        float distance=_orbit.Distance*.35f;var offset=new Vector3(0,Mathf.Sin(_viewPitch)*distance,Mathf.Cos(_viewPitch)*distance).Rotated(Vector3.Up,_orbit.Yaw);
        Vector3 destination=focus+offset;
        if(!_noclip && IsInsideTree())
        {
            var query=PhysicsRayQueryParameters3D.Create(focus,destination,1);var hit=GetWorld3D().DirectSpaceState.IntersectRay(query);
            if(hit.Count>0)destination=hit["position"].AsVector3()-offset.Normalized()*.2f;
        }
        _camera.Position=destination;_camera.LookAt(focus);
    }
    public override void _UnhandledInput(InputEvent input)
    {
        if(input is InputEventMouseMotion motion && Input.MouseMode==Input.MouseModeEnum.Captured){_orbit.Look(motion.ScreenRelative.X,0,1,false);_viewPitch=Mathf.Clamp(_viewPitch+motion.ScreenRelative.Y*.0035f,-1.3f,1.45f);}
        if(input is InputEventMouseButton {Pressed:true} button && Input.MouseMode==Input.MouseModeEnum.Captured)
        {if(button.ButtonIndex==MouseButton.WheelUp)_orbit.Zoom(-1);if(button.ButtonIndex==MouseButton.WheelDown)_orbit.Zoom(1);}
        if(input is not InputEventKey {Pressed:true,Echo:false} key)return;
        switch(key.Keycode)
        {
            case Key.Escape:Input.MouseMode=Input.MouseModeEnum.Visible;_panel.Visible=true;break;
            case Key.Tab:Input.MouseMode=Input.MouseMode==Input.MouseModeEnum.Captured?Input.MouseModeEnum.Visible:Input.MouseModeEnum.Captured;_panel.Visible=Input.MouseMode!=Input.MouseModeEnum.Captured;break;
            case Key.F1:_panel.Visible=!_panel.Visible;Input.MouseMode=_panel.Visible?Input.MouseModeEnum.Visible:Input.MouseModeEnum.Captured;break;
            case Key.F2:_flyToggle.ButtonPressed=!_fly;break;
            case Key.F3:_noclipToggle.ButtonPressed=!_noclip;break;
            case Key.G:_gravityToggle.ButtonPressed=!_gravity;break;
            case Key.Space:_jump=true;break;
            case Key.F4:_boundaries=!_boundaries;ApplyOverlays();break;
            case Key.F5:RequestInspection();break;
            case Key.F6:SaveBookmark();break;
            case Key.F7:Teleport(_previous);break;
            case Key.F8:CopyReport();break;
            case Key.F9:_hudPanel.Visible=!_hudPanel.Visible;break;
            case Key.F10:_detailedHud=!_detailedHud;RefreshHud();break;
        }
    }
    public override void _Notification(int what)
    {if(what==NotificationWMCloseRequest)Close();if(what==NotificationApplicationFocusOut)Input.MouseMode=Input.MouseModeEnum.Visible;}
    private async void Close(int code=0)
    {
        if(_closing)return;_closing=true;_setupCancel?.Cancel();CancelInspection();Input.MouseMode=Input.MouseModeEnum.Visible;
        SetProcess(false);SetPhysicsProcess(false);SetProcessUnhandledInput(false);foreach(var child in GetChildren())child.QueueFree();
        await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);await ToSignal(GetTree(),SceneTree.SignalName.ProcessFrame);
        GC.Collect();GC.WaitForPendingFinalizers();GetTree().Quit(code);
    }
    public override void _ExitTree()
    {
        _setupCancel?.Cancel();CancelInspection();if(_hydroJob!=null)_=_hydroJob.ContinueWith(t=>{_=t.Exception;},TaskScheduler.Default);if(_setup!=null)_=_setup.ContinueWith(t=>{_=t.Exception;},TaskScheduler.Default);
    }
    private void ApplyOverlays(){_terrain.Overlay(_boundaries,_terrainMode,_waterStatus);RebuildHydrology();}
    private void RefreshHud()
    {
        if(_world==null)return;var tile=PlayableWorldPolicy.TileAt(_position);var region=WorldRegion.At(_position.X,_position.Z);
        _fullHud=$"THORNWAKE / PLAYABLE A4 LAB | {_world.Manifest.Profile.Name} / seed {_world.Manifest.Seed}\n"+
            $"Global {_position.X:F2}, {_position.Y:F2}, {_position.Z:F2} m | height {_world.Sample(_position.X,_position.Z).Elevation:F2}\n"+
            $"Region16 {region.X},{region.Z} | Render tile256 {tile.X},{tile.Z} | {(_fly?"FLY":"WALK")} {(_noclip?"NOCLIP":"COLLISION")} | {(_hold?"WAITING":"READY")}\n"+
            (_showOrigin?$"Local {_player.Position} | Origin {_origin.X:F0},{_origin.Y:F0},{_origin.Z:F0} | rebases {_rebases}\n":"")+
            $"Tiles {_terrain.Resident}/1849 | job {_terrain.Pending}/1 | queued {_terrain.Queued} | unload {_terrain.Unloaded} | stale {_terrain.Discarded}\n"+
            $"Worker {_terrain.WorkerMs:F1} ms | collider {_terrain.ColliderMs:F1} | commit {_terrain.CommitMs:F1} (max {_terrain.MaxCommitMs:F1}) | {_terrain.Triangles:N0} tris\n"+
            (_streamOverlay?$"Streaming: terrain tile {( _terrain.Has(tile)?"GREEN / resident":"AMBER / pending")} | 8 m nearby / coarse interior far; horizon ~5 km\n":"")+
            (_waterStatus?"CYAN: representative sampled sea | GOLD: provisional checked reach | MAGENTA: unresolved candidate\n":"")+
            $"{_hydroSummary}\nF1 help / Tab mouse | F2 fly | F3 noclip | G gravity | F5 inspect | F8 copy report";
        _hud.Text=_detailedHud?_fullHud:
            $"A4 · {_world.Manifest.Profile.Name} · seed {_world.Manifest.Seed} · {_renderer} · {Engine.GetFramesPerSecond()} FPS\n"+
            $"XYZ {_position.X:F1}, {_position.Y:F1}, {_position.Z:F1} m | {(_fly?"Fly":"Walk")} x{(_fly?_flySpeed:_walkSpeed):0.#} {(_noclip?"noclip":"collision")} | rebases {_rebases}\n"+
            $"Tiles {_terrain.Resident} / 1849 | near {_terrain.DetailedCount} | worker {_terrain.Pending} | queued {_terrain.Queued} | commit {_terrain.CommitMs:F1} ms\n"+
            "Tab explore / F1 panel / F2 fly / F3 noclip / F9 HUD / F10 details";
        _message.Text=Math.Abs(_position.Y-_world.Sample(_position.X,_position.Z).Elevation)>6500?"Beyond terrain rendering altitude: use fly/noclip to descend or teleport to X/Z.":_terrain.Failure!=""?"TERRAIN FAILURE: "+_terrain.Failure:_notice;
    }
    private string Report()=>"Renderer: "+_renderer+"\nExact global XYZ: "+FormattableString.Invariant($"{_position.X:R} {_position.Y:R} {_position.Z:R}")+"\n"+_fullHud+"\n"+_notice+"\nWorld manifest: "+System.Text.Json.JsonSerializer.Serialize(_world?.Manifest)+"\nHydrology: "+(_inspection?.LayerId??"not inspected")+"\n"+(_inspectedTile?.Lake?.LakeId??"no inspected candidate")+"\nFly speed="+_flySpeed+" walk="+_walkSpeed+" gravity="+_gravity;
    private void CopyReport(){RefreshHud();DisplayServer.ClipboardSet(Report());_notice="Reproduction report copied: frozen manifest, coordinates, versions, controls and metrics.";}
    private void SaveBookmark(){if(_saved.Count>=64){_notice="Session bookmark limit: 64.";return;}_saved.Add(_position);_bookmarks.AddItem($"{_saved.Count}: {_position.X:F0}, {_position.Z:F0}");_bookmarks.Select(_saved.Count-1);}
    private void BuildUi()
    {
        var layer=new CanvasLayer();AddChild(layer);
        var top=new PanelContainer{Position=new Vector2(12,12),MouseFilter=Control.MouseFilterEnum.Ignore};layer.AddChild(top);_hudPanel=top;
        var info=new VBoxContainer();top.AddChild(info);_hud=new Label{Text="Loading A4...",MouseFilter=Control.MouseFilterEnum.Ignore};_hud.AddThemeFontSizeOverride("font_size",14);info.AddChild(_hud);
        _message=new Label{Text=_notice,MouseFilter=Control.MouseFilterEnum.Ignore};_message.AddThemeFontSizeOverride("font_size",14);info.AddChild(_message);
        _panel=new PanelContainer{Position=new Vector2(930,12),Size=new Vector2(490,850)};layer.AddChild(_panel);
        var scroll=new ScrollContainer{CustomMinimumSize=new Vector2(480,820)};_panel.AddChild(scroll);var box=new VBoxContainer{CustomMinimumSize=new Vector2(455,0)};scroll.AddChild(box);
        void Label(string s)=>box.AddChild(new Label{Text=s});
        void Button(string s,Action action){var b=new Button{Text=s};b.Pressed+=()=>{try{action();}catch(Exception e){_notice=e.Message;}};box.AddChild(b);}
        CheckButton Toggle(string s,bool value,Action<bool> action){var b=new CheckButton{Text=s,ButtonPressed=value};b.Toggled+=v=>action(v);box.AddChild(b);return b;}
        Label("A4 WORLD VALIDATION / F1 or Tab to explore\nWASD strafe/move | mouse look | wheel zoom\nSpace jump; in flight Space up / Ctrl down\nEscape releases mouse; close window to exit");
        _preset=new OptionButton();foreach(var p in _profiles)_preset.AddItem(p.Name);box.AddChild(_preset);
        _seed=new LineEdit{Text="42",PlaceholderText="Seed"};box.AddChild(_seed);Button("Load frozen / generate A4 world",Generate);
        _flyToggle=Toggle("F2 Fly",false,v=>{_fly=v;if(!v){_noclipToggle.ButtonPressed=false;_player.Velocity=Vector3.Zero;RecoverGround();}});
        _noclipToggle=Toggle("F3 Noclip (requires fly)",false,v=>{_noclip=v;if(v)_flyToggle.ButtonPressed=true;else RecoverGround();});
        _gravityToggle=Toggle("G Gravity",true,v=>_gravity=v);
        void Speed(string title,float initial,Action<float> setter)
        {Label(title);var number=new SpinBox{MinValue=.1,MaxValue=500,Step=.1,Value=initial};number.ValueChanged+=v=>setter((float)v);box.AddChild(number);var row=new HBoxContainer();box.AddChild(row);foreach(int n in new[]{1,2,5,10,25,50}){var b=new Button{Text=n+"x"};b.Pressed+=()=>number.Value=n;row.AddChild(b);}}
        Speed("Walk multiplier (base 5 m/s)",1,v=>_walkSpeed=v);Speed("Fly multiplier (base 20 m/s)",1,v=>_flySpeed=v);
        _coordinates=new LineEdit{PlaceholderText="Global X Z  or  X Y Z (metres; decimal dot)"};box.AddChild(_coordinates);
        Button("Teleport to coordinates",()=>{if(_world==null)return;var n=Parse(_coordinates.Text);if(n.Length is not (2 or 3))throw new ArgumentException("Enter X Z or X Y Z.");Teleport(PlayableWorldPolicy.Teleport(_world,n[0],n[^1],n.Length==3?n[1]:null));});
        _regionInput=new LineEdit{PlaceholderText="WorldRegion X Z (16 m integer indices)"};box.AddChild(_regionInput);
        Button("Teleport to selected region center",()=>{if(_world==null)return;var n=Parse(_regionInput.Text);if(n.Length!=2 || n.Any(v=>v!=Math.Truncate(v)))throw new ArgumentException("Two integer region indices required.");var r=new WorldRegion(checked((long)n[0]),checked((long)n[1]));r.Validate();Teleport(PlayableWorldPolicy.Teleport(_world,r.X*16d+8,r.Z*16d+8));});
        _features=new OptionButton();box.AddChild(_features);Button("Teleport to feature / reference",()=>{if(_features.Selected>=0)Teleport(_landmarks[_features.Selected].Position);});
        Button("Mountain approach / face the range",MountainApproach);
        Button("F7 Previous position",()=>Teleport(_previous));Button("F6 Bookmark current position",SaveBookmark);_bookmarks=new OptionButton();box.AddChild(_bookmarks);Button("Go to session bookmark",()=>{if(_bookmarks.Selected>=0)Teleport(_saved[_bookmarks.Selected]);});
        Toggle("Show HUD (F9)",true,v=>_hudPanel.Visible=v);
        Toggle("Detailed HUD (F10)",false,v=>{_detailedHud=v;RefreshHud();});
        Toggle("F4 Tile boundaries",false,v=>{_boundaries=v;ApplyOverlays();});Toggle("Resident / pending overlay",false,v=>{_streamOverlay=v;RebuildHydrology();});
        Toggle("Near / outer terrain rings",false,v=>{_rings=v;RebuildHydrology();});
        Toggle("Origin / local-coordinate information",true,v=>_showOrigin=v);
        var mode=new OptionButton();foreach(string s in new[]{"Terrain presentation","Slope heat map","Elevation heat map"})mode.AddItem(s);mode.ItemSelected+=i=>{_terrainMode=(int)i;ApplyOverlays();};box.AddChild(mode);
        Toggle("Corridor proposals (not physical flow)",true,v=>{_corridors=v;RebuildHydrology();});Toggle("Unresolved basin outlines",true,v=>{_basins=v;RebuildHydrology();});Toggle("Water status colours / legend",true,v=>{_waterStatus=v;ApplyOverlays();});
        Button("F5 Inspect current coarse cell (one request)",RequestInspection);Button("Go to inspected corridor",()=>{var p=_inspection?.Corridors.FirstOrDefault()?.Points.FirstOrDefault();if(p.HasValue && _world!=null)Teleport(PlayableWorldPolicy.Teleport(_world,p.Value.X,p.Value.Z));});
        Button("F8 Copy full reproduction report",CopyReport);
        Label("SCULPT TOOLS — deferred authority integration\nRaise / lower / dig / fill / smooth / flatten\nNo compatible streamed A4 edit authority yet.");
        var disabled=new Button{Text="Terrain tool provider: unavailable",Disabled=true};box.AddChild(disabled);
        Button("Open existing cave / connected-water lab",()=>{Input.MouseMode=Input.MouseModeEnum.Visible;GetTree().ChangeSceneToFile("res://Game/Development/CaveTerrainLab.tscn");});
        Label("Cave lab is an authored isolated experiment.\nTrees/grass here are visual scale placeholders.\nNo harvesting, swimming, fluid state or edits.");
    }
    private void Face(double x,double z)
    {
        float yaw=Mathf.Atan2((float)(_position.X-x),(float)(_position.Z-z));
        _orbit.Look((_orbit.TargetYaw-yaw)/.0035f,0,1,false);
    }
    private void MountainApproach()
    {
        if(_world==null)return;
        var peak=_landmarks.First(l=>l.Name.StartsWith("High mountain")).Position;
        Teleport(PlayableWorldPolicy.Teleport(_world,peak.X+2500,peak.Z+2500));Face(peak.X,peak.Z);_viewPitch=.04f;
    }
    private static double[] Parse(string text)=>text.Split(new[]{' ',','},StringSplitOptions.RemoveEmptyEntries).Select(s=>double.Parse(s,CultureInfo.InvariantCulture)).ToArray();
    private void RecoverGround()
    {
        if(_world==null)return;double height=_world.Sample(_position.X,_position.Z).Elevation;
        if(_position.Y<height+.1)Teleport(_position with{Y=height+1});
        _hold=!_terrain.CanTraverse(_position,_position);
    }
}
