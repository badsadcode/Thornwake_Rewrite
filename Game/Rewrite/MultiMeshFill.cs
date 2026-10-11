using System;
using System.Linq;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Fills a MultiMesh in one call (round 30, tree performance). Setting instances one by one costs an engine call each
/// (through the render server's command queue) on the main thread; a tile of far trees has tens of thousands of
/// instance parts, so committing it stalled the frame (up to 420 ms measured). Here the instances are packed into one
/// float array in plain C# and handed over with a single <c>MultiMesh.Buffer</c> assignment: the same result, a
/// fraction of the time. Layout per instance (Godot's): a 3×4 transform by rows (basis row 0, origin.x, row 1,
/// origin.y, row 2, origin.z), then the colour (4 floats) if used, then the custom data (4 floats) if used.
/// <c>--legacy-multimesh</c> switches back to per-instance calls (to compare in the benchmark).
/// </summary>
public static class MultiMeshFill
{
    public static readonly bool Legacy = OS.GetCmdlineUserArgs().Contains("--legacy-multimesh");

    public static int Stride(MultiMesh m) => 12 + (m.UseColors ? 4 : 0) + (m.UseCustomData ? 4 : 0);

    public static void Put(float[] buffer, int offset, in Transform3D t)
    {
        var b = t.Basis;
        buffer[offset] = b.X.X; buffer[offset + 1] = b.Y.X; buffer[offset + 2] = b.Z.X; buffer[offset + 3] = t.Origin.X;
        buffer[offset + 4] = b.X.Y; buffer[offset + 5] = b.Y.Y; buffer[offset + 6] = b.Z.Y; buffer[offset + 7] = t.Origin.Y;
        buffer[offset + 8] = b.X.Z; buffer[offset + 9] = b.Y.Z; buffer[offset + 10] = b.Z.Z; buffer[offset + 11] = t.Origin.Z;
    }

    public static void Put(float[] buffer, int offset, Color c)
    {
        buffer[offset] = c.R; buffer[offset + 1] = c.G; buffer[offset + 2] = c.B; buffer[offset + 3] = c.A;
    }

    /// <summary>Fill <paramref name="multi"/> (format, mesh and flags already set) with <paramref name="count"/> instances.</summary>
    public static void Fill(MultiMesh multi, int count, Func<int, Transform3D> transform, Func<int, Color>? colour = null, Func<int, Color>? custom = null)
    {
        multi.InstanceCount = count;
        if (count == 0) return;
        if (Legacy)
        {
            for (int i = 0; i < count; i++)
            {
                multi.SetInstanceTransform(i, transform(i));
                if (colour != null) multi.SetInstanceColor(i, colour(i));
                if (custom != null) multi.SetInstanceCustomData(i, custom(i));
            }
            return;
        }
        int stride = Stride(multi);
        var buffer = new float[count * stride];
        for (int i = 0, o = 0; i < count; i++, o += stride)
        {
            Put(buffer, o, transform(i));
            int extra = o + 12;
            if (multi.UseColors) { Put(buffer, extra, colour?.Invoke(i) ?? Colors.White); extra += 4; }
            if (multi.UseCustomData) Put(buffer, extra, custom?.Invoke(i) ?? new Color(0, 0, 0, 0));
        }
        multi.Buffer = buffer;
    }
}
