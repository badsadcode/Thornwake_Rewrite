using System;
using Godot;
using Thornwake.Core;

namespace Thornwake.Development;
public partial class CaveTerrainLab
{
    private bool _shoreSmoke;
    private int _shoreStage;
    private readonly System.Diagnostics.Stopwatch _shoreClock = System.Diagnostics.Stopwatch.StartNew();
    private void ShoreFeedbackStep()
    {
        Require(_shoreClock.Elapsed.TotalSeconds<90,"shore feedback timeout");
        if(_readyFrames<3) return;
        if(_shoreStage==0)
        {
            _state=CaveSpikeStore.Load(ProjectSettings.GlobalizePath("res://Tests/Fixtures/cave-player-shore-cuts-v2.json"));
            _field=new(_state); _waterRunning=false;
            _terrain.Configure(_field,.5f,false); BuildWater();
            _shoreStage=1;
        }
        else if(_shoreStage==1)
        {
            Require(_state.Cuts.Count==6,"player's saved six-stroke shoreline fixture");
            Teleport(new(-.8f,-1.3f,7)); _camera.LookAt(new Vector3(-.6f,-1.8f,3));
            _shoreStage=2;
        }
        else if(_shoreStage==2)
        {
            GD.Print("CAVE_SHORE_PASS: saved player excavation and water, stable shared terrain normals, 0.5m shoreline capture");
            _shoreStage=3; CaptureAndClose();
        }
    }
}
