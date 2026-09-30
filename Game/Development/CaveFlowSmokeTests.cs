using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Thornwake.Core;

namespace Thornwake.Development;
public partial class CaveTerrainLab
{
    private bool _flowSmoke;
    private int _flowStage;
    private long _breachTick;
    private readonly Stopwatch _flowTestClock=Stopwatch.StartNew();
    private bool OpenLakeOutlet()
    {
        if(!_terrain.IsCollisionReady || !WaterReady) { Tell("Wait for terrain and water preparation."); return false; }
        var planned=new System.Collections.Generic.List<CaveCut>();
        var previous=new System.Numerics.Vector3(9,_field.Surface(9,-9)-.6f,-9);
        for(float z=-7.5f;z<=21;z+=1.5f)
        {
            var end=new System.Numerics.Vector3(9,_field.Surface(9,z)-.6f,z);
            var cut=new CaveCut(end.X,end.Y,end.Z,1.5f,previous.X,previous.Y,previous.Z);
            if(!_state.Cuts.Contains(cut)) planned.Add(cut);
            previous=end;
        }
        if(_state.Cuts.Count+planned.Count>128) { Tell("Not enough remaining test cuts for the outlet."); return false; }
        foreach(var cut in planned) Require(cut.Valid,"valid continuous outlet stroke");
        foreach(var cut in planned) { Require(_field.TryCut(cut),"outlet stroke accepted"); _terrain.Invalidate(cut); }
        _waterDirty=true;
        Tell("Outlet opened. Water advances downhill over time; use Outlet view to watch, F4 to pause.");
        return true;
    }
    private void ViewOutlet()
    {
        var position=new Vector3(13,_field.Surface(13,8)+1,8); var target=new Vector3(9,4,12);
        Teleport(position); var direction=(target-_camera.GlobalPosition).Normalized();
        _player.Rotation=new(0,Mathf.Atan2(-direction.X,-direction.Z),0);
        _pitch=Mathf.Asin(direction.Y); _camera.Rotation=new(_pitch,0,0);
    }
    private void FlowSmokeStep()
    {
        Require(_flowTestClock.Elapsed.TotalSeconds<120,"downhill water engine test timed out");
        if(_readyFrames<3 || _flow==null) return;
        if(_flowStage==0)
        {
            Require(_field.WaterAt(new System.Numerics.Vector3(9,-6,-9))==null,"lower chamber dry before trench");
            Require(OpenLakeOutlet(),"outlet fixture accepted");
            _waterDirty=true; _breachTick=_flow.Tick; _flowStage=1;
            Teleport(new(13,11,8)); _camera.LookAt(new Vector3(9,4,12));
            GD.Print("CAVE_FLOW_BREACH: opened hilltop lake into a descending excavated channel");
        }
        else if(_flowStage==1)
        {
            var wet=_flow.Topology.Cells.Select((c,i)=>(c,i)).Where(p=>p.c.X is 8 or 9 && p.c.Z>=14 && _flow.Volume(p.i)>.015).ToArray();
            if(wet.Length==0) return;
            Require(wet.Any(p=>_flow.Level(p.i)<_field.Reservoirs[0].Level-2),"downstream surface is below lake level");
            Require(_flow.Tick>_breachTick+1,"river advances over simulation time");
            Require(_field.WaterAt(new System.Numerics.Vector3(9,-6,-9))==null,"unbreached lower chamber remains dry beside outflow");
            _waterRunning=false; _flowStage=2;
        }
        else if(_flowStage==2 && _waterJob==null)
        {
            CaveSpikeStore.Save(_savePath,_state);
            var loaded=CaveSpikeStore.Load(_savePath);
            Require(loaded.Water!=null && loaded.Water.Tick==_state.Water!.Tick,"water progression saved");
            Require(Math.Abs(loaded.Water!.Cells.Sum(c=>c.Volume)-_flow.TotalVolume)<.000001,"water storage survives serialization");
            var restored=LocalWaterSimulation.ForCave(new CaveSpikeField(loaded));
            Require(Math.Abs(restored.TotalVolume-_flow.TotalVolume)<.000001,"new scene topology restores same water amount");
            GD.Print($"CAVE_FLOW_PASS: downhill channel reached z>=14 after {_flow.Tick-_breachTick} ticks; storage {_flow.TotalVolume:0.000} m3; dry sealed chamber; persistent water");
            if(_report!="") File.WriteAllText(_report,JsonSerializer.Serialize(new { ticksAfterBreach=_flow.Tick-_breachTick,
                cells=_flow.Topology.Cells.Count, connections=_flow.Topology.Connections.Count,
                wetCells=_state.Water!.Cells.Count, storedCubicMetres=_flow.TotalVolume,
                elapsedSeconds=_flowTestClock.Elapsed.TotalSeconds },new JsonSerializerOptions { WriteIndented=true }));
            _flowStage=3; CaptureAndClose();
        }
    }
}

