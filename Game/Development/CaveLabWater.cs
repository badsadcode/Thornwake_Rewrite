using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Thornwake.Core;

namespace Thornwake.Development;

public partial class CaveTerrainLab
{
    private sealed record WaterResult(LocalWaterSimulation Simulation, ReservoirMeshData Mesh);
    private Task<WaterResult>? _waterJob;
    private CaveSpikeField? _pendingWater, _flowField, _waterJobField;
    private LocalWaterSimulation? _flow;
    private CancellationTokenSource? _waterCancellation;
    private int _waterRevision, _waterJobRevision, _flowCuts, _waterJobCuts;
    private bool _waterGenerationReady, _waterRunning = true;
    private double _waterAccumulator;
    private bool WaterReady => _waterGenerationReady && !_waterDirty;
    private void BuildWater()
    {
        _waterDirty=false; _waterGenerationReady=false; _waterRevision++; _waterCancellation?.Cancel();
        _pendingWater=_field.Snapshot();
    }
    private void UpdateWater(double delta)
    {
        if (_terrain.IsCollisionReady && _waterRunning) _waterAccumulator=Math.Min(.4,_waterAccumulator+delta);
        if (_waterJob is { IsCompleted: true })
        {
            try
            {
                var result=_waterJob.GetAwaiter().GetResult();
                if (_waterJobRevision==_waterRevision)
                {
                    _flow=result.Simulation; _flowField=_waterJobField; _flowCuts=_waterJobCuts;
                    _state.Water=_flow.Save(); CommitWater(result.Mesh); _waterGenerationReady=true;
                }
            }
            catch (OperationCanceledException) { }
            finally { _waterJob=null; _waterCancellation?.Dispose(); _waterCancellation=null; }
        }
        if (_waterJob==null && _pendingWater==null && _terrain.IsCollisionReady && _waterRunning && _waterAccumulator>=.2)
            _pendingWater=_field.Snapshot();
        if (_waterJob==null && _pendingWater!=null)
        {
            var snapshot=_pendingWater; _pendingWater=null; _waterJobRevision=_waterRevision;
            _waterJobField=_field; _waterJobCuts=snapshot.State.Cuts.Count;
            bool rebuild=_flow==null || !ReferenceEquals(_flowField,_field) || _flowCuts!=snapshot.State.Cuts.Count;
            var previous=_flow; float spacing=_terrain.Spacing;
            int steps=_waterRunning?Math.Min(4,(int)(_waterAccumulator/.1)):0; _waterAccumulator-=steps*.1;
            if ((_flowSmoke && _flowStage>0 || _floorSmoke && _floorStage>0) && _waterRunning) steps=10;
            _waterCancellation=new(); var token=_waterCancellation.Token;
            _waterJob=Task.Run(()=>
            {
                var simulation=rebuild?LocalWaterSimulation.ForCave(snapshot,token):previous!.Copy();
                for(int i=0;i<steps;i++) { token.ThrowIfCancellationRequested(); simulation.Step(.1f); }
                var mesh=LocalWaterMesher.Build(snapshot,simulation,spacing,token);
                return new WaterResult(simulation,mesh);
            },token);
        }
    }
    private void CommitWater(ReservoirMeshData data)
    {
        var root=new Node3D();
        if(data.Vertices.Count>0)
        {
            var shader=GD.Load<Shader>("res://Assets/Water/CaveWater.gdshader");
            using var arrays=new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex]=data.Vertices.Select(v=>new Vector3(v.X,v.Y,v.Z)).ToArray();
            arrays[(int)Mesh.ArrayType.Normal]=Enumerable.Repeat(Vector3.Up,data.Vertices.Count).ToArray();
            arrays[(int)Mesh.ArrayType.Color]=data.Depths.Select((d,i)=>new Color(Math.Clamp(d/4,0,1),Math.Clamp(data.ShoreDistances[i]/.5f,0,1),0)).ToArray();
            var mesh=new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
            root.AddChild(new MeshInstance3D { Mesh=mesh,MaterialOverride=new ShaderMaterial { Shader=shader },
                CastShadow=GeometryInstance3D.ShadowCastingSetting.Off });
        }
        if(_water!=null) { RemoveChild(_water); _water.QueueFree(); }
        _water=root; AddChild(root);
    }
    private void StopWater()
    {
        _waterCancellation?.Cancel(); _pendingWater=null;
        if(_waterJob!=null) _=_waterJob.ContinueWith(t=>{ _=t.Exception; },TaskScheduler.Default);
    }
}



