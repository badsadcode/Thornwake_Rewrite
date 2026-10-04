using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Thornwake.World;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Playable preview of the rewritten world: the deterministic generator (continents, eroded mountains, rivers,
/// lakes, walking-scale relief) streamed in 3D with LOD to the horizon, water, forests and a walking character.
/// Development scene: no saves, harvesting or building yet (those move here in milestone M9).
/// </summary>
public partial class WorldLab : Node3D
{
    private WorldStreamer _streamer = null!;
    private CharacterBody3D _player = null!;
    private Node3D _avatar = null!;
    private Node3D _pivot = null!;
    private SpringArm3D _arm = null!;
    private Camera3D _camera = null!;
    private DirectionalLight3D _sun = null!;
    private Label _hud = null!, _overlay = null!, _help = null!;
    private PanelContainer _menu = null!;
    private OptionButton _profileList = null!;
    private LineEdit _seedEdit = null!;
    private ItemList _landmarkList = null!;
    private CheckButton _flyToggle = null!, _noclipToggle = null!;
    private HSlider _timeSlider = null!;

    private IReadOnlyList<WorldProfile> _profiles = Array.Empty<WorldProfile>();
    private TerrainSampler? _sampler;
    private Task<TerrainSampler>? _generation;
    private List<Landmark> _landmarks = new();
    private double _originX, _originZ;
    private float _yaw = .6f, _pitch = -.32f, _zoom = 6.5f, _walkPhase;
    private bool _fly, _noclip, _holding = true, _showHud = true, _swimming;
    private double _hudTimer, _generationStarted;
    private string _status = "";

    // Headless screenshot mode: --world-smoke=<dir> [--preset=id] [--seed=n]
    private string? _smokeDirectory, _shotPath;
    private (double X, double Z)? _shotAt;
    private (float Yaw, float Pitch, float Zoom)? _shotView;
    private float _shotHeight;
    private int _smokeStage;
    private double _smokeTimer;

    public override void _Ready()
    {
        _profiles = WorldProfile.ParseCatalog(Godot.FileAccess.GetFileAsString("res://Content/world_profiles.json"));
        // The water shader reads the screen and depth textures; with MSAA those reads produce lines along the water
        // mesh's triangle edges (Tom's playtest) and wrong refraction. This scene uses FXAA instead (the valley game
        // keeps the project's MSAA setting).
        GetViewport().Msaa3D = Viewport.Msaa.Disabled;
        GetViewport().ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
        BuildEnvironment();
        _streamer = new WorldStreamer { Name = "Terrain" };
        AddChild(_streamer);
        BuildPlayer();
        BuildUi();
        string preset = "mountainous"; int seed = 42;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--preset=")) preset = arg[9..];
            if (arg.StartsWith("--seed=")) seed = int.Parse(arg[7..], CultureInfo.InvariantCulture);
            if (arg.StartsWith("--world-smoke=")) _smokeDirectory = arg[14..];
            // Single screenshot: --world-shot=<png> --at=x,z [--view=yaw,pitch,zoom] [--msaa=0|2|4] [--fly-height=m]
            if (arg.StartsWith("--world-shot=")) { _shotPath = arg[13..]; _smokeDirectory = Path.GetDirectoryName(_shotPath) ?? "."; }
            if (arg.StartsWith("--at=")) { var v = arg[5..].Split(','); _shotAt = (double.Parse(v[0], CultureInfo.InvariantCulture), double.Parse(v[1], CultureInfo.InvariantCulture)); }
            if (arg.StartsWith("--view=")) { var v = arg[7..].Split(','); _shotView = (float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture), float.Parse(v[2], CultureInfo.InvariantCulture)); }
            if (arg.StartsWith("--msaa=")) GetViewport().Msaa3D = arg[7..] switch { "0" => Viewport.Msaa.Disabled, "4" => Viewport.Msaa.Msaa4X, _ => Viewport.Msaa.Msaa2X };
            if (arg.StartsWith("--fly-height=")) _shotHeight = float.Parse(arg[13..], CultureInfo.InvariantCulture);
        }
        int index = Math.Max(0, _profiles.ToList().FindIndex(p => p.Id == preset));
        _profileList.Select(index);
        _seedEdit.Text = seed.ToString(CultureInfo.InvariantCulture);
        if (_smokeDirectory == null) Input.MouseMode = Input.MouseModeEnum.Captured;
        Generate();
    }

    // ------------------------------------------------------------------ world

    private void Generate()
    {
        if (_generation != null) return;
        if (!int.TryParse(_seedEdit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed)) { _status = "Seed must be a whole number."; return; }
        var profile = _profiles[Math.Max(0, _profileList.Selected)];
        _overlay.Text = $"Generating {profile.Name}, seed {seed}…\ncontinents → erosion → rivers and lakes";
        _overlay.Visible = true;
        _generationStarted = Time.GetTicksMsec();
        _generation = Task.Run(() => new TerrainSampler(new WorldPlan(WorldManifest.Create(seed, profile))));
    }

    private void FinishGeneration()
    {
        var task = _generation!;
        _generation = null;
        if (task.IsFaulted) { _overlay.Text = "World generation failed:\n" + task.Exception?.GetBaseException().Message; GD.PushError(task.Exception?.ToString()); return; }
        _sampler = task.Result;
        _landmarks = Landmarks.Find(_sampler);
        _landmarkList.Clear();
        foreach (var l in _landmarks) _landmarkList.AddItem(l.Name);
        var start = _landmarks.Count > 0 ? _landmarks[0] : Landmarks.Dry(_sampler, "Start", _sampler.Plan.Layout.Land[0].Center.X, _sampler.Plan.Layout.Land[0].Center.Z);
        _originX = Math.Floor(start.X / 64) * 64; _originZ = Math.Floor(start.Z / 64) * 64;
        _streamer.Configure(_sampler, _originX, _originZ);
        Teleport(start.X, start.Z);
        var plan = _sampler.Plan;
        _status = $"{plan.Profile.Name}, seed {plan.Manifest.Seed}: generated in {(Time.GetTicksMsec() - _generationStarted) / 1000:0.0} s " +
            $"({plan.Hydrology.Reaches.Count} river reaches, {plan.Hydrology.Lakes.Count} lakes).";
        _overlay.Text = "Loading terrain…";
    }

    private void Teleport(double x, double z)
    {
        if (_sampler == null) return;
        var t = _sampler.Sample(x, z);
        double y = Math.Max(t.Height, double.IsNaN(t.WaterLevel) ? t.Height : t.WaterLevel) + 1.0;
        Rebase(x, z, force: true);
        _player.Position = new Vector3((float)(x - _originX), (float)y, (float)(z - _originZ));
        _player.Velocity = Vector3.Zero;
        _holding = true; // wait for ground collision before gravity
    }

    private void Rebase(double x, double z, bool force = false)
    {
        double ox = Math.Floor(x / 64) * 64, oz = Math.Floor(z / 64) * 64;
        if (!force && Math.Abs(x - _originX) < 1536 && Math.Abs(z - _originZ) < 1536) return;
        var shift = new Vector3((float)(_originX - ox), 0, (float)(_originZ - oz));
        _originX = ox; _originZ = oz;
        _player.Position += shift;
        _streamer.Rebase(ox, oz);
    }

    private double PlayerX => _originX + _player.Position.X;
    private double PlayerZ => _originZ + _player.Position.Z;

    // ------------------------------------------------------------------ frame

    public override void _Process(double delta)
    {
        if (_generation is { IsCompleted: true }) FinishGeneration();
        if (_sampler == null) return;
        Rebase(PlayerX, PlayerZ);
        _streamer.Update(PlayerX, PlayerZ);
        if (_holding && _streamer.HasGround(PlayerX, PlayerZ))
        {
            // Snap to the generated ground once its collision exists.
            var t = _sampler.Sample(PlayerX, PlayerZ);
            _player.Position = new Vector3(_player.Position.X, (float)t.Height + .05f, _player.Position.Z);
            _holding = false;
        }
        if (_overlay.Visible && _generation == null && _streamer.Missing < 20) _overlay.Visible = false;
        UpdateCamera();
        UpdateSun();
        _hudTimer -= delta;
        if (_hudTimer <= 0) { _hudTimer = .2; UpdateHud(); }
        if (_smokeDirectory != null) Smoke(delta);
    }

    public override void _PhysicsProcess(double delta)
    {
        if (_sampler == null || _holding) return;
        float dt = (float)delta;
        var input = Vector2.Zero;
        bool typing = _menu.Visible;
        if (!typing)
        {
            if (Input.IsKeyPressed(Key.W)) input.Y -= 1;
            if (Input.IsKeyPressed(Key.S)) input.Y += 1;
            if (Input.IsKeyPressed(Key.A)) input.X -= 1;
            if (Input.IsKeyPressed(Key.D)) input.X += 1;
        }
        if (_smokeDirectory != null && _smokeStage == 2) input = new Vector2(0, -1);
        input = input.LimitLength(1);
        var forward = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
        var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
        var move = right * input.X - forward * input.Y;
        bool sprint = !typing && Input.IsKeyPressed(Key.Shift);
        var velocity = _player.Velocity;

        var water = _sampler.Sample(PlayerX, PlayerZ);
        double feet = _player.Position.Y;
        _swimming = !_fly && water.Water != WaterKind.None && water.WaterLevel - feet > 1.25;

        if (_fly)
        {
            float speed = sprint ? 220 : 45;
            if (Input.IsKeyPressed(Key.Ctrl)) speed *= 6;
            float vertical = (!typing && Input.IsKeyPressed(Key.Space) ? 1 : 0) - (!typing && (Input.IsKeyPressed(Key.C) || Input.IsKeyPressed(Key.Q)) ? 1 : 0);
            velocity = move * speed + Vector3.Up * vertical * speed * .6f;
        }
        else if (_swimming)
        {
            float speed = sprint ? 3.2f : 1.8f;
            velocity.X = move.X * speed; velocity.Z = move.Z * speed;
            // Buoyancy holds the head above the surface.
            float target = (float)(water.WaterLevel - 1.3);
            velocity.Y = Mathf.Clamp((target - _player.Position.Y) * 3f, -2f, 2f);
            if (!typing && Input.IsKeyPressed(Key.Space)) velocity.Y = 1.5f;
        }
        else
        {
            float speed = sprint ? 11 : 5;
            velocity.X = move.X * speed; velocity.Z = move.Z * speed;
            if (_player.IsOnFloor()) { if (!typing && Input.IsKeyPressed(Key.Space)) velocity.Y = 6.5f; }
            else velocity.Y -= 20 * dt;
        }
        _player.Velocity = velocity;
        _player.MoveAndSlide();
        // Never fall through the world if collision has not streamed in yet (e.g. after very fast flight).
        var ground = _sampler.Sample(PlayerX, PlayerZ).Height;
        if (!_noclip && _player.Position.Y < ground - .5) _player.Position = new Vector3(_player.Position.X, (float)ground + .05f, _player.Position.Z);

        // Character: face the direction of travel, swing limbs.
        var flat = new Vector3(_player.Velocity.X, 0, _player.Velocity.Z);
        if (flat.LengthSquared() > .05f)
        {
            float target = Mathf.Atan2(flat.X, flat.Z);
            _avatar.Rotation = new Vector3(0, Mathf.LerpAngle(_avatar.Rotation.Y, target, 1 - Mathf.Exp(-12 * dt)), 0);
            _walkPhase += flat.Length() * dt * 1.6f;
        }
        else _walkPhase = Mathf.Lerp(_walkPhase, Mathf.Round(_walkPhase / Mathf.Pi) * Mathf.Pi, 1 - Mathf.Exp(-10 * dt));
        float swing = Mathf.Sin(_walkPhase) * (_fly ? 0 : .55f);
        Limb("LeftLeg", swing); Limb("RightLeg", -swing); Limb("LeftArm", -swing * .8f); Limb("RightArm", swing * .8f);
    }

    private void Limb(string name, float angle)
    {
        if (_avatar.GetNodeOrNull<Node3D>(name) is { } limb) limb.Rotation = new Vector3(angle, limb.Rotation.Y, limb.Rotation.Z);
    }

    private void UpdateCamera()
    {
        _pivot.Position = _player.Position + Vector3.Up * 1.55f;
        _pivot.Rotation = new Vector3(_pitch, _yaw, 0);
        _arm.SpringLength = Mathf.Lerp(_arm.SpringLength, _zoom, .2f);
    }

    private void UpdateSun()
    {
        float hour = (float)_timeSlider.Value;
        float elevation = Mathf.Sin((hour - 6) / 12 * Mathf.Pi) * 62;
        _sun.RotationDegrees = new Vector3(-Mathf.Max(elevation, -5), -35 + (hour - 12) * 12, 0);
        _sun.LightEnergy = Mathf.Clamp(elevation / 25, 0.05f, 1.1f);
    }

    private void UpdateHud()
    {
        if (_sampler == null) { _hud.Text = _status; return; }
        var t = _sampler.Sample(PlayerX, PlayerZ);
        string where = t.Water switch
        {
            WaterKind.Ocean => "at sea",
            WaterKind.Lake => $"in lake {t.WaterBody} (surface {t.WaterLevel:0.0} m)",
            WaterKind.River => $"in a river (surface {t.WaterLevel:0.0} m)",
            _ => t.Mountain > .3 ? "mountains" : t.Height - _sampler.Plan.Profile.SeaLevel < 6 ? "coast" : "lowland",
        };
        if (_sampler.Plan.Hydrology.NearestRiver(PlayerX, PlayerZ, 400, out var river) && t.Water == WaterKind.None)
            where += $" · river {river.Distance:0} m away ({river.Width:0} m wide)";
        string mode = _fly ? (_noclip ? "Fly + noclip" : "Fly") : _swimming ? "Swimming" : "Walk";
        _hud.Text = $"{_status}\n" +
            $"Position {PlayerX:0} , {PlayerZ:0}   ground {t.Height:0.0} m   {where}\n" +
            $"{mode}   FPS {Engine.GetFramesPerSecond():0}   tiles {_streamer.Resident} (+{_streamer.Missing} queued)   " +
            $"terrain triangles {_streamer.Triangles / 1000}k   tile build {_streamer.LastBuildMs:0.0} ms, commit {_streamer.LastCommitMs:0.0} ms";
    }

    // ------------------------------------------------------------------ input

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * .0035f;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * .0035f, -1.45f, .9f);
        }
        if (e is InputEventMouseButton button && button.Pressed)
        {
            if (button.ButtonIndex == MouseButton.WheelUp) _zoom = Mathf.Max(1.8f, _zoom * .88f);
            if (button.ButtonIndex == MouseButton.WheelDown) _zoom = Mathf.Min(60f, _zoom * 1.14f);
            if (button.ButtonIndex == MouseButton.Left && !_menu.Visible) Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        switch (key.Keycode)
        {
            case Key.Escape: ToggleMenu(!_menu.Visible); break;
            case Key.Tab: Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured; break;
            case Key.F: if (!_menu.Visible) SetFly(!_fly); break;
            case Key.N: if (!_menu.Visible) SetNoclip(!_noclip); break;
            case Key.F1: _help.Visible = !_help.Visible; break;
            case Key.F2: _showHud = !_showHud; _hud.Visible = _showHud; break;
            case Key.F3: _gridShown = !_gridShown; _streamer.ShowGrid(_gridShown); break;
            case Key.F12: Screenshot(Path.Combine(OS.GetUserDataDir(), $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png")); break;
        }
    }

    private bool _gridShown;

    private void SetFly(bool fly) { _fly = fly; _flyToggle.SetPressedNoSignal(fly); if (!fly) SetNoclip(false); }
    private void SetNoclip(bool noclip) { _noclip = noclip; _noclipToggle.SetPressedNoSignal(noclip); _player.CollisionMask = noclip ? 0u : 1u; if (noclip) SetFly(true); }

    private void ToggleMenu(bool open)
    {
        _menu.Visible = open;
        Input.MouseMode = open ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    private void Screenshot(string path)
    {
        var image = GetViewport().GetTexture()?.GetImage();
        if (image == null) { GD.Print("SCREENSHOT_SKIPPED (no rendered image, e.g. --headless) " + path); return; }
        image.SavePng(path);
        _status = "Saved " + path;
        GD.Print("CAPTURED " + path);
    }

    // ------------------------------------------------------------------ scene setup

    private void BuildEnvironment()
    {
        var sky = new ProceduralSkyMaterial
        {
            SkyTopColor = new Color("4e7599"), SkyHorizonColor = new Color("aebfc2"),
            GroundBottomColor = new Color("39463e"), GroundHorizonColor = new Color("aebfc2"),
        };
        AddChild(new WorldEnvironment
        {
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Sky, Sky = new Sky { SkyMaterial = sky },
                AmbientLightSource = Godot.Environment.AmbientSource.Sky, AmbientLightEnergy = .5f,
                ReflectedLightSource = Godot.Environment.ReflectionSource.Sky,
                TonemapMode = Godot.Environment.ToneMapper.Filmic, TonemapExposure = .95f,
                FogEnabled = true, FogLightColor = new Color("aebfc2"), FogDensity = .00009f, FogAerialPerspective = .35f, FogSkyAffect = .25f,
                SsaoEnabled = true, SsaoRadius = 1.5f, SsaoIntensity = 1.2f,
            }
        });
        _sun = new DirectionalLight3D { LightEnergy = 1.05f, ShadowEnabled = true, DirectionalShadowMaxDistance = 180, LightColor = new Color("fff3df") };
        AddChild(_sun);
    }

    private void BuildPlayer()
    {
        _player = new CharacterBody3D { Name = "Player", CollisionLayer = 2, CollisionMask = 1, FloorSnapLength = .6f, FloorMaxAngle = Mathf.DegToRad(50) };
        _player.AddChild(new CollisionShape3D { Position = Vector3.Up * .9f, Shape = new CapsuleShape3D { Radius = .3f, Height = 1.8f } });
        _avatar = Visuals.Person(new Color("526b61"));
        _player.AddChild(_avatar);
        AddChild(_player);
        _pivot = new Node3D { Name = "CameraPivot" };
        _arm = new SpringArm3D { SpringLength = _zoom, CollisionMask = 1, Margin = .3f };
        _arm.AddExcludedObject(_player.GetRid());
        _camera = new Camera3D { Current = true, Fov = 70, Near = .1f, Far = 60000 };
        _arm.AddChild(_camera);
        _pivot.AddChild(_arm);
        AddChild(_pivot);
    }

    private void BuildUi()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        _hud = new Label { Position = new Vector2(14, 10), Modulate = new Color(1, 1, 1, .95f) };
        _hud.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .8f));
        _hud.AddThemeConstantOverride("shadow_offset_x", 1); _hud.AddThemeConstantOverride("shadow_offset_y", 1);
        layer.AddChild(_hud);
        _help = new Label
        {
            Text = "WASD move · Shift sprint · Space jump/swim up · mouse look (Tab frees cursor) · wheel zoom\n" +
                   "F fly (Space/C up/down, Shift faster, Ctrl ×6) · N noclip · Esc menu: teleport, new world, time of day\n" +
                   "F1 hide help · F2 hide HUD · F3 tile grid · F12 screenshot (saved to the user data folder)",
            AnchorTop = 1, AnchorBottom = 1, OffsetTop = -78, OffsetLeft = 14, OffsetRight = 900,
        };
        _help.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .8f));
        layer.AddChild(_help);
        _overlay = new Label
        {
            AnchorRight = 1, AnchorBottom = 1, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        _overlay.AddThemeFontSizeOverride("font_size", 26);
        _overlay.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .9f));
        layer.AddChild(_overlay);

        _menu = new PanelContainer { Visible = false, AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .5f, AnchorBottom = .5f,
            OffsetLeft = -260, OffsetRight = 260, OffsetTop = -280, OffsetBottom = 280 };
        layer.AddChild(_menu);
        var box = new VBoxContainer();
        _menu.AddChild(box);
        box.AddChild(new Label { Text = "Thornwake — rewrite world preview", HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(new Label { Text = "Teleport" });
        _landmarkList = new ItemList { CustomMinimumSize = new Vector2(0, 170) };
        _landmarkList.ItemActivated += i => { var l = _landmarks[(int)i]; Teleport(l.X, l.Z); ToggleMenu(false); };
        box.AddChild(_landmarkList);
        var go = new Button { Text = "Go to selected place" };
        go.Pressed += () => { var sel = _landmarkList.GetSelectedItems(); if (sel.Length > 0) { var l = _landmarks[sel[0]]; Teleport(l.X, l.Z); ToggleMenu(false); } };
        box.AddChild(go);
        var toggles = new HBoxContainer();
        _flyToggle = new CheckButton { Text = "Fly (F)" }; _flyToggle.Toggled += SetFly;
        _noclipToggle = new CheckButton { Text = "Noclip (N)" }; _noclipToggle.Toggled += SetNoclip;
        toggles.AddChild(_flyToggle); toggles.AddChild(_noclipToggle);
        box.AddChild(toggles);
        box.AddChild(new Label { Text = "Time of day" });
        _timeSlider = new HSlider { MinValue = 5, MaxValue = 20, Step = .1, Value = 10.5 };
        box.AddChild(_timeSlider);
        box.AddChild(new Label { Text = "New world (preset and seed)" });
        var row = new HBoxContainer();
        _profileList = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var p in _profiles) _profileList.AddItem(p.Name);
        _seedEdit = new LineEdit { CustomMinimumSize = new Vector2(110, 0), PlaceholderText = "seed" };
        row.AddChild(_profileList); row.AddChild(_seedEdit);
        box.AddChild(row);
        var generate = new Button { Text = "Generate this world" };
        generate.Pressed += () => { ToggleMenu(false); Generate(); };
        box.AddChild(generate);
        var random = new Button { Text = "Random seed" };
        random.Pressed += () => _seedEdit.Text = ((int)(GD.Randi() % 100000)).ToString(CultureInfo.InvariantCulture);
        box.AddChild(random);
        var back = new Button { Text = "Back to launcher" };
        back.Pressed += () => { Input.MouseMode = Input.MouseModeEnum.Visible; GetTree().ChangeSceneToFile("res://Game/Launcher.tscn"); };
        box.AddChild(back);
        var quit = new Button { Text = "Quit" };
        quit.Pressed += () => GetTree().Quit();
        box.AddChild(quit);
    }

    // ------------------------------------------------------------------ headless screenshot run

    private void Smoke(double delta)
    {
        _smokeTimer += delta;
        bool settled = _streamer.Settled && !_holding;
        if (_shotPath != null)
        {
            if (_smokeStage == 0)
            {
                if (_shotAt is { } at) Teleport(at.X, at.Z);
                if (_shotView is { } v) { _yaw = v.Yaw; _pitch = v.Pitch; _zoom = v.Zoom; _arm.SpringLength = v.Zoom; }
                _smokeStage = 1; _smokeTimer = 0;
            }
            else if (_smokeStage == 1 && settled)
            {
                if (_shotHeight > 0) { SetFly(true); _player.Position += Vector3.Up * _shotHeight; }
                _smokeStage = 2; _smokeTimer = 0;
            }
            else if (_smokeStage == 2 && settled && _smokeTimer > 1.5)
            {
                GD.Print($"SCATTER grass={_streamer.GrassCommitted} rocks={_streamer.RocksCommitted} props={_streamer.PropsCommitted} farTrees={_streamer.FarTreesCommitted}");
                Screenshot(_shotPath); GetTree().Quit(0);
            }
            return;
        }
        void Shot(string name) => Screenshot(Path.Combine(_smokeDirectory!, name));
        switch (_smokeStage)
        {
            case 0: if (settled && _smokeTimer > 2) { Shot("01-start.png"); Next(); } break;
            case 1: _yaw += 2.6f; _pitch = -.18f; _zoom = 9; Next(); break;
            case 2: if (_smokeTimer > 6) { Shot("02-walk.png"); Next(); } break;
            case 3: { var l = _landmarks.FirstOrDefault(m => m.Name.StartsWith("Shore")); if (l.Name != null) Teleport(l.X, l.Z); _yaw = 1.57f; _pitch = -.2f; _zoom = 10; Next(); break; }
            case 4: if (settled && _smokeTimer > 2) { Shot("03-lake.png"); Next(); } break;
            case 5: { var l = _landmarks.FirstOrDefault(m => m.Name.StartsWith("Foothills")); if (l.Name != null) Teleport(l.X, l.Z); _yaw = 1.57f; _pitch = -.05f; _zoom = 14; Next(); break; }
            case 6: if (settled && _smokeTimer > 2) { Shot("04-mountains.png"); Next(); } break;
            case 7: { var l = _landmarks.FirstOrDefault(m => m.Name.StartsWith("Mouth")); if (l.Name != null) Teleport(l.X, l.Z); _pitch = -.4f; _zoom = 30; Next(); break; }
            case 8: if (settled && _smokeTimer > 2) { Shot("05-river-mouth.png"); Next(); } break;
            case 9: SetFly(true); _player.Position += Vector3.Up * 400; _pitch = -.25f; _zoom = 12; Next(); break;
            case 10: if (settled && _smokeTimer > 2) { Shot("06-aerial.png"); Next(); } break;
            case 11: GD.Print($"WORLD_SMOKE_PASS tiles={_streamer.Resident} built={_streamer.Built} maxCommitMs={_streamer.MaxCommitMs:0.0}"); GetTree().Quit(0); break;
        }
        if (_smokeTimer > 900) { GD.PushError("WORLD_SMOKE_TIMEOUT at stage " + _smokeStage); GetTree().Quit(1); }
        void Next() { _smokeStage++; _smokeTimer = 0; }
    }
}
