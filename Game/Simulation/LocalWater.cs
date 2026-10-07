using System;
using System.Collections.Generic;
using Thornwake.WorldGen;

namespace Thornwake.Hydraulics;

/// <summary>Ground for one simulation chunk: floor height per cell (absolute metres) and whether the cell lies in a
/// generated water body (river, lake, sea), which absorbs any simulated water that reaches it.</summary>
public readonly record struct ChunkGround(float[] Floor, bool[] Sink, float[]? Level = null, bool[]? Edited = null, bool[]? Sea = null);

/// <summary>
/// Local shallow-water simulation around the player (milestone M6), ported from GangTheGAME's WaterField
/// ("virtual pipes": each cell holds a depth, neighbours exchange flow driven by the difference in water surface
/// height; mass is conserved exactly apart from sources and sinks) and adapted to an endless world:
/// <list type="bullet">
/// <item>Only a window of <see cref="WindowChunks"/>² chunks (128 m at 0.5 m cells) around the player is simulated;
/// it moves in whole chunks. Chunks leaving the window keep their water in a dormant store and resume it when they
/// come back; the rest of the world costs nothing.</item>
/// <item>Ground comes from the deterministic terrain, interpolated exactly like the rendered level-0 mesh (1 m), so water lies
/// on what the player sees.</item>
/// <item>Generated rivers, lakes and the sea are sinks: water running into them joins them (and is counted).</item>
/// <item>Only the wet bounding box (plus a rim) is stepped.</item>
/// </list>
/// Engine-free and deterministic for a given input sequence (tests run headless).
/// </summary>
public sealed class LocalWater
{
    public const int ChunkCells = 32, WindowChunks = 8, Side = ChunkCells * WindowChunks;
    public const double Cell = .5, ChunkSize = ChunkCells * Cell;
    private const float Gravity = 9.8f, FluxDamping = .995f, Dry = 1e-5f, RenderTolerance = .004f, MaxSpeed = 5f;
    private const float CellArea = (float)(Cell * Cell);

    private readonly Func<long, long, ChunkGround> _load;
    private float[] _floor = new float[Side * Side], _depth = new float[Side * Side];
    private float[] _left = new float[Side * Side], _right = new float[Side * Side], _up = new float[Side * Side], _down = new float[Side * Side];
    private bool[] _sink = new bool[Side * Side];
    // Surface of the generated water body at sink cells: rivers, lakes and the sea are fixed-level reservoirs, so
    // water also flows *out* of them into lower ground next to them (a channel dug from a river fills).
    private float[] _level = new float[Side * Side];
    private bool[] _feeds = new bool[Side * Side];
    // Ground the player dug: the only place a river/lake/sea feeds water out to (generated banks are consistent by
    // construction; at 0.5 m they can still dip a hair below the water and would leak).
    private bool[] _edited = new bool[Side * Side];
    // Sea cells: the sea's own surface (waves) covers holes dug in its floor; this simulation never draws over it
    // (round 24, Tom: digging the seabed in a storm laid a flat, calm sheet over the waves along the dug path).
    private bool[] _sea = new bool[Side * Side];
    private int _feedMinX, _feedMaxX = -1, _feedMinZ, _feedMaxZ = -1;
    private float[] _shown = new float[Side * Side];
    // Ground that held water stays dark for a few minutes (1 = wet now, fades to 0), as in GangTheGAME.
    private float[] _wet = new float[Side * Side], _wetShown = new float[Side * Side];
    private float[] _soil = new float[Side * Side]; // water held in the soil (m), up to SoilCapacity
    private const float WetThreshold = .01f, DryingPerSecond = 1f / 150f;
    private float _dryClock;
    private readonly Dictionary<(long, long), float[]> _dormant = new();
    private readonly HashSet<(long, long)> _dirty = new();
    private int _minX, _maxX = -1, _minZ, _maxZ = -1;
    private bool _placed;

    /// <summary>World chunk coordinates of the window's minimum corner.</summary>
    public long OriginChunkX { get; private set; }
    public long OriginChunkZ { get; private set; }
    /// <summary>Water poured in, absorbed by generated water bodies, and run off the window's edge (m³).</summary>
    public double Added { get; private set; }
    public double Absorbed { get; private set; }
    public double Spilled { get; private set; }
    /// <summary>Water soaked into the ground (m³).</summary>
    public double Soaked { get; private set; }
    /// <summary>Water that rivers, lakes and the sea fed into lower ground beside them (m³).</summary>
    public double Drawn { get; private set; }
    /// <summary>
    /// Infiltration into dry soil (m/s of depth). Soil fills up: the rate falls with saturation (up to
    /// <see cref="SoilCapacity"/> metres of water per cell) to <see cref="MinInfiltration"/>, so after rain puddles
    /// and ponds last; saturated soil drains slowly again while no water stands on it.
    /// </summary>
    public float Infiltration { get; set; } = 1.7e-5f; // ~1 mm a minute into dry soil
    public float MinInfiltration { get; set; } = 1.5e-7f; // ~0.5 mm an hour into saturated soil
    public float SoilCapacity { get; set; } = .03f;
    public float SoilDrain { get; set; } = 1.2e-5f;      // saturation drains ~3 cm in ~40 min
    /// <summary>Evaporation from standing water (m/s): ponds dry over game days, never instantly.</summary>
    public float Evaporation { get; set; } = 8e-8f;      // ~0.3 mm an hour
    /// <summary>Rain over the whole window (m/s of water depth; 0 = dry).</summary>
    public float RainRate { get; set; }
    public sealed class Source { public double X, Z; public float Rate; }
    /// <summary>Point sources (springs, pipes): world position, m³/s.</summary>
    public List<Source> Sources { get; } = new();
    public int DormantChunks => _dormant.Count;

    public LocalWater(Func<long, long, ChunkGround> load) { _load = load; }

    /// <summary>Ground from the terrain sampler, matching the rendered level-0 mesh (1 m vertices, same triangles,
    /// same LOD filter), with generated water bodies as sinks.</summary>
    public static ChunkGround FromSampler(TerrainSampler sampler, long chunkX, long chunkZ)
    {
        const double MeshSpacing = 1;
        int vertices = (int)(ChunkSize / MeshSpacing) + 1;
        double x0 = chunkX * ChunkSize, z0 = chunkZ * ChunkSize;
        var height = new double[vertices * vertices];
        var level = new double[vertices * vertices];
        var oceanAt = new bool[vertices * vertices];
        for (int j = 0; j < vertices; j++)
            for (int i = 0; i < vertices; i++)
            {
                var t = sampler.Sample(x0 + i * MeshSpacing, z0 + j * MeshSpacing, MeshSpacing * 2);
                height[j * vertices + i] = t.Height;
                level[j * vertices + i] = t.Water != WaterKind.None ? t.WaterLevel : double.NaN;
                oceanAt[j * vertices + i] = t.Water == WaterKind.Ocean;
            }
        var floor = new float[ChunkCells * ChunkCells];
        var sink = new bool[ChunkCells * ChunkCells];
        var surface = new float[ChunkCells * ChunkCells];
        var edited = new bool[ChunkCells * ChunkCells];
        var sea = new bool[ChunkCells * ChunkCells];
        for (int cz = 0; cz < ChunkCells; cz++)
            for (int cx = 0; cx < ChunkCells; cx++)
            {
                double gx = (cx + .5) * Cell / MeshSpacing, gz = (cz + .5) * Cell / MeshSpacing;
                int i = (int)gx, j = (int)gz;
                double fx = gx - i, fz = gz - j;
                int a = j * vertices + i, b = a + 1, c = a + vertices, d = c + 1;
                // Same triangle split as the terrain mesh (a,b,c / b,d,c).
                double h = fx + fz <= 1 ? height[a] + (height[b] - height[a]) * fx + (height[c] - height[a]) * fz
                    : height[d] + (height[c] - height[d]) * (1 - fx) + (height[b] - height[d]) * (1 - fz);
                floor[cz * ChunkCells + cx] = (float)h;
                double l = Max(level[a], level[b], level[c], level[d]);
                sink[cz * ChunkCells + cx] = !double.IsNaN(l) && h < l;
                // The surface: the corners' average (the highest corner lifted water a little above a sloping river).
                if (!double.IsNaN(l)) l = Mean(level[a], level[b], level[c], level[d]);
                surface[cz * ChunkCells + cx] = double.IsNaN(l) ? float.NaN : (float)l;
                edited[cz * ChunkCells + cx] = sampler.Edits.Delta(x0 + (cx + .5) * Cell, z0 + (cz + .5) * Cell) < -.02;
                sea[cz * ChunkCells + cx] = sink[cz * ChunkCells + cx] && (oceanAt[a] || oceanAt[b] || oceanAt[c] || oceanAt[d]);
            }
        return new ChunkGround(floor, sink, surface, edited, sea);

        static double Mean(params double[] v)
        {
            double sum = 0; int count = 0;
            foreach (var x in v) if (!double.IsNaN(x)) { sum += x; count++; }
            return count > 0 ? sum / count : double.NaN;
        }

        static double Max(params double[] v)
        {
            double m = double.NaN;
            foreach (var x in v) if (!double.IsNaN(x) && (double.IsNaN(m) || x > m)) m = x;
            return m;
        }
    }

    private static int Index(int x, int z) => z * Side + x;

    /// <summary>Keep the window centred on (x, z): moves it in whole chunks when the point leaves the middle.</summary>
    public void Follow(double x, double z)
    {
        long cx = (long)Math.Floor(x / ChunkSize) - WindowChunks / 2, cz = (long)Math.Floor(z / ChunkSize) - WindowChunks / 2;
        if (_placed && Math.Abs(cx - OriginChunkX) <= 1 && Math.Abs(cz - OriginChunkZ) <= 1) return;
        MoveTo(cx, cz);
    }

    private void MoveTo(long ox, long oz)
    {
        var floor = new float[Side * Side]; var depth = new float[Side * Side]; var sink = new bool[Side * Side]; var level = new float[Side * Side]; var edited = new bool[Side * Side]; var seaCells = new bool[Side * Side];
        var left = new float[Side * Side]; var right = new float[Side * Side]; var up = new float[Side * Side]; var down = new float[Side * Side];
        var shown = new float[Side * Side]; var wet = new float[Side * Side]; var wetShown = new float[Side * Side]; var soil = new float[Side * Side];
        // Chunks leaving the window go dormant (if wet).
        if (_placed)
            for (int wz = 0; wz < WindowChunks; wz++)
                for (int wx = 0; wx < WindowChunks; wx++)
                {
                    long gx = OriginChunkX + wx, gz = OriginChunkZ + wz;
                    if (gx >= ox && gx < ox + WindowChunks && gz >= oz && gz < oz + WindowChunks) continue;
                    float[]? saved = null;
                    for (int j = 0; j < ChunkCells; j++)
                        for (int i = 0; i < ChunkCells; i++)
                        {
                            float d = _depth[Index(wx * ChunkCells + i, wz * ChunkCells + j)];
                            if (d <= Dry) continue;
                            saved ??= new float[ChunkCells * ChunkCells];
                            saved[j * ChunkCells + i] = d;
                        }
                    if (saved != null) _dormant[(gx, gz)] = saved;
                    _dirty.Add((gx, gz)); // its view must be removed
                }
        for (int wz = 0; wz < WindowChunks; wz++)
            for (int wx = 0; wx < WindowChunks; wx++)
            {
                long gx = ox + wx, gz = oz + wz;
                long px = gx - OriginChunkX, pz = gz - OriginChunkZ;
                bool kept = _placed && px >= 0 && px < WindowChunks && pz >= 0 && pz < WindowChunks;
                ChunkGround? ground = kept ? null : _load(gx, gz);
                _dormant.Remove((gx, gz), out var resumed);
                for (int j = 0; j < ChunkCells; j++)
                    for (int i = 0; i < ChunkCells; i++)
                    {
                        int n = Index(wx * ChunkCells + i, wz * ChunkCells + j);
                        if (kept)
                        {
                            int o = Index((int)px * ChunkCells + i, (int)pz * ChunkCells + j);
                            floor[n] = _floor[o]; depth[n] = _depth[o]; sink[n] = _sink[o]; shown[n] = _shown[o]; level[n] = _level[o]; edited[n] = _edited[o]; seaCells[n] = _sea[o];
                            wet[n] = _wet[o]; wetShown[n] = _wetShown[o]; soil[n] = _soil[o];
                            left[n] = _left[o]; right[n] = _right[o]; up[n] = _up[o]; down[n] = _down[o];
                        }
                        else
                        {
                            floor[n] = ground!.Value.Floor[j * ChunkCells + i];
                            sink[n] = ground.Value.Sink[j * ChunkCells + i];
                            level[n] = ground.Value.Level?[j * ChunkCells + i] ?? float.NaN;
                            edited[n] = ground.Value.Edited?[j * ChunkCells + i] ?? true;
                            seaCells[n] = ground.Value.Sea?[j * ChunkCells + i] ?? false;
                            depth[n] = resumed?[j * ChunkCells + i] ?? 0;
                            wet[n] = depth[n] > WetThreshold ? 1 : 0;
                            soil[n] = depth[n] > 0 ? SoilCapacity : 0;
                        }
                    }
                if (!kept && resumed != null) _dirty.Add((gx, gz));
            }
        _floor = floor; _depth = depth; _sink = sink; _level = level; _edited = edited; _sea = seaCells; _shown = shown; _wet = wet; _wetShown = wetShown; _soil = soil; _left = left; _right = right; _up = up; _down = down;
        OriginChunkX = ox; OriginChunkZ = oz; _placed = true;
        RecomputeBounds();
        RecomputeFeeders();
    }

    private void RecomputeBounds()
    {
        _minX = _minZ = int.MaxValue; _maxX = _maxZ = -1;
        for (int z = 0; z < Side; z++)
            for (int x = 0; x < Side; x++)
                if (_depth[Index(x, z)] > Dry) Grow(x, z);
        if (_maxX < 0) { _minX = _minZ = 0; }
    }

    private void Grow(int x, int z)
    {
        if (_maxX < _minX || _maxX < 0) { _minX = _maxX = x; _minZ = _maxZ = z; return; }
        _minX = Math.Min(_minX, x); _maxX = Math.Max(_maxX, x); _minZ = Math.Min(_minZ, z); _maxZ = Math.Max(_maxZ, z);
    }

    private bool ToCell(double x, double z, out int ix, out int iz)
    {
        ix = (int)Math.Floor(x / Cell - OriginChunkX * ChunkCells);
        iz = (int)Math.Floor(z / Cell - OriginChunkZ * ChunkCells);
        return _placed && ix >= 0 && iz >= 0 && ix < Side && iz < Side;
    }

    /// <summary>Pour water in at a world position (m³). Outside the window it is ignored.</summary>
    public void Add(double x, double z, double volume)
    {
        if (volume <= 0 || !ToCell(x, z, out int ix, out int iz)) return;
        _depth[Index(ix, iz)] += (float)(volume / CellArea);
        Added += volume;
        Grow(ix, iz);
    }

    /// <summary>The ground changed (terrain edit) inside a world rectangle: reload floor and sinks there, keep the water.</summary>
    public void ReloadGround(double minX, double minZ, double maxX, double maxZ)
    {
        if (!_placed) return;
        long c0x = (long)Math.Floor(minX / ChunkSize), c1x = (long)Math.Floor(maxX / ChunkSize);
        long c0z = (long)Math.Floor(minZ / ChunkSize), c1z = (long)Math.Floor(maxZ / ChunkSize);
        for (long gz = c0z; gz <= c1z; gz++)
            for (long gx = c0x; gx <= c1x; gx++)
            {
                if (!InWindow(gx, gz)) continue;
                var ground = _load(gx, gz);
                int bx = (int)(gx - OriginChunkX) * ChunkCells, bz = (int)(gz - OriginChunkZ) * ChunkCells;
                for (int j = 0; j < ChunkCells; j++)
                    for (int i = 0; i < ChunkCells; i++)
                    {
                        int n = Index(bx + i, bz + j);
                        _floor[n] = ground.Floor[j * ChunkCells + i];
                        _sink[n] = ground.Sink[j * ChunkCells + i];
                        _level[n] = ground.Level?[j * ChunkCells + i] ?? float.NaN;
                        _edited[n] = ground.Edited?[j * ChunkCells + i] ?? true;
                        _sea[n] = ground.Sea?[j * ChunkCells + i] ?? false;
                        if (_depth[n] > Dry) Grow(bx + i, bz + j);
                    }
                _dirty.Add((gx, gz));
                // Water standing next to the change must be stepped (it may now flow into a new channel).
                Grow(Math.Max(0, bx - 1), Math.Max(0, bz - 1)); Grow(Math.Min(Side - 1, bx + ChunkCells), Math.Min(Side - 1, bz + ChunkCells));
            }
        RecomputeFeeders();
    }

    /// <summary>Generated water cells next to lower dry ground: the only ones that can feed water out (usually none).</summary>
    private void RecomputeFeeders()
    {
        _feedMinX = _feedMinZ = int.MaxValue; _feedMaxX = _feedMaxZ = -1;
        for (int z = 0; z < Side; z++)
            for (int x = 0; x < Side; x++)
            {
                int i = Index(x, z);
                bool feeds = false;
                if (_sink[i] && !float.IsNaN(_level[i]))
                {
                    float top = _level[i] - .005f;
                    bool Opens(int n) => !_sink[n] && _edited[n] && _floor[n] < top;
                    feeds = x > 0 && Opens(i - 1) || x < Side - 1 && Opens(i + 1) || z > 0 && Opens(i - Side) || z < Side - 1 && Opens(i + Side);
                }
                _feeds[i] = feeds;
                if (feeds) { _feedMinX = Math.Min(_feedMinX, x); _feedMaxX = Math.Max(_feedMaxX, x); _feedMinZ = Math.Min(_feedMinZ, z); _feedMaxZ = Math.Max(_feedMaxZ, z); }
            }
    }

    /// <summary>Water depth at a world position (0 where dry or outside the window).</summary>
    public float DepthAt(double x, double z) => ToCell(x, z, out int ix, out int iz) ? _depth[Index(ix, iz)] : 0;

    /// <summary>Water surface at a world position, or NaN where dry.</summary>
    public float SurfaceAt(double x, double z)
    {
        if (!ToCell(x, z, out int ix, out int iz)) return float.NaN;
        int i = Index(ix, iz);
        return _depth[i] > .01f ? _floor[i] + _depth[i] : float.NaN;
    }

    public double Volume()
    {
        double sum = 0;
        foreach (var d in _depth) sum += d;
        foreach (var chunk in _dormant.Values) foreach (var d in chunk) sum += d;
        return sum * CellArea;
    }

    public void Clear()
    {
        Array.Clear(_depth); Array.Clear(_wet); Array.Clear(_soil); Array.Clear(_left); Array.Clear(_right); Array.Clear(_up); Array.Clear(_down);
        foreach (var key in _dormant.Keys) _dirty.Add(key);
        _dormant.Clear(); Sources.Clear(); RainRate = 0;
        for (int wz = 0; wz < WindowChunks; wz++) for (int wx = 0; wx < WindowChunks; wx++) _dirty.Add((OriginChunkX + wx, OriginChunkZ + wz));
        RecomputeBounds();
    }

    /// <summary>One step. dt ≤ 1/30 s keeps it stable for depths of a few metres at 0.5 m cells.</summary>
    public void Step(float dt)
    {
        if (!_placed) return;
        foreach (var s in Sources) Add(s.X, s.Z, s.Rate * dt);
        if (RainRate > 0)
        {
            float rain = RainRate * dt;
            for (int i = 0; i < _depth.Length; i++) if (!_sink[i]) _depth[i] += rain;
            Added += rain * CellArea * Side * Side;
            _minX = _minZ = 0; _maxX = _maxZ = Side - 1;
        }
        if (_feedMaxX >= 0)
        {
            if (_maxX < 0) { _minX = _feedMinX; _maxX = _feedMaxX; _minZ = _feedMinZ; _maxZ = _feedMaxZ; }
            else { _minX = Math.Min(_minX, _feedMinX); _maxX = Math.Max(_maxX, _feedMaxX); _minZ = Math.Min(_minZ, _feedMinZ); _maxZ = Math.Max(_maxZ, _feedMaxZ); }
        }
        if (_maxX < _minX || _maxX < 0) return;
        int x0 = Math.Max(0, _minX - 1), x1 = Math.Min(Side - 1, _maxX + 1), z0 = Math.Max(0, _minZ - 1), z1 = Math.Min(Side - 1, _maxZ + 1);
        float pipe = dt * Gravity * (float)Cell, area = CellArea;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = Index(x, z);
                bool reservoir = _sink[i];
                float d = _depth[i];
                if (reservoir)
                {
                    // A generated water body: a fixed surface that only flows out into lower non-water neighbours.
                    if (!_feeds[i]) { _left[i] = _right[i] = _up[i] = _down[i] = 0; continue; }
                    d = _level[i] - _floor[i];
                }
                else if (d <= Dry) { _left[i] = _right[i] = _up[i] = _down[i] = 0; continue; }
                float h = _floor[i] + d;
                float Flow(float current, int nx, int nz)
                {
                    if (nx < 0 || nz < 0 || nx >= Side || nz >= Side) return reservoir ? 0 : Math.Max(0, current * FluxDamping + pipe * d); // open edge
                    int n = Index(nx, nz);
                    if (_sink[n])
                    {
                        if (reservoir) return 0;
                        float surface = float.IsNaN(_level[n]) ? _floor[n] : Math.Max(_level[n], _floor[n]);
                        return Math.Max(0, current * FluxDamping + pipe * (h - surface));
                    }
                    if (reservoir && !_edited[n]) return 0; // rivers only feed dug ground
                    return Math.Max(0, current * FluxDamping + pipe * (h - _floor[n] - _depth[n]));
                }
                float l = Flow(_left[i], x - 1, z), r = Flow(_right[i], x + 1, z), u = Flow(_up[i], x, z - 1), w = Flow(_down[i], x, z + 1);
                float total = (l + r + u + w) * dt;
                float scale = total > d * area ? d * area / total : 1f; // never more out than the cell holds
                _left[i] = l * scale; _right[i] = r * scale; _up[i] = u * scale; _down[i] = w * scale;
            }
        int nx0 = int.MaxValue, nx1 = -1, nz0 = int.MaxValue, nz1 = -1;
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = Index(x, z);
                float inflow = (x > 0 ? _right[i - 1] : 0) + (x < Side - 1 ? _left[i + 1] : 0) + (z > 0 ? _down[i - Side] : 0) + (z < Side - 1 ? _up[i + Side] : 0);
                float outflow = _left[i] + _right[i] + _up[i] + _down[i];
                if (x == 0) Spilled += _left[i] * dt;
                if (x == Side - 1) Spilled += _right[i] * dt;
                if (z == 0) Spilled += _up[i] * dt;
                if (z == Side - 1) Spilled += _down[i] * dt;
                if (_sink[i])
                {
                    // Reservoir: what flows in joins the river/lake/sea, what flows out is drawn from it.
                    Absorbed += (inflow + _depth[i] * area / dt) * dt; Drawn += outflow * dt;
                    _depth[i] = 0;
                    continue;
                }
                float d = Math.Max(0, _depth[i] + (inflow - outflow) * dt / area);
                if (d > 0)
                {
                    float free = Math.Max(0, 1 - _soil[i] / SoilCapacity);
                    float soak = Math.Min(d, (Infiltration * free * free + MinInfiltration) * dt);
                    _soil[i] = Math.Min(SoilCapacity, _soil[i] + soak);
                    soak += Math.Min(d - soak, Evaporation * dt); // evaporation, counted with what soaked away
                    d -= soak; Soaked += soak * area;
                }
                _depth[i] = d;
                if (d > Dry) { nx0 = Math.Min(nx0, x); nx1 = Math.Max(nx1, x); nz0 = Math.Min(nz0, z); nz1 = Math.Max(nz1, z); }
                if (Math.Abs(d - _shown[i]) > RenderTolerance) { _shown[i] = d; _dirty.Add((OriginChunkX + x / ChunkCells, OriginChunkZ + z / ChunkCells)); }
            }
        _minX = nx0; _maxX = nx1; _minZ = nz0; _maxZ = nz1;
        if (_maxX < 0) _minX = _minZ = 0;
        // Wetness: water marks the ground at once; drying is slow, so it is updated over the whole window twice a second.
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                int i = Index(x, z);
                if (_depth[i] > WetThreshold && _wet[i] < 1) { _wet[i] = 1; MarkWet(x, z, i); }
            }
        _dryClock += dt;
        if (_dryClock >= .5f)
        {
            float dry = DryingPerSecond * _dryClock;
            _dryClock = 0;
            for (int z = 0; z < Side; z++)
                for (int x = 0; x < Side; x++)
                {
                    int i = Index(x, z);
                    if (_depth[i] > Dry) continue;
                    if (_soil[i] > 0) _soil[i] = Math.Max(0, _soil[i] - SoilDrain * (dry / DryingPerSecond));
                    if (_wet[i] <= 0 || _depth[i] > WetThreshold) continue;
                    _wet[i] = Math.Max(0, _wet[i] - dry);
                    MarkWet(x, z, i);
                }
        }
    }

    private void MarkWet(int x, int z, int i)
    {
        if (Math.Abs(_wet[i] - _wetShown[i]) < .1f && _wet[i] > 0) return;
        _wetShown[i] = _wet[i];
        _dirty.Add((OriginChunkX + x / ChunkCells, OriginChunkZ + z / ChunkCells));
    }

    /// <summary>Wetness of the ground in a window cell (0 dry … 1 wet now).</summary>
    public float WetnessAt(double x, double z) => ToCell(x, z, out int ix, out int iz) ? _wet[Index(ix, iz)] : 0;

    /// <summary>Horizontal water velocity (m/s) in a window cell, from the flows through it.</summary>
    private (float X, float Z) Velocity(int x, int z)
    {
        int i = Index(x, z);
        float d = _depth[i];
        if (d < .01f) return (0, 0);
        float fromLeft = x > 0 ? _right[i - 1] : 0, fromRight = x < Side - 1 ? _left[i + 1] : 0;
        float fromUp = z > 0 ? _down[i - Side] : 0, fromDown = z < Side - 1 ? _up[i + Side] : 0;
        float vx = .5f * ((fromLeft - _left[i]) + (_right[i] - fromRight)) / (d * (float)Cell);
        float vz = .5f * ((fromUp - _up[i]) + (_down[i] - fromDown)) / (d * (float)Cell);
        float s = MathF.Sqrt(vx * vx + vz * vz);
        return s > MaxSpeed ? (vx / s * MaxSpeed, vz / s * MaxSpeed) : (vx, vz);
    }

    private bool NearSimWater(int x, int z) => SimWaterDistance(x, z) <= 3;

    /// <summary>Distance (cells) from a river/lake cell to the nearest simulated water or dug ground below it, up to 3; 99 if none.</summary>
    private float SimWaterDistance(int x, int z)
    {
        float level = _level[Index(x, z)], best = 99;
        for (int b = -3; b <= 3; b++)
            for (int a = -3; a <= 3; a++)
            {
                int nx = x + a, nz = z + b;
                if (nx < 0 || nz < 0 || nx >= Side || nz >= Side) continue;
                int n = Index(nx, nz);
                // Simulated water, or dug ground below this water (the terrain tiles leave both to this view). On ground
                // nobody dug, only real standing water (3 cm) counts: a rain film along a bank made this view redraw
                // the river's edge without its current (clear patches ending in straight chunk edges).
                if (!_sink[n] && (_depth[n] > (_edited[n] ? .008f : .03f) || _edited[n] && _floor[n] < level)) best = Math.Min(best, MathF.Sqrt(a * a + b * b));
            }
        return best;
    }

    /// <summary>Chunks (world chunk coordinates) whose visible water changed since the last call.</summary>
    public List<(long X, long Z)> TakeDirty() { var list = new List<(long, long)>(_dirty); _dirty.Clear(); return list; }

    /// <summary>Water saved for a chunk outside the window (it resumes when the window returns).</summary>
    public bool HasDormant(long chunkX, long chunkZ) => _dormant.ContainsKey((chunkX, chunkZ));

    public bool InWindow(long chunkX, long chunkZ) =>
        _placed && chunkX >= OriginChunkX && chunkX < OriginChunkX + WindowChunks && chunkZ >= OriginChunkZ && chunkZ < OriginChunkZ + WindowChunks;

    /// <summary>
    /// Render data for a window chunk: (ChunkCells+1)² samples at cell centres (the last row/column from the
    /// neighbouring chunk, so chunk meshes join): floor, depth, velocity. False if the chunk is not in the window.
    /// </summary>
    /// <param name="join">Optional: 1 where this view's water is its own; on river/lake cells drawn only to join the
    /// terrain's sheet, fading to 0 away from the simulated water (the overlap ends softly, not in 0.5 m steps).</param>
    public bool ChunkView(long chunkX, long chunkZ, float[] floor, float[] depth, float[] vx, float[] vz, float[] wet, float[]? join = null)
    {
        if (!InWindow(chunkX, chunkZ)) return false;
        int bx = (int)(chunkX - OriginChunkX) * ChunkCells, bz = (int)(chunkZ - OriginChunkZ) * ChunkCells, n = ChunkCells + 1;
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int x = Math.Min(bx + i, Side - 1), z = Math.Min(bz + j, Side - 1), k = j * n + i, c = Index(x, z);
                floor[k] = _floor[c]; depth[k] = _depth[c];
                if (join != null) join[k] = 1;
                // Wet ground: 3×3 average, so the darkened patch has soft edges rather than 0.5 m squares.
                float ws = 0;
                for (int b = -1; b <= 1; b++)
                    for (int a = -1; a <= 1; a++)
                        ws += _wetShown[Index(Math.Clamp(x + a, 0, Side - 1), Math.Clamp(z + b, 0, Side - 1))];
                wet[k] = ws / 9;
                // A river/lake cell next to simulated water (a dug channel) is drawn at its surface too, so the two
                // sheets join without a gap.
                if (_sink[c] && !float.IsNaN(_level[c]))
                {
                    float near = _sea[c] ? float.MaxValue : _edited[c] && _floor[c] < _level[c] ? 0 : SimWaterDistance(x, z);
                    if (near <= 3)
                    {
                        depth[k] = Math.Max(0, _level[c] - _floor[c]);
                        if (join != null) join[k] = Math.Clamp(1 - (near - 1) / 2.5f, 0, 1);
                    }
                    // A river/lake cell this view does not draw (the terrain tile's sheet does) reports its surface as the
                    // floor: the edge of a drawn patch then stays on the water surface instead of dropping to the bed
                    // (walls of water around springs placed in a river).
                    else floor[k] = Math.Max(_floor[c], _level[c]);
                }
                (vx[k], vz[k]) = Velocity(x, z);
            }
        return true;
    }
}
