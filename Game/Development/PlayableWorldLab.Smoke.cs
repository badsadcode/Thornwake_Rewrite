using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Thornwake.Core;

namespace Thornwake.Development;

/// <summary>Rendered engine exercise through the same controller/streaming entry points. Not aesthetic acceptance.</summary>
public partial class PlayableWorldLab
{
    private string _smokeDirectory="";
    private int _smokeStage,_smokeChecks;
    private double _smokeTime,_smokeTotal,_jumpBase,_jumpPeak,_progressAt;
    private int _peakResident,_peakDetailed;
    private long _peakTriangles;
    private Vector2 _smokeInput;
    private float _smokeVertical;
    private WorldPoint _smokeStart;
    private readonly List<object> _captures=new();
    private void SmokeCheck(bool condition,string reason){if(!condition)throw new InvalidOperationException("PLAYABLE_SMOKE: "+reason);_smokeChecks++;}
    private void NextSmoke(){_smokeStage++;_smokeTime=0;_smokeInput=Vector2.Zero;_smokeVertical=0;}
    private void Capture(string name)
    {
        Directory.CreateDirectory(_smokeDirectory);string path=Path.Combine(_smokeDirectory,name+".png");
        using var image=GetViewport().GetTexture().GetImage();if(image.SavePng(path)!=Error.Ok)throw new IOException("Capture failed.");
        _captures.Add(new{Name=name,Global=_position,Origin=_origin,Local=new[]{_player.Position.X,_player.Position.Y,_player.Position.Z},Resident=_terrain.Resident,Detailed=_terrain.DetailedCount,Region=WorldRegion.At(_position.X,_position.Z),Camera=new[]{_camera.Position.X,_camera.Position.Y,_camera.Position.Z},_terrain.WorkerMs,_terrain.ColliderMs,_terrain.CommitMs,_terrain.MaxCommitMs,_terrain.Triangles,_rebases,Hydrology=_hydroSummary});
        GD.Print($"PLAYABLE_CAPTURE {name} global={_position} tiles={_terrain.Resident} worker={_terrain.WorkerMs:F1} commit={_terrain.CommitMs:F1} rebases={_rebases}");
    }
    private void SmokeStep(double delta)
    {
        _smokeTotal+=delta;_smokeTime+=delta;
        if(_smokeTotal>600)throw new TimeoutException("Playable rendered test exceeded ten-minute bound.");
        if(_world==null || _setup!=null)return;
        _peakResident=Math.Max(_peakResident,_terrain.Resident);_peakDetailed=Math.Max(_peakDetailed,_terrain.DetailedCount);_peakTriangles=Math.Max(_peakTriangles,_terrain.Triangles);
        if(_smokeTotal-_progressAt>15){_progressAt=_smokeTotal;GD.Print($"PLAYABLE_PROGRESS stage={_smokeStage} seconds={_smokeTotal:F0} tiles={_terrain.Resident} near={_terrain.DetailedCount} hold={_hold} floor={_player.IsOnFloor()}");}
        SmokeCheck(_terrain.Resident<=PlayableWorldPolicy.MaximumTiles && _terrain.Pending<=1,"bounded terrain work/residency");
        var reconstructed=PlayableWorldPolicy.FromLocal(new(_player.Position.X,_player.Position.Y,_player.Position.Z),_origin);
        SmokeCheck(Math.Abs(reconstructed.X-_position.X)<.002 && Math.Abs(reconstructed.Y-_position.Y)<.002 && Math.Abs(reconstructed.Z-_position.Z)<.002,"global/local alignment");
        switch(_smokeStage)
        {
            case 0:
                if(_hold || _terrain.Resident<625 || !_player.IsOnFloor())return;
                _smokeStart=_position;Capture("01-spawn");NextSmoke();break;
            case 1:
                _smokeInput=new Vector2(1,-1).Normalized();
                if(_smokeTime<3)return;
                SmokeCheck(Math.Abs(_position.X-_smokeStart.X)+Math.Abs(_position.Z-_smokeStart.Z)>3,"walk/strafe on actual collider");
                _jumpBase=_position.Y;_jumpPeak=_position.Y;_jump=true;NextSmoke();break;
            case 2:
                _jumpPeak=Math.Max(_jumpPeak,_position.Y);if(_smokeTime<2)return;
                SmokeCheck(_jumpPeak>_jumpBase+.5,"jump rises above ground");SmokeCheck(_player.IsOnFloor(),"jump lands on collider");Capture("02-ground");
                _flyToggle.ButtonPressed=true;_noclipToggle.ButtonPressed=true;_flySpeed=50;NextSmoke();break;
            case 3:
                _smokeInput=new(0,-1);_smokeVertical=.2f;if(_smokeTime<2.2)return;
                SmokeCheck(_rebases>=3,"high speed flight rebases repeatedly");NextSmoke();break;
            case 4:
                if(_hold || !_terrain.CanTraverse(_position,_position))return;Capture("03-flight");MountainApproach();NextSmoke();break;
            case 5:
                if(_hold || _terrain.Resident<1250)return;Capture("04-mountain-approach");
                Teleport(_landmarks.First(l=>l.Name.StartsWith("High mountain")).Position);_flyToggle.ButtonPressed=false;_boundaries=true;_terrainMode=1;ApplyOverlays();NextSmoke();break;
            case 6:
                if(_hold || _terrain.Resident<49 || !_player.IsOnFloor())return;_smokeStartSlope=_position;NextSmoke();break;
            case 7:
                _smokeInput=new(1,0);if(_smokeTime<2)return;
                SmokeCheck(Math.Abs(_position.X-_smokeStartSlope.X)+Math.Abs(_position.Z-_smokeStartSlope.Z)>1,"movement on mountain slope collider");
                Capture("05-ridge-slope");_boundaries=false;_terrainMode=0;ApplyOverlays();_flyToggle.ButtonPressed=true;_noclipToggle.ButtonPressed=true;
                Teleport(_landmarks.First(l=>l.Name.StartsWith("Coast")).Position with{Y=_world.Manifest.Profile.SeaLevel+3});Face(_position.X+1000,_position.Z);_viewPitch=.12f;NextSmoke();break;
            case 8:
                if(_hold || _terrain.Resident<625)return;_waterStatus=false;ApplyOverlays();NextSmoke();break;
            case 9:
                if(_smokeTime<.6)return;Capture("06-coast");
                Teleport(_landmarks.First(l=>l.Name.StartsWith("Unresolved basin")).Position with{Y=_landmarks.First(l=>l.Name.StartsWith("Unresolved basin")).Position.Y+100});RequestInspection();_streamOverlay=true;_rings=true;_waterStatus=true;_detailedHud=true;NextSmoke();break;
            case 10:
                if(_hold || _terrain.Resident<49 || _hydroJob!=null)return;
                SmokeCheck(_inspection!=null && _inspectedTile?.Lake!=null,"real regional candidate inspected, not filled");ApplyOverlays();NextSmoke();break;
            case 11:
                if(_smokeTime<.6)return;Capture("07-basin-diagnostics");_streamOverlay=false;_rings=false;_terrainMode=0;_detailedHud=false;ApplyOverlays();
                Teleport(_smokeStart);_flyToggle.ButtonPressed=false;NextSmoke();break;
            case 12:
                if(_hold || _terrain.Resident<49 || !_player.IsOnFloor())return;
                SmokeCheck(_terrain.Unloaded>50,"distant terrain unloads");SmokeCheck(_rebases>=6,"continental teleports/rebases");
                Capture("08-return");File.WriteAllText(Path.Combine(_smokeDirectory,"report.json"),JsonSerializer.Serialize(new{Renderer=_renderer,Manifest=_world.Manifest,Checks=_smokeChecks,Seconds=_smokeTotal,Captures=_captures,Builds=_terrain.Builds,_terrain.Unloaded,_terrain.Discarded,Rebases=_rebases,PeakResident=_peakResident,PeakDetailed=_peakDetailed,PeakTriangles=_peakTriangles,Status="PLAYABLE_WORLD_SMOKE_PASS"},new JsonSerializerOptions{WriteIndented=true}));
                GD.Print($"PLAYABLE_WORLD_SMOKE_PASS: {_smokeChecks} checks, {_terrain.Builds} builds, {_terrain.Unloaded} unloads, {_rebases} rebases");Close();break;
        }
    }
    private WorldPoint _smokeStartSlope;
}
