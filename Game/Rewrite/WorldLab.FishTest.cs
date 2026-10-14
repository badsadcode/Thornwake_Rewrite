using System;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// --fish-test[=png]: fishing end to end (D114) — a rod and bait in the pack (the host's), the nearest water within
/// 400 m, a cast into it, a strike at the bite, the tug-of-war played well (A/D against the pull, a quarter of a second
/// late), the fish landed through the host. FISH … lines, FISH_FAIL on a failed check, FISH_DONE and quit.
/// </summary>
public partial class WorldLab
{
    private int _ftStage = -1;
    private double _ftTimer, _ftSince;
    private int _ftFishBefore, _ftBaitBefore, _ftSeen;
    private string? _ftShot;

    private void FishTest(double delta)
    {
        if (_ftStage == -1)
        {
            var arg = OS.GetCmdlineUserArgs().FirstOrDefault(a => a == "--fish-test" || a.StartsWith("--fish-test="));
            _ftStage = arg == null ? -2 : 0;
            _ftShot = arg?.Contains('=') == true ? arg[12..] : null;
            return;
        }
        if (_ftStage < 0 || _sampler == null || _generation != null || ((IClientGame)this).LocalState() == null) return;
        if (_ftStage == 6) { PlayFight(delta); return; }
        if ((_ftTimer -= delta) > 0) return;
        switch (_ftStage)
        {
            case 0:
            {
                if (_shotAt is { } start && !_gtMoved) { _gtMoved = true; Teleport(start.X, start.Z); _ftTimer = 4; return; }
                if (_rules == null || IsNetClient) { Fail("run alone or as the host (the test fills the host's pack)"); return; }
                var inv = _rules.Inventory((int)NetServer.HostPeer);
                if (!inv.HasTag("fishing")) inv.Add("fishing_rod");
                inv.Add("bait", 3);
                _rules.InventoryChanged((int)NetServer.HostPeer);
                // The nearest water: rings outward every 4 m; stand 6 m back on dry ground, facing it.
                for (double r = 4; r < 400; r += 4)
                    for (int k = 0; k < 48; k++)
                    {
                        double a = k * Math.Tau / 48, x = PlayerX + Math.Cos(a) * r, z = PlayerZ + Math.Sin(a) * r;
                        var t = _sampler.Sample(x, z);
                        if (t.Water == WaterKind.None) continue;
                        double bx = x - Math.Cos(a) * 7, bz = z - Math.Sin(a) * 7;
                        if (_sampler.Sample(bx, bz).Water != WaterKind.None) continue;
                        Teleport(bx, bz);
                        _yaw = Mathf.Atan2(-(float)Math.Cos(a), -(float)Math.Sin(a)); _pitch = -.25f; _zoom = 5;
                        Print($"water ({Fishing.WaterName(t.Water)}) {r:0} m away; standing at {bx:0.0}, {bz:0.0}");
                        Go(1, 4); return;
                    }
                Fail("no water within 400 m");
                return;
            }
            case 1:
                Check(_myInv.HasTag("fishing") && _myInv.HasTag("bait"), "a rod and bait in the pack");
                ToggleRod();
                Check(_me.RodHeld, "the rod in hand");
                _testLmb = true; // hold to aim farther…
                Go(2, .6); break;
            case 2:
                _testLmb = false; // …and release: cast
                _ftFishBefore = _myInv.Count("raw_fish"); _ftBaitBefore = _myInv.CountTag("bait");
                Go(3, 1.2); break;
            case 3:
                Check(_fishStage == FishStage.Waiting, $"the bobber is in the water ({_fishWater}; {_status})");
                if (_fishStage != FishStage.Waiting) { Fail("the cast missed the water"); return; }
                _fishTimer = Math.Min(_fishTimer, 1.5); // (no need to wait long in a test)
                Go(4, .1); break;
            case 4:
                if (_fishStage != FishStage.Bite) { _ftTimer = .05; break; }
                Check(true, $"a bite ({_hooked?.Name})");
                _testLmb = true; // strike
                Go(5, .15); break;
            case 5:
                _testLmb = false;
                Check(_fishStage == FishStage.Fight, "the fight is on");
                if (_ftShot != null) GetTree().CreateTimer(1.2).Timeout += () => Screenshot(_ftShot);
                _ftSeen = 0; _ftSince = 0;
                Go(6, 0); break;
            case 7:
                Check(_myInv.Count("raw_fish") == _ftFishBefore + 1 && _myInv.CountTag("bait") == _ftBaitBefore - 1, $"landed and in the pack, one bait used ({_status})");
                GD.Print("FISH_DONE");
                _ftStage = -2;
                GetTree().CreateTimer(1).Timeout += () => GetTree().Quit();
                break;
        }

        void Go(int stage, double wait) { _ftStage = stage; _ftTimer = wait; }
        void Print(string s) { GD.Print("FISH " + s); _net?.Stats.Event("fish test: " + s); }
        void Check(bool ok, string what) { GD.Print((ok ? "FISH ok: " : "FISH_FAIL: ") + what); }
        void Fail(string what) { GD.Print("FISH_FAIL: " + what); _ftStage = -2; }
    }

    /// <summary>The test's hands: against the pull, a quarter of a second late at every turn.</summary>
    private void PlayFight(double delta)
    {
        if (_fight == null)
        {
            _testPull = null;
            GD.Print(_fishStage == FishStage.Idle ? "FISH ok: the fight is over" : "FISH_FAIL: the fight did not end");
            _ftStage = 7; _ftTimer = 1.5;
            return;
        }
        if (_fight.Pull != _ftSeen) { _ftSince += delta; if (_ftSince >= .25) { _ftSeen = _fight.Pull; _ftSince = 0; } }
        _testPull = -_ftSeen;
    }
}
