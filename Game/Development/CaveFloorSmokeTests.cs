using System;
using System.IO;
using Godot;
using Thornwake.Core;

namespace Thornwake.Development;
public partial class CaveTerrainLab
{
    private bool _floorSmoke;
    private int _floorStage;
    private void FloorFeedbackStep()
    {
        if (_readyFrames < 3) return;
        if (_floorStage == 0)
        {
            var cuts = CaveSpikeStore.Load(ProjectSettings.GlobalizePath("res://Tests/Fixtures/cave-player-floor-cuts-v1.json"));
            cuts.Version = 2; _state = cuts; _field = new(_state);
            // Repeat floor excavation with ridges between adjacent cuts, rather than only wall excavation.
            for (int z = 0; z < 4; z++) for (int x = 0; x < 4; x++)
                Require(_field.TryCut(new(-2.7f+x*1.8f,-2.3f,-2.2f-z*1.8f,1.5f)), "floor regression cut accepted");
            _terrain.Configure(_field,.5f,false); BuildWater(); _floorStage = 1;
        }
        else if (_floorStage == 1)
        {
            Require(_state.Cuts.Count == 20,"floor regression has 20 cuts");
            GD.Print("CAVE_FLOOR_PASS: saved player floor strokes plus 16 overlapping floor cuts; aligned water at 0.5m");
            Teleport(new(0,0,3)); _camera.LookAt(new Vector3(0,-2,-4));
            _floorStage = 2;
        }
        else if (_floorStage == 2 && _flow != null && _flow.Tick >= 150) { _floorStage = 3; _waterRunning = false; CaptureAndClose(); }
    }
}


