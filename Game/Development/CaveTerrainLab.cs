using System;
using System.IO;
using System.Linq;
using Godot;
using Thornwake.Core;
using NVector = System.Numerics.Vector3;

namespace Thornwake.Development;

public partial class CaveTerrainLab : Node3D
{
    private CaveSpikeState _state = new();
    private CaveSpikeField _field = null!;
    private CaveLabTerrain _terrain = null!;
    private CharacterBody3D _player = null!;
    private Camera3D _camera = null!;
    private Label _status = null!, _message = null!;
    private Node3D _fixtures = null!, _water = null!;
    private bool _waterDirty;
    private string _savePath = "", _capture = "", _report = "";
    private bool _closing, _smoke, _autoCapture;
    private int _readyFrames;
    private float _pitch;

    public override void _Ready()
    {
        try
        {
            GetTree().AutoAcceptQuit = false;
            _savePath = ProjectSettings.GlobalizePath("user://master-cave-spike.json");
            foreach (string arg in OS.GetCmdlineUserArgs())
            {
                if (arg == "--cave-flow-smoke") { _smoke=true; _flowSmoke=true; }
                if (arg == "--cave-shore-smoke") { _smoke = true; _shoreSmoke = true; }
                if (arg == "--cave-floor-smoke") { _smoke = true; _floorSmoke = true; }
                if (arg == "--cave-smoke") _smoke = true;
                if (arg == "--cave-feedback-smoke") { _smoke = true; _feedbackSmoke = true; }
                if (arg.StartsWith("--capture=")) _capture = arg[10..];
                if (arg.StartsWith("--lab-save=")) _savePath = arg[11..];
                if (arg.StartsWith("--report=")) _report = arg[9..];
            }
            _autoCapture = !_smoke && _capture != "";
            _field = new(_state);
            _terrain = new CaveLabTerrain(); AddChild(_terrain); _terrain.Configure(_field, 1, false);
            AddChild(new WorldEnvironment { Environment = new Godot.Environment {
                BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("9eaeb2"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightColor = new Color("acb9c0"), AmbientLightEnergy = .5f,
                FogEnabled = true, FogLightColor = new Color("9eaeb2"), FogDensity = .003f } });
            AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-48, -25, 0), LightEnergy = .85f, ShadowEnabled = true });
            _player = new CharacterBody3D { CollisionLayer = 2, CollisionMask = 1, FloorSnapLength = .4f, FloorMaxAngle = Mathf.DegToRad(50) };
            _player.AddChild(new CollisionShape3D { Position = new Vector3(0, .9f, 0), Shape = new CapsuleShape3D { Radius = .3f, Height = 1.8f } });
            AddChild(_player);
            _camera = new Camera3D { Position = new Vector3(0, 1.6f, 0), Current = true, Fov = 80, Near = .06f, Far = 180 }; _player.AddChild(_camera);
            _player.AddChild(new OmniLight3D { Position = new Vector3(0, 1.8f, 0), OmniRange = 13, LightEnergy = 1.3f, LightColor = new Color("ffe1aa"), ShadowEnabled = true });
            BuildUi(); BuildFixtures(); Teleport(new Vector3(0, .5f, 24));
            if (!_smoke && !_autoCapture) Input.MouseMode = Input.MouseModeEnum.Captured;
            GD.Print("CAVE_LAB_READY: experimental hybrid terrain, real collision, isolated save " + _savePath);
        }
        catch (Exception error) { GD.PushError(error.ToString()); Close(1); }
    }
    private void BuildUi()
    {
        var layer = new CanvasLayer(); AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(16, 16), MouseFilter = Control.MouseFilterEnum.Stop }; layer.AddChild(panel);
        var box = new VBoxContainer(); panel.AddChild(box);
        box.AddChild(new Label { Text = "  THORNWAKE · CAVE TERRAIN TEST  " });
        box.AddChild(new Label { Text = "  WASD move · Mouse look · Space jump · Left click excavate · Tab cursor  " });
        box.AddChild(new Label { Text = "  F4 water pause · F5 save · F9 load · F6 resolution · F7 rebuild · F8 hybrid/volume · F10 regions  " });
        var row = new HBoxContainer(); box.AddChild(row);
        void Button(string title, Action action) { var b = new Godot.Button { Text = title }; b.Pressed += action; row.AddChild(b); }
        Button("Entrance", () => Teleport(new(0, .5f, 24)));
        Button("Cave", () => Teleport(new(0, 0, -4)));
        Button("Lake", () => Teleport(new(5, _field.Surface(5, -9) + .2f, -9)));
        Button("Dry chamber", () => Teleport(new(9, -7, -9)));
        Button("Dig outlet", () => OpenLakeOutlet()); Button("Outlet view", ViewOutlet);
        Button("Save", Save); Button("Load", Load);
        _status = new Label(); box.AddChild(_status);
        _message = new Label { Text = "  Follow the amber lights into the hill. This test does not touch your normal world.  " }; box.AddChild(_message);
        var crosshair = new Label { Text = "+", MouseFilter = Control.MouseFilterEnum.Ignore };
        crosshair.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.Center);
        crosshair.OffsetLeft = -6; crosshair.OffsetRight = 6; crosshair.OffsetTop = -14; crosshair.OffsetBottom = 14;
        crosshair.HorizontalAlignment = HorizontalAlignment.Center; layer.AddChild(crosshair);
        // UI is intentionally a laboratory: no item rewards, tool progression or swimming; connected water is a bounded prototype.
    }
    private void BuildFixtures()
    {
        if (_fixtures != null) { RemoveChild(_fixtures); _fixtures.QueueFree(); }
        _fixtures = new Node3D(); AddChild(_fixtures);
        foreach (var location in new[] { new Vector3(-2.5f, 2, 14), new Vector3(-2.5f, 2, 5), new Vector3(-2.5f, 2, -5), new Vector3(9, -5, -9) })
        {
            var lamp = new OmniLight3D { Position = location, OmniRange = 10, LightEnergy = .8f, LightColor = new Color("ffc788") };
            _fixtures.AddChild(lamp);
            _fixtures.AddChild(new MeshInstance3D { Position = location, Mesh = new SphereMesh { Radius = .09f, Height = .18f },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("ffb66a"), EmissionEnabled = true, Emission = new Color("ffa451") } });
        }
        float support = _field.Surface(-8, -8);
        var slab = new StaticBody3D { Position = new Vector3(-8, support + .15f, -8), CollisionLayer = 1, CollisionMask = 0 };
        slab.AddChild(new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(2, .4f, 2) }, MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color("a39470") } });
        slab.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = new Vector3(2, .4f, 2) } }); _fixtures.AddChild(slab);
        BuildWater();
    }
    public override void _Process(double delta)
    {
        if (_closing || _terrain == null) return;
        try
        {
            _terrain.Stream(_player.Position);
            if (_waterDirty && _terrain.IsCollisionReady) BuildWater();
            UpdateWater(delta);
            _status.Text = $"  {(_terrain.FullVolume ? "Full volume" : "Hybrid")} · {_terrain.Spacing:0.0} m samples · {_terrain.ResidentCount} regions · {_state.Cuts.Count}/128 cuts\n" +
                $"  Worker {_terrain.LastMeshMilliseconds:0.0} ms · main thread {_terrain.LastCommitMilliseconds:0.0} ms · {(_terrain.IsCollisionReady ? (WaterReady ? (_waterRunning ? "Terrain ready · Water running" : "Terrain ready · Water paused") : "Updating water…") : "Building in background…")}  ";
            if (_terrain.IsCollisionReady && WaterReady) _readyFrames++; else _readyFrames = 0;
            if (_feedbackSmoke) FeedbackStep();
            if (_floorSmoke) FloorFeedbackStep();
            if (_shoreSmoke) ShoreFeedbackStep();
            if (_flowSmoke) FlowSmokeStep();
            if (_autoCapture && _readyFrames == 20) CaptureAndClose();
        }
        catch (Exception error) { GD.PushError(error.ToString()); Close(1); }
    }
    public override void _PhysicsProcess(double delta)
    {
        if (_closing || _player == null) return;
        if (_floorSmoke || _flowSmoke || _shoreSmoke) return;
        if (_feedbackSmoke) { FeedbackPhysics((float)delta); return; }
        if (_smoke && !_terrain.IsCollisionReady) return;
        if (!_smoke && !_terrain.CanWalkAt(_player.Position)) return;
        try
        {
            if (_smoke) { SmokeStep((float)delta); return; }
            var input = Input.MouseMode == Input.MouseModeEnum.Captured ? new Vector2(
                (Input.IsPhysicalKeyPressed(Key.D) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.A) ? 1 : 0),
                (Input.IsPhysicalKeyPressed(Key.S) ? 1 : 0) - (Input.IsPhysicalKeyPressed(Key.W) ? 1 : 0)) : Vector2.Zero;
            Move(input, (float)delta, Input.IsPhysicalKeyPressed(Key.Space));
            if (_player.Position.Y < -11 || Math.Abs(_player.Position.X) > 30 || Math.Abs(_player.Position.Z) > 30) Teleport(new(0, .5f, 24));
        }
        catch (Exception error) { GD.PushError("CAVE_LAB_FAIL: " + error); Close(2); }
    }
    private void Move(Vector2 input, float delta, bool jump = false)
    {
        input = input.LimitLength();
        Vector3 direction = new Vector3(input.X, 0, input.Y).Rotated(Vector3.Up, _player.Rotation.Y);
        var velocity = _player.Velocity; velocity.X = direction.X * 4; velocity.Z = direction.Z * 4;
        velocity.Y = _player.IsOnFloor() ? (jump ? 7 : 0) : velocity.Y - 18 * delta;
        if (!_terrain.CanWalkAt(_player.Position + velocity * delta)) { velocity.X = 0; velocity.Z = 0; }
        _player.Velocity = velocity; _player.MoveAndSlide();
    }
    private void Teleport(Vector3 position)
    {
        _player.Position = position; _player.Velocity = Vector3.Zero; _player.Rotation = Vector3.Zero;
        _pitch = 0; _camera.Rotation = Vector3.Zero; _readyFrames = 0;
        _terrain.Stream(position);
    }
    private Godot.Collections.Dictionary Ray(Vector3 from, Vector3 to) => GetWorld3D().DirectSpaceState.IntersectRay(
        PhysicsRayQueryParameters3D.Create(from, to, 1));
    private void Excavate()
    {
        var hit = Ray(_camera.GlobalPosition, _camera.GlobalPosition - _camera.GlobalBasis.Z * 5);
        if (hit.Count == 0) { Tell("Aim at rock within five metres."); return; }
        var target = hit["position"].AsVector3() - hit["normal"].AsVector3() * .65f;
        var cut = new CaveCut(target.X, target.Y, target.Z, 1.5f);
        if (!_field.TryCut(cut)) { Tell("Outside the editable cave patch, protected support, or cut limit reached."); return; }
        _terrain.Invalidate(cut);
        _waterDirty = true;
        Tell("Excavated solid terrain. F5 saves these test edits.");
    }
    private void Save()
    {
        try { CaveSpikeStore.Save(_savePath, _state); Tell("Saved cave terrain and water progression separately from your normal world."); }
        catch (Exception error) { Tell("Save failed: " + error.Message); }
    }
    private void Load()
    {
        try
        {
            var loaded = CaveSpikeStore.Load(_savePath); loaded.Version = 2; var field = new CaveSpikeField(loaded);
            _state = loaded; _field = field; _terrain.Configure(_field, _terrain.Spacing, _terrain.FullVolume); BuildFixtures();
            Teleport(new(0, .5f, 24)); Tell("Loaded cave edits with the smoother brush. Original save is retained until F5. Returned to the entrance.");
        }
        catch (Exception error) { Tell("Load failed; current test retained: " + error.Message); }
    }
    private void Tell(string text) => _message.Text = "  " + text + "  ";
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_closing || _smoke) return;
        if (@event is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _player.RotateY(-motion.Relative.X * .0025f); _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * .0025f, -1.4f, 1.4f);
            _camera.Rotation = new Vector3(_pitch, 0, 0);
        }
        if (@event is InputEventMouseButton { Pressed: true, ButtonIndex: MouseButton.Left } && Input.MouseMode == Input.MouseModeEnum.Captured && _terrain.IsCollisionReady && WaterReady) Excavate();
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.Tab: Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured; break;
            case Key.Escape: Close(); break;
            case Key.F4: _waterRunning = !_waterRunning; Tell(_waterRunning ? "Connected water simulation resumed." : "Water paused for inspection."); break;
            case Key.F5: Save(); break;
            case Key.F9: Load(); break;
            case Key.F6: _terrain.Configure(_field, _terrain.Spacing == 1 ? .5f : 1, _terrain.FullVolume); _waterDirty = true; break;
            case Key.F7: _terrain.UnloadAll(); Tell("Unloaded meshes and collision. Rebuilding from the same saved-format data."); break;
            case Key.F8: _terrain.Configure(_field, _terrain.Spacing, !_terrain.FullVolume); _waterDirty = true; break;
            case Key.F10: _terrain.ShowRegions = !_terrain.ShowRegions; _terrain.RefreshColors(); break;
        }
    }
    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) Close();
        if (what == NotificationApplicationFocusOut) Input.MouseMode = Input.MouseModeEnum.Visible;
    }
    private async void CaptureAndClose()
    {
        _autoCapture = false;
        if (_capture != "")
        {
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            using var image = GetViewport().GetTexture().GetImage();
            if (image.SavePng(_capture) != Error.Ok) { GD.PushError("Cave capture failed"); Close(1); return; }
            GD.Print("CAVE_LAB_CAPTURED " + _capture);
        }
        Close();
    }
    private async void Close(int code = 0)
    {
        if (_closing) return; _closing = true;
        StopWater();
        SetProcess(false); SetPhysicsProcess(false); SetProcessUnhandledInput(false); Input.MouseMode = Input.MouseModeEnum.Visible;
        foreach (var child in GetChildren()) child.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame); await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _terrain = null!; _player = null!; _camera = null!; _fixtures = null!; _water = null!; _status = null!; _message = null!;
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); GC.WaitForPendingFinalizers();
        GetTree().Quit(code);
    }
}















