using System;
using System.Threading.Tasks;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Far land beyond the streamed terrain (round 28, Tom: "hide the hard cutoff of the terrain in the far distance —
/// from a mountain top you see the unloaded edge"). One coarse mesh of concentric squares from the exact edge of
/// the outermost terrain ring out to ~100 or ~250 km (the true horizon from 2.5 km up; ~180 km from a 2.6 km summit), the squares spaced geometrically (a few thousand vertices),
/// shaped and coloured from the world plan: heights at the ring's own resolution, biome grass and dry colours,
/// rock on steep slopes, snow where it is cold, lakes and the sea flat at their level. It follows the Earth's curve
/// (distant land sinks below the horizon as it really does), a skirt under its inner edge hides any seam with the
/// terrain, and the haze fades it into the sky — so the land runs on to the horizon instead of stopping in a square.
/// Rebuilt on a worker thread when the outermost ring moves (every ~8 km of travel).
/// </summary>
public partial class HorizonLand : MeshInstance3D
{
    private const int PerSide = 48;   // vertices per square side
    private const double EarthRadius = 6371000;

    private TerrainSampler? _sampler;
    private Task<(Vector3[] V, Vector3[] N, Color[] C, int[] I)>? _job;
    private (long X, long Z, double Size, int Range, int Revision) _built = (long.MinValue, 0, 0, 0, -1), _pending;
    private double _centerX, _centerZ, _jobCenterX, _jobCenterZ;
    private int _revision;
    private readonly ShaderMaterial _material;

    public HorizonLand()
    {
        CastShadow = ShadowCastingSetting.Off;
        GIMode = GIModeEnum.Disabled;
        ExtraCullMargin = 400000;
        _material = new ShaderMaterial { Shader = new Shader { Code = @"
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;
// Far land (HorizonLand.cs): vertex colours in sRGB; alpha 1 = water (smooth, a little sky reflection).
void fragment() {
	vec3 c = COLOR.rgb;
	ALBEDO = mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(vec3(0.04045), c));
	ROUGHNESS = mix(0.97, 0.12, COLOR.a);
	SPECULAR = mix(0.3, 0.6, COLOR.a);
}" } };
        MaterialOverride = _material;
    }

    /// <summary>Still building (screenshots wait for it).</summary>
    public bool Busy => _job != null || (_sampler != null && GameSettings.HorizonLand.Index > 0 && Mesh == null);

    /// <summary>A new world: forget the old mesh.</summary>
    public void Configure(TerrainSampler sampler)
    {
        _sampler = sampler;
        _revision++;
        Mesh = null;
        _built = (long.MinValue, 0, 0, 0, -1);
    }

    /// <summary>How far the land reaches now (m; 0 = off): the camera's far plane must cover it.</summary>
    public int Range { get; private set; }

    /// <summary>
    /// Per frame. <paramref name="levels"/>: the streamer's level count (the top level's tiles set the inner edge);
    /// <paramref name="topRadius"/>: its top ring radius in tiles; <paramref name="height"/>: the camera above sea level.
    /// </summary>
    public void Step(double px, double pz, double originX, double originZ, int levels, int topRadius, double height)
    {
        int range = GameSettings.HorizonLand.Index switch { 0 => 0, 1 => 100000, _ => 250000 };
        // At least out to the true horizon (+10 %), in 25 km steps (a rebuild only when climbing that much), at most
        // 400 km: from 2 km up the horizon is ~160 km away, and beyond ~100 km of land the rays that pass over its edge
        // but not yet over the horizon hit nothing — sky below the horizon, with the sky's clouds instead of the land's:
        // a stepped line along the horizon inside clouds (Tom, dumps 221259 / 221314).
        if (range > 0)
            range = (int)Math.Min(400000, Math.Max(range, Math.Ceiling(Math.Sqrt(2 * EarthRadius * Math.Max(height, 0)) * 1.1 / 25000) * 25000));
        Range = range;
        Visible = range > 0 && Mesh != null;
        if (_sampler == null || range == 0) return;
        double size = TileBuilder.BaseTile * (1L << (levels - 1));
        long cx = (long)Math.Floor(px / size), cz = (long)Math.Floor(pz / size);
        var want = (cx, cz, size, range, _revision);
        if (_job is { IsCompleted: true } done)
        {
            var (v, n, c, i) = done.Result;
            _job = null;
            if (_pending.Revision == _revision && v.Length > 0)
            {
                var arrays = new Godot.Collections.Array();
                arrays.Resize((int)Mesh.ArrayType.Max);
                arrays[(int)Mesh.ArrayType.Vertex] = v;
                arrays[(int)Mesh.ArrayType.Normal] = n;
                arrays[(int)Mesh.ArrayType.Color] = c;
                arrays[(int)Mesh.ArrayType.Index] = i;
                var mesh = new ArrayMesh();
                mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
                Mesh = mesh;
                _built = _pending;
                _centerX = _jobCenterX; _centerZ = _jobCenterZ;
            }
        }
        if (_job == null && want != _built)
        {
            _pending = want;
            // The inner edge is the outer edge of the top ring: tiles cx−R … cx+R.
            double minX = (cx - topRadius) * size, maxX = (cx + topRadius + 1) * size;
            double minZ = (cz - topRadius) * size, maxZ = (cz + topRadius + 1) * size;
            _jobCenterX = (minX + maxX) / 2; _jobCenterZ = (minZ + maxZ) / 2;
            double half = (maxX - minX) / 2, ccx = _jobCenterX, ccz = _jobCenterZ, viewX = px, viewZ = pz;
            var sampler = _sampler;
            _job = Background.Run(() => Build(sampler, ccx, ccz, half, range, viewX, viewZ));
        }
        Position = new Vector3((float)(_centerX - originX), 0, (float)(_centerZ - originZ));
    }

    private static (Vector3[], Vector3[], Color[], int[]) Build(TerrainSampler sampler, double cx, double cz, double inner,
        double range, double viewX, double viewZ)
    {
        double outer = Math.Max(inner * 1.5, range);
        int perRing = PerSide * 4;
        // Rings: a skirt (inner edge, dropped), then squares from the inner edge growing geometrically.
        int rings = 40;
        double growth = Math.Pow(outer / inner, 1.0 / (rings - 1));
        int total = (rings + 1) * perRing;
        var v = new Vector3[total];
        var c = new Color[total];
        var n = new Vector3[total];
        double sea = sampler.Plan.Profile.SeaLevel;
        var climate = sampler.Plan.Climate;
        var biomes = sampler.Plan.Biomes;
        double inner2 = inner * inner;
        for (int r = 0; r < rings; r++)
        {
            double half = inner * Math.Pow(growth, r);
            double spacing = half * 2 / PerSide;
            for (int k = 0; k < perRing; k++)
            {
                // Walk the square's perimeter: side, then position along it.
                int side = k / PerSide;
                double t = (k % PerSide) / (double)PerSide * 2 - 1;
                double ox = side switch { 0 => t, 1 => 1, 2 => -t, _ => -1 } * half;
                double oz = side switch { 0 => -1, 1 => t, 2 => 1, _ => -t } * half;
                double x = cx + ox, z = cz + oz;
                var s = sampler.Sample(x, z, Math.Max(16, spacing * .5));
                bool water = s.Water != WaterKind.None && !double.IsNaN(s.WaterLevel) && s.WaterLevel > s.Height;
                double y = water ? s.WaterLevel : s.Height;
                // The Earth's curve (relative to the inner edge, so the join stays level).
                double dx = x - viewX, dz = z - viewZ;
                y -= Math.Max(0, dx * dx + dz * dz - inner2) / (2 * EarthRadius);
                int index = (r + 1) * perRing + k;
                v[index] = new Vector3((float)ox, (float)y, (float)oz);
                c[index] = water ? WaterColour(s.Water) : LandColour(s, x, z, sea, climate, biomes);
            }
        }
        // Skirt: under the inner edge, 400 m down.
        for (int k = 0; k < perRing; k++) { v[k] = v[perRing + k] - new Vector3(0, 400, 0); c[k] = c[perRing + k]; }
        // Normals from the ring grid (across: neighbours on the square; outward: the next ring).
        for (int r = 0; r <= rings; r++)
            for (int k = 0; k < perRing; k++)
            {
                int i = r * perRing + k;
                var a = v[r * perRing + (k + 1) % perRing] - v[r * perRing + (k + perRing - 1) % perRing];
                int ro = Math.Clamp(r + 1, 1, rings), ri = Math.Clamp(r - 1, 1, rings);
                var b = v[ro * perRing + k] - v[ri * perRing + k];
                var nn = b.Cross(a);
                if (nn.Y < 0) nn = -nn;
                n[i] = nn.LengthSquared() > 1e-6f ? nn.Normalized() : Vector3.Up;
            }
        // Steep far slopes read as rock (biome rock colour mixed in by the normal).
        for (int r = 1; r <= rings; r++)
            for (int k = 0; k < perRing; k++)
            {
                int i = r * perRing + k;
                if (c[i].A > .5f) continue;
                float rock = Mathf.Clamp((.86f - n[i].Y) / .2f, 0, 1);
                if (rock > 0) c[i] = c[i].Lerp(new Color(.46f, .44f, .4f, 0), rock * .7f);
            }
        var indices = new int[rings * perRing * 6];
        int q = 0;
        for (int r = 0; r < rings; r++)
            for (int k = 0; k < perRing; k++)
            {
                int a0 = r * perRing + k, a1 = r * perRing + (k + 1) % perRing;
                int b0 = (r + 1) * perRing + k, b1 = (r + 1) * perRing + (k + 1) % perRing;
                indices[q++] = a0; indices[q++] = a1; indices[q++] = b1;
                indices[q++] = a0; indices[q++] = b1; indices[q++] = b0;
            }
        return (v, n, c, indices);
    }

    private static Color WaterColour(WaterKind kind) => kind == WaterKind.Ocean ? new Color(.05f, .16f, .24f, 1) : new Color(.08f, .2f, .24f, 1);

    private static Color LandColour(TerrainSample s, double x, double z, double sea, Climate climate, BiomeMap biomes)
    {
        var (ia, ib, share) = biomes.Sample(x, z);
        var a = biomes.Pool[ia];
        var b = biomes.Pool[ib];
        float sa = (float)share, sb = 1 - sa;
        Color Mix((float R, float G, float B) p, (float R, float G, float B) q) => new(p.R * sa + q.R * sb, p.G * sa + q.G * sb, p.B * sa + q.B * sb, 0);
        double plants = Math.Clamp(a.Def.Vegetation.Grass * share + b.Def.Vegetation.Grass * (1 - share), 0, 1);
        double forest = Math.Clamp(a.Def.Vegetation.TreeDensity * share + b.Def.Vegetation.TreeDensity * (1 - share), 0, 1);
        var grass = Mix(a.Grass, b.Grass);
        var dry = Mix(a.Dry, b.Dry);
        // Sparse plants show the dry ground; forests darken it from afar (canopy colour).
        var colour = dry.Lerp(grass, (float)plants);
        colour = colour.Lerp(new Color(colour.R * .62f, colour.G * .68f, colour.B * .6f, 0), (float)(forest * .6));
        double elevation = s.Height - sea;
        var (temperature, _) = climate.At(x, z, Math.Max(0, elevation));
        double snow = climate.Snow(a.Snow * share + b.Snow * (1 - share), temperature, elevation);
        colour = colour.Lerp(new Color(.92f, .93f, .95f, 0), (float)Math.Clamp(snow, 0, 1));
        // Beaches: a pale strip right at the sea.
        if (elevation < 3) colour = colour.Lerp(new Color(.76f, .7f, .52f, 0), (float)Math.Clamp((3 - elevation) / 3, 0, 1) * .6f);
        return colour;
    }
}
