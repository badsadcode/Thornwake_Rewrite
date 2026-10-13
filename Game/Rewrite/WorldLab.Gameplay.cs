using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;
using Thornwake.World;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Step 1's placeholder mechanics in the game (GAMEPLAY_DESIGN.md §14 step 1, §15; D110). The rules run in the server
/// part (GameRules, on the host); this PC asks through its client part and draws what comes back — in single player too,
/// where the same server and client parts talk without a network, so there is one path for everyone.
/// Controls (Tom): what you hold decides what the left mouse does — with the axe (6) a tree in front of you is chopped
/// (later a weapon attacks); E interacts and gathers (now: wood on a campfire); I opens the pack and crafting.
/// </summary>
public partial class WorldLab : IWorldQuery
{
    private Catalog _catalog = null!;
    private GameRules? _rules;                          // the true state (host and single player)
    private NetServer? _localServer;                    // single player: the server and client parts without a network
    private NetClient? _localClient;
    private Inventory _myInv = null!;                   // this player's inventory as the host sends it
    private readonly Needs _needs = new();
    private readonly PlacedObjects _seenObjects = new(); // the placed objects as the host sends them (drawn from these)
    private readonly Dictionary<long, Node3D> _objectViews = new();
    private readonly Dictionary<(long, long), (Node3D Node, double X, double Y, double Z)> _stumps = new();
    private readonly List<(int Layer, long X, long Z, double? RemovedAt)> _pendingRemovals = new();
    private readonly List<byte[]> _pendingObjects = new();
    private bool _axeHeld;
    private double _gameplayHours = double.NaN;
    private Vegetation.LatticeTree? _chopTarget;
    private bool _chopHooked;
    /// <summary>Trees this PC draws itself while they shake and fall (D111).</summary>
    private readonly Dictionary<(long, long), FallingTree> _fallingTrees = new();
    private readonly Dictionary<long, double> _pileShowAt = new();

    private sealed class FallingTree
    {
        public required Node3D Pivot;   // at the tree's base (moved with the floating origin)
        public required Node3D Model;
        public double X, Y, Z, Height;
        public float Shake, Angle, Speed, MaxAngle, Lying = -1, Sink;
        public Vector3 Axis;
        public int Bounces, Hits;
        public bool Falling;
    }
    private Label _needsLabel = null!;
    private PanelContainer _pack = null!;
    private ItemList _packItems = null!, _packRecipes = null!;
    private Label _packInfo = null!;

    private NetClient? GameClient => _net?.Client ?? _localClient;
    private static bool NeedsOn => GameSettings.NeedsOn.On;

    private void InitGameplay(CanvasLayer layer)
    {
        _catalog = new Catalog(Godot.FileAccess.GetFileAsString("res://Content/gameplay_items.json"),
            Godot.FileAccess.GetFileAsString("res://Content/gameplay_recipes.json"));
        _myInv = new Inventory(_catalog);
        _needsLabel = new Label { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -420, OffsetRight = -14, OffsetTop = -40,
            HorizontalAlignment = HorizontalAlignment.Right };
        _needsLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .8f));
        layer.AddChild(_needsLabel);
        BuildPack(layer);
    }

    /// <summary>After StartNetwork: the rules on a host or alone; alone, the local server and client parts.</summary>
    private void StartGameplay()
    {
        if (IsNetClient) return;
        _rules ??= new GameRules(_catalog, this);
        _rules.Inventory((int)NetServer.HostPeer); // (this player's start kit, sent with the first changes)
        if (_net == null)
        {
            string version = (string)ProjectSettings.GetSetting("application/config/version", "?");
            _localServer = new NetServer(this, (peer, data, _) => { if (peer == NetServer.HostPeer) _localClient?.Receive(data); }, version);
            _localClient = new NetClient(this, (data, _) => _localServer?.Receive(NetServer.HostPeer, data), isHost: true);
        }
    }

    /// <summary>Hosting starts (Esc → Host this world): the network's server and client parts take over from the local ones.</summary>
    private void StopLocalGameplayLoop() { _localServer = null; _localClient = null; }

    // ---- IWorldQuery (the rules ask about the generated world)
    (double X, double Z, double Scale)? IWorldQuery.Tree(long cx, long cz) =>
        _sampler != null && Vegetation.TreeAt(_sampler, cx, cz, DetailConfig.For(_sampler)) is { } t ? (t.X, t.Z, t.Scale) : null;
    ObjectRemovals IWorldQuery.Removals => _sampler!.Removals;
    double IWorldQuery.Hours => _clock.Day * 24 + _clock.Hour;
    (double X, double Z, double Speed) IWorldQuery.Wind => (_atmosphere.Wind.Direction.X, _atmosphere.Wind.Direction.Y, _atmosphere.Wind.Speed);
    double IWorldQuery.Ground(double x, double z) => _sampler!.Height(x, z);
    ObjectRemovals? IWorldHost.Removals => _generation == null ? _sampler?.Removals : null;
    GameRules? IWorldHost.Rules => _rules;

    /// <summary>A new world here: nothing placed or felled carries over (inventories do: the character is the same).</summary>
    private void GameplayWorldReady()
    {
        foreach (var v in _objectViews.Values) v.QueueFree();
        _objectViews.Clear();
        foreach (var s in _stumps.Values) s.Node.QueueFree();
        _stumps.Clear();
        foreach (var f in _fallingTrees.Values) f.Pivot.QueueFree();
        _fallingTrees.Clear();
        _pileShowAt.Clear();
        if (_rules != null && !IsNetClient) { _rules.Objects.Clear(); _seenObjects.Clear(); _seenObjects.TakeChanged(); }
        if (IsNetClient) _seenObjects.Clear();
        _gameplayHours = double.NaN;
        if (_pendingRemovals.Count > 0) { var p = _pendingRemovals.ToList(); _pendingRemovals.Clear(); ((IClientGame)this).ApplyRemovals(p); }
        if (_pendingObjects.Count > 0)
        {
            var p = _pendingObjects.ToList(); _pendingObjects.Clear();
            foreach (var data in p) ((IClientGame)this).ApplyObjects(new BinaryReader(new MemoryStream(data)));
        }
    }

    private void Act(ActionRequest a)
    {
        if (GameClient is { } c) c.SendAction(a);
        else _status = "Not in the world yet.";
    }

    /// <summary>Per frame: the local server part, time for the rules and the body, chopping, the views, the needs line.</summary>
    private void UpdateGameplay(double delta)
    {
        if (_sampler == null || _generation != null) { _needsLabel.Text = ""; return; }
        _localServer?.Tick(delta, ((IClientGame)this).LocalState());
        _localClient?.Tick(delta);
        double hours = _clock.Day * 24 + _clock.Hour;
        double dh = double.IsNaN(_gameplayHours) || hours < _gameplayHours || hours - _gameplayHours > 2 ? 0 : hours - _gameplayHours;
        _gameplayHours = hours;
        if (_rules != null && !IsNetClient) _rules.Step(dh * 60);
        _seenObjects.Step(dh * 60); _seenObjects.TakeChanged(); // (the copy burns down too, for the fires drawn here)

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured && !_menu.Visible && !_pack.Visible && !_map.Visible;
        UpdateChopping(delta, captured && _axeHeld && Input.IsMouseButtonPressed(MouseButton.Left));
        if (NeedsOn)
        {
            var t = _sampler.Sample(PlayerX, PlayerZ);
            double sea = _sampler.Plan.Profile.SeaLevel;
            double mean = _sampler.Plan.Climate.At(PlayerX, PlayerZ, t.Height - sea).Temperature;
            double precip = _rain ? .7 : _atmosphere.Precipitation;
            double air = Thornwake.Gameplay.Needs.AirTemperature(mean, _sampler.Plan.Climate.Latitude(PlayerZ), GameSettings.Season.Index, _clock.Hour, precip);
            bool sprint = Input.IsKeyPressed(Key.Shift) && new Vector2(_player.Velocity.X, _player.Velocity.Z).Length() > 6;
            var body = new BodyInput(air, _atmosphere.Wind.Speed, precip * (1 - .7 * Math.Clamp(_atmosphere.Canopy, 0, 1)) * (_atmosphere.Snowing ? .4 : 1),
                _swimming, GameRules.FireWarmth(_catalog, _seenObjects, PlayerX, PlayerZ), _me.Chopping ? 1 : sprint ? .6 : 0);
            _needs.Step(dh, body);
            _needsLabel.Text = $"{_needs.Describe()} · feels {_needs.FeelsLike(body):0} °C" + (_needs.Health <= 0 ? "\nYou are dying: eat, get warm (a fire)." : "");
        }
        else _needsLabel.Text = "";
        UpdateGameplayViews(delta);
    }

    // ---- chopping (Tom: like Valheim — impacts, the tree shakes, then falls; D111): the axe held (6), the left mouse
    // held on a tree in front of you; every stroke that lands is one hit, sent to the host, which counts them.
    private void UpdateChopping(double delta, bool swinging)
    {
        if (!_chopHooked) { _me.ChopHit += OnChopHit; _chopHooked = true; }
        if (!swinging) { _chopTarget = null; _me.Chopping = false; return; }
        var config = DetailConfig.For(_sampler!);
        var forward = -_camera.GlobalTransform.Basis.Z;
        var flat = new Vector2(forward.X, forward.Z).Normalized();
        // The tree in front: within reach, the nearest one inside a cone around where you look (one already falling
        // here does not count).
        Vegetation.LatticeTree? best = null; double bestScore = double.MaxValue;
        foreach (var tree in Vegetation.TreesNear(_sampler!, PlayerX, PlayerZ, GameRules.ChopReach, config))
        {
            if (_fallingTrees.TryGetValue((tree.Cx, tree.Cz), out var f) && f.Falling) continue;
            var to = new Vector2((float)(tree.X - PlayerX), (float)(tree.Z - PlayerZ));
            float d = to.Length();
            if (d > .3f && to.Normalized().Dot(flat) < .5f) continue; // more than 60° off
            if (d < bestScore) { bestScore = d; best = tree; }
        }
        if (best is not { } target) { _me.Chopping = false; _chopTarget = null; _status = "No tree within reach in front of you."; return; }
        _chopTarget = target;
        _me.Chopping = true;
        _me.Facing = Mathf.Atan2((float)(target.X - PlayerX), (float)(target.Z - PlayerZ));
        StandAlone(target); // (drawn on its own from the first swing: it will shake)
    }

    private void OnChopHit()
    {
        if (_chopTarget is not { } t || !_axeHeld) return;
        Chips(t);
        Act(new ActionRequest(ActionKind.Chop, t.Cx, t.Cz, "", t.X, t.Y, t.Z, 0));
    }

    /// <summary>The tree of the cell drawn on its own (a proxy), left out of the forest's batches (rebuilt around it).</summary>
    private FallingTree StandAlone(Vegetation.LatticeTree t)
    {
        if (_fallingTrees.TryGetValue((t.Cx, t.Cz), out var known)) return known;
        var pivot = new Node3D { Name = $"Felling{t.Cx}_{t.Cz}" };
        var model = _streamer.Field.Proxy(t.Species, t.Scale, t.Yaw);
        pivot.AddChild(model);
        AddChild(pivot);
        var f = new FallingTree { Pivot = pivot, Model = model, X = t.X, Y = t.Y, Z = t.Z, Height = GameRules.TreeHeight(t.Scale) };
        _fallingTrees[(t.Cx, t.Cz)] = f;
        _sampler!.Hidden.Remove(ObjectRemovals.Trees, t.Cx, t.Cz, 0);
        _streamer.Invalidate(t.X - 8, t.Z - 8, t.X + 8, t.Z + 8);
        PlaceFalling(f);
        return f;
    }

    void IClientGame.TreeHit(in TreeEvent e)
    {
        if (_sampler == null || _generation != null) return;
        if (Vegetation.TreeAt(_sampler, e.Cx, e.Cz, DetailConfig.For(_sampler)) is not { } t) return;
        var f = StandAlone(t);
        f.Shake = 1;
        f.Hits++;
        if (!e.Felled || f.Falling) return;
        // It falls toward Dir: tipping over its base about the horizontal axis across that direction.
        var dir = new Vector3((float)e.DirX, 0, (float)e.DirZ).Normalized();
        f.Falling = true;
        f.Axis = Vector3.Up.Cross(dir).Normalized();
        f.Angle = .05f; f.Speed = .25f; // (the last cut gives it a nudge: no long, slow start)
        // It stops where the trunk meets the ground (higher ground ahead stops it sooner).
        double rise = _sampler.Height(t.X + dir.X * f.Height * .7, t.Z + dir.Z * f.Height * .7) - t.Y;
        f.MaxAngle = Mathf.Pi / 2 - (float)Math.Atan2(rise, f.Height * .7) - .03f;
    }

    /// <summary>Wood chips flying off the trunk where the axe lands.</summary>
    private void Chips(Vegetation.LatticeTree t)
    {
        var to = new Vector3((float)(PlayerX - t.X), 0, (float)(PlayerZ - t.Z)).Normalized();
        var mesh = new BoxMesh { Size = new Vector3(.05f, .015f, .03f), Material = new StandardMaterial3D { AlbedoColor = new Color("c9a46a"), Roughness = .9f } };
        var chips = new CpuParticles3D
        {
            Emitting = true, OneShot = true, Amount = 14, Lifetime = .9, Explosiveness = .95f, LocalCoords = false,
            Mesh = mesh, Direction = to + Vector3.Up * .6f, Spread = 35,
            InitialVelocityMin = 1.5f, InitialVelocityMax = 3.5f, Gravity = new Vector3(0, -9.8f, 0),
            AngularVelocityMin = -400, AngularVelocityMax = 400,
            Position = new Vector3((float)(t.X - _originX) + to.X * .3f, (float)t.Y + 1.0f, (float)(t.Z - _originZ) + to.Z * .3f),
        };
        AddChild(chips);
        GetTree().CreateTimer(1.5).Timeout += chips.QueueFree;
    }

    /// <summary>Per frame: shakes die down; falling trees tip over faster and faster (a trunk pivoting on its base:
    /// θ'' = 3g / 2L · sin θ), crash with a small bounce, lie a moment, then sink away as their logs appear.</summary>
    private void UpdateFallingTrees(double delta)
    {
        float dt = (float)delta;
        foreach (var (key, f) in _fallingTrees.ToList())
        {
            f.Shake = Mathf.MoveToward(f.Shake, 0, dt * 2.2f);
            if (f.Falling && f.Lying < 0)
            {
                f.Speed += 1.5f * 9.81f / (float)f.Height * Mathf.Sin(Math.Max(f.Angle, .02f)) * dt;
                f.Angle += f.Speed * dt;
                if (f.Angle >= f.MaxAngle)
                {
                    f.Angle = f.MaxAngle;
                    if (f.Bounces++ < 2 && f.Speed > .4f) f.Speed = -f.Speed * .22f; // the crash: a small bounce
                    else { f.Speed = 0; f.Lying = 0; }
                }
            }
            else if (f.Lying >= 0)
            {
                f.Lying += dt;
                if (f.Lying > 2.5f) f.Sink += dt * .9f; // then it sinks away and its logs are there
                if (f.Sink > 2) { f.Pivot.QueueFree(); _fallingTrees.Remove(key); continue; }
            }
            PlaceFalling(f);
        }
    }

    private void PlaceFalling(FallingTree f)
    {
        f.Pivot.Position = new Vector3((float)(f.X - _originX), (float)f.Y - f.Sink, (float)(f.Z - _originZ));
        var basis = f.Falling ? new Basis(f.Axis, f.Angle) : Basis.Identity;
        // A hit shakes the crown: a quick, dying sway about the trunk's base.
        if (f.Shake > 0)
            basis = basis * new Basis(Vector3.Right, .025f * f.Shake * Mathf.Sin((float)_flicker * 38)) * new Basis(Vector3.Forward, .02f * f.Shake * Mathf.Sin((float)_flicker * 31 + 1));
        f.Pivot.Basis = basis;
    }

    private void ToggleAxe()
    {
        if (!_axeHeld && !_myInv.HasTag("chopping")) { _status = "You have no axe."; return; }
        _axeHeld = !_axeHeld;
        _me.AxeHeld = _axeHeld;
        if (_axeHeld) _tools.Select(0); // (hands: the terrain tools stay put)
        _status = _axeHeld ? "Axe in hand: hold the left mouse button on a tree in front of you to chop it (6 puts it away)." : "Axe put away.";
    }

    /// <summary>E: the nearest placed object within reach — a fire takes wood. (Gathering grass, berries and stones
    /// comes here later; Tom.)</summary>
    private void Interact()
    {
        var near = _seenObjects.Near(PlayerX, PlayerZ, GameRules.UseReach + 1.5).OrderBy(o => (o.X - PlayerX) * (o.X - PlayerX) + (o.Z - PlayerZ) * (o.Z - PlayerZ)).FirstOrDefault();
        if (near == null) { _status = "Nothing here to use (E)."; return; }
        Act(new ActionRequest(near.Contents.Count > 0 ? ActionKind.Gather : ActionKind.Fuel, near.Id, 0, "", 0, 0, 0, 0));
    }

    // ---- what the host sends back
    void IClientGame.ActionDone(ActionKind kind, bool ok, string message, double food)
    {
        _status = message;
        if (ok && kind == ActionKind.Eat) _needs.Eat(food);
        if (_pack.Visible) RefreshPack();
    }

    void IClientGame.ApplyInventory(BinaryReader r)
    {
        _myInv.Read(r);
        if (_axeHeld && !_myInv.HasTag("chopping")) { _axeHeld = false; _me.AxeHeld = false; }
        if (_pack.Visible) RefreshPack();
    }

    void IClientGame.ApplyObjects(BinaryReader r)
    {
        if (_sampler == null || _generation != null)
        {
            // (the world is still being built: keep the message for when it is ready)
            var rest = new MemoryStream(); r.BaseStream.CopyTo(rest); _pendingObjects.Add(rest.ToArray()); return;
        }
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            var (id, o) = _seenObjects.Read(r);
            if (o == null) { if (_objectViews.Remove(id, out var gone)) gone.QueueFree(); continue; }
            if (!_objectViews.ContainsKey(id))
            {
                var view = o.Kind switch { "campfire" => Visuals.Campfire(), "log_pile" => LogPile(o), _ => new Node3D() };
                // A felled tree's logs appear once it has come down here (a late joiner sees them at once).
                if (o.Kind == "log_pile" && _fallingTrees.Values.Any(f => f.Falling && (f.X - o.X) * (f.X - o.X) + (f.Z - o.Z) * (f.Z - o.Z) < 15 * 15))
                    _pileShowAt[id] = _flicker + 4.5;
                view.Rotation = new Vector3(0, o.Yaw, 0);
                AddChild(view);
                _objectViews[id] = view;
            }
        }
        _seenObjects.TakeChanged();
    }

    void IClientGame.ApplyRemovals(IReadOnlyList<(int Layer, long X, long Z, double? RemovedAt)> removals)
    {
        if (_sampler == null || _generation != null) { _pendingRemovals.AddRange(removals); return; }
        var config = DetailConfig.For(_sampler);
        foreach (var (layer, cx, cz, at) in removals)
        {
            _sampler.Removals.Set(layer, cx, cz, at); // (on the host the same set: already there)
            if (layer != ObjectRemovals.Trees || Vegetation.TreeAt(_sampler, cx, cz, config) is not { } tree) continue;
            // The tree goes (tiles, tree field, impostors, collision rebuilt around it); a stump stays.
            _streamer.Invalidate(tree.X - 8, tree.Z - 8, tree.X + 8, tree.Z + 8);
            if (at.HasValue && !_stumps.ContainsKey((cx, cz)))
            {
                var stump = Visuals.Stump(tree.Scale * .9f);
                stump.Rotation = new Vector3(0, tree.Yaw, 0);
                AddChild(stump);
                _stumps[(cx, cz)] = (stump, tree.X, tree.Y + .1, tree.Z);
                _net?.Stats.Event(FormattableString.Invariant($"tree {cx},{cz} felled (at {tree.X:0.0}, {tree.Z:0.0})"));
            }
            else if (!at.HasValue && _stumps.Remove((cx, cz), out var s)) s.Node.QueueFree();
        }
    }

    private double _flicker;

    private void UpdateGameplayViews(double delta)
    {
        _flicker += delta;
        foreach (var (id, view) in _objectViews)
        {
            if (_seenObjects.Get(id) is not { } o) continue;
            view.Position = new Vector3((float)(o.X - _originX), (float)o.Y, (float)(o.Z - _originZ));
            foreach (var child in view.GetChildren())
                if (child is Node3D n && n.Name.ToString().StartsWith("Flame")) n.Visible = o.Lit;
            if (view.GetNodeOrNull<OmniLight3D>("Firelight") is { } light)
            {
                light.Visible = o.Lit;
                // Flicker (placeholder): a few out-of-step waves; a dying fire (under 10 minutes of wood) dims.
                float f = (float)(.82 + .1 * Math.Sin(_flicker * 13.1 + id) + .06 * Math.Sin(_flicker * 23.7 + id * 2) + .05 * Math.Sin(_flicker * 5.3));
                light.LightEnergy = 1.6f * f * (float)Math.Clamp(o.Fuel / 10, .3, 1);
            }
        }
        foreach (var (pileId, at) in _pileShowAt.ToList())
            if (_objectViews.TryGetValue(pileId, out var pile)) { pile.Visible = _flicker >= at; if (pile.Visible) _pileShowAt.Remove(pileId); }
            else _pileShowAt.Remove(pileId);
        UpdateFallingTrees(delta);
        foreach (var s in _stumps.Values) s.Node.Position = new Vector3((float)(s.X - _originX), (float)s.Y, (float)(s.Z - _originZ));
    }

    // ---- the pack (I): inventory, eating, crafting
    private void BuildPack(CanvasLayer layer)
    {
        _pack = new PanelContainer { Visible = false, AnchorLeft = .5f, AnchorRight = .5f, AnchorTop = .5f, AnchorBottom = .5f,
            OffsetLeft = -430, OffsetRight = 430, OffsetTop = -270, OffsetBottom = 270 };
        layer.AddChild(_pack);
        var columns = new HBoxContainer();
        columns.AddThemeConstantOverride("separation", 16);
        _pack.AddChild(columns);
        var left = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        var right = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        columns.AddChild(left); columns.AddChild(right);
        left.AddChild(new Label { Text = "Pack (I or Esc closes)" });
        _packItems = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 300) };
        _packItems.ItemSelected += _ => ShowItem();
        left.AddChild(_packItems);
        var itemButtons = new HBoxContainer();
        var eat = new Button { Text = "Eat" };
        eat.Pressed += () => { if (SelectedItem() is { } d) Act(new ActionRequest(ActionKind.Eat, 0, 0, d.Id, 0, 0, 0, 0)); };
        var hold = new Button { Text = "Hold / put away the axe (6)" };
        hold.Pressed += ToggleAxe;
        itemButtons.AddChild(eat); itemButtons.AddChild(hold);
        left.AddChild(itemButtons);
        right.AddChild(new Label { Text = "Make" });
        _packRecipes = new ItemList { SizeFlagsVertical = Control.SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 300) };
        _packRecipes.ItemSelected += _ => ShowRecipe();
        _packRecipes.ItemActivated += _ => MakeSelected();
        right.AddChild(_packRecipes);
        var make = new Button { Text = "Make it" };
        make.Pressed += MakeSelected;
        right.AddChild(make);
        _packInfo = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, CustomMinimumSize = new Vector2(820, 60) };
        var outer = new VBoxContainer();
        _pack.RemoveChild(columns);
        _pack.AddChild(outer);
        outer.AddChild(columns);
        outer.AddChild(_packInfo);
    }

    private void TogglePack(bool open)
    {
        _pack.Visible = open;
        Input.MouseMode = open ? Input.MouseModeEnum.Visible : Input.MouseModeEnum.Captured;
        if (open) RefreshPack();
    }

    private void RefreshPack()
    {
        int item = _packItems.GetSelectedItems() is { Length: > 0 } si ? si[0] : -1, recipe = _packRecipes.GetSelectedItems() is { Length: > 0 } sr ? sr[0] : -1;
        _packItems.Clear();
        foreach (var (id, count) in _myInv.Stacks) _packItems.AddItem($"{_catalog.Item(id).Name}{(count > 1 ? $" × {count}" : "")}");
        if (item >= 0 && item < _packItems.ItemCount) _packItems.Select(item);
        _packRecipes.Clear();
        foreach (var r in _catalog.Recipes.Values)
        {
            string? missing = _rules != null && !IsNetClient ? _rules.Missing(_myInv, r, PlayerX, PlayerZ) : MissingHere(r);
            _packRecipes.AddItem($"{r.Name}  ({string.Join(", ", r.Inputs.Select(i => $"{i.Count} {(i.Item != null ? _catalog.Item(i.Item).Name.ToLowerInvariant() : i.Tag)}"))})");
            _packRecipes.SetItemCustomFgColor(_packRecipes.ItemCount - 1, missing == null ? Colors.White : new Color(1, 1, 1, .45f));
        }
        if (recipe >= 0 && recipe < _packRecipes.ItemCount) _packRecipes.Select(recipe);
        _packInfo.Text = $"Carrying {_myInv.Weight:0.0} kg. " + (NeedsOn ? _needs.Describe() : "");
    }

    /// <summary>A joined player's own check (the host checks again): its copy of the pack and the fires it sees.</summary>
    private string? MissingHere(RecipeDef r)
    {
        foreach (var tool in r.Tools) if (!_myInv.HasTag(tool)) return tool;
        foreach (var input in r.Inputs) if (_myInv.Count(input) < input.Count) return "inputs";
        if (r.Station == "fire" && GameRules.FireWarmth(_catalog, _seenObjects, PlayerX, PlayerZ) <= 0) return "fire";
        return null;
    }

    private ItemDef? SelectedItem() =>
        _packItems.GetSelectedItems() is { Length: > 0 } s && s[0] < _myInv.Stacks.Count ? _catalog.Item(_myInv.Stacks[s[0]].Item) : null;

    private RecipeDef? SelectedRecipe() =>
        _packRecipes.GetSelectedItems() is { Length: > 0 } s && s[0] < _catalog.Recipes.Count ? _catalog.Recipes.Values.ElementAt(s[0]) : null;

    private void ShowItem() { if (SelectedItem() is { } d) _packInfo.Text = $"{d.Name}: {d.Description}" + (d.Food > 0 ? $" (food {d.Food:0})" : ""); }

    private void ShowRecipe()
    {
        if (SelectedRecipe() is not { } r) return;
        string? missing = _rules != null && !IsNetClient ? _rules.Missing(_myInv, r, PlayerX, PlayerZ) : null;
        _packInfo.Text = $"{r.Name}: {r.Description}".TrimEnd(' ', ':') + $" Takes {r.Seconds:0} s." + (missing != null ? $"\n{missing}" : "");
    }

    private double _makeTimer;
    private RecipeDef? _making;

    private void MakeSelected()
    {
        if (SelectedRecipe() is not { } r) { _packInfo.Text = "Choose something to make."; return; }
        _making = r; _makeTimer = r.Seconds;
        TogglePack(false);
        _status = $"Making: {r.Name}…";
    }

    /// <summary>Per frame: a recipe being made takes its time (moving away cancels it), then the host is asked.</summary>
    private void UpdateMaking(double delta)
    {
        if (_making is not { } r) return;
        if (new Vector2(_player.Velocity.X, _player.Velocity.Z).Length() > 1.5f) { _making = null; _status = "Stopped making it (you moved)."; return; }
        _makeTimer -= delta;
        _status = $"Making: {r.Name}… {Math.Clamp(1 - _makeTimer / r.Seconds, 0, 1) * 100:0} %";
        if (_makeTimer > 0) return;
        _making = null;
        // A placed object goes 1.8 m in front of you, on the ground, facing you.
        float yaw = _yaw;
        double x = PlayerX - Math.Sin(yaw) * 1.8, z = PlayerZ - Math.Cos(yaw) * 1.8;
        Act(new ActionRequest(ActionKind.Make, 0, 0, r.Id, x, _sampler!.Height(x, z), z, yaw));
    }

    /// <summary>Keys of the gameplay part; true when used.</summary>
    private bool GameplayKey(Key key)
    {
        switch (key)
        {
            case Key.I when !_menu.Visible && !_map.Visible: TogglePack(!_pack.Visible); return true;
            case Key.Escape when _pack.Visible: TogglePack(false); return true;
            case Key.Key6 when !_menu.Visible && !_pack.Visible: ToggleAxe(); return true;
            case Key.E when !_menu.Visible && !_pack.Visible: Interact(); return true;
        }
        return false;
    }

    /// <summary>A felled tree's wood (placeholder): logs lying along where the trunk fell, a few branches on top.</summary>
    private static Node3D LogPile(PlacedObject o)
    {
        var root = new Node3D { Name = "LogPile" };
        var bark = new StandardMaterial3D { AlbedoColor = new Color("6b4a2e"), Roughness = .95f };
        var cut = new StandardMaterial3D { AlbedoColor = new Color("c9a46a"), Roughness = .9f };
        int logs = o.Contents.Where(c => c.Item == "log").Sum(c => c.Count);
        for (int i = 0; i < Math.Max(1, logs); i++)
        {
            var log = new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .16f, BottomRadius = .17f, Height = 1.7f, RadialSegments = 8 }, MaterialOverride = bark };
            log.Rotation = new Vector3(Mathf.Pi / 2, 0, 0); // lying along the fall
            log.Position = new Vector3((i % 3 - 1) * .36f + (i / 3) * .18f, .17f + (i / 3) * .3f, (i % 2) * .25f);
            root.AddChild(log);
            foreach (float end in new[] { -.86f, .86f })
                log.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .15f, BottomRadius = .15f, Height = .02f, RadialSegments = 8 }, MaterialOverride = cut, Position = new Vector3(0, end, 0) });
        }
        for (int i = 0; i < 4; i++)
            root.AddChild(new MeshInstance3D { Mesh = new CylinderMesh { TopRadius = .025f, BottomRadius = .035f, Height = 1.1f, RadialSegments = 5 }, MaterialOverride = bark,
                Position = new Vector3(-.4f + i * .27f, .5f, -.3f + (i % 2) * .5f), Rotation = new Vector3(1.4f, i * .7f, .3f) });
        return root;
    }

    private string GameplayHud() => _axeHeld ? "Holding: stone axe — hold LMB on a tree in front of you to chop · E: pick up wood, feed a fire · I: pack" : "";
}
