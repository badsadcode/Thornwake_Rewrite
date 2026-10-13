using System;
using System.Globalization;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// --gameplay-test[=png]: step 1's slice end to end through the real path (client part → server part → rules → back):
/// go to the nearest tree, chop it, build a campfire, put a log on it, eat; each step printed as GAMEPLAY …, failures as
/// GAMEPLAY_FAIL; a picture of the stump and the fire, then GAMEPLAY_DONE and quit. Works alone, hosting or joined.
/// </summary>
public partial class WorldLab
{
    private int _gtStage = -1;
    private double _gtTimer;
    private Vegetation.LatticeTree _gtTree;
    private int _gtLogs;
    private string? _gtShot;
    private bool _gtMoved;
    private Vector3 _gtPushFrom;
    private double _gtHealth;

    private void GameplayTest(double delta)
    {
        if (_gtStage == -1)
        {
            if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a == "--gameplay-test" || a.StartsWith("--gameplay-test=")) is not { } arg) { _gtStage = -2; return; }
            _gtShot = arg.Contains('=') ? arg[16..] : null;
            _gtStage = 0;
        }
        if (_gtStage < 0 || _sampler == null || _generation != null || ((IClientGame)this).LocalState() == null) return;
        _gtTimer -= delta;
        if (_gtTimer > 0) return;
        switch (_gtStage)
        {
            case 0:
            {
                // (--at=x,z: start from there, e.g. a forest)
                if (_shotAt is { } start && !_gtMoved) { _gtMoved = true; Teleport(start.X, start.Z); _gtTimer = 3; return; }
                var trees = Vegetation.TreesNear(_sampler, PlayerX, PlayerZ, 120, DetailConfig.For(_sampler));
                if (trees.Count == 0) trees = Vegetation.TreesNear(_sampler, PlayerX, PlayerZ, 600, DetailConfig.For(_sampler));
                if (trees.Count == 0) { Fail("no tree within 600 m"); return; }
                _gtTree = trees.OrderBy(t => (t.X - PlayerX) * (t.X - PlayerX) + (t.Z - PlayerZ) * (t.Z - PlayerZ)).First();
                Teleport(_gtTree.X + 1.6, _gtTree.Z + .4);
                Print($"tree {_gtTree.Cx},{_gtTree.Cz} at {_gtTree.X:0.0}, {_gtTree.Z:0.0} (species {Vegetation.Species[_gtTree.Species]}, size {_gtTree.Scale:0.00}); axe in pack: {_myInv.HasTag("chopping")}");
                Next(2.5); break;
            }
            case 1:
                Check(_myInv.HasTag("chopping"), "the start kit arrived (an axe in the pack)");
                if (!_axeHeld) ToggleAxe();
                _gtLogs = 0;
                Go(10, 0); break;
            case 10: // one hit per stroke (D111, D112) until it falls
                if (_sampler.Removals.Contains(ObjectRemovals.Trees, _gtTree.Cx, _gtTree.Cz))
                {
                    Check(_gtLogs == GameRules.TreeHits(_gtTree.Scale), $"felled after {_gtLogs} hits ({_status})");
                    Check(_fallingTrees.TryGetValue((_gtTree.Cx, _gtTree.Cz), out var f) && f.Falling, "it is falling");
                    _yaw = Mathf.Atan2(-(float)(_gtTree.X - PlayerX), -(float)(_gtTree.Z - PlayerZ)) + 1.2f; _pitch = -.15f; _zoom = 9;
                    Go(11, 1.0); break;
                }
                // (every hit so far came back as a shake: alone at once, joined a round trip later)
                if (_gtLogs > 0) Check(_fallingTrees.TryGetValue((_gtTree.Cx, _gtTree.Cz), out var shaking) && shaking.Hits == _gtLogs, $"hit {_gtLogs}: it shook");
                if (_gtLogs == 3) Check(_fallingTrees.TryGetValue((_gtTree.Cx, _gtTree.Cz), out var notched) && notched.Notch != null, "a notch on its side");
                if (_gtLogs == 3 && _gtShot != null) { _yaw = Mathf.Atan2(-(float)(_gtTree.X - PlayerX), -(float)(_gtTree.Z - PlayerZ)) + 2.2f; _pitch = .05f; _zoom = 9; }
                if (_gtLogs == 5 && _gtShot != null) Screenshot(_gtShot.Replace(".png", "-notch.png"));
                // Notch done: finish from behind (it falls toward the notch, where we stood).
                if (_gtLogs == GameRules.TreeHits(_gtTree.Scale) - 2)
                {
                    var away = new Vector2((float)(_gtTree.X - PlayerX), (float)(_gtTree.Z - PlayerZ)).Normalized();
                    Teleport(_gtTree.X + away.X * 1.5, _gtTree.Z + away.Y * 1.5);
                    _gtHealth = _needs.Health;
                }
                Act(new ActionRequest(ActionKind.Chop, _gtTree.Cx, _gtTree.Cz, "", 0, 0, 0, 0));
                _gtLogs++;
                Go(10, .45); break;
            case 11:
                if (_gtShot != null) Screenshot(_gtShot.Replace(".png", "-fall.png"));
                Go(12, 4); break;
            case 12:
            {
                Check(!_fallingTrees.ContainsKey((_gtTree.Cx, _gtTree.Cz)), "it came down (the crown broke up)");
                Check(_needs.Health >= _gtHealth, $"standing behind it, it did not hit me (health {_needs.Health:0})");
                Check(_stumps.ContainsKey((_gtTree.Cx, _gtTree.Cz)), "a stump stands there");
                Check(!Vegetation.TreesNear(_sampler, _gtTree.X, _gtTree.Z, .5, DetailConfig.For(_sampler)).Any(), "the tree is gone from the forest");
                var trunk = _trunks.Values.FirstOrDefault();
                Check(trunk is { Waiting: false } && trunk.Body.Visible, "a bare trunk lies there");
                Check(trunk is { Owned: true } && !trunk.Body.Freeze, "this PC moves it (physics on)");
                var branches = _seenObjects.All.FirstOrDefault(o => o.Kind == "branch_pile");
                Check(branches != null && _objectViews.TryGetValue(branches.Id, out var bv) && bv.Visible, "the crown's branches lie there");
                if (_gtShot != null) Screenshot(_gtShot.Replace(".png", "-trunk.png"));
                // A push (as walking into it would): it moves, and the host hears where it went.
                // (across the log: it rolls)
                if (trunk != null) { _gtPushFrom = trunk.Body.GlobalPosition; trunk.Body.ApplyCentralImpulse(trunk.Body.Basis.Y.Cross(Vector3.Up).Normalized() * 300); }
                Go(13, 2); break;
            }
            case 13:
            {
                var trunk = _trunks.Values.FirstOrDefault();
                float moved = trunk == null ? 0 : trunk.Body.GlobalPosition.DistanceTo(_gtPushFrom);
                Check(moved > .1f, $"pushed, it moved {moved:0.00} m");
                if (trunk != null && _rules != null && !IsNetClient && _rules.Objects.Get(trunk.Id) is { } truth)
                    Check(new Vector3((float)(truth.X - _originX), (float)truth.Y, (float)(truth.Z - _originZ)).DistanceTo(trunk.Body.GlobalPosition) < .3f, "the host knows where it is now");
                if (trunk != null) { var p = trunk.Body.GlobalPosition; Teleport(p.X + _originX + 1.5, p.Z + _originZ + .5); }
                _gtLogs = 0;
                Go(14, 2.5); break;
            }
            case 14: // cut it up
            {
                var trunk = _trunks.Values.FirstOrDefault();
                if (trunk == null)
                {
                    Check(_gtLogs == GameRules.TrunkHits(_gtTree.Scale), $"cut into logs after {_gtLogs} hits ({_status})");
                    var pile = _seenObjects.All.FirstOrDefault(o => o.Kind == "log_pile");
                    Check(pile != null && _objectViews.ContainsKey(pile.Id), "the logs lie there");
                    if (pile != null) Teleport(pile.X - 1, pile.Z - 1);
                    Go(15, 2.5); break;
                }
                Act(new ActionRequest(ActionKind.ChopTrunk, trunk.Id, 0, "", 0, 0, 0, 0));
                _gtLogs++;
                Go(14, .45); break;
            }
            case 15:
                Interact(); // E: the logs
                Go(16, 1.5); break;
            case 16:
            {
                Check(_myInv.Count("log") == GameRules.TrunkLogs(_gtTree.Scale), $"picked up {_myInv.Count("log")} logs (size {_gtTree.Scale:0.00}: {GameRules.TrunkLogs(_gtTree.Scale)}) ({_status})");
                var branches = _seenObjects.All.FirstOrDefault(o => o.Kind == "branch_pile");
                if (branches != null) Teleport(branches.X - 1, branches.Z - 1);
                Go(17, 2.5); break;
            }
            case 17:
                Interact(); // E: the branches
                Go(18, 1.5); break;
            case 18:
                Check(_myInv.Count("branch") >= 4, $"and {_myInv.Count("branch")} branches ({_status})");
                Act(new ActionRequest(ActionKind.Make, 0, 0, "campfire", PlayerX + 2.2, _sampler.Height(PlayerX + 2.2, PlayerZ + 1.5), PlayerZ + 1.5, 0));
                Go(3, 1.5); break;
            case 3:
            {
                var fire = _seenObjects.All.Where(o => o.Kind == "campfire").OrderBy(o => (o.X - PlayerX) * (o.X - PlayerX) + (o.Z - PlayerZ) * (o.Z - PlayerZ)).FirstOrDefault();
                Check(fire is { Lit: true }, $"a lit campfire ({_status})");
                Check(fire != null && _objectViews.ContainsKey(fire.Id), "drawn here");
                if (fire != null) Act(new ActionRequest(ActionKind.Fuel, fire.Id, 0, "", 0, 0, 0, 0));
                Next(1.5); break;
            }
            case 4:
            {
                var fire = _seenObjects.All.Where(o => o.Kind == "campfire").OrderBy(o => (o.X - PlayerX) * (o.X - PlayerX) + (o.Z - PlayerZ) * (o.Z - PlayerZ)).FirstOrDefault();
                Check(fire != null && fire.Fuel > 90, $"wood on it: {fire?.Fuel:0} min ({_status})");
                double food = _needs.Food;
                Act(new ActionRequest(ActionKind.Eat, 0, 0, "dried_meat", 0, 0, 0, 0));
                _gtLogs = (int)Math.Round(food);
                Next(1.5); break;
            }
            case 5:
                Check(_needs.Food > _gtLogs, $"ate: food {_gtLogs} → {_needs.Food:0} ({_status})");
                Check(GameRules.FireWarmth(_catalog, _seenObjects, PlayerX, PlayerZ) > 0, $"warm by the fire: {GameRules.FireWarmth(_catalog, _seenObjects, PlayerX, PlayerZ):0.0} °C");
                // A view of the stump and the fire from a little behind.
                _yaw = Mathf.Atan2(-(float)(_gtTree.X - PlayerX), -(float)(_gtTree.Z - PlayerZ)) - .5f; _pitch = -.35f; _zoom = 6;
                Next(4); break;
            case 6:
                if (_gtShot != null) Screenshot(_gtShot);
                Print(_needs.Describe());
                GD.Print("GAMEPLAY_DONE");
                _gtStage = -2;
                GetTree().CreateTimer(1).Timeout += () => GetTree().Quit();
                break;
        }

        void Next(double wait) { _gtStage++; _gtTimer = wait; }
        void Go(int stage, double wait) { _gtStage = stage; _gtTimer = wait; }
        void Print(string s) { GD.Print("GAMEPLAY " + s); _net?.Stats.Event("gameplay test: " + s); }
        void Check(bool ok, string what) { GD.Print((ok ? "GAMEPLAY ok: " : "GAMEPLAY_FAIL: ") + what); _net?.Stats.Event("gameplay test: " + (ok ? "ok " : "FAIL ") + what); }
        void Fail(string what) { GD.Print("GAMEPLAY_FAIL: " + what); _gtStage = -2; }
    }
}
