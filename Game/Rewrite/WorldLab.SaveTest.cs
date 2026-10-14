using System;
using System.IO;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// --save-test: saving and loading end to end (D116) — fell the nearest tree and pick up a stone, save, walk 300 m away,
/// load: the world is built again and the felled tree (stump, trunk), the stone, the pack, the clock and the place come
/// back. SAVE … lines, SAVE_FAIL on a failed check, SAVE_DONE and quit. Alone (or hosting).
/// </summary>
public partial class WorldLab
{
    private int _svStage = -1;
    private double _svTimer;
    private Vegetation.LatticeTree _svTree;
    private (long Cx, long Cz) _svStone;
    private int _svDay, _svStones;
    private double _svX, _svZ, _svPhase;

    private void SaveTest(double delta)
    {
        if (_svStage == -1) { _svStage = OS.GetCmdlineUserArgs().Contains("--save-test") ? 0 : -2; return; }
        if (_svStage < 0 || _sampler == null || _generation != null || ((IClientGame)this).LocalState() == null) return;
        if ((_svTimer -= delta) > 0) return;
        string path = Path.Combine(SaveFolder, "savetest.twsave");
        switch (_svStage)
        {
            case 0:
            {
                if (_shotAt is { } start && !_gtMoved) { _gtMoved = true; Teleport(start.X, start.Z); _svTimer = 4; return; }
                var trees = Vegetation.TreesNear(_sampler, PlayerX, PlayerZ, 120, DetailConfig.For(_sampler));
                if (trees.Count == 0) { Fail("no tree near"); return; }
                _svTree = trees.OrderBy(t => (t.X - PlayerX) * (t.X - PlayerX) + (t.Z - PlayerZ) * (t.Z - PlayerZ)).First();
                Teleport(_svTree.X + 1.6, _svTree.Z + .4);
                Go(1, 3); break;
            }
            case 1:
                if (_sampler.Removals.Contains(ObjectRemovals.Trees, _svTree.Cx, _svTree.Cz)) { Go(2, 5); break; }
                Act(new ActionRequest(ActionKind.Chop, _svTree.Cx, _svTree.Cz, "", 0, 0, 0, 0));
                Go(1, .3); break;
            case 2:
            {
                var stone = _streamer.GatherablesNear(PlayerX, PlayerZ, 60).FirstOrDefault(g => g.Layer == ObjectRemovals.Stones);
                if (stone.Layer != ObjectRemovals.Stones) { Fail("no stone near"); return; }
                _svStone = (stone.Cx, stone.Cz);
                Teleport(stone.X - .9, stone.Z - .9);
                Go(3, 3); break;
            }
            case 3:
                Interact();
                Go(4, 2); break;
            case 4:
                _svStones = _myInv.Count("stone");
                Check(_svStones > 0 && _sampler.Removals.Contains(ObjectRemovals.Stones, _svStone.Cx, _svStone.Cz), "a stone picked up");
                _svX = PlayerX; _svZ = PlayerZ; _svDay = _clock.Day; _svPhase = _clock.Phase;
                if (File.Exists(path)) File.Delete(path);
                SaveWorld("savetest");
                Check(File.Exists(path), $"saved ({new FileInfo(path).Length} bytes)");
                Teleport(PlayerX + 300, PlayerZ);
                Go(5, 3); break;
            case 5:
                LoadSave(path);
                Go(6, 2); break;
            case 6:
                if (_generation != null || _pendingSave != null) { _svTimer = .5; break; }
                Go(7, 6); break;
            case 7:
            {
                Check(_sampler.Removals.Contains(ObjectRemovals.Trees, _svTree.Cx, _svTree.Cz), "the felled tree is still down");
                Check(_stumps.ContainsKey((_svTree.Cx, _svTree.Cz)), "its stump stands");
                Check(_trunks.Count > 0 && _seenObjects.All.Any(o => o.Kind == "trunk"), "its trunk lies there");
                Check(_sampler.Removals.Contains(ObjectRemovals.Stones, _svStone.Cx, _svStone.Cz), "the stone is still gone");
                Check(!_streamer.GatherablesNear(_svX, _svZ, 60).Any(g => g.Layer == ObjectRemovals.Stones && g.Cx == _svStone.Cx && g.Cz == _svStone.Cz), "and not drawn");
                Check(_myInv.Count("stone") == _svStones && _myInv.HasTag("chopping"), $"the pack ({_myInv.Count("stone")} stones)");
                Check(Math.Abs(PlayerX - _svX) < 2 && Math.Abs(PlayerZ - _svZ) < 2, $"back where I was ({PlayerX - _svX:0.0}, {PlayerZ - _svZ:0.0})");
                Check(_clock.Day == _svDay && Math.Abs(_clock.Phase - _svPhase) < .01, "the same time of day");
                GD.Print("SAVE_DONE");
                _svStage = -2;
                GetTree().CreateTimer(1).Timeout += () => GetTree().Quit();
                break;
            }
        }

        void Go(int stage, double wait) { _svStage = stage; _svTimer = wait; }
        void Check(bool ok, string what) => GD.Print((ok ? "SAVE ok: " : "SAVE_FAIL: ") + what);
        void Fail(string what) { GD.Print("SAVE_FAIL: " + what); _svStage = -2; }
    }
}
