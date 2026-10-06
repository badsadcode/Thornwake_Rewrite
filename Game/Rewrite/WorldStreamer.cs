using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Thornwake.World;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Streams the terrain as nested LOD rings (a quadtree "clipmap" in tiles): level 0 is 64 m tiles at 1 m spacing
/// around the player; each coarser level doubles tile size and covers the ring outside the finer one, out to
/// level 7 (8 km tiles, ~20 km radius). Tiles are built on worker threads from the deterministic sampler and
/// committed on the main thread within a time budget. Positions are float offsets from a double-precision origin
/// that is rebased as the player travels, so precision never degrades far from the world centre.
/// </summary>
public partial class WorldStreamer : Node3D
{
    public const int TopRadius = 2;
    /// <summary>LOD levels (7 → ~10 km, 8 → ~20 km, 9 → ~40 km view distance); from the terrain distance setting.</summary>
    public int Levels { get; private set; } = 8;
    public int Resident => _tiles.Count;
    public int Pending => _jobs.Count;
    public int Missing { get; private set; }
    public int Built { get; private set; }
    public double LastBuildMs { get; private set; }
    public double LastCommitMs { get; private set; }
    public double MaxCommitMs { get; private set; }
    public long Triangles { get; private set; }
    /// <summary>Instances committed so far (diagnostics): grass tufts, rocks, props, far tree cards.</summary>
    public long GrassCommitted { get; private set; }
    public long RocksCommitted { get; private set; }
    public long PropsCommitted { get; private set; }
    public long FarTreesCommitted { get; private set; }
    public bool Settled => _sampler != null && Missing == 0 && _jobs.Count == 0;

    private TerrainSampler? _sampler;
    private long _revision;
    private double _originX, _originZ;
    private readonly Dictionary<TileKey, Node3D> _tiles = new();
    private readonly Dictionary<TileKey, long> _tileTriangles = new();
    private readonly Dictionary<TileKey, long> _tileRevision = new();
    private DetailConfig _config;
    private readonly HashSet<TileKey> _desired = new();
    private readonly Dictionary<TileKey, (Task<TileData> Task, long Revision)> _jobs = new();
    private readonly HashSet<TileKey> _failed = new();
    private readonly int _maxJobs = Math.Max(1, System.Environment.ProcessorCount - 1);
    private ShaderMaterial _terrain = null!, _water = null!, _farTree = null!;
    private Mesh _grass = null!, _card = null!;
    private TreeCatalog _trees = null!;
    private readonly PropCatalog _props = new();

    public override void _Ready()
    {
        _terrain = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/Terrain.gdshader") };
        _terrain.SetShaderParameter("rock_tex", RockTextureArray.Get());
        _water = new ShaderMaterial { Shader = WaterShader() };
        // Water style (classic / experimental): one material, the shader swapped in place, so every uniform the
        // atmosphere sets (waves, sun, reflections) carries over and existing tiles switch at once.
        GameSettings.Changed += s => { if (s == GameSettings.WaterStyle) _water.Shader = WaterShader(); };
        _trees = new TreeCatalog();
        _farTree = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/FarTree.gdshader") };
        _grass = Visuals.GrassMesh();
        // Rewrite look: translucent, breeze-bent grass (the valley game keeps its own tuft material).
        _grass.SurfaceSetMaterial(0, new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/Grass.gdshader") });
        // Unit card: x in [-0.5, 0.5], y in [0, 1]; the shader faces it to the camera and scales it per instance.
        var card = new ArrayMesh();
        using (var arrays = new Godot.Collections.Array())
        {
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = new[] { new Vector3(-.5f, 0, 0), new Vector3(.5f, 0, 0), new Vector3(.5f, 1, 0), new Vector3(-.5f, 1, 0) };
            arrays[(int)Mesh.ArrayType.TexUV] = new[] { new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0) };
            arrays[(int)Mesh.ArrayType.Normal] = new[] { Vector3.Back, Vector3.Back, Vector3.Back, Vector3.Back };
            arrays[(int)Mesh.ArrayType.Index] = new[] { 0, 2, 1, 0, 3, 2 };
            card.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        }
        // Cards are moved in the vertex shader; give the mesh a generous box so culling never drops them.
        card.CustomAabb = new Aabb(new Vector3(-40, -5, -40), new Vector3(80, 60, 80));
        _card = card;
    }

    public ShaderMaterial WaterMaterial => _water;

    private static Shader WaterShader() => GD.Load<Shader>(GameSettings.WaterStyle.Index switch
    {
        1 => "res://Game/Rewrite/WaterExperimental.gdshader",
        2 => "res://Game/Rewrite/WaterExperimental2.gdshader",
        _ => "res://Game/Rewrite/Water.gdshader",
    });

    public void ShowGrid(bool show) => _terrain.SetShaderParameter("show_grid", show);
    public void ShowBiomes(bool show) => _terrain.SetShaderParameter("biome_debug", show ? 1f : 0f);

    public void Configure(TerrainSampler sampler, double originX, double originZ)
    {
        _revision++;
        foreach (var node in _tiles.Values) { RemoveChild(node); node.QueueFree(); }
        _tiles.Clear(); _tileTriangles.Clear(); _tileRevision.Clear(); _failed.Clear(); _desired.Clear();
        _config = DetailConfig.For(sampler); Levels = 7 + GameSettings.TerrainDistance.Index;
        _sampler = sampler; _originX = originX; _originZ = originZ; Triangles = 0; Built = 0; MaxCommitMs = 0;
    }

    /// <summary>
    /// Detail or distance settings changed: rebuild every tile in the background. Old tiles stay on screen until
    /// their replacements are committed, so there are no holes.
    /// </summary>
    public void Refresh()
    {
        _revision++;
        if (_sampler != null) _config = DetailConfig.For(_sampler);
        Levels = 7 + GameSettings.TerrainDistance.Index;
        _failed.Clear();
    }

    /// <summary>The ground changed inside a world rectangle (terrain edit): rebuild the tiles there at the nearest
    /// levels (coarser tiles would barely change). Old tiles stay until replaced.</summary>
    public void Invalidate(double minX, double minZ, double maxX, double maxZ, int maxLevel = 2)
    {
        foreach (var key in _tiles.Keys)
            if (key.Level <= maxLevel && key.MinX <= maxX && key.MinX + key.Size >= minX && key.MinZ <= maxZ && key.MinZ + key.Size >= minZ)
            {
                _tileRevision[key] = -1;
                if (_jobs.TryGetValue(key, out var job)) _jobs[key] = (job.Task, -1);
            }
    }

    /// <summary>Move the float origin; every resident tile is repositioned (no rebuild).</summary>
    public void Rebase(double originX, double originZ)
    {
        _originX = originX; _originZ = originZ;
        foreach (var (key, node) in _tiles) node.Position = new Vector3((float)(key.MinX - _originX), 0, (float)(key.MinZ - _originZ));
    }

    /// <summary>True once a collision tile under (x, z) is resident.</summary>
    public bool HasGround(double x, double z) =>
        _tiles.ContainsKey(new TileKey(0, (long)Math.Floor(x / TileBuilder.BaseTile), (long)Math.Floor(z / TileBuilder.BaseTile)));

    public void Update(double px, double pz)
    {
        if (_sampler == null) return;
        Desired(px, pz);
        var timer = Stopwatch.StartNew();

        // Commit finished builds, nearest levels first, within a frame budget.
        foreach (var key in _jobs.Where(j => j.Value.Task.IsCompleted).Select(j => j.Key).OrderBy(k => k.Level).ToList())
        {
            if (timer.Elapsed.TotalMilliseconds > 6) break;
            var (task, revision) = _jobs[key];
            _jobs.Remove(key);
            if (task.IsFaulted) { _failed.Add(key); GD.PushError(task.Exception?.ToString() ?? "tile build failed"); continue; }
            if (revision != _revision || !_desired.Contains(key)) continue;
            var commit = Stopwatch.StartNew();
            var node = Commit(task.Result);
            LastBuildMs = task.Result.BuildMs;
            if (_tiles.Remove(key, out var old)) { RemoveChild(old); old.QueueFree(); Triangles -= _tileTriangles[key]; }
            AddChild(node);
            _tiles[key] = node; _tileRevision[key] = revision;
            long tris = task.Result.Indices.Length / 3;
            _tileTriangles[key] = tris; Triangles += tris; Built++;
            LastCommitMs = commit.Elapsed.TotalMilliseconds; MaxCommitMs = Math.Max(MaxCommitMs, LastCommitMs);
        }

        // Schedule missing tiles: finest level first, then nearest.
        var missing = _desired.Where(k => (!_tiles.ContainsKey(k) || _tileRevision[k] != _revision) && !_failed.Contains(k)).ToList();
        Missing = missing.Count;
        if (_jobs.Count < _maxJobs)
        {
            var sampler = _sampler; long revision = _revision; var config = _config;
            foreach (var key in missing.Where(k => !_jobs.ContainsKey(k) || _jobs[k].Revision != _revision)
                         .OrderBy(k => k.Level).ThenBy(k => Distance(k, px, pz)).Take(_maxJobs - _jobs.Count))
            {
                if (_jobs.TryGetValue(key, out var stale)) { if (!stale.Task.IsCompleted) continue; _jobs.Remove(key); }
                _jobs[key] = (Task.Run(() => TileBuilder.Build(sampler, key, config)), revision);
            }
        }

        // Drop tiles that are no longer wanted once everything wanted is on screen (no holes while moving).
        if (Missing == 0)
            foreach (var key in _tiles.Keys.Where(k => !_desired.Contains(k)).ToList())
            {
                var node = _tiles[key]; RemoveChild(node); node.QueueFree();
                _tiles.Remove(key); Triangles -= _tileTriangles[key]; _tileTriangles.Remove(key); _tileRevision.Remove(key);
            }
    }

    private static double Distance(TileKey k, double px, double pz)
    {
        double cx = k.MinX + k.Size / 2, cz = k.MinZ + k.Size / 2;
        return Math.Max(Math.Abs(cx - px), Math.Abs(cz - pz));
    }

    /// <summary>
    /// Nested rings. Top level: (2R+1)² tiles around the player. Every level below fills the 3×3 hole around the
    /// player's tile of the level above with its 6×6 children; level 0 keeps its whole 6×6 region.
    /// </summary>
    private void Desired(double px, double pz)
    {
        _desired.Clear();
        int top = Levels - 1;
        double size = TileBuilder.BaseTile * (1L << top);
        long cx = (long)Math.Floor(px / size), cz = (long)Math.Floor(pz / size);
        long x0 = cx - TopRadius, x1 = cx + TopRadius, z0 = cz - TopRadius, z1 = cz + TopRadius;
        for (int level = top; level >= 0; level--)
        {
            size = TileBuilder.BaseTile * (1L << level);
            cx = (long)Math.Floor(px / size); cz = (long)Math.Floor(pz / size);
            for (long z = z0; z <= z1; z++)
                for (long x = x0; x <= x1; x++)
                {
                    bool hole = level > 0 && Math.Abs(x - cx) <= 1 && Math.Abs(z - cz) <= 1;
                    if (!hole) _desired.Add(new TileKey(level, x, z));
                }
            // Next finer level covers this level's hole exactly.
            x0 = 2 * (cx - 1); x1 = 2 * (cx + 1) + 1; z0 = 2 * (cz - 1); z1 = 2 * (cz + 1) + 1;
        }
    }

    private Node3D Commit(TileData data)
    {
        var key = data.Key;
        var root = new Node3D { Name = $"T{key.Level}_{key.X}_{key.Z}", Position = new Vector3((float)(key.MinX - _originX), 0, (float)(key.MinZ - _originZ)) };
        using (var arrays = new Godot.Collections.Array())
        {
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = data.Vertices;
            arrays[(int)Mesh.ArrayType.Normal] = data.Normals;
            arrays[(int)Mesh.ArrayType.Color] = data.Colors;
            arrays[(int)Mesh.ArrayType.TexUV] = data.Uv;
            arrays[(int)Mesh.ArrayType.TexUV2] = data.Uv2;
            arrays[(int)Mesh.ArrayType.Custom0] = data.Biome0;
            arrays[(int)Mesh.ArrayType.Custom1] = data.Biome1;
            arrays[(int)Mesh.ArrayType.Custom2] = data.Biome2;
            arrays[(int)Mesh.ArrayType.Custom3] = data.Biome3;
            arrays[(int)Mesh.ArrayType.Index] = data.Indices;
            var mesh = new ArrayMesh();
            const int rgbaFloat = (int)Mesh.ArrayCustomFormat.RgbaFloat;
            var format = (Mesh.ArrayFormat)(((long)rgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift) | ((long)rgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift)
                | ((long)rgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift) | ((long)rgbaFloat << (int)Mesh.ArrayFormat.FormatCustom3Shift));
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null, format);
            root.AddChild(new MeshInstance3D
            {
                Mesh = mesh, MaterialOverride = _terrain,
                CastShadow = key.Level <= 1 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
            });
        }
        if (data.Water is { } water && water.Indices.Length > 0)
        {
            using var arrays = new Godot.Collections.Array();
            arrays.Resize((int)Mesh.ArrayType.Max);
            arrays[(int)Mesh.ArrayType.Vertex] = water.Vertices;
            arrays[(int)Mesh.ArrayType.Color] = water.Colors;
            arrays[(int)Mesh.ArrayType.TexUV] = water.Flow;
            if (data.WaterTint is { } tint && tint.Length == water.Vertices.Length) arrays[(int)Mesh.ArrayType.TexUV2] = tint;
            else arrays[(int)Mesh.ArrayType.TexUV2] = new Vector2[water.Vertices.Length];
            arrays[(int)Mesh.ArrayType.Normal] = Enumerable.Repeat(Vector3.Up, water.Vertices.Length).ToArray();
            arrays[(int)Mesh.ArrayType.Index] = water.Indices;
            var mesh = new ArrayMesh();
            if (data.WaterShore is { } shore && shore.Length == water.Vertices.Length * 4)
            {
                arrays[(int)Mesh.ArrayType.Custom0] = shore;
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays, null, null,
                    (Mesh.ArrayFormat)((long)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift));
            }
            else mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
            root.AddChild(new MeshInstance3D { Mesh = mesh, MaterialOverride = _water, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        }
        if (data.CollisionFaces != null)
        {
            var body = new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
            body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = data.CollisionFaces, BackfaceCollision = true } });
            ObjectCollision.Add(body, data);
            root.AddChild(body);
        }
        if (data.Trees.Count > 0) _trees.Instance(root, data.Trees, key.Level >= 2 ? 2 : 1, key.Level == 0 && GameSettings.Shadows.Index > 0);
        GrassCommitted += data.Grass.Count; RocksCommitted += data.Rocks.Count; PropsCommitted += data.Props.Count; FarTreesCommitted += data.FarTrees.Count;
        if (data.Grass.Count > 0)
            root.AddChild(Multi(_grass, data.Grass.ToArray(), GeometryInstance3D.ShadowCastingSetting.Off, GameSettings.GrassDistance.F));
        if (data.Reeds.Count > 0)
            root.AddChild(Multi(_grass, data.Reeds.ToArray(), GeometryInstance3D.ShadowCastingSetting.Off, GameSettings.GrassDistance.F * 1.3f));
        foreach (var kind in data.Drapes.GroupBy(d => d.Kind))
            root.AddChild(Multi(PlaceholderPlants.Drape(kind.Key), kind.Select(d => d.Transform).ToArray(), GeometryInstance3D.ShadowCastingSetting.Off,
                160 * GameSettings.ScatterDistance.F));
        foreach (var kind in data.WetPlants.GroupBy(d => d.Kind))
            root.AddChild(Multi(PlaceholderPlants.WetPlant(kind.Key), kind.Select(d => d.Transform).ToArray(), GeometryInstance3D.ShadowCastingSetting.Off,
                (kind.Key == PlaceholderPlants.BogShrub ? 220 : 150) * GameSettings.ScatterDistance.F));
        foreach (var kind in data.Rocks.GroupBy(r => r.Kind))
            root.AddChild(Multi(RockMeshes.Get(kind.Key), kind.Select(r => r.Transform).ToArray(),
                kind.Key == GroundCover.Stone ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
                kind.Key == GroundCover.Stone ? 90 * GameSettings.ScatterDistance.F : kind.Key == GroundCover.Rock ? 400 * GameSettings.ScatterDistance.F : 0,
                kind.Select(r => r.Tint.A > 0 ? r.Tint : Colors.White).ToArray()));
        foreach (var kind in data.Props.GroupBy(p => p.Kind))
        {
            var transforms = kind.Select(p => p.Transform).ToArray();
            foreach (var (mesh, part) in _props.Parts(kind.Key))
                root.AddChild(Multi(mesh, transforms.Select(t => t * part).ToArray(),
                    key.Level == 0 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off, 260 * GameSettings.ScatterDistance.F));
        }
        if (data.FarTrees.Count > 0)
        {
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = _card, InstanceCount = data.FarTrees.Count };
            for (int i = 0; i < data.FarTrees.Count; i++)
            {
                var t = data.FarTrees[i];
                multi.SetInstanceTransform(i, new Transform3D(Basis.Identity.Scaled(new Vector3(1, t.Height, 1)), t.Position));
                multi.SetInstanceCustomData(i, new Color(t.Crown.R, t.Crown.G, t.Crown.B, t.Conifer ? 1 : 0));
            }
            root.AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = _farTree, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = 2 });
        }
        return root;
    }

    private static MultiMeshInstance3D Multi(Mesh mesh, Transform3D[] transforms, GeometryInstance3D.ShadowCastingSetting shadows, float visibleTo, Color[]? colours = null)
    {
        // Visibility ranges are measured from the node's origin, and tile roots sit at sea level (possibly hundreds of
        // metres below the ground): place the node at the centre of its instances instead.
        var centre = Vector3.Zero;
        foreach (var t in transforms) centre += t.Origin;
        if (transforms.Length > 0) centre /= transforms.Length;
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colours != null, Mesh = mesh, InstanceCount = transforms.Length };
        for (int i = 0; i < transforms.Length; i++) multi.SetInstanceTransform(i, transforms[i].Translated(-centre));
        if (colours != null) for (int i = 0; i < transforms.Length; i++) multi.SetInstanceColor(i, colours[i]);
        // Render layer 2: objects (tool previews project onto the terrain only, layer 1).
        var node = new MultiMeshInstance3D { Multimesh = multi, CastShadow = shadows, Position = centre, Layers = 2 };
        if (visibleTo > 0) { node.VisibilityRangeEnd = visibleTo; node.VisibilityRangeEndMargin = 15; node.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self; }
        return node;
    }

    public override void _ExitTree()
    {
        _revision++;
        foreach (var (task, _) in _jobs.Values) _ = task.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
        // Let running tile builds finish (a few ms each) so no worker is inside managed code while the engine shuts
        // down (an intermittent exit-time crash seen once under Wine).
        var running = _jobs.Values.Select(j => j.Task).Where(t => !t.IsCompleted).ToArray();
        try { if (running.Length > 0) Task.WaitAll(running, 3000); } catch (AggregateException) { }
    }
}

/// <summary>Tree meshes per species and LOD, extracted once from the woodland pack (or built for placeholder plants) and
/// drawn with MultiMesh.</summary>
public sealed class TreeCatalog
{
    private readonly Dictionary<(int Species, int Lod), List<(Mesh Mesh, Transform3D Transform)>> _parts = new();

    private List<(Mesh, Transform3D)> Parts(int species, int lod)
    {
        if (_parts.TryGetValue((species, lod), out var parts)) return parts;
        if (PlaceholderPlants.Has(Vegetation.Species[species]))
            return _parts[(species, lod)] = PlaceholderPlants.Build(Vegetation.Species[species], lod);
        parts = new List<(Mesh, Transform3D)>();
        var tree = WoodlandAssets.Tree(Vegetation.Species[species]);
        foreach (var source in WoodlandAssets.Meshes(tree.Levels[lod]))
        {
            var mesh = (Mesh)source.Mesh.Duplicate();
            for (int s = 0; s < mesh.GetSurfaceCount(); s++) mesh.SurfaceSetMaterial(s, source.GetActiveMaterial(s));
            Transform3D transform = source.Transform; Node? parent = source.GetParent();
            while (parent is Node3D p && p != tree) { transform = p.Transform * transform; parent = p.GetParent(); }
            parts.Add((mesh, transform));
        }
        tree.Free();
        _parts[(species, lod)] = parts;
        return parts;
    }

    public void Instance(Node3D root, List<TreeInstance> trees, int lod, bool shadows)
    {
        foreach (var group in trees.GroupBy(t => t.Species))
        {
            var transforms = group.Select(t => t.Transform).ToArray();
            foreach (var (mesh, part) in Parts(group.Key, lod))
            {
                var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = transforms.Length };
                for (int i = 0; i < transforms.Length; i++) multi.SetInstanceTransform(i, transforms[i] * part);
                root.AddChild(new MultiMeshInstance3D
                {
                    Multimesh = multi, Layers = 2,
                    CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
        }
    }
}
