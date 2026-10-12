using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// The tree field (round 30, docs/PERFORMANCE_TREES.md): every tree on screen, drawn in world-aligned cells around the
/// player instead of riding on the terrain tile rings.
/// * <b>Near cells</b> (128 m): the 3D trees, one batch per species and mesh part. Cells close to the player carry the
///   detailed model (LOD1) and cast shadows; Godot switches them to LOD2 by the cell's distance (visibility ranges with a
///   dithered fade). Batches are 10–50× larger than the old per-64 m-tile ones: far fewer draw calls.
/// * <b>Far cells</b> (1024 m): the same trees as impostors (TreeImpostors), one batch per cell.
/// * The hand-overs 3D → impostor → tree card are cross-fades on circles around the camera in the shaders
///   (TreeFade.gdshaderinc), so cell edges never show and nothing pops.
/// Trees are the same world-aligned lattice as before (Vegetation.Trees): every tree keeps its place, species, size and
/// rotation whatever represents it. Collision still comes from the level-0 terrain tiles. Off (Tree impostors: off)
/// = build 41 (trees and cards on the terrain tiles).
/// </summary>
public sealed partial class TreeField : Node3D
{
    public const int FineLevel = 1, CoarseLevel = 4; // TileKey levels: 128 m and 1024 m cells
    private const float Fade = 40;                   // half-width of the cross-fade bands (m)
    private const float LodBand = 20;                // half-width of the detailed → lighter model band (m)
    private const int Stride = 16;                   // floats per 3D tree instance: transform 12 + custom 4 (x = role)

    /// <summary>Detailed (LOD1, shadows) out to here; LOD2 3D trees to <see cref="DetailRange"/>; impostors to <see cref="ImpostorRange"/>.</summary>
    public float NearRange { get; private set; } = 220;
    public float DetailRange { get; private set; } = 800;
    public float ImpostorRange { get; private set; } = 3200;
    public int Pending => _jobs.Count;
    public int Missing { get; private set; }
    public bool Settled => _sampler == null || (Missing == 0 && _jobs.Count == 0);
    public long Instances3D { get; private set; }
    public long ImpostorInstances { get; private set; }
    /// <summary>Main-thread time committing cells in the last Update (benchmark).</summary>
    public double FrameCommitMs { get; private set; }

    private readonly TreeCatalog _catalog;
    private readonly TreeImpostors _impostors;
    private TerrainSampler? _sampler;
    private DetailConfig _config;
    private double _originX, _originZ;
    private long _revision;
    private readonly Dictionary<TileKey, Cell> _cells = new();
    private readonly Dictionary<TileKey, (Task<CellData> Task, long Revision)> _jobs = new();
    private readonly HashSet<TileKey> _desired = new();
    private readonly Dictionary<(int Species, int Lod), (Mesh Mesh, Transform3D Transform)[]> _parts = new();

    private sealed class Cell
    {
        public required Node3D Node;
        public bool Near;
        public long Revision;
        public long Count;
    }

    private sealed class CellData
    {
        public required TileKey Key;
        public bool Near;
        public Vector3 Centre;
        public List<(int Species, int Lod, int Part, float[] Buffer, int Count)> Batches = new();
        public float[]? Impostors;
        public int ImpostorCount;
        public long Trees;
    }

    public TreeField(TreeCatalog catalog, TreeImpostors impostors)
    {
        _catalog = catalog;
        _impostors = impostors;
        Name = "TreeField";
    }

    public static bool Enabled => GameSettings.TreeImpostors.Index > 0;

    /// <summary>Ranges from the settings: 3D trees 400 m × 2^(Detailed trees), impostors 800 m × 2^(Tree impostors).</summary>
    public void ReadSettings()
    {
        DetailRange = 400f * (1 << GameSettings.TreeDistance.Index);
        ImpostorRange = Math.Max(DetailRange, 800f * (1 << GameSettings.TreeImpostors.Index));
        NearRange = Math.Min(GameSettings.TreeFullDetail.F, DetailRange * .5f);
        RenderingServer.GlobalShaderParameterSet("tree_ranges", Enabled ? new Vector4(DetailRange, ImpostorRange, Fade, 1) : Vector4.Zero);
        RenderingServer.GlobalShaderParameterSet("tree_lod", Enabled ? new Vector4(NearRange, LodBand, 0, 0) : Vector4.Zero);
    }

    public void Configure(TerrainSampler sampler, DetailConfig config, double originX, double originZ)
    {
        Clear();
        _sampler = sampler; _config = config; _originX = originX; _originZ = originZ;
        ReadSettings();
        // Part meshes and transforms are engine resources: load them here, on the main thread, for the workers.
        for (int s = 0; s < Vegetation.Species.Length; s++)
            foreach (int lod in new[] { 1, 2 })
                _parts[(s, lod)] = _catalog.PartsOf(s, lod).ToArray();
    }

    public void Refresh(DetailConfig config)
    {
        _config = config;
        ReadSettings();
        _revision++;
    }

    public void Clear()
    {
        _revision++;
        foreach (var c in _cells.Values) { RemoveChild(c.Node); c.Node.QueueFree(); }
        _cells.Clear();
        _desired.Clear();
        Instances3D = ImpostorInstances = 0;
        if (!Enabled) { RenderingServer.GlobalShaderParameterSet("tree_ranges", Vector4.Zero); RenderingServer.GlobalShaderParameterSet("tree_lod", Vector4.Zero); }
    }

    public void Rebase(double originX, double originZ)
    {
        _originX = originX; _originZ = originZ;
        foreach (var (key, c) in _cells) c.Node.Position = Centre(key) - new Vector3((float)_originX, 0, (float)_originZ);
    }

    /// <summary>The ground changed (terrain edit): rebuild the cells over that rectangle.</summary>
    public void Invalidate(double minX, double minZ, double maxX, double maxZ)
    {
        foreach (var (key, c) in _cells)
            if (key.MinX <= maxX && key.MinX + key.Size >= minX && key.MinZ <= maxZ && key.MinZ + key.Size >= minZ) c.Revision = -1;
    }

    private static Vector3 Centre(TileKey k) => new((float)(k.MinX + k.Size / 2), 0, (float)(k.MinZ + k.Size / 2));

    private static double Dist(TileKey k, double px, double pz)
    {
        double dx = Math.Max(0, Math.Abs(k.MinX + k.Size / 2 - px) - k.Size / 2), dz = Math.Max(0, Math.Abs(k.MinZ + k.Size / 2 - pz) - k.Size / 2);
        return Math.Sqrt(dx * dx + dz * dz); // to the nearest point of the cell
    }

    public void Update(double px, double pz)
    {
        FrameCommitMs = 0;
        if (_sampler == null || !Enabled || !_impostors.Ready) return;
        // Wanted cells: near cells where any part is within the 3D range, far cells where any part is within the
        // impostor range and some part beyond the 3D range.
        _desired.Clear();
        AddCells(FineLevel, px, pz, 0, DetailRange + Fade);
        if (ImpostorRange > DetailRange) AddCells(CoarseLevel, px, pz, DetailRange - Fade, ImpostorRange + Fade);

        var timer = Stopwatch.StartNew();
        foreach (var key in _jobs.Where(j => j.Value.Task.IsCompleted).Select(j => j.Key).OrderBy(k => Dist(k, px, pz)).ToList())
        {
            if (timer.Elapsed.TotalMilliseconds > 4) break;
            var (task, revision) = _jobs[key];
            _jobs.Remove(key);
            if (task.IsFaulted) { GD.PushError(task.Exception?.ToString() ?? "tree cell failed"); continue; }
            if (revision != _revision || !_desired.Contains(key)) continue;
            Commit(task.Result, revision);
        }
        FrameCommitMs = timer.Elapsed.TotalMilliseconds;

        // Schedule: missing cells, stale ones, and near cells that now need their detailed model.
        var missing = _desired.Where(k => !_cells.TryGetValue(k, out var c) || c.Revision != _revision
            || (k.Level == FineLevel && !c.Near && Dist(k, px, pz) < NearRange + Fade)).ToList();
        Missing = missing.Count;
        int maxJobs = Background.CellJobs;
        if (_jobs.Count < maxJobs)
        {
            var sampler = _sampler; var config = _config; long rev = _revision;
            float nearRange = NearRange + Fade + 128;
            foreach (var key in missing.Where(k => !_jobs.ContainsKey(k)).OrderBy(k => k.Level).ThenBy(k => Dist(k, px, pz)).Take(maxJobs - _jobs.Count))
            {
                bool near = key.Level == FineLevel && Dist(key, px, pz) < nearRange;
                _jobs[key] = (Background.Run(() => Build(sampler, config, key, near)), rev);
            }
        }

        // Drop cells that are no longer wanted once the wanted ones are in (no holes while moving).
        if (Missing == 0)
            foreach (var key in _cells.Keys.Where(k => !_desired.Contains(k)).ToList())
            {
                var c = _cells[key];
                if (key.Level == FineLevel) Instances3D -= c.Count; else ImpostorInstances -= c.Count;
                RemoveChild(c.Node); c.Node.QueueFree(); _cells.Remove(key);
            }
    }

    private void AddCells(int level, double px, double pz, double inner, double outer)
    {
        double size = TileBuilder.BaseTile * (1 << level);
        long x0 = (long)Math.Floor((px - outer) / size), x1 = (long)Math.Floor((px + outer) / size);
        long z0 = (long)Math.Floor((pz - outer) / size), z1 = (long)Math.Floor((pz + outer) / size);
        double half = size * .5 * Math.Sqrt(2);
        for (long z = z0; z <= z1; z++)
            for (long x = x0; x <= x1; x++)
            {
                var key = new TileKey(level, x, z);
                double near = Dist(key, px, pz);
                double far = Math.Sqrt(Math.Pow(Math.Abs(key.MinX + size / 2 - px) + size / 2, 2) + Math.Pow(Math.Abs(key.MinZ + size / 2 - pz) + size / 2, 2));
                if (near <= outer && far >= inner) _desired.Add(key);
            }
    }

    /// <summary>Worker thread: the cell's trees and their instance buffers.</summary>
    private CellData Build(TerrainSampler sampler, DetailConfig config, TileKey key, bool near)
    {
        var trees = Vegetation.Trees(sampler, key, config);
        var centre = Centre(key);
        // Instance transforms relative to the cell centre (visibility ranges are measured from the node's origin).
        var offset = new Vector3((float)key.MinX, 0, (float)key.MinZ) - centre;
        var data = new CellData { Key = key, Near = near, Centre = centre, Trees = trees.Count };
        if (key.Level == FineLevel)
        {
            foreach (var group in trees.GroupBy(t => t.Species))
            {
                var list = group.ToArray();
                foreach (int lod in near ? new[] { 1, 2 } : new[] { 2 })
                {
                    var parts = _parts[(group.Key, lod)];
                    // Hand-over role in the custom data (TreeFade.gdshaderinc): 1 = detailed model, 2 = the lighter one
                    // in a near cell (takes over across the band), 0 = no hand-over.
                    float role = lod == 1 ? 1 : near ? 2 : 0;
                    for (int p = 0; p < parts.Length; p++)
                    {
                        var buffer = new float[list.Length * Stride];
                        for (int i = 0; i < list.Length; i++)
                        {
                            MultiMeshFill.Put(buffer, i * Stride, list[i].Transform.Translated(offset) * parts[p].Transform);
                            buffer[i * Stride + 12] = role;
                        }
                        data.Batches.Add((group.Key, lod, p, buffer, list.Length));
                    }
                }
            }
        }
        else
        {
            data.Impostors = new float[trees.Count * 16];
            for (int i = 0; i < trees.Count; i++)
            {
                MultiMeshFill.Put(data.Impostors, i * 16, trees[i].Transform.Translated(offset));
                MultiMeshFill.Put(data.Impostors, i * 16 + 12, new Color(trees[i].Species, 0, 0, 0));
            }
            data.ImpostorCount = trees.Count;
        }
        return data;
    }

    private void Commit(CellData data, long revision)
    {
        var key = data.Key;
        if (_cells.Remove(key, out var old))
        {
            if (key.Level == FineLevel) Instances3D -= old.Count; else ImpostorInstances -= old.Count;
            RemoveChild(old.Node); old.Node.QueueFree();
        }
        var node = new Node3D { Name = $"C{key.Level}_{key.X}_{key.Z}", Position = data.Centre - new Vector3((float)_originX, 0, (float)_originZ) };
        float diagonal = (float)(key.Size * .5 * Math.Sqrt(2));
        bool shadows = GameSettings.Shadows.Index > 0;
        foreach (var (species, lod, part, buffer, count) in data.Batches)
        {
            var mesh = _parts[(species, lod)][part].Mesh;
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh };
            multi.InstanceCount = count;
            multi.Buffer = buffer;
            var mmi = new MultiMeshInstance3D { Multimesh = multi, Layers = lod == 1 ? RenderLayers.TreesNear : RenderLayers.TreesFar };
            bool detailedShadows = GameSettings.TreeShadows.Index == 1;
            if (lod == 1)
            {
                mmi.CastShadow = shadows && detailedShadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
                mmi.VisibilityRangeEnd = NearRange + LodBand; // (the shaders dither the hand-over: tree_lod)
            }
            else
            {
                mmi.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
                if (data.Near) mmi.VisibilityRangeBegin = Math.Max(0, NearRange - LodBand);
                mmi.VisibilityRangeEnd = DetailRange + Fade + diagonal; // beyond: impostors (the shader fades the edge)
                if (data.Near && shadows && !detailedShadows)
                {
                    // Simplified tree shadows: the near trees' shadows come from this lighter model of the same trees
                    // (shadow only), while the detailed model is what the camera sees. Its own copy of the instances,
                    // with role 1: the shadow leaves the band like the detailed model, not takes it like this model.
                    var twin = (float[])buffer.Clone();
                    for (int i = 0; i < count; i++) twin[i * Stride + 12] = 1;
                    var shadowMulti = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh };
                    shadowMulti.InstanceCount = count;
                    shadowMulti.Buffer = twin;
                    node.AddChild(new MultiMeshInstance3D
                    {
                        Multimesh = shadowMulti, Layers = RenderLayers.TreesNear, CastShadow = GeometryInstance3D.ShadowCastingSetting.ShadowsOnly,
                        VisibilityRangeEnd = NearRange + LodBand,
                    });
                }
            }
            node.AddChild(mmi);
        }
        if (data.Impostors != null && data.ImpostorCount > 0)
        {
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = _impostors.Quad };
            multi.InstanceCount = data.ImpostorCount;
            multi.Buffer = data.Impostors;
            node.AddChild(new MultiMeshInstance3D
            {
                Multimesh = multi, MaterialOverride = _impostors.Material, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                Layers = RenderLayers.TreeImpostors, VisibilityRangeEnd = ImpostorRange + Fade + diagonal,
            });
        }
        node.SetMeta(key.Level == FineLevel ? "trees" : "impostors", data.Trees);
        AddChild(node);
        _cells[key] = new Cell { Node = node, Near = data.Near, Revision = revision, Count = data.Trees };
        if (key.Level == FineLevel) Instances3D += data.Trees; else ImpostorInstances += data.Trees;
    }
}
