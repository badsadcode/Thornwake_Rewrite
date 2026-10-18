using System;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// --hunt-test (D120): a deer put down in front of Alden, struck twice with the spear (a carcass), butchered; a stag shot
/// with the bow; a wolf that comes for him. --build-test (D121): a small log hut — a foundation through the ghost and
/// the left mouse, then walls (one a doorway, one swapped for a window), a thatch ridge roof and peak gables snapped on,
/// a door, a campfire — every blueprint supplied with E; inside it is sheltered and walkable; the door opens. HUNT / BUILD
/// lines, …_FAIL on a failed check, …_DONE and quit. --test-shot=path.png takes screenshots along the way (suffixed).
/// Host or single player only (the test hands out items and puts animals down through the rules).
/// </summary>
public partial class WorldLab
{
    private int _htStage = -1, _btStage = -1;
    private double _htTimer, _btTimer;
    private bool _htMoved, _btMoved, _buildTestPlace;
    private int _htTarget;
    private long _btFoundation, _btEastWall;
    private Pose _btBase;
    private V3 _btAimAt;

    /// <summary>Turn the camera so the screen's centre is on a world point (a few calls: the camera orbits as it turns).</summary>
    private void AimAt(V3 world)
    {
        var target = new Vector3((float)(world.X - _originX), (float)world.Y, (float)(world.Z - _originZ));
        var d = (target - _camera.GlobalPosition).Normalized();
        _yaw = Mathf.Atan2(-d.X, -d.Z);
        _pitch = Mathf.Asin(d.Y);
    }
    private double _htHealth;
    private (long Cx, long Cz, double X, double Y, double Z) _htTree;
    private (double X, double Z) _htStart;
    private V3 _htAim;

    private void HuntTest(double delta)
    {
        if (_htStage == -1) { _htStage = OS.GetCmdlineUserArgs().Contains("--hunt-test") ? 0 : -2; return; }
        if (_htStage < 0 || _sampler == null || _generation != null || _rules == null || ((IClientGame)this).LocalState() == null) return;
        if ((_htTimer -= delta) > 0) return;
        var inv = _rules.Inventory((int)NetServer.HostPeer);
        switch (_htStage)
        {
            case 0:
                if (_shotAt is { } start && !_htMoved) { _htMoved = true; Teleport(start.X, start.Z); _htTimer = 5; return; }
                _animalsOverride = 0; // (only the test's animals; not the saved setting)
                _htStart = (PlayerX, PlayerZ);
                _rules.Wildlife.Clear();
                inv.Add("spear"); inv.Add("bow"); inv.Add("arrow", 20); inv.Add("stone_knife");
                _rules.InventoryChanged((int)NetServer.HostPeer);
                var deer = Put("deer", 9);
                deer.Tame = true;
                _htTarget = deer.Id;
                Go(1, 2); break;
            case 1:
                Check(_animalViews.ContainsKey(_htTarget), "the deer is drawn here");
                Shot("-deer");
                Go(2, .2); break;
            case 2:
            {
                // Up to it (it is calm: the test keeps it still) and the spear.
                if (_rules.Wildlife.Get(_htTarget) is not { } d) { Fail("the deer is gone"); return; }
                d.State = AnimalState.Graze; d.Speed = 0;
                Teleport(d.X - Math.Sin(d.Yaw + 1.2) * 2.2, d.Z - Math.Cos(d.Yaw + 1.2) * 2.2);
                ToggleWeapon("spear");
                Go(3, 1); break;
            }
            case 3:
            {
                if (_rules.Wildlife.Get(_htTarget) is not { } d) { Fail("the deer is gone"); return; }
                Face(d.X, d.Z);
                _htHealth = d.Health;
                _me.Strike();
                Go(4, 1.1); break;
            }
            case 4:
            {
                var d = _rules.Wildlife.Get(_htTarget);
                Check(d != null && d.Health < _htHealth, $"the thrust hurt it ({_status})");
                if (d != null) { d.State = AnimalState.Graze; d.Speed = 0; Teleport(d.X - Math.Sin(d.Yaw + 1.2) * 2.2, d.Z - Math.Cos(d.Yaw + 1.2) * 2.2); Face(d.X, d.Z); }
                Go(5, .4); break;
            }
            case 5:
            {
                if (_rules.Wildlife.Get(_htTarget) is { } d) Face(d.X, d.Z);
                _me.Strike();
                Go(6, 1.2); break;
            }
            case 6:
                Check(_rules.Wildlife.Get(_htTarget) is null or { State: AnimalState.Dead }, $"the second thrust killed it ({_status})");
                Shot("-dying");
                Go(7, 3); break;
            case 7:
            {
                var carcass = _seenObjects.All.FirstOrDefault(o => o.Kind == "carcass");
                Check(carcass != null && _objectViews.ContainsKey(carcass.Id), "a carcass lies there, drawn");
                if (carcass == null) { Done(); return; }
                Teleport(carcass.X - 1.5, carcass.Z - 1.5);
                Face(carcass.X, carcass.Z);
                _pitch = -.5f; _zoom = 4;
                Go(8, 1.5); break;
            }
            case 8:
                Shot("-carcass");
                Interact(); // E: butcher it (a moment's work)
                Go(9, GameRules.ButcherSeconds(_catalog, _myInv, "deer") + 1.5); break;
            case 9:
                Check(_myInv.Count("raw_meat") >= 4 && _myInv.Count("hide") >= 1, $"butchered: {_myInv.Count("raw_meat")} raw meat, {_myInv.Count("hide")} hide ({_status})");
                Check(!_seenObjects.All.Any(o => o.Kind == "carcass"), "the carcass is gone");
                ToggleWeapon("bow");
                var stag = Put("stag", 18);
                stag.Tame = true;
                _htTarget = stag.Id;
                Go(10, 1.5); break;
            case 10:
            {
                if (_rules.Wildlife.Get(_htTarget) is not { } s) { Fail("the stag is gone"); return; }
                s.State = AnimalState.Graze; s.Speed = 0;
                _htHealth = s.Health;
                // The camera's aim on its flank, a little above to allow for the drop.
                double dx = s.X - PlayerX, dz = s.Z - PlayerZ, dist = Math.Sqrt(dx * dx + dz * dz);
                _yaw = Mathf.Atan2(-(float)dx, -(float)dz);
                _zoom = 3;
                _testAim = true; // (the right mouse held: aiming)
                _pitch = (float)Math.Atan2(s.Y + s.Def.Height * .55 - (_player.Position.Y + 1.6), dist) + .02f;
                _me.Facing = _yaw + Mathf.Pi;
                Go(11, .6); break;
            }
            case 11:
                _me.Draw = 1;
                Loose(1);
                _me.Draw = 0;
                Go(12, .12); break;
            case 12:
                Shot("-arrow");
                _testAim = false;
                Go(13, 1.5); break;
            case 13:
            {
                var s = _rules.Wildlife.Get(_htTarget);
                Check(s != null && s.Health < _htHealth || _seenObjects.All.Any(o => o.Kind == "arrow"), $"the arrow struck the stag or fell where it can be found ({_status}; stag {s?.Health:0} of {_htHealth:0})");
                Check(_myInv.Count("arrow") == 19, $"one arrow used: {_myInv.Count("arrow")} left");
                _rules.Wildlife.Clear();
                if (!NeedsOn) { Print("hunger and cold off: the wolf test skipped"); Done(); return; }
                _htHealth = _needs.Health;
                var wolf = Put("wolf", 7);
                wolf.State = AnimalState.Chase;
                wolf.Target = (int)NetServer.HostPeer;
                Go(14, 4); break;
            }
            case 14:
                Shot("-wolf");
                Check(_needs.Health < _htHealth, $"the wolf bit: health {_htHealth:0} → {_needs.Health:0} ({_status})");
                _rules.Wildlife.Clear();
                Go(15, .2); break;
            case 15:
            {
                // Tom (build 65): a tree hit once lost its collision. One axe hit, then is its trunk still solid?
                var tree = Vegetation.TreesNear(_sampler, PlayerX, PlayerZ, 80, DetailConfig.For(_sampler)).FirstOrDefault();
                if (tree.Scale <= 0) { Print("no tree within 80 m: the trunk check skipped"); Go(18, .2); return; }
                _htTree = (tree.Cx, tree.Cz, tree.X, tree.Y, tree.Z);
                Teleport(tree.X + 1.5, tree.Z);
                Go(16, 3); break;
            }
            case 16:
                _rules.Chop((int)NetServer.HostPeer, _htTree.Cx, _htTree.Cz, _htTree.X + 1.5, _htTree.Z);
                Go(17, 1.5); break;
            case 17:
            {
                var at = new Vector3((float)(_htTree.X - _originX), (float)_htTree.Y + 1, (float)(_htTree.Z - _originZ));
                var hits = _player.GetWorld3D().DirectSpaceState.IntersectPoint(new PhysicsPointQueryParameters3D { Position = at, CollisionMask = 1 }, 4);
                Check(_fallingTrees.ContainsKey((_htTree.Cx, _htTree.Cz)) && hits.Count > 0, $"a tree hit once is still solid ({hits.Count} colliders in its trunk)");
                Teleport(_htStart.X, _htStart.Z);
                Go(18, 3); break;
            }
            case 18:
            {
                // Tom (build 66): the arrow did not come down where he aimed. A part draw at a point on the ground 30 m away.
                if (_me.Weapon != "bow") ToggleWeapon("bow");
                var ahead = new Vector2(-Mathf.Sin(_yaw), -Mathf.Cos(_yaw));
                double tx = PlayerX + ahead.X * 30, tz = PlayerZ + ahead.Y * 30;
                _htAim = new V3(tx, _sampler.Height(tx, tz), tz);
                _testAim = true;
                Go(19, .5); break;
            }
            case 19: case 20: case 21:
                AimAt(_htAim);
                _me.Facing = _yaw + Mathf.Pi;
                Go(_htStage + 1, .3); break;
            case 22:
            {
                // (the point the crosshair is on: the camera's ray, as Loose sees it)
                var from = _camera.GlobalPosition; var look = -_camera.GlobalTransform.Basis.Z;
                var hit = GetWorld3D().DirectSpaceState.IntersectRay(PhysicsRayQueryParameters3D.Create(from, from + look * 300, 1));
                if (hit.Count > 0) { var p = hit["position"].AsVector3(); _htAim = new V3(p.X + _originX, p.Y, p.Z + _originZ); }
                _lastLanding = null;
                Loose(.6f);
                Go(23, .45); break;
            }
            case 23:
                Shot("-trail");
                Go(24, 2); break;
            case 24:
            {
                _testAim = false;
                double miss = _lastLanding is { } l ? Math.Sqrt((l.X - _htAim.X) * (l.X - _htAim.X) + (l.Z - _htAim.Z) * (l.Z - _htAim.Z)) : double.NaN;
                Check(miss < .6, $"a part-drawn arrow comes down where the crosshair was ({miss:0.00} m from it, 30 m away)");
                Done(); break;
            }
        }

        Animal Put(string kind, double ahead)
        {
            var dir = new Vector2(-Mathf.Sin(_yaw), -Mathf.Cos(_yaw));
            double x = PlayerX + dir.X * ahead, z = PlayerZ + dir.Y * ahead;
            var a = _rules!.Wildlife.Spawn(_catalog.Animals[kind], x, z);
            a.Yaw = Math.Atan2(-dir.X, -dir.Y) + 1.4; // (side on)
            Print($"{kind} put down at {x:0.0}, {z:0.0}");
            return a;
        }
        void Face(double x, double z)
        {
            _me.Facing = Mathf.Atan2((float)(x - PlayerX), (float)(z - PlayerZ));
            _yaw = _me.Facing + Mathf.Pi;
        }
        void Go(int stage, double wait) { _htStage = stage; _htTimer = wait; }
        void Print(string s) { GD.Print("HUNT " + s); }
        void Check(bool ok, string what) => GD.Print((ok ? "HUNT ok: " : "HUNT_FAIL: ") + what);
        void Fail(string what) { GD.Print("HUNT_FAIL: " + what); Done(); }
        void Done() { GD.Print("HUNT_DONE"); _htStage = -2; GetTree().CreateTimer(1).Timeout += () => GetTree().Quit(); }
        void Shot(string suffix) => TestShot(suffix);
    }

    private bool _spawned, _testAim;
    private double _censusTimer = double.NaN, _scanTestTimer = double.NaN;
    private double _guestTimer = double.NaN;
    private int _guestStage;

    /// <summary>
    /// --spawn=kind,kind…: animals put down around the player once the world is ready (host; a debug aid).
    /// --guest-check=seconds (a joined player): after that long, what the host shared — animals, pieces — counted, a
    /// campfire blueprint asked for and seen; GUEST lines, GUEST_FAIL, GUEST_DONE and quit.
    /// </summary>
    private void NetTestTools(double delta)
    {
        var args = OS.GetCmdlineUserArgs();
        if (!_spawned && _rules != null && _sampler != null && _generation == null && ((IClientGame)this).LocalState() != null
            && args.FirstOrDefault(a => a.StartsWith("--spawn=")) is { } spawn)
        {
            _spawned = true;
            var rng = new Random(3);
            foreach (var kind in spawn[8..].Split(','))
                if (_catalog.Animals.TryGetValue(kind, out var def))
                {
                    double a = rng.NextDouble() * Math.Tau, d = 8 + rng.NextDouble() * 6;
                    _rules.Wildlife.Spawn(def, PlayerX + Math.Cos(a) * d, PlayerZ + Math.Sin(a) * d);
                }
            GD.Print($"SPAWN {spawn[8..]}");
        }
        // --census=seconds: after that long, the animals around (as the HUD says), a picture, and quit.
        if (_sampler != null && _generation == null && ((IClientGame)this).LocalState() != null && args.FirstOrDefault(a => a.StartsWith("--census=")) is { } census)
        {
            if (double.IsNaN(_censusTimer))
            {
                _censusTimer = double.Parse(census[9..], System.Globalization.CultureInfo.InvariantCulture);
                if (_shotAt is { } at) Teleport(at.X, at.Z);
            }
            if ((_censusTimer -= delta) <= 0 && _censusTimer > -1e8)
            {
                _censusTimer = -1e9;
                var tags = ((IWorldQuery)this).BiomeTags(PlayerX, PlayerZ);
                GD.Print($"CENSUS biome tags here: {string.Join(", ", tags)}");
                GD.Print($"CENSUS {AnimalsHud()} · FPS {Engine.GetFramesPerSecond():0}");
                GD.Print($"CENSUS kinds: {string.Join(", ", _animalViews.Values.GroupBy(v => v.Def.Id).Select(g => $"{g.Key} {g.Count()}"))}" +
                    (_rules != null ? $"; on the host {_rules.Wildlife.All.Count}" : ""));
                TestShot("-census");
                GetTree().CreateTimer(1).Timeout += () => GetTree().Quit();
            }
        }
        // --scan-test=seconds: after that long (animals about, leaving tracks), Z, a picture of what it marks, and quit.
        if (_sampler != null && _generation == null && ((IClientGame)this).LocalState() != null && args.FirstOrDefault(a => a.StartsWith("--scan-test=")) is { } st)
        {
            if (double.IsNaN(_scanTestTimer)) { _scanTestTimer = double.Parse(st[12..], System.Globalization.CultureInfo.InvariantCulture); if (_shotAt is { } at) Teleport(at.X, at.Z); }
            if ((_scanTestTimer -= delta) <= 0 && _scanTestTimer > -1e8)
            {
                _scanTestTimer = -1e9;
                _pitch = -.45f; _zoom = 6;
                Scan();
                GD.Print($"SCAN {_status} · tracks kept {_tracks.Count} · marks {_scanMarks.Count}");
                GetTree().CreateTimer(.7).Timeout += () => TestShot("-wave");
                GetTree().CreateTimer(1.6).Timeout += () => { TestShot("-scan"); GetTree().CreateTimer(1).Timeout += () => GetTree().Quit(); };
            }
        }
        if (!IsNetClient || _sampler == null || _generation != null || ((IClientGame)this).LocalState() == null) return;
        if (double.IsNaN(_guestTimer))
        {
            if (args.FirstOrDefault(a => a.StartsWith("--guest-check=")) is not { } check) { _guestTimer = double.PositiveInfinity; return; }
            _guestTimer = double.Parse(check[14..], System.Globalization.CultureInfo.InvariantCulture);
        }
        if ((_guestTimer -= delta) > 0) return;
        switch (_guestStage)
        {
            case 0:
                GD.Print($"GUEST animals drawn: {_animalViews.Count} ({string.Join(", ", _animalViews.Values.Select(v => v.Def.Id))})");
                GD.Print($"GUEST pieces seen: {_seenObjects.All.Count(o => _catalog.Piece(o.Kind) != null)}, finished {_seenObjects.All.Count(o => _catalog.Piece(o.Kind) != null && !o.Blueprint)}");
                TestShot("-guest");
                var dir = new Vector2(-Mathf.Sin(_yaw), -Mathf.Cos(_yaw));
                double x = PlayerX + dir.X * 3, z = PlayerZ + dir.Y * 3;
                Act(new ActionRequest(ActionKind.Place, 0, 0, "campfire", x, _sampler.Height(x, z), z, 0));
                _guestStage = 1; _guestTimer = 2; break;
            case 1:
                bool ok = _seenObjects.All.Any(o => o.Kind == "campfire" && o.Blueprint && Math.Abs(o.X - PlayerX) < 6 && Math.Abs(o.Z - PlayerZ) < 6);
                GD.Print((ok ? "GUEST ok: " : "GUEST_FAIL: ") + $"my campfire blueprint, placed through the host ({_status})");
                GD.Print("GUEST_DONE");
                _guestStage = 2;
                GetTree().CreateTimer(1).Timeout += () => GetTree().Quit();
                break;
        }
    }

    private void TestShot(string suffix)
    {
        if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--test-shot=")) is { } shot) Screenshot(shot[12..].Replace(".png", suffix + ".png"));
    }

    private void BuildTest(double delta)
    {
        if (_btStage == -1) { _btStage = OS.GetCmdlineUserArgs().Contains("--build-test") ? 0 : -2; return; }
        if (_btStage < 0 || _sampler == null || _generation != null || _rules == null || ((IClientGame)this).LocalState() == null) return;
        if ((_btTimer -= delta) > 0) return;
        var inv = _rules.Inventory((int)NetServer.HostPeer);
        switch (_btStage)
        {
            case 0:
                if (_shotAt is { } start && !_btMoved) { _btMoved = true; Teleport(start.X, start.Z); _btTimer = 5; return; }
                inv.Add("log", 200); inv.Add("plant_fibre", 80); inv.Add("branch", 40);
                _rules.InventoryChanged((int)NetServer.HostPeer);
                if (!OS.GetCmdlineUserArgs().Any(a => a.StartsWith("--spawn="))) _animalsOverride = 0;
                _testingOverride = false; // (blueprints first: the player's own Testing tools setting does not count here)
                SetBuilding(true);
                _buildCategory = _catalog.Categories.ToList().IndexOf("Foundation");
                _pitch = -.55f; _zoom = 5;
                Go(1, 1); break;
            case 1:
                Check(_ghost != null && _ghost.Visible && _ghostPose != null, $"a foundation ghost where I look ({_ghostWhy})");
                Shot("-ghost");
                _buildTestPlace = true; // (the left mouse, once)
                Go(2, 1); break;
            case 2:
            {
                var f = _seenObjects.All.FirstOrDefault(o => o.Kind == "log_foundation");
                Check(f is { Blueprint: true }, $"a foundation blueprint ({_status})");
                if (f == null) { Done(); return; }
                _btFoundation = f.Id;
                _btBase = new Pose(f.X, f.Y, f.Z, f.Yaw);
                SetBuilding(false);
                Go(3, .5); break;
            }
            case 3:
                AimAt(new V3(_btBase.X, _btBase.Y - .1, _btBase.Z)); // (on the blueprint: the camera has left the shoulder)
                Go(301, .3); break;
            case 301:
                AimAt(new V3(_btBase.X, _btBase.Y - .1, _btBase.Z));
                Go(302, .3); break;
            case 302:
                Shot("-blueprint");
                Interact(); // E on the aimed blueprint
                Go(4, 2); break;
            case 4:
                Check(_seenObjects.Get(_btFoundation) is { Blueprint: false }, $"E brought the logs: built ({_status})");
                // Tom's case (build 63): a wall aimed just past the deck's edge, at the ground — it must stand on the deck.
                SetBuilding(true);
                _buildCategory = _catalog.Categories.ToList().IndexOf("Wall");
                _btAimAt = Building.ToWorld(_btBase, new V3(1.8, 0, .4));
                _btAimAt = _btAimAt with { Y = _sampler.Height(_btAimAt.X, _btAimAt.Z) };
                Teleport(Building.ToWorld(_btBase, new V3(6, 0, 1)).X, Building.ToWorld(_btBase, new V3(6, 0, 1)).Z);
                Go(41, 2); break;
            case 41: case 42: case 43:
                AimAt(_btAimAt);
                Go(_btStage + 1, .25); break;
            case 44:
                Check(_ghostPose is { } wg && Math.Abs(wg.Y - _btBase.Y) < .01 && !_ghostFree,
                    $"a wall aimed at the ground just past the deck stands on the deck (ghost at {_ghostPose?.Y:0.00}, deck {_btBase.Y:0.00}; {_ghostWhy})");
                Shot("-wallghost");
                _buildCategory = _catalog.Categories.ToList().IndexOf("Roof");
                Go(441, 2.5); break;
            case 441:
                Shot("-menu-roof");
                _buildCategory = _catalog.Categories.ToList().IndexOf("Foundation");
                _buildShape["Foundation"] = "On stilts";
                AimAt(Building.ToWorld(_btBase, new V3(6, 0, -5)) is var g ? g with { Y = _sampler.Height(g.X, g.Z) } : default);
                Go(442, 2); break;
            case 442:
                Check(_ghostPiece == "log_foundation_stilts" && _ghostFree, $"the stilts foundation's ghost stands freely ({_ghostWhy})");
                Shot("-stilts");
                SetBuilding(false);
                Go(45, .3); break;
            case 45:
                // The walls: snapped onto the foundation's four edges, as the ghost would.
                Snapped("log_wall", 1.4, 0, 0, Math.PI / 2); // (turned along the side, as a player facing it)
                Snapped("log_wall", -1.4, 0, 0, Math.PI / 2);
                Snapped("log_wall_doorway", 0, 0, 1.4);
                Snapped("log_wall", 0, 0, -1.4);
                Go(5, .6); break;
            case 5:
            {
                var walls = _seenObjects.All.Where(o => o.Kind.StartsWith("log_wall")).ToList();
                Check(walls.Count == 4, $"four wall blueprints ({walls.Count}; {_status})");
                foreach (var w in walls) Act(new ActionRequest(ActionKind.Supply, w.Id, 0, "", 0, 0, 0, 0));
                // The roof: a thatch ridge across the walls running along X (eaves on the walls at ±z), peaks on the others.
                Snapped("thatch_roof_ridge_steep", 0, 3, 1.5); // (its eaves on the walls along X, the peaks on the others)
                Go(6, .6); break;
            }
            case 6:
                Snapped("log_peak_steep", 1.5, 3, 0, Math.PI / 2);
                Snapped("log_peak_steep", -1.5, 3, 0, Math.PI / 2);
                Snapped("log_door", 0, .9, 1.5);
                Go(7, .6); break;
            case 7:
            {
                foreach (var o in _seenObjects.All.Where(o => o.Blueprint).ToList()) Act(new ActionRequest(ActionKind.Supply, o.Id, 0, "", 0, 0, 0, 0));
                // A campfire in front of the door, freely on the ground.
                var front = Building.ToWorld(_btBase, new V3(0, 0, 4.5));
                Act(new ActionRequest(ActionKind.Place, 0, 0, "campfire", front.X, _sampler.Height(front.X, front.Z), front.Z, 0));
                Go(8, .6); break;
            }
            case 8:
            {
                foreach (var o in _seenObjects.All.Where(o => o.Blueprint).ToList()) Act(new ActionRequest(ActionKind.Supply, o.Id, 0, "", 0, 0, 0, 0));
                var kinds = _seenObjects.All.Where(o => _catalog.Piece(o.Kind) != null).Select(o => o.Kind).ToList();
                Print("pieces: " + string.Join(", ", kinds));
                Check(kinds.Contains("thatch_roof_ridge_steep") && kinds.Count(k => k == "log_peak_steep") == 2 && kinds.Contains("log_door") && kinds.Contains("campfire"),
                    $"the roof, two peaks, the door, the campfire ({_status})");
                // Outside, looking at the front from a corner.
                var eye = Building.ToWorld(_btBase, new V3(5, 0, 7));
                Teleport(eye.X, eye.Z);
                Go(9, 2.5); break;
            }
            case 9:
            {
                var c = Building.ToWorld(_btBase, new V3(0, 1.5, 0));
                _yaw = Mathf.Atan2(-(float)(c.X - PlayerX), -(float)(c.Z - PlayerZ)); _pitch = -.15f; _zoom = 6;
                Check(!_seenObjects.All.Any(o => o.Blueprint), "everything built");
                Go(10, 1.2); break;
            }
            case 10:
                Shot("-hut");
                // In through the door: stand inside, on the floor (collision), sheltered.
                var inside = Building.ToWorld(_btBase, new V3(.3, 0, -.3));
                _player.Position = new Vector3((float)(inside.X - _originX), (float)_btBase.Y + 1f, (float)(inside.Z - _originZ));
                _player.Velocity = Vector3.Zero;
                Go(11, 1.5); break;
            case 11:
                Check(Math.Abs(_player.Position.Y - _btBase.Y) < .35, $"standing on the floor inside: feet at {_player.Position.Y:0.00}, the floor at {_btBase.Y:0.00}");
                Check(GameRules.Sheltered(_catalog, _seenObjects, PlayerX, PlayerZ, _player.Position.Y), "sheltered under the roof");
                Act(new ActionRequest(ActionKind.Use, _seenObjects.All.First(o => o.Kind == "log_door").Id, 0, "", 0, 0, 0, 0));
                Go(12, 1); break;
            case 12:
            {
                Check(_seenObjects.All.First(o => o.Kind == "log_door").State == 1, "the door opened");
                var c = Building.ToWorld(_btBase, new V3(0, 1.4, 1.5));
                _yaw = Mathf.Atan2(-(float)(c.X - PlayerX), -(float)(c.Z - PlayerZ)); _pitch = -.05f; _zoom = 2.5f;
                Go(13, 1.2); break;
            }
            case 13:
            {
                Shot("-inside");
                // Swap the east wall for a window wall (aiming at its middle with the window shape, as a player would).
                var east = _seenObjects.All.First(o => o.Kind == "log_wall" && Building.ToWorld(_btBase, new V3(1.5, 0, 0)) is var e && Math.Abs(o.X - e.X) < .1 && Math.Abs(o.Z - e.Z) < .1);
                _btEastWall = east.Id;
                Act(new ActionRequest(ActionKind.Place, east.Id, 0, "log_wall_window", 0, 0, 0, east.Yaw));
                var eye = Building.ToWorld(_btBase, new V3(7, 0, -3));
                Teleport(eye.X, eye.Z); // (outside: on the ground)
                Go(14, 2); break;
            }
            case 14:
            {
                Check(_seenObjects.Get(_btEastWall) == null && _seenObjects.All.Any(o => o.Kind == "log_wall_window" && !o.Blueprint), $"the east wall is a window wall now ({_status})");
                var c = Building.ToWorld(_btBase, new V3(0, 1.8, 0));
                _yaw = Mathf.Atan2(-(float)(c.X - PlayerX), -(float)(c.Z - PlayerZ)); _pitch = -.12f; _zoom = 5;
                Go(15, 1.2); break;
            }
            case 15:
            {
                Shot("-window");
                // A second, 6 × 6 m house beside it: four foundations, eight walls, a hip roof of four log corners.
                var at = Building.ToWorld(_btBase, new V3(0, 0, -10));
                double y = _sampler.Height(at.X, at.Z) + _catalog.Pieces["log_foundation"].Lift;
                Act(new ActionRequest(ActionKind.Place, 0, 0, "log_foundation", at.X, y, at.Z, (float)_btBase.Yaw));
                _btBase = new Pose(at.X, y, at.Z, _btBase.Yaw);
                var stand = Building.ToWorld(_btBase, new V3(1.5, 0, 6)); // (within reach of all of it)
                Teleport(stand.X, stand.Z);
                Go(16, 2); break;
            }
            case 16:
                Snapped("log_foundation", 3.2, 0, .2);
                Snapped("log_foundation", .2, 0, 3.2);
                Go(17, .5); break;
            case 17:
                Snapped("log_foundation", 3.2, 0, 3.2);
                Go(18, .5); break;
            case 18:
                // (the house's frame: its centre is the shared corner at local (1.5, 1.5))
                foreach (var (lx, lz) in new[] { (-1.4, 0.0), (-1.4, 3.0), (4.4, 0.0), (4.4, 3.0), (0.0, -1.4), (3.0, -1.4), (0.0, 4.4), (3.0, 4.4) })
                    Snapped(lx == 0 && lz == 4.4 ? "log_wall_doorway" : lx == 4.4 && lz == 0 ? "log_wall_window" : "log_wall", lx, 0, lz,
                        Math.Abs(lx) > 1 && Math.Abs(lz % 3) < .01 ? Math.PI / 2 : 0);
                Go(19, .6); break;
            case 19:
                foreach (var o in _seenObjects.All.Where(o => o.Blueprint).ToList()) Act(new ActionRequest(ActionKind.Supply, o.Id, 0, "", 0, 0, 0, 0));
                // Each corner of the roof aimed at near its house corner's wall top, facing in.
                Snapped("log_roof_outer_steep", -1.2, 3, -1.2, Math.PI / 4);
                Go(20, .5); break;
            case 20:
                Snapped("log_roof_outer_steep", 4.2, 3, -1.2, -Math.PI / 4);
                Go(201, .5); break;
            case 201:
                Snapped("log_roof_outer_steep", -1.2, 3, 4.2, Math.PI * 3 / 4);
                Go(202, .5); break;
            case 202:
                Snapped("log_roof_outer_steep", 4.2, 3, 4.2, -Math.PI * 3 / 4);
                Go(21, .6); break;
            case 21:
            {
                foreach (var o in _seenObjects.All.Where(o => o.Blueprint).ToList()) Act(new ActionRequest(ActionKind.Supply, o.Id, 0, "", 0, 0, 0, 0));
                int corners = _seenObjects.All.Count(o => o.Kind == "log_roof_outer_steep");
                var apexes = _seenObjects.All.Where(o => o.Kind == "log_roof_outer_steep")
                    .Select(o => Building.ToWorld(new Pose(o.X, o.Y, o.Z, o.Yaw), new V3(1.5, 3, 1.5))).ToList();
                var centre = Building.ToWorld(_btBase, new V3(1.5, 6, 1.5));
                Check(corners == 4 && apexes.All(p => p.DistanceTo(centre) < .05), $"a hip roof: {corners} corners meeting at the top");
                var eye = Building.ToWorld(_btBase, new V3(-6, 0, 9));
                Teleport(eye.X, eye.Z);
                Go(22, 2.5); break;
            }
            case 22:
            {
                var c = Building.ToWorld(_btBase, new V3(1.5, 2.5, 1.5));
                _yaw = Mathf.Atan2(-(float)(c.X - PlayerX), -(float)(c.Z - PlayerZ)); _pitch = -.2f; _zoom = 7;
                Check(!_seenObjects.All.Any(o => o.Blueprint), "the second house built");
                Go(23, 1.2); break;
            }
            case 23:
                Shot("-house");
                // A lone foundation in the open, then a campfire on its deck (Tom: not only on the ground).
                var spot = Building.ToWorld(_btBase, new V3(-9, 0, 2));
                var fp = Building.Free(_catalog.Pieces["log_foundation"], spot, _btBase.Yaw, 0, (x, z) => _sampler.Height(x, z));
                Act(new ActionRequest(ActionKind.Place, 0, 0, "log_foundation", fp.X, fp.Y, fp.Z, (float)fp.Yaw));
                _btBase = fp;
                Go(231, .8); break;
            case 231:
                foreach (var o in _seenObjects.All.Where(o => o.Blueprint).ToList()) Act(new ActionRequest(ActionKind.Supply, o.Id, 0, "", 0, 0, 0, 0));
                var deck = Building.ToWorld(_btBase, new V3(.4, 0, .4));
                Act(new ActionRequest(ActionKind.Place, 0, 0, "campfire", deck.X, _btBase.Y, deck.Z, 0));
                Go(24, .8); break;
            case 24:
            {
                var fire = _seenObjects.All.FirstOrDefault(o => o.Kind == "campfire" && Math.Abs(o.Y - _btBase.Y) < .01);
                Check(fire != null, $"a campfire on the deck ({_status})");
                if (fire == null) { Done(); return; }
                // Building open, aiming at it: right mouse → its menu; Tab → choosing with the mouse.
                SetBuilding(true);
                Teleport(Building.ToWorld(_btBase, new V3(0, 0, 6)).X, Building.ToWorld(_btBase, new V3(0, 0, 6)).Z);
                _btAimAt = new V3(fire.X, fire.Y + .4, fire.Z);
                Go(25, 2); break;
            }
            case 25: case 26: case 27: case 271: case 272:
                AimAt(_btAimAt);
                Go(_btStage switch { 27 => 271, 271 => 272, 272 => 28, _ => _btStage + 1 }, .25); break;
            case 28:
            {
                var fire = _seenObjects.All.First(o => o.Kind == "campfire" && Math.Abs(o.Y - _btBase.Y) < .01);
                Check(_aimedAny == fire.Id, $"the campfire can be aimed at (aimed {_aimedAny}, fire {fire.Id})");
                OpenPieceMenu(fire.Id);
                Go(29, .6); break;
            }
            case 29:
                Shot("-piecemenu");
                ClosePieceMenu(true);
                SetPicking(true);
                Go(30, .8); break;
            case 30:
                Shot("-picking");
                SetPicking(false);
                Dismantle(); // (the aimed campfire)
                Go(31, .8); break;
            case 31:
                Check(!_seenObjects.All.Any(o => o.Kind == "campfire" && Math.Abs(o.Y - _btBase.Y) < .01), $"the campfire taken down by aiming at it ({_status})");
                SetBuilding(false);
                // A diagonal wall across the lone foundation and a triangle foundation beside it (D135).
                Snapped("log_wall_diagonal", .2, 0, .2, Math.PI / 4);
                Snapped("log_foundation_triangle", 3.4, 0, -.6);
                Go(32, .8); break;
            case 32:
            {
                foreach (var o in _seenObjects.All.Where(o => o.Blueprint).ToList()) Act(new ActionRequest(ActionKind.Supply, o.Id, 0, "", 0, 0, 0, 0));
                Check(_seenObjects.All.Any(o => o.Kind == "log_wall_diagonal") && _seenObjects.All.Any(o => o.Kind == "log_foundation_triangle"), $"a diagonal wall and a triangle foundation ({_status})");
                var view = Building.ToWorld(_btBase, new V3(-3, 0, 7));
                Teleport(view.X, view.Z);
                Go(33, 2.5); break;
            }
            case 33:
            {
                var c = Building.ToWorld(_btBase, new V3(1.5, 1, 0));
                _yaw = Mathf.Atan2(-(float)(c.X - PlayerX), -(float)(c.Z - PlayerZ)); _pitch = -.35f; _zoom = 6;
                Go(34, 1.2); break;
            }
            case 34:
                Shot("-diagonal");
                Done(); break;
        }

        // A piece snapped as the ghost would snap it when aiming at (lx, ly, lz) of the foundation's frame.
        void Snapped(string piece, double lx, double ly, double lz, double turn = 0)
        {
            var def = _catalog.Pieces[piece];
            var aim = Building.ToWorld(_btBase, new V3(lx, ly, lz));
            var near = GameRules.PiecesNear(_catalog, _seenObjects, aim.X, aim.Z, 12).ToList();
            if (Building.Snap(def, aim, _btBase.Yaw + turn, near) is not { } s) { Check(false, $"{piece} found nothing to snap to"); return; }
            Act(new ActionRequest(ActionKind.Place, 0, 0, piece, s.Pose.X, s.Pose.Y, s.Pose.Z, (float)s.Pose.Yaw));
        }
        void Go(int stage, double wait) { _btStage = stage; _btTimer = wait; }
        void Print(string s) { GD.Print("BUILD " + s); }
        void Check(bool ok, string what) => GD.Print((ok ? "BUILD ok: " : "BUILD_FAIL: ") + what);
        void Done() { GD.Print("BUILD_DONE"); _btStage = -2; if (!OS.GetCmdlineUserArgs().Contains("--test-stay")) GetTree().CreateTimer(1).Timeout += () => GetTree().Quit(); }
        void Shot(string suffix) => TestShot(suffix);
    }
}
