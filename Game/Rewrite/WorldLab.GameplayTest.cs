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
                var trees = Vegetation.TreesNear(_sampler, PlayerX, PlayerZ, 120, DetailConfig.For(_sampler));
                if (trees.Count == 0) trees = Vegetation.TreesNear(_sampler, PlayerX, PlayerZ, 600, DetailConfig.For(_sampler));
                if (trees.Count == 0) { Fail("no tree within 600 m"); return; }
                _gtTree = trees.OrderBy(t => (t.X - PlayerX) * (t.X - PlayerX) + (t.Z - PlayerZ) * (t.Z - PlayerZ)).First();
                Teleport(_gtTree.X + 1.6, _gtTree.Z + .4);
                Print($"tree {_gtTree.Cx},{_gtTree.Cz} at {_gtTree.X:0.0}, {_gtTree.Z:0.0} (species {Vegetation.Species[_gtTree.Species]}, size {_gtTree.Scale:0.00}); axe in pack: {_myInv.HasTag("chopping")}");
                Next(2.5); break;
            }
            case 1:
                if (!_axeHeld) ToggleAxe();
                _gtLogs = _myInv.Count("log");
                Act(new ActionRequest(ActionKind.Chop, _gtTree.Cx, _gtTree.Cz, "", 0, 0, 0, 0));
                Next(1.5); break;
            case 2:
                Check(_sampler.Removals.Contains(ObjectRemovals.Trees, _gtTree.Cx, _gtTree.Cz), "the tree is felled here");
                Check(_myInv.Count("log") > _gtLogs, $"logs in the pack: {_myInv.Count("log")}, branches {_myInv.Count("branch")} (status: {_status})");
                Check(_stumps.ContainsKey((_gtTree.Cx, _gtTree.Cz)), "a stump stands there");
                Check(!Vegetation.TreesNear(_sampler, _gtTree.X, _gtTree.Z, .5, DetailConfig.For(_sampler)).Any(), "the tree is gone from the forest");
                Act(new ActionRequest(ActionKind.Make, 0, 0, "campfire", PlayerX + 2.2, _sampler.Height(PlayerX + 2.2, PlayerZ + 1.5), PlayerZ + 1.5, 0));
                Next(1.5); break;
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
        void Print(string s) { GD.Print("GAMEPLAY " + s); _net?.Stats.Event("gameplay test: " + s); }
        void Check(bool ok, string what) { GD.Print((ok ? "GAMEPLAY ok: " : "GAMEPLAY_FAIL: ") + what); _net?.Stats.Event("gameplay test: " + (ok ? "ok " : "FAIL ") + what); }
        void Fail(string what) { GD.Print("GAMEPLAY_FAIL: " + what); _gtStage = -2; }
    }
}
