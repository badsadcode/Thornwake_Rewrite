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
/// Meshed as the sea stacks are (a signed-distance solid, surface nets, crevice shadow baked in, auto-smooth), in metres, the
/// formation's foot at y = 0 and its pieces reaching 2 m below to sit in the ground. Each piece is also kept as a box, for
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
        public (Vector3 N, float D)[] Cuts = Array.Empty<(Vector3, float)>();
        public float Bevel, Reach;

        public float Distance(Vector3 p)
        {
            var q = Inverse * (p - Centre);
            float d = SmoothMax(SmoothMax(Math.Abs(q.X) - Half.X, Math.Abs(q.Y) - Half.Y, Bevel), Math.Abs(q.Z) - Half.Z, Bevel);
            foreach (var (n, o) in Cuts) d = SmoothMax(d, q.Dot(n) - o, Bevel * .7f);
            return d;
        }
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
            var list = new List<(Vector3, float)>();
            for (int c = 0; c < cuts; c++)
            {
                // A plane cutting off a corner: pointing toward one, set inside it by a fifth to a half of the way.
                var corner = new Vector3(rng.Unit() < .5 ? -1 : 1, rng.Unit() < .65 ? 1 : -1, rng.Unit() < .5 ? -1 : 1);
                var n = (corner + new Vector3((float)rng.Between(-.5, .5), (float)rng.Between(-.4, .4), (float)rng.Between(-.5, .5))).Normalized();
                float full = Math.Abs(n.X) * half.X + Math.Abs(n.Y) * half.Y + Math.Abs(n.Z) * half.Z;
                list.Add((n, full * (float)rng.Between(.55, .82)));
            }
            blocks.Add(new Block { Centre = centre, Half = half, Basis = basis, Inverse = basis.Inverse(), Cuts = list.ToArray(), Bevel = bevel,
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
                    off += new Vector3((float)rng.Between(-1.2, 1.2), 0, (float)rng.Between(-.9, .9));
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
                // A tor: big rounded granite blocks piled two or three high, a slab split off and lying beside.
                int count = 3 + rng.Int(3);
                float y = -1.2f;
                for (int i = 0; i < count; i++)
                {
                    float s = (float)rng.Between(2.6, 4.2) * (1 - .1f * i);
                    var at = new Vector3((float)rng.Between(-3.5, 3.5), y + s * .7f, (float)rng.Between(-2.5, 2.5));
                    Add(at, new Vector3(s * (float)rng.Between(1, 1.5), s * (float)rng.Between(.6, .85), s * (float)rng.Between(.8, 1.2)), (float)rng.Unit() * Mathf.Tau, .18f, 2, s * .55f);
                    if (rng.Unit() < .55) y += s * 1.1f;
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
        float cell = Math.Clamp((max.X - min.X + max.Z - min.Z) / 70, .55f, .9f);
        var model = SeaStackView.KitSurface(Solid, min - Vector3.One * 2, max + Vector3.One * 2, cell, seed);
        var pieces = new Piece[blocks.Count];
        for (int i = 0; i < blocks.Count; i++) pieces[i] = new Piece(blocks[i].Centre, blocks[i].Half * .92f, blocks[i].Basis);
        return (model, pieces);
    }
}
