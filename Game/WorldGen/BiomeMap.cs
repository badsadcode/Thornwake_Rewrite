using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Thornwake.WorldGen;

/// <summary>
/// Biome placement (D42, docs/BIOMES_PLAN.md): which biomes cover the world, as smooth organic regions layered on the
/// finished geography. On a raster at the climate resolution, every pool biome gets a fitness per cell from its
/// placement preferences (temperature, moisture, elevation, slope, mountains, latitude, coast and river proximity) ×
/// its weight × a region noise at its own region size; patch biomes (fantasy and special biomes) take over host
/// biomes inside noise patches sized to their coverage; forbidden neighbours are suppressed and preferred ones
/// boosted. Each cell keeps its three best biomes; sampling blends them with a domain warp, so borders are organic.
/// Never changes the terrain itself.
/// </summary>
public sealed class BiomeMap
{
    public BiomeRegistry Registry { get; }
    public IReadOnlyList<ResolvedBiome> Pool => Registry.Pool;
    private readonly MacroRaster _grid;
    private readonly byte[] _ids;   // 3 per cell
    private bool[]? _land;
    private readonly float[] _w;    // 3 per cell, normalised
    private readonly int _n;
    private readonly ulong _seed;
    private readonly double _warp;

    public BiomeMap(WorldPlan plan, Climate climate, BiomeRegistry registry)
    {
        Registry = registry;
        var pool = registry.Pool;
        int count = pool.Count;
        if (count > 255) throw new InvalidOperationException("At most 255 biomes in a pool.");
        _grid = climate.Raster;
        int n = _n = _grid.Size;
        double cell = _grid.Cell;
        _seed = Hashing.FeatureSeed(plan.Manifest.Seed, 6, plan.Manifest.ProfileDigest, "biomes");
        _warp = cell * 1.3;
        _ids = new byte[n * n * 3];
        _w = new float[n * n * 3];
        if (count == 1) { Array.Fill(_w, 0f); for (int k = 0; k < n * n; k++) _w[k * 3] = 1; return; }

        // ---- per-cell geography and climate
        var land = new bool[n * n]; var elevation = new float[n * n]; var slope = new float[n * n]; var mountain = new float[n * n];
        var temperature = new float[n * n]; var moisture = new float[n * n]; var latitude = new float[n * n];
        Parallel.For(0, n, j =>
        {
            for (int i = 0; i < n; i++)
            {
                double x = _grid.CellX(i), z = _grid.CellZ(j);
                int k = j * n + i;
                plan.Macro.Sample(x, z, WorldPlan.LandChannel, WorldPlan.HeightChannel, WorldPlan.MountainChannel, out double l, out double h, out double m);
                land[k] = l > 0;
                elevation[k] = (float)Math.Max(0, h);
                mountain[k] = (float)Math.Clamp(m, 0, 1);
                plan.Macro.SampleGradient(WorldPlan.HeightChannel, x, z, out double gx, out double gz);
                slope[k] = (float)Math.Sqrt(gx * gx + gz * gz);
                var (t, p) = climate.At(x, z, Math.Max(0, h));
                temperature[k] = (float)t; moisture[k] = (float)p;
                latitude[k] = (float)climate.Latitude(z);
            }
        });
        _land = land;
        var coast = Distance(n, k => !land[k], cell);          // metres to the sea
        var river = RiverDistance(plan, n, cell);               // metres to a river or lake

        // ---- fitness per biome
        var fit = new float[count][];
        var climateBiomes = Enumerable.Range(0, count).Where(b => pool[b].Def.Placement.Patch == null).ToArray();
        Parallel.For(0, count, b =>
        {
            var p = pool[b].Def.Placement;
            var f = fit[b] = new float[n * n];
            double scale = Math.Sqrt(p.RegionSize[0] * p.RegionSize[1]) * 1000 * .5;
            ulong seed = _seed ^ Hashing.Mix((ulong)b * 977 + 13);
            for (int j = 0; j < n; j++)
                for (int i = 0; i < n; i++)
                {
                    int k = j * n + i;
                    if (!land[k]) continue;
                    double v = Math.Max(0, p.Weight)
                        * Member(temperature[k], p.Temperature, 3) * MemberLog(moisture[k], p.Moisture, .25)
                        * Member(elevation[k], p.Elevation, 150) * Member(slope[k], p.Slope, .03)
                        * Member(mountain[k], p.Mountain, .1) * Member(latitude[k], p.Latitude, 4);
                    if (v <= 0) continue;
                    double nearCoast = 1 - DetMath.Smooth(coast[k] / 30000), nearRiver = 1 - DetMath.Smooth(river[k] / 5000);
                    v *= Math.Exp(p.Coast * 1.2 * nearCoast) * Math.Exp(p.River * 1.2 * nearRiver);
                    // Region noise at the biome's own scale: irregular regions of roughly its size, not climate bands.
                    v *= Math.Exp(.55 * Noise.Fbm(_grid.CellX(i), _grid.CellZ(j), scale, 3, cell * 2, seed, .5));
                    f[k] = (float)v;
                }
        });

        // ---- patch biomes: take over their hosts inside noise patches covering the requested share
        foreach (int b in Enumerable.Range(0, count).Where(b => pool[b].Def.Placement.Patch != null))
        {
            var p = pool[b].Def.Placement;
            var hosts = p.Patch!.Within.Length == 0 ? climateBiomes
                : climateBiomes.Where(h => p.Patch.Within.Contains(pool[h].Def.Id)).ToArray();
            double scale = Math.Sqrt(p.RegionSize[0] * p.RegionSize[1]) * 1000 * .6;
            ulong seed = _seed ^ Hashing.Mix((ulong)b * 31337 + 7);
            var mask = new float[n * n];
            var hostFit = new float[n * n];
            var values = new List<float>();
            for (int k = 0; k < n * n; k++)
            {
                if (!land[k]) continue;
                float best = 0, bestAll = 0;
                foreach (int h in hosts) best = Math.Max(best, fit[h][k]);
                foreach (int h in climateBiomes) bestAll = Math.Max(bestAll, fit[h][k]);
                // Host present here (its fitness near the best) and this biome's own preferences allow it.
                float hostShare = bestAll > 0 ? best / bestAll : 0;
                if (hostShare < .6f || fit[b][k] <= 0) continue;
                int i = k % n, j = k / n;
                mask[k] = (float)Noise.Fbm(_grid.CellX(i), _grid.CellZ(j), scale, 3, cell * 2, seed, .55);
                hostFit[k] = bestAll;
                values.Add(mask[k]);
            }
            if (values.Count == 0) { Array.Clear(fit[b]); continue; }
            values.Sort();
            float threshold = values[Math.Clamp((int)((1 - p.Patch.Coverage) * values.Count), 0, values.Count - 1)];
            var f = fit[b];
            for (int k = 0; k < n * n; k++)
                f[k] = hostFit[k] > 0 ? (float)(hostFit[k] * 2.2 * DetMath.Smooth((mask[k] - threshold) / .06 + .5)) : 0;
        }

        // ---- neighbour rules (one soft pass): suppressed near forbidden neighbours, boosted near preferred ones
        var top = Top(fit, land, n, count);
        var presence = new float[count][];
        int radius = Math.Max(1, (int)Math.Round(8000 / cell));
        bool any = pool.Any(r => r.Def.Placement.ForbiddenNeighbours.Length + r.Def.Placement.PreferNeighbours.Length > 0);
        if (any)
        {
            for (int b = 0; b < count; b++)
            {
                var m = new float[n * n];
                for (int k = 0; k < n * n; k++) if (top[k] == b) m[k] = 1;
                Blur(m, n, radius);
                presence[b] = m;
            }
            for (int b = 0; b < count; b++)
            {
                var p = pool[b].Def.Placement;
                var forbid = p.ForbiddenNeighbours.Select(id => pool.FirstOrDefault(r => r.Def.Id == id)?.Index ?? -1).Where(i => i >= 0).ToArray();
                var prefer = p.PreferNeighbours.Select(id => pool.FirstOrDefault(r => r.Def.Id == id)?.Index ?? -1).Where(i => i >= 0).ToArray();
                if (forbid.Length + prefer.Length == 0) continue;
                for (int k = 0; k < n * n; k++)
                {
                    double factor = 1;
                    foreach (int o in forbid) factor *= 1 - Math.Min(1, presence[o][k] * 3);
                    foreach (int o in prefer) factor *= 1 + .35 * presence[o][k];
                    fit[b][k] = (float)(fit[b][k] * factor);
                }
            }
        }

        // ---- keep the three best per cell, sharpened so regions are decisive with soft edges
        for (int k = 0; k < n * n; k++)
        {
            int a = 0, b2 = 0, c = 0; float fa = -1, fb = -1, fc = -1;
            for (int b = 0; b < count; b++)
            {
                float v = fit[b][k];
                if (v > fa) { c = b2; fc = fb; b2 = a; fb = fa; a = b; fa = v; }
                else if (v > fb) { c = b2; fc = fb; b2 = b; fb = v; }
                else if (v > fc) { c = b; fc = v; }
            }
            // Nothing fits (or sea): the nearest climate biome, weak at sea so coastal land decides the blend there.
            if (fa <= 0) { a = b2 = c = NearestClimateBiome(k, temperature, moisture, elevation, slope, mountain, latitude, pool, climateBiomes); fa = land[k] ? 1 : .03f; fb = fc = 0; }
            double wa = fa * fa, wb = Math.Max(0, fb) * Math.Max(0, fb), wc = Math.Max(0, fc) * Math.Max(0, fc), sum = wa + wb + wc;
            _ids[k * 3] = (byte)a; _ids[k * 3 + 1] = (byte)b2; _ids[k * 3 + 2] = (byte)c;
            _w[k * 3] = (float)(wa / sum); _w[k * 3 + 1] = (float)(wb / sum); _w[k * 3 + 2] = (float)(wc / sum);
        }
    }

    /// <summary>The two strongest biomes at a world point and the first one's share (0.5..1), blended smoothly.</summary>
    public (int A, int B, double ShareA) Sample(double x, double z)
    {
        if (Pool.Count == 1) return (0, 0, 1);
        // Organic borders: a domain warp of about a cell, then bilinear blending of the cells' biome weights.
        double wx = x + Noise.Fbm(x, z, _warp * 6, 3, _warp * .5, _seed + 101, .5) * _warp;
        double wz = z + Noise.Fbm(x + 913, z - 377, _warp * 6, 3, _warp * .5, _seed + 102, .5) * _warp;
        double fx = (wx - _grid.MinX) / _grid.Cell - .5, fz = (wz - _grid.MinZ) / _grid.Cell - .5;
        int i0 = (int)Math.Floor(fx), j0 = (int)Math.Floor(fz);
        double tx = fx - i0, tz = fz - j0;
        Span<int> ids = stackalloc int[12];
        Span<double> ws = stackalloc double[12];
        int used = 0;
        for (int c = 0; c < 4; c++)
        {
            int i = Math.Clamp(i0 + (c & 1), 0, _n - 1), j = Math.Clamp(j0 + (c >> 1), 0, _n - 1);
            double corner = ((c & 1) == 1 ? tx : 1 - tx) * ((c >> 1) == 1 ? tz : 1 - tz);
            int k = (j * _n + i) * 3;
            for (int e = 0; e < 3; e++)
            {
                double w = _w[k + e] * corner;
                if (w <= 0) continue;
                int id = _ids[k + e], slot = -1;
                for (int s = 0; s < used; s++) if (ids[s] == id) { slot = s; break; }
                if (slot < 0) { slot = used++; ids[slot] = id; ws[slot] = 0; }
                ws[slot] += w;
            }
        }
        int a = 0, b = 0; double fa = -1, fb = 0;
        for (int s = 0; s < used; s++)
        {
            if (ws[s] > fa) { b = a; fb = Math.Max(fa, 0); a = ids[s]; fa = ws[s]; }
            else if (ws[s] > fb) { b = ids[s]; fb = ws[s]; }
        }
        if (fa <= 0) return (0, 0, 1);
        if (fb <= 0) b = a;
        return (a, b, fa / (fa + fb));
    }

    /// <summary>Share of land cells where each pool biome is the strongest (debug, tests).</summary>
    public double[] Coverage()
    {
        var c = new double[Pool.Count]; double total = 0;
        for (int k = 0; k < _n * _n; k++) if (_land == null || _land[k]) { c[_ids[k * 3]]++; total++; }
        for (int i = 0; i < c.Length; i++) c[i] /= Math.Max(1, total);
        return c;
    }

    private static double Member(double v, double[]? range, double soft) => range == null ? 1
        : DetMath.Smooth((v - range[0]) / soft + .5) * DetMath.Smooth((range[1] - v) / soft + .5);

    private static double MemberLog(double v, double[]? range, double soft) => range == null ? 1
        : Member(Math.Log(Math.Max(1, v)), new[] { Math.Log(Math.Max(1, range[0])), Math.Log(Math.Max(1, range[1])) }, soft);

    /// <summary>
    /// Where no biome fits: the climate biome whose preferences are missed by the least, every range counted in units
    /// of its soft edge (so an Alpine biome never fills a cold lowland gap just because its temperature range fits).
    /// </summary>
    private static int NearestClimateBiome(int k, float[] t, float[] p, float[] elevation, float[] slope, float[] mountain,
        float[] latitude, IReadOnlyList<ResolvedBiome> pool, int[] climateBiomes)
    {
        static double Gap(double v, double[]? r, double soft) => r == null ? 0 : Math.Max(0, Math.Max(r[0] - v, v - r[1])) / soft;
        int best = climateBiomes.Length > 0 ? climateBiomes[0] : 0; double bestD = double.MaxValue;
        foreach (int b in climateBiomes)
        {
            var pl = pool[b].Def.Placement;
            double lp = Math.Log(Math.Max(1, p[k]));
            double[]? lr = pl.Moisture == null ? null : new[] { Math.Log(Math.Max(1, pl.Moisture[0])), Math.Log(Math.Max(1, pl.Moisture[1])) };
            double g = Sq(Gap(t[k], pl.Temperature, 3)) + Sq(Gap(lp, lr, .25)) + Sq(Gap(elevation[k], pl.Elevation, 150))
                + Sq(Gap(slope[k], pl.Slope, .03)) + Sq(Gap(mountain[k], pl.Mountain, .1)) + Sq(Gap(latitude[k], pl.Latitude, 4));
            g -= .01 * Math.Log(Math.Max(1e-3, pl.Weight)); // ties: the more common biome
            if (g < bestD) { bestD = g; best = b; }
        }
        return best;
    }

    private static double Sq(double v) => v * v;

    private static int[] Top(float[][] fit, bool[] land, int n, int count)
    {
        var top = new int[n * n];
        for (int k = 0; k < n * n; k++)
        {
            top[k] = -1;
            if (!land[k]) continue;
            float best = 0;
            for (int b = 0; b < count; b++) if (fit[b][k] > best) { best = fit[b][k]; top[k] = b; }
        }
        return top;
    }

    /// <summary>Chamfer distance (m) to the nearest cell where <paramref name="target"/> holds.</summary>
    private static float[] Distance(int n, Func<int, bool> target, double cell)
    {
        var d = new float[n * n];
        const float far = 1e9f;
        for (int k = 0; k < n * n; k++) d[k] = target(k) ? 0 : far;
        float a = (float)cell, b = (float)(cell * Math.Sqrt(2));
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
            {
                int k = j * n + i; float v = d[k];
                if (i > 0) v = Math.Min(v, d[k - 1] + a);
                if (j > 0) { v = Math.Min(v, d[k - n] + a); if (i > 0) v = Math.Min(v, d[k - n - 1] + b); if (i < n - 1) v = Math.Min(v, d[k - n + 1] + b); }
                d[k] = v;
            }
        for (int j = n - 1; j >= 0; j--)
            for (int i = n - 1; i >= 0; i--)
            {
                int k = j * n + i; float v = d[k];
                if (i < n - 1) v = Math.Min(v, d[k + 1] + a);
                if (j < n - 1) { v = Math.Min(v, d[k + n] + a); if (i < n - 1) v = Math.Min(v, d[k + n + 1] + b); if (i > 0) v = Math.Min(v, d[k + n - 1] + b); }
                d[k] = v;
            }
        return d;
    }

    private float[] RiverDistance(WorldPlan plan, int n, double cell)
    {
        var wet = new bool[n * n];
        foreach (var r in plan.Hydrology.Reaches)
            foreach (var pt in r.Points)
            {
                int i = (int)((pt.X - _grid.MinX) / cell), j = (int)((pt.Z - _grid.MinZ) / cell);
                if (i >= 0 && j >= 0 && i < n && j < n) wet[j * n + i] = true;
            }
        for (int j = 0; j < n; j++)
            for (int i = 0; i < n; i++)
                if (plan.Hydrology.LakeAt(_grid.CellX(i), _grid.CellZ(j)) >= 0) wet[j * n + i] = true;
        return Distance(n, k => wet[k], cell);
    }

    private static void Blur(float[] a, int n, int r)
    {
        var tmp = new float[a.Length];
        for (int j = 0; j < n; j++)
        {
            double sum = 0; int count = 0;
            for (int i = -r; i <= r; i++) if (i >= 0 && i < n) { sum += a[j * n + i]; count++; }
            for (int i = 0; i < n; i++)
            {
                tmp[j * n + i] = (float)(sum / count);
                int add = i + r + 1, rem = i - r;
                if (add < n) { sum += a[j * n + add]; count++; }
                if (rem >= 0) { sum -= a[j * n + rem]; count--; }
            }
        }
        for (int i = 0; i < n; i++)
        {
            double sum = 0; int count = 0;
            for (int j = -r; j <= r; j++) if (j >= 0 && j < n) { sum += tmp[j * n + i]; count++; }
            for (int j = 0; j < n; j++)
            {
                a[j * n + i] = (float)(sum / count);
                int add = j + r + 1, rem = j - r;
                if (add < n) { sum += tmp[add * n + i]; count++; }
                if (rem >= 0) { sum -= tmp[rem * n + i]; count--; }
            }
        }
    }
}
