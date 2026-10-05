using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Thornwake.WorldGen;

/// <summary>
/// One step of a world type's landform template (D36): Thornwake's own take on the idea of heightmap templates —
/// a short list of big landforms that gives a world type its general direction. Positions and sizes are fractions
/// of the world (0..1), so a template keeps its look at every world size; two-element arrays are a range the seed
/// picks from, one element is a fixed value. Effects add to the macro layer before erosion and rivers:
/// <see cref="Land"/> to land influence (about 1 = continent interior, below 0 = sea), <see cref="Relief"/> in
/// multiples of the profile's hill height, <see cref="Mountain"/> as a share of the profile's mountain height.
/// Unset effects take the step's defaults (see <see cref="Landforms.Defaults"/>).
/// </summary>
public sealed record Landform
{
    /// <summary>Hills, Raise, Lower, Basin, Range, Trough, Strait, Islands or Mask.</summary>
    public string Step { get; init; } = "";
    public double[]? Count { get; init; }
    /// <summary>Where the step is placed (start of a line, centre of a blob), as world fractions.</summary>
    public double[]? X { get; init; }
    public double[]? Z { get; init; }
    /// <summary>Blob radius, island radius or line half-width as a world fraction (Range: × profile mountain width;
    /// Mask: radius of the centre, or width of the faded band).</summary>
    public double[]? Size { get; init; }
    /// <summary>Line length as a world fraction; any step with a length becomes a long shape (Islands: a chain).</summary>
    public double[]? Length { get; init; }
    /// <summary>Line direction in degrees (0 = east, 90 = south).</summary>
    public double[]? Angle { get; init; }
    public double? Land { get; init; }
    public double? Relief { get; init; }
    public double? Mountain { get; init; }
    /// <summary>Mask only: Edges, Centre, North, South, East or West.</summary>
    public string? Shape { get; init; }
    /// <summary>Mask only: how much land is taken away.</summary>
    public double? Strength { get; init; }
}

/// <summary>A compiled landform template: deterministic shapes for one world, evaluated per macro raster cell.</summary>
public sealed class Landforms
{
    public static readonly string[] Steps = { "Hills", "Raise", "Lower", "Basin", "Range", "Trough", "Strait", "Islands", "Mask" };
    public const int MaxSteps = 32, MaxShapes = 1200;

    /// <summary>Default (land, relief, mountain, size, length) per step.</summary>
    public static (double Land, double Relief, double Mountain, double[] Size, double[] Length) Defaults(string step) => step switch
    {
        "Hills" => (.25, 1.6, 0, new[] { .03, .07 }, new[] { 0.0 }),
        "Raise" => (.7, .6, 0, new[] { .1, .2 }, new[] { 0.0 }),
        "Lower" => (-.8, -.6, 0, new[] { .08, .16 }, new[] { 0.0 }),
        "Basin" => (0, -2.5, 0, new[] { .02, .05 }, new[] { 0.0 }),
        "Range" => (.4, 0, 1, new[] { 1.0 }, new[] { .15, .35 }),
        "Trough" => (-.1, -2.2, 0, new[] { .006, .012 }, new[] { .1, .25 }),
        "Strait" => (-2.2, 0, 0, new[] { .004, .008 }, new[] { .15, .3 }),
        "Islands" => (1.7, .8, 0, new[] { .004, .008 }, new[] { .1, .3 }),
        _ => (0, 0, 0, new[] { 0.0 }, new[] { 0.0 }),
    };

    public static void Validate(IReadOnlyList<Landform>? steps)
    {
        if (steps == null) return;
        if (steps.Count > MaxSteps) throw new InvalidDataException($"At most {MaxSteps} landform steps.");
        static bool Ok(double[]? r, double min, double max) => r == null || (r.Length is 1 or 2 && r.All(v => double.IsFinite(v) && v >= min && v <= max));
        static bool In(double? v, double min, double max) => v == null || (double.IsFinite(v.Value) && v >= min && v <= max);
        foreach (var s in steps)
        {
            if (!Steps.Contains(s.Step)) throw new InvalidDataException($"Unknown landform step '{s.Step}' (use {string.Join(", ", Steps)}).");
            if (!Ok(s.Count, 0, 400) || !Ok(s.X, -.2, 1.2) || !Ok(s.Z, -.2, 1.2) || !Ok(s.Size, 0, 5) || !Ok(s.Length, 0, 1.5) ||
                !Ok(s.Angle, -720, 720) || !In(s.Land, -4, 4) || !In(s.Relief, -10, 10) || !In(s.Mountain, -1, 2) || !In(s.Strength, 0, 4))
                throw new InvalidDataException($"Landform step '{s.Step}' has a value out of range.");
            if (s.Step == "Mask" && s.Shape is not (null or "Edges" or "Centre" or "North" or "South" or "East" or "West"))
                throw new InvalidDataException($"Unknown mask shape '{s.Shape}'.");
        }
    }

    /// <summary>A blob or one segment of a line. Segments of one line share a <see cref="Group"/> (their influence is the
    /// maximum over the group, so joints do not bulge) and the line's parameter range T0..T1 (for tapered ends).</summary>
    private readonly record struct Shape(double Ax, double Az, double Bx, double Bz, double Ra, double Rb, double T0, double T1, bool Line,
        int Group, double Land, double Relief, double Mountain, ulong Seed, double MinX, double MinZ, double MaxX, double MaxZ);
    private readonly record struct MaskShape(string Kind, double Strength, double Size, ulong Seed);

    private readonly Shape[] _shapes;
    private readonly MaskShape[] _masks;
    private readonly List<int>[] _bins;
    private const int Bins = 32;
    private readonly double _world, _hill;
    public int ShapeCount => _shapes.Length;
    public bool Empty => _shapes.Length == 0 && _masks.Length == 0;

    public Landforms(WorldManifest manifest)
    {
        var p = manifest.Profile;
        _world = p.WorldSize; _hill = Math.Max(p.HillHeight, 60);
        var shapes = new List<Shape>();
        var masks = new List<MaskShape>();
        var steps = p.Landforms ?? Array.Empty<Landform>();
        int group = 0;
        for (int n = 0; n < steps.Length; n++)
        {
            var s = steps[n];
            var rng = new RandomStream(Hashing.FeatureSeed(manifest.Seed, 3, manifest.ProfileDigest, "landform-" + s.Step, 0, 0, n));
            double Pick(double[]? r, double fallback) => r == null ? fallback : r.Length == 1 ? r[0] : rng.Between(r[0], r[1]);
            if (s.Step == "Mask")
            {
                string kind = s.Shape ?? "Edges";
                masks.Add(new MaskShape(kind, s.Strength ?? .8, Math.Clamp(Pick(s.Size, kind switch { "Edges" => .22, "Centre" => .22, _ => .4 }), .02, .5), rng.Next()));
                continue;
            }
            var d = Defaults(s.Step);
            double land = s.Land ?? d.Land, relief = (s.Relief ?? d.Relief) * _hill, mountain = s.Mountain ?? d.Mountain;
            double bend = s.Step switch { "Trough" => .06, "Strait" => .12, "Islands" => .14, _ => .1 };
            int count = (int)Math.Round(Pick(s.Count, 1));
            for (int c = 0; c < count && shapes.Count < MaxShapes; c++)
            {
                double ax = (Pick(s.X, rng.Between(.15, .85)) - .5) * _world, az = (Pick(s.Z, rng.Between(.15, .85)) - .5) * _world;
                double size = Pick(s.Size, Pick(d.Size, 0));
                // Mountain ranges are as wide as the profile's ranges; everything else is a world fraction, with
                // floors so small worlds do not get needle-thin features.
                double radius = s.Step == "Range" ? Math.Max(p.MountainWidth * size, 600) : Math.Max(size * _world, s.Step == "Islands" ? 350 : 500);
                // Any step with a length is a line (a long ridge, an arm of land); Islands become a chain.
                double length = Pick(s.Length, Pick(d.Length, 0)) * _world;
                if (length <= 0) { Add(ax, az, ax, az, radius, radius, 0, 1, false, rng.Next()); group++; continue; }
                double angle = Pick(s.Angle, rng.Between(0, 180)) * Math.PI / 180;
                // A smoothly bending polyline (two sine bends) with varying width.
                int segments = Math.Clamp((int)(length / (radius * 2)), 1, 10);
                var px = new double[segments + 1]; var pz = new double[segments + 1]; var pr = new double[segments + 1];
                double cos = Math.Cos(angle), sin = Math.Sin(angle);
                double b1 = rng.Between(-1, 1), b2 = rng.Between(-1, 1), phase = rng.Between(0, 2 * Math.PI);
                for (int i = 0; i <= segments; i++)
                {
                    double u = i / (double)segments, along = length * u;
                    double off = length * bend * (b1 * Math.Sin(Math.PI * u) + .5 * b2 * (Math.Sin(2 * Math.PI * u + phase) - Math.Sin(phase) * (1 - u)));
                    px[i] = ax + cos * along - sin * off; pz[i] = az + sin * along + cos * off;
                    pr[i] = radius * rng.Between(.7, 1.3);
                }
                if (s.Step != "Islands")
                {
                    ulong seed = rng.Next();
                    for (int i = 0; i < segments && shapes.Count < MaxShapes; i++)
                        Add(px[i], pz[i], px[i + 1], pz[i + 1], pr[i], pr[i + 1], i / (double)segments, (i + 1) / (double)segments, true, seed);
                    group++;
                    continue;
                }
                // An island chain: islands of varied size scattered along the line.
                int islands = Math.Max(2, (int)Math.Round(length / (radius * 3.5)));
                for (int i = 0; i < islands && shapes.Count < MaxShapes; i++)
                {
                    double t = DetMath.Clamp((i + rng.Between(-.3, .3)) / Math.Max(1, islands - 1), 0, 1) * segments;
                    int k = Math.Min((int)t, segments - 1); double f = t - k, off = rng.Between(-1.5, 1.5) * radius;
                    double cx = px[k] + (px[k + 1] - px[k]) * f - sin * off, cz = pz[k] + (pz[k + 1] - pz[k]) * f + cos * off;
                    double r = radius * rng.Between(.5, 1.3);
                    Add(cx, cz, cx, cz, r, r, 0, 1, false, rng.Next());
                    group++;
                }
            }

            void Add(double ax, double az, double bx, double bz, double ra, double rb, double t0, double t1, bool line, ulong seed)
            {
                double reach = Math.Max(ra, rb) * 1.9;
                shapes.Add(new Shape(ax, az, bx, bz, ra, rb, t0, t1, line, group, land, relief, mountain, seed,
                    Math.Min(ax, bx) - reach, Math.Min(az, bz) - reach, Math.Max(ax, bx) + reach, Math.Max(az, bz) + reach));
            }
        }
        _shapes = shapes.ToArray();
        _masks = masks.ToArray();
        _bins = new List<int>[Bins * Bins];
        for (int k = 0; k < _bins.Length; k++) _bins[k] = new List<int>();
        for (int n = 0; n < _shapes.Length; n++)
        {
            var sh = _shapes[n];
            int i0 = Bin(sh.MinX), i1 = Bin(sh.MaxX), j0 = Bin(sh.MinZ), j1 = Bin(sh.MaxZ);
            for (int j = j0; j <= j1; j++) for (int i = i0; i <= i1; i++) _bins[j * Bins + i].Add(n);
        }
    }

    private int Bin(double local) => Math.Clamp((int)((local / _world + .5) * Bins), 0, Bins - 1);

    /// <summary>Adds the template to one macro cell (origin-relative coordinates).</summary>
    public void Apply(double lx, double lz, ref double land, ref double relief, ref double mountain, Func<double, double, double> rugged)
    {
        foreach (var m in _masks)
        {
            // -0.5 .. 0.5 across the world, warped so mask edges are not geometric.
            double u = lx / _world + Noise.Value(lx / _world * 5, lz / _world * 5, m.Seed) * .05;
            double v = lz / _world + Noise.Value(lx / _world * 5 + 17.3, lz / _world * 5 - 9.1, m.Seed) * .05;
            double fade = m.Kind switch
            {
                "Centre" => 1 - DetMath.Smooth(Math.Sqrt(u * u + v * v) / m.Size),
                "North" => DetMath.Smooth((-v - (.45 - m.Size)) / (m.Size * .9)),
                "South" => DetMath.Smooth((v - (.45 - m.Size)) / (m.Size * .9)),
                "West" => DetMath.Smooth((-u - (.45 - m.Size)) / (m.Size * .9)),
                "East" => DetMath.Smooth((u - (.45 - m.Size)) / (m.Size * .9)),
                _ => DetMath.Smooth((Math.Max(Math.Abs(u), Math.Abs(v)) - (.5 - m.Size)) / (m.Size * .9)),
            };
            land -= fade * m.Strength * 1.5;
        }
        if (_shapes.Length == 0) return;
        int current = -1, best = -1; double strongest = 0;
        double addLand = 0, cutLand = 0, addRelief = 0, outMountain = mountain;
        foreach (int n in _bins[Bin(lz) * Bins + Bin(lx)])
        {
            ref readonly var s = ref _shapes[n];
            if (s.Group != current) { Flush(); current = s.Group; strongest = 0; best = -1; }
            if (lx < s.MinX || lx > s.MaxX || lz < s.MinZ || lz > s.MaxZ) continue;
            // Domain-warped distance to the segment (a point for blobs), so shapes are not circles or tubes.
            double rw = Math.Max(s.Ra, s.Rb) * .8;
            double wx = lx + Noise.Value(lx / rw, lz / rw, s.Seed) * rw * .22;
            double wz = lz + Noise.Value(lx / rw + 31.7, lz / rw - 18.3, s.Seed) * rw * .22;
            double ex = s.Bx - s.Ax, ez = s.Bz - s.Az, len2 = ex * ex + ez * ez;
            double t = len2 > 0 ? DetMath.Clamp(((wx - s.Ax) * ex + (wz - s.Az) * ez) / len2, 0, 1) : 0;
            double dx = wx - (s.Ax + ex * t), dz = wz - (s.Az + ez * t), r = s.Ra + (s.Rb - s.Ra) * t;
            double q = (dx * dx + dz * dz) / (r * r);
            if (q >= 1) continue;
            double falloff = (1 - q) * (1 - q);
            if (s.Line) { double tg = s.T0 + (s.T1 - s.T0) * t; falloff *= DetMath.Smooth(Math.Min(tg, 1 - tg) * 6 + .12); }
            if (falloff > strongest) { strongest = falloff; best = n; }
        }
        Flush();
        land += addLand;
        // Cuts (straits, lowered land) make shallow sea, never deepen sea that is already there.
        if (cutLand < 0) land = Math.Max(land + cutLand, Math.Min(land, -.2));
        relief += addRelief; mountain = outMountain;

        void Flush()
        {
            if (best < 0) return;
            ref readonly var s = ref _shapes[best];
            double r = Math.Max(s.Ra, s.Rb);
            // Coast and bank irregularity: noise strongest where the shape fades out (its coastline).
            double coast = Noise.Value(lx / (r * .5), lz / (r * .5), s.Seed + 1) * .6 + Noise.Value(lx / (r * .16), lz / (r * .16), s.Seed + 2) * .4;
            double change = s.Land * strongest + Math.Min(Math.Abs(s.Land) * .22, .45) * coast * DetMath.Smooth(strongest * 5);
            if (s.Land < 0) cutLand += Math.Min(0, change); else addLand += change;
            addRelief += s.Relief * strongest;
            if (s.Mountain != 0)
            {
                // Peaks and passes along the range.
                double along = .6 + .4 * Noise.Value(lx / (r * 3), lz / (r * 3), s.Seed + 3);
                outMountain = DetMath.Clamp(outMountain + s.Mountain * strongest * along * (.3 + .7 * rugged(lx, lz)), 0, 1);
            }
            best = -1;
        }
    }
}
