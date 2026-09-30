using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Thornwake.Core;
using NVector = System.Numerics.Vector3;

namespace Thornwake.Development;

/// <summary>Bounded worker preparation with revision checks and atomic mesh/collision replacement.</summary>
public partial class CaveLabTerrain : Node3D
{
    private readonly Dictionary<TerrainRegion, Node3D> _regions = new();
    private readonly HashSet<TerrainRegion> _dirty = new();
    private readonly StandardMaterial3D _material = new() { VertexColorUseAsAlbedo = true, VertexColorIsSrgb = true, Roughness = .92f };
    private sealed record Prepared(TerrainRegion Region, Vector3[] Vertices, Vector3[] Normals, Color[] Colors, double Milliseconds);
    private Task<Prepared[]>? _job;
    private CancellationTokenSource? _cancellation;
    private Prepared[]? _prepared;
    private readonly List<(TerrainRegion Region, Node3D Root)> _staged = new();
    private long _revision, _jobRevision;
    public CaveSpikeField Field { get; private set; } = null!;
    public float Spacing { get; private set; } = 1;
    public bool FullVolume { get; private set; }
    public bool ShowRegions { get; set; }
    public bool IsCollisionReady { get; private set; }
    public int ResidentCount => _regions.Count;
    public int CommittedRevision { get; private set; }
    public int DiscardedJobs { get; private set; }
    public double LastMeshMilliseconds { get; private set; }
    public double LastColliderMilliseconds { get; private set; }
    public double LastCommitMilliseconds { get; private set; }
    public double MaximumCommitMilliseconds { get; private set; }
    public double TotalMeshMilliseconds { get; private set; }
    public double TotalColliderMilliseconds { get; private set; }
    public long TotalTriangles { get; private set; }
    public int Builds { get; private set; }
    public ulong RegionInstance(TerrainRegion region) => _regions.TryGetValue(region, out var node) ? node.GetInstanceId() : 0;

    public bool CanWalkAt(Vector3 position)
    {
        // Retain old collision while digging; only stop at genuinely unloaded terrain.
        foreach (float x in new[] { position.X - .6f, position.X + .6f })
            foreach (float z in new[] { position.Z - .6f, position.Z + .6f })
                if (!_regions.ContainsKey(TerrainRegion.At(x, z))) return false;
        return true;
    }
    private void CancelRevision()
    {
        _revision++; _cancellation?.Cancel();
        foreach (var entry in _staged) entry.Root.Free();
        _staged.Clear(); _prepared = null; IsCollisionReady = false;
    }
    public void Configure(CaveSpikeField field, float spacing, bool fullVolume)
    {
        if (spacing is not (1 or .5f)) throw new ArgumentOutOfRangeException(nameof(spacing));
        bool sameWorld = ReferenceEquals(Field, field);
        CancelRevision(); Field = field; Spacing = spacing; FullVolume = fullVolume;
        if (sameWorld) foreach (var region in _regions.Keys) _dirty.Add(region);
        else ClearRegions();
    }
    private void Remove(TerrainRegion region)
    {
        if (_regions.Remove(region, out var old)) { RemoveChild(old); old.QueueFree(); }
        _dirty.Remove(region);
    }
    private void ClearRegions() { foreach (var region in _regions.Keys.ToArray()) Remove(region); }
    public void UnloadAll() { CancelRevision(); ClearRegions(); }
    public void Invalidate(CaveCut cut)
    {
        CancelRevision();
        var start=cut.Start; var end=cut.Center; float halo=cut.Radius+1;
        var low=TerrainRegion.At(Math.Min(start.X,end.X)-halo,Math.Min(start.Z,end.Z)-halo);
        var high=TerrainRegion.At(Math.Max(start.X,end.X)+halo,Math.Max(start.Z,end.Z)+halo);
        for(int x=low.X;x<=high.X;x++) for(int z=low.Z;z<=high.Z;z++)
            if(_regions.ContainsKey(new(x,z))) _dirty.Add(new(x,z));
    }
    public void RefreshColors() { CancelRevision(); foreach (var region in _regions.Keys) _dirty.Add(region); }
    public void Stream(Vector3 focus)
    {
        var frame = Stopwatch.StartNew();
        var observer = WorldRegion.At(focus.X, focus.Z);
        var plan = RegionStreaming.Plan(_regions.Keys.Select(r => new WorldRegion(r.X, r.Z)).ToArray(), new[] { observer }, 3, 3, 1,
            r => r.X >= -2 && r.X <= 1 && r.Z >= -2 && r.Z <= 1);
        if (plan.Unload.Count > 0)
        {
            CancelRevision();
            foreach (var region in plan.Unload) Remove(new((int)region.X, (int)region.Z));
        }
        if (_job is { IsCompleted: true })
        {
            try
            {
                var result = _job.GetAwaiter().GetResult();
                if (_jobRevision == _revision) _prepared = result; else DiscardedJobs++;
            }
            catch (OperationCanceledException) { DiscardedJobs++; }
            finally { _job = null; _cancellation?.Dispose(); _cancellation = null; }
        }
        if (_prepared != null)
        {
            // Prepare one engine mesh/collider per frame, leaving the old batch in the physics space.
            var data = _prepared[_staged.Count];
            _staged.Add((data.Region, PrepareNode(data)));
            if (_staged.Count == _prepared.Length)
            {
                // All sides of a cross-border edit change together; no intermediate cracks/collision gap.
                foreach (var entry in _staged) { Remove(entry.Region); AddChild(entry.Root); _regions[entry.Region] = entry.Root; }
                _staged.Clear(); _prepared = null; CommittedRevision++;
            }
            LastCommitMilliseconds = frame.Elapsed.TotalMilliseconds;
            MaximumCommitMilliseconds = Math.Max(MaximumCommitMilliseconds, LastCommitMilliseconds);
        }
        else if (_job == null)
        {
            TerrainRegion[] requested = _dirty.OrderBy(r => Math.Abs(r.X - observer.X) + Math.Abs(r.Z - observer.Z)).ThenBy(r => r.X).ThenBy(r => r.Z).ToArray();
            if (requested.Length == 0 && plan.Load.Count > 0) requested = new[] { new TerrainRegion((int)plan.Load[0].X, (int)plan.Load[0].Z) };
            if (requested.Length > 0)
            {
                var snapshot = Field.Snapshot(); float spacing = Spacing; bool full = FullVolume, colors = ShowRegions;
                _jobRevision = _revision; _cancellation = new(); var token = _cancellation.Token;
                // Exactly one active worker, even during repeated F6/F8/load/edit cancellation.
                _job = Task.Run(() => requested.Select(r => Prepare(snapshot, r, spacing, full, colors, token)).ToArray(), token);
            }
        }
        IsCollisionReady = _job == null && _prepared == null && _dirty.Count == 0 && plan.Load.Count == 0;
    }
    private static Vector3 ToGodot(NVector v) => new(v.X, v.Y, v.Z);
    private static Prepared Prepare(CaveSpikeField field, TerrainRegion region, float spacing, bool full, bool showRegions, CancellationToken token)
    {
        var watch = Stopwatch.StartNew();
        var data = VolumeMesher.Build(field, region, spacing, full || CaveSpikeField.IsVolumeRegion(region), token);
        var vertices = data.Vertices.Select(ToGodot).ToArray(); var normals = data.Normals.Select(ToGodot).ToArray();
        var colors = new Color[vertices.Length];
        var cache = new Dictionary<NVector, Color>();
        for (int i = 0; i < vertices.Length; i++)
        {
            if (i % 1024 == 0) token.ThrowIfCancellationRequested();
            var v = data.Vertices[i];
            if (!cache.TryGetValue(v, out var color))
                cache[v] = color = showRegions ? ((region.X + region.Z) % 2 == 0 ? new Color("518a85") : new Color("b59159")) :
                    field.DepositContains(v) ? new Color("cc733e") : v.Y > field.Surface(v.X, v.Z) - .25f ? new Color("67814a") : new Color("77776e");
            colors[i] = color;
        }
        return new(region, vertices, normals, colors, watch.Elapsed.TotalMilliseconds);
    }
    private Node3D PrepareNode(Prepared data)
    {
        using var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices; arrays[(int)Mesh.ArrayType.Normal] = data.Normals; arrays[(int)Mesh.ArrayType.Color] = data.Colors;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var root = new Node3D { Name = $"Terrain_{data.Region.X}_{data.Region.Z}" };
        root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _material });
        LastMeshMilliseconds = data.Milliseconds;
        var watch = Stopwatch.StartNew();
        var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
        body.AddChild(new CollisionShape3D { Shape = mesh.CreateTrimeshShape() }); root.AddChild(body);
        LastColliderMilliseconds = watch.Elapsed.TotalMilliseconds;
        TotalTriangles += data.Vertices.Length / 3; Builds++;
        TotalMeshMilliseconds += LastMeshMilliseconds; TotalColliderMilliseconds += LastColliderMilliseconds;
        return root;
    }
    public override void _ExitTree()
    {
        CancelRevision();
        // Observe exceptions without waiting on the render thread. Workers own only private data, no Godot objects.
        if (_job != null) _ = _job.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        _material.Dispose();
    }
}

