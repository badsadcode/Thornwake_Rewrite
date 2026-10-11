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
    /// <summary>Main-thread time spent committing finished tiles in the last <see cref="Update"/> (benchmark).</summary>
    public double FrameCommitMs { get; private set; }
    /// <summary>Last Update's commit time split up (benchmark): terrain and water meshes, collision shapes, objects
    /// (trees, grass, rocks, cards), adding the tiles to the scene (physics bodies register here), the tree field.</summary>
    public double CommitMeshMs { get; private set; }
    public double CommitCollisionMs { get; private set; }
    public double CommitObjectsMs { get; private set; }
    public double CommitEnterMs { get; private set; }
    public double CommitFieldMs => _field.FrameCommitMs;
    /// <summary>Of <see cref="CommitCollisionMs"/>: the ground's triangle shapes (the rest: trees, rocks, logs, bushes).</summary>
    public double CommitGroundMs { get; private set; }
    private readonly Dictionary<TileKey, PendingCollisionTile> _pendingCollision = new();

    /// <summary>A level-0 tile whose collision is being built, a piece per step.</summary>
    private sealed class PendingCollisionTile(Node3D root, TileData data)
    {
        public readonly Node3D Root = root;
        public readonly TileData Data = data;
        public StaticBody3D? Body;
        public int Next; // next ground piece
        public bool GroundDone; // the body (ground) is in the scene; trunks, rocks, logs and bushes follow in batches
        public IEnumerator<CollisionShape3D>? Objects;
    }

    // Object shapes added per step (a dense forest tile has ~1,000; all at once took up to 15 ms).
    private const int ObjectBatch = 128;

    // The ground's triangle shape is built in pieces of this many triangles (a whole 64 m tile, 8,192 triangles, took
    // ~18 ms in one go: a hitch whenever a tile arrived).
    private const int GroundPieceTriangles = 1024;

    private void DropCollision(TileKey key)
    {
        if (!_pendingCollision.Remove(key, out var p)) return;
        p.Objects?.Dispose();
        if (!p.GroundDone) p.Body?.Free(); // a half-built body is not in the scene yet (otherwise it goes with its tile)
    }
    public int PendingCollision => _pendingCollision.Count;
    public long Triangles { get; private set; }
    /// <summary>Instances committed so far (diagnostics): grass tufts, rocks, props, far tree cards.</summary>
    public long GrassCommitted { get; private set; }
    public long RocksCommitted { get; private set; }
    public long PropsCommitted { get; private set; }
    public long FarTreesCommitted { get; private set; }
    public bool Settled => _sampler != null && Missing == 0 && _jobs.Count == 0 && _pendingCollision.Count == 0 && (!TreeField.Enabled || (Impostors.Ready && _field.Settled));
    public TreeImpostors Impostors { get; } = new();
    private TreeField _field = null!;
    public TreeField Field => _field;
    public void ResetCommitStats() => MaxCommitMs = 0;

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
    private readonly int _maxJobs = Background.TileJobs;
    private ShaderMaterial _terrain = null!, _water = null!, _farTree = null!;
    private Mesh _grass = null!, _card = null!;
    private TreeCatalog _trees = null!;
    private readonly PropCatalog _props = new();

    public override void _Ready()
    {
        _terrain = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/Terrain.gdshader") };
        _terrain.SetShaderParameter("rock_tex", RockTextureArray.Get());
        // Ground style (current / Noor's textures): a uniform, switched in place.
        _terrain.SetShaderParameter("ground_tex", GroundTextures.Get());
        _terrain.SetShaderParameter("ground_tex_lin", GroundTextures.Get());
        _terrain.SetShaderParameter("ground_mean", GroundTextures.Means);
        _terrain.SetShaderParameter("ground_variety", GameSettings.GroundVariety.F);
        _terrain.SetShaderParameter("ground_relief", GameSettings.GroundRelief.F);
        _terrain.SetShaderParameter("ground_parallax", GameSettings.GroundParallax.F);
        _terrain.SetShaderParameter("ground_style", GameSettings.GroundTextures.Index);
        _terrain.SetShaderParameter("flora_tint", GameSettings.GrassStyle.Index == 1 ? 1f : 0f);
        _water = new ShaderMaterial { Shader = WaterShader() };
        // Water style (classic / experimental): one material, the shader swapped in place, so every uniform the
        // atmosphere sets (waves, sun, reflections) carries over and existing tiles switch at once.
        GameSettings.Changed += s =>
        {
            if (s == GameSettings.WaterStyle) _water.Shader = WaterShader();
            if (s == GameSettings.GroundTextures) _terrain.SetShaderParameter("ground_style", GameSettings.GroundTextures.Index);
            if (s == GameSettings.GroundVariety) _terrain.SetShaderParameter("ground_variety", GameSettings.GroundVariety.F);
            if (s == GameSettings.GroundRelief) _terrain.SetShaderParameter("ground_relief", GameSettings.GroundRelief.F);
            if (s == GameSettings.GroundParallax) _terrain.SetShaderParameter("ground_parallax", GameSettings.GroundParallax.F);
        };
        _trees = new TreeCatalog();
        Impostors.Bake(this, _trees);
        _field = new TreeField(_trees, Impostors);
        AddChild(_field);
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
        3 => RenderingServer.GetRenderingDevice() != null ? "res://Game/Rewrite/WaterExperimental3.gdshader" : "res://Game/Rewrite/WaterExperimental2.gdshader",
        4 => RenderingServer.GetRenderingDevice() != null ? "res://Game/Rewrite/WaterExperimental3A.gdshader" : "res://Game/Rewrite/WaterExperimental2.gdshader",
        5 => RenderingServer.GetRenderingDevice() != null ? "res://Game/Rewrite/WaterExperimental4.gdshader" : "res://Game/Rewrite/WaterExperimental2.gdshader",
        _ => "res://Game/Rewrite/Water.gdshader",
    });

    public void ShowGrid(bool show) => _terrain.SetShaderParameter("show_grid", show);
    public void ShowBiomes(bool show) => _terrain.SetShaderParameter("biome_debug", show ? 1f : 0f);

    public void Configure(TerrainSampler sampler, double originX, double originZ)
    {
        _revision++;
        foreach (var node in _tiles.Values) { RemoveChild(node); node.QueueFree(); }
        foreach (var p in _pendingCollision.Values) p.Body?.Free(); // never added to the scene
        _pendingCollision.Clear();
        _tiles.Clear(); _tileTriangles.Clear(); _tileRevision.Clear(); _failed.Clear(); _desired.Clear();
        _config = DetailConfig.For(sampler); Levels = 7 + GameSettings.TerrainDistance.Index;
        _sampler = sampler; _originX = originX; _originZ = originZ; Triangles = 0; Built = 0; MaxCommitMs = 0;
        _field.Configure(sampler, _config, originX, originZ);
        _terrain.SetShaderParameter("sea_level", (float)sampler.Plan.Profile.SeaLevel);
        _terrain.SetShaderParameter("flora_tint", GameSettings.GrassStyle.Index == 1 ? 1f : 0f);
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
        if (TreeField.Enabled) _field.Refresh(_config); else _field.Clear();
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
        _field.Invalidate(minX, minZ, maxX, maxZ);
    }

    /// <summary>Move the float origin; every resident tile is repositioned (no rebuild).</summary>
    public void Rebase(double originX, double originZ)
    {
        _originX = originX; _originZ = originZ;
        foreach (var (key, node) in _tiles) node.Position = new Vector3((float)(key.MinX - _originX), 0, (float)(key.MinZ - _originZ));
        _field.Rebase(originX, originZ);
    }

    /// <summary>True once a collision tile under (x, z) is resident.</summary>
    public bool HasGround(double x, double z)
    {
        var key = new TileKey(0, (long)Math.Floor(x / TileBuilder.BaseTile), (long)Math.Floor(z / TileBuilder.BaseTile));
        return _tiles.ContainsKey(key) && (!_pendingCollision.TryGetValue(key, out var p) || p.GroundDone);
    }

    public void Update(double px, double pz)
    {
        if (_sampler == null) return;
        _field.Update(px, pz);
        Desired(px, pz);
        var timer = Stopwatch.StartNew();
        CommitMeshMs = CommitCollisionMs = CommitObjectsMs = CommitEnterMs = CommitGroundMs = 0;

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
            long enter = Stopwatch.GetTimestamp();
            AddChild(node);
            CommitEnterMs += Stopwatch.GetElapsedTime(enter).TotalMilliseconds;
            _tiles[key] = node; _tileRevision[key] = revision;
            long tris = task.Result.Indices.Length / 3;
            _tileTriangles[key] = tris; Triangles += tris; Built++;
            LastCommitMs = commit.Elapsed.TotalMilliseconds; MaxCommitMs = Math.Max(MaxCommitMs, LastCommitMs);
        }
        // Collision for committed level-0 tiles: the nearest every frame, more while there is time left.
        if (_pendingCollision.Count > 0)
        {
            foreach (var key in _pendingCollision.Keys.OrderBy(k => Distance(k, px, pz)).ToList())
            {
                var p = _pendingCollision[key];
                if (!GodotObject.IsInstanceValid(p.Root) || !_tiles.TryGetValue(key, out var current) || current != p.Root) { DropCollision(key); continue; }
                // The same collision as build 43 (Tom, build 44: heightmap ground and server-side shapes made rocks push
                // the player back and let the player walk up slopes that could not be climbed before): triangle shapes
                // for the ground, a CollisionShape3D per trunk, rock, log and bush. Only *when* it is built changed:
                // after the tile appears, in pieces, nearest tile first; the ground under the player waits for it.
                // Over the frame's budget, only the tile the player is on (or next to) still gets a step.
                bool urgent = Distance(key, px, pz) < TileBuilder.BaseTile * 1.5;
                bool Over() => (CommitCollisionMs > 0 || !urgent) && timer.Elapsed.TotalMilliseconds > 8;
                var faces = p.Data.CollisionFaces!;
                int pieces = (faces.Length / 3 + GroundPieceTriangles - 1) / GroundPieceTriangles;
                while (p.Next < pieces)
                {
                    if (Over()) goto budget;
                    long t0 = Stopwatch.GetTimestamp();
                    p.Body ??= new StaticBody3D { CollisionLayer = 1, CollisionMask = 0 };
                    int start = p.Next * GroundPieceTriangles * 3, count = Math.Min(GroundPieceTriangles * 3, faces.Length - start);
                    p.Body.AddChild(new CollisionShape3D { Shape = new ConcavePolygonShape3D { Data = faces[start..(start + count)], BackfaceCollision = true } });
                    p.Next++;
                    double ms = Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                    CommitGroundMs += ms; CommitCollisionMs += ms;
                }
                if (!p.GroundDone)
                {
                    long t0 = Stopwatch.GetTimestamp();
                    p.Root.AddChild(p.Body);
                    p.GroundDone = true;
                    p.Objects = ObjectCollision.Shapes(p.Data).GetEnumerator();
                    CommitCollisionMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                }
                while (true)
                {
                    if (Over()) goto budget;
                    long t0 = Stopwatch.GetTimestamp();
                    bool more = true;
                    for (int i = 0; i < ObjectBatch && (more = p.Objects!.MoveNext()); i++) p.Body!.AddChild(p.Objects.Current);
                    CommitCollisionMs += Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
                    if (!more) { p.Objects!.Dispose(); _pendingCollision.Remove(key); break; }
                }
            }
            budget:;
        }
        FrameCommitMs = timer.Elapsed.TotalMilliseconds;

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
                _jobs[key] = (Background.Run(() => TileBuilder.Build(sampler, key, config)), revision);
            }
        }

        // Drop tiles that are no longer wanted once everything wanted is on screen (no holes while moving).
        if (Missing == 0)
            foreach (var key in _tiles.Keys.Where(k => !_desired.Contains(k)).ToList())
            {
                var node = _tiles[key]; RemoveChild(node); node.QueueFree(); DropCollision(key);
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
        // The hole each level leaves for the next finer one: ±1 tile (build 32) up to ±4 (Terrain detail range): a
        // finer level then covers (4k+2)² children instead of 6×6, so every detail level reaches farther.
        int k = Math.Clamp((int)Math.Round(GameSettings.TerrainDetail.Value), 1, 4);
        for (int level = top; level >= 0; level--)
        {
            size = TileBuilder.BaseTile * (1L << level);
            cx = (long)Math.Floor(px / size); cz = (long)Math.Floor(pz / size);
            for (long z = z0; z <= z1; z++)
                for (long x = x0; x <= x1; x++)
                {
                    bool hole = level > 0 && Math.Abs(x - cx) <= k && Math.Abs(z - cz) <= k;
                    if (!hole) _desired.Add(new TileKey(level, x, z));
                }
            // Next finer level covers this level's hole exactly.
            x0 = 2 * (cx - k); x1 = 2 * (cx + k) + 1; z0 = 2 * (cz - k); z1 = 2 * (cz + k) + 1;
        }
    }

    private Node3D Commit(TileData data)
    {
        var key = data.Key;
        long stage = Stopwatch.GetTimestamp();
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
        CommitMeshMs += Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        stage = Stopwatch.GetTimestamp();
        // Collision is built afterwards, nearest tile first, a little per frame (round 30c: building it here made
        // every level-0 tile a 10–36 ms stall while moving).
        DropCollision(key); // a rebuilt tile replaces the old one's unfinished collision
        if (data.CollisionFaces != null) _pendingCollision[key] = new PendingCollisionTile(root, data);
        CommitCollisionMs += Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        stage = Stopwatch.GetTimestamp();
        root.SetMeta("cards", data.FarTrees.Count); // (benchmark counts)
        // With the tree field the tile's trees are only for collision; the field draws them.
        if (!TreeField.Enabled) root.SetMeta("trees", data.Trees.Count);
        if (data.Trees.Count > 0 && !TreeField.Enabled) _trees.Instance(root, data.Trees, key.Level >= 2 ? 2 : 1, key.Level == 0 && GameSettings.Shadows.Index > 0,
            key.Level >= 2 ? RenderLayers.TreesFar : RenderLayers.TreesNear);
        GrassCommitted += data.Grass.Count; RocksCommitted += data.Rocks.Count; PropsCommitted += data.Props.Count; FarTreesCommitted += data.FarTrees.Count;
        // Grass and plant shadows (option): only where the sun casts any.
        var plantShadows = GameSettings.PlantShadows.On && GameSettings.Shadows.Index > 0
            ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
        if (data.Grass.Count > 0)
            root.AddChild(Multi(_grass, data.Grass.ToArray(), plantShadows, GameSettings.GrassDistance.F, layer: RenderLayers.Plants));
        if (data.Reeds.Count > 0)
            root.AddChild(Multi(_grass, data.Reeds.ToArray(), plantShadows, GameSettings.GrassDistance.F * 1.3f, layer: RenderLayers.Plants));
        if (data.Flora.Count > 0)
        {
            // Land grass, shore plants, floating leaves and underwater plants: one draw each, all sprites in one array.
            float grass = GameSettings.GrassDistance.F, scatter = GameSettings.ScatterDistance.F;
            // Floating and underwater plants in 32 m quarters: a visibility range is measured from the node, so one
            // node per 64 m tile switched a whole lake bed off at once (Tom, build 28: "in the lake they disappear").
            int Kind(FloraInstance f) => f.Species <= Flora.WheatGolden ? 0 : f.Species <= Flora.Flowering ? 1 : Flora.All[f.Species].Form == Flora.Form.Floating ? 2 : 3;
            int Quarter(FloraInstance f) => Kind(f) < 2 ? 0 : (f.Transform.Origin.X >= 32 ? 1 : 0) + (f.Transform.Origin.Z >= 32 ? 2 : 0);
            foreach (var group in data.Flora.GroupBy(f => (Kind(f), Quarter(f))))
            {
                var items = group.ToArray();
                int kind = group.Key.Item1;
                // (floors: at view.scatter 0.6 the underwater range was 42 m, and turning the camera, which orbits
                // the player, moved it across that line — Tom's dumps 20261007-122642/122651)
                float range = kind switch { 0 => grass, 1 => grass * 1.3f, 2 => Math.Max(100, 180 * scatter), _ => Math.Max(70, 110 * scatter) };
                root.AddChild(MultiFlora(kind == 2 ? Flora.Flat() : Flora.Upright(), items, kind == 3 ? 2 : kind == 2 ? 0 : 1, range,
                    kind < 2 ? plantShadows : GeometryInstance3D.ShadowCastingSetting.Off));
            }
            GrassCommitted += data.Flora.Count;
        }
        foreach (var kind in data.Drapes.GroupBy(d => d.Kind))
            root.AddChild(Multi(PlaceholderPlants.Drape(kind.Key), kind.Select(d => d.Transform).ToArray(), GeometryInstance3D.ShadowCastingSetting.Off,
                160 * GameSettings.ScatterDistance.F, layer: RenderLayers.Plants));
        foreach (var kind in data.WetPlants.GroupBy(d => d.Kind))
            root.AddChild(Multi(PlaceholderPlants.WetPlant(kind.Key), kind.Select(d => d.Transform).ToArray(), GeometryInstance3D.ShadowCastingSetting.Off,
                (kind.Key == PlaceholderPlants.BogShrub ? 220 : 150) * GameSettings.ScatterDistance.F, layer: RenderLayers.Plants));
        foreach (var kind in data.Rocks.GroupBy(r => r.Kind))
            root.AddChild(Multi(RockMeshes.Get(kind.Key), kind.Select(r => r.Transform).ToArray(),
                kind.Key == GroundCover.Stone ? GeometryInstance3D.ShadowCastingSetting.Off : GeometryInstance3D.ShadowCastingSetting.On,
                kind.Key == GroundCover.Stone ? 90 * GameSettings.ScatterDistance.F : kind.Key == GroundCover.Rock ? 400 * GameSettings.ScatterDistance.F : 0,
                kind.Select(r => r.Tint.A > 0 ? r.Tint : Colors.White).ToArray(), RenderLayers.Rocks));
        foreach (var kind in data.Props.GroupBy(p => p.Kind))
        {
            var transforms = kind.Select(p => p.Transform).ToArray();
            foreach (var (mesh, part) in _props.Parts(kind.Key))
                root.AddChild(Multi(mesh, transforms.Select(t => t * part).ToArray(),
                    key.Level == 0 ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off, 260 * GameSettings.ScatterDistance.F,
                    layer: RenderLayers.Rocks));
        }
        if (data.FarTrees.Count > 0)
        {
            var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = _card };
            var far = data.FarTrees;
            MultiMeshFill.Fill(multi, far.Count, i => new Transform3D(Basis.Identity.Scaled(new Vector3(1, far[i].Height, 1)), far[i].Position),
                custom: i => new Color(far[i].Crown.R, far[i].Crown.G, far[i].Crown.B, far[i].Conifer ? 1 : 0));
            root.AddChild(new MultiMeshInstance3D { Multimesh = multi, MaterialOverride = _farTree, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, Layers = RenderLayers.TreeCards });
        }
        CommitObjectsMs += Stopwatch.GetElapsedTime(stage).TotalMilliseconds;
        return root;
    }

    private static MultiMeshInstance3D Multi(Mesh mesh, Transform3D[] transforms, GeometryInstance3D.ShadowCastingSetting shadows, float visibleTo, Color[]? colours = null,
        uint layer = RenderLayers.Objects)
    {
        // Visibility ranges are measured from the node's origin, and tile roots sit at sea level (possibly hundreds of
        // metres below the ground): place the node at the centre of its instances instead.
        var centre = Vector3.Zero;
        foreach (var t in transforms) centre += t.Origin;
        if (transforms.Length > 0) centre /= transforms.Length;
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = colours != null, Mesh = mesh };
        MultiMeshFill.Fill(multi, transforms.Length, i => transforms[i].Translated(-centre), colours != null ? i => colours[i] : null);
        var node = new MultiMeshInstance3D { Multimesh = multi, CastShadow = shadows, Position = centre, Layers = layer };
        if (visibleTo > 0) { node.VisibilityRangeEnd = visibleTo; node.VisibilityRangeEndMargin = 15; node.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self; }
        return node;
    }

    /// <summary>Plant sprites with per-instance tint (colour) and custom data (texture layer, motion kind, phase).</summary>
    private static MultiMeshInstance3D MultiFlora(Mesh mesh, FloraInstance[] items, int motion, float visibleTo,
        GeometryInstance3D.ShadowCastingSetting shadows = GeometryInstance3D.ShadowCastingSetting.Off)
    {
        var centre = Vector3.Zero;
        foreach (var f in items) centre += f.Transform.Origin;
        centre /= items.Length;
        var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh };
        MultiMeshFill.Fill(multi, items.Length, i => items[i].Transform.Translated(-centre), i => items[i].Tint,
            i => new Color(items[i].Species, motion, (float)Hashing.Unit(Hashing.Mix((ulong)i * 2654435761UL + (ulong)items[i].Species)), 0));
        var node = new MultiMeshInstance3D { Multimesh = multi, CastShadow = shadows, Position = centre, Layers = RenderLayers.Plants };
        if (visibleTo > 0) { node.VisibilityRangeEnd = visibleTo; node.VisibilityRangeEndMargin = 12; node.VisibilityRangeFadeMode = GeometryInstance3D.VisibilityRangeFadeModeEnum.Self; }
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

/// <summary>
/// Render layers of the world's objects. Layer 1 is terrain and water, the only layer tool previews project onto. Each
/// kind of object has its own layer, so the benchmark can hide one kind at a time through the camera's cull mask, with
/// no rebuild, and measure what it costs.
/// </summary>
public static class RenderLayers
{
    public const uint Terrain = 1, Objects = 2, TreesNear = 4, TreesFar = 8, TreeCards = 16, Plants = 32, Rocks = 64, TreeImpostors = 128;
}

/// <summary>Tree meshes per species and LOD, extracted once from the woodland pack (or built for placeholder plants) and
/// drawn with MultiMesh.</summary>
public sealed class TreeCatalog
{
    private readonly Dictionary<(int Species, int Lod), List<(Mesh Mesh, Transform3D Transform)>> _parts = new();

    public List<(Mesh Mesh, Transform3D Transform)> PartsOf(int species, int lod) => Parts(species, lod);

    private static readonly Dictionary<Material, Material> Barks = new();

    /// <summary>The pack's bark material as TreeBark.gdshader (same texture and tint), so trunks hand over to the
    /// impostors with the leaves (round 30). Leaves already use the shared canopy shader, which fades too.</summary>
    private static Material Rewrite(Material material)
    {
        if (material is not BaseMaterial3D b) return material;
        if (Barks.TryGetValue(material, out var done)) return done;
        var bark = new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/TreeBark.gdshader") };
        bark.SetShaderParameter("has_texture", b.AlbedoTexture != null);
        if (b.AlbedoTexture != null) bark.SetShaderParameter("albedo_tex", b.AlbedoTexture);
        bark.SetShaderParameter("tint", b.AlbedoColor);
        return Barks[material] = bark;
    }

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
            for (int s = 0; s < mesh.GetSurfaceCount(); s++) mesh.SurfaceSetMaterial(s, Rewrite(source.GetActiveMaterial(s)));
            Transform3D transform = source.Transform; Node? parent = source.GetParent();
            while (parent is Node3D p && p != tree) { transform = p.Transform * transform; parent = p.GetParent(); }
            parts.Add((mesh, transform));
        }
        tree.Free();
        _parts[(species, lod)] = parts;
        return parts;
    }

    public void Instance(Node3D root, List<TreeInstance> trees, int lod, bool shadows, uint layer = RenderLayers.TreesNear)
    {
        foreach (var group in trees.GroupBy(t => t.Species))
        {
            var transforms = group.Select(t => t.Transform).ToArray();
            foreach (var (mesh, part) in Parts(group.Key, lod))
            {
                var multi = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh };
                var p = part;
                MultiMeshFill.Fill(multi, transforms.Length, i => transforms[i] * p);
                root.AddChild(new MultiMeshInstance3D
                {
                    Multimesh = multi, Layers = layer,
                    CastShadow = shadows ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
                });
            }
        }
    }
}
