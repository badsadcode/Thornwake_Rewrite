using System;
using System.Collections.Generic;
using Godot;
using Thornwake.Hydraulics;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Runs the <see cref="LocalWater"/> simulation around the player and draws it: one mesh per 16 m chunk with a vertex
/// at every 0.5 m cell, rebuilt only when its water changed visibly (a few chunks per frame). Uses the terrain water
/// material, so simulated water looks like the rivers (flow ripples from the simulated velocity).
/// </summary>
public partial class LocalWaterView : Node3D
{
    public LocalWater? Sim { get; private set; }
    private ShaderMaterial _material = null!;
    private double _originX, _originZ;
    private readonly Dictionary<(long, long), MeshInstance3D> _meshes = new();
    private readonly HashSet<(long, long)> _pending = new();
    private const int N = LocalWater.ChunkCells + 1;
    private readonly float[] _floor = new float[N * N], _depth = new float[N * N], _vx = new float[N * N], _vz = new float[N * N];

    public void Configure(TerrainSampler sampler, ShaderMaterial material, double originX, double originZ)
    {
        foreach (var mesh in _meshes.Values) mesh.QueueFree();
        _meshes.Clear(); _pending.Clear();
        _material = material; _originX = originX; _originZ = originZ;
        Sim = new LocalWater((cx, cz) => LocalWater.FromSampler(sampler, cx, cz));
    }

    public void Rebase(double originX, double originZ)
    {
        _originX = originX; _originZ = originZ;
        foreach (var ((cx, cz), mesh) in _meshes) mesh.Position = ChunkPosition(cx, cz);
    }

    private Vector3 ChunkPosition(long cx, long cz) =>
        new((float)(cx * LocalWater.ChunkSize - _originX), 0, (float)(cz * LocalWater.ChunkSize - _originZ));

    /// <summary>Physics tick: follow the player and advance the water (two half steps keep deep water stable).</summary>
    public void Step(float dt, double playerX, double playerZ)
    {
        if (Sim == null || !GameSettings.WaterSimulation.On) return;
        Sim.Follow(playerX, playerZ);
        Sim.Step(dt * .5f); Sim.Step(dt * .5f);
        foreach (var chunk in Sim.TakeDirty()) _pending.Add(chunk);
    }

    public override void _Process(double delta)
    {
        if (Sim == null || _pending.Count == 0) return;
        int budget = 6;
        var done = new List<(long, long)>();
        foreach (var chunk in _pending)
        {
            if (budget-- <= 0) break;
            Rebuild(chunk.Item1, chunk.Item2);
            done.Add(chunk);
        }
        foreach (var chunk in done) _pending.Remove(chunk);
    }

    private void Rebuild(long cx, long cz)
    {
        _meshes.Remove((cx, cz), out var old);
        old?.QueueFree();
        if (Sim == null || !Sim.ChunkView(cx, cz, _floor, _depth, _vx, _vz)) return;
        const float Wet = .008f;
        var vertices = new Vector3[N * N]; var colors = new Color[N * N]; var flow = new Vector2[N * N]; var normals = new Vector3[N * N];
        bool any = false;
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                int k = j * N + i;
                float y;
                if (_depth[k] > Wet) { y = _floor[k] + _depth[k] + .01f; any = true; }
                else
                {
                    // Dry vertex beside water: the neighbours' surface, so the sheet ends at the true shoreline.
                    float sum = 0; int count = 0;
                    for (int b = -1; b <= 1; b++)
                        for (int a = -1; a <= 1; a++)
                        {
                            int x = i + a, z = j + b;
                            if (x < 0 || z < 0 || x >= N || z >= N || _depth[z * N + x] <= Wet) continue;
                            sum += _floor[z * N + x] + _depth[z * N + x]; count++;
                        }
                    y = count > 0 ? Math.Min(sum / count, _floor[k] + .01f) : _floor[k] - .3f;
                }
                vertices[k] = new Vector3((i + .5f) * (float)LocalWater.Cell, y, (j + .5f) * (float)LocalWater.Cell);
                normals[k] = Vector3.Up;
                // Fast sheets read as rivers, still pools as lakes.
                float speed = MathF.Sqrt(_vx[k] * _vx[k] + _vz[k] * _vz[k]);
                colors[k] = new Color(Math.Clamp(_depth[k] / 20, 0, 1), speed > .25f ? 1 : .5f, 0, 1);
                flow[k] = new Vector2(_vx[k], _vz[k]);
            }
        if (!any) return;
        var indices = new List<int>();
        for (int j = 0; j < N - 1; j++)
            for (int i = 0; i < N - 1; i++)
            {
                int a = j * N + i, b = a + 1, c = a + N, d = c + 1;
                if (_depth[a] <= Wet && _depth[b] <= Wet && _depth[c] <= Wet && _depth[d] <= Wet) continue;
                indices.Add(a); indices.Add(b); indices.Add(c);
                indices.Add(b); indices.Add(d); indices.Add(c);
            }
        using var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertices;
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        arrays[(int)Mesh.ArrayType.Color] = colors;
        arrays[(int)Mesh.ArrayType.TexUV] = flow;
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        var instance = new MeshInstance3D { Mesh = mesh, MaterialOverride = _material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Position = ChunkPosition(cx, cz) };
        AddChild(instance);
        _meshes[(cx, cz)] = instance;
    }

    public void Clear() { Sim?.Clear(); }
}
