using System;
using System.Linq;
using Godot;
using Thornwake.Gameplay;
using Thornwake.Rewrite.Net;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// --gather-test: E-gathering end to end (D113) — the nearest loose stone and berry bush on the tiles drawn: walk up, E,
/// check the pack and that they are gone from the world; then long grass twice and a fishing rod. GATHER … lines,
/// GATHER_FAIL on a failed check, GATHER_DONE and quit. (Use --at=x,z for a place with bushes: forest edges.)
/// </summary>
public partial class WorldLab
{
    private int _gsStage = -1;
    private double _gsTimer;
    private (int Layer, long Cx, long Cz, double X, double Y, double Z) _gsTarget;
    private int _gsBefore;
    private double _gsPatchX, _gsPatchZ;
    private int _gsTufts;

    private void GatherTest(double delta)
    {
        if (_gsStage == -1) { _gsStage = OS.GetCmdlineUserArgs().Contains("--gather-test") ? 0 : -2; return; }
        if (_gsStage < 0 || _sampler == null || _generation != null || ((IClientGame)this).LocalState() == null) return;
        if ((_gsTimer -= delta) > 0) return;
        switch (_gsStage)
        {
            case 0:
                if (_shotAt is { } start && !_gtMoved) { _gtMoved = true; Teleport(start.X, start.Z); _gsTimer = 4; return; }
                if (!Find(ObjectRemovals.Stones)) { Fail("no loose stone within 60 m"); return; }
                Go(1, 3); break;
            case 1:
                Interact(); // E
                if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--gather-shot=")) is { } shot) GetTree().CreateTimer(.45).Timeout += () => Screenshot(shot[14..]);
                _gsBefore = _myInv.Count("stone");
                Go(2, 2); break;
            case 2:
                Check(_myInv.Count("stone") == _gsBefore + 1, $"picked up a stone ({_status})");
                Check(_sampler.Removals.Contains(ObjectRemovals.Stones, _gsTarget.Cx, _gsTarget.Cz), "it is gone from the world");
                Go(3, 1.5); break;
            case 3:
                Check(!_streamer.GatherablesNear(_gsTarget.X, _gsTarget.Z, .2).Any(g => g.Layer == ObjectRemovals.Stones && g.Cx == _gsTarget.Cx && g.Cz == _gsTarget.Cz),
                    "no longer drawn there (tiles rebuilt)");
                if (!Find(ObjectRemovals.Berries)) { Print("no berry bush within 60 m: berries skipped"); Go(6, 0); break; }
                Go(4, 3); break;
            case 4:
                Interact();
                _gsBefore = _myInv.Count("berries");
                Go(5, 2.5); break;
            case 5:
                Check(_myInv.Count("berries") == _gsBefore + 4, $"picked berries ({_status})");
                Check(_sampler.Removals.Contains(ObjectRemovals.Berries, _gsTarget.Cx, _gsTarget.Cz), "the bush is picked");
                Go(6, 1); break;
            case 6:
            {
                // Somewhere grassy nearby (rings outward), the camera looking down at the spot.
                for (double r = 0; r < 40 && !GrassHere(); r += 2) Teleport(PlayerX + 2, PlayerZ + 1);
                if (!GrassHere()) Print("no grass found");
                _gsPatchX = PlayerX; _gsPatchZ = PlayerZ;
                _pitch = -.75f; _zoom = 5;
                Go(61, 3); break;
            }
            case 61:
                Shot("-grass-before");
                _gsTufts = TuftsInPatch();
                Print($"{_gsTufts} grass tufts in the 3 x 3 m patch before");
                Interact();
                Go(62, 2); break;
            case 62:
                Check(_sampler.Removals.Contains(ObjectRemovals.Grass, (long)Math.Floor(_gsPatchX), (long)Math.Floor(_gsPatchZ)), "the pulled grass is gone from the world");
                Check(!GrassHere(), "nothing left to pull right here");
                int after = TuftsInPatch();
                Check(_gsTufts > 0 && after == 0, $"the patch is bare: {_gsTufts} tufts before, {after} after");
                Teleport(_gsPatchX + 3.2, _gsPatchZ);
                Go(63, 3); break;
            case 63:
                _yaw = Mathf.Atan2(-(float)(_gsPatchX - PlayerX), -(float)(_gsPatchZ - PlayerZ)); _pitch = -.6f; _zoom = 4;
                Go(7, 1.5); break;
            case 7:
                Shot("-grass-after");
                Interact(); // (a fresh patch here)
                Go(8, 2); break;
            case 8:
                Check(_myInv.Count("plant_fibre") >= 4, $"plant fibre: {_myInv.Count("plant_fibre")} ({_status})");
                if (_myInv.Count("branch") == 0) Print("(no branches: the rod needs one — skipped)");
                else Act(new ActionRequest(ActionKind.Make, 0, 0, "fishing_rod", 0, 0, 0, 0));
                Go(9, 1.5); break;
            case 9:
                if (_myInv.Count("branch") > 0 || _myInv.HasTag("fishing")) Check(_myInv.HasTag("fishing"), $"a fishing rod ({_status})");
                GD.Print("GATHER_DONE");
                _gsStage = -2;
                GetTree().CreateTimer(1).Timeout += () => GetTree().Quit();
                break;
        }

        bool Find(int layer)
        {
            var spot = _streamer.GatherablesNear(PlayerX, PlayerZ, 60).FirstOrDefault(g => g.Layer == layer);
            if (spot.Layer != layer) return false;
            _gsTarget = spot;
            Teleport(spot.X - .9, spot.Z - .9);
            Print($"{(layer == ObjectRemovals.Stones ? "stone" : "berry bush")} {spot.Cx},{spot.Cz} at {spot.X:0.0}, {spot.Z:0.0}");
            return true;
        }
        void Go(int stage, double wait) { _gsStage = stage; _gsTimer = wait; }
        int TuftsInPatch()
        {
            // The tile's plants as the game builds them now, counted in the 3 x 3 cells around the spot.
            long gx = (long)Math.Floor(_gsPatchX), gz = (long)Math.Floor(_gsPatchZ);
            var key = new TileKey(0, (long)Math.Floor(_gsPatchX / TileBuilder.BaseTile), (long)Math.Floor(_gsPatchZ / TileBuilder.BaseTile));
            var data = TileBuilder.Build(_sampler!, key, DetailConfig.For(_sampler!));
            return data.Flora?.Count(f =>
            {
                double wx = key.MinX + f.Transform.Origin.X, wz = key.MinZ + f.Transform.Origin.Z;
                return Math.Abs(Math.Floor(wx) - gx) <= 1 && Math.Abs(Math.Floor(wz) - gz) <= 1;
            }) ?? -1;
        }
        bool Shot(string suffix)
        {
            if (OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--gather-shot=")) is not { } shot) return false;
            Screenshot(shot[14..].Replace(".png", suffix + ".png"));
            return true;
        }
        void Print(string s) { GD.Print("GATHER " + s); _net?.Stats.Event("gather test: " + s); }
        void Check(bool ok, string what) { GD.Print((ok ? "GATHER ok: " : "GATHER_FAIL: ") + what); _net?.Stats.Event("gather test: " + (ok ? "ok " : "FAIL ") + what); }
        void Fail(string what) { GD.Print("GATHER_FAIL: " + what); _gsStage = -2; }
    }
}
