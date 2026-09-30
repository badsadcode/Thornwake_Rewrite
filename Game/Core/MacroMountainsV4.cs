using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.Core;

/// <summary>
/// Opt-in algorithm-4 mountain contribution only. Immutable bounded descriptors; no erosion/world-wide grids.
/// Coordinates used for noise and geometry are relative to the frozen world origin.
/// </summary>
public sealed class MacroMountainsV4
{
    public const int MaximumBeltsPerContinent = 6;
    public const int MaximumSegmentsPerBelt = 32;
    public IReadOnlyList<MacroRange> Descriptors { get; }
    public int SegmentCount => _belts.Sum(b => b.Points.Length - 1);
    public int PassCount => _belts.Sum(b => b.Passes.Length);
    private readonly Belt[] _belts;
    private readonly double _width, _height;
    private readonly ulong _noise;
    private readonly record struct Knot(MacroPoint Point, double Width, double Height, double U);
    private readonly record struct Pass(double U, double Span, double Depth);
    private sealed record Belt(Knot[] Points, Pass[] Passes, double MinX, double MinZ, double MaxX, double MaxZ, double Taper);

    public MacroMountainsV4(MacroWorldManifest manifest, IReadOnlyList<MacroLandFeature> land)
    {
        var p = manifest.Profile; _width = p.MountainWidth; _height = p.MountainHeight;
        var identity = new GenerationIdentity(manifest.Seed, 4, manifest.ProfileDigest);
        _noise = identity.FeatureSeed("mountain-field", new(0, 0), 0);
        var belts = new List<Belt>(); var descriptors = new List<MacroRange>();
        if (p.RangesPerContinent > 0 && p.MountainHeight > 0)
            for (int continent = 0; continent < p.Continents; continent++)
            {
                var feature = land[continent];
                // World translation does not re-roll morphology; the feature ID still has its global owner.
                var rng = new RandomStream(identity.FeatureSeed("mountain-shape", new(0, 0), continent));
                var center = new MacroPoint(feature.Center.X - manifest.OriginX, feature.Center.Z - manifest.OriginZ);
                double angle = feature.Angle + rng.Between(-.25, .25), style = rng.Unit();
                bool broken = style >= .55 && style < .75, massif = style >= .9;
                double length = feature.RadiusX * (massif ? rng.Between(.55, .85) : rng.Between(1.1, 1.6));
                double curvature = feature.RadiusZ * (style < .18 ? rng.Between(.025, .065) : rng.Between(.12, .36));
                double width = p.MountainWidth * (massif ? rng.Between(1.7, 2.5) : rng.Between(1.0, 1.65));
                double phase = rng.Unit() * Math.PI * 2;
                var main = Controls(center, angle, length, curvature, phase, 7, rng);
                Add(main, width, rng.Between(.75, 1), broken, rng.Between(.13, .25));
                int auxiliary = rng.Int(p.RangesPerContinent); // 0..budget-1; unbranched belts remain possible.
                for (int branch = 0; branch < auxiliary; branch++)
                {
                    double position = rng.Between(.16, .84);
                    var start = Interpolate(main, position);
                    bool parallel = branch == 0 && style >= .75 && style < .9;
                    double branchAngle = angle + (rng.Unit() < .5 ? -1 : 1) * rng.Between(.45, 1.8);
                    double branchLength = feature.RadiusZ * rng.Between(.3, .85);
                    List<Knot> points;
                    if (parallel)
                    {
                        double offset = width * rng.Between(1.5, 2.5) * (rng.Unit() < .5 ? -1 : 1);
                        points = main.Select(k => k with { Point = new(k.Point.X - Math.Sin(angle) * offset, k.Point.Z + Math.Cos(angle) * offset) }).ToList();
                    }
                    else
                    {
                        // Anchor the beginning to an irregular interior position, rather than fixed trunk vertices.
                        var branchCenter = new MacroPoint(start.Point.X + Math.Cos(branchAngle) * branchLength * .5,
                            start.Point.Z + Math.Sin(branchAngle) * branchLength * .5);
                        points = Controls(branchCenter, branchAngle, branchLength, branchLength * rng.Between(.08, .22),
                            rng.Unit() * Math.PI * 2, 5, rng);
                        var shift = new MacroPoint(start.Point.X - points[0].Point.X, start.Point.Z - points[0].Point.Z);
                        points = points.Select(k => k with { Point = new(k.Point.X + shift.X, k.Point.Z + shift.Z) }).ToList();
                    }
                    Add(points, width * rng.Between(.4, .85), rng.Between(.35, .7), rng.Unit() < .2, rng.Between(.16, .3));
                }
                void Add(List<Knot> controls, double baseWidth, double amplitude, bool segmented, double taper)
                {
                    var points = new List<Knot>();
                    // Four samples per irregular control interval, bounded at 24 segments for the main belt.
                    for (int i = 0; i < controls.Count - 1; i++)
                        for (int j = 0; j < 4; j++) points.Add(Spline(controls, i, j / 4d, baseWidth, amplitude));
                    points.Add(controls[^1] with { Width = controls[^1].Width * baseWidth, Height = controls[^1].Height * amplitude });
                    var passes = new Pass[1 + rng.Int(3)];
                    for (int i = 0; i < passes.Length; i++) passes[i] = new(rng.Between(.2, .8), rng.Between(.025, .075),
                        segmented && i == 0 ? .99 : rng.Between(.25, .65));
                    double extent = points.Max(k => k.Width) * 3 + _width;
                    var belt = new Belt(points.ToArray(), passes, points.Min(k => k.Point.X) - extent, points.Min(k => k.Point.Z) - extent,
                        points.Max(k => k.Point.X) + extent, points.Max(k => k.Point.Z) + extent, taper);
                    // Identity ordinal follows canonical construction order; never depends on streaming/query order.
                    descriptors.Add(new(identity.FeatureId("range", feature.Owner, descriptors.Count),
                        Array.AsReadOnly(points.Select(k => new MacroPoint(k.Point.X + manifest.OriginX, k.Point.Z + manifest.OriginZ)).ToArray()), baseWidth, amplitude * _height));
                    belts.Add(belt);
                }
            }
        _belts = belts.ToArray(); Descriptors = descriptors.AsReadOnly();
    }

    public double Sample(double x, double z)
    {
        if (_belts.Length == 0) return 0;
        // Shared domain warp changes the footprint, not merely ridge height. Bounded by 0.58 * profile width per axis.
        double wx = x + _width * (.42 * Noise(x / (_width * 2.1), z / (_width * 2.1), _noise) +
            .16 * Noise(x / (_width * .63) + 17, z / (_width * .63), _noise + 1));
        double wz = z + _width * (.42 * Noise(x / (_width * 2.1) - 31, z / (_width * 2.1) + 9, _noise + 2) +
            .16 * Noise(x / (_width * .63), z / (_width * .63) - 13, _noise + 3));
        double combined = 0, rugged = -1;
        Span<double> distances = stackalloc double[MaximumSegmentsPerBelt];
        Span<double> projections = stackalloc double[MaximumSegmentsPerBelt];
        foreach (var belt in _belts)
        {
            if (x < belt.MinX || x > belt.MaxX || z < belt.MinZ || z > belt.MaxZ) continue;
            double nearest = double.PositiveInfinity;
            for (int i = 1; i < belt.Points.Length; i++)
            {
                var a = belt.Points[i - 1]; var b = belt.Points[i];
                double dx = b.Point.X - a.Point.X, dz = b.Point.Z - a.Point.Z;
                double t = Math.Clamp(((wx - a.Point.X) * dx + (wz - a.Point.Z) * dz) / (dx * dx + dz * dz), 0, 1);
                double ex = wx - a.Point.X - dx * t, ez = wz - a.Point.Z - dz * t, distance = ex * ex + ez * ez;
                projections[i-1] = t; distances[i-1] = distance; nearest = Math.Min(nearest, distance);
            }
            // Blend projection attributes near competing segments. Choosing only the nearest segment
            // makes width/height/U jump at its Voronoi boundary when a curved belt doubles back.
            // Minimum distance is continuous; compact smooth weights leave at least one weight = 1.
            double width = 0, amplitude = 0, along = 0, weightSum = 0;
            double blendDistanceSquared = _width * _width * .04;
            for (int i = 1; i < belt.Points.Length; i++)
            {
                double w = Smooth(1 - (distances[i-1] - nearest) / blendDistanceSquared);
                if (w <= 0) continue;
                var a = belt.Points[i-1]; var b = belt.Points[i]; double t = projections[i-1];
                width += (a.Width + (b.Width - a.Width) * t) * w;
                amplitude += (a.Height + (b.Height - a.Height) * t) * w;
                along += (a.U + (b.U - a.U) * t) * w; weightSum += w;
            }
            width /= weightSum; amplitude /= weightSum; along /= weightSum;
            double taper = Smooth(along / belt.Taper) * Smooth((1 - along) / belt.Taper);
            width *= .4 + .6 * taper;
            double radiusSquared = nearest / (width * width);
            if (radiusSquared >= 9 || taper <= 0) continue;
            double saddle = 1;
            foreach (var pass in belt.Passes) saddle *= 1 - pass.Depth * Math.Exp(-Math.Pow((along - pass.U) / pass.Span, 2));
            // Multiscale ridged relief and valleys inside a varying belt envelope; broad shoulders support foothills.
            if (rugged < 0)
            {
                // Rotate independent octaves; broad relief drives the shape and finer relief adds detail.
                // Compute once per query (the relief field is shared by overlapping belts).
                double nx = wx * .857 - wz * .515, nz = wx * .515 + wz * .857;
                double n1 = 1 - Math.Abs(Noise(nx / (_width * 1.1), nz / (_width * 1.1), _noise + 4));
                double n2 = 1 - Math.Abs(Noise((nx + nz) / (_width * .59), (nz - nx) / (_width * .59), _noise + 5));
                double n3 = 1 - Math.Abs(Noise(wx / (_width * .16), wz / (_width * .16), _noise + 6));
                rugged = n1 * n1 * (.45 + .55 * n2 * n2) * (.8 + .2 * n3);
            }
            double core = Math.Exp(-radiusSquared * 3.2) * (.28 + .72 * rugged);
            double shoulders = Math.Exp(-radiusSquared * .9) * (.08 + .24 * rugged);
            double contribution = Math.Min(1, (core + shoulders) * amplitude * taper * saddle * Smooth(9 - radiusSquared));
            // Bounded smooth union avoids max-composition creases and never exceeds the profile height budget.
            combined += (1 - combined) * contribution;
        }
        return combined * _height;
    }

    private static List<Knot> Controls(MacroPoint center, double angle, double length, double curvature, double phase, int count, RandomStream rng)
    {
        var steps = Enumerable.Range(0, count - 1).Select(_ => rng.Between(.6, 1.5)).ToArray(); double total = steps.Sum(), distance = 0;
        var result = new List<Knot>();
        for (int i = 0; i < count; i++)
        {
            double u = distance / total, along = (u - .5) * length;
            double across = curvature * (Math.Sin(u * Math.PI * 1.4 + phase) + .28 * Math.Sin(u * Math.PI * 3.2 - phase));
            result.Add(new(new(center.X + Math.Cos(angle) * along - Math.Sin(angle) * across,
                center.Z + Math.Sin(angle) * along + Math.Cos(angle) * across), rng.Between(.55, 1.5), rng.Between(.6, 1), u));
            if (i < steps.Length) distance += steps[i];
        }
        return result;
    }
    private static Knot Interpolate(List<Knot> points, double u)
    {
        int i = 0; while (i + 2 < points.Count && points[i + 1].U < u) i++;
        return Spline(points, i, (u - points[i].U) / (points[i + 1].U - points[i].U), 1, 1);
    }
    private static Knot Spline(List<Knot> points, int i, double t, double width, double height)
    {
        var a = points[Math.Max(0, i - 1)]; var b = points[i]; var c = points[i + 1]; var d = points[Math.Min(points.Count - 1, i + 2)];
        double Cubic(double p0, double p1, double p2, double p3) => .5 * ((2 * p1) + (-p0 + p2) * t +
            (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
        double smooth = Smooth(t);
        return new(new(Cubic(a.Point.X,b.Point.X,c.Point.X,d.Point.X), Cubic(a.Point.Z,b.Point.Z,c.Point.Z,d.Point.Z)),
            (b.Width + (c.Width - b.Width) * smooth) * width, (b.Height + (c.Height - b.Height) * smooth) * height, b.U + (c.U - b.U) * t);
    }
    private static double Smooth(double t) { t = Math.Clamp(t, 0, 1); return t * t * t * (t * (t * 6 - 15) + 10); }
    private static double Noise(double x, double z, ulong seed)
    {
        long ix = (long)Math.Floor(x), iz = (long)Math.Floor(z); double u = Smooth(x - ix), v = Smooth(z - iz);
        double Value(long a, long b) => ((Mix(unchecked((ulong)a * 0x9E3779B97F4A7C15UL ^ (ulong)b * 0xD1B54A32D192ED03UL) ^ seed) >> 11) * (1.0 / 9007199254740992.0)) * 2 - 1;
        double low = Value(ix, iz) * (1 - u) + Value(ix + 1, iz) * u, high = Value(ix, iz + 1) * (1 - u) + Value(ix + 1, iz + 1) * u;
        return low * (1 - v) + high * v;
    }
    private static ulong Mix(ulong value)
    {
        value = unchecked((value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL);
        value = unchecked((value ^ (value >> 27)) * 0x94D049BB133111EBUL); return value ^ (value >> 31);
    }
    private sealed class RandomStream
    {
        private ulong _state;
        public RandomStream(ulong seed) { _state = seed; }
        public double Unit() { _state = unchecked(_state + 0x9E3779B97F4A7C15UL); return (Mix(_state) >> 11) * (1.0 / 9007199254740992.0); }
        public double Between(double min, double max) => min + (max - min) * Unit();
        public int Int(int count) => (int)(Unit() * count);
    }
}
