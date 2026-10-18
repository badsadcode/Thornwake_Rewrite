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
    private Label _needsLabel = null!;
    private PanelContainer _pack = null!;
    private ItemList _packItems = null!, _packRecipes = null!;
    private Label _packInfo = null!;

    private NetClient? GameClient => _net?.Client ?? _localClient;
    private static bool NeedsOn => GameSettings.NeedsOn.On;

    private void InitGameplay(CanvasLayer layer)
    {
        _catalog = new Catalog(Godot.FileAccess.GetFileAsString("res://Content/gameplay_items.json"),
            Godot.FileAccess.GetFileAsString("res://Content/gameplay_recipes.json"),
            Godot.FileAccess.GetFileAsString("res://Content/gameplay_animals.json"),
            Godot.FileAccess.GetFileAsString("res://Content/gameplay_pieces.json"));
        _myInv = new Inventory(_catalog);
        _needsLabel = new Label { AnchorLeft = 1, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetLeft = -420, OffsetRight = -14, OffsetTop = -40,
            HorizontalAlignment = HorizontalAlignment.Right };
        _needsLabel.AddThemeColorOverride("font_shadow_color", new Color(0, 0, 0, .8f));
        layer.AddChild(_needsLabel);
        BuildPack(layer);
        InitHunting(layer);
        InitBuilding(layer);
    }

    /// <summary>After StartNetwork: the rules on a host or alone; alone, the local server and client parts.</summary>
    private void StartGameplay()
    {
        if (IsNetClient) return;
        _rules ??= new GameRules(_catalog, this);
        _rules.Name((int)NetServer.HostPeer, GameRules.HostName); // (the host's own player in saves)
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
    (double X, double Z, double Scale, int Species)? IWorldQuery.Tree(long cx, long cz) =>
        _sampler != null && Vegetation.TreeAt(_sampler, cx, cz, DetailConfig.For(_sampler)) is { } t ? (t.X, t.Z, t.Scale, t.Species) : null;
    ObjectRemovals IWorldQuery.Removals => _sampler!.Removals;
    double IWorldQuery.Hours => _clock.Day * 24 + _clock.Hour;
    (double X, double Z, double Speed) IWorldQuery.Wind => (_atmosphere.Wind.Direction.X, _atmosphere.Wind.Direction.Y, _atmosphere.Wind.Speed);
    double IWorldQuery.Ground(double x, double z) => _sampler!.Height(x, z);
    string IWorldQuery.WaterNear(double x, double z, double radius)
    {
        // The nearest water on rings around the point (every ~3 m).
        for (double r = 0; r <= radius; r += 3)
            for (int k = 0; k < (r == 0 ? 1 : 12); k++)
            {
                double a = k * Math.Tau / 12;
                var kind = _sampler!.Sample(x + Math.Cos(a) * r, z + Math.Sin(a) * r).Water;
                if (kind != WaterKind.None) return Fishing.WaterName(kind);
            }
        return "";
    }
    (double X, double Z) IWorldQuery.GatherSpot(int layer, long cx, long cz) => GroundCover.GatherPosition(layer, cx, cz, GroundCover.CoverSeed(_sampler!));
    IReadOnlySet<string> IWorldQuery.BiomeTags(double x, double z) =>
        _sampler == null ? new HashSet<string>() : Vegetation.BiomeOf(_sampler, x, z).A.Def.Tags.ToHashSet();
    bool IWorldQuery.IsWater(double x, double z) =>
        _sampler == null || _sampler.Sample(x, z) is var t && (t.Water != WaterKind.None || t.Height < _sampler.Plan.Profile.SeaLevel + .2);
    ObjectRemovals? IWorldHost.Removals => _generation == null ? _sampler?.Removals : null;
    GameRules? IWorldHost.Rules => _rules;

    /// <summary>A new world here: nothing placed or felled carries over (inventories do: the character is the same).</summary>
    private void GameplayWorldReady()
    {
        foreach (var v in _objectViews.Values) v.QueueFree();
        _objectViews.Clear();
        foreach (var s in _stumps.Values) s.Node.QueueFree();
        _stumps.Clear();
        FellingWorldReady();
        HuntingWorldReady();
        BuildingWorldReady();
        if (_rules != null && !IsNetClient) { _rules.Objects.Clear(); _rules.Wildlife.Clear(); _seenObjects.Clear(); _seenObjects.TakeChanged(); }
        if (IsNetClient) _seenObjects.Clear();
        _gameplayHours = double.NaN;
        if (_pendingRemovals.Count > 0) { var p = _pendingRemovals.ToList(); _pendingRemovals.Clear(); ((IClientGame)this).ApplyRemovals(p); }
        if (_pendingObjects.Count > 0)
        {
            var p = _pendingObjects.ToList(); _pendingObjects.Clear();
            foreach (var data in p) ((IClientGame)this).ApplyObjects(new BinaryReader(new MemoryStream(data)));
        }
        ApplyPendingSave(); // (a loaded save: its changes on the fresh world, after the clean-up above)
    }

    private bool _dying;

    /// <summary>After dying (D115): a new character at the world's start, fed, warm and whole; the old one's pack lies
    /// where they fell.</summary>
    private void Respawn(string message)
    {
        _dying = false;
        _needs.Food = 80; _needs.Warmth = 72; _needs.Wet = 0; _needs.Health = 100;
        if (_axeHeld) ToggleAxe();
        if (_me.RodHeld) ToggleRod();
        _me.Weapon = null;
        if (_building) SetBuilding(false);
        if (_landmarks.Count > 0) Teleport(_landmarks[0].X, _landmarks[0].Z);
        _status = message + " A new life begins at the start.";
        _camShake = 1;
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
        if (_rules != null && !IsNetClient)
        {
            _rules.Step(dh * 60);
            _rules.Wildlife.Density = GameSettings.Animals.Index switch { 0 => 0, 1 => .5, 2 => 1, _ => 2 };
        }
        _seenObjects.Step(dh * 60); _seenObjects.TakeChanged(); // (the copy burns down too, for the fires drawn here)

        bool captured = Input.MouseMode == Input.MouseModeEnum.Captured && !_menu.Visible && !_pack.Visible && !_map.Visible;
        UpdateChopping(delta, captured && _axeHeld && !_me.RodHeld && !_building && Input.IsMouseButtonPressed(MouseButton.Left));
        UpdateHunting(delta, captured && !_building);
        UpdateBuilding(delta, captured);
        UpdatePieces(delta);
        if (NeedsOn)
        {
            var t = _sampler.Sample(PlayerX, PlayerZ);
            double sea = _sampler.Plan.Profile.SeaLevel;
            double mean = _sampler.Plan.Climate.At(PlayerX, PlayerZ, t.Height - sea).Temperature;
            double precip = _rain ? .7 : _atmosphere.Precipitation;
            double air = Thornwake.Gameplay.Needs.AirTemperature(mean, _sampler.Plan.Climate.Latitude(PlayerZ), GameSettings.Season.Index, _clock.Hour, precip);
            bool sprint = Input.IsKeyPressed(Key.Shift) && new Vector2(_player.Velocity.X, _player.Velocity.Z).Length() > 6;
            bool sheltered = GameRules.Sheltered(_catalog, _seenObjects, PlayerX, PlayerZ, _player.Position.Y);
            var body = new BodyInput(air, _atmosphere.Wind.Speed * (sheltered ? .3 : 1),
                sheltered ? 0 : precip * (1 - .7 * Math.Clamp(_atmosphere.Canopy, 0, 1)) * (_atmosphere.Snowing ? .4 : 1),
                _swimming, GameRules.FireWarmth(_catalog, _seenObjects, PlayerX, PlayerZ), _me.Chopping ? 1 : sprint ? .6 : 0);
            _needs.Step(dh, body);
            if (_needs.Health <= 0 && !_dying) { _dying = true; Act(new ActionRequest(ActionKind.Die, 0, 0, "", 0, 0, 0, 0)); }
            _needsLabel.Text = $"{_needs.Describe()} · feels {_needs.FeelsLike(body):0} °C" + (sheltered ? " · sheltered" : "") + (_needs.Health <= 0 ? "\nYou are dying: eat, get warm (a fire)." : "");
        }
        else _needsLabel.Text = "";
        UpdateGameplayViews(delta);
    }

    private void ToggleAxe()
    {
        if (!_axeHeld && !_myInv.HasTag("chopping")) { _status = "You have no axe."; return; }
        _axeHeld = !_axeHeld;
        _me.AxeHeld = _axeHeld;
        if (_axeHeld) _tools.Select(0); // (hands: the terrain tools stay put)
        _status = _axeHeld ? "Axe in hand: hold the left mouse button on a tree in front of you to chop it (6 puts it away)." : "Axe put away.";
    }

    /// <summary>
    /// E (Tom: E gathers and interacts; D113): the nearest of — wood lying in a pile (picked up), a campfire (fed), a
    /// berry bush with fruit (picked), a loose stone (picked up) — or else long grass where you stand (plant fibre).
    /// Gathering from nature takes a moment (moving stops it).
    /// </summary>
    private void Interact()
    {
        if (_gathering != null) return;
        if (InteractPiece()) return; // (the aimed blueprint or door)
        var near = _seenObjects.Near(PlayerX, PlayerZ, GameRules.UseReach + 1.5).Where(o => o.Contents.Count > 0 || o.Kind == "campfire").OrderBy(o => (o.X - PlayerX) * (o.X - PlayerX) + (o.Z - PlayerZ) * (o.Z - PlayerZ)).FirstOrDefault();
        if (near is { Kind: "carcass" } carcass)
        {
            if (!_myInv.HasTag("cutting")) { _status = "You need something sharp to butcher it (a knife, an axe)."; return; }
            string name = _catalog.Animals.TryGetValue(carcass.Ref, out var def) ? def.Name.ToLowerInvariant() : "animal";
            StartTimed(new ActionRequest(ActionKind.Butcher, carcass.Id, 0, "", 0, 0, 0, 0), carcass.X, carcass.Z,
                GameRules.ButcherSeconds(_catalog, _myInv, carcass.Ref), $"Butchering the {name}");
            return;
        }
        if (near != null) { Act(new ActionRequest(near.Contents.Count > 0 ? ActionKind.Gather : ActionKind.Fuel, near.Id, 0, "", 0, 0, 0, 0)); return; }
        var spot = _streamer.GatherablesNear(PlayerX, PlayerZ, GameRules.UseReach - .5).FirstOrDefault();
        if (spot.Layer != 0 || spot.X != 0 || spot.Z != 0)
        {
            bool stone = spot.Layer == ObjectRemovals.Stones;
            StartGathering(stone ? "stone" : "berries", spot.Cx, spot.Cz, spot.X, spot.Z, stone ? .8 : 1.5, stone ? "Picking up a stone" : "Picking berries");
            return;
        }
        if (GrassHere()) { StartGathering("grass", 0, 0, PlayerX, PlayerZ, 1.2, "Pulling long grass"); return; }
        _status = "Nothing here to gather or use (E).";
    }

    /// <summary>Long grass grows here: land, not bare (the biome's grass), not too steep, not under water.</summary>
    private bool GrassHere()
    {
        var t = _sampler!.Sample(PlayerX, PlayerZ);
        if (t.Water != WaterKind.None || t.Height - _sampler.Plan.Profile.SeaLevel < .5) return false;
        var (a, b, share) = Vegetation.BiomeOf(_sampler, PlayerX, PlayerZ);
        double grass = a.Def.Vegetation.Grass * share + b.Def.Vegetation.Grass * (1 - share);
        double slope = Math.Abs(_sampler.Height(PlayerX + 1, PlayerZ) - t.Height) + Math.Abs(_sampler.Height(PlayerX, PlayerZ + 1) - t.Height);
        // (and not pulled already: the patch you stand on grows back in a few days)
        long gx = (long)Math.Floor(PlayerX / ObjectRemovals.GrassCell), gz = (long)Math.Floor(PlayerZ / ObjectRemovals.GrassCell);
        return grass > .3 && slope < .9 && !_sampler.Removals.Contains(ObjectRemovals.Grass, gx, gz);
    }

    private (ActionRequest Request, double Seconds, double Left, string Label)? _gathering;

    private void StartGathering(string what, long cx, long cz, double x, double z, double seconds, string label) =>
        StartTimed(new ActionRequest(ActionKind.Forage, cx, cz, what, x, 0, z, 0), x, z, seconds, label);

    /// <summary>Something that takes a moment (gathering, butchering, building): the bar fills, then the host is asked;
    /// moving away stops it.</summary>
    private void StartTimed(ActionRequest request, double x, double z, double seconds, string label)
    {
        _gathering = (request, seconds, seconds, label);
        if (x != PlayerX || z != PlayerZ) _me.Facing = Mathf.Atan2((float)(x - PlayerX), (float)(z - PlayerZ));
    }

    /// <summary>Per frame: a gathering in progress (moving away stops it), then the host is asked.</summary>
    private void UpdateGathering(double delta)
    {
        if (_gathering is not { } g) return;
        if (new Vector2(_player.Velocity.X, _player.Velocity.Z).Length() > 1.5f) { _gathering = null; _status = "Stopped (you moved)."; return; }
        double left = g.Left - delta;
        _status = g.Label + "…";
        ShowAction(g.Label, 1 - left / g.Seconds);
        if (left > 0) { _gathering = g with { Left = left }; return; }
        _gathering = null;
        Act(g.Request);
    }

    // ---- what the host sends back
    void IClientGame.ActionDone(ActionKind kind, bool ok, string message, double food)
    {
        _status = message;
        if (ok && kind == ActionKind.Eat) _needs.Eat(food);
        if (kind == ActionKind.Die) Respawn(message);
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
            if (o == null) { ObjectGone(id); PieceGone(id); if (_objectViews.Remove(id, out var gone)) gone.QueueFree(); continue; }
            if (o.Kind == "trunk") { TrunkChanged(o); continue; } // (drawn and moved by the felling part)
            if (_objectViews.ContainsKey(id)) { if (_catalog.Piece(o.Kind) is { } changed) RefreshPiece(o, changed); continue; }
            {
                var view = _catalog.Piece(o.Kind) is { } piece ? PieceView(o, piece)
                    : o.Kind switch { "log_pile" => LogPile(o), "branch_pile" => BranchPile(o), "pack" => Pack(), "carcass" => CarcassView(o), "arrow" => StuckArrow(), _ => new Node3D() };
                view.Rotation = new Vector3(0, o.Yaw, 0);
                AddChild(view);
                _objectViews[id] = view;
                WaitForImpact(id, view, o); // (a felled tree's branches appear when its crown comes down)
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
            if (layer == ObjectRemovals.Grass)
            {
                // Grass pulled (or grown back): the tile there is drawn again, without (or with) it.
                double gx = (cx + .5) * ObjectRemovals.GrassCell, gz = (cz + .5) * ObjectRemovals.GrassCell;
                _streamer.Invalidate(gx - 1, gz - 1, gx + 1, gz + 1);
                continue;
            }
            if (layer is ObjectRemovals.Stones or ObjectRemovals.Berries)
            {
                // A stone gone, a bush picked (or its berries back): the tiles around it are drawn again.
                var (gx, gz) = GroundCover.GatherPosition(layer, cx, cz, GroundCover.CoverSeed(_sampler));
                _streamer.Invalidate(gx - 4, gz - 4, gx + 4, gz + 4);
                continue;
            }
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
        UpdateFelling(delta);
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
        _status = $"Making: {r.Name}…";
        ShowAction("Making: " + r.Name.ToLowerInvariant(), 1 - _makeTimer / r.Seconds);
        if (_makeTimer > 0) return;
        _making = null;
        // A placed object goes 1.8 m in front of you, on the ground, facing you.
        float yaw = _yaw;
        double x = PlayerX - Math.Sin(yaw) * 1.8, z = PlayerZ - Math.Cos(yaw) * 1.8;
        Act(new ActionRequest(ActionKind.Make, 0, 0, r.Id, x, _sampler!.Height(x, z), z, yaw));
    }

    /// <summary>Keys of the gameplay part; true when used.</summary>
    private bool GameplayKey(InputEventKey e)
    {
        if (!_menu.Visible && !_pack.Visible && !_map.Visible && BuildingKey(e)) return true;
        var key = e.Keycode;
        switch (key)
        {
            case Key.B when !_menu.Visible && !_pack.Visible && !_map.Visible: SetBuilding(!_building); return true;
            case Key.Key8 when !_menu.Visible && !_pack.Visible: ToggleWeapon("spear"); return true;
            case Key.Key9 when !_menu.Visible && !_pack.Visible: ToggleWeapon("bow"); return true;
            case Key.Key0 when !_menu.Visible && !_pack.Visible: ToggleWeapon("stone_sword"); return true;
            case Key.I when !_menu.Visible && !_map.Visible: TogglePack(!_pack.Visible); return true;
            case Key.Escape when _pack.Visible: TogglePack(false); return true;
            case Key.Key6 when !_menu.Visible && !_pack.Visible: if (_me.RodHeld) ToggleRod(); _me.Weapon = null; if (_building) SetBuilding(false); ToggleAxe(); return true;
            case Key.Key7 when !_menu.Visible && !_pack.Visible: _me.Weapon = null; if (_building) SetBuilding(false); ToggleRod(); return true;
            case Key.E when !_menu.Visible && !_pack.Visible: Interact(); return true;
        }
        return false;
    }

    /// <summary>A lean-to (placeholder): two forked poles, a ridge pole, a roof of poles sloping to the ground behind,
    /// branches on it. Open toward the builder (its front faces where they stood).</summary>
    private static Node3D LeanTo()
    {
        var root = new Node3D { Name = "LeanTo" };
        var wood = new StandardMaterial3D { AlbedoColor = new Color("6b4a2e"), Roughness = .95f };
        var leaf = new StandardMaterial3D { AlbedoColor = new Color("4a5f2c"), Roughness = 1 };
        MeshInstance3D Pole(float r, float length) => new() { Mesh = new CylinderMesh { TopRadius = r, BottomRadius = r * 1.1f, Height = length, RadialSegments = 6 }, MaterialOverride = wood };
        // (front = +Z toward the builder after the object's turn; the roof falls to −Z)
        foreach (float x in new[] { -1.4f, 1.4f }) { var p = Pole(.06f, 1.7f); p.Position = new Vector3(x, .85f, .9f); root.AddChild(p); }
        var ridge = Pole(.06f, 3.2f); ridge.Rotation = new Vector3(0, 0, Mathf.Pi / 2); ridge.Position = new Vector3(0, 1.65f, .9f); root.AddChild(ridge);
        float slope = Mathf.Atan2(1.65f, 1.9f), len = Mathf.Sqrt(1.65f * 1.65f + 1.9f * 1.9f);
        for (int i = 0; i < 7; i++)
        {
            var rafter = Pole(.045f, len + .2f);
            rafter.Rotation = new Vector3(Mathf.Pi / 2 - slope, 0, 0); // (up toward the ridge at the front, down to the ground behind)
            rafter.Position = new Vector3(-1.35f + i * .45f, .82f, -.05f);
            root.AddChild(rafter);
        }
        var thatch = new MeshInstance3D { Mesh = new BoxMesh { Size = new Vector3(3.1f, .08f, len) }, MaterialOverride = leaf,
            Rotation = new Vector3(-slope, 0, 0), Position = new Vector3(0, .9f, -.05f) };
        root.AddChild(thatch);
        return root;
    }

    /// <summary>A dead character's pack (placeholder: a leather bag with a strap).</summary>
    private static Node3D Pack()
    {
        var root = new Node3D { Name = "Pack" };
        var leather = new StandardMaterial3D { AlbedoColor = new Color("5b3a22"), Roughness = .8f };
        root.AddChild(new MeshInstance3D { Mesh = new CapsuleMesh { Radius = .22f, Height = .6f }, MaterialOverride = leather, Position = new Vector3(0, .2f, 0), Rotation = new Vector3(Mathf.Pi / 2, 0, .3f) });
        root.AddChild(new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = .2f, OuterRadius = .24f }, MaterialOverride = leather, Position = new Vector3(0, .25f, 0), Rotation = new Vector3(0, 0, 1.2f) });
        return root;
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

    private string GameplayHud() => _building ? BuildingHud() : _me.Weapon != null ? HuntingHud() : _me.RodHeld ? "Holding: fishing rod — hold and release LMB to cast · click when it bites · then A / D against the fish · 7 puts it away" : _axeHeld ? "Holding: stone axe — hold LMB on a tree or a trunk in front of you to chop · E: gather, pick up, feed a fire · I: pack" : "";
}
