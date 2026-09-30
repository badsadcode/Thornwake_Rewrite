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
    private bool _feedbackSmoke;
    private int _feedbackStage, _backgroundPhysicsTicks;
    private readonly Stopwatch _feedbackClock = Stopwatch.StartNew();
    private ulong _unchangedRegion, _editedRegion, _previousWater;
    private Vector3 _beforeBackgroundMove;
    private double _digStarted, _highResolutionDigMilliseconds;
    private void FeedbackPhysics(float dt)
    {
        if (_feedbackStage is 1 or 3 && _terrain.CanWalkAt(_player.Position))
        {
            Move(new(0, .25f), dt);
            if (!_terrain.IsCollisionReady) _backgroundPhysicsTicks++;
        }
    }
    private void FeedbackStep()
    {
        Require(_feedbackClock.Elapsed.TotalSeconds < 120, "feedback smoke timeout");
        if (_readyFrames < 3) return;
        switch (_feedbackStage)
        {
            case 0:
                Teleport(new(0, 0, -5));
                _beforeBackgroundMove = _player.Position;
                _unchangedRegion = _terrain.RegionInstance(new(-2, 0));
                _editedRegion = _terrain.RegionInstance(new(0, -1));
                for (int i = 0; i < 20; i++)
                {
                    var cut = new CaveCut(2.8f + .42f * (i % 3), .3f + .43f * (i % 4), -4.1f - .51f * i, 1.5f);
                    Require(_field.TryCut(cut), "overlapping wall cut accepted"); _terrain.Invalidate(cut);
                }
                Require(_terrain.RegionInstance(new(0, -1)) == _editedRegion && _terrain.CanWalkAt(_player.Position), "old collision retained during edit");
                _feedbackStage = 1;
                break;
            case 1:
                Require(_terrain.RegionInstance(new(0, -1)) != _editedRegion, "edited region replaced");
                Require(_terrain.RegionInstance(new(-2, 0)) == _unchangedRegion, "unaffected region retained");
                Require(_player.Position.Z > _beforeBackgroundMove.Z && _backgroundPhysicsTicks > 0, "actor keeps moving during worker preparation");
                GD.Print("CAVE_BACKGROUND_MOVEMENT_PASS: overlapping edits, retained collision and unaffected region, physics ticks=" + _backgroundPhysicsTicks);
                _terrain.Configure(_field, .5f, false); _feedbackStage = 2;
                break;
            case 2:
                _digStarted = _feedbackClock.Elapsed.TotalMilliseconds;
                for (int i = 0; i < 8; i++)
                {
                    var cut = new CaveCut(4.1f + .3f * (i % 3), 1.1f + .25f * (i % 2), -5.4f - .48f * i, 1.5f);
                    Require(_field.TryCut(cut), "high resolution wall cut accepted"); _terrain.Invalidate(cut);
                }
                _feedbackStage = 3;
                break;
            case 3:
                _highResolutionDigMilliseconds = _feedbackClock.Elapsed.TotalMilliseconds - _digStarted;
                // Cancel a running resolution request, then settle to a newer snapshot. No stale geometry may commit.
                _terrain.Configure(_field, 1, true); _terrain.Stream(_player.Position);
                _terrain.Configure(_field, .5f, false); _feedbackStage = 4;
                break;
            case 4:
                Require(_terrain.Spacing == .5f && !_terrain.FullVolume && _terrain.DiscardedJobs > 0, "obsolete resolution work discarded");
                _previousWater = _water.GetInstanceId();
                var shoreCut = new CaveCut(4, -1.4f, -8, 1.5f);
                Require(_field.TryCut(shoreCut), "shoreline cut accepted");
                _terrain.Invalidate(shoreCut); _waterDirty = true; _feedbackStage = 5;
                break;
            case 5:
                Require(_water.GetInstanceId() != _previousWater, "water geometry refreshes after committed shoreline excavation");
                CaveSpikeStore.Save(_savePath, _state);
                Require(CaveSpikeStore.Load(_savePath).Cuts.Count == 29, "stress excavation persisted");
                Require(Ray(new(0, 1, -6), new(9, 1, -6)).Count > 0, "excavated wall retains collision");
                GD.Print($"CAVE_FEEDBACK_PASS: 29 cuts, high-res edit batch {_highResolutionDigMilliseconds:0.0} ms elapsed, max terrain main-thread commit {_terrain.MaximumCommitMilliseconds:0.0} ms, stale jobs {_terrain.DiscardedJobs}");
                if (_report != "") File.WriteAllText(_report, JsonSerializer.Serialize(new {
                    cuts = _state.Cuts.Count, backgroundPhysicsTicks = _backgroundPhysicsTicks,
                    highResolutionEditBatchElapsedMilliseconds = _highResolutionDigMilliseconds,
                    maximumTerrainCommitMilliseconds = _terrain.MaximumCommitMilliseconds,
                    workerMilliseconds = _terrain.TotalMeshMilliseconds, colliderMilliseconds = _terrain.TotalColliderMilliseconds,
                    discardedJobs = _terrain.DiscardedJobs
                }, new JsonSerializerOptions { WriteIndented = true }));
                _feedbackStage = 6; CaptureFeedback();
                break;
        }
    }
    private async void CaptureFeedback()
    {
        try
        {
            if (_capture != "")
            {
                async System.Threading.Tasks.Task View(string suffix, Vector3 position, Vector3 target)
                {
                    Teleport(position); _camera.LookAt(target);
                    await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
                    await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                    using var shot = GetViewport().GetTexture().GetImage();
                    string path = Path.Combine(Path.GetDirectoryName(_capture)!, Path.GetFileNameWithoutExtension(_capture) + suffix + ".png");
                    Require(shot.SavePng(path) == Error.Ok, "feedback screenshot saved");
                }
                await View("-wall", new(1, -.2f, -7), new(4, 1, -7));
                await View("-pool", new(0, -.3f, -4), new(1, -1.4f, -9));
                float lake = _field.Reservoirs[0].Level;
                await View("-lake", new(6, lake + 1, -5), new(9, lake, -9));
            }
            Close();
        }
        catch (Exception error) { GD.PushError(error.ToString()); Close(1); }
    }
}

