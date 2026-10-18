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
        double hx = Math.Max(0, d.BoxHalf.X - margin), hz = Math.Max(0, d.BoxHalf.Z - margin);
        return hx * Math.Abs(c * ux - s * uz) + hz * Math.Abs(s * ux + c * uz);
    }

    /// <summary>A piece already in the world, as the rules see it.</summary>
    public readonly record struct Placed(long Id, PieceDef Def, Pose Pose);

    /// <summary>Overlaps a piece of its own group (ignoring <paramref name="ignore"/>: the one it replaces).</summary>
    public static bool Blocked(PieceDef def, Pose pose, IEnumerable<Placed> nearby, long ignore = 0) =>
        nearby.Any(p => p.Id != ignore && p.Def.Group == def.Group && def.Group.Length > 0 && Overlaps(def, pose, p.Def, p.Pose));

    /// <summary>How many of the piece's points meet a point of the pieces nearby.</summary>
    public static int Joints(PieceDef def, Pose pose, IEnumerable<Placed> nearby, long ignore = 0)
    {
        var mine = SnapPoints(def, pose).ToList();
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
        if (ghost.Snap.Count == 0 || joinable.Count == 0) return null;
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
                for (int k = 0; k < 4; k++)
                {
                    if (align && k % 2 == 1) continue; // (a door turns only in line with what it joins)
                    double yaw = p.Pose.Yaw + k * Math.PI / 2;
                    double turn = Math.Abs(Math.IEEERemainder(yaw - desiredYaw, Math.Tau)) / (Math.PI / 2); // 0 … 2
                    foreach (var g in ghost.Snap)
                    {
                        var at = ToWorld(new Pose(0, 0, 0, yaw), g);
                        var pose = new Pose(t.X - at.X, t.Y - at.Y, t.Z - at.Z, yaw);
                        int joints = 0;
                        foreach (var m in SnapPoints(ghost, pose))
                            if (cells.Contains(((long)Math.Floor(m.X / .1), (long)Math.Floor(m.Y / .1), (long)Math.Floor(m.Z / .1)))) joints++;
                        // Near the aim first; more meeting points; the piece's body toward where you aim (a roof corner
                        // in the corner you point at, not half a cell along the wall); then the heading the player wants.
                        var centre = ToWorld(pose, ghost.BoxCenter);
                        double body = centre.DistanceTo(aim);
                        // On the side of the surface you aim at (a wall on a deck, not hanging under its edge: as many
                        // joints either way, Tom's build 63), and rather up than down.
                        var off = centre - aim;
                        double side = normal is { } nv ? off.X * nv.X + off.Y * nv.Y + off.Z * nv.Z : 0;
                        double below = .8 * Math.Max(0, -side) + .25 * Math.Max(0, -off.Y);
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
