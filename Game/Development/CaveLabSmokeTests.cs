using System;
using System.IO;
using System.Text.Json;
using Godot;
using Thornwake.Core;
using NVector = System.Numerics.Vector3;

namespace Thornwake.Development;

public partial class CaveTerrainLab
{
    private int _smokeStage, _smokeTicks;
    private bool _ceilingHit;
    private float _floorBefore;
    private ulong _beforeReload;
    private float FloorAt(float x, float z)
    {
        var hit = Ray(new(x, .5f, z), new(x, -11, z));
        Require(hit.Count > 0, "cave floor ray hit"); return hit["position"].AsVector3().Y;
    }
    private static void Require(bool condition, string reason) { if (!condition) throw new Exception(reason); }
    private void SmokeStep(float dt)
    {
        if (_readyFrames < 3) return; // allow newly installed shapes to enter the physics space
        _smokeTicks++;
        switch (_smokeStage)
        {
            case 0:
                var top = Ray(new(0, 15, -8), new(0, -11, -8));
                var roof = Ray(new(0, 1, -8), new(0, 14, -8));
                Require(top.Count > 0 && roof.Count > 0, "surface and cave ceiling collide independently");
                Require(top["position"].AsVector3().Y > 8 && roof["position"].AsVector3().Y < 6 && FloorAt(0, -8) < -2,
                    "surface, ceiling and floor coexist at one XZ");
                var support = Ray(new(-8, 14, -8), new(-8, 8, -8));
                Require(support.Count > 0 && support["position"].AsVector3().Y > _field.Surface(-8, -8) + .3f, "supported slab has independent collision");
                Require(_field.WaterAt(new NVector(9, -6, -9)) == null, "lower cavity under lake is dry");
                Teleport(new(0, .5f, 24)); _smokeStage = 1; _smokeTicks = 0;
                GD.Print("CAVE_COLLISION_PASS: overworld, cave floor/ceiling, support slab, local water isolation");
                break;
            case 1:
                Move(new(0, -1), dt);
                Require(_player.Position.Y > -10, "actor does not fall through terrain");
                Require(_smokeTicks < 1000, "entrance traversal timeout at " + _player.Position);
                if (_player.Position.Z < -6)
                {
                    Require(_player.Position.Y < 1, "walking into cave never snaps to overworld");
                    GD.Print("CAVE_TRAVERSAL_PASS: CharacterBody walked from outside through entrance and region seams to " + _player.Position);
                    _player.Velocity = new Vector3(0, 20, 0); _player.MoveAndSlide(); _smokeStage = 2; _smokeTicks = 0;
                }
                break;
            case 2:
                Move(Vector2.Zero, dt); _ceilingHit |= _player.IsOnCeiling();
                if (_smokeTicks > 95)
                {
                    Require(_ceilingHit, "character capsule hits real ceiling");
                    _floorBefore = FloorAt(0, 0);
                    var cut = new CaveCut(0, -2.5f, 0, 1.5f);
                    Require(_field.TryCut(cut), "cross-region excavation accepted"); _terrain.Invalidate(cut);
                    _smokeStage = 3; _smokeTicks = 0;
                    GD.Print("CAVE_CEILING_PASS: physical capsule upward movement blocked");
                }
                break;
            case 3:
                Require(FloorAt(0, 0) < _floorBefore - .5f, "excavation changes collision across region edge");
                CaveSpikeStore.Save(_savePath, _state);
                var loaded = CaveSpikeStore.Load(_savePath); var restored = new CaveSpikeField(loaded);
                Require(loaded.Cuts.Count == 1 && restored.DepositId == _field.DepositId, "cuts and feature identity persist");
                Require(restored.Density(new NVector(0, -3, 0)) == _field.Density(new NVector(0, -3, 0)), "saved solid/void query matches");
                _state = loaded; _field = restored; _beforeReload = _terrain.RegionInstance(new(0, 0));
                _terrain.Configure(_field, 1, false); _smokeStage = 4; _smokeTicks = 0;
                break;
            case 4:
                Require(_terrain.RegionInstance(new(0, 0)) != _beforeReload && FloorAt(0, 0) < _floorBefore - .5f,
                    "unload/save/reload restores modified collision");
                _terrain.Configure(_field, .5f, false); _smokeStage = 5; _smokeTicks = 0;
                GD.Print("CAVE_EDIT_SAVE_PASS: four-region edit, rebuilt collision, isolated atomic save, fresh region reconstruction");
                break;
            case 5:
                Require(FloorAt(0, 0) < _floorBefore - .5f, "half-metre meshing retains excavation");
                _terrain.Configure(_field, 1, true); _smokeStage = 6; _smokeTicks = 0;
                break;
            case 6:
                Require(FloorAt(0, 0) < _floorBefore - .5f, "full-volume comparison uses same persistent authority");
                var ceiling = Ray(new(0, 1, -8), new(0, 14, -8)); Require(ceiling.Count > 0, "full-volume ceiling retained");
                GD.Print("CAVE_BACKEND_COMPARISON_PASS: hybrid 1m/0.5m and full-volume 1m share cave/cut collision");
                _terrain.Configure(_field, 1, false);
                Teleport(new(-20, _field.Surface(-20, -8) + .2f, -8));
                _smokeStage = 7; _smokeTicks = 0;
                break;
            case 7:
                Move(new(1, 0), dt);
                Require(_player.Position.Y > _field.Surface(_player.Position.X, _player.Position.Z) - .2f, "actor stays supported at heightfield/volume interface");
                Require(_smokeTicks < 300, "hybrid interface traversal timeout");
                if (_player.Position.X > -12)
                {
                    GD.Print("CAVE_HYBRID_SEAM_PASS: capsule walks across heightfield/volume collision interface");
                    // Return along the unexcavated side of the tunnel; the center now contains the deliberate deep test pit.
                    Teleport(new(2, -1, -6)); _smokeStage = 8; _smokeTicks = 0;
                }
                break;
            case 8:
                Move(new(0, 1), dt);
                Require(_player.Position.Y > -10, "exit fell through terrain at " + _player.Position);
                Require(_smokeTicks < 1000, "exit traversal timeout at " + _player.Position);
                if (_player.Position.Z < 23) break;
                Require(_player.Position.Y > -.2f, "actor returns to outside ground through cave entrance");
                GD.Print("CAVE_RETURN_PASS: capsule exits underground space back onto overworld");
                if (_report != "")
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_report))!);
                    File.WriteAllText(_report, JsonSerializer.Serialize(new {
                        builds = _terrain.Builds, trianglesBuilt = _terrain.TotalTriangles,
                        workerPreparationMilliseconds = _terrain.TotalMeshMilliseconds,
                        maximumTerrainCommitMilliseconds = _terrain.MaximumCommitMilliseconds,
                        colliderConstructionMilliseconds = _terrain.TotalColliderMilliseconds,
                        managedBytes = GC.GetTotalMemory(false), residentRegions = _terrain.ResidentCount,
                        caveFloorBefore = _floorBefore, caveFloorAfter = FloorAt(0, 0), caveDepositId = _field.DepositId
                    }, new JsonSerializerOptions { WriteIndented = true }));
                }
                GD.Print("CAVE_LAB_SMOKE_PASS: real traversal, ceiling collision, hybrid interfaces, edit, reload, resolution/backend comparison");
                _smokeStage = 9; _smoke = false; Teleport(new(0, .4f, 18));
                _pitch = -.05f; _camera.Rotation = new(_pitch, 0, 0);
                CaptureAndClose();
                break;
        }
    }
}

