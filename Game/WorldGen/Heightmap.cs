using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace Thornwake.WorldGen;

/// <summary>
/// "Natural" world shapes (D38): a world type's heightmap template, Thornwake's own take on the way Azgaar's Fantasy
/// Map Generator builds land (ideas only, no code): a coarse hex grid of heights 0..100 (sea level 20) shaped by a
/// list of steps. A hill does not have a geometric outline: its height spreads cell by cell to the neighbours, each
/// getting the parent's height to a power below 1 times a random 0.9–1.1, so the randomness compounds into irregular,
/// natural coastlines. Ranges and troughs are wandering paths whose height spreads sideways; straits cut channels
/// across the map. The result replaces the placed continents in the macro layer (land influence, broad relief,
/// mountains); everything after (erosion, rivers, coasts, the 3D terrain) is unchanged.
///
/// Steps, one per line (ranges "a-b" pick per seed; x, y are percent of the world, y from north to south):
/// <code>
/// Hill     count height x y      raised blob(s)
/// Pit      count depth  x y      sunk blob(s) (lakes, bays)
/// Range    count height x y [x2 y2]   mountain chain(s) from x,y (toward x2,y2, else a random direction)
/// Trough   count depth  x y [x2 y2]   long valley(s) / rifts
/// Strait   width vertical|horizontal   sea channel across the map
/// Mask     fraction              fade land toward the edges (negative: toward the centre)
/// Add      value land|all|a-b    raise/lower heights in a range
/// Multiply factor land|all|a-b   scale land heights above the sea
/// Smooth   fraction              blend with the neighbours (2 = half)
/// Invert   probability x|y|both  mirror the map
/// </code>
/// </summary>
public sealed class Heightmap
{
    public const int N = 160;          // cells per side (25,600 cells)
    public const double SeaLevel = 20;
    private const double BlobPower = .985, LinePower = .86, BlobFade = .35;
    public static readonly string[] Tools = { "Hill", "Pit", "Range", "Trough", "Strait", "Mask", "Add", "Multiply", "Smooth", "Invert" };

    private readonly double[] _h = new double[N * N];
    private readonly RandomStream _rng;
    public IReadOnlyList<double> Heights => _h;

    public Heightmap(IReadOnlyList<string> steps, ulong seed)
    {
        _rng = new RandomStream(seed);
        foreach (var line in steps) Run(Parse(line));
        Smooth(3); // single-cell roughness would read as speckled, mossy coasts at world scale
    }

    // ------------------------------------------------------------------ parsing / validation

    private sealed record Step(string Tool, string[] Args);

    private static Step Parse(string line)
    {
        var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) throw new InvalidDataException("Empty heightmap step.");
        string tool = Array.Find(Tools, t => string.Equals(t, parts[0], StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Unknown heightmap step '{parts[0]}' (use {string.Join(", ", Tools)}).");
        int need = tool switch { "Hill" or "Pit" or "Range" or "Trough" => 4, "Strait" or "Add" or "Multiply" or "Invert" => 2, _ => 1 };
        if (parts.Length - 1 < need) throw new InvalidDataException($"Heightmap step '{line}' needs {need} values.");
        return new Step(tool, parts[1..]);
    }

    public static void Validate(IReadOnlyList<string>? steps)
    {
        if (steps == null) return;
        if (steps.Count > 64) throw new InvalidDataException("At most 64 heightmap steps.");
        foreach (var line in steps)
        {
            var s = Parse(line);
            try
            {
                switch (s.Tool)
                {
                    case "Hill" or "Pit" or "Range" or "Trough":
                        Check(Range(s.Args[0]), 0, 100); Check(Range(s.Args[1]), 0, 100); Check(Range(s.Args[2]), 0, 100); Check(Range(s.Args[3]), 0, 100);
                        if (s.Args.Length >= 6) { Check(Range(s.Args[4]), 0, 100); Check(Range(s.Args[5]), 0, 100); }
                        break;
                    case "Strait": Check(Range(s.Args[0]), 1, 20); if (s.Args[1] is not ("vertical" or "horizontal")) throw new FormatException(); break;
                    case "Mask": Check(Range(s.Args[0]), -20, 20); break;
                    case "Add": Check(Range(s.Args[0]), -100, 100); Interval(s.Args[1]); break;
                    case "Multiply": Check(Range(s.Args[0]), 0, 10); Interval(s.Args[1]); break;
                    case "Smooth": Check(Range(s.Args[0]), 1, 20); break;
                    case "Invert": Check(Range(s.Args[0]), 0, 1); if (s.Args[1] is not ("x" or "y" or "both")) throw new FormatException(); break;
                }
            }
            catch (FormatException) { throw new InvalidDataException($"Heightmap step '{line}' has an invalid value."); }
        }
        static void Check((double A, double B) r, double min, double max)
        {
            if (!double.IsFinite(r.A) || !double.IsFinite(r.B) || r.A < min || r.B > max || r.A > r.B) throw new FormatException();
        }
    }

    private static (double A, double B) Range(string s)
    {
        int dash = s.IndexOf('-', 1); // a leading '-' is a sign
        if (dash < 0) { double v = double.Parse(s, CultureInfo.InvariantCulture); return (v, v); }
        return (double.Parse(s[..dash], CultureInfo.InvariantCulture), double.Parse(s[(dash + 1)..], CultureInfo.InvariantCulture));
    }

    private static (double Min, double Max) Interval(string s) => s switch
    {
        "land" => (SeaLevel, 100),
        "all" => (0, 100),
        _ => Range(s),
    };

    /// <summary>A count; a fraction is a chance of one more (1.5 = one, and a second half the time).</summary>
    private int Count(string s) { double c = Pick(s); int whole = (int)Math.Floor(c); return whole + (_rng.Unit() < c - whole ? 1 : 0); }

    private double Pick(string s) { var (a, b) = Range(s); return a == b ? a : a + (b - a) * _rng.Unit(); }

    // ------------------------------------------------------------------ grid

    private static int Index(int i, int j) => j * N + i;

    /// <summary>Cell centre in world fractions (0..1); odd rows are offset by half a cell (hex grid).</summary>
    public static (double X, double Y) Centre(int i, int j) => ((i + .5 + (j & 1) * .5) / N, (j + .5) / N);

    private static int Neighbours(int c, Span<int> into)
    {
        int i = c % N, j = c / N, n = 0;
        void Add(int a, int b, Span<int> s) { if (a >= 0 && b >= 0 && a < N && b < N) s[n++] = Index(a, b); }
        int o = j & 1; // odd rows sit half a cell to the right
        Add(i - 1, j, into); Add(i + 1, j, into);
        Add(i - 1 + o, j - 1, into); Add(i + o, j - 1, into);
        Add(i - 1 + o, j + 1, into); Add(i + o, j + 1, into);
        return n;
    }

    private int CellAt(double x, double y)
    {
        int j = Math.Clamp((int)(y * N), 0, N - 1);
        int i = Math.Clamp((int)(x * N - (j & 1) * .5), 0, N - 1);
        return Index(i, j);
    }

    private int PickCell(string xs, string ys, Func<double, bool> ok)
    {
        int cell = 0;
        for (int attempt = 0; attempt < 50; attempt++)
        {
            cell = CellAt(Pick(xs) / 100, Pick(ys) / 100);
            if (ok(_h[cell])) break;
        }
        return cell;
    }

    // ------------------------------------------------------------------ steps

    private void Run(Step s)
    {
        var a = s.Args;
        switch (s.Tool)
        {
            case "Hill": { int count = Count(a[0]); for (int k = 0; k < count; k++) Blob(Pick(a[1]), a[2], a[3], +1); break; }
            case "Pit": { int count = Count(a[0]); for (int k = 0; k < count; k++) Blob(Pick(a[1]), a[2], a[3], -1); break; }
            case "Range": { int count = Count(a[0]); for (int k = 0; k < count; k++) Line(Pick(a[1]), a[2], a[3], +1, a.Length >= 6 ? a[4..6] : null); break; }
            case "Trough": { int count = Count(a[0]); for (int k = 0; k < count; k++) Line(Pick(a[1]), a[2], a[3], -1, a.Length >= 6 ? a[4..6] : null); break; }
            case "Strait": Strait(Count(a[0]), a[1] == "vertical"); break;
            case "Mask": Mask(Pick(a[0])); break;
            case "Add": { double v = Pick(a[0]); var (lo, hi) = Interval(a[1]); for (int c = 0; c < _h.Length; c++) if (_h[c] >= lo && _h[c] <= hi) _h[c] = Lim(_h[c] + v); break; }
            case "Multiply":
            {
                double v = Pick(a[0]); var (lo, hi) = Interval(a[1]);
                for (int c = 0; c < _h.Length; c++)
                    if (_h[c] >= lo && _h[c] <= hi) _h[c] = Lim(lo >= SeaLevel ? SeaLevel + (_h[c] - SeaLevel) * v : _h[c] * v);
                break;
            }
            case "Smooth": Smooth(Pick(a[0])); break;
            case "Invert": if (_rng.Unit() < Pick(a[0])) Invert(a[1]); break;
        }
    }

    private static double Lim(double h) => Math.Clamp(h, 0, 100);

    /// <summary>A hill (sign +1) or pit (−1): the change spreads breadth-first, each neighbour getting
    /// change^power × random 0.9..1.1 of its parent, until it drops below 1.</summary>
    private void Blob(double height, string xs, string ys, int sign)
    {
        int start = PickCell(xs, ys, h => sign > 0 ? h + height <= 90 : h >= SeaLevel);
        var change = new double[N * N];
        change[start] = height;
        var queue = new Queue<int>();
        queue.Enqueue(start);
        Span<int> nb = stackalloc int[6];
        while (queue.Count > 0)
        {
            int q = queue.Dequeue();
            int count = Neighbours(q, nb);
            for (int k = 0; k < count; k++)
            {
                int n = nb[k];
                if (change[n] > 0) continue;
                // (minus a small fade per ring: a finite reach, so the faint tails of many hills do not add up to
                // land everywhere)
                change[n] = Math.Max(1e-6, Math.Pow(change[q], BlobPower) * (.9 + .2 * _rng.Unit()) - BlobFade);
                if (change[n] > 1) queue.Enqueue(n);
            }
        }
        for (int c = 0; c < _h.Length; c++) _h[c] = Lim(_h[c] + sign * change[c]);
    }

    /// <summary>A wandering path from a start in x,y toward a random end 1/8–1/3 of the map away; the path gets the
    /// height, which spreads sideways ring by ring (h^power − 1, randomised); short spurs branch off downhill.</summary>
    private void Line(double height, string xs, string ys, int sign, string[]? to = null)
    {
        int start = PickCell(xs, ys, h => sign > 0 ? h < 80 : h > SeaLevel);
        var (sx, sy) = Centre(start % N, start / N);
        int end = start;
        if (to != null) end = CellAt(Pick(to[0]) / 100, Pick(to[1]) / 100);
        else for (int attempt = 0; attempt < 50; attempt++)
        {
            double ex = .05 + .9 * _rng.Unit(), ey = .05 + .9 * _rng.Unit();
            double d = Math.Sqrt((ex - sx) * (ex - sx) + (ey - sy) * (ey - sy));
            end = CellAt(ex, ey);
            if (d > 1.0 / 8 && d < 1.0 / 3) break;
        }
        var path = Path(start, end, .15);
        var used = new bool[N * N];
        var ring = new List<int>(path);
        foreach (int c in ring) used[c] = true;
        double h = height;
        Span<int> nb = stackalloc int[6];
        while (h > 2 && ring.Count > 0)
        {
            foreach (int c in ring) _h[c] = Lim(_h[c] + sign * h * (.85 + .3 * _rng.Unit()));
            h = Math.Pow(h, LinePower) - 1;
            var next = new List<int>();
            foreach (int c in ring)
            {
                int count = Neighbours(c, nb);
                for (int k = 0; k < count; k++) if (!used[nb[k]]) { used[nb[k]] = true; next.Add(nb[k]); }
            }
            ring = next;
        }
        // Spurs: every sixth path cell, a short ridge (or side valley) running downhill.
        for (int p = 0; p < path.Count; p += 6)
        {
            int c = path[p];
            double spur = height * .5;
            for (int step = 0; step < 6 && spur > 2; step++)
            {
                int count = Neighbours(c, nb), low = nb[0];
                for (int k = 1; k < count; k++) if (_h[nb[k]] < _h[low]) low = nb[k];
                c = low;
                _h[c] = Lim(_h[c] + sign * spur * .5);
                spur *= .75;
            }
        }
    }

    /// <summary>Greedy path toward the end cell; with probability <paramref name="wander"/> a random neighbour.</summary>
    private List<int> Path(int from, int to, double wander)
    {
        var path = new List<int> { from };
        var (tx, ty) = Centre(to % N, to / N);
        Span<int> nb = stackalloc int[6];
        int c = from;
        for (int guard = 0; c != to && guard < N * 4; guard++)
        {
            int count = Neighbours(c, nb), best = nb[0];
            double bestD = double.MaxValue;
            for (int k = 0; k < count; k++)
            {
                var (x, y) = Centre(nb[k] % N, nb[k] / N);
                double d = (x - tx) * (x - tx) + (y - ty) * (y - ty);
                if (_rng.Unit() < wander) d *= .5 + _rng.Unit(); // wander off the straight line
                if (d < bestD) { bestD = d; best = nb[k]; }
            }
            c = best;
            path.Add(c);
        }
        return path;
    }

    /// <summary>A sea channel across the map: a wandering path from one edge to the other, lowering cells around it
    /// (more toward the middle of the channel).</summary>
    private void Strait(int width, bool vertical)
    {
        double a = .15 + .7 * _rng.Unit(), b = .15 + .7 * _rng.Unit();
        int from = vertical ? CellAt(a, 0) : CellAt(0, a), to = vertical ? CellAt(b, .999) : CellAt(.999, b);
        var ring = Path(from, to, .25);
        var used = new bool[N * N];
        foreach (int c in ring) used[c] = true;
        Span<int> nb = stackalloc int[6];
        for (int r = 0; r < width && ring.Count > 0; r++)
        {
            double keep = .55 + .45 * r / width; // the middle drops most
            foreach (int c in ring) _h[c] = Math.Min(_h[c], SeaLevel + (_h[c] - SeaLevel) * keep - (width - r) * .8);
            var next = new List<int>();
            foreach (int c in ring)
            {
                int count = Neighbours(c, nb);
                for (int k = 0; k < count; k++) if (!used[nb[k]]) { used[nb[k]] = true; next.Add(nb[k]); }
            }
            ring = next;
        }
        for (int c = 0; c < _h.Length; c++) _h[c] = Lim(_h[c]);
    }

    /// <summary>Fade heights toward the edges (fraction > 0; larger = gentler) or toward the centre (fraction < 0).</summary>
    private void Mask(double fraction)
    {
        double fr = Math.Abs(fraction);
        if (fr < 1) fr = 1;
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
            {
                var (x, y) = Centre(i, j);
                double nx = 2 * x - 1, ny = 2 * y - 1, distance = (1 - nx * nx) * (1 - ny * ny);
                if (fraction < 0) distance = 1 - distance;
                int c = Index(i, j);
                _h[c] = Lim((_h[c] * (fr - 1) + _h[c] * distance) / fr);
            }
    }

    private void Smooth(double fraction)
    {
        double fr = Math.Max(1, fraction);
        var copy = (double[])_h.Clone();
        Span<int> nb = stackalloc int[6];
        for (int c = 0; c < _h.Length; c++)
        {
            int count = Neighbours(c, nb);
            double sum = copy[c];
            for (int k = 0; k < count; k++) sum += copy[nb[k]];
            double mean = sum / (count + 1);
            _h[c] = Lim((copy[c] * (fr - 1) + mean) / fr);
        }
    }

    private void Invert(string axes)
    {
        var copy = (double[])_h.Clone();
        bool fx = axes is "x" or "both", fy = axes is "y" or "both";
        for (int j = 0; j < N; j++)
            for (int i = 0; i < N; i++)
                _h[Index(i, j)] = copy[Index(fx ? N - 1 - i : i, fy ? N - 1 - j : j)];
    }

    // ------------------------------------------------------------------ sampling

    /// <summary>Height 0..100 at world fractions (x east, y south), bilinear between the hex rows.</summary>
    public double Sample(double x, double y)
    {
        double fy = y * N - .5;
        int j0 = (int)Math.Floor(fy);
        double ty = fy - j0;
        double Row(int j)
        {
            j = Math.Clamp(j, 0, N - 1);
            double fx = x * N - .5 - (j & 1) * .5;
            int i0 = (int)Math.Floor(fx);
            double tx = fx - i0;
            double a = _h[Index(Math.Clamp(i0, 0, N - 1), j)], b = _h[Index(Math.Clamp(i0 + 1, 0, N - 1), j)];
            return a + (b - a) * tx;
        }
        double r0 = Row(j0), r1 = Row(j0 + 1);
        return r0 + (r1 - r0) * ty;
    }

    /// <summary>Share of cells above sea level.</summary>
    public double LandShare()
    {
        int land = 0;
        foreach (var h in _h) if (h >= SeaLevel) land++;
        return land / (double)_h.Length;
    }
}
