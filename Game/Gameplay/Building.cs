using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.Gameplay;

/// <summary>A point or direction in world or piece coordinates (metres, Y up; doubles: worlds are hundreds of km wide).</summary>
public readonly record struct V3(double X, double Y, double Z)
{
    public static V3 operator +(V3 a, V3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static V3 operator -(V3 a, V3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
    public double DistanceTo(V3 o) => (this - o).Length;
}

/// <summary>Where a piece stands: its origin and its turn about the vertical (radians, 0 = local +Z toward world +Z).</summary>
public readonly record struct Pose(double X, double Y, double Z, double Yaw)
{
    public V3 Origin => new(X, Y, Z);
}

/// <summary>
/// A building piece (Content/gameplay_pieces.json; D121): its category and shape (B → a category, V cycles its shapes,
/// X its materials — Soulmask style), cost, model, the box it fills (for overlaps), the snap points it joins others by,
/// and whether it may stand on the bare ground. <c>Object</c> names the behaviour of the finished piece (a campfire burns,
/// a lean-to shelters); otherwise the piece is its own kind.
/// </summary>
public sealed record PieceDef(string Id, string Name, string Category, string Shape, string Material, string Model,
    IReadOnlyList<Ingredient> Cost, V3 BoxCenter, V3 BoxHalf, IReadOnlyList<V3> Snap, bool Ground, double Lift, double Legs,
    IReadOnlySet<string> Tags, string Group, IReadOnlySet<string>? Attach = null)
{
    public bool Has(string tag) => Tags.Contains(tag);
    /// <summary>Lines (local) walls and stairs attach to: a floor's sides, diagonals and middle lines, a wall's top (D136).</summary>
    public IReadOnlyList<(V3 A, V3 B)> Edges { get; init; } = Array.Empty<(V3, V3)>();
    /// <summary>The pieces of one shape in their sizes (a wall: 1.5, 3 and 4.24 m); an edge picks the size that fits.</summary>
    public string Family { get; init; } = "";
    public double Length { get; init; }
    /// <summary>How it attaches to an edge: "wall" (centred on it, along it), "stairs" (its top on it, running away), or "".</summary>
    public string EdgeKind { get; init; } = "";
    /// <summary>The family to use on an edge too short for this one (a wall on a 1.5 m side: the vertical half wall).</summary>
    public string Smaller { get; init; } = "";
    /// <summary>The points it joins others by when placed (floors: their outer edges, so a floor on a wall lies with its
    /// edge on it, not its middle — Tom, build 71); null: all its snap points.</summary>
    public IReadOnlyList<V3>? Joins { get; init; }
    public IReadOnlyList<V3> JoinPoints => Joins ?? Snap;
    /// <summary>Joins pieces of that group by their points (null: any group).</summary>
    public bool JoinsTo(PieceDef other) => Attach == null || Attach.Contains(other.Group);
}

/// <summary>
/// Building rules and geometry, engine-free (the host checks with the same code the player's ghost uses): turning piece
/// points into the world, snapping a new piece onto pieces nearby, overlaps within a group (two walls in one place) and
/// support (on the ground, or joined to another piece).
/// </summary>
public static class Building
{
    /// <summary>Points closer than this are the same point (a joint).</summary>
    public const double Joint = .06;
    /// <summary>Snap to a point of a piece within this distance of where you aim.</summary>
    public const double SnapRadius = 2.2;
    /// <summary>(debugging: the best candidates of each snap)</summary>
    public static Action<string>? Debug;

    public static V3 ToWorld(Pose p, V3 local)
    {
        double s = Math.Sin(p.Yaw), c = Math.Cos(p.Yaw);
        return new V3(p.X + local.X * c + local.Z * s, p.Y + local.Y, p.Z - local.X * s + local.Z * c);
    }

    public static IEnumerable<V3> SnapPoints(PieceDef def, Pose p) => def.Snap.Select(s => ToWorld(p, s));

    /// <summary>The piece's box (shrunk by <paramref name="margin"/>) overlaps the other's: rotated rectangles in plan
    /// (separating axes) and their heights. Pieces that only touch do not overlap.</summary>
    public static bool Overlaps(PieceDef a, Pose pa, PieceDef b, Pose pb, double margin = .08)
    {
        var ca = ToWorld(pa, a.BoxCenter); var cb = ToWorld(pb, b.BoxCenter);
        double ha = a.BoxHalf.Y - margin, hb = b.BoxHalf.Y - margin;
        if (Math.Abs(ca.Y - cb.Y) >= ha + hb) return false;
        double[] axes = { pa.Yaw, pa.Yaw + Math.PI / 2, pb.Yaw, pb.Yaw + Math.PI / 2 };
        foreach (double ax in axes)
        {
            double ux = Math.Cos(ax), uz = -Math.Sin(ax); // (the local X axis turned by the yaw, as ToWorld)
            double ra = Extent(a, pa.Yaw, ux, uz, margin), rb = Extent(b, pb.Yaw, ux, uz, margin);
            double d = Math.Abs((cb.X - ca.X) * ux + (cb.Z - ca.Z) * uz);
            if (d >= ra + rb) return false;
        }
        return true;
    }

    // Half the box's width along the direction (ux, uz).
    private static double Extent(PieceDef d, double yaw, double ux, double uz, double margin)
    {
        double s = Math.Sin(yaw), c = Math.Cos(yaw);
        // (shrunk by the margin, but never past half a thin side, and the long sides by a little more: walls meeting in an L
        // sit exactly side to end — Tom, build 68 — and must not count as overlapping by a rounding)
        double Shrink(double h) => h - Math.Min(h > .5 ? margin * 1.5 : margin, h * .5);
        double hx = Shrink(d.BoxHalf.X), hz = Shrink(d.BoxHalf.Z);
        return hx * Math.Abs(c * ux - s * uz) + hz * Math.Abs(s * ux + c * uz);
    }

    /// <summary>A piece already in the world, as the rules see it.</summary>
    public readonly record struct Placed(long Id, PieceDef Def, Pose Pose);

    /// <summary>Overlaps a piece of its own group (ignoring <paramref name="ignore"/>: the one it replaces).</summary>
    public static bool Blocked(PieceDef def, Pose pose, IEnumerable<Placed> nearby, long ignore = 0) => BlockedBy(def, pose, nearby, ignore) != null;

    /// <summary>The piece of its group it would overlap (null: none) — shown to the player, so nothing blocks unseen.</summary>
    public static Placed? BlockedBy(PieceDef def, Pose pose, IEnumerable<Placed> nearby, long ignore = 0)
    {
        foreach (var p in nearby)
            if (p.Id != ignore && p.Def.Group == def.Group && def.Group.Length > 0 && Overlaps(def, pose, p.Def, p.Pose)) return p;
        return null;
    }

    /// <summary>How many of the piece's points meet a point of the pieces nearby.</summary>
    public static int Joints(PieceDef def, Pose pose, IEnumerable<Placed> nearby, long ignore = 0)
    {
        var mine = def.JoinPoints.Select(s => ToWorld(pose, s)).ToList();
        var others = nearby.Where(p => p.Id != ignore && def.JoinsTo(p.Def)).ToList();
        int n = 0;
        foreach (var m in mine)
            if (others.Any(p => SnapPoints(p.Def, p.Pose).Any(q => q.DistanceTo(m) < Joint))) n++;
        return n;
    }

    /// <summary>
    /// Can it stand there: a ground piece with the ground between its legs' feet and a little above its origin; any
    /// piece joined at a point to another piece (a roof needs a wall or a roof under it).
    /// </summary>
    public static bool Supported(PieceDef def, Pose pose, Func<double, double, double> ground, IEnumerable<Placed> nearby, long ignore = 0)
    {
        if (def.Ground)
        {
            double g = ground(pose.X, pose.Z);
            if (g <= pose.Y + .6 && g >= pose.Y - def.Legs - .6) return true;
            if (OnFloor(pose, nearby, ignore)) return true;
        }
        return def.Snap.Count > 0 && Joints(def, pose, nearby, ignore) > 0;
    }

    /// <summary>Standing on the top of a floor or foundation (Tom: a campfire on the deck, not only on the ground).</summary>
    public static bool OnFloor(Pose pose, IEnumerable<Placed> nearby, long ignore = 0) =>
        nearby.Any(p => p.Id != ignore && p.Def.Has("floor") && Math.Abs(pose.Y - p.Pose.Y) < .15 && Inside(p.Def, p.Pose, new V3(pose.X, p.Pose.Y - .02, pose.Z), .05));

    /// <summary>
    /// Where a new piece goes when the player aims at <paramref name="aim"/> wanting it to face <paramref name="desiredYaw"/>
    /// (the camera's heading plus the player's turns): onto a point of a piece nearby — the point nearest the aim, the
    /// new piece turned in quarter turns of that piece, the way that makes the most of its points meet others (a wall
    /// along a foundation's edge, a roof's eave along a wall's top) and closest to the wanted heading — and not
    /// overlapping its group. Null when nothing nearby fits (then it goes freely where you aim).
    /// </summary>
    public static (Pose Pose, int Joints)? Snap(PieceDef ghost, V3 aim, double desiredYaw, IReadOnlyList<Placed> nearby, V3? normal = null)
    {
        var joinable = nearby.Where(p => ghost.JoinsTo(p.Def)).ToList();
        if (ghost.JoinPoints.Count == 0 || joinable.Count == 0) return null;
        // (a floor over a room: its middle above a floor or foundation a storey or so below — rather than out over the wall)
        bool floorGhost = ghost.Has("floor");
        var floorsBelow = floorGhost ? nearby.Where(p => p.Def.Has("floor")).ToList() : new List<Placed>();
        // Every point it may join, in a hash of 0.1 m cells (fast "is there a point here").
        var cells = new HashSet<(long, long, long)>();
        foreach (var p in joinable)
            foreach (var q in SnapPoints(p.Def, p.Pose))
                for (int dx = -1; dx <= 1; dx++) for (int dy = -1; dy <= 1; dy++) for (int dz = -1; dz <= 1; dz++)
                    cells.Add(((long)Math.Floor(q.X / .1) + dx, (long)Math.Floor(q.Y / .1) + dy, (long)Math.Floor(q.Z / .1) + dz));
        var candidates = new List<(double Score, Pose Pose, int Joints)>();
        // The way the player turns it decides where it faces (Tom, build 65: stairs and half floors would not turn): a
        // quarter turn away costs more than a few joints; snapping only finds the place. Doors line up with their doorway.
        bool align = ghost.Has("align");
        double turnWeight = align ? .45 : 2;
        foreach (var p in joinable)
        {
            foreach (var t in SnapPoints(p.Def, p.Pose))
            {
                double toAim = t.DistanceTo(aim);
                if (toAim > SnapRadius) continue;
                // (a diagonal piece turns in eighths of what it joins — Tom: diagonal walls; the rest in quarters)
                int steps = ghost.Has("diagonal") ? 8 : 4;
                for (int k = 0; k < steps; k++)
                {
                    if (align && k % 2 == 1) continue; // (a door turns only in line with what it joins)
                    double yaw = p.Pose.Yaw + k * Math.Tau / steps;
                    double turn = Math.Abs(Math.IEEERemainder(yaw - desiredYaw, Math.Tau)) / (Math.PI / 2); // 0 … 2
                    foreach (var g in ghost.JoinPoints)
                    {
                        var at = ToWorld(new Pose(0, 0, 0, yaw), g);
                        var pose = new Pose(t.X - at.X, t.Y - at.Y, t.Z - at.Z, yaw);
                        int joints = 0;
                        foreach (var m in ghost.JoinPoints.Select(j => ToWorld(pose, j)))
                            if (cells.Contains(((long)Math.Floor(m.X / .1), (long)Math.Floor(m.Y / .1), (long)Math.Floor(m.Z / .1)))) joints++;
                        // Near the aim first; more meeting points; the piece's body toward where you aim (a roof corner
                        // in the corner you point at, not half a cell along the wall); then the heading the player wants.
                        var centre = ToWorld(pose, ghost.BoxCenter);
                        double body = centre.DistanceTo(aim);
                        // On the side of the surface you aim at (a wall on a deck, not hanging under its edge: as many
                        // joints either way, Tom's build 63), and rather up than down.
                        var off = centre - aim;
                        double side = normal is { } nv ? off.X * nv.X + off.Y * nv.Y + off.Z * nv.Z : 0;
                        // (behind the surface costs 2.5 a metre: a wall aimed at an upper floor must not wedge between the
                        // storeys, its top on that floor and its bottom on the one below — twice the joints; Tom, build 67)
                        double below = 2.5 * Math.Max(0, -side) + .25 * Math.Max(0, -off.Y);
                        if (floorGhost && floorsBelow.Count > 0 && FloorUnder(centre, floorsBelow)) below -= .8;
                        candidates.Add((toAim - .7 * (joints - 1) + .35 * body + turnWeight * turn + below, pose, joints));
                    }
                }
            }
        }
        foreach (var c in candidates.OrderBy(c => c.Score))
            if (!Blocked(ghost, c.Pose, nearby) && Joints(ghost, c.Pose, nearby) > 0)
            {
                if (Debug != null) foreach (var d in candidates.OrderBy(d => d.Score).Take(8)) Debug($"{d.Score:0.00} j{d.Joints} {d.Pose} blocked {Blocked(ghost, d.Pose, nearby)}");
                return (c.Pose, c.Joints);
            }
        return null;
    }

    /// <summary>The way a piece of a family was placed: which member, where, and the edge it took.</summary>
    public readonly record struct EdgeSnap(PieceDef Piece, Pose Pose, double Distance);

    /// <summary>Edges are taken within this distance of the aim.</summary>
    public const double EdgeRadius = 1.6;

    /// <summary>
    /// Walls and stairs on edges (D136; Tom: "diagonal walls, doorways, doors and steps should snap to a diagonal line by
    /// themselves"): the edge of a floor, foundation or wall top nearest the aim; a wall of the chosen family in the size
    /// that fits it (straight, narrow, diagonal) centred on it, facing the side nearest the wanted heading; stairs with
    /// their top on it (slid along it to the nearest quarter, inside it), running away from the piece that has the edge
    /// (toward the wanted heading's side when it is shared). Not overlapping its group. Null: no edge fits near the aim.
    /// </summary>
    public static EdgeSnap? SnapToEdge(IReadOnlyList<PieceDef> family, V3 aim, double desiredYaw, IReadOnlyList<Placed> nearby,
        IReadOnlyList<PieceDef>? smaller = null)
    {
        if (family.Count == 0 || family[0].EdgeKind.Length == 0) return null;
        // Stairs aimed at the top of a floor or foundation (in from its edges) stand on it — by their points — instead of
        // hanging from an edge (Tom, build 71: no stairs up to a second storey).
        if (family[0].EdgeKind == "stairs" && nearby.Any(p => p.Def.Has("floor") && Math.Abs(aim.Y - p.Pose.Y) < .3
            && Inside(p.Def, p.Pose, new V3(aim.X, p.Pose.Y + p.Def.BoxCenter.Y, aim.Z), -.35))) return null;
        var found = new List<EdgeSnap>();
        foreach (var p in nearby)
            foreach (var (la, lb) in p.Def.Edges)
            {
                var a = ToWorld(p.Pose, la); var b = ToWorld(p.Pose, lb);
                var ab = b - a;
                double len = ab.Length;
                if (len < .3) continue;
                var dir = new V3(ab.X / len, ab.Y / len, ab.Z / len);
                double s = Math.Clamp((aim - a).X * dir.X + (aim - a).Y * dir.Y + (aim - a).Z * dir.Z, 0, len);
                var at = new V3(a.X + dir.X * s, a.Y + dir.Y * s, a.Z + dir.Z * s);
                double dist = at.DistanceTo(aim);
                if (dist > EdgeRadius) continue;
                double yawAlong = Math.Atan2(-dir.Z, dir.X); // (a piece's local +X along the edge)
                // Where lines meet (a side's middle and the line through the centre), the outer edge and the one you face win:
                // an inner line (through the piece's centre) costs 0.25 m, an edge askew to the view up to 0.4 m.
                var mid0 = new V3((a.X + b.X) / 2, 0, (a.Z + b.Z) / 2);
                var c0 = ToWorld(p.Pose, p.Def.BoxCenter);
                bool inner = Math.Sqrt((mid0.X - c0.X) * (mid0.X - c0.X) + (mid0.Z - c0.Z) * (mid0.Z - c0.Z)) < .2;
                double rank = dist + (inner ? .25 : 0) + .4 * (1 - Math.Abs(Math.Cos(desiredYaw - yawAlong)));
                if (family[0].EdgeKind == "wall")
                {
                    // The size that fills the edge; on an edge too short, the smaller family's; a piece shorter than the
                    // edge (a vertical half wall on a 3 m side) slots into its halves — the one nearest the aim (D137).
                    var member = family.FirstOrDefault(m => Math.Abs(m.Length - len) < .05)
                        ?? smaller?.FirstOrDefault(m => Math.Abs(m.Length - len) < .05)
                        ?? family.FirstOrDefault(m => m.Length > .1 && m.Length < len - .05 && Math.Abs(len / m.Length - Math.Round(len / m.Length)) < .02);
                    if (member == null) continue;
                    double piece = member.Length, slots = Math.Round(len / piece);
                    double centre = Within((Math.Floor(s / piece) + .5) * piece, piece / 2, len - piece / 2);
                    if (slots <= 1) centre = len / 2;
                    var mid = new V3(a.X + dir.X * centre, a.Y + dir.Y * centre, a.Z + dir.Z * centre);
                    double yaw = Nearer(yawAlong, yawAlong + Math.PI, desiredYaw);
                    found.Add(new EdgeSnap(member, new Pose(mid.X, mid.Y, mid.Z, yaw), rank));
                }
                else
                {
                    var st = family[0];
                    double half = st.BoxHalf.X, run = st.BoxHalf.Z * 2, rise = st.BoxHalf.Y * 2;
                    if (len < half * 2 - .01) continue;
                    // Slid along the edge to the nearest quarter of it, kept inside it.
                    double q = len / 4, sq = Within(Math.Round(s / q) * q, half, len - half);
                    var top = new V3(a.X + dir.X * sq, a.Y, a.Z + dir.Z * sq);
                    // Away from the piece that has the edge (its centre's side), or toward the wanted heading's side.
                    var n = new V3(-dir.Z, 0, dir.X);
                    var centre = ToWorld(p.Pose, p.Def.BoxCenter);
                    double inward = (centre.X - top.X) * n.X + (centre.Z - top.Z) * n.Z;
                    if (inward < 0) n = new V3(-n.X, 0, -n.Z); // (n: toward the piece: the way up)
                    double up = Math.Atan2(n.X, n.Z);
                    // Up onto the piece; only when clearly turned the other way (within 60°: R twice), up from beneath it.
                    double yaw = Math.Abs(Math.IEEERemainder(desiredYaw - (up + Math.PI), Math.Tau)) < Math.PI / 3 ? up + Math.PI : up;
                    var dirUp = new V3(Math.Sin(yaw), 0, Math.Cos(yaw));
                    var origin = new V3(top.X - dirUp.X * run / 2, top.Y - rise, top.Z - dirUp.Z * run / 2);
                    found.Add(new EdgeSnap(st, new Pose(origin.X, origin.Y, origin.Z, yaw), rank));
                }
            }
        foreach (var f in found.OrderBy(f => f.Distance))
            if (!Blocked(f.Piece, f.Pose, nearby)) return f;
        return null;
    }

    /// <summary>Where a chosen piece goes (D136): on an edge (walls and stairs; the family member that fits), else by its
    /// points. The piece returned may be another size of the chosen one.</summary>
    public static (PieceDef Piece, Pose Pose)? SnapPiece(PieceDef chosen, IReadOnlyList<PieceDef> family, V3 aim, double desiredYaw, IReadOnlyList<Placed> nearby, V3? normal = null,
        IReadOnlyList<PieceDef>? smaller = null)
    {
        if (SnapToEdge(family, aim, desiredYaw, nearby, smaller) is { } e) return (e.Piece, e.Pose);
        if (Snap(chosen, aim, desiredYaw, nearby, normal) is { } s) return (chosen, s.Pose);
        return null;
    }

    private static bool FloorUnder(V3 centre, List<Placed> floors) =>
        floors.Any(p => centre.Y - p.Pose.Y is > 1 and < 7 && Inside(p.Def, p.Pose, new V3(centre.X, p.Pose.Y + p.Def.BoxCenter.Y, centre.Z)));

    /// <summary>Clamped into [lo, hi]; the middle when the edge is a hair too short for the piece (rounding: 2.9999999 m
    /// for 3 — Math.Clamp throws then, and the ghost stopped: Tom's stairs, build 71).</summary>
    private static double Within(double v, double lo, double hi) => lo > hi ? (lo + hi) / 2 : Math.Clamp(v, lo, hi);

    private static double Nearer(double a, double b, double want) =>
        Math.Abs(Math.IEEERemainder(a - want, Math.Tau)) <= Math.Abs(Math.IEEERemainder(b - want, Math.Tau)) ? a : b;

    /// <summary>The pose of a piece put down where nothing snaps: at the aim (lifted by the piece's lift and the player's
    /// raise), facing the wanted heading.</summary>
    public static Pose Free(PieceDef def, V3 aim, double desiredYaw, double raise, Func<double, double, double>? ground = null)
    {
        double y = aim.Y;
        // A piece on legs (a foundation) stands level above the highest ground under its corners (Tom: on a slope it
        // should not sink into the hill); its legs reach down to the rest.
        if (ground != null && def.Ground && def.Legs > 1)
        {
            var pose = new Pose(aim.X, 0, aim.Z, desiredYaw);
            y = ground(aim.X, aim.Z);
            foreach (double sx in new[] { -1.0, 1 })
                foreach (double sz in new[] { -1.0, 1 })
                {
                    var c = ToWorld(pose, new V3(def.BoxCenter.X + sx * def.BoxHalf.X, 0, def.BoxCenter.Z + sz * def.BoxHalf.Z));
                    y = Math.Max(y, ground(c.X, c.Z));
                }
        }
        return new(aim.X, y + def.Lift + raise, aim.Z, desiredYaw);
    }

    /// <summary>Is the point (world) inside the piece's box, grown by <paramref name="grow"/> (the player under a roof).</summary>
    public static bool Inside(PieceDef def, Pose pose, V3 point, double grow = 0)
    {
        var c = ToWorld(pose, def.BoxCenter);
        double dx = point.X - c.X, dz = point.Z - c.Z, s = Math.Sin(pose.Yaw), co = Math.Cos(pose.Yaw);
        double lx = dx * co - dz * s, lz = dx * s + dz * co; // (back into the piece's axes)
        return Math.Abs(lx) <= def.BoxHalf.X + grow && Math.Abs(lz) <= def.BoxHalf.Z + grow && Math.Abs(point.Y - c.Y) <= def.BoxHalf.Y + grow;
    }
}
