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
    /// <summary>The local player's figure and torch (Character.cs; remote players have their own).</summary>
    private Character _me = null!;
    // First person (round 32, Tom: "zooming in to the maximum, could it go into first-person mode?"): one wheel step in
    // from the closest third-person zoom. The body still casts its shadow; the torch moves to the lower right of the view.
    private bool FirstPerson => _zoom < .5f;
    private Node3D _fpHold = null!;
    private Node3D _pivot = null!;
    private SpringArm3D _arm = null!;
    private Camera3D _camera = null!;
    private Atmosphere _atmosphere = null!;
    private readonly MistField _mist = new() { Name = "Mist" };
    private readonly WorldMap _map = new() { Name = "Map" };
    private LocalWaterView _water = null!;
    private Toolbox _tools = null!;
    private bool _rain;
    private readonly WorldGenOptions _gen = new();
    private Label _hud = null!, _overlay = null!, _help = null!;
    private PanelContainer _menu = null!;
    private Button _hostButton = null!;
    private OptionButton _profileList = null!;
    private LineEdit _seedEdit = null!;
    private ItemList _landmarkList = null!;
    private CheckButton _flyToggle = null!, _noclipToggle = null!;
    private HSlider _timeSlider = null!;
    private readonly GameClock _clock = new();
    private readonly Weather _weather = new();
    private bool _weatherProbe;
    private HorizonLand? _horizon;
    private float _shotRainHere = -1; // screenshots: force the local rain (0..1) under the simulated weather
    private double _shotStrike = -1; // screenshots: a held lightning strike this far ahead (m)
    private bool _strikePlaced;
    private Lightning? _lightning;
    private string? _thunderWavs;
    private WeatherField? _weatherField;
    private WeatherMap? _weatherMap;
    private WindMap? _windMap;
    private double _windTimer;
    private (double X, double Z, double Speed) _localWind = (1, 0, 5);
    private TerrainSampler? _weatherFieldFor;
    /// <summary>The world weather field (simulated weather model; null otherwise): the map and the sky read it.</summary>
    public WeatherField? WeatherField => GameSettings.WeatherModel.Index == 1 ? _weatherField : null;
    private double? _pendingHour;

    private IReadOnlyList<WorldProfile> _profiles = Array.Empty<WorldProfile>();
    private IReadOnlyList<WorldRecipe> _recipes = Array.Empty<WorldRecipe>();
    private Task<WorldPlan>? _preview;
    private string _previewKey = "", _pendingKey = "";
    private WorldPlan? _previewPlan;
    private TextureRect _previewImage = null!;
    private CheckButton _biomeView = null!;
    private bool _biomeTint;
    private Label _previewInfo = null!, _typeInfo = null!;
    private TerrainSampler? _sampler;
    private Task<TerrainSampler>? _generation;
    private List<Landmark> _landmarks = new();
    private double _originX, _originZ;
    private float _yaw = .6f, _pitch = -.32f, _zoom = 6.5f;
    private bool _fly, _noclip, _holding = true, _showHud = true, _swimming;
    private double _hudTimer, _generationStarted;
    private string _status = "";

    // Headless screenshot mode: --world-smoke=<dir> [--preset=id] [--seed=n]
    private string? _smokeDirectory, _shotPath;
    private (double X, double Z)? _shotAt;
    private (float Yaw, float Pitch, float Zoom)? _shotView;
    private bool _faceSun; // debug: turn the camera toward the sun before the screenshot
    // --extra-yaws=a,b,…: after the shot, more shots turned by these yaw offsets (<name>-1.png …), same world and tiles.
    private readonly List<float> _extraYaws = new();
    private int _extraShot;
    /// <summary>Screenshot aid: a row of every placeholder plant in front of the camera (--gallery).</summary>
    private bool _gallery, _openMap;
    private float _shotHeight, _shotWait;
    private bool _shotRain;
    private float _shotDive;
    private (float Ground, float Puddles)? _shotWet;
    private readonly List<(double X, double Z, double Volume)> _shotPours = new();
    private readonly List<(double X0, double Z0, double X1, double Z1, double Depth)> _shotDigs = new();
    private int _smokeStage;
    private double _smokeTimer;

    public override void _Ready()
    {
        _profiles = WorldProfile.ParseCatalog(Godot.FileAccess.GetFileAsString("res://Content/world_profiles.json"));
        LoadBiomes();
        _recipes = WorldRecipe.Catalog(_profiles, LoadCustomTypes());
        // The water shader reads the screen and depth textures; with MSAA those reads produce lines along the water
        // mesh's triangle edges (Tom's playtest) and wrong refraction. This scene uses FXAA or TAA instead (the valley
        // game keeps the project's MSAA setting); Atmosphere applies the anti-aliasing setting.
        GameSettings.Load();
        DisplaySettings.Apply(GetTree().Root);
        GetTree().Root.SizeChanged += DisplaySettings.WindowChanged;
        _atmosphere = new Atmosphere { Name = "Atmosphere" };
        AddChild(_atmosphere);
        AddChild(_mist);
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
            // --type-file=<json>: a player's own world type (from a debug dump), added to the list for this run.
            if (arg.StartsWith("--type-file="))
            {
                var type = WorldRecipe.FromJson(File.ReadAllText(arg[12..]));
                _recipes = _recipes.Where(r => r.Id != type.Id).Append(type).ToList();
                _profileList.Clear();
                foreach (var r in _recipes) _profileList.AddItem(r.Name);
            }
            if (arg.StartsWith("--seed=")) seed = int.Parse(arg[7..], CultureInfo.InvariantCulture);
            if (arg.StartsWith("--world-smoke=")) _smokeDirectory = arg[14..];
            // Single screenshot: --world-shot=<png> --at=x,z [--view=yaw,pitch,zoom] [--msaa=0|2|4] [--fly-height=m] [--hour=h] [--menu=tab]
            if (arg.StartsWith("--world-shot=")) { _shotPath = arg[13..]; _smokeDirectory = Path.GetDirectoryName(_shotPath) ?? "."; _atmosphere.StillShot = true; }
            if (arg.StartsWith("--at=")) { var v = arg[5..].Split(','); _shotAt = (double.Parse(v[0], CultureInfo.InvariantCulture), double.Parse(v[1], CultureInfo.InvariantCulture)); }
            if (arg.StartsWith("--extra-yaws=")) foreach (var y in arg[13..].Split(',')) _extraYaws.Add(float.Parse(y, CultureInfo.InvariantCulture));
            if (arg.StartsWith("--view=")) { var v = arg[7..].Split(','); _shotView = (float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture), float.Parse(v[2], CultureInfo.InvariantCulture)); }
            if (arg.StartsWith("--msaa=")) GetViewport().Msaa3D = arg[7..] switch { "0" => Viewport.Msaa.Disabled, "4" => Viewport.Msaa.Msaa4X, _ => Viewport.Msaa.Msaa2X };
            if (arg.StartsWith("--fly-height=")) _shotHeight = float.Parse(arg[13..], CultureInfo.InvariantCulture);
            // --set=<setting key>=<value>, e.g. --set=fx.water_reflections=0 (testing; also saved)
            if (arg.StartsWith("--set=")) { var kv = arg[6..].Split('='); var setting = GameSettings.All.FirstOrDefault(x => x.Key == kv[0]); if (setting != null) setting.Value = double.Parse(kv[1], CultureInfo.InvariantCulture); }
            if (arg.StartsWith("--debug-draw=")) GetViewport().DebugDraw = (Viewport.DebugDrawEnum)int.Parse(arg[13..], CultureInfo.InvariantCulture);
            // Screenshot of simulated water: --pour=x,z,m³ (repeatable) --rain --wait=s (simulated seconds before the shot)
            if (arg.StartsWith("--pour=")) { var v = arg[7..].Split(','); _shotPours.Add((double.Parse(v[0], CultureInfo.InvariantCulture), double.Parse(v[1], CultureInfo.InvariantCulture), double.Parse(v[2], CultureInfo.InvariantCulture))); }
            if (arg == "--rain") _shotRain = true;
            // --dive=m: the player (and camera) this many metres under the water surface
            if (arg.StartsWith("--dive=")) _shotDive = float.Parse(arg[7..], CultureInfo.InvariantCulture);
            // --wet=ground,puddles: cosmetic rain look (0..1 each) without simulating rain
            if (arg.StartsWith("--wet=")) { var v = arg[6..].Split(','); _shotWet = (float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture)); }
            // --dig=x0,z0,x1,z1,depth: a straight trench (negative depth raises a bank) for screenshots and tests
            if (arg.StartsWith("--dig=")) { var v = arg[6..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray(); _shotDigs.Add((v[0], v[1], v[2], v[3], v[4])); }
            // --clouds=cumulus,storm,mackerel,lens: force the cloud types (0..1 each) for screenshots; --storm-at=deg: the
            // distant storms' bearing (0 = +X, 90 = +Z); --cloud-flash: lightning in a distant cell
            if (arg.StartsWith("--cloud-drift=")) _atmosphere.CloudDrift = arg[14..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            if (arg.StartsWith("--clouds=")) { var v = arg[9..].Split(',').Select(t => float.Parse(t, CultureInfo.InvariantCulture)).ToArray(); _atmosphere.ForceClouds = new Vector4(v[0], v[1], v[2], v[3]); }
            if (arg.StartsWith("--storm-at=")) _atmosphere.ForceStormAzimuth = Mathf.DegToRad(float.Parse(arg[11..], CultureInfo.InvariantCulture));
            if (arg == "--cloud-flash") _atmosphere.ShotFlash = true;
            // --front=km,strength: an approaching rain front at this distance (bearing from --storm-at, else upwind)
            if (arg.StartsWith("--front=")) { var v = arg[8..].Split(','); _atmosphere.ForceFront = new Vector2(float.Parse(v[0], CultureInfo.InvariantCulture), float.Parse(v[1], CultureInfo.InvariantCulture)); }
            if (arg.StartsWith("--wait=")) _shotWait = float.Parse(arg[7..], CultureInfo.InvariantCulture);
            if (arg == "--weather-probe") _weatherProbe = true;
            if (arg.StartsWith("--rain-here=")) _shotRainHere = float.Parse(arg[12..], CultureInfo.InvariantCulture);
            if (arg == "--face-sun") _faceSun = true;
            if (arg.StartsWith("--strike=")) _shotStrike = double.Parse(arg[9..], CultureInfo.InvariantCulture);
            if (arg.StartsWith("--thunder-wavs=")) _thunderWavs = arg[15..];
            if (arg.StartsWith("--tool=")) _tools.Select(int.Parse(arg[7..], CultureInfo.InvariantCulture));
            if (arg == "--grid") { _gridShown = true; _streamer.ShowGrid(true); }
            if (arg == "--biome-tint") { _biomeTint = true; _streamer.ShowBiomes(true); }
            if (arg == "--gallery") _gallery = true;
            if (arg == "--open-map") _openMap = true;
            if (arg.StartsWith("--day=")) _clock.SetDay(int.Parse(arg[6..], CultureInfo.InvariantCulture));
            if (arg.StartsWith("--hour=")) _pendingHour = double.Parse(arg[7..], CultureInfo.InvariantCulture);
            if (arg.StartsWith("--menu=")) { _menu.Visible = true; _menu.GetChild<TabContainer>(0).CurrentTab = int.Parse(arg[7..], CultureInfo.InvariantCulture); }
        }
        int index = Math.Max(0, _recipes.ToList().FindIndex(r => r.Id == preset));
        _profileList.Select(index);
        SelectType(index);
        // --set=gen.<key>=<value> also sets world-generation parameters (after the preset has loaded its values).
        foreach (string arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--set=gen."))
            {
                var kv = arg[6..].Split('=');
                // Dumps before build 42 wrote the key with a doubled prefix ("gen.gen.size"); accept both.
                string key = kv[0].StartsWith("gen.gen.") ? kv[0][4..] : kv[0];
                if (_gen.All.FirstOrDefault(x => x.Key == key) is { } g) g.Value = double.Parse(kv[1], CultureInfo.InvariantCulture);
            }
        _seedEdit.Text = seed.ToString(CultureInfo.InvariantCulture);
        if (OS.GetCmdlineUserArgs().Contains("--preview-biomes")) _biomeView.ButtonPressed = true;
        if (OS.GetCmdlineUserArgs().Contains("--preview") || OS.GetCmdlineUserArgs().Contains("--preview-biomes")) StartPreview();
        if (_smokeDirectory == null) Input.MouseMode = Input.MouseModeEnum.Captured;
        // --bench=<suite> [--bench-profile=<id>] [--bench-out=<dir>]: run a benchmark and quit (docs/BENCHMARK.md).
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--bench=")) is { } bench)
        {
            string profile = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--bench-profile="))?[16..] ?? "current";
            string? outDir = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--bench-out="))?[12..];
            BenchStart(bench[8..], profile, outDir, quitWhenDone: true);
            return;
        }
        // --window-at=x,y: where the window opens (two game windows side by side for multiplayer tests)
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--window-at=")) is { } at && at[12..].Split(',') is [var wx, var wy])
            Callable.From(() => DisplayServer.WindowSetPosition(new Vector2I(int.Parse(wx, CultureInfo.InvariantCulture), int.Parse(wy, CultureInfo.InvariantCulture)))).CallDeferred();
        bool joining = StartNetwork();
        StartGameplay();
        if (!joining) Generate(); // (joining: the world comes from the host)
    }

    // ------------------------------------------------------------------ world

    private void Generate()
    {
        if (_generation != null) return;
        if (IsNetClient && !_netWorldLoading) { _status = "In a multiplayer game the world is the host's."; return; }
        if (!int.TryParse(_seedEdit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed)) { _status = "Seed must be a whole number."; return; }
        var recipe = _recipes[Math.Max(0, _profileList.Selected)];
        WorldProfile profile; TerrainStyle style;
        try { (profile, style) = _gen.Build(recipe); }
        catch (Exception error) { _status = "These world parameters are not valid: " + error.Message; return; }
        _custom = !_gen.IsPreset;
        _worldTypeJson = recipe.Custom ? recipe.ToJson() : null; // (the world built, not whatever the list shows later)
        _worldArgs = $"--preset={recipe.Id} --seed={seed}" + (_custom ? " " + string.Join(" ", _gen.All.Select(g => $"--set={g.Key}={g.Value.ToString("R", CultureInfo.InvariantCulture)}")) : "");
        _overlay.Text = $"Generating {profile.Name}{(_custom ? " (custom)" : "")}, seed {seed}…\ncontinents → erosion → rivers and lakes";
        _overlay.Visible = true;
        _generationStarted = Time.GetTicksMsec();
        // The previewed world is exactly this one: reuse its plan instead of generating it again.
        var reuse = _previewPlan != null && _previewKey == PlanKey(seed) ? _previewPlan : null;
        _generation = Task.Run(() => new TerrainSampler(reuse ?? new WorldPlan(WorldManifest.Create(seed, profile), style: style)));
    }

    private const string CustomTypes = "user://world_types";

    /// <summary>Biome definitions and asset sets: built-in content plus the player's own files (user://biomes/*.json,
    /// user://asset_sets/*.json — the format of Content/biomes.json; docs/BIOMES.md). A broken custom file is skipped
    /// on its own (the log names it); the others still load.</summary>
    private static void LoadBiomes()
    {
        static List<(string Name, string Json)> Files(string folder)
        {
            var list = new List<(string, string)>();
            using var dir = DirAccess.Open(folder);
            if (dir == null) return list;
            foreach (var file in dir.GetFiles())
                if (file.EndsWith(".json")) list.Add((file, Godot.FileAccess.GetFileAsString($"{folder}/{file}")));
            return list;
        }
        string biomes = Godot.FileAccess.GetFileAsString("res://Content/biomes.json"), sets = Godot.FileAccess.GetFileAsString("res://Content/asset_sets.json");
        var goodSets = new List<string>();
        var goodBiomes = new List<string>();
        foreach (var (name, json) in Files("user://asset_sets"))
            try { BiomeRegistry.Parse(biomes, sets, goodBiomes, goodSets.Append(json)); goodSets.Add(json); }
            catch (Exception error) { GD.PushWarning($"Custom asset set file {name} skipped: {error.Message}"); }
        foreach (var (name, json) in Files("user://biomes"))
            try { BiomeRegistry.Parse(biomes, sets, goodBiomes.Append(json), goodSets); goodBiomes.Add(json); }
            catch (Exception error) { GD.PushWarning($"Custom biome file {name} skipped: {error.Message}"); }
        BiomeRegistry.Current = BiomeRegistry.Parse(biomes, sets, goodBiomes, goodSets);
    }

    /// <summary>World types the player saved (one JSON file each, editable by hand for deeper tweaks).</summary>
    private static List<WorldRecipe> LoadCustomTypes()
    {
        var list = new List<WorldRecipe>();
        using var dir = DirAccess.Open(CustomTypes);
        if (dir == null) return list;
        foreach (var file in dir.GetFiles())
        {
            if (!file.EndsWith(".json")) continue;
            try { list.Add(WorldRecipe.FromJson(Godot.FileAccess.GetFileAsString($"{CustomTypes}/{file}"))); }
            catch (Exception error) { GD.PushWarning($"World type {file} skipped: {error.Message}"); }
        }
        return list;
    }

    private void SaveCustomType(string name)
    {
        name = name.Trim();
        if (name.Length == 0) { _previewInfo.Text = "Give the new world type a name first."; return; }
        var current = _recipes[Math.Max(0, _profileList.Selected)];
        try
        {
            var (profile, _) = _gen.Build(current);
            var recipe = WorldRecipe.Create(name, $"Custom world type based on {current.Name}, {profile.WorldSize / 1000:0} km.", profile,
                current.Mainland, _gen.TypeSettings());
            DirAccess.MakeDirRecursiveAbsolute(CustomTypes);
            using (var f = Godot.FileAccess.Open($"{CustomTypes}/{recipe.Id}.json", Godot.FileAccess.ModeFlags.Write)) f.StoreString(recipe.ToJson());
            _recipes = WorldRecipe.Catalog(_profiles, LoadCustomTypes());
            _profileList.Clear();
            foreach (var r in _recipes) _profileList.AddItem(r.Name);
            int index = _recipes.ToList().FindIndex(r => r.Id == recipe.Id);
            _profileList.Select(index);
            SelectType(index);
            _previewInfo.Text = $"Saved world type \"{recipe.Name}\" ({ProjectSettings.GlobalizePath($"{CustomTypes}/{recipe.Id}.json")}). It adapts to any world size like the built-in types.";
        }
        catch (Exception error) { _previewInfo.Text = "Could not save the world type: " + error.Message; }
    }

    private void SelectType(int index)
    {
        var recipe = _recipes[Math.Clamp(index, 0, _recipes.Count - 1)];
        _gen.LoadRecipe(recipe);
        _typeInfo.Text = recipe.Description;
    }

    /// <summary>Identity of the world the creation screen describes (type, seed, every parameter).</summary>
    private string PlanKey(int seed) =>
        $"{_recipes[Math.Max(0, _profileList.Selected)].Id}|{seed}|" + string.Join(",", _gen.All.Select(x => x.Value.ToString("R", CultureInfo.InvariantCulture)));

    private void StartPreview()
    {
        if (_preview != null) return;
        if (!int.TryParse(_seedEdit.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int seed)) { _previewInfo.Text = "Seed must be a whole number."; return; }
        WorldProfile profile; TerrainStyle style;
        try { (profile, style) = _gen.Build(_recipes[Math.Max(0, _profileList.Selected)]); }
        catch (Exception error) { _previewInfo.Text = "Not valid: " + error.Message; return; }
        _pendingKey = PlanKey(seed);
        _previewInfo.Text = $"Generating the map of {profile.Name}…";
        _preview = Task.Run(() => new WorldPlan(WorldManifest.Create(seed, profile), style: style));
    }

    private void FinishPreview()
    {
        var task = _preview!;
        _preview = null;
        if (task.IsFaulted) { _previewInfo.Text = "Map failed: " + task.Exception?.GetBaseException().Message; return; }
        var plan = task.Result;
        _previewPlan = plan; _previewKey = _pendingKey;
        _previewImage.Texture = ImageTexture.CreateFromImage(WorldPreview.Render(plan, 512, _biomeView.ButtonPressed));
        var p = plan.Profile;
        _previewInfo.Text = $"{p.WorldSize / 1000:0} km × {p.WorldSize / 1000:0} km · " +
            (plan.LandformShapes > 0 ? $"landform template ({plan.LandformShapes} shapes)" + (p.Continents > 0 ? $" + {p.Continents} continent(s)" : "") : $"{p.Continents} continent(s)") +
            $", {p.Islands} islands · " +
            $"land {WorldPreview.LandShare(plan) * 100:0} % · {plan.Hydrology.Reaches.Count} river reaches, {plan.Hydrology.Lakes.Count} lakes · " +
            $"built in {plan.BuildMilliseconds / 1000:0.0} s";
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
        var start = _landmarks.Count > 0 ? _landmarks[0] : Landmarks.Dry(_sampler, "Start", Landmarks.Home(_sampler.Plan).X, Landmarks.Home(_sampler.Plan).Z);
        _originX = Math.Floor(start.X / 64) * 64; _originZ = Math.Floor(start.Z / 64) * 64;
        _streamer.Configure(_sampler, _originX, _originZ);
        if (_horizon == null) { _horizon = new HorizonLand(); AddChild(_horizon); }
        _horizon.Configure(_sampler);
        _water.Configure(_sampler, _streamer.WaterMaterial, _originX, _originZ);
        _rain = _shotRain && _shotPath == null; // a dump's Reproduce line carries --rain (H); shots start it later
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
                // (a fingerprint of the 3D trees of the 128 m cells around: every species, place, size and turn)
                ulong treeHash = 1469598103934665603UL;
                for (long tz = -2; tz <= 2; tz++)
                    for (long tx = -2; tx <= 2; tx++)
                        foreach (var tree in Vegetation.Trees(_sampler, new TileKey(TreeField.FineLevel, (long)Math.Floor(qx / 128) + tx, (long)Math.Floor(qz / 128) + tz), DetailConfig.For(_sampler)))
                            foreach (float f in new[] { tree.Species, tree.Transform.Origin.X, tree.Transform.Origin.Y, tree.Transform.Origin.Z, tree.Transform.Basis.X.X, tree.Transform.Basis.X.Z, tree.Transform.Basis.Y.Y })
                                treeHash = (treeHash ^ BitConverter.SingleToUInt32Bits(f)) * 1099511628211UL;
                GD.Print($"TREES around {qx}, {qz}: fingerprint {treeHash:x16}");
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
        if (_weatherProbe) { WeatherProbe(); GetTree().Quit(); }
        if (_thunderWavs != null) { ThunderWavs(_thunderWavs); GetTree().Quit(); }
        NetWorldReady();
        GameplayWorldReady();
    }

    /// <summary>The weather at the player: fronts over game time, rain amount from the local climate, snow when cold.</summary>
    private void UpdateWeather(double delta, SkyState sky)
    {
        // How closed the forest is around the player, for the exposure of light model 1 (4× a second; with the weather off too, so a switch
        // to model 1 starts from the right value).
        if ((_canopyTimer -= delta) <= 0) { _canopyTimer = .25; _atmosphere.Canopy = CanopyAround(PlayerX, PlayerZ); }
        if (!GameSettings.WeatherOn.On || _sampler == null)
        {
            _atmosphere.Precipitation = 0; _atmosphere.Snowing = false; _atmosphere.Thunder = false; _atmosphere.WeatherClouds = float.NaN;
            // (and no weather map or wind left from before: the distant rain kept drawing it — Tom's dump 104752 — and a
            // gale kept blowing over the lakes with the weather off — dumps 131735…131957)
            WeatherMap.Disable(); WindMap.Disable();
            _atmosphere.SetSimulatedWeather(null, 0, null, 0, 0);
            return;
        }
        double elevation = _sampler.Height(PlayerX, PlayerZ, 8) - _sampler.Plan.Profile.SeaLevel;
        var (temperature, rain) = _sampler.Plan.Climate.At(PlayerX, PlayerZ, Math.Max(0, elevation));
        var (ba, bb, share) = _sampler.Plan.Biomes.Sample(PlayerX, PlayerZ);
        double groundSnow = _sampler.Plan.Biomes.Pool[ba].Snow * share + _sampler.Plan.Biomes.Pool[bb].Snow * (1 - share);
        // Screenshots run with the clock stopped; let the weather settle at once there.
        double gameHours = sky.Day * 24 + sky.Hour;
        bool simulated = GameSettings.WeatherModel.Index == 1;
        if (simulated)
        {
            if (_weatherField == null || _weatherFieldFor != _sampler)
            {
                _weatherField = new WeatherField((ulong)_sampler.Plan.Manifest.Seed, _sampler.Plan.Climate);
                _weatherFieldFor = _sampler;
            }
            _weather.UpdateFromField(_weatherField, _shotPath != null ? 600 : delta, gameHours, PlayerX, PlayerZ, temperature, groundSnow, _shotPath != null);
            // The surface wind here (regional wind: systems, ridges, lee, valleys, coasts), sampled twice a second.
            _windTimer -= delta;
            if (_windTimer <= 0 || _shotPath != null)
            {
                _windTimer = .5;
                _localWind = _weatherField.WindAt(_sampler, PlayerX, PlayerZ, gameHours);
            }
            var (sx, sz, speed) = _localWind;
            _atmosphere.SetSimulatedWeather(new Vector2((float)sx, (float)sz), (float)speed, _weather.NearCells, PlayerX, PlayerZ);
            _windMap ??= new WindMap();
            _weatherMap ??= new WeatherMap();
            var camNow = _camera.GlobalPosition;
            _weatherMap.Update(_weatherField, gameHours, _originX + camNow.X, _originZ + camNow.Z, delta, _shotPath != null);
            float ratio = (float)(_atmosphere.Wind.SteadySpeed / Math.Max(.5, _localWind.Speed));
            _windMap.Update(_weatherField, _sampler, gameHours, _originX + camNow.X, _originZ + camNow.Z, _originX, _originZ, ratio,
                delta, _shotPath != null);
        }
        else
        {
            _weather.Update(_shotPath != null ? 600 : delta, gameHours, rain, temperature, groundSnow, (ulong)_sampler.Plan.Manifest.Seed);
            _atmosphere.SetSimulatedWeather(null, 0, null, 0, 0);
            WeatherMap.Disable();
            WindMap.Disable();
        }
        _atmosphere.Precipitation = _shotRainHere >= 0 ? _shotRainHere : _weather.Precipitation;
        _atmosphere.Snowing = _weather.Snow;
        _atmosphere.Thunder = _weather.Thunder;
        _atmosphere.WeatherClouds = _weather.Cloudiness;
        _atmosphere.Cumulus = _weather.Cumulus;
        _atmosphere.DistantStorm = _weather.DistantStorm;
        _atmosphere.StormComing = _weather.StormComing;
        _atmosphere.Mackerel = _weather.Mackerel;
        _atmosphere.FrontHours = _weather.FrontHours;
        _atmosphere.FrontStrength = _weather.FrontStrength;
    }

    /// <summary>--weather-probe: two game days of the simulated weather at the start point, printed (and a rain map).</summary>
    private void WeatherProbe()
    {
        var field = new WeatherField((ulong)_sampler!.Plan.Manifest.Seed, _sampler.Plan.Climate);
        double x = _shotAt?.X ?? PlayerX, z = _shotAt?.Z ?? PlayerZ;
        var (t0, r0) = _sampler.Plan.Climate.At(x, z, 0);
        GD.Print($"WEATHER probe at {x:0},{z:0}: {t0:0.0} C, {r0:0} mm/yr");
        int rainy = 0, stormy = 0, n = 0; double maxCells = 0;
        var cells = new System.Collections.Generic.List<WeatherField.Cell>();
        for (double h = 0; h < 48; h += .25, n++)
        {
            var w = field.At(x, z, h);
            field.Cells(x, z, h, 70000, cells);
            maxCells = Math.Max(maxCells, cells.Count);
            if (w.Precip > .1) rainy++;
            if (w.Convective > .35) stormy++;
            if (h % 2 == 0)
            {
                var (sx, sz, sp) = field.Steering(h);
                GD.Print($"  h{h,4:0} cloud {w.Cloud:0.00} rain {w.Precip:0.00} conv {w.Convective:0.00} front {w.Front:0.00} cells {cells.Count} steer {sp:0.0} m/s");
            }
        }
        GD.Print($"WEATHER rain {100.0 * rainy / n:0}% of the time, thunder {100.0 * stormy / n:0}%, up to {maxCells} cells within 70 km");
        // Regional wind around (round 28): speed and direction at points 0–12 km away, with the ground height.
        foreach (double h in new[] { 14.0, 3.0 })
            for (int i = 0; i < 9; i++)
            {
                double px = x + (i == 0 ? 0 : Math.Cos(i * Math.PI / 4) * 6000), pz = z + (i == 0 ? 0 : Math.Sin(i * Math.PI / 4) * 6000);
                var (wx, wz, ws) = field.WindAt(_sampler, px, pz, h);
                GD.Print($"WIND h{h} at {px - x:+0;-0} {pz - z:+0;-0}: ground {_sampler.Height(px, pz, 200):0} m, {ws:0.0} m/s toward ({wx:0.00}, {wz:0.00})");
            }
        foreach (double h in new[] { 14.0, 38.0 })
        {
            GD.Print($"WEATHER map h{h} (200 km, 5 km per char; # heavy, + rain, . cloud):");
            for (int j = -20; j < 20; j++)
            {
                var line = new System.Text.StringBuilder();
                for (int i = -20; i < 20; i++)
                {
                    var w = field.At(x + i * 5000, z + j * 5000, h);
                    line.Append(w.Precip > .5 ? '#' : w.Precip > .1 ? '+' : w.Cloud > .6 ? '.' : ' ');
                }
                GD.Print("  |" + line + "|");
            }
        }
    }

    /// <summary>--thunder-wavs=dir: thunder of one bolt heard from 0.3, 1.5, 5 and 15 km, saved as WAV files.</summary>
    private static void ThunderWavs(string dir)
    {
        System.IO.Directory.CreateDirectory(dir);
        var builder = new Lightning();
        var segments = new System.Collections.Generic.List<(Vector3, Vector3, float)>();
        var build = typeof(Lightning).GetMethod("BuildBolt", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        build.Invoke(builder, new object[] { segments, new Vector3(0, 1600, 0), new Vector3(0, 0, 0), 3f, 0 });
        foreach (double d in new[] { 300.0, 1500, 5000, 15000 })
        {
            var (sound, delay, db) = Lightning.Thunder(segments, new Vector3((float)d, 2, 0), 7, true);
            if (sound == null) continue;
            string path = System.IO.Path.Combine(dir, $"thunder_{d / 1000:0.0}km.wav");
            sound.SaveToWav(path);
            GD.Print($"THUNDER {d / 1000:0.0} km: heard after {delay:0.0} s, {sound.Data.Length / 2 / 22050.0:0.0} s long, {db:0.0} dB -> {path}");
        }
        builder.Free();
    }

    private double _peakTimer, _peakX = double.NaN, _peakZ, _reliefX = double.NaN, _reliefZ;
    private Vector2 _reliefWind;

    /// <summary>
    /// The land for the clouds (Atmosphere.SetRelief): 64 × 64 coarse heights over 64 km around the player, and how
    /// much the wind climbs there (windward slopes). Rebuilt after moving 12 km or when the wind turns by ~25°.
    /// </summary>
    private void UpdateRelief()
    {
        if (_sampler == null) return;
        var wd = _atmosphere.Wind.Direction;
        bool moved = double.IsNaN(_reliefX) || Math.Abs(PlayerX - _reliefX) + Math.Abs(PlayerZ - _reliefZ) > 12000;
        if (!moved && wd.Dot(_reliefWind) > .9f) return;
        _reliefX = PlayerX; _reliefZ = PlayerZ; _reliefWind = wd;
        const int N = 64; const float Size = 64000; double cell = Size / N, sea = _sampler.Plan.Profile.SeaLevel;
        var h = new double[(N + 2) * (N + 2)];
        for (int j = 0; j < N + 2; j++)
            for (int i = 0; i < N + 2; i++)
                h[j * (N + 2) + i] = Math.Max(0, _sampler.MacroHeight(_reliefX + (i - .5 - N / 2.0) * cell, _reliefZ + (j - .5 - N / 2.0) * cell) - sea);
        var data = new byte[N * N * 4];
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                double c = h[(j + 1) * (N + 2) + i + 1];
                double gx = (h[(j + 1) * (N + 2) + i + 2] - h[(j + 1) * (N + 2) + i]) / (2 * cell);
                double gz = (h[(j + 2) * (N + 2) + i + 1] - h[j * (N + 2) + i + 1]) / (2 * cell);
                double up = -(gx * wd.X + gz * wd.Y); // the ground rises into the wind's path: + where the wind climbs
                int k = (j * N + i) * 4;
                data[k] = (byte)Math.Clamp(c / 4000 * 255, 0, 255);
                data[k + 1] = (byte)Math.Clamp(Math.Max(0, up) / .08 * 255, 0, 255);
                data[k + 3] = 255;
            }
        _atmosphere.SetRelief(Image.CreateFromData(N, N, false, Image.Format.Rgba8, data), _reliefX, _reliefZ, Size);
    }

    /// <summary>
    /// The highest peak within ~18 km and how far it rises above the land around the player (lenticular clouds stand
    /// over it in strong wind): a coarse ring search every few seconds, at once after a jump.
    /// </summary>
    private void UpdatePeak(double delta)
    {
        if (_sampler == null) return;
        _peakTimer -= delta;
        bool jumped = double.IsNaN(_peakX) || Math.Abs(PlayerX - _peakX) + Math.Abs(PlayerZ - _peakZ) > 1500;
        if (_peakTimer > 0 && !jumped) return;
        _peakTimer = 4; _peakX = PlayerX; _peakZ = PlayerZ;
        double bestH = double.MinValue, bx = 0, bz = 0, sum = 0;
        int n = 0;
        // (from 5 km out: a lens right overhead is just a grey ceiling; one over a peak a few kilometres away shows its shape)
        foreach (double r in new[] { 5000.0, 8000.0, 12000.0, 17000.0, 23000.0 })
            for (int i = 0; i < 20; i++)
            {
                double a = i * Math.Tau / 20 + r * 1e-4;
                double x = PlayerX + Math.Cos(a) * r, z = PlayerZ + Math.Sin(a) * r;
                double h = _sampler.Height(x, z, 200);
                sum += h; n++;
                if (h > bestH) { bestH = h; bx = x; bz = z; }
            }
        double around = Math.Max(sum / n, _sampler.Plan.Profile.SeaLevel);
        _atmosphere.Peak = new Vector3((float)bx, (float)bestH, (float)bz);
        _atmosphere.PeakRelief = (float)(bestH - around);
        _atmosphere.SeaLevel = (float)_sampler.Plan.Profile.SeaLevel;
        if (_shotPath != null) GD.Print($"Peak {bx:0} {bestH:0} {bz:0}, {Math.Sqrt((bx - PlayerX) * (bx - PlayerX) + (bz - PlayerZ) * (bz - PlayerZ)):0} m away, relief {bestH - around:0} m");
    }

    /// <summary>A biome's sky mood: tint (rgb) and haze (a) — neutral when the biome sets none.</summary>
    private static Vector4 Mood(ResolvedBiome b)
    {
        var a = b.Def.Atmosphere;
        var (r, g, bl) = a.SkyTint is { } hex ? BiomeRegistry.Hex(hex) : (1f, 1f, 1f);
        return new Vector4(r, g, bl, (float)a.Haze);
    }

    private void Gallery()
    {
        var parent = _player.GetParent();
        var forward = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
        var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
        var names = PlaceholderPlants.Names;
        for (int i = 0; i < names.Length; i++)
        {
            var p = _player.Position + forward * (30 + (i % 2) * 6) + right * ((i - (names.Length - 1) / 2f) * 7.5f);
            double h = _sampler!.Height(p.X + _originX, p.Z + _originZ);
            var mesh = PlaceholderPlants.Build(names[i], 1)[0].Mesh;
            parent.AddChild(new MeshInstance3D { Mesh = mesh, Position = new Vector3(p.X, (float)h, p.Z) });
        }
    }

    /// <summary>
    /// Height of the walkable surface at (x, z): the first collision hit straight down from high above (terrain or an
    /// object), else the exact terrain height. The collision mesh is the terrain sampled every metre, so at a cliff it
    /// can run metres above the exact height between two grid points.
    /// </summary>
    private float GroundY(double x, double z)
    {
        double h = _sampler!.Sample(x, z).Height;
        var at = new Vector3((float)(x - _originX), (float)h, (float)(z - _originZ));
        var space = _player.GetWorld3D().DirectSpaceState;
        var from = at + Vector3.Up * 300;
        for (int i = 0; i < 6; i++)
        {
            var hit = space.IntersectRay(PhysicsRayQueryParameters3D.Create(from, at + Vector3.Down * 50, 1));
            if (hit.Count == 0) break;
            var y = ((Vector3)hit["position"]).Y;
            // Terrain or a rock: stand on it. A trunk, stump, bush or log: look below it.
            var owner = hit["collider"].As<CollisionObject3D>();
            var shape = owner?.ShapeOwnerGetOwner(owner.ShapeFindOwner(hit["shape"].AsInt32())) as CollisionShape3D;
            if (shape?.Shape is ConcavePolygonShape3D or ConvexPolygonShape3D or null) return y;
            from = new Vector3(at.X, y - .05f, at.Z);
        }
        return (float)h;
    }

    private double _canopyTimer;

    /// <summary>0 open … 1 closed forest: forest density at the player and six points 10 m around.</summary>
    private float CanopyAround(double x, double z)
    {
        if (_sampler == null) return 0;
        double cover = _sampler.Plan.Style.ForestCover, sum = 0;
        for (int i = 0; i < 7; i++)
        {
            double a = i * Math.Tau / 6, r = i == 0 ? 0 : 10;
            double px = x + Math.Cos(a) * r, pz = z + Math.Sin(a) * r;
            sum += Vegetation.ForestDensity(_sampler, px, pz, _sampler.Sample(px, pz), cover) * (i == 0 ? 2 : 1);
        }
        // (none when flying over the forest: the forest look is for being under the crowns)
        double above = _player.Position.Y - _sampler.Sample(x, z).Height;
        return (float)(Math.Clamp(sum / 8 * _sampler.Plan.Style.ForestDensity, 0, 1) * (1 - DetMath.Smooth((above - 12) / 18)));
    }

    private void Teleport(double x, double z)
    {
        if (_sampler == null) return;
        _canopyTimer = 0;
        _atmosphere.SnapExposure = true;
        var t = _sampler.Sample(x, z);
        double y = Math.Max(t.Height, double.IsNaN(t.WaterLevel) ? t.Height : t.WaterLevel) + 1.0;
        Rebase(x, z, force: true);
        _player.Position = new Vector3((float)(x - _originX), (float)y, (float)(z - _originZ));
        _player.Velocity = Vector3.Zero;
        _holding = true; // wait for ground collision before gravity
    }

    // Wakes in the water around the player (experimental 2 draws them).
    private readonly Wakes _wakes = new();
    private Vector2 _wakeCurrent;
    private double _wakeCurrentAge = 1;

    private void UpdateWakes(float dt, double level)
    {
        if (GameSettings.WaterStyle.Index < 2) return;
        float feet = _player.Position.Y;
        bool inWater = !_fly && !double.IsNaN(level) && feet < level + .05 && level - feet < 2.6;
        // The local current (a few terrain samples): refreshed a few times a second.
        _wakeCurrentAge += dt;
        if (inWater && _wakeCurrentAge > .2) { _wakeCurrent = Wakes.CurrentAt(_sampler!, PlayerX, PlayerZ); _wakeCurrentAge = 0; }
        _wakes.Update(dt, _atmosphere.OceanTime, _player.Position, _player.Velocity, inWater, inWater ? _wakeCurrent : Vector2.Zero);
        _wakes.Apply(_streamer.WaterMaterial, _player.Position);
    }

    private void Rebase(double x, double z, bool force = false)
    {
        double ox = Math.Floor(x / 64) * 64, oz = Math.Floor(z / 64) * 64;
        if (!force && Math.Abs(x - _originX) < 1536 && Math.Abs(z - _originZ) < 1536) return;
        var shift = new Vector3((float)(_originX - ox), 0, (float)(_originZ - oz));
        _originX = ox; _originZ = oz;
        _player.Position += shift;
        _wakes.Shift(shift);
        _streamer.Rebase(ox, oz);
        _water.Rebase(ox, oz);
    }

    private bool _custom;

    private void OnSettingChanged(Setting s)
    {
        if (s.Rebuild) _streamer.Refresh();
        if (_net?.Server != null && GameSettings.AffectsWorld(s)) _net.Server.SettingsChanged();
    }

    public override void _ExitTree()
    {
        GameSettings.Changed -= OnSettingChanged;
        if (IsNetClient) GameSettings.ClearOverrides(); // (the player's own time, weather and sea settings back)
        GetTree().Root.SizeChanged -= DisplaySettings.WindowChanged;
        RenderingServer.GlobalShaderParameterSet("tree_ranges", Vector4.Zero); // the valley game's trees never fade
        // Static caches of Godot resources (tree, rock and prop meshes, materials) must be freed while the engine is
        // still alive; left to the .NET finalizers at process exit they intermittently crashed on quit (as the valley
        // game already handles in Main).
        RockMeshes.Release(); ObjectCollision.Release(); GameplayModels.Release(); RockTextureArray.Release(); GroundTextures.Release(); Flora.Release(); WoodlandAssets.Release(); Visuals.ReleaseMaterials();
        // Tens of thousands of wrappers (tiles' MultiMeshes, collision shapes) are still referenced from C#: finalised
        // after Godot's C# layer shut down they aborted the quit (FATAL in _instance_binding_free_callback, 1-2 in 10
        // collision test runs). Collect them now, while the engine is alive.
        GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
    }

    private double PlayerX => _originX + _player.Position.X;
    private double PlayerZ => _originZ + _player.Position.Z;

    // ------------------------------------------------------------------ frame

    private ulong _processStartUs;
    private bool _collisionTestPending = OS.GetCmdlineUserArgs().Contains("--collision-test");
    /// <summary>Time of the last _Process (this script's own work each frame; benchmark).</summary>
    private double _lastProcessMs;

    private readonly Hitches _hitch = new(), _physicsHitch = new();

    public override void _Process(double delta)
    {
        _processStartUs = Time.GetTicksUsec();
        _hitch.Begin("process");
        if (_generation is { IsCompleted: true }) FinishGeneration();
        if (_preview is { IsCompleted: true }) FinishPreview();
        StepJoining(delta);
        TeleportTest(delta);
        if (_bench != null && (_sampler == null || _generation != null)) BenchStep(delta);
        if (_sampler == null) return;
        Rebase(PlayerX, PlayerZ);
        _hitch.Mark("rebase");
        _streamer.Update(PlayerX, PlayerZ);
        _hitch.Mark("terrain streaming");
        _horizon?.Step(PlayerX, PlayerZ, _originX, _originZ, _streamer.Levels, WorldStreamer.TopRadius, _camera.GlobalPosition.Y);
        // The far plane covers the horizon land (it was 90 km whatever its range: land beyond was cut off).
        _camera.Far = Math.Max(90000, (_horizon?.Range ?? 0) * 1.05f);
        _hitch.Mark("horizon land");
        if (_holding && _streamer.HasGround(PlayerX, PlayerZ))
        {
            // Snap to the generated ground once its collision exists: onto the collision surface, which at cliffs (a
            // drop between two 1 m grid points) runs above the exact terrain height; the player stood under the ground.
            _player.Position = new Vector3(_player.Position.X, GroundY(PlayerX, PlayerZ) + .05f, _player.Position.Z);
            _player.Velocity = Vector3.Zero;
            _holding = false;
        }
        if (_overlay.Visible && _generation == null && _streamer.Missing < 20) _overlay.Visible = false;
        UpdateCamera();
        StepRemotes(delta);
        UpdateGameplay(delta);
        UpdateMaking(delta);
        UpdateGathering(delta);
        UpdateFishing(delta);
        UpdateActionBar(); // (after everything that may be busy this frame)
        GameplayTest(delta);
        GatherTest(delta);
        HuntTest(delta);
        BuildTest(delta);
        FishTest(delta);
        SaveTest(delta);
        UpdateAutosave(delta);
        var cam = _camera.GlobalPosition;
        RenderingServer.GlobalShaderParameterSet("tree_camera", new Vector4(cam.X, cam.Y, cam.Z, 0)); // tree hand-overs (TreeFade)
        double latitude = _sampler.Plan.Climate.Latitude(PlayerZ);
        if (_pendingHour is { } setHour) { _clock.SetHour(setHour, latitude); _pendingHour = null; }
        var sky = _clock.Advance(delta, latitude, run: _shotPath == null);
        _hitch.Mark("ground snap, camera, others");
        UpdateWeather(delta, sky);
        _hitch.Mark("weather");
        UpdatePeak(delta);
        UpdateRelief();
        if (!_timeSlider.HasFocus()) _timeSlider.SetValueNoSignal(sky.Hour);
        _atmosphere.Update(delta, sky, _originX + cam.X, _originZ + cam.Z, _originX, _originZ, cam);
        _hitch.Mark("atmosphere");
        if (GameSettings.WeatherModel.Index == 1 && GameSettings.WeatherOn.On)
        {
            if (_lightning == null)
            {
                _lightning = new Lightning { GroundAt = (x, z) => _sampler?.Height(x, z, 32) ?? 0 };
                AddChild(_lightning);
            }
            _lightning.Step(delta, sky.Day * 24 + sky.Hour, _atmosphere.Cells, false, _atmosphere.Snowing, cam, _originX, _originZ,
                _atmosphere.Precipitation, _atmosphere.Wind.Speed, _atmosphere.Wind.Gust);
        }
        else if (_lightning != null) { _lightning.QueueFree(); _lightning = null; RenderingServer.GlobalShaderParameterSet("lightning", Vector4.Zero); }
        if (GameSettings.GroundFog.Index > 0)
            _mist.Update(_sampler, PlayerX, PlayerZ, _originX, _originZ, _atmosphere.MistAmount, (float)_sampler.Height(PlayerX, PlayerZ, 8), _atmosphere.MistColour);
        else _mist.Visible = false;
        _hitch.Mark("lightning, mist");
        UpdateUnderwater(cam);
        _hudTimer -= delta;
        if (_hudTimer <= 0) { _hudTimer = .2; UpdateHud(); }
        _lastProcessMs = (Time.GetTicksUsec() - _processStartUs) / 1000.0;
        _hitch.End($"at {PlayerX:0}, {PlayerZ:0}");
        if (_findSitesPending && _sampler != null && _generation == null && !_holding) { _findSitesPending = false; FindSites(); }
        if (_clearSpotPending && _sampler != null && _generation == null && !_holding) { _clearSpotPending = false; ClearSpot(); }
        if (_probeWaterPending && _sampler != null && _generation == null) { _probeWaterPending = false; ProbeWater(); }
        if (_collisionTestPending && _streamer.Settled && _streamer.PendingCollision == 0 && !_holding && _generation == null)
        {
            // --at=x,z picks the test area (otherwise the world's start).
            if (_shotAt is { } testAt && Math.Abs(PlayerX - testAt.X) + Math.Abs(PlayerZ - testAt.Z) > 2) { SetFly(false); Teleport(testAt.X, testAt.Z); }
            else { _collisionTestPending = false; CollisionTestSetup(); }
        }
        if (_bench != null && _generation == null) BenchStep(delta);
        else if (_smokeDirectory != null) Smoke(delta);
    }

    /// <summary>Water surface above a world point (generated water, simulated water, waves), NaN if dry.</summary>
    private double WaterSurface(double x, double z, out WaterKind kind)
    {
        var t = _sampler!.Sample(x, z);
        kind = t.Water;
        double level = t.Water != WaterKind.None ? t.WaterLevel : double.NaN;
        float simulated = _water.Sim?.SurfaceAt(x, z) ?? float.NaN;
        if (!float.IsNaN(simulated) && (double.IsNaN(level) || simulated > level)) { level = simulated; if (kind == WaterKind.None) kind = WaterKind.River; }
        if (t.Water is WaterKind.Ocean or WaterKind.Lake && !double.IsNaN(level))
        {
            // Waves in the shader's scene-local coordinates (the float origin), so the surface matches what is drawn.
            double lx = x - _originX, lz = z - _originZ, depth = level - t.Height, time = _atmosphere.OceanTime;
            if (GameSettings.WaterStyle.Index >= 1)
            {
                bool sea = t.Water == WaterKind.Ocean;
                var (gx, gz, exposure) = sea ? WaterShore.CoastAt(_sampler, x, z, depth) : (0f, 0f, 1f);
                double fetch = sea ? 1e5 : t.WaterBody >= 0 && t.WaterBody < _sampler.Plan.Hydrology.Lakes.Count ? Math.Sqrt(_sampler.Plan.Hydrology.Lakes[t.WaterBody].Area) : 0;
                var wind = _atmosphere.Wind.Direction;
                level += GameSettings.WaterStyle.Index >= 3 && _atmosphere.Ocean != null
                    ? WaterShore.Height3(_atmosphere.Ocean, x, z, lx, lz, time, Math.Min(depth, 20), sea, exposure, gx, gz, _atmosphere.Spectrum.SeaState)
                    : GameSettings.WaterStyle.Index >= 2
                    ? WaterShore.Height2(_atmosphere.Spectrum, lx, lz, time, Math.Min(depth, 20), sea, !sea, fetch, exposure, gx, gz, wind.X, wind.Y)
                    : WaterShore.Height(_atmosphere.Waves, lx, lz, time, Math.Min(depth, 20), sea, !sea, fetch, exposure, gx, gz);
            }
            else level += _atmosphere.Waves.Height(lx, lz, time, depth, t.Water == WaterKind.Lake);
        }
        return level;
    }

    private float _cameraDepth;

    private void UpdateUnderwater(Vector3 cam)
    {
        if (_sampler == null) return;
        double level = WaterSurface(_originX + cam.X, _originZ + cam.Z, out var kind);
        _cameraDepth = double.IsNaN(level) ? 0 : (float)(level - cam.Y);
        _atmosphere.SetUnderwater(_cameraDepth, kind);
        // The nearest water surface for raindrop splashes: under the camera, else in rings 12 and 24 m around.
        _splashTimer -= GetProcessDeltaTime();
        if (_splashTimer <= 0)
        {
            _splashTimer = .25;
            double found = level;
            for (int ring = 1; ring <= 2 && double.IsNaN(found); ring++)
                for (int i = 0; i < 8 && double.IsNaN(found); i++)
                {
                    double a = i * Math.Tau / 8;
                    found = WaterSurface(_originX + cam.X + Math.Cos(a) * 12 * ring, _originZ + cam.Z + Math.Sin(a) * 12 * ring, out _);
                }
            _splashLevel = (float)found;
        }
        _atmosphere.SetSplashSurface(_splashLevel, cam);
    }
    private double _splashTimer;
    private float _splashLevel = float.NaN;

    public override void _PhysicsProcess(double delta)
    {
        if (_sampler == null || _holding) return;
        if (_bench != null) { _player.Velocity = Vector3.Zero; return; } // the benchmark places the player itself
        _physicsHitch.Begin("physics");
        float dt = (float)delta;
        var input = Vector2.Zero;
        bool typing = _menu.Visible || _pack.Visible;
        if (!typing)
        {
            if (Input.IsKeyPressed(Key.W)) input.Y -= 1;
            if (Input.IsKeyPressed(Key.S)) input.Y += 1;
            // (with a fish on, A and D turn the rod against it, not step sideways)
            if (Input.IsKeyPressed(Key.A) && !FishingSteers) input.X -= 1;
            if (Input.IsKeyPressed(Key.D) && !FishingSteers) input.X += 1;
        }
        if (_smokeDirectory != null && _shotPath == null && _smokeStage == 2) input = new Vector2(0, -1);
        if (CollisionTestActive) { CollisionTestStep(delta); input = _testInput; }
        input = input.LimitLength(1);
        var forward = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
        var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
        var move = right * input.X - forward * input.Y;
        bool sprint = (!typing && Input.IsKeyPressed(Key.Shift)) || (CollisionTestActive && _testSprint);
        var velocity = _player.Velocity;

        UpdateLocalWater(dt, typing);
        _physicsHitch.Mark("local water, tools");
        double feet = _player.Position.Y;
        // Swim on the waves (the same waves the water shader draws).
        double level = WaterSurface(PlayerX, PlayerZ, out _);
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
            bool dive = !typing && (Input.IsKeyPressed(Key.C) || Input.IsKeyPressed(Key.Q)), up = !typing && Input.IsKeyPressed(Key.Space);
            float target = (float)(level - 1.3);
            bool submerged = _player.Position.Y < target - .4f;
            if (dive) velocity.Y = sprint ? -3f : -1.8f;            // C/Q: dive down to the bottom
            else if (up) velocity.Y = sprint ? 3f : 1.8f;           // Space: swim up
            else if (submerged)
            {
                // Under water: swimming forward follows the view (look down to go deeper); idle, you drift up slowly.
                velocity.Y = input.Y < 0 ? Mathf.Sin(_pitch + .32f) * speed * -input.Y + .15f : .5f; // relative to the default view
            }
            else velocity.Y = Mathf.Clamp((target - _player.Position.Y) * 3f, -2f, 2f); // buoyancy: head above the surface
        }
        else
        {
            float speed = sprint ? 11 : 5;
            velocity.X = move.X * speed; velocity.Z = move.Z * speed;
            if (_player.IsOnFloor()) { if ((!typing && Input.IsKeyPressed(Key.Space)) || (CollisionTestActive && _testJump)) velocity.Y = 6.5f; }
            else velocity.Y -= 20 * dt;
        }
        _player.Velocity = velocity;
        _player.MoveAndSlide();
        if (!_fly && !_swimming) StepUp(move, sprint ? 11 : 5, dt);
        PushObjects(delta); // (walking into a fallen trunk pushes it)
        _physicsHitch.Mark("move");
        UpdateWakes(dt, level);
        // Never fall through the world if collision has not streamed in yet (e.g. after very fast flight): back onto
        // the collision surface (or the terrain height without collision), and stop the fall.
        var ground = _sampler.Sample(PlayerX, PlayerZ).Height;
        // Below a cliff the 1 m collision mesh can legitimately run under the exact height: only act when the player
        // is under the collision surface itself (it used to lift the player onto the exact height, up cliff faces).
        if (!_noclip && _player.Position.Y < ground - .5)
        {
            float surface = GroundY(PlayerX, PlayerZ);
            if (_player.Position.Y < surface - .25f)
            {
                _player.Position = new Vector3(_player.Position.X, surface + .05f, _player.Position.Z);
                _player.Velocity = _player.Velocity with { Y = 0 };
            }
        }

        // Character: face the direction of travel, swing limbs.
        _me.Animate(dt, _player.Velocity, _fly, holdFacing: FirstPerson);
        _physicsHitch.End($"at {PlayerX:0}, {PlayerZ:0}");
    }

    /// <summary>Local water: simulate, and the debug tools (G pour at the aimed point, J toggle a spring there).</summary>
    private double _simTime;

    private void UpdateLocalWater(float dt, bool typing)
    {
        _simTime += dt;
        var sim = _water.Sim;
        if (sim == null) return;
        float precip = _rain ? .7f : _atmosphere.Precipitation;
        sim.RainRate = precip > .1f && !_atmosphere.Snowing ? 6e-5f * GameSettings.RainStrength.F * precip / .7f : 0;
        _atmosphere.Raining = _rain;
        _water.Step(dt, PlayerX, PlayerZ);
        // G pours where you look; the water size (, .) makes it bigger: 2.5 → 160 m³/s over a 0.5 → 4 m disc.
        if (!typing && Input.IsKeyPressed(Key.G) && Aim() is { } at) sim.AddDisc(at.X, at.Z, WaterRadius, WaterPour * dt);
        // Rain erosion: the ground the streams wore away (or built up) goes to the terrain edits, a few mm at a time.
        sim.ErosionRate = GameSettings.RainErosion.F;
        if (!IsNetClient && (_erosionTimer -= dt) <= 0) // (a joining player's ground comes from the host)
        {
            _erosionTimer = 10;
            if (sim.TakeErosion(.004f) is { Count: > 0 } worn) ApplyErosion(worn);
        }
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
        var near = sim.Sources.Find(s => Math.Abs(s.X - at.X) < 4 + s.Radius && Math.Abs(s.Z - at.Z) < 4 + s.Radius);
        if (near != null) { sim.Sources.Remove(near); _status = "Spring removed."; }
        else
        {
            sim.Sources.Add(new Thornwake.Hydraulics.LocalWater.Source { X = at.X, Z = at.Z, Rate = WaterSpring, Radius = WaterRadius });
            _status = $"Spring placed, size {_waterSize} ({WaterSpring:0.#} m³/s; J on it again removes it).";
        }
    }

    // Water tools' size (, smaller · . bigger): 1 = as before (G 2.5 m³/s on one spot, spring 0.6 m³/s), each step ×4.
    private int _waterSize = 1;
    private double _erosionTimer = 10;
    private float WaterPour => 2.5f * MathF.Pow(4, _waterSize - 1);
    private float WaterSpring => .6f * MathF.Pow(4, _waterSize - 1);
    private float WaterRadius => _waterSize == 1 ? 0 : .5f * MathF.Pow(2, _waterSize - 1);

    private void WaterSize(int step)
    {
        _waterSize = Math.Clamp(_waterSize + step, 1, 4);
        _status = $"Water size {_waterSize}: G pours {WaterPour:0.#} m³/s, J springs give {WaterSpring:0.#} m³/s" +
            (WaterRadius > 0 ? $", over {WaterRadius * 2:0.#} m" : "") + " (, smaller · . bigger).";
    }

    /// <summary>Rain erosion's floor changes (0.5 m cells) into the terrain edits (1 m vertices: the mean of the four
    /// cells around each), and the touched tiles rebuilt.</summary>
    private void ApplyErosion(List<(double X, double Z, float Dz)> cells)
    {
        var sums = new Dictionary<(long X, long Z), double>();
        foreach (var (x, z, dz) in cells)
        {
            var key = ((long)Math.Round(x), (long)Math.Round(z));
            sums[key] = sums.GetValueOrDefault(key) + dz;
        }
        foreach (var key in sums.Keys.ToList()) sums[key] /= 4;
        // Only the touched chunks: after a teleport the cells lie at both places (D106).
        foreach (var (x0, z0, x1, z1) in _sampler!.Edits.Add(sums))
            _streamer.Invalidate(x0 - 2, z0 - 2, x1 + 2, z1 + 2);
    }


    private void UpdateCamera()
    {
        _pivot.Rotation = new Vector3(_pitch, _yaw, 0);
        if (FirstPerson)
        {
            // At the eyes, a little in front of the face; the body faces where you look.
            var forward = new Vector3(-Mathf.Sin(_yaw), 0, -Mathf.Cos(_yaw));
            _pivot.Position = _player.Position + Vector3.Up * 1.64f + forward * .12f;
            _arm.SpringLength = 0;
            _me.Facing = _yaw + Mathf.Pi;
        }
        else
        {
            // Over the right shoulder while aiming the bow or building (D120): the screen's centre is not Alden's head.
            bool aiming = GameSettings.ShoulderAim.On && (_me.Weapon == "bow" || _building);
            _shoulder = Mathf.Lerp(_shoulder, aiming ? .7f : 0, .15f);
            var right = new Vector3(Mathf.Cos(_yaw), 0, -Mathf.Sin(_yaw));
            _pivot.Position = _player.Position + Vector3.Up * 1.55f + right * _shoulder;
            _arm.SpringLength = Mathf.Lerp(_arm.SpringLength, _zoom, .2f);
        }
        // First person: the body is only a shadow; the torch goes from the hand to the view.
        _me.SetFirstPerson(FirstPerson ? _fpHold : null);
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
            WaterKind.Pond => $"in a pool ({t.WaterLevel - t.Height:0.0} m deep)",
            _ => t.Mountain > .3 ? "mountains" : t.Height - _sampler.Plan.Profile.SeaLevel < 6 ? "coast" : "lowland",
        };
        if (_sampler.Plan.Hydrology.NearestRiver(PlayerX, PlayerZ, 400, out var river) && t.Water == WaterKind.None)
            where += $" · river {river.Distance:0} m away ({river.Width:0} m wide)";
        // Biome (and the one it blends into), with the local climate.
        var biomes = _sampler.Plan.Biomes;
        var (ba, bb, share) = biomes.Sample(PlayerX, PlayerZ);
        var (temperature, rain) = _sampler.Plan.Climate.At(PlayerX, PlayerZ, t.Height - _sampler.Plan.Profile.SeaLevel);
        where += $"\nBiome {biomes.Pool[ba].Def.Name}" + (bb != ba && share < .95 ? $" ({share * 100:0} %, → {biomes.Pool[bb].Def.Name})" : "") +
            $" · {temperature:0} °C, {rain:0} mm/yr" + (_atmosphere.MistAmount > .02f ? $" · mist {_atmosphere.MistAmount:0.00}" : "");
        _atmosphere.SetBiomeMood(Mood(biomes.Pool[ba]) * (float)share + Mood(biomes.Pool[bb]) * (float)(1 - share));
        LightProfiles.SetTarget(LightProfiles.For(biomes.Pool[ba].Def.Id), LightProfiles.For(biomes.Pool[bb].Def.Id), (float)share);
        static float MistOf(ResolvedBiome b) => (float)Math.Clamp(b.Def.Atmosphere.Mist + .5 * b.Def.Atmosphere.Fog, 0, 1);
        float nearWater = t.Water != WaterKind.None ? 1 : _sampler.Plan.Hydrology.NearestRiver(PlayerX, PlayerZ, 300, out var near) ? 1 - (float)(near.Distance / 300) : 0;
        _atmosphere.SetMist((float)t.Height, MistOf(biomes.Pool[ba]) * (float)share + MistOf(biomes.Pool[bb]) * (float)(1 - share), nearWater);
        string mode = _fly ? (_noclip ? "Fly + noclip" : "Fly") : _swimming ? "Swimming" : "Walk";
        var (dayHours, dayShare) = GameClock.Daylight(_sampler.Plan.Climate.Latitude(PlayerZ));
        var wind = _atmosphere.Wind;
        where += $"\n{_clock.Describe()} · daylight {dayHours:0.0} h → {GameSettings.DayLength.Value * dayShare:0} real min day, " +
                 $"{GameSettings.DayLength.Value * (1 - dayShare):0} min night{(GameSettings.TimePaused.On ? " (time stopped)" : "")} · wind {wind.Speed:0} m/s from {wind.Compass()}";
        _hud.Text = $"Alden Wayfarer · {_status}\n" +
            $"Position {PlayerX:0} , {PlayerZ:0}   ground {t.Height:0.0} m   {where}\n" +
            $"{mode}   FPS {Engine.GetFramesPerSecond():0}   tiles {_streamer.Resident} (+{_streamer.Missing} queued)   " +
            $"terrain triangles {_streamer.Triangles / 1000}k   tile build {_streamer.LastBuildMs:0.0} ms, commit {_streamer.LastCommitMs:0.0} ms";
        _hud.Text += "\n" + (_axeHeld || _me.RodHeld || _me.Weapon != null || _building ? GameplayHud() : _tools.Hud()) + NetHud();
        if (_water.Sim is { Added: > 0 } sim)
            _hud.Text += $"\nWater simulation: {sim.Volume():0.0} m³ on the land, {sim.Absorbed:0.0} m³ joined rivers/lakes{(_rain ? ", raining" : "")}" +
                $"{(sim.Sources.Count > 0 ? $", {sim.Sources.Count} spring(s)" : "")}";
    }

    // ------------------------------------------------------------------ input

    public override void _UnhandledInput(InputEvent e)
    {
        if (_bench != null)
        {
            if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) BenchAbort("stopped with Esc");
            return;
        }
        if (e is InputEventMouseMotion motion && Input.MouseMode == Input.MouseModeEnum.Captured)
        {
            _yaw -= motion.Relative.X * .0035f;
            _pitch = Mathf.Clamp(_pitch - motion.Relative.Y * .0035f, -1.45f, .9f);
        }
        if (e is InputEventMouseButton built && Input.MouseMode == Input.MouseModeEnum.Captured && BuildingWheel(built)) return; // (building: the wheel turns the piece)
        if (e is InputEventMouseButton button && button.Pressed)
        {
            // (one step in from the closest zoom: first person; one step out of it: the closest zoom again)
            if (button.ButtonIndex == MouseButton.WheelUp) _zoom = _zoom <= 1.85f ? 0 : Mathf.Max(1.8f, _zoom * .88f);
            if (button.ButtonIndex == MouseButton.WheelDown) _zoom = FirstPerson ? 1.8f : Mathf.Min(60f, _zoom * 1.14f);
            if (button.ButtonIndex == MouseButton.Left && !_menu.Visible) Input.MouseMode = Input.MouseModeEnum.Captured;
        }
        if (e is not InputEventKey { Pressed: true, Echo: false } key) return;
        if (GameplayKey(key)) return;
        switch (key.Keycode)
        {
            case Key.Escape when _map.Visible: _map.Visible = false; Input.MouseMode = Input.MouseModeEnum.Captured; _hud.Visible = _showHud; _help.Visible = true; break;
            case Key.M when !_menu.Visible && _sampler != null:
                if (_map.Visible) { _map.Visible = false; Input.MouseMode = Input.MouseModeEnum.Captured; _hud.Visible = _showHud; _help.Visible = true; }
                else { _map.Open(_sampler.Plan, PlayerX, PlayerZ, _yaw); Input.MouseMode = Input.MouseModeEnum.Visible; _hud.Visible = false; _help.Visible = false; }
                break;
            case Key.B when _map.Visible: _map.ToggleBiomes(); break;
            case Key.Escape: ToggleMenu(!_menu.Visible); break;
            case Key.Tab: Input.MouseMode = Input.MouseMode == Input.MouseModeEnum.Captured ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured; break;
            case Key.F: if (!_menu.Visible) SetFly(!_fly); break;
            case Key.N: if (!_menu.Visible) SetNoclip(!_noclip); break;
            case Key.F1: _help.Visible = !_help.Visible; break;
            case Key.F2: _showHud = !_showHud; _hud.Visible = _showHud; break;
            case Key.F3: _gridShown = !_gridShown; _streamer.ShowGrid(_gridShown); break;
            case Key.F4: _biomeTint = !_biomeTint; _streamer.ShowBiomes(_biomeTint); break;
            case Key.J: if (!_menu.Visible) ToggleSpring(); break;
            case Key.Comma: if (!_menu.Visible) WaterSize(-1); break;
            case Key.Period: if (!_menu.Visible) WaterSize(1); break;
            case >= Key.Key1 and <= Key.Key5:
                if (_menu.Visible) break;
                if (_axeHeld) ToggleAxe(); // (another tool in the hand)
                if (_me.RodHeld) ToggleRod();
                if (_tools.Select((int)(key.Keycode - Key.Key1))) _status = _tools.Status;
                break;
            case Key.T or Key.Y or Key.Bracketleft or Key.Bracketright or Key.Minus or Key.Equal: if (!_menu.Visible) _tools.DevKey(key.Keycode); break;
            case Key.R: if (!_menu.Visible) { _me.Torch.Lit = !_me.Torch.Lit; _status = _me.Torch.Lit ? "Torch lit (R puts it out)." : "Torch out."; } break;
            case Key.H:
                if (_menu.Visible) break;
                if (IsNetClient) { _status = "The host starts and stops the rain (H in the host's game)."; break; }
                _rain = !_rain; _status = _rain ? "Rain on (H stops it)." : "Rain stopped."; break;
            case Key.K: if (!_menu.Visible) { _water.Clear(); _rain = false; _status = "Simulated water cleared."; } break;
            case Key.L: if (!_menu.Visible && _lightning != null)
                {
                    _lightning.StrikeNear(_camera.GlobalPosition, -_camera.GlobalBasis.Z, _originX, _originZ);
                    _status = "Lightning (dev): a strike ahead — count the seconds to the thunder (×343 m).";
                }
                else if (!_menu.Visible) _status = "Lightning needs Settings → Simulation → Weather model: simulated.";
                break;
            case Key.F9: DebugDump(); break;
            case Key.F5: SaveWorld("quicksave"); break;
            case Key.F12: Screenshot(Path.Combine(OS.GetUserDataDir(), $"screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png")); break;
        }
    }

    private bool _gridShown;

    private void SetFly(bool fly) { _fly = fly; _flyToggle.SetPressedNoSignal(fly); if (!fly) SetNoclip(false); }
    private void SetNoclip(bool noclip) { _noclip = noclip; _noclipToggle.SetPressedNoSignal(noclip); _player.CollisionMask = noclip ? 0u : 1u; if (noclip) SetFly(true); }

    private void ToggleMenu(bool open)
    {
        _menu.Visible = open;
        _hostButton.Visible = _net == null && _sampler != null && _generation == null;
        Input.MouseMode = open ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
    }

    private string _worldArgs = "";
    private string? _worldTypeJson;

    /// <summary>
    /// F9 (Tom: "a debug dump … so you can recreate the same place, same situation"): a text file with the world, the
    /// place, the view, time, weather, sea, every setting and the machine, plus a screenshot next to it and a ready
    /// command line that rebuilds the same scene for a --world-shot. Saved in the user folder (opened afterwards).
    /// </summary>
    private void DebugDump()
    {
        if (_sampler == null) return;
        var c = CultureInfo.InvariantCulture;
        string F(double v) => v.ToString("0.###", c);
        string dir = Path.Combine(OS.GetUserDataDir(), "debug");
        Directory.CreateDirectory(dir);
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", c), path = Path.Combine(dir, $"dump-{stamp}.txt");
        var t = _sampler.Sample(PlayerX, PlayerZ);
        var plan = _sampler.Plan;
        var cam = _camera.GlobalPosition;
        var (ba, bb, share) = plan.Biomes.Sample(PlayerX, PlayerZ);
        var (temperature, rain) = plan.Climate.At(PlayerX, PlayerZ, Math.Max(0, t.Height - plan.Profile.SeaLevel));
        double latitude = plan.Climate.Latitude(PlayerZ);
        double fly = _player.Position.Y - Math.Max(t.Height, double.IsNaN(t.WaterLevel) ? t.Height : t.WaterLevel);
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Thornwake debug dump {DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", c)}");
        sb.AppendLine($"Version {ProjectSettings.GetSetting("application/config/version", "?")} · Godot {Engine.GetVersionInfo()["string"]} · {OS.GetName()} {OS.GetVersion()}");
        sb.AppendLine($"GPU {RenderingServer.GetVideoAdapterName()} ({RenderingServer.GetVideoAdapterVendor()}, {RenderingServer.GetVideoAdapterApiVersion()}) · window {DisplayServer.WindowGetSize()} · FPS {Engine.GetFramesPerSecond():0}");
        sb.AppendLine();
        sb.AppendLine($"World: {plan.Profile.Name} (type {_recipes[Math.Max(0, _profileList.Selected)].Id}{(_custom ? ", custom parameters" : "")}), seed {plan.Manifest.Seed}, size {F(plan.Profile.WorldSize / 1000)} km, sea level {F(plan.Profile.SeaLevel)} m");
        if (_custom) sb.AppendLine("World parameters: " + string.Join(", ", _gen.All.Select(g => $"{g.Key}={F(g.Value)}")));
        sb.AppendLine($"Player: x {F(PlayerX)}, z {F(PlayerZ)}, y {F(_player.Position.Y)} ({F(fly)} m above ground/water) · {(_fly ? (_noclip ? "fly+noclip" : "fly") : _swimming ? "swimming" : "walking")}");
        sb.AppendLine($"Ground {F(t.Height)} m · water {t.Water}{(double.IsNaN(t.WaterLevel) ? "" : $" surface {F(t.WaterLevel)} m, depth {F(t.WaterLevel - t.Height)} m")} · mountain {F(t.Mountain)}");
        sb.AppendLine($"Biome {plan.Biomes.Pool[ba].Def.Name}{(bb != ba ? $" ({F(share * 100)} %, → {plan.Biomes.Pool[bb].Def.Name})" : "")} · {F(temperature)} °C, {F(rain)} mm/yr · latitude {F(latitude)}°");
        sb.AppendLine($"Camera: yaw {F(_yaw)}, pitch {F(_pitch)}, zoom {F(_zoom)} · scene position {cam} · origin ({F(_originX)}, {F(_originZ)})");
        sb.AppendLine($"Time: {_clock.Describe()} (hour {F(_clock.Hour)})");
        sb.AppendLine(_atmosphere.DebugInfo());
        if (_water.Sim is { Added: > 0 } sim) sb.AppendLine($"Water simulation: {F(sim.Volume())} m³ on the land, {sim.Sources.Count} spring(s){(_rain ? ", raining" : "")}");
        sb.AppendLine();
        if (_net != null) { sb.AppendLine(_net.Stats.Summary(_net.Peers())); }
        sb.AppendLine("Settings:");
        foreach (var g in GameSettings.All) sb.AppendLine($"  {g.Key} = {F(g.Value)}{(g.Options != null ? $" ({g.Options[Math.Clamp((int)g.Value, 0, g.Options.Length - 1)]})" : "")}");
        sb.AppendLine();
        // The same scene for the developer's screenshot tool (view: yaw, pitch, zoom; weather follows day + hour).
        string settings = string.Join(" ", GameSettings.All.Select(g => $"--set={g.Key}={g.Value.ToString("R", c)}"));
        // A player's own world type is only on their PC: the dump carries it (replayed with --type-file).
        var recipeNow = _recipes[Math.Max(0, _profileList.Selected)];
        if (recipeNow.Custom)
        {
            sb.AppendLine("World type file:");
            sb.AppendLine(recipeNow.ToJson());
            sb.AppendLine("End of world type file");
            sb.AppendLine();
        }
        sb.AppendLine("Reproduce:");
        sb.AppendLine($"  {_worldArgs} --at={F(PlayerX)},{F(PlayerZ)} --view={F(_yaw)},{F(_pitch)},{F(_zoom)} --day={_clock.Day} --hour={F(_clock.Hour)}" +
            (_fly ? $" --fly-height={F(fly)}" : "") + (_rain ? " --rain" : "") + (_me.Torch.Lit ? " --torch" : "") +
            $" --cloud-drift={string.Join(",", _atmosphere.CloudDrift.Select(v => v.ToString("R", c)))} {settings}");
        File.WriteAllText(path, sb.ToString());
        Screenshot(Path.Combine(dir, $"dump-{stamp}.png"));
        _status = $"Debug dump saved: {path} (send me the .txt and .png)";
        if (_shotPath == null) OS.ShellOpen(dir);
        else GD.Print("DUMPED " + path);
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
        _player = new CharacterBody3D { Name = "AldenWayfarer", // the first explorer (docs/HALL_OF_FAME.md)
             CollisionLayer = 2, CollisionMask = 1, FloorSnapLength = .6f, FloorMaxAngle = Mathf.DegToRad(50) };
        _player.AddChild(new CollisionShape3D { Position = Vector3.Up * .9f, Shape = new CapsuleShape3D { Radius = .3f, Height = 1.8f } });
        _me = new Character(new Color("526b61"), _atmosphere.Wind);
        _player.AddChild(_me);
        _me.Torch.Lit = OS.GetCmdlineUserArgs().Contains("--torch");
        AddChild(_player);
        _pivot = new Node3D { Name = "CameraPivot" };
        _arm = new SpringArm3D { SpringLength = _zoom, CollisionMask = 1, Margin = .3f };
        _arm.AddExcludedObject(_player.GetRid());
        _camera = new Camera3D { Current = true, Fov = GameSettings.Fov.F, Near = .1f, Far = 90000 };
        _arm.AddChild(_camera);
        // (in the left hand, as in third person — Tom)
        _fpHold = new Node3D { Name = "FirstPersonHold", Position = new Vector3(-.36f, -.66f, -.8f), Rotation = new Vector3(-.25f, 0, .12f) };
        _camera.AddChild(_fpHold);
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
        layer.AddChild(_map);
        InitGameplay(layer);
        _map.Teleport = (x, z) =>
        {
            Teleport(x, z);
            Input.MouseMode = Input.MouseModeEnum.Captured;
            _hud.Visible = _showHud; _help.Visible = true;
            _status = $"Teleported to {x:0} , {z:0}.";
        };
        _map.Others = MapOthers;
        _map.GoTo = GoToPlayer;
        _help = new Label
        {
            Text = "WASD move · Shift sprint · Space jump/swim up · C dive · mouse look (Tab frees cursor) · wheel zoom (all the way in: first person) · R torch\n" +
                   "F fly (Space/C up/down, Shift faster, Ctrl ×6) · N noclip · Esc menu: teleport, new world, time of day\n" +
                   "Water: hold G pour where you look · J spring · , . water size · H rain · K clear\n" +
                   "Tools: 1 hands · 2 shovel · 3 pickaxe · 4 hoe · 5 terraform (dev) — left/right mouse use\n" +
                   "6 axe · 7 rod · 8 spear · 9 bow · 0 sword · E gather, use, butcher, build · I pack and crafting · B build\n" +
                   "M world map (right-click teleports) · F1 hide help · F2 hide HUD · F3 tile grid · F4 biome colours · F9 debug dump · F12 screenshot",
            AnchorTop = 1, AnchorBottom = 1, OffsetTop = -142, OffsetLeft = 14, OffsetRight = 900,
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
        _benchLabel = new Label { Visible = false, AnchorLeft = .5f, AnchorRight = .5f, OffsetLeft = -420, OffsetRight = 420, OffsetTop = 12,
            HorizontalAlignment = HorizontalAlignment.Center };
        _benchLabel.AddThemeFontSizeOverride("font_size", 18);
        _benchLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .9f));
        layer.AddChild(_benchLabel);

        _menu = new PanelContainer { Visible = false, AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .5f, AnchorBottom = .5f,
            OffsetLeft = -520, OffsetRight = 520, OffsetTop = -340, OffsetBottom = 340 };
        layer.AddChild(_menu);
        var menuColumn = new VBoxContainer();
        _menu.AddChild(menuColumn);
        var tabs = new TabContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        menuColumn.AddChild(tabs);
        // Always visible under every tab (Tom: "add close/exit in ESC menu so I don't have to Alt+F4 in fullscreen").
        var menuBar = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.End };
        menuColumn.AddChild(menuBar);
        // Set the world up alone first (New world, settings, digging), then open it to others (Tom).
        _hostButton = new Button { Text = "Host this world…", TooltipText = "Others can join this world now, on the port and under the name from the launcher's Multiplayer page." };
        _hostButton.Pressed += () => { HostHere(); ToggleMenu(false); };
        menuBar.AddChild(_hostButton);
        var resume = new Button { Text = "Resume (Esc)" };
        resume.Pressed += () => ToggleMenu(false);
        var back = new Button { Text = "Back to launcher" };
        back.Pressed += () => { Input.MouseMode = Input.MouseModeEnum.Visible; GetTree().ChangeSceneToFile("res://Game/Launcher.tscn"); };
        var quit = new Button { Text = "Quit game" };
        quit.Pressed += () => GetTree().Quit();
        AddSaveButtons(menuBar);
        menuBar.AddChild(resume); menuBar.AddChild(back); menuBar.AddChild(quit);

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
        _timeSlider = new HSlider { MinValue = 0, MaxValue = 24, Step = .05, Value = 10.5 };
        _timeSlider.ValueChanged += v => _pendingHour = v;
        box.AddChild(_timeSlider);

        // ---- New world: size, world type, seed, parameters (Auto unless changed), map preview
        var gen = new VBoxContainer { Name = "New world" };
        tabs.AddChild(gen);
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "World type" });
        _profileList = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var r in _recipes) _profileList.AddItem(r.Name);
        _profileList.ItemSelected += i => SelectType((int)i);
        _seedEdit = new LineEdit { CustomMinimumSize = new Vector2(110, 0), PlaceholderText = "seed" };
        var random = new Button { Text = "Random seed" };
        random.Pressed += () => _seedEdit.Text = ((int)(GD.Randi() % 100000)).ToString(CultureInfo.InvariantCulture);
        row.AddChild(_profileList); row.AddChild(new Label { Text = "Seed" }); row.AddChild(_seedEdit); row.AddChild(random);
        gen.AddChild(row);
        _typeInfo = new Label { Modulate = new Color(1, 1, 1, .75f), AutowrapMode = TextServer.AutowrapMode.WordSmart };
        gen.AddChild(_typeInfo);
        var columns = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        gen.AddChild(columns);
        var parameters = SettingsUi.List(_gen.All, section => { foreach (var s in _gen.All) if (s.Section == section) s.Value = s.Default; });
        parameters.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        columns.AddChild(parameters);
        var side = new VBoxContainer { CustomMinimumSize = new Vector2(400, 0) };
        columns.AddChild(side);
        _previewImage = new TextureRect { CustomMinimumSize = new Vector2(400, 400), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered, TextureFilter = CanvasItem.TextureFilterEnum.Linear };
        side.AddChild(_previewImage);
        var previewRow = new HBoxContainer();
        var previewButton = new Button { Text = "Preview map", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        previewButton.Pressed += StartPreview;
        _biomeView = new CheckButton { Text = "Show biomes" };
        _biomeView.Toggled += _ => { if (_previewPlan != null) _previewImage.Texture = ImageTexture.CreateFromImage(WorldPreview.Render(_previewPlan, 512, _biomeView.ButtonPressed)); };
        previewRow.AddChild(previewButton); previewRow.AddChild(_biomeView);
        side.AddChild(previewRow);
        _previewInfo = new Label { Text = "Choose size, type and seed; parameters on auto adapt to both. Preview the map, then generate.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(400, 0) };
        side.AddChild(_previewInfo);
        var saveRow = new HBoxContainer();
        var typeName = new LineEdit { PlaceholderText = "new world type name", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var save = new Button { Text = "Save as world type" };
        save.Pressed += () => SaveCustomType(typeName.Text);
        saveRow.AddChild(typeName); saveRow.AddChild(save);
        side.AddChild(saveRow);
        var generateRow = new HBoxContainer();
        var generate = new Button { Text = "Generate this world", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        generate.Pressed += () => { ToggleMenu(false); Generate(); };
        var reset = new Button { Text = "Reset all to auto" };
        reset.Pressed += _gen.Reset;
        generateRow.AddChild(generate); generateRow.AddChild(reset);
        gen.AddChild(generateRow);

        // ---- Settings: detail, view distance, effects, sky, camera and performance
        var settings = new VBoxContainer { Name = "Settings" };
        tabs.AddChild(settings);
        settings.AddChild(new Label { Text = "Saved automatically. Detail and distance changes rebuild the terrain in the background.", Modulate = new Color(1, 1, 1, .7f) });
        settings.AddChild(SettingsUi.List(GameSettings.All.Where(s => s.Section is not ("Audio" or "Display")), GameSettings.ResetSection));

        // ---- Display (Tom, 2026-10-11: native resolution, window modes, aspect ratios, UI scale; saved)
        tabs.AddChild(BuildDisplayTab());

        // ---- Audio (round 29, Tom)
        var audio = new VBoxContainer { Name = "Audio" };
        tabs.AddChild(audio);
        audio.AddChild(new Label { Text = "Saved automatically.", Modulate = new Color(1, 1, 1, .7f) });
        audio.AddChild(SettingsUi.List(GameSettings.All.Where(s => s.Section == "Audio"), GameSettings.ResetSection));

        // ---- Benchmark (docs/BENCHMARK.md)
        var benchBox = new VBoxContainer { Name = "Benchmark" };
        tabs.AddChild(benchBox);
        benchBox.AddChild(new Label
        {
            Text = "Runs fixed test scenes in the same world on every PC: time, weather and clouds are frozen, V-Sync and the frame limit " +
                   "are off. It measures frame times, GPU and CPU time and hitches, then what each kind of object and effect costs. " +
                   "Takes 5–20 minutes; keep the window in front and don't touch the mouse. Esc stops. Your settings are restored afterwards.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(900, 0),
        });
        var benchConfig = BenchConfig();
        var suiteRow = new HBoxContainer();
        suiteRow.AddChild(new Label { Text = "Test", CustomMinimumSize = new Vector2(120, 0) });
        var suitePick = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var suiteIds = benchConfig["suites"]!.AsObject().Select(p => p.Key).ToList();
        foreach (var (_, v) in benchConfig["suites"]!.AsObject()) suitePick.AddItem(v!["name"]!.GetValue<string>());
        suiteRow.AddChild(suitePick);
        benchBox.AddChild(suiteRow);
        var profileRow = new HBoxContainer();
        profileRow.AddChild(new Label { Text = "Quality", CustomMinimumSize = new Vector2(120, 0) });
        var profilePick = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var profileIds = new List<string> { "current" };
        profilePick.AddItem("Your current settings");
        foreach (var (k, v) in benchConfig["profiles"]!.AsObject()) { profileIds.Add(k); profilePick.AddItem(v!["name"]!.GetValue<string>()); }
        profileRow.AddChild(profilePick);
        benchBox.AddChild(profileRow);
        benchBox.AddChild(new Label
        {
            Text = "To compare PCs, use the same quality and window size (Display tab). Results are saved in the bench folder of the game's user folder " +
                   "(results.json, summary.txt, one CSV of frame times per scene) and the folder opens when done.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(900, 0), Modulate = new Color(1, 1, 1, .75f),
        });
        var benchButtons = new HBoxContainer();
        var startBench = new Button { Text = "Start benchmark", SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        startBench.Pressed += () => BenchStart(suiteIds[suitePick.Selected], profileIds[profilePick.Selected]);
        var openBench = new Button { Text = "Open results folder" };
        openBench.Pressed += () => { string d = Path.Combine(OS.GetUserDataDir(), "bench"); Directory.CreateDirectory(d); OS.ShellOpen(d); };
        benchButtons.AddChild(startBench); benchButtons.AddChild(openBench);
        benchBox.AddChild(benchButtons);
    }

    // ------------------------------------------------------------------ headless screenshot run

    private void Smoke(double delta)
    {
        _smokeTimer += delta;
        bool settled = _streamer.Settled && !_holding && !(_horizon?.Busy ?? false);
        if (!settled && (int)(_smokeTimer / 10) != (int)((_smokeTimer - delta) / 10))
            GD.Print($"SMOKE_WAIT t={_smokeTimer:0} missing={_streamer.Missing} jobs={_streamer.Pending} impostors={_streamer.Impostors.Ready} field={_streamer.Field.Settled} (missing {_streamer.Field.Missing}, jobs {_streamer.Field.Pending}) holding={_holding} horizon={_horizon?.Busy} collision={_streamer.PendingCollision}");
        if (_shotPath != null)
        {
            if (_shotWet is { } wet && !_shotRain) _atmosphere.SetWetness(wet.Ground, wet.Puddles);
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
                if (_faceSun) { var s = _atmosphere.Sun.GlobalTransform.Basis.Z; _yaw = Mathf.Atan2(-s.X, -s.Z); }
                _smokeStage = 1; _smokeTimer = 0;
            }
            else if (_smokeStage == 1 && settled)
            {
                if (_shotHeight > 0) { SetFly(true); _player.Position += Vector3.Up * _shotHeight; }
                if (_gallery) Gallery();
                if (_openMap) { _map.Open(_sampler!.Plan, PlayerX, PlayerZ, _yaw); _hud.Visible = false; _help.Visible = false; }
                if (_shotDive > 0 && !double.IsNaN(WaterSurface(PlayerX, PlayerZ, out _)))
                {
                    SetNoclip(true);
                    var p = _player.Position;
                    _player.Position = new Vector3(p.X, (float)(WaterSurface(PlayerX, PlayerZ, out _) - _shotDive), p.Z);
                }
                foreach (var (x, z, v) in _shotPours) _water.Sim?.Sources.Add(new Thornwake.Hydraulics.LocalWater.Source { X = x, Z = z, Rate = (float)(v / Math.Max(1, _shotWait * .5)) });
                if (_shotRain) _rain = true;
                _smokeStage = 2; _smokeTimer = 0; _simTime = 0;
            }
            else if (_smokeStage == 2 && _shotPours.Count > 0 && _simTime > _shotWait * .5 && _water.Sim is { } sim && sim.Sources.Count > 0) sim.Sources.Clear();
            else if (_smokeStage == 2 && _shotStrike > 0 && !_strikePlaced && _lightning != null && (settled || _smokeTimer > 240)
                && _smokeTimer > 1.5 && _simTime >= _shotWait)
            {
                _lightning.StrikeNear(_camera.GlobalPosition, -_camera.GlobalBasis.Z, _originX, _originZ, _shotStrike, hold: true);
                _strikePlaced = true; _smokeTimer = 0;
            }
            else if (_smokeStage == 2 && (settled || _smokeTimer > 240) && _smokeTimer > 1.5 && _simTime >= _shotWait)
            {
                GD.Print($"SCATTER grass={_streamer.GrassCommitted} rocks={_streamer.RocksCommitted} props={_streamer.PropsCommitted} farTrees={_streamer.FarTreesCommitted}");
                Screenshot(_shotPath);
                if (OS.GetCmdlineUserArgs().Contains("--dump")) DebugDump();
                if (_extraYaws.Count == 0) GetTree().Quit(0);
                else { _smokeStage = 3; _smokeTimer = 0; _yaw += _extraYaws[0]; }
            }
            else if (_smokeStage == 3 && _smokeTimer > 3 && settled)
            {
                _extraShot++;
                Screenshot(Path.Combine(Path.GetDirectoryName(_shotPath) ?? ".", $"{Path.GetFileNameWithoutExtension(_shotPath)}-{_extraShot}.png"));
                if (_extraShot >= _extraYaws.Count) GetTree().Quit(0);
                else { _smokeTimer = 0; _yaw += _extraYaws[_extraShot] - _extraYaws[_extraShot - 1]; }
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
            case 11:
            {
                // The player walked (stage 2) and flew (stage 9): after landing again its feet must be on the ground
                // (collision built and matching the terrain; round 30c moved it to heightmap shapes).
                SetFly(false); Teleport(PlayerX, PlayerZ); Next(); break;
            }
            case 12: if (_smokeTimer > 4 && !_holding)
            {
                double feet = _player.Position.Y - _sampler!.Height(PlayerX, PlayerZ, 1);
                bool onGround = Math.Abs(feet) < .6 || !double.IsNaN(WaterSurface(PlayerX, PlayerZ, out _));
                GD.Print($"WORLD_SMOKE_{(onGround ? "PASS" : "FAIL")} tiles={_streamer.Resident} built={_streamer.Built} maxCommitMs={_streamer.MaxCommitMs:0.0} feet={feet:0.00}");
                GetTree().Quit(onGround ? 0 : 1);
            }
            break;
        }
        if (_smokeTimer > 900) { GD.PushError("WORLD_SMOKE_TIMEOUT at stage " + _smokeStage); GetTree().Quit(1); }
        void Next() { _smokeStage++; _smokeTimer = 0; }
    }
}
