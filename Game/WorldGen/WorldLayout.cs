using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.WorldGen;

public readonly record struct Point2(double X, double Z);

/// <summary>A continent or island footprint (A4 <c>MacroLandFeature</c>). Positions are absolute world metres.</summary>
public sealed record LandFeature(string Id, bool Island, Point2 Center, double RadiusX, double RadiusZ, double Angle, ulong NoiseSeed)
{
    internal double Cos { get; } = DetMath.Cos(Angle);
    internal double Sin { get; } = DetMath.Sin(Angle);
    internal double LobePhase { get; } = Hashing.Unit(NoiseSeed) * DetMath.TwoPi;
}

/// <summary>Spline knot of a mountain belt, relative to the world origin.</summary>
public readonly record struct BeltKnot(Point2 Point, double Width, double Height, double U);
public readonly record struct BeltPass(double U, double Span, double Depth);

/// <summary>A mountain belt (A4 <c>MacroMountainsV4</c> belt): densified spline, passes, taper, bounding box.</summary>
public sealed record MountainBelt(string Id, int Continent, BeltKnot[] Points, BeltPass[] Passes, double Taper,
    double MinX, double MinZ, double MaxX, double MaxZ, double BaseWidth, double Amplitude);

/// <summary>
/// Macro layout: where continents, islands and mountain belts are. A faithful port of the accepted A4 layout
/// (same seeded streams and construction order), using <see cref="DetMath"/> so it is identical on every platform.
/// </summary>
public sealed class WorldLayout
{
    public WorldManifest Manifest { get; }
    public IReadOnlyList<LandFeature> Land => _land;
    public IReadOnlyList<MountainBelt> Belts => _belts;
    // Arrays (not interfaces) on the hot paths: interface foreach allocates an enumerator per raster cell.
    private readonly LandFeature[] _land;
    private readonly MountainBelt[] _belts;
    /// <summary>Seed of the mountain relief field (A4 "mountain-field").</summary>
    public ulong MountainNoise { get; }
    /// <summary>Seed of the A4 hill field ("relief").</summary>
    public ulong ReliefNoise { get; }

    public const int MaximumSegmentsPerBelt = 32;

    public WorldLayout(WorldManifest manifest)
    {
        manifest.Validate();
        Manifest = manifest;
        var p = manifest.Profile;
        // A4 derives geography from algorithm 3's identity and only mountains from algorithm 4.
        ulong Seed3(string layer, int ordinal = 0) => Hashing.FeatureSeed(manifest.Seed, 3, manifest.ProfileDigest, layer, 0, 0, ordinal);
        string Id3(string layer, Point2 owner, int ordinal) =>
            Hashing.FeatureId(manifest.Seed, 3, manifest.ProfileDigest, layer, (long)Math.Floor(owner.X / 16), (long)Math.Floor(owner.Z / 16), ordinal);
        ReliefNoise = Seed3("relief");

        var land = new List<LandFeature>();
        for (int i = 0; i < p.Continents + p.Islands; i++)
        {
            bool island = i >= p.Continents;
            var random = new RandomStream(Seed3(island ? "island-shape" : "continent-shape", i));
            double size = (island ? p.IslandRadius : p.ContinentRadius) * (.8 + random.Unit() * .2);
            double x, z;
            if (!island)
            {
                // Bounded rejection separates major cores without an obvious ring/grid layout.
                double limit = p.WorldSize * .46 - size * 1.3, best = double.NegativeInfinity; x = 0; z = 0;
                for (int attempt = 0; attempt < 160; attempt++)
                {
                    double cx = (random.Unit() * 2 - 1) * limit, cz = (random.Unit() * 2 - 1) * limit;
                    double clearance = double.PositiveInfinity;
                    foreach (var other in land)
                    {
                        double dx = cx - (other.Center.X - manifest.OriginX), dz = cz - (other.Center.Z - manifest.OriginZ);
                        clearance = Math.Min(clearance, Math.Sqrt(dx * dx + dz * dz) - (size + other.RadiusX) * 1.15);
                    }
                    if (clearance > best) { best = clearance; x = cx; z = cz; }
                    if (clearance >= 0) break;
                }
            }
            else
            {
                // Independent island groups make chains/archipelagos rather than uniform confetti.
                int groups = Math.Max(1, Math.Min(12, p.Islands / 10));
                var group = new RandomStream(Seed3("island-group", (i - p.Continents) % groups));
                double gx = (group.Unit() - .5) * p.WorldSize * .76, gz = (group.Unit() - .5) * p.WorldSize * .76, angle = group.Unit() * DetMath.TwoPi;
                double along = (random.Unit() - .5) * p.IslandRadius * 12, across = (random.Unit() - .5) * p.IslandRadius * 4;
                double c = DetMath.Cos(angle), s = DetMath.Sin(angle);
                x = DetMath.Clamp(gx + c * along - s * across, -p.WorldSize * .43, p.WorldSize * .43);
                z = DetMath.Clamp(gz + s * along + c * across, -p.WorldSize * .43, p.WorldSize * .43);
            }
            var center = new Point2(x + manifest.OriginX, z + manifest.OriginZ);
            string id = Id3(island ? "island" : "continent", center, i);
            double radiusZ = size * (.6 + random.Unit() * .3);
            double featureAngle = random.Unit() * DetMath.Pi;
            ulong noiseSeed = random.Next();
            land.Add(new LandFeature(id, island, center, size, radiusZ, featureAngle, noiseSeed));
        }
        _land = land.ToArray();
        (var belts, MountainNoise) = BuildBelts(manifest, land);
        _belts = belts.ToArray();
    }

    // ---------------------------------------------------------------- mountains (A4 MacroMountainsV4 constructor)

    private static (IReadOnlyList<MountainBelt>, ulong) BuildBelts(WorldManifest manifest, List<LandFeature> land)
    {
        var p = manifest.Profile;
        ulong Seed4(string layer, int ordinal = 0) => Hashing.FeatureSeed(manifest.Seed, 4, manifest.ProfileDigest, layer, 0, 0, ordinal);
        ulong noise = Seed4("mountain-field");
        var belts = new List<MountainBelt>();
        if (p.RangesPerContinent <= 0 || p.MountainHeight <= 0) return (belts, noise);
        for (int continent = 0; continent < p.Continents; continent++)
        {
            var feature = land[continent];
            var rng = new RandomStream(Seed4("mountain-shape", continent));
            var center = new Point2(feature.Center.X - manifest.OriginX, feature.Center.Z - manifest.OriginZ);
            double angle = feature.Angle + rng.Between(-.25, .25), style = rng.Unit();
            bool broken = style >= .55 && style < .75, massif = style >= .9;
            double length = feature.RadiusX * (massif ? rng.Between(.55, .85) : rng.Between(1.1, 1.6));
            double curvature = feature.RadiusZ * (style < .18 ? rng.Between(.025, .065) : rng.Between(.12, .36));
            double width = p.MountainWidth * (massif ? rng.Between(1.7, 2.5) : rng.Between(1.0, 1.65));
            double phase = rng.Unit() * DetMath.TwoPi;
            var main = Controls(center, angle, length, curvature, phase, 7, rng);
            double mainAmplitude = rng.Between(.75, 1), mainTaper = rng.Between(.13, .25);
            Add(main, width, mainAmplitude, broken, mainTaper);
            int auxiliary = rng.Int(p.RangesPerContinent);
            for (int branch = 0; branch < auxiliary; branch++)
            {
                double position = rng.Between(.16, .84);
                var start = Interpolate(main, position);
                bool parallel = branch == 0 && style >= .75 && style < .9;
                double side = rng.Unit() < .5 ? -1 : 1;
                double branchAngle = angle + side * rng.Between(.45, 1.8);
                double branchLength = feature.RadiusZ * rng.Between(.3, .85);
                List<BeltKnot> points;
                if (parallel)
                {
                    double offsetScale = rng.Between(1.5, 2.5);
                    double offset = width * offsetScale * (rng.Unit() < .5 ? -1 : 1);
                    double sa = DetMath.Sin(angle), ca = DetMath.Cos(angle);
                    points = main.Select(k => k with { Point = new(k.Point.X - sa * offset, k.Point.Z + ca * offset) }).ToList();
                }
                else
                {
                    var branchCenter = new Point2(start.Point.X + DetMath.Cos(branchAngle) * branchLength * .5,
                        start.Point.Z + DetMath.Sin(branchAngle) * branchLength * .5);
                    double branchCurvature = branchLength * rng.Between(.08, .22);
                    double branchPhase = rng.Unit() * DetMath.TwoPi;
                    points = Controls(branchCenter, branchAngle, branchLength, branchCurvature, branchPhase, 5, rng);
                    var shift = new Point2(start.Point.X - points[0].Point.X, start.Point.Z - points[0].Point.Z);
                    points = points.Select(k => k with { Point = new(k.Point.X + shift.X, k.Point.Z + shift.Z) }).ToList();
                }
                double bw = width * rng.Between(.4, .85), amp = rng.Between(.35, .7);
                bool seg = rng.Unit() < .2;
                double taper = rng.Between(.16, .3);
                Add(points, bw, amp, seg, taper);
            }

            void Add(List<BeltKnot> controls, double baseWidth, double amplitude, bool segmented, double taper)
            {
                var points = new List<BeltKnot>();
                for (int i = 0; i < controls.Count - 1; i++)
                    for (int j = 0; j < 4; j++) points.Add(Spline(controls, i, j / 4d, baseWidth, amplitude));
                points.Add(controls[^1] with { Width = controls[^1].Width * baseWidth, Height = controls[^1].Height * amplitude });
                var passes = new BeltPass[1 + rng.Int(3)];
                for (int i = 0; i < passes.Length; i++)
                {
                    double u = rng.Between(.2, .8), span = rng.Between(.025, .075);
                    double depth = segmented && i == 0 ? .99 : rng.Between(.25, .65);
                    passes[i] = new(u, span, depth);
                }
                double extent = points.Max(k => k.Width) * 3 + p.MountainWidth;
                string id = Hashing.FeatureId(manifest.Seed, 4, manifest.ProfileDigest, "range",
                    (long)Math.Floor(feature.Center.X / 16), (long)Math.Floor(feature.Center.Z / 16), belts.Count);
                belts.Add(new MountainBelt(id, continent, points.ToArray(), passes, taper,
                    points.Min(k => k.Point.X) - extent, points.Min(k => k.Point.Z) - extent,
                    points.Max(k => k.Point.X) + extent, points.Max(k => k.Point.Z) + extent, baseWidth, amplitude * p.MountainHeight));
            }
        }
        return (belts, noise);
    }

    private static List<BeltKnot> Controls(Point2 center, double angle, double length, double curvature, double phase, int count, RandomStream rng)
    {
        var steps = new double[count - 1];
        for (int i = 0; i < steps.Length; i++) steps[i] = rng.Between(.6, 1.5);
        double total = steps.Sum(), distance = 0, c = DetMath.Cos(angle), s = DetMath.Sin(angle);
        var result = new List<BeltKnot>();
        for (int i = 0; i < count; i++)
        {
            double u = distance / total, along = (u - .5) * length;
            double across = curvature * (DetMath.Sin(u * DetMath.Pi * 1.4 + phase) + .28 * DetMath.Sin(u * DetMath.Pi * 3.2 - phase));
            var point = new Point2(center.X + c * along - s * across, center.Z + s * along + c * across);
            double width = rng.Between(.55, 1.5), height = rng.Between(.6, 1);
            result.Add(new(point, width, height, u));
            if (i < steps.Length) distance += steps[i];
        }
        return result;
    }

    private static BeltKnot Interpolate(List<BeltKnot> points, double u)
    {
        int i = 0;
        while (i + 2 < points.Count && points[i + 1].U < u) i++;
        return Spline(points, i, (u - points[i].U) / (points[i + 1].U - points[i].U), 1, 1);
    }

    private static BeltKnot Spline(List<BeltKnot> points, int i, double t, double width, double height)
    {
        var a = points[Math.Max(0, i - 1)]; var b = points[i]; var c = points[i + 1]; var d = points[Math.Min(points.Count - 1, i + 2)];
        double Cubic(double p0, double p1, double p2, double p3) => .5 * ((2 * p1) + (-p0 + p2) * t +
            (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t * t * t);
        double smooth = DetMath.Smooth(t);
        return new(new(Cubic(a.Point.X, b.Point.X, c.Point.X, d.Point.X), Cubic(a.Point.Z, b.Point.Z, c.Point.Z, d.Point.Z)),
            (b.Width + (c.Width - b.Width) * smooth) * width, (b.Height + (c.Height - b.Height) * smooth) * height, b.U + (c.U - b.U) * t);
    }

    // ---------------------------------------------------------------- analytic fields (rasterized by WorldPlan)

    /// <summary>
    /// A4 land influence at an absolute position: &gt; 0 on land, ≤ 0 at sea. Expensive (loops over features);
    /// evaluated only when building the raster.
    /// </summary>
    public double LandInfluence(double x, double z)
    {
        var p = Manifest.Profile;
        double lx = x - Manifest.OriginX, lz = z - Manifest.OriginZ;
        double edge = (p.WorldSize * .5 - Math.Max(Math.Abs(lx), Math.Abs(lz))) / (p.WorldSize * .06);
        if (edge <= 0) return Math.Min(-1, edge - .3);
        double land = -1;
        foreach (var f in _land)
        {
            double dx = x - f.Center.X, dz = z - f.Center.Z;
            if (Math.Abs(dx) > f.RadiusX * 3 || Math.Abs(dz) > f.RadiusX * 3) continue;
            double scale = f.RadiusX, cr = p.CoastRoughness;
            double wx = dx + Noise.Value(dx / (scale * .7), dz / (scale * .7), f.NoiseSeed) * scale * cr * .5;
            double wz = dz + Noise.Value(dx / (scale * .7) + 31.7, dz / (scale * .7) - 18.3, f.NoiseSeed) * scale * cr * .5;
            double a = (f.Cos * wx + f.Sin * wz) / f.RadiusX;
            double b = (-f.Sin * wx + f.Cos * wz) / f.RadiusZ;
            double coast = (Noise.Value(dx / (scale * .28), dz / (scale * .28), f.NoiseSeed) * .7 +
                Noise.Value(dx / (scale * .09), dz / (scale * .09), f.NoiseSeed + 1) * .3) * cr;
            double radius = Math.Sqrt(a * a + b * b), theta = DetMath.Atan2(b, a), phase = f.LobePhase;
            double lobes = cr * (.9 * DetMath.Sin(theta * 2 + phase) + .65 * DetMath.Sin(theta * 3 - phase) + .4 * DetMath.Sin(theta * 5 + phase * .7));
            double influence = 1 - radius + coast + lobes * DetMath.Smooth(radius / .6) * (f.Island ? .55 : 1);
            if (influence > land) land = influence;
        }
        return Math.Min(land, edge - .3);
    }

    /// <summary>Domain warp applied to mountain footprints and relief (A4). Origin-relative coordinates.</summary>
    public void MountainWarp(double lx, double lz, out double wx, out double wz)
    {
        double w = Manifest.Profile.MountainWidth;
        ulong n = MountainNoise;
        wx = lx + w * (.42 * Noise.Value(lx / (w * 2.1), lz / (w * 2.1), n) + .16 * Noise.Value(lx / (w * .63) + 17, lz / (w * .63), n + 1));
        wz = lz + w * (.42 * Noise.Value(lx / (w * 2.1) - 31, lz / (w * 2.1) + 9, n + 2) + .16 * Noise.Value(lx / (w * .63), lz / (w * .63) - 13, n + 3));
    }

    /// <summary>A4 multiscale ridged relief in [0, 1] at warped origin-relative coordinates.</summary>
    public double Rugged(double wx, double wz)
    {
        double w = Manifest.Profile.MountainWidth;
        ulong n = MountainNoise;
        double nx = wx * .857 - wz * .515, nz = wx * .515 + wz * .857;
        double n1 = 1 - Math.Abs(Noise.Value(nx / (w * 1.1), nz / (w * 1.1), n + 4));
        double n2 = 1 - Math.Abs(Noise.Value((nx + nz) / (w * .59), (nz - nx) / (w * .59), n + 5));
        double n3 = 1 - Math.Abs(Noise.Value(wx / (w * .16), wz / (w * .16), n + 6));
        return n1 * n1 * (.45 + .55 * n2 * n2) * (.8 + .2 * n3);
    }

    /// <summary>A4 ridged relief without its finest octave: n1² · (.45 + .55 n2²), in [0, 1].</summary>
    public double RuggedBroad(double wx, double wz)
    {
        double w = Manifest.Profile.MountainWidth;
        ulong n = MountainNoise;
        double nx = wx * .857 - wz * .515, nz = wx * .515 + wz * .857;
        double n1 = 1 - Math.Abs(Noise.Value(nx / (w * 1.1), nz / (w * 1.1), n + 4));
        double n2 = 1 - Math.Abs(Noise.Value((nx + nz) / (w * .59), (nz - nx) / (w * .59), n + 5));
        return n1 * n1 * (.45 + .55 * n2 * n2);
    }

    /// <summary>
    /// Mountain envelope without the relief noise: the "core" and "shoulder" weights such that A4's mountain
    /// height ≈ min(1, core·(.28 + .72·rugged) + shoulder·(.08 + .24·rugged)) · MountainHeight.
    /// Overlapping belts combine by smooth union. Origin-relative coordinates.
    /// </summary>
    public void MountainEnvelope(double lx, double lz, out double core, out double shoulder)
    {
        core = 0; shoulder = 0;
        if (_belts.Length == 0) return;
        double profileWidth = Manifest.Profile.MountainWidth;
        MountainWarp(lx, lz, out double wx, out double wz);
        Span<double> distances = stackalloc double[MaximumSegmentsPerBelt];
        Span<double> projections = stackalloc double[MaximumSegmentsPerBelt];
        foreach (var belt in _belts)
        {
            if (lx < belt.MinX || lx > belt.MaxX || lz < belt.MinZ || lz > belt.MaxZ) continue;
            double nearest = double.PositiveInfinity;
            var pts = belt.Points;
            for (int i = 1; i < pts.Length; i++)
            {
                var a = pts[i - 1]; var b = pts[i];
                double dx = b.Point.X - a.Point.X, dz = b.Point.Z - a.Point.Z;
                double t = DetMath.Clamp(((wx - a.Point.X) * dx + (wz - a.Point.Z) * dz) / (dx * dx + dz * dz), 0, 1);
                double ex = wx - a.Point.X - dx * t, ez = wz - a.Point.Z - dz * t, distance = ex * ex + ez * ez;
                projections[i - 1] = t; distances[i - 1] = distance;
                if (distance < nearest) nearest = distance;
            }
            double width = 0, amplitude = 0, along = 0, weightSum = 0;
            double blend = profileWidth * profileWidth * .04;
            for (int i = 1; i < pts.Length; i++)
            {
                double w = DetMath.Smooth(1 - (distances[i - 1] - nearest) / blend);
                if (w <= 0) continue;
                var a = pts[i - 1]; var b = pts[i]; double t = projections[i - 1];
                width += (a.Width + (b.Width - a.Width) * t) * w;
                amplitude += (a.Height + (b.Height - a.Height) * t) * w;
                along += (a.U + (b.U - a.U) * t) * w;
                weightSum += w;
            }
            width /= weightSum; amplitude /= weightSum; along /= weightSum;
            double taper = DetMath.Smooth(along / belt.Taper) * DetMath.Smooth((1 - along) / belt.Taper);
            width *= .4 + .6 * taper;
            double r2 = nearest / (width * width);
            if (r2 >= 9 || taper <= 0) continue;
            double saddle = 1;
            foreach (var pass in belt.Passes)
            {
                double q = (along - pass.U) / pass.Span;
                saddle *= 1 - pass.Depth * DetMath.Exp(-q * q);
            }
            double k = amplitude * taper * saddle * DetMath.Smooth(9 - r2);
            double c = Math.Min(1, DetMath.Exp(-r2 * 3.2) * k), s = Math.Min(1, DetMath.Exp(-r2 * .9) * k);
            core += (1 - core) * c;
            shoulder += (1 - shoulder) * s;
        }
    }
}
