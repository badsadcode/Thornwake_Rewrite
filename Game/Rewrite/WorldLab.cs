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
    private Atmosphere _atmosphere = null!;
    private LocalWaterView _water = null!;
    private Toolbox _tools = null!;
    private bool _rain;
    private readonly WorldGenOptions _gen = new();
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
    private float _shotHeight, _shotWait;
    private bool _shotRain;
    private readonly List<(double X, double Z, double Volume)> _shotPours = new();
    private readonly List<(double X0, double Z0, double X1, double Z1, double Depth)> _shotDigs = new();
    private int _smokeStage;
    private double _smokeTimer;

    public override void _Ready()
    {
        _profiles = WorldProfile.ParseCatalog(Godot.FileAccess.GetFileAsString("res://Content/world_profiles.json"));
        // The water shader reads the screen and depth textures; with MSAA those reads produce lines along the water
        // mesh's triangle edges (Tom's playtest) and wrong refraction. This scene uses FXAA or TAA instead (the valley
        // game keeps the project's MSAA setting); Atmosphere applies the anti-aliasing setting.
        GameSettings.Load();
        _atmosphere = new Atmosphere { Name = "Atmosphere" };
        AddChild(_atmosphere);
        _streamer = new WorldStreamer { Name = "Terrain" };
        AddChild(_streamer);
        _atmosphere.SetWater(_streamer.WaterMaterial);
        _water = new LocalWaterView { Name = "LocalWater" };
        AddChild(_water);
        _tools = new Toolbox { Name = "Tools" };
        AddChild(_tools);
        BuildPlayer();
        _atmosphere.AttachCamera(_camera);
        BuildUi();
        GameSettings.Changed += OnSettingChanged;
        string preset = "mountainous"; int seed = 42;
        foreach (string arg in OS.GetCmdlineUserArgs())
        {
            if (arg.StartsWith("--preset=")) preset = arg[9..];
            if (arg.StartsWith("--seed=")) seed = int.Parse(arg[7..], CultureInfo.InvariantCulture);
            if (arg.StartsWith("--world-smoke=")) _smokeDirectory = arg[14..];
            // Single screenshot: --world-shot=<png> --at=x,z [--view=yaw,pitch,zoom] [--msaa=0|2|4] [--fly-height=m] [--hour=h] [--menu=tab]
            if (arg.StartsWith("--world-shot=")) { _shotPath = arg[13..]; _smokeDirectory = Path.GetDirectoryName(_shotPath) ?? "."; }
            if (arg.StartsWith("--at=")) { var v = arg[5..].Split(','); _shotAt = (double.Parse(v[0], CultureInfo.InvariantCulture), double.Parse(v[1], CultureInfo.InvariantCulture)); }
            if (arg.StartsWith("--view=")) { var v = arg[7..].Split(','); _shotView = (float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture), float.Parse(v[2], CultureInfo.InvariantCulture)); }
            if (arg.StartsWith("--msaa=")) GetViewport().Msaa3D = arg[7..] switch { "0" => Viewport.Msaa.Disabled, "4" => Viewport.Msaa.Msaa4X, _ => Viewport.Msaa.Msaa2X };
            if (arg.StartsWith("--fly-height=")) _shotHeight = float.Parse(arg[13..], CultureInfo.InvariantCulture);
            // --set=<setting key>=<value>, e.g. --set=fx.water_reflections=0 (testing; also saved)
            if (arg.StartsWith("--set=")) { var kv = arg[6..].Split('='); var setting = GameSettings.All.FirstOrDefault(x => x.Key == kv[0]); if (setting != null) setting.Value = double.Parse(kv[1], CultureInfo.InvariantCulture); }
            if (arg.StartsWith("--debug-draw=")) GetViewport().DebugDraw = (Viewport.DebugDrawEnum)int.Parse(arg[13..], CultureInfo.InvariantCulture);
            // Screenshot of simulated water: --pour=x,z,m³ (repeatable) --rain --wait=s (simulated seconds before the shot)
            if (arg.StartsWith("--pour=")) { var v = arg[7..].Split(','); _shotPours.Add((double.Parse(v[0], CultureInfo.InvariantCulture), double.Parse(v[1], CultureInfo.InvariantCulture), double.Parse(v[2], CultureInfo.InvariantCulture))); }
            if (arg == "--rain") _shotRain = true;
            // --dig=x0,z0,x1,z1,depth: a straight trench (negative depth raises a bank) for screenshots and tests
            if (arg.StartsWith("--dig=")) { var v = arg[6..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray(); _shotDigs.Add((v[0], v[1], v[2], v[3], v[4])); }
            if (arg.StartsWith("--wait=")) _shotWait = float.Parse(arg[7..], CultureInfo.InvariantCulture);
            if (arg.StartsWith("--tool=")) _tools.Select(int.Parse(arg[7..], CultureInfo.InvariantCulture));
            if (arg == "--grid") { _gridShown = true; _streamer.ShowGrid(true); }
            if (arg.StartsWith("--hour=")) _timeSlider.Value = double.Parse(arg[7..], CultureInfo.InvariantCulture);
            if (arg.StartsWith("--menu=")) { _menu.Visible = true; _menu.GetChild<TabContainer>(0).CurrentTab = int.Parse(arg[7..], CultureInfo.InvariantCulture); }
        }
        int index = Math.Max(0, _profiles.ToList().FindIndex(p => p.Id == preset));
        _profileList.Select(index);
        _gen.LoadPreset(_profiles[index]);
        // --set=gen.<key>=<value> also sets world-generation parameters (after the preset has loaded its values).
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--set=gen."))
            {
                var kv = arg[6..].Split('=');
                if (_gen.All.FirstOrDefault(x => x.Key == kv[0]) is { } g) g.Value = double.Parse(kv[1], CultureInfo.InvariantCulture);
            }
        _seedEdit.Text = seed.ToString(CultureInfo.InvariantCulture);
        if (_smokeDirectory == null) Input.MouseMode = Input.MouseModeEnum.Captured;
        Generate();
    }

    // ------------------------------------------------------------------ world

    private void Generate()
    {
        if (_generation != null) return;
        if (!int.TryParse(_seedEdit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed)) { _status = "Seed must be a whole number."; return; }
        var preset = _profiles[Math.Max(0, _profileList.Selected)];
        WorldProfile profile; TerrainStyle style;
        try { (profile, style) = _gen.Build(preset); }
        catch (Exception error) { _status = "These world parameters are not valid: " + error.Message; return; }
        _custom = !_gen.IsPreset;
        _overlay.Text = $"Generating {profile.Name}{(_custom ? " (custom)" : "")}, seed {seed}…\ncontinents → erosion → rivers and lakes";
        _overlay.Visible = true;
        _generationStarted = Time.GetTicksMsec();
        _generation = Task.Run(() => new TerrainSampler(new WorldPlan(WorldManifest.Create(seed, profile), style: style)));
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
        _water.Configure(_sampler, _streamer.WaterMaterial, _originX, _originZ);
        _rain = false;
        Teleport(start.X, start.Z);
        var plan = _sampler.Plan;
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--dump-tile="))
            {
                // Debug: water mesh facts of the level-0 tile at x,z.
                var v = arg[12..].Split(',');
                double qx = double.Parse(v[0], CultureInfo.InvariantCulture), qz = double.Parse(v[1], CultureInfo.InvariantCulture);
                var key = new TileKey(0, (long)Math.Floor(qx / TileBuilder.BaseTile), (long)Math.Floor(qz / TileBuilder.BaseTile));
                var data = TileBuilder.Build(_sampler, key, DetailConfig.For(_sampler));
                double meanForest = 0; for (int k = 0; k < data.Side * data.Side; k++) meanForest += data.Colors[k].R;
                GD.Print($"TILE {key} water {(data.Water is { } w ? w.Indices.Length / 3 : -1)} triangles, trees {data.Trees.Count}, mean forest {meanForest / (data.Side * data.Side):0.00}, cards {data.FarTrees.Count}");
                foreach (int level in new[] { 1, 2, 3 })
                {
                    var k2 = new TileKey(level, (long)Math.Floor(qx / (TileBuilder.BaseTile * (1 << level))), (long)Math.Floor(qz / (TileBuilder.BaseTile * (1 << level))));
                    var d2 = TileBuilder.Build(_sampler, k2, DetailConfig.For(_sampler));
                    double mf = 0; for (int k = 0; k < d2.Side * d2.Side; k++) mf += d2.Colors[k].R;
                    GD.Print($"TILE {k2} trees {d2.Trees.Count}, mean forest {mf / (d2.Side * d2.Side):0.00}, cards {d2.FarTrees.Count}");
                }
                if (data.Water is { } water)
                    for (int k = 0; k < water.Vertices.Length; k++)
                        if (water.Colors[k].R > 0 || water.Vertices[k].Y > data.Vertices[k].Y)
                            GD.Print($"  v{k} ({k % data.Side},{k / data.Side}) water {water.Vertices[k].Y:0.00} ground {data.Vertices[k].Y:0.00}");
            }
        _status = $"{plan.Profile.Name}{(_custom ? " (custom)" : "")}, seed {plan.Manifest.Seed}: generated in {(Time.GetTicksMsec() - _generationStarted) / 1000:0.0} s " +
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
        _water.Rebase(ox, oz);
    }

    private bool _custom;

    private void OnSettingChanged(Setting s)
    {
        if (s.Rebuild) _streamer.Refresh();
    }

    public override void _ExitTree()
    {
        GameSettings.Changed -= OnSettingChanged;
        // Static caches of Godot resources (tree, rock and prop meshes, materials) must be freed while the engine is
        // still alive; left to the .NET finalizers at process exit they intermittently crashed on quit (as the valley
        // game already handles in Main).
        RockMeshes.Release(); WoodlandAssets.Release(); Visuals.ReleaseMaterials();
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
        var cam = _camera.GlobalPosition;
        _atmosphere.Update(delta, (float)_timeSlider.Value, _originX + cam.X, _originZ + cam.Z, _originX, _originZ, cam);
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
        if (_smokeDirectory != null && _shotPath == null && _smokeStage == 2) input = new Vector2(0, -1);
        input = input.LimitLength(1);
        var forward = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
        var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
        var move = right * input.X - forward * input.Y;
        bool sprint = !typing && Input.IsKeyPressed(Key.Shift);
        var velocity = _player.Velocity;

        UpdateLocalWater(dt, typing);
        var water = _sampler.Sample(PlayerX, PlayerZ);
        double feet = _player.Position.Y;
        double level = water.Water != WaterKind.None ? water.WaterLevel : double.NaN;
        float simulated = _water.Sim?.SurfaceAt(PlayerX, PlayerZ) ?? float.NaN;
        if (!float.IsNaN(simulated) && (double.IsNaN(level) || simulated > level)) level = simulated;
        _swimming = !_fly && !double.IsNaN(level) && level - feet > 1.25;

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
            float target = (float)(level - 1.3);
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

    /// <summary>Local water: simulate, and the debug tools (G pour at the aimed point, J toggle a spring there).</summary>
    private double _simTime;

    private void UpdateLocalWater(float dt, bool typing)
    {
        _simTime += dt;
        var sim = _water.Sim;
        if (sim == null) return;
        sim.RainRate = _rain ? 6e-5f * GameSettings.RainStrength.F : 0;
        _atmosphere.Raining = _rain;
        _water.Step(dt, PlayerX, PlayerZ);
        if (!typing && Input.IsKeyPressed(Key.G) && Aim() is { } at) sim.Add(at.X, at.Z, 2.5 * dt);
        // Terrain tools (1–5): left mouse = primary, right = secondary, while the cursor is captured.
        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured && !typing;
        var aim = AimLocal();
        var result = _tools.Update(dt, _sampler!, aim?.Hit, aim?.Direction ?? Vector3.Down, _originX, _originZ, _player.Position,
            captured && Input.IsMouseButtonPressed(MouseButton.Left), captured && Input.IsMouseButtonPressed(MouseButton.Right));
        if (result is { } r)
        {
            _streamer.Invalidate(r.MinX, r.MinZ, r.MaxX, r.MaxZ);
            sim.ReloadGround(r.MinX, r.MinZ, r.MaxX, r.MaxZ);
        }
    }


    /// <summary>Scene-local point under the screen centre and the aiming direction, or null.</summary>
    private (Vector3 Hit, Vector3 Direction)? AimLocal()
    {
        var from = _camera.GlobalPosition;
        var direction = -_camera.GlobalTransform.Basis.Z;
        var query = PhysicsRayQueryParameters3D.Create(from, from + direction * 400, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        return hit.Count == 0 ? null : (hit["position"].AsVector3(), direction);
    }

    /// <summary>World point under the screen centre (terrain collision), or null.</summary>
    private (double X, double Z)? Aim()
    {
        var from = _camera.GlobalPosition;
        var to = from - _camera.GlobalTransform.Basis.Z * 250;
        var query = PhysicsRayQueryParameters3D.Create(from, to, 1);
        var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);
        if (hit.Count == 0) return null;
        var p = hit["position"].AsVector3();
        return (_originX + p.X, _originZ + p.Z);
    }

    private void ToggleSpring()
    {
        var sim = _water.Sim;
        if (sim == null || Aim() is not { } at) return;
        var near = sim.Sources.Find(s => Math.Abs(s.X - at.X) < 4 && Math.Abs(s.Z - at.Z) < 4);
        if (near != null) { sim.Sources.Remove(near); _status = "Spring removed."; }
        else { sim.Sources.Add(new Thornwake.Hydraulics.LocalWater.Source { X = at.X, Z = at.Z, Rate = .6f }); _status = "Spring placed (J on it again removes it)."; }
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
        _hud.Text += "\n" + _tools.Hud();
        if (_water.Sim is { Added: > 0 } sim)
            _hud.Text += $"\nWater simulation: {sim.Volume():0.0} m³ on the land, {sim.Absorbed:0.0} m³ joined rivers/lakes{(_rain ? ", raining" : "")}" +
                $"{(sim.Sources.Count > 0 ? $", {sim.Sources.Count} spring(s)" : "")}";
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
            case Key.J: if (!_menu.Visible) ToggleSpring(); break;
            case >= Key.Key1 and <= Key.Key5: if (!_menu.Visible && _tools.Select((int)(key.Keycode - Key.Key1))) _status = _tools.Status; break;
            case Key.T or Key.Y or Key.Bracketleft or Key.Bracketright or Key.Minus or Key.Equal: if (!_menu.Visible) _tools.DevKey(key.Keycode); break;
            case Key.H: if (!_menu.Visible) { _rain = !_rain; _status = _rain ? "Rain on (H stops it)." : "Rain stopped."; } break;
            case Key.K: if (!_menu.Visible) { _water.Clear(); _rain = false; _status = "Simulated water cleared."; } break;
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
        _camera = new Camera3D { Current = true, Fov = GameSettings.Fov.F, Near = .1f, Far = 90000 };
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
                   "Water: hold G pour where you look · J spring · H rain · K clear\n" +
                   "Tools: 1 hands · 2 shovel · 3 pickaxe · 4 hoe · 5 terraform (dev) — left/right mouse use\n" +
                   "F1 hide help · F2 hide HUD · F3 tile grid · F12 screenshot (saved to the user data folder)",
            AnchorTop = 1, AnchorBottom = 1, OffsetTop = -122, OffsetLeft = 14, OffsetRight = 900,
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
            OffsetLeft = -330, OffsetRight = 330, OffsetTop = -330, OffsetBottom = 330 };
        layer.AddChild(_menu);
        var tabs = new TabContainer();
        _menu.AddChild(tabs);

        // ---- World: teleport, movement, time of day, leave
        var box = new VBoxContainer { Name = "World" };
        tabs.AddChild(box);
        box.AddChild(new Label { Text = "Thornwake — rewrite world preview", HorizontalAlignment = HorizontalAlignment.Center });
        box.AddChild(new Label { Text = "Teleport" });
        _landmarkList = new ItemList { CustomMinimumSize = new Vector2(0, 220) };
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
        box.AddChild(new Control { SizeFlagsVertical = Control.SizeFlags.ExpandFill });
        var back = new Button { Text = "Back to launcher" };
        back.Pressed += () => { Input.MouseMode = Input.MouseModeEnum.Visible; GetTree().ChangeSceneToFile("res://Game/Launcher.tscn"); };
        box.AddChild(back);
        var quit = new Button { Text = "Quit" };
        quit.Pressed += () => GetTree().Quit();
        box.AddChild(quit);

        // ---- New world: preset, seed, generation parameters
        var gen = new VBoxContainer { Name = "New world" };
        tabs.AddChild(gen);
        var row = new HBoxContainer();
        _profileList = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var p in _profiles) _profileList.AddItem(p.Name);
        _profileList.ItemSelected += i => _gen.LoadPreset(_profiles[(int)i]);
        _seedEdit = new LineEdit { CustomMinimumSize = new Vector2(110, 0), PlaceholderText = "seed" };
        var random = new Button { Text = "Random seed" };
        random.Pressed += () => _seedEdit.Text = ((int)(GD.Randi() % 100000)).ToString(CultureInfo.InvariantCulture);
        row.AddChild(_profileList); row.AddChild(_seedEdit); row.AddChild(random);
        gen.AddChild(row);
        gen.AddChild(new Label { Text = "Preset values; change any to make a custom world. Hover a name for details.", Modulate = new Color(1, 1, 1, .7f) });
        gen.AddChild(SettingsUi.List(_gen.All, section => { foreach (var s in _gen.All) if (s.Section == section) s.Value = s.Default; }));
        var generateRow = new HBoxContainer();
        var generate = new Button { Text = "Generate this world", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        generate.Pressed += () => { ToggleMenu(false); Generate(); };
        var reset = new Button { Text = "Reset all to preset" };
        reset.Pressed += _gen.Reset;
        generateRow.AddChild(generate); generateRow.AddChild(reset);
        gen.AddChild(generateRow);

        // ---- Settings: detail, view distance, effects, sky, camera and performance
        var settings = new VBoxContainer { Name = "Settings" };
        tabs.AddChild(settings);
        settings.AddChild(new Label { Text = "Saved automatically. Detail and distance changes rebuild the terrain in the background.", Modulate = new Color(1, 1, 1, .7f) });
        settings.AddChild(SettingsUi.List(GameSettings.All, GameSettings.ResetSection));
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
                foreach (var (dx0, dz0, dx1, dz1, depth) in _shotDigs)
                {
                    double length = Math.Sqrt((dx1 - dx0) * (dx1 - dx0) + (dz1 - dz0) * (dz1 - dz0));
                    for (double t = 0; t <= length; t += 1.5)
                    {
                        double ex = dx0 + (dx1 - dx0) * t / length, ez = dz0 + (dz1 - dz0) * t / length;
                        var r = _sampler!.Edits.Apply(ex, ez, 1.6, -depth * .5);
                        _streamer.Invalidate(r.MinX - 1, r.MinZ - 1, r.MaxX + 1, r.MaxZ + 1);
                    }
                }
                if (_shotView is { } v) { _yaw = v.Yaw; _pitch = v.Pitch; _zoom = v.Zoom; _arm.SpringLength = v.Zoom; }
                _smokeStage = 1; _smokeTimer = 0;
            }
            else if (_smokeStage == 1 && settled)
            {
                if (_shotHeight > 0) { SetFly(true); _player.Position += Vector3.Up * _shotHeight; }
                foreach (var (x, z, v) in _shotPours) _water.Sim?.Sources.Add(new Thornwake.Hydraulics.LocalWater.Source { X = x, Z = z, Rate = (float)(v / Math.Max(1, _shotWait * .5)) });
                if (_shotRain) _rain = true;
                _smokeStage = 2; _smokeTimer = 0; _simTime = 0;
            }
            else if (_smokeStage == 2 && _shotPours.Count > 0 && _simTime > _shotWait * .5 && _water.Sim is { } sim && sim.Sources.Count > 0) sim.Sources.Clear();
            else if (_smokeStage == 2 && settled && _smokeTimer > 1.5 && _simTime >= _shotWait)
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
