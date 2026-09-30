using System;
using Godot;
using Thornwake.Core;
using Thornwake.World;

namespace Thornwake.Development;

/// <summary>Finite streaming laboratory. Never loads or writes the player's save.</summary>
public partial class WorldStreamingLab : Node3D
{
    private Valley _terrain = null!;
    private Camera3D _camera = null!;
    private Label _status = null!;
    private Vector2 _focus = new(-24, 16);
    private int _frames;
    private bool _closing;
    private string _capture = "";

    public override void _Ready()
    {
        GetTree().AutoAcceptQuit = false;
        var state = LandscapeGenerator.Create(7319, new GenerationSettings(), new SandboxSettings());
        // Same saved-format edits as gameplay, deliberately crossing a presentation boundary.
        state.TerrainEdits.Add(new TerrainEdit { X = -32, Z = 16, Radius = 3, Delta = -.5f });
        state.Earthworks.Steps[TerrainGrid.Key(-32, 16)] = -35;
        _terrain = new Valley(); AddChild(_terrain); _terrain.InitializeTerrainPreview(state);
        AddChild(new WorldEnvironment { Environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = new Color("18282d"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color, AmbientLightEnergy = .65f } });
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-55, -25, 0), LightEnergy = .8f });
        _camera = new Camera3D { Current = true, Far = 250 }; AddChild(_camera);
        var layer = new CanvasLayer(); AddChild(layer);
        var panel = new PanelContainer { Position = new Vector2(16, 16) }; layer.AddChild(panel);
        var box = new VBoxContainer(); panel.AddChild(box);
        box.AddChild(new Label { Text = "  WORLD STREAMING LAB — legacy terrain adapter  " });
        box.AddChild(new Label { Text = "  WASD / arrows: pan · F9: overlays · Escape: exit  " });
        var buttons = new HBoxContainer(); box.AddChild(buttons);
        void Button(string title, Vector2 target)
        {
            var button = new Godot.Button { Text = title }; button.Pressed += () => _focus = target;
            buttons.AddChild(button);
        }
        Button("Edited west", new(-32, 16)); Button("East corner", new(48, -48)); Button("Center", Vector2.Zero);
        _status = new Label(); box.AddChild(_status);
        foreach (string arg in OS.GetCmdlineUserArgs()) if (arg.StartsWith("--capture=")) _capture = arg[10..];
        GD.Print("STREAMING_LAB_READY: isolated 128m presentation, 2 mesh builds per frame, no player save access");
    }

    public override void _Process(double delta)
    {
        float speed = (float)Math.Min(delta, .05) * 24;
        _focus.X += speed * ((Input.IsPhysicalKeyPressed(Key.D) || Input.IsPhysicalKeyPressed(Key.Right) ? 1 : 0) -
                            (Input.IsPhysicalKeyPressed(Key.A) || Input.IsPhysicalKeyPressed(Key.Left) ? 1 : 0));
        _focus.Y += speed * ((Input.IsPhysicalKeyPressed(Key.S) || Input.IsPhysicalKeyPressed(Key.Down) ? 1 : 0) -
                            (Input.IsPhysicalKeyPressed(Key.W) || Input.IsPhysicalKeyPressed(Key.Up) ? 1 : 0));
        _focus = _focus.Clamp(new Vector2(-55, -55), new Vector2(55, 55));
        var observer = WorldRegion.At(_focus.X, _focus.Y);
        var plan = _terrain.StreamTerrain(new[] { observer });
        _camera.Position = new Vector3(_focus.X, 65, _focus.Y + 45);
        _camera.LookAt(new Vector3(_focus.X, 0, _focus.Y));
        _status.Text = $"  Region ({observer.X}, {observer.Z}) · resident {_terrain.TerrainRegions.Count} / 49 max\n" +
            $"  Desired {plan.DesiredCount} · built {plan.Load.Count} · unloaded {plan.Unload.Count}\n" +
            "  Existing generator 2 + earthwork edits. Caves/continents are future milestones.  ";
        if (++_frames == 60 && _capture != "") Capture();
    }

    private async void Capture()
    {
        await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        using var image = GetViewport().GetTexture().GetImage();
        var error = image.SavePng(_capture);
        if (error != Error.Ok) GD.PushError("Streaming lab capture failed: " + error);
        else GD.Print("STREAMING_LAB_CAPTURED " + _capture);
        Close(error == Error.Ok ? 0 : 1);
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMCloseRequest) Close();
    }

    private async void Close(int code = 0)
    {
        if (_closing) return;
        _closing = true;
        SetProcess(false); SetProcessUnhandledKeyInput(false);
        foreach (var child in GetChildren()) child.QueueFree();
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        _terrain = null!; _camera = null!; _status = null!;
        // Match the game's shutdown discipline: finalize managed resources while Godot is alive.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect(); GC.WaitForPendingFinalizers();
        GetTree().Quit(code);
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (key.Keycode == Key.F9) _terrain.CycleTerrainDebug();
        if (key.Keycode == Key.Escape) Close();
    }
}
