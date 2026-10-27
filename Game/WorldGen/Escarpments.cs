using System;
using System.Collections.Generic;

namespace Thornwake.WorldGen;

/// <summary>
/// Escarpments and plateaus (landform no. 1, terrain revision 18, D232; docs/LANDFORMS.md): long, wandering lines inland
/// with a raised table-land on one side — a steep face, a broad top that slopes gently away behind it, ends that ramp
/// down (the ways up). Big landform: the step goes into the plan's macro height before erosion and rivers, as a ramp a
/// few raster cells wide, so rivers flow off the plateau, cut notches into its edge or run along its foot. At walking
/// scale <see cref="Ground"/> sharpens that ramp into the cliff face, fading where erosion or a river has cut through
/// the edge, so a valley through the scarp keeps its floor.
/// </summary>
public sealed class Escarpments : PlanLandform
{
    private sealed class Line
    {
        public required double[] X, Z, Along;
        public required double Length, Height, Depth;
        public required ulong Seed;
        public double MinX, MinZ, MaxX, MaxZ;
    }

    private const double Step = 2000, GridCell = 2000;
    private readonly List<Line> _lines = new();
    private MacroRaster _macro = null!, _profile = null!;
    private double _ox, _oz, _gridMinX, _gridMinZ;
    private int _gridSize;
    private int[] _gridStart = Array.Empty<int>(), _gridLines = Array.Empty<int>();

    public override LandformInfo Info => LandformCatalog.Escarpments;
    /// <summary>Number of escarpments placed in this world.</summary>
    public int Count => _lines.Count;

    /// <summary>The middle of each escarpment's edge: position, unit normal toward the plateau, designed height (tests,
    /// captures and Go to spots).</summary>
    public IEnumerable<(double X, double Z, double Nx, double Nz, double Height)> Probes()
    {
        foreach (var l in _lines)
        {
            int i = l.X.Length / 2;
            double dx = l.X[i] - l.X[i - 1], dz = l.Z[i] - l.Z[i - 1], len = Math.Sqrt(dx * dx + dz * dz);
            yield return (l.X[i], l.Z[i], -dz / len, dx / len, l.Height);
        }
    }

    public override void Shape(MacroRaster macro, WorldManifest manifest, LandformSetting setting)
    {
        _macro = macro; _ox = manifest.OriginX; _oz = manifest.OriginZ;
        Place(macro, manifest, setting);
        if (_lines.Count == 0) return;
        // The raster form of the profile: the face spread over 2.5 cells, so the bicubic raster holds it without ringing
        // (ringing left a trench along the foot, where lakes formed).
        _profile = new MacroRaster(macro.Size, macro.MinX, macro.MinZ, macro.Cell, 1);
        double face = macro.Cell * 2.5, mw = macro.Cell * 2;
        _profile.Fill((i, j, channels) =>
        {
            double x = _profile.CellX(i), z = _profile.CellZ(j), v = 0;
            foreach (var l in _lines)
                if (x >= l.MinX - l.Depth && x <= l.MaxX + l.Depth && z >= l.MinZ - l.Depth && z <= l.MaxZ + l.Depth)
                    v = Math.Max(v, Profile(l, x, z, face, mw, false, out _, out _, out _));
            channels[0][j * macro.Size + i] = (float)v;
        });
        var raised = _profile.Channel(0);
        var land = macro.Channel(WorldPlan.LandChannel);
        var height = macro.Channel(WorldPlan.HeightChannel);
        for (int k = 0; k < raised.Length; k++)
            if (raised[k] > 0) height[k] += (float)(raised[k] * DetMath.Smooth((land[k] - .2) / .3));
        BuildGrid(macro);
    }

    /// <summary>
    /// Lines from the world's seed: inland (land well above the coast), off the mountain cores, apart from each other.
    /// Amount scales how many (about one per 100 km × 100 km of land at "normal", at least one), size their height, depth
    /// and length.
    /// </summary>
    private void Place(MacroRaster macro, WorldManifest manifest, LandformSetting setting)
    {
        var rng = new RandomStream(LandformCatalog.Seed(manifest, Info));
        var land = macro.Channel(WorldPlan.LandChannel);
        var mountain = macro.Channel(WorldPlan.MountainChannel);
        int landCells = 0;
        var starts = new List<int>(); // (inland, off the mountain cores)
        for (int c = 0; c < land.Length; c++)
        {
            if (land[c] > .5f) landCells++;
            if (land[c] >= .6f && mountain[c] <= .3f) starts.Add(c);
        }
        double landKm2 = landCells * macro.Cell * macro.Cell / 1e6, world = manifest.Profile.WorldSize;
        int want = Math.Clamp((int)Math.Round(setting.Amount * landKm2 / (100 * 100)), landKm2 > 3000 ? 1 : 0, 64);
        double Land(double x, double z) => macro.Sample(WorldPlan.LandChannel, x, z);
        double Mountain(double x, double z) => macro.Sample(WorldPlan.MountainChannel, x, z);
        for (int attempt = 0; attempt < want * 40 && _lines.Count < want && starts.Count > 0; attempt++)
        {
            int k = starts[rng.Int(starts.Count)];
            double x = macro.CellX(k % macro.Size), z = macro.CellZ(k / macro.Size);
            double length = Math.Min(rng.Between(20000, 70000) * setting.Size, world * .35);
            double height = rng.Between(60, 180) * setting.Size, depth = rng.Between(8000, 20000) * Math.Sqrt(setting.Size);
            double angle = rng.Unit() * Math.Tau, side = rng.Unit() < .5 ? -1 : 1, turn = rng.Between(-.06, .06);
            ulong seed = rng.Next();
            // The face looks downhill: the plateau is the side the land already rises toward, so the lowland drains away
            // from the foot (facing uphill, the plateau dammed the land's water into lakes against the cliff).
            double ux = -Math.Sin(angle), uz = Math.Cos(angle);
            double rise = macro.Sample(WorldPlan.HeightChannel, x + ux * 8000, z + uz * 8000) - macro.Sample(WorldPlan.HeightChannel, x - ux * 8000, z - uz * 8000);
            if (Math.Abs(rise) > 5) side = rise > 0 ? 1 : -1;
            var xs = new List<double> { x }; var zs = new List<double> { z };
            for (double run = 0; run < length; run += Step)
            {
                turn = Math.Clamp(turn + rng.Between(-.06, .06), -.12, .12); // (gentle bends: the plateau behind stays whole)
                angle += turn;
                double nx = x + Math.Cos(angle) * Step, nz = z + Math.Sin(angle) * Step;
                double px = -Math.Sin(angle) * side, pz = Math.Cos(angle) * side; // toward the plateau
                // The edge stays inland on both sides; the plateau stays land a good way back; no mountains; apart.
                if (Land(nx, nz) < .55 || Mountain(nx, nz) > .4 || Land(nx - px * 3000, nz - pz * 3000) < .5 ||
                    Land(nx + px * 3000, nz + pz * 3000) < .5 || Land(nx + px * depth * .6, nz + pz * depth * .6) < .4 || Near(nx, nz, 15000))
                    break;
                x = nx; z = nz; xs.Add(x); zs.Add(z);
            }
            if ((xs.Count - 1) * Step < 15000) continue;
            var along = new double[xs.Count];
            for (int i = 1; i < xs.Count; i++) along[i] = along[i - 1] + Math.Sqrt(Sq(xs[i] - xs[i - 1]) + Sq(zs[i] - zs[i - 1]));
            // Store the line with the plateau on its left (positive signed distance), whichever side was drawn.
            if (side < 0) { xs.Reverse(); zs.Reverse(); for (int i = 0; i < along.Length; i++) along[i] = along[^1] - along[i]; Array.Reverse(along); }
            var line = new Line { X = xs.ToArray(), Z = zs.ToArray(), Along = along, Length = along[^1], Height = height, Depth = depth, Seed = seed };
            line.MinX = line.MaxX = line.X[0]; line.MinZ = line.MaxZ = line.Z[0];
            for (int i = 1; i < line.X.Length; i++)
            {
                line.MinX = Math.Min(line.MinX, line.X[i]); line.MaxX = Math.Max(line.MaxX, line.X[i]);
                line.MinZ = Math.Min(line.MinZ, line.Z[i]); line.MaxZ = Math.Max(line.MaxZ, line.Z[i]);
            }
            _lines.Add(line);
        }
    }

    private bool Near(double x, double z, double distance)
    {
        foreach (var l in _lines)
            for (int i = 0; i < l.X.Length; i++)
                if (Sq(l.X[i] - x) + Sq(l.Z[i] - z) < distance * distance) return true;
        return false;
    }

    /// <summary>Lines near each 2 km cell (within reach of the sharpened face).</summary>
    private void BuildGrid(MacroRaster macro)
    {
        double world = macro.Size * macro.Cell, reach = macro.Cell * 2.5 + 400;
        _gridMinX = macro.MinX; _gridMinZ = macro.MinZ;
        _gridSize = (int)Math.Ceiling(world / GridCell);
        var cells = new List<int>[_gridSize * _gridSize];
        for (int n = 0; n < _lines.Count; n++)
        {
            var l = _lines[n];
            int i0 = Cell(l.MinX - reach, _gridMinX), i1 = Cell(l.MaxX + reach, _gridMinX), j0 = Cell(l.MinZ - reach, _gridMinZ), j1 = Cell(l.MaxZ + reach, _gridMinZ);
            for (int j = j0; j <= j1; j++)
                for (int i = i0; i <= i1; i++)
                {
                    double cx = _gridMinX + (i + .5) * GridCell, cz = _gridMinZ + (j + .5) * GridCell;
                    Nearest(l, cx, cz, out double s, out _, out _, out _);
                    if (Math.Abs(s) > reach + GridCell * .71) continue;
                    (cells[j * _gridSize + i] ??= new List<int>()).Add(n);
                }
        }
        _gridStart = new int[cells.Length + 1];
        var items = new List<int>();
        for (int c = 0; c < cells.Length; c++) { _gridStart[c] = items.Count; if (cells[c] != null) items.AddRange(cells[c]); }
        _gridStart[^1] = items.Count;
        _gridLines = items.ToArray();
    }

    private int Cell(double v, double min) => Math.Clamp((int)Math.Floor((v - min) / GridCell), 0, _gridSize - 1);

    /// <summary>
    /// Walking-scale correction (metres): the sharp face minus the ramp the raster holds, near the edge only. Scaled by
    /// how much of the step the eroded raster still has there, so valleys cut through the edge keep their floors.
    /// </summary>
    public override double Ground(double x, double z, double minWavelength)
    {
        if (_lines.Count == 0) return 0;
        int c = Cell(z, _gridMinZ) * _gridSize + Cell(x, _gridMinX);
        int from = _gridStart[c], to = _gridStart[c + 1];
        if (from == to) return 0;
        double cell = _macro.Cell, best = 0, bestAbs = double.MaxValue, nx = 0, nz = 0;
        for (int n = from; n < to; n++)
        {
            var l = _lines[_gridLines[n]];
            double v = Profile(l, x, z, Math.Max(Face(l), minWavelength * .6), minWavelength, true, out double s, out double ux, out double uz);
            if (Math.Abs(s) < bestAbs) { bestAbs = Math.Abs(s); nx = ux; nz = uz; }
            best = Math.Max(best, v);
        }
        double reach = DetMath.Smooth((cell * 2.25 - bestAbs) / (cell * .5));
        if (reach <= 0) return 0;
        double correction = best - _profile.Sample(0, x, z);
        if (correction == 0) return 0;
        // How much of the step survives erosion here: the eroded height across the edge against the step put in.
        double d = cell * 1.5;
        double topX = x + nx * d, topZ = z + nz * d, footX = x - nx * d, footZ = z - nz * d;
        double put = _profile.Sample(0, topX, topZ) - _profile.Sample(0, footX, footZ);
        double kept = _macro.Sample(WorldPlan.HeightChannel, topX, topZ) - _macro.Sample(WorldPlan.HeightChannel, footX, footZ);
        double survive = put > 1 ? DetMath.Smooth((kept / put - .3) / .5) : 0;
        return correction * reach * survive;
    }

    /// <summary>Face width at walking scale: steep (about 65°) rock, a little wider for taller faces.</summary>
    private static double Face(Line l) => 12 + l.Height * .45;

    /// <summary>
    /// Raised height (m) of one escarpment at a point: the face of width <paramref name="face"/> centred on the
    /// (wobbling) edge, a flat top for 55 % of the depth, then a gentle fall behind; ends ramp down, height varies along.
    /// With <paramref name="detail"/> (walking scale; the raster form has none of it, D233) the face is broken up:
    /// ravines cut back into the plateau, buttresses jut out, the face is steeper or gentler along its length, the rim
    /// is uneven, and a scree apron lies along the foot with bigger fans below the ravines.
    /// Also gives the signed distance to the edge and the unit normal toward the plateau.
    /// </summary>
    private double Profile(Line l, double x, double z, double face, double minWavelength, bool detail, out double s, out double nx, out double nz)
    {
        Nearest(l, x, z, out s, out double t, out nx, out nz);
        double lx = x - _ox, lz = z - _oz;
        s += 90 * Noise.Fbm(lx, lz, 700, 3, minWavelength, l.Seed) + 260 * Noise.Fbm(lx, lz, 4000, 2, minWavelength, l.Seed + 1);
        double ramp = l.Length * .18;
        double taper = DetMath.Smooth(t / ramp) * DetMath.Smooth((l.Length - t) / ramp);
        // (features fade out on coarse tiles: they are tens of metres wide)
        double feat = detail ? 1 - DetMath.Smooth((minWavelength - 40) / 120) : 0;
        if (taper <= 0 || s < -face - feat * (l.Height * .95 + 130)) return 0;
        double h = l.Height * taper * (.7 + .6 * DetMath.Smooth(.5 + Noise.Fbm(t, 0, 9000, 2, 0, l.Seed + 2)));
        double back = s <= l.Depth * .55 ? 1 : 1 - DetMath.Smooth((s - l.Depth * .55) / (l.Depth * .45));
        if (feat <= 0) return h * DetMath.Smooth((s + face * .5) / face) * back;

        // Face steepness along the cliff (sheer walls and broken, gentler stretches) and an uneven rim.
        double fw = face * (.6 + 1.1 * DetMath.Smooth(.5 + 1.4 * Noise.Fbm(t, 1, 900, 2, 0, l.Seed + 3)));
        double rim = h * (1 + .06 * feat * Noise.Fbm(lx, lz, 160, 2, minWavelength, l.Seed + 4));
        // Ravines and buttresses: one feature (or none) per 260 m of edge.
        const double Spacing = 260;
        long c0 = (long)Math.Floor(t / Spacing);
        double shift = 0, ravine = 0, floor = 0, fan = 0;
        for (long c = c0 - 1; c <= c0 + 1; c++)
        {
            ulong hash = Hashing.Lattice(c, 7, l.Seed + 5);
            double kind = Hashing.Unit(hash), tc = (c + .2 + .6 * Hashing.Unit(Hashing.Mix(hash + 1))) * Spacing;
            double width = Hashing.Unit(Hashing.Mix(hash + 2)), size = Hashing.Unit(Hashing.Mix(hash + 3)), a = Math.Abs(t - tc);
            if (kind < .4)
            {
                // Ravine: a V-shaped notch narrowing back into the plateau, its floor rising from the foot to the rim.
                double depth = (70 + 190 * size) * Math.Sqrt(Math.Min(1, h / 100)), w0 = 22 + 40 * width;
                double into = Math.Clamp(s / depth, 0, 1), half = w0 * (1 - .75 * into);
                double inside = DetMath.Smooth((half - a) / (half * .6 + 4)) * DetMath.Smooth((depth - s) / (depth * .25 + 8));
                if (inside > ravine) { ravine = inside; floor = .85 * DetMath.Smooth(into); }
                fan = Math.Max(fan, DetMath.Smooth((w0 * 1.8 - a) / (w0 * 1.2)));
            }
            else if (kind < .75)
            {
                // Buttress: the edge juts out toward the lowland.
                double jut = (25 + 85 * size) * Math.Min(1, h / 80), half = 35 + 70 * width;
                shift = Math.Min(shift, -jut * DetMath.Smooth((half - a) / (half * .6)));
            }
        }
        double e = s - shift * feat;
        double v = rim * DetMath.Smooth((e + fw * .5) / fw) * back;
        if (ravine > 0) v += (h * floor * back - v) * ravine * feat;
        // Scree apron: rock fallen from the face, ~22 % of the height at the foot, thinning out over 0.7 × the height;
        // fans below ravines are higher and reach further.
        double foot = e + fw * .5;
        if (foot < fw)
        {
            double apron = h * (.7 + .5 * fan), top = h * (.22 + .12 * fan);
            double scree = foot >= 0 ? top : top * Math.Pow(Math.Max(0, 1 + foot / apron), 2);
            v = Math.Max(v, scree * feat);
        }
        return v;
    }

    /// <summary>Nearest point of the line: signed distance (positive on the plateau side, left of the line), position
    /// along it, unit normal toward the plateau.</summary>
    private static void Nearest(Line l, double x, double z, out double s, out double t, out double nx, out double nz)
    {
        double best = double.MaxValue; s = 0; t = 0; nx = 0; nz = 1;
        for (int i = 0; i + 1 < l.X.Length; i++)
        {
            double ax = l.X[i], az = l.Z[i], dx = l.X[i + 1] - ax, dz = l.Z[i + 1] - az, len2 = dx * dx + dz * dz;
            double u = Math.Clamp(((x - ax) * dx + (z - az) * dz) / len2, 0, 1);
            double px = ax + dx * u - x, pz = az + dz * u - z, d2 = px * px + pz * pz;
            if (d2 >= best) continue;
            best = d2;
            double len = Math.Sqrt(len2), cross = (dx * (z - az) - dz * (x - ax)) / len;
            nx = -dz / len; nz = dx / len;
            s = cross >= 0 ? Math.Sqrt(d2) : -Math.Sqrt(d2);
            t = l.Along[i] + u * len;
        }
    }

    private static double Sq(double v) => v * v;
}
