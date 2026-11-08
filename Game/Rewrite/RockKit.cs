using System;
using System.Collections.Generic;
using Godot;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// The rock kit (D277, after Tom's references: chunky stylised rocks, a formation built of many distinct pieces, not one lumpy
/// blob): each piece a solid with mostly flat faces and bevelled edges — a box, turned and tilted, its corners cut off by a few
/// random planes — and four kinds of formation built of them: column clusters (tall prisms side by side), slab stacks (flat,
/// offset layers), tiered piles (stepped blocks with rubble round the foot) and tors (rounded granite blocks with split slabs).
/// Each piece built as a true polyhedron from its planes (flat faces, bevelled edges, flat-shaded: crisp), the crevice shadow
/// from the pieces' union as a solid; in metres, the formation's foot at y = 0, its pieces reaching 2 m below into the ground. Each piece is also kept as a box, for
/// collision.
/// </summary>
public static class RockKit
{
    public enum Kind { Columns, Slabs, Tiered, Tor }
    public const int Kinds = 4, Variants = 4;

    /// <summary>A piece: centre, half extents and turn (local axes), for collision.</summary>
    public readonly record struct Piece(Vector3 Centre, Vector3 Half, Basis Basis);

    /// <summary>One piece as a solid: a bevelled box with corners cut.</summary>
    private sealed class Block
    {
        public Vector3 Centre, Half;
        public Basis Basis, Inverse;
        /// <summary>Its six faces (D277, after Tom: "too regular ... too artificial"): each a box face turned a little its own
        /// way — the sides leaning in toward the top (tapered) or out, the top sloping — so no two faces are square to each other.</summary>
        public (Vector3 N, float D)[] Faces = Array.Empty<(Vector3, float)>();
        public (Vector3 N, float D)[] Cuts = Array.Empty<(Vector3, float)>();
        public float Bevel, Reach;

        public float Distance(Vector3 p)
        {
            var q = Inverse * (p - Centre);
            float d = float.MinValue;
            foreach (var (n, o) in Faces) d = d == float.MinValue ? q.Dot(n) - o : SmoothMax(d, q.Dot(n) - o, Bevel);
            foreach (var (n, o) in Cuts) d = SmoothMax(d, q.Dot(n) - o, Bevel * .7f);
            return d;
        }
    }

    private static readonly Color Stone = new("777a72"), Dark = new("5d605a"), Moss = new("66744f");
    private const float RockLight = 1.45f;

    /// <summary>
    /// The pieces as polyhedra: each the intersection of its half-spaces — the box's six faces, a chamfer along each of its
    /// twelve edges (a plane between the two faces, cut in by the bevel), and the corner cuts — its vertices every three
    /// planes' meeting point inside all the rest, each face the points on its plane in order round them. Flat-shaded, each face
    /// its own shade (bands of the beds by height), moss on open tops, darker by how much rock lies close above it.
    /// </summary>
    private static (Vector3[] Vertices, Vector3[] Normals, Color[] Colors) Polyhedra(List<Block> blocks, Func<float, float, float, float> solid, ulong seed)
    {
        var vertices = new List<Vector3>(); var normals = new List<Vector3>(); var colours = new List<Color>();
        var rng = new RandomStream(seed ^ 0xFACEUL);
        var axes = new[] { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward };
        foreach (var b in blocks)
        {
            var planes = new List<(Vector3 N, float D)>(b.Faces);
            float c = Math.Max(.15f, b.Bevel * .7f);
            for (int i = 0; i < 6; i++)
                for (int j = i + 1; j < 6; j++)
                {
                    if (j == (i ^ 1)) continue; // (opposite faces)
                    // A chamfer along the edge where two faces meet: on that edge n_i·p = d_i and n_j·p = d_j, so (n_i + n_j)·p =
                    // d_i + d_j; moved in by c.
                    var (ni, di) = b.Faces[i]; var (nj, dj) = b.Faces[j];
                    var sum = ni + nj; float len = sum.Length();
                    planes.Add((sum / len, (di + dj) / len - c));
                }
            foreach (var (n, o) in b.Cuts) planes.Add((n, o));
            // The corners: every three planes meeting inside all the others.
            var points = new List<Vector3>();
            for (int i = 0; i < planes.Count; i++)
                for (int j = i + 1; j < planes.Count; j++)
                    for (int k = j + 1; k < planes.Count; k++)
                    {
                        var (n1, d1) = planes[i]; var (n2, d2) = planes[j]; var (n3, d3) = planes[k];
                        float det = n1.Dot(n2.Cross(n3));
                        if (Math.Abs(det) < 1e-5f) continue;
                        var q = (n2.Cross(n3) * d1 + n3.Cross(n1) * d2 + n1.Cross(n2) * d3) / det;
                        bool inside = true;
                        foreach (var (n, d) in planes) if (n.Dot(q) > d + 1e-3f) { inside = false; break; }
                        if (inside) points.Add(q);
                    }
            foreach (var (n, d) in planes)
            {
                // This face's corners (no repeats), in order round its middle.
                var face = new List<Vector3>();
                foreach (var q in points)
                    if (Math.Abs(n.Dot(q) - d) < 2e-3f && !face.Exists(f => f.DistanceSquaredTo(q) < 1e-6f)) face.Add(q);
                if (face.Count < 3) continue;
                var mid = Vector3.Zero; foreach (var q in face) mid += q; mid /= face.Count;
                var u = (Math.Abs(n.Y) < .9f ? Vector3.Up : Vector3.Right).Cross(n).Normalized(); var v = n.Cross(u);
                face.Sort((p1, p2) => Mathf.Atan2((p1 - mid).Dot(v), (p1 - mid).Dot(u)).CompareTo(Mathf.Atan2((p2 - mid).Dot(v), (p2 - mid).Dot(u))));
                // Into the formation's space.
                var wn = (b.Basis * n).Normalized();
                var wp = face.ConvertAll(q => b.Centre + b.Basis * q);
                var wmid = b.Centre + b.Basis * mid;
                if (wmid.Y < -2.4f && wn.Y < -.5f) continue; // (a buried underside)
                float shade = (float)rng.Unit();
                float band = .5f + .5f * Mathf.Sin(wmid.Y * 1.3f + (float)rng.Unit() * .6f);
                var colour = Stone.Lerp(Dark, .08f + .3f * band + .2f * shade);
                if (wn.Y > .72f) colour = Moss.Lerp(colour, .45f + .2f * shade);
                // Crevice shadow: how much rock lies close above the face (the union's solid at 0.5, 1.2 and 2.5 m out).
                float occ = 0;
                foreach (var (dd, w) in new[] { (.5f, .5f), (1.2f, .3f), (2.5f, .2f) })
                {
                    var at = wmid + wn * dd;
                    occ += w * Mathf.Clamp((dd - solid(at.X, at.Y, at.Z)) / dd, 0, 1);
                }
                float ao = 1 - .5f * Mathf.Clamp(occ * 1.4f, 0, 1);
                var tinted = new Color(colour.R * RockLight * ao, colour.G * RockLight * ao, colour.B * RockLight * ao, 1);
                // (Godot front faces are clockwise seen from outside: the corners run counter-clockwise round n)
                for (int t = 1; t + 1 < wp.Count; t++)
                {
                    vertices.Add(wp[0]); vertices.Add(wp[t + 1]); vertices.Add(wp[t]);
                    for (int r = 0; r < 3; r++) { normals.Add(wn); colours.Add(tinted); }
                }
            }
        }
        return (vertices.ToArray(), normals.ToArray(), colours.ToArray());
    }

    private static float SmoothMax(float a, float b, float k)
    {
        if (k <= 1e-4f) return Math.Max(a, b);
        float h = Math.Max(k - Math.Abs(a - b), 0) / k;
        return Math.Max(a, b) + h * h * k * .25f;
    }

    /// <summary>A formation's mesh data and its pieces.</summary>
    public static ((Vector3[] Vertices, Vector3[] Normals, Color[] Colors) Mesh, Piece[] Pieces) Build(Kind kind, int variant)
    {
        ulong seed = 0x40C4C17UL + (ulong)kind * 7919 + (ulong)variant * 104729;
        var rng = new RandomStream(seed);
        var blocks = new List<Block>();
        // A piece: half extents, turned about y by `turn`, tilted a little, corners cut by `cuts` planes.
        void Add(Vector3 centre, Vector3 half, float turn, float tilt, int cuts, float bevel)
        {
            var basis = new Basis(Vector3.Up, turn) * new Basis(Vector3.Right, (float)rng.Between(-tilt, tilt)) * new Basis(Vector3.Forward, (float)rng.Between(-tilt, tilt));
            // The faces turned each their own way: sides leaning in toward the top (mostly), the top and bottom sloping.
            var faces = new (Vector3, float)[6];
            var axes0 = new[] { Vector3.Right, Vector3.Left, Vector3.Up, Vector3.Down, Vector3.Back, Vector3.Forward };
            float[] h0 = { half.X, half.X, half.Y, half.Y, half.Z, half.Z };
            for (int f = 0; f < 6; f++)
            {
                var n = axes0[f];
                if (f is 2 or 3) n += new Vector3((float)rng.Between(-.22, .22), 0, (float)rng.Between(-.22, .22));
                else n += Vector3.Up * (float)rng.Between(-.06, .3) + (f < 2 ? Vector3.Back : Vector3.Right) * (float)rng.Between(-.16, .16);
                faces[f] = (n.Normalized(), h0[f] * (float)rng.Between(.88, 1.05));
            }
            cuts += rng.Int(3);
            var list = new List<(Vector3, float)>();
            for (int c = 0; c < cuts; c++)
            {
                // A plane cutting off a corner: pointing toward one, set inside it by a fifth to a half of the way.
                var corner = new Vector3(rng.Unit() < .5 ? -1 : 1, rng.Unit() < .65 ? 1 : -1, rng.Unit() < .5 ? -1 : 1);
                var n = (corner + new Vector3((float)rng.Between(-.5, .5), (float)rng.Between(-.4, .4), (float)rng.Between(-.5, .5))).Normalized();
                float full = Math.Abs(n.X) * half.X + Math.Abs(n.Y) * half.Y + Math.Abs(n.Z) * half.Z;
                list.Add((n, full * (float)rng.Between(.48, .8)));
            }
            blocks.Add(new Block { Centre = centre, Half = half, Basis = basis, Inverse = basis.Inverse(), Faces = faces, Cuts = list.ToArray(), Bevel = bevel,
                Reach = half.Length() + bevel + 1 });
        }
        float Rubble(float spread, int count, float size)
        {
            for (int i = 0; i < count; i++)
            {
                float a = (float)rng.Unit() * Mathf.Tau, r = spread * (float)rng.Between(.75, 1.15), s = size * (float)rng.Between(.5, 1.1);
                Add(new Vector3(Mathf.Cos(a) * r, s * .3f, Mathf.Sin(a) * r), new Vector3(s, s * (float)rng.Between(.4, .7), s * (float)rng.Between(.6, 1)),
                    (float)rng.Unit() * Mathf.Tau, .35f, 3, s * .25f);
            }
            return spread;
        }
        switch (kind)
        {
            case Kind.Columns:
            {
                // Tall prisms packed on a jittered grid in an ellipse, the tallest toward the middle, their tops cut at angles.
                float rx = (float)rng.Between(5, 8), rz = (float)rng.Between(3.5, 6), top = (float)rng.Between(11, 17), step = (float)rng.Between(2.6, 3.4);
                float turn = (float)rng.Unit() * Mathf.Tau;
                for (float gx = -rx; gx <= rx; gx += step)
                    for (float gz = -rz; gz <= rz; gz += step * .9f)
                    {
                        float u = (gx * gx) / (rx * rx) + (gz * gz) / (rz * rz);
                        if (u > 1 || rng.Unit() < .12) continue;
                        float h = top * (1 - .65f * u) * (float)rng.Between(.6, 1.05);
                        float w = step * (float)rng.Between(.45, .62);
                        var at = new Vector3(gx + (float)rng.Between(-.5, .5), h * .5f - 2, gz + (float)rng.Between(-.5, .5));
                        Add(at.Rotated(Vector3.Up, turn), new Vector3(w, h * .5f + 1, w * (float)rng.Between(.7, 1)), turn + (float)rng.Between(-.4, .4), .07f, 3, w * .18f);
                    }
                Rubble(Math.Max(rx, rz) + 1.5f, 6 + rng.Int(5), 1.2f);
                break;
            }
            case Kind.Slabs:
            {
                // Flat layers, each smaller and set off from the one below, a little tilted: a stack weathered from bedded rock.
                int layers = 3 + rng.Int(4);
                float w = (float)rng.Between(6, 9), d = (float)rng.Between(4, 6.5), y = -1.5f, turn = (float)rng.Unit() * Mathf.Tau;
                var off = Vector3.Zero;
                for (int i = 0; i < layers; i++)
                {
                    float t = (float)rng.Between(.8, 1.5);
                    // (each layer set off from the one below, but its middle always over it)
                    off += new Vector3((float)rng.Between(-1, 1), 0, (float)rng.Between(-.8, .8));
                    off = new Vector3(Math.Clamp(off.X, -w * .3f, w * .3f), 0, Math.Clamp(off.Z, -d * .3f, d * .3f));
                    Add((off + new Vector3(0, y + t, 0)).Rotated(Vector3.Up, turn), new Vector3(w, t, d), turn + (float)rng.Between(-.25, .25), .05f, 4, t * .35f);
                    y += t * 2 - .15f;
                    w *= (float)rng.Between(.72, .92); d *= (float)rng.Between(.72, .95);
                }
                // A slab fallen against the side.
                if (rng.Unit() < .7)
                {
                    float a = (float)rng.Unit() * Mathf.Tau;
                    Add(new Vector3(Mathf.Cos(a) * 7, 1.2f, Mathf.Sin(a) * 6), new Vector3(3, .8f, 2.2f), a, .5f, 3, .3f);
                }
                Rubble(9, 3 + rng.Int(4), 1);
                break;
            }
            case Kind.Tiered:
            {
                // Steps of blocks round a high middle: the pile of the reference images, rubble at the foot.
                int tiers = 3 + rng.Int(2);
                float r = (float)rng.Between(7, 9.5), y = -1.5f;
                for (int t = 0; t < tiers; t++)
                {
                    int count = Math.Max(1, 7 - 2 * t + rng.Int(2));
                    float ring = r * (1 - (float)t / tiers) * .75f, size = r * .32f * (1 - .12f * t), hgt = (float)rng.Between(1.6, 2.6) + t * .4f;
                    float a0 = (float)rng.Unit() * Mathf.Tau;
                    for (int i = 0; i < count; i++)
                    {
                        float a = a0 + i * Mathf.Tau / count + (float)rng.Between(-.25, .25), rr = count == 1 ? 0 : ring * (float)rng.Between(.7, 1);
                        Add(new Vector3(Mathf.Cos(a) * rr, y + hgt, Mathf.Sin(a) * rr), new Vector3(size * (float)rng.Between(.7, 1.1), hgt, size * (float)rng.Between(.6, 1)),
                            a + (float)rng.Between(-.3, .3), .08f, 3, size * .16f);
                    }
                    y += hgt * 1.6f;
                }
                Rubble(r + 1.5f, 8 + rng.Int(5), 1.1f);
                break;
            }
            default:
            {
                // A tor: big rounded granite blocks, a few on the ground side by side and one or two resting on them — each upper
                // block centred over the one below, smaller, sunk into it (Tom: some "feel like they shouldn't be standing like
                // that": blocks set anywhere at any height overhung their support), a slab split off and lying beside.
                int ground = 2 + rng.Int(2);
                var bases = new List<(Vector3 At, Vector3 Half)>();
                for (int i = 0; i < ground; i++)
                {
                    float s = (float)rng.Between(2.8, 4.2);
                    var hh = new Vector3(s * (float)rng.Between(1, 1.4), s * (float)rng.Between(.55, .8), s * (float)rng.Between(.8, 1.15));
                    var at = new Vector3(i * 4.5f - (ground - 1) * 2.25f + (float)rng.Between(-1, 1), hh.Y * .75f - 1.3f, (float)rng.Between(-1.5, 1.5));
                    Add(at, hh, (float)rng.Unit() * Mathf.Tau, .12f, 2, s * .5f);
                    bases.Add((at, hh));
                }
                for (int up = 0, n = 1 + rng.Int(2); up < n; up++)
                {
                    var (bAt, bHalf) = bases[rng.Int(bases.Count)];
                    float s = Math.Min(bHalf.X, bHalf.Z) * (float)rng.Between(.6, .85);
                    var hh = new Vector3(s * (float)rng.Between(1, 1.3), s * (float)rng.Between(.5, .7), s * (float)rng.Between(.8, 1.1));
                    var at = bAt + new Vector3((float)rng.Between(-.3, .3) * bHalf.X, bHalf.Y + hh.Y * .8f, (float)rng.Between(-.3, .3) * bHalf.Z);
                    Add(at, hh, (float)rng.Unit() * Mathf.Tau, .1f, 2, s * .5f);
                    bases.Add((at, hh));
                }
                for (int i = 0, slabs = 1 + rng.Int(2); i < slabs; i++)
                {
                    float a = (float)rng.Unit() * Mathf.Tau;
                    Add(new Vector3(Mathf.Cos(a) * 6.5f, .3f, Mathf.Sin(a) * 5.5f), new Vector3(2.6f, .7f, 1.9f), a, .35f, 3, .5f);
                }
                Rubble(8, 3 + rng.Int(3), 1);
                break;
            }
        }
        // The solid: the nearest piece (each only near its own box), a little roughness on the faces.
        var min = new Vector3(float.MaxValue, -3, float.MaxValue); var max = new Vector3(float.MinValue, 0, float.MinValue);
        foreach (var b in blocks)
        {
            min = new Vector3(Math.Min(min.X, b.Centre.X - b.Reach), min.Y, Math.Min(min.Z, b.Centre.Z - b.Reach));
            max = new Vector3(Math.Max(max.X, b.Centre.X + b.Reach), Math.Max(max.Y, b.Centre.Y + b.Reach), Math.Max(max.Z, b.Centre.Z + b.Reach));
        }
        float Solid(float x, float y, float z)
        {
            var p = new Vector3(x, y, z);
            float d = float.MaxValue;
            foreach (var b in blocks)
            {
                float far = (p - b.Centre).Length() - b.Reach;
                d = Math.Min(d, far > 0 ? far + 1 : b.Distance(p));
            }
            // (closed below their buried foot)
            return SmoothMax(d, -2.5f - y, .5f);
        }
        // Crisp (D277, Tom: "crisper faces first"): each piece built from its own planes — a true polyhedron, its faces flat and
        // its edges bevelled by narrow flat strips — not sampled from the solid (that rounded everything a little: lumps).
        // The pieces overlap; the solid is kept for the crevice shadow.
        var model = Polyhedra(blocks, Solid, seed);
        var pieces = new Piece[blocks.Count];
        for (int i = 0; i < blocks.Count; i++) pieces[i] = new Piece(blocks[i].Centre, blocks[i].Half * .92f, blocks[i].Basis);
        return (model, pieces);
    }
}
