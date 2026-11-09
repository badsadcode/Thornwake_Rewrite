using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// The world's waterfalls as the water draws them (D285, the water rework's second step): every stretch of river between two
/// of its points that drops at least 3 m, steeper than 1 in 5 — the waterfalls module's falls (D238) and the glacial troughs'
/// (hanging valleys, a trough's head) alike, found from the final rivers. The river's sheet leaves them out (it ran straight
/// from the lip to the pool, blended across the river: a rounded dome, D275) and <see cref="WaterfallView"/> draws the
/// falling water there.
/// </summary>
public sealed class FallSet
{
    /// <summary>One fall: the lip (where the water leaves its level) and the foot (the next point, at the pool's level), the
    /// water's level at each, the river's width at the lip and at the foot (a plunge pool is wider), the direction it falls
    /// (unit, xz).</summary>
    public readonly record struct Fall(int Reach, int Point, double LipX, double LipZ, double FootX, double FootZ, double Top, double Bottom,
        double Width, double FootWidth, double Dx, double Dz, double Length)
    {
        public double Height => Top - Bottom;
        /// <summary>The river's width <paramref name="along"/> metres down from the lip.</summary>
        public double WidthAt(double along) => Width + (FootWidth - Width) * Math.Clamp(along / Length, 0, 1);
    }

    private const double MinDrop = 3, MinGrade = .2, GridCell = 2000;
    private static readonly ConditionalWeakTable<WorldPlan, FallSet> Cache = new();
    private readonly List<Fall> _falls = new();
    private readonly Dictionary<(int, int), List<int>> _grid = new();

    public IReadOnlyList<Fall> All => _falls;

    /// <summary>The falls of a world (found once per world plan, thread-safe).</summary>
    public static FallSet For(WorldPlan plan) => Cache.GetValue(plan, p => new FallSet(p.Hydrology));

    private FallSet(Hydrology hy)
    {
        foreach (var r in hy.Reaches)
        {
            if (r.Distributary || r.Points.Length < 2 || r.Along.Length != r.Points.Length) continue;
            for (int k = 0; k + 1 < r.Points.Length; k++)
            {
                double drop = r.Surface[k] - r.Surface[k + 1], length = r.Along[k + 1] - r.Along[k];
                if (drop < MinDrop || length <= 0 || drop / length < MinGrade) continue;
                var a = r.Points[k]; var b = r.Points[k + 1];
                double dx = (b.X - a.X) / length, dz = (b.Z - a.Z) / length;
                _falls.Add(new Fall(r.Id, k, a.X, a.Z, b.X, b.Z, r.Surface[k], r.Surface[k + 1], r.Width[k], r.Width[k + 1], dx, dz, length));
            }
        }
        for (int i = 0; i < _falls.Count; i++)
        {
            var f = _falls[i];
            double reach = f.Length * 2.5 + Math.Max(f.Width, f.FootWidth) + 40;
            for (int gx = Cell(Math.Min(f.LipX, f.FootX) - reach); gx <= Cell(Math.Max(f.LipX, f.FootX) + reach); gx++)
                for (int gz = Cell(Math.Min(f.LipZ, f.FootZ) - reach); gz <= Cell(Math.Max(f.LipZ, f.FootZ) + reach); gz++)
                {
                    if (!_grid.TryGetValue((gx, gz), out var list)) _grid[(gx, gz)] = list = new List<int>();
                    list.Add(i);
                }
        }
    }

    private static int Cell(double v) => (int)Math.Floor(v / GridCell);

    /// <summary>How far past a fall's river sides its face is cut from the river's sheet and the falling water reaches (D292:
    /// 3 m, a quarter of the width on wide rivers — the sheet on a wide fall's slope spreads further past its banks).</summary>
    public static double Margin(Fall f) => Math.Max(3, f.Width * .25);

    /// <summary>Whether a square of the water's sheet is part of a fall's face: near a fall (from a little above its lip to a
    /// little past its foot, up to 3 m beyond the river's sides) with water between the pool's level and the lip's
    /// (<paramref name="low"/>, <paramref name="high"/>: its corners' lowest and highest). By the water's level, not by the
    /// river's width: the sheet runs out past the river under its banks, and its ramp showed beside the falling water.</summary>
    public bool OnFace(double x, double z, double low, double high)
    {
        if (!_grid.TryGetValue((Cell(x), Cell(z)), out var list)) return false;
        foreach (int i in list)
        {
            var f = _falls[i];
            double px = x - f.LipX, pz = z - f.LipZ, along = px * f.Dx + pz * f.Dz, across = Math.Abs(px * f.Dz - pz * f.Dx);
            // (D290: 3 m past the river's sides and from 4 m above the lip: 20 m and 15 m cut a V-shaped notch in the water above)
            // (from 1.5 m above the lip: the curtain overlaps it from 3 m above, fading in — D291, a gap showed as a line)
            // (D296: well past the river's sides, by the level test below only the sheet's ramp: running on under the banks it
            // hung over the V of a wide fall's gorge as a dark ribbed panel beside the falling water — Tom's dump 001431)
            if (along < -1.5 || along > f.Length * 2.5 + 20 || across > f.WidthAt(Math.Min(along, f.Length)) * .5 + Math.Max(Margin(f) * 2.5, 15)) continue;
            // (any corner between the two levels: the dome over the lip sinks a little below the lip's level already)
            if (low < f.Top - .25 && high > f.Bottom + .8) return true; // (D299: the sheet kept up to 0.8 m over the pool: it overlaps the curtain's foot)
        }
        return false;
    }

    /// <summary>The falls within <paramref name="range"/> metres of a point.</summary>
    public IEnumerable<Fall> Near(double x, double z, double range) =>
        _falls.Where(f => Math.Abs(f.LipX - x) < range && Math.Abs(f.LipZ - z) < range);
}

/// <summary>
/// Falling water at the world's waterfalls (D285): a curtain from just above the lip down to the pool — falling freely from
/// the lip, sliding down the rock wherever the rock lies higher — streaked and churning (WaterFall.gdshader), white water
/// spreading on the pool at its foot, and mist rising there. Built on worker threads for the falls within a few kilometres,
/// moved with the scene's origin. Off with *Waterfalls → river sheet (build 137)*.
/// </summary>
public sealed class WaterfallView
{
    private const double DrawRange = 4000, MistRange = 1500, Upstream = 3;
    private readonly Node3D _parent;
    private readonly Dictionary<(int, int), (Node3D Node, FallSet.Fall Fall, GpuParticles3D? Mist)> _shown = new();
    private readonly Dictionary<(int, int), (Task<Model> Job, FallSet.Fall Fall)> _building = new();
    private TerrainSampler? _for;
    private FallSet? _falls;
    private Task<FallSet>? _finding;
    private double _originX = double.NaN, _originZ, _lastX = double.NaN, _lastZ;
    private static ShaderMaterial? _curtain, _foam;
    private static Material? _mistMaterial;
    private int _spray = GameSettings.Spray.Index;

    public WaterfallView(Node3D parent) { _parent = parent; }

    /// <summary>Falls shown now (diagnostics).</summary>
    public int Shown => _shown.Count;

    public void Update(TerrainSampler terrain, double camX, double camZ, double originX, double originZ)
    {
        bool on = GameSettings.Waterfalls.Index == 1;
        if (_spray != GameSettings.Spray.Index) { _spray = GameSettings.Spray.Index; Clear(); _building.Clear(); _lastX = double.NaN; }
        if (_for != terrain || !on)
        {
            Clear(); _building.Clear(); _lastX = double.NaN;
            if (!on) { _for = null; return; }
            _for = terrain; _falls = null;
            _finding = Task.Run(() => FallSet.For(terrain.Plan));
        }
        if (_finding is { IsCompleted: true } done) { _falls = done.Result; _finding = null; _lastX = double.NaN; }
        if (_falls == null) return;
        bool moved = double.IsNaN(_lastX) || Math.Abs(camX - _lastX) + Math.Abs(camZ - _lastZ) > 150;
        bool rebased = originX != _originX || originZ != _originZ;
        if (moved)
        {
            _lastX = camX; _lastZ = camZ;
            var near = _falls.Near(camX, camZ, DrawRange).ToDictionary(f => (f.Reach, f.Point));
            foreach (var key in _shown.Keys.Where(k => !near.ContainsKey(k)).ToList()) Remove(key);
            int started = 0;
            foreach (var (key, fall) in near)
                if (!_shown.ContainsKey(key) && !_building.ContainsKey(key) && started++ < 4)
                    _building[key] = (Task.Run(() => Build(terrain, fall)), fall);
            if (started > 4) _lastX = double.NaN;
        }
        foreach (var (key, b) in _building)
            if (b.Job.IsCompleted)
            {
                _building.Remove(key);
                if (b.Job.IsFaulted) GD.PrintErr($"Waterfall at {b.Fall.LipX:0},{b.Fall.LipZ:0}: {b.Job.Exception?.InnerException?.Message}");
                if (!b.Job.IsFaulted && Math.Abs(b.Fall.LipX - camX) <= DrawRange * 1.1 && Math.Abs(b.Fall.LipZ - camZ) <= DrawRange * 1.1)
                    Add(key, b.Fall, b.Job.Result, originX, originZ);
                break;
            }
        foreach (var (key, s) in _shown)
        {
            if (rebased) s.Node.Position = new Vector3((float)(s.Fall.LipX - originX), 0, (float)(s.Fall.LipZ - originZ));
            if (s.Mist != null) s.Mist.Emitting = Math.Abs(s.Fall.LipX - camX) + Math.Abs(s.Fall.LipZ - camZ) < MistRange;
        }
        _originX = originX; _originZ = originZ;
    }

    private void Clear() { foreach (var key in _shown.Keys.ToList()) Remove(key); }

    private void Remove((int, int) key)
    {
        var s = _shown[key];
        s.Node.QueueFree();
        _shown.Remove(key);
    }

    // ---- the meshes

    /// <summary>A fall's curtain and foam disc, in metres from the lip across (y absolute, as the terrain's).</summary>
    private sealed record Model(Vector3[] Vertices, Vector3[] Normals, Vector2[] Uv, Vector2[] Uv2, Color[] Colors, int[] Indices,
        Vector3[] FoamVertices, Vector2[] FoamUv, Color[] FoamColors, int[] FoamIndices, Vector3 Foot, float Spread, Vector3 Face, float Width);

    private static Model Build(TerrainSampler terrain, FallSet.Fall f)
    {
        // As wide as the river at the lip, a little wider toward the foot (falling water spreads), thinning at its sides (D290:
        // the pool's width at the foot and 4 m more a side made a 5 m stream a 20 m sheet wrapped round the cliff — Tom's
        // dumps 214522/214703).
        // D291: 3.5 m more a side, fading and fraying there — the river's sheet is cut 3 m past the river's sides and the curtain
        // stopped short of that: a clear cut-off between them (Tom: "the sides are too regular. There's a clear cutoff").
        // D298 (Tom, build 139: "The waterfall is wider than the river"): the river's width (a tenth wider toward the foot as the
        // water spreads), fading out over its own edges — the margin past the river's sides (D291/D292, up to a quarter of the
        // width a side: 19 m on the wide fall) hid where the river's sheet was cut, which now fades out by itself (D296) and
        // meets the banks at the lip (D297).
        double margin = Math.Clamp(f.Width * .12, 1, 4);
        double Wide(double h) => Math.Max(2, f.Width * (1 + .1 * Math.Clamp(h / Math.Max(1, f.Length), 0, 1)) + 1);
        double width = Wide(0);
        double ax = f.Dz, az = -f.Dx; // across the fall (to its right)
        double lipLevel = terrain.Sample(f.LipX, f.LipZ).WaterLevel;
        if (double.IsNaN(lipLevel)) lipLevel = f.Top;
        double v0 = Math.Clamp(1.5 + .04 * f.Width, 1.5, 4), bottom = f.Bottom;
        // The centre line's path, at steps of about a metre along it: draped on the river above the lip, then falling freely
        // or sliding on the rock, down to the pool.
        // (the free fall alone reaches the pool's level within a few metres; on a long ramp the water lands on the rock and
        // slides on down it: the path follows whichever is higher, or the curtain ended a third of the way down)
        double Centre(double hh) => hh <= 0 ? lipLevel
            : Math.Max(lipLevel - 4.9 * (hh / v0) * (hh / v0), terrain.Height(f.LipX + f.Dx * hh, f.LipZ + f.Dz * hh) + .3);
        // Whether the falling water's sides (a quarter and the edge of the river's width either side) have reached the pool's level.
        bool SidesDown(double hh)
        {
            foreach (double u in new[] { -.5, -.3, .3, .5 })
            {
                double off = u * f.WidthAt(hh), x = f.LipX + f.Dx * hh + ax * off, z = f.LipZ + f.Dz * hh + az * off;
                if (terrain.Height(x, z) + .3 > bottom + .2) return false;
            }
            return true;
        }
        bool faces = terrain.Plan.FallLips is { Faces: true };
        var steps = new List<double> { -Upstream };
        {
            double h = -Upstream, y = lipLevel;
            while (h < f.Length * 2.5 + 20 && steps.Count < 900)
            {
                // About a metre of path a step: shorter where it drops steeply.
                double step = 1;
                for (int tries = 0; tries < 6 && Math.Abs(Centre(h + step) - y) > 1; tries++) step *= .5;
                h += step; steps.Add(h);
                y = Centre(h);
                // (D299: on until every column has reached the pool — the sides of a V-shaped gorge reach it further down than the
                // middle; the rows were cut where the middle did)
                // (D300: with a plunge pool under the face — revision 43 — it ends where the falling water meets the pool)
                if (faces ? h > 0 && y <= bottom - .1 : h > f.Length + 4 && y <= bottom - .1 && SidesDown(h)) break;
            }
        }
        int cols = Math.Clamp((int)Math.Ceiling(Math.Max(width, Wide(f.Length)) / 1.2), 2, 60) + 1, rows = steps.Count;
        var vertices = new Vector3[cols * rows];
        var uv = new Vector2[cols * rows];
        var uv2 = new Vector2[cols * rows];
        var colors = new Color[cols * rows];
        for (int c = 0; c < cols; c++)
        {
            double u = (double)c / (cols - 1);
            double pathLength = 0, travel = 0, lastSpeed = v0; Vector3 last = default;
            for (int r = 0; r < rows; r++)
            {
                double h = steps[r], off = (u - .5) * Wide(h);
                double x = f.LipX + f.Dx * h + ax * off, z = f.LipZ + f.Dz * h + az * off;
                double y, onRock = 0, wall = 0;
                if (h < 0)
                {
                    double level = terrain.Sample(x, z).WaterLevel;
                    y = (double.IsNaN(level) ? lipLevel : level) + .03;
                }
                else
                {
                    // Each column falls freely or slides on the rock under it (D292, Tom: the wide fall "goes through the
                    // terrain": with one height per row, D290, a 74 m fall over a curved slope cut into it and hovered over it),
                    // but no higher than the centre's path by more than a metre: where the rock beside rises into a wall the
                    // column fades out instead of climbing it (each column following its rock up curled the sheet round the
                    // gorge's walls, D290).
                    double free = lipLevel - 4.9 * (h / v0) * (h / v0), centre = Centre(h), ground = terrain.Height(x, z) + .3;
                    double own = Math.Max(free, ground);
                    // (only outside the river's own width: inside it bumps of the rock stood a metre over the centre's path and
                    // the fade cut holes in the falling water)
                    // (D296: each column on its own path; where the rock rises well over the centre's path it fades out — inside
                    // the river only past 2.5 m, bumps of the bed stay — instead of being held a metre over the centre's path,
                    // which hung those columns straight down from the lip as a flat, stepped wall: Tom's dump 001431)
                    double outside = DetMath.Smooth((Math.Abs(off) - f.WidthAt(h) * .5 + 1) / 2);
                    // (D299: only outside the river's width — within it the lip's water pours down whatever slope is there; fading it
                    // where it slid down a gorge's V sides left them dry under a full-width river, Tom's red circles)
                    wall = DetMath.Smooth((own - centre - 1) / 2.5) * outside;
                    y = Math.Max(own, bottom - .25);
                    onRock = DetMath.Smooth((ground - free) / .5);
                }
                var p = new Vector3((float)(x - f.LipX), (float)y, (float)(z - f.LipZ));
                double drop = Math.Max(0, lipLevel - y), speed = Math.Sqrt(v0 * v0 + 2 * 9.8 * drop);
                // Travel time from the top (D291): the streaks are drawn at (travel time − clock), so they move at the water's own
                // speed everywhere. At (metres − speed × clock) the pattern sheared as the water sped up and, after a while of
                // play, crawled up the fall (Tom: "one part of the water flow flows up").
                if (r > 0) { double seg = (p - last).Length(); pathLength += seg; travel += seg / Math.Max(.5, .5 * (speed + lastSpeed)); }
                last = p; lastSpeed = speed;
                int k = r * cols + c;
                vertices[k] = p;
                uv[k] = new Vector2((float)(u * width), (float)pathLength);
                uv2[k] = new Vector2((float)travel, 0);
                double foot = DetMath.Smooth((y - bottom) / -Math.Max(1, (lipLevel - bottom) * .15) + 1); // (1 near the pool)
                // (fading out as it reaches the pool: drawn after the water, its rows lying under the pool showed as a glassy sheet)
                double fromSide = Math.Min(u, 1 - u) * Wide(h);
                double edge = DetMath.Smooth(fromSide / margin) * DetMath.Smooth((h + Upstream) / (Upstream - 1))
                    * (h > 0 ? DetMath.Smooth((y - bottom + .2) / .3) : 1) * (1 - wall);
                colors[k] = new Color((float)Math.Min(1, speed / 25), (float)foot, (float)onRock, (float)edge);
            }
        }
        // Columns that end on the dry bank above the pool (the sides of a wide fall over a V-shaped gorge: every column has the
        // centre's rows, so a side column stopped part-way down the bank) fade out over the lower half of their way down instead
        // of ending in a hard line along the bank (D292, Tom's marked screenshot).
        // (D296: each column's fade smoothed with its neighbours' — column by column it drew a staircase along the curtain's
        // lower edge, seen from above the lip)
        var colStrength = new double[cols];
        for (int c = 0; c < cols; c++) colStrength[c] = DetMath.Smooth((vertices[(rows - 1) * cols + c].Y - bottom - .4) / 2.5);
        for (int pass = 0; pass < 3; pass++)
        {
            var next = (double[])colStrength.Clone();
            for (int c = 0; c < cols; c++) next[c] = (colStrength[Math.Max(0, c - 1)] + 2 * colStrength[c] + colStrength[Math.Min(cols - 1, c + 1)]) / 4;
            colStrength = next;
        }
        for (int c = 0; c < cols; c++)
        {
            double strength = colStrength[c];
            if (strength < .01) continue;
            for (int r = 0; r < rows; r++)
            {
                double t = (double)r / Math.Max(1, rows - 1);
                int k = r * cols + c;
                colors[k].A *= (float)(1 - strength * DetMath.Smooth((t - .35) / .6));
            }
        }
        var normals = new Vector3[vertices.Length];
        var indices = new List<int>((cols - 1) * (rows - 1) * 6);
        for (int r = 0; r + 1 < rows; r++)
            for (int c = 0; c + 1 < cols; c++)
            {
                int a = r * cols + c, b = a + 1, d = a + cols, e = d + 1;
                indices.Add(a); indices.Add(d); indices.Add(b);
                indices.Add(b); indices.Add(d); indices.Add(e);
            }
        for (int t = 0; t < indices.Count; t += 3)
        {
            var n = (vertices[indices[t + 1]] - vertices[indices[t]]).Cross(vertices[indices[t + 2]] - vertices[indices[t]]);
            normals[indices[t]] += n; normals[indices[t + 1]] += n; normals[indices[t + 2]] += n;
        }
        for (int k = 0; k < normals.Length; k++) normals[k] = normals[k].LengthSquared() > 1e-12f ? normals[k].Normalized() : Vector3.Up;
        // Where the centre line meets the pool: the foam's middle.
        var footPoint = vertices[(rows - 1) * cols + cols / 2] with { Y = (float)(bottom + .04) };
        for (int r = 0; r < rows; r++)
            if (vertices[r * cols + cols / 2].Y <= bottom + .05) { footPoint = vertices[r * cols + cols / 2] with { Y = (float)(bottom + .04) }; break; }
        float spread = (float)Math.Clamp(width * .45 + (lipLevel - bottom) * .15, 4, 35);
        // Half-way down the falling water, its centre (the face's spray, D292).
        var face = footPoint;
        for (int r = 0; r < rows; r++)
            if (vertices[r * cols + cols / 2].Y <= (lipLevel + bottom) * .5) { face = vertices[r * cols + cols / 2]; break; }
        // The foam disc: rings round the foot, a little longer downstream.
        const int rings = 8, spokes = 28;
        var fv = new List<Vector3> { footPoint }; var fuv = new List<Vector2> { Vector2.Zero }; var fc = new List<Color> { new(1, 1, 1, 1) };
        for (int ring = 1; ring <= rings; ring++)
            for (int s = 0; s < spokes; s++)
            {
                double ang = s * Math.Tau / spokes, rr = spread * ring / rings;
                double down = Math.Cos(ang), stretch = 1 + .6 * Math.Max(0, down);
                double ox = (f.Dx * Math.Cos(ang) + ax * Math.Sin(ang)) * rr * stretch, oz = (f.Dz * Math.Cos(ang) + az * Math.Sin(ang)) * rr * stretch;
                fv.Add(footPoint + new Vector3((float)ox, 0, (float)oz));
                fuv.Add(new Vector2((float)(Math.Cos(ang) * rr * stretch), (float)(Math.Sin(ang) * rr * stretch))); // (downstream, across)
                fc.Add(new Color(1, 1, 1, (float)(1 - (double)ring / rings)));
            }
        var fi = new List<int>();
        for (int s = 0; s < spokes; s++) { fi.Add(0); fi.Add(1 + s); fi.Add(1 + (s + 1) % spokes); }
        for (int ring = 1; ring < rings; ring++)
            for (int s = 0; s < spokes; s++)
            {
                int a = 1 + (ring - 1) * spokes + s, b = 1 + (ring - 1) * spokes + (s + 1) % spokes, c = a + spokes, d = b + spokes;
                fi.Add(a); fi.Add(c); fi.Add(b); fi.Add(b); fi.Add(c); fi.Add(d);
            }
        return new Model(vertices, normals, uv, uv2, colors, indices.ToArray(), fv.ToArray(), fuv.ToArray(), fc.ToArray(), fi.ToArray(), footPoint, spread, face, (float)f.Width);
    }

    private void Add((int, int) key, FallSet.Fall fall, Model m, double originX, double originZ)
    {
        // (drawn after the water: it is see-through too, and drawn before it the foam vanished under the pool)
        _curtain ??= new ShaderMaterial { Shader = GD.Load<Shader>("res://Game/Rewrite/WaterFall.gdshader"), RenderPriority = 2 };
        if (_foam == null) { _foam = new ShaderMaterial { Shader = _curtain.Shader, RenderPriority = 1 }; _foam.SetShaderParameter("foam_disc", true); }
        var node = new Node3D { Name = "Waterfall", Position = new Vector3((float)(fall.LipX - originX), 0, (float)(fall.LipZ - originZ)) };
        var curtain = new ArrayMesh();
        using (var a = new Godot.Collections.Array())
        {
            a.Resize((int)Mesh.ArrayType.Max);
            a[(int)Mesh.ArrayType.Vertex] = m.Vertices; a[(int)Mesh.ArrayType.Normal] = m.Normals;
            a[(int)Mesh.ArrayType.TexUV] = m.Uv; a[(int)Mesh.ArrayType.TexUV2] = m.Uv2; a[(int)Mesh.ArrayType.Color] = m.Colors; a[(int)Mesh.ArrayType.Index] = m.Indices;
            curtain.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, a);
        }
        node.AddChild(new MeshInstance3D { Mesh = curtain, MaterialOverride = _curtain, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        var foam = new ArrayMesh();
        using (var a = new Godot.Collections.Array())
        {
            a.Resize((int)Mesh.ArrayType.Max);
            var up = new Vector3[m.FoamVertices.Length]; Array.Fill(up, Vector3.Up);
            a[(int)Mesh.ArrayType.Vertex] = m.FoamVertices; a[(int)Mesh.ArrayType.Normal] = up;
            a[(int)Mesh.ArrayType.TexUV] = m.FoamUv; a[(int)Mesh.ArrayType.Color] = m.FoamColors; a[(int)Mesh.ArrayType.Index] = m.FoamIndices;
            foam.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, a);
        }
        node.AddChild(new MeshInstance3D { Mesh = foam, MaterialOverride = _foam, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off });
        GpuParticles3D mist;
        if (Spray.On)
        {
            // Billowing spray (D292): rolling up at the foot and drifting downstream; finer spray off the face of a high fall.
            float h = (float)fall.Height, w = m.Width;
            var frame = new Basis(new Vector3((float)fall.Dz, 0, (float)-fall.Dx), Vector3.Up, new Vector3((float)fall.Dx, 0, (float)fall.Dz));
            mist = Spray.Emitter(new Vector3(Math.Max(1.5f, w * .45f), .5f, 1.5f + h * .02f), new Vector3(0, 1, .7f), 40,
                1.2f + h * .03f, 2.5f + h * .07f, 2.2f + w * .04f + h * .05f, 3.5f + w * .08f + h * .12f, 2.4f, Math.Clamp(3.5f + h * .03f, 3.5f, 7),
                Math.Clamp((int)(10 + w * 1.6f + h * 1.2f), 12, 180), .25f, .5f, Spray.Tint, "soft_cloud");
            mist.Basis = frame;
            mist.Position = m.Foot + Vector3.Up * .8f;
            node.AddChild(mist);
            if (h > 8)
            {
                var off = Spray.Emitter(new Vector3(Math.Max(1, w * .45f), h * .28f, .6f), new Vector3(0, -.2f, 1), 25, .6f, 1.8f,
                    .9f + w * .02f, 1.8f + w * .04f + h * .02f, 2, 2.6f, Math.Clamp((int)(h * .8f + w * .6f), 8, 90), -.4f, .35f, Spray.Tint, "fine_droplets");
                off.Basis = frame;
                off.Position = m.Face;
                node.AddChild(off);
                // Mist rising off the pool above the foot's clouds (Tom's rising plume).
                var plume = Spray.Emitter(new Vector3(Math.Max(1.5f, w * .4f), .5f, 1.5f), new Vector3(0, 1, .3f), 20, .8f, 1.6f + h * .03f,
                    3 + w * .05f + h * .06f, 5 + w * .08f + h * .1f, 1.8f, Math.Clamp(5 + h * .04f, 5, 9), Math.Clamp((int)(4 + h * .3f + w * .2f), 4, 40),
                    .35f, .3f, Spray.Tint, "rising_plume");
                plume.Basis = frame;
                plume.Position = m.Foot + Vector3.Up * 2 + frame.Z * 3;
                if (GameSettings.Spray.Index == 2) node.AddChild(plume); else plume.QueueFree();
            }
        }
        else
        {
            mist = Mist(m.Foot, m.Spread, fall);
            node.AddChild(mist);
        }
        _parent.AddChild(node);
        _shown[key] = (node, fall, mist);
    }

    /// <summary>Mist rising at the foot: soft white puffs drifting up and away, more for a higher fall.</summary>
    private static GpuParticles3D Mist(Vector3 foot, float spread, FallSet.Fall fall)
    {
        _mistMaterial ??= MistMaterial();
        float height = (float)fall.Height;
        var process = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Sphere,
            EmissionSphereRadius = spread * .45f,
            Direction = new Vector3((float)fall.Dx * .3f, 1, (float)fall.Dz * .3f),
            Spread = 35,
            InitialVelocityMin = .6f, InitialVelocityMax = 1.2f + height * .03f,
            Gravity = new Vector3(0, .15f, 0),
            DampingMin = .3f, DampingMax = .6f,
            ScaleMin = 2.5f + spread * .08f, ScaleMax = 4.5f + spread * .2f,
        };
        var fade = new Gradient();
        fade.SetColor(0, new Color(1, 1, 1, 0)); fade.SetColor(1, new Color(1, 1, 1, 0));
        fade.AddPoint(.2f, new Color(1, 1, 1, .5f)); fade.AddPoint(.6f, new Color(1, 1, 1, .3f));
        process.ColorRamp = new GradientTexture1D { Gradient = fade };
        var grow = new Curve(); grow.AddPoint(new Vector2(0, .5f)); grow.AddPoint(new Vector2(1, 1.6f));
        process.ScaleCurve = new CurveTexture { Curve = grow };
        return new GpuParticles3D
        {
            Name = "Mist", Position = foot + Vector3.Up * 1.5f,
            Amount = Math.Clamp((int)(height * 1.5f + spread), 16, 140), Lifetime = 6, Preprocess = 6,
            ProcessMaterial = process,
            DrawPass1 = new QuadMesh { Size = Vector2.One, Material = _mistMaterial },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            VisibilityAabb = new Aabb(new Vector3(-spread * 2, -5, -spread * 2), new Vector3(spread * 4, 30 + height * .5f, spread * 4)),
        };
    }

    private static Material MistMaterial()
    {
        var puff = new Gradient();
        puff.SetColor(0, new Color(1, 1, 1, 1)); puff.SetColor(1, new Color(1, 1, 1, 0));
        var tex = new GradientTexture2D { Gradient = puff, Fill = GradientTexture2D.FillEnum.Radial, FillFrom = new Vector2(.5f, .5f), FillTo = new Vector2(1, .5f), Width = 64, Height = 64 };
        return new StandardMaterial3D
        {
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, ShadingMode = BaseMaterial3D.ShadingModeEnum.PerVertex,
            BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles, VertexColorUseAsAlbedo = true,
            AlbedoTexture = tex, AlbedoColor = new Color(.92f, .95f, 1f), ProximityFadeEnabled = true, ProximityFadeDistance = 2,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
    }
}
