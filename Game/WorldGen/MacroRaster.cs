using System;
using System.Threading.Tasks;

namespace Thornwake.WorldGen;

/// <summary>
/// Square float grid covering the whole world, with cell-centred samples and C1-continuous Catmull–Rom
/// (bicubic) reconstruction. Several channels share one lookup so a query computes weights once.
/// Outside the grid the edge value is extended (the world edge is open ocean).
/// </summary>
public sealed class MacroRaster
{
    public int Size { get; }
    public double MinX { get; }
    public double MinZ { get; }
    public double Cell { get; }
    public int ChannelCount => _channels.Length;
    private readonly float[][] _channels;

    public MacroRaster(int size, double minX, double minZ, double cell, int channels)
    {
        if (size < 4 || size > 8192 || channels < 1 || channels > 16 || !(cell > 0)) throw new ArgumentOutOfRangeException(nameof(size));
        Size = size; MinX = minX; MinZ = minZ; Cell = cell;
        _channels = new float[channels][];
        for (int c = 0; c < channels; c++) _channels[c] = new float[size * size];
    }

    public float[] Channel(int channel) => _channels[channel];
    public long Bytes => (long)_channels.Length * Size * Size * sizeof(float);

    /// <summary>World position of a cell centre.</summary>
    public double CellX(int i) => MinX + (i + .5) * Cell;
    public double CellZ(int j) => MinZ + (j + .5) * Cell;

    /// <summary>Fill every cell in parallel. <paramref name="fill"/> must be a pure function of (i, j).</summary>
    public void Fill(Action<int, int, float[][]> fill)
    {
        Parallel.For(0, Size, j =>
        {
            for (int i = 0; i < Size; i++) fill(i, j, _channels);
        });
    }

    /// <summary>Bicubic sample of up to three channels at once (pass -1 for unused channels).</summary>
    public void Sample(double x, double z, int c0, int c1, int c2, out double v0, out double v1, out double v2)
    {
        double u = (x - MinX) / Cell - .5, w = (z - MinZ) / Cell - .5;
        double fu = Math.Floor(u), fw = Math.Floor(w);
        double tu = u - fu, tw = w - fw;
        // Catmull–Rom weights for taps -1..2 on each axis.
        double tu2 = tu * tu, tu3 = tu2 * tu, tw2 = tw * tw, tw3 = tw2 * tw;
        double a0 = .5 * (-tu3 + 2 * tu2 - tu), a1 = .5 * (3 * tu3 - 5 * tu2 + 2), a2 = .5 * (-3 * tu3 + 4 * tu2 + tu), a3 = .5 * (tu3 - tu2);
        double b0 = .5 * (-tw3 + 2 * tw2 - tw), b1 = .5 * (3 * tw3 - 5 * tw2 + 2), b2 = .5 * (-3 * tw3 + 4 * tw2 + tw), b3 = .5 * (tw3 - tw2);
        if (fu >= 1 && fw >= 1 && fu <= Size - 3 && fw <= Size - 3)
        {
            // Interior fast path: no clamping.
            int k = ((int)fw - 1) * Size + (int)fu - 1;
            v0 = Fast(_channels[c0], k, a0, a1, a2, a3, b0, b1, b2, b3);
            v1 = c1 < 0 ? 0 : Fast(_channels[c1], k, a0, a1, a2, a3, b0, b1, b2, b3);
            v2 = c2 < 0 ? 0 : Fast(_channels[c2], k, a0, a1, a2, a3, b0, b1, b2, b3);
            return;
        }
        int i = (int)DetMath.Clamp(fu, -2, Size + 1), j = (int)DetMath.Clamp(fw, -2, Size + 1);
        if (fu < -2 || fu > Size + 1) { a0 = 0; a1 = 1; a2 = 0; a3 = 0; }
        if (fw < -2 || fw > Size + 1) { b0 = 0; b1 = 1; b2 = 0; b3 = 0; }
        Span<double> wu = stackalloc double[] { a0, a1, a2, a3 };
        Span<double> ww = stackalloc double[] { b0, b1, b2, b3 };
        v0 = Gather(c0, i, j, wu, ww);
        v1 = c1 < 0 ? 0 : Gather(c1, i, j, wu, ww);
        v2 = c2 < 0 ? 0 : Gather(c2, i, j, wu, ww);
    }

    private double Fast(float[] d, int k, double a0, double a1, double a2, double a3, double b0, double b1, double b2, double b3)
    {
        int s = Size;
        double r0 = d[k] * a0 + d[k + 1] * a1 + d[k + 2] * a2 + d[k + 3] * a3; k += s;
        double r1 = d[k] * a0 + d[k + 1] * a1 + d[k + 2] * a2 + d[k + 3] * a3; k += s;
        double r2 = d[k] * a0 + d[k + 1] * a1 + d[k + 2] * a2 + d[k + 3] * a3; k += s;
        double r3 = d[k] * a0 + d[k + 1] * a1 + d[k + 2] * a2 + d[k + 3] * a3;
        return r0 * b0 + r1 * b1 + r2 * b2 + r3 * b3;
    }

    /// <summary>Bicubic sample with its world-space gradient (one channel).</summary>
    public double SampleGradient(int channel, double x, double z, out double ddx, out double ddz)
    {
        double u = (x - MinX) / Cell - .5, w = (z - MinZ) / Cell - .5;
        double fu = Math.Floor(u), fw = Math.Floor(w);
        int i = (int)DetMath.Clamp(fu, -2, Size + 1), j = (int)DetMath.Clamp(fw, -2, Size + 1);
        double tu = DetMath.Clamp(u - fu, 0, 1), tw = DetMath.Clamp(w - fw, 0, 1);
        Span<double> wu = stackalloc double[4], ww = stackalloc double[4], du = stackalloc double[4], dw = stackalloc double[4];
        Weights(tu, wu); Weights(tw, ww); Derivatives(tu, du); Derivatives(tw, dw);
        ddx = Gather(channel, i, j, du, ww) / Cell;
        ddz = Gather(channel, i, j, wu, dw) / Cell;
        return Gather(channel, i, j, wu, ww);
    }

    public double Sample(int channel, double x, double z)
    {
        Sample(x, z, channel, -1, -1, out double v, out _, out _);
        return v;
    }

    private double Gather(int channel, int i, int j, ReadOnlySpan<double> wu, ReadOnlySpan<double> ww)
    {
        var data = _channels[channel];
        double sum = 0;
        int last = Size - 1;
        for (int b = 0; b < 4; b++)
        {
            int row = Math.Clamp(j - 1 + b, 0, last) * Size;
            double s = 0;
            for (int a = 0; a < 4; a++) s += data[row + Math.Clamp(i - 1 + a, 0, last)] * wu[a];
            sum += s * ww[b];
        }
        return sum;
    }

    // Catmull–Rom weights for taps at -1, 0, 1, 2.
    private static void Weights(double t, Span<double> w)
    {
        double t2 = t * t, t3 = t2 * t;
        w[0] = .5 * (-t3 + 2 * t2 - t);
        w[1] = .5 * (3 * t3 - 5 * t2 + 2);
        w[2] = .5 * (-3 * t3 + 4 * t2 + t);
        w[3] = .5 * (t3 - t2);
    }

    private static void Derivatives(double t, Span<double> w)
    {
        double t2 = t * t;
        w[0] = .5 * (-3 * t2 + 4 * t - 1);
        w[1] = .5 * (9 * t2 - 10 * t);
        w[2] = .5 * (-9 * t2 + 8 * t + 1);
        w[3] = .5 * (3 * t2 - 2 * t);
    }
}
