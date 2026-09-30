using System;
using System.Collections.Generic;
using System.Numerics;

namespace Thornwake.Core;

/// <summary>The same piecewise-linear tetrahedral field used by the visible cave mesh.</summary>
public sealed class CaveTerrainSampler
{
    private readonly CaveSpikeField _field;
    private readonly Dictionary<(int X, int Y, int Z), float> _values = new();
    private readonly Dictionary<(int X, int Z), float> _heights = new();
    public float Spacing { get; }
    public CaveTerrainSampler(CaveSpikeField field, float spacing)
    {
        if (spacing is not (1 or .5f)) throw new ArgumentOutOfRangeException(nameof(spacing));
        _field = field; Spacing = spacing;
    }
    public float Grid(int x, int y, int z)
    {
        if (!_values.TryGetValue((x,y,z), out float value))
        {
            if (!_heights.TryGetValue((x,z), out float height)) _heights[(x,z)] = height = _field.Surface(x * Spacing, z * Spacing);
            _values[(x,y,z)] = value = _field.Density(new(x * Spacing, y * Spacing, z * Spacing), height);
        }
        return value;
    }
    public float Density(Vector3 point)
    {
        var p = point / Spacing;
        int x = (int)MathF.Floor(p.X), y = (int)MathF.Floor(p.Y), z = (int)MathF.Floor(p.Z);
        Span<(float Fraction, int Axis)> axes = stackalloc (float,int)[] { (p.X-x,0), (p.Y-y,1), (p.Z-z,2) };
        for (int i = 0; i < 2; i++) for (int j = i + 1; j < 3; j++)
            if (axes[j].Fraction > axes[i].Fraction) (axes[i], axes[j]) = (axes[j], axes[i]);
        float value = (1 - axes[0].Fraction) * Grid(x,y,z);
        for (int i = 0; i < 3; i++)
        {
            switch (axes[i].Axis) { case 0: x++; break; case 1: y++; break; case 2: z++; break; }
            float next = i == 2 ? 0 : axes[i+1].Fraction;
            value += (axes[i].Fraction - next) * Grid(x,y,z);
        }
        return value;
    }
    public static readonly (int X,int Y,int Z)[] Corners = { (0,0,0), (1,0,0), (1,1,0), (0,1,0), (0,0,1), (1,0,1), (1,1,1), (0,1,1) };
    public static readonly int[,] Tetrahedra = { {0,5,1,6}, {0,1,2,6}, {0,2,3,6}, {0,3,7,6}, {0,7,4,6}, {0,4,5,6} };
}
