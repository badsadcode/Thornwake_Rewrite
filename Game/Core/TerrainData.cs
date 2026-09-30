using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

namespace Thornwake.Core;

public readonly record struct TerrainRegion(int X,int Z)
{
    public const int Size=16;
    public float MinX=>X*Size;
    public float MinZ=>Z*Size;
    public static TerrainRegion At(float x,float z)=>new((int)MathF.Floor(x/Size),(int)MathF.Floor(z/Size));
}
public enum TerrainSurface { Soil, Sand, Gravel, Rock }
public readonly record struct TerrainSample(float Height,float BaseHeight,float Delta,Vector3 Normal,float SlopeDegrees,float Moisture,float Fertility,float DistanceFromWater,TerrainSurface Surface);

/// <summary>Terrain authority independent of Godot meshes. Version 1 preserves every legacy edit exactly.</summary>
public static class TerrainData
{
    // Query stencil is independent of visual mesh density; a future saved grid gets its own versioned spacing.
    public const float QuerySpacing=1;
    public static float BaseHeight(WorldState state,float x,float z)
    {
        return state.GeneratorVersion switch{1=>WorldGenerator.Height(x,z,state.Seed),2=>LandscapeGenerator.Sample(x,z,state.Seed,state.Generation).Height,_=>throw new InvalidDataException("Unsupported terrain generator version.")};
    }
    public static float Height(WorldState state,float x,float z)
    {
        float h=BaseHeight(state,x,z);
        foreach(var edit in state.TerrainEdits)
        {
            float dx=x-edit.X,dz=z-edit.Z;
            float distance=MathF.Sqrt(dx*dx+dz*dz)/edit.Radius;
            if(distance<1)h+=edit.Delta*(1-distance*distance)*(1-distance*distance);
        }
        return h+TerrainGrid.Offset(state,x,z);
    }
    public static Vector3 Normal(WorldState state,float x,float z)
    {
        float dx=Height(state,x+QuerySpacing,z)-Height(state,x-QuerySpacing,z);
        float dz=Height(state,x,z+QuerySpacing)-Height(state,x,z-QuerySpacing);
        return Vector3.Normalize(new Vector3(-dx,2*QuerySpacing,-dz));
    }
    public static TerrainSurface MaterialAt(WorldState state,float x,float z,float? surfaceHeight=null)
    {
        float basis=BaseHeight(state,x,z),delta=(surfaceHeight??Height(state,x,z))-basis;
        if(delta>0.025f)return TerrainSurface.Soil;
        float dx=BaseHeight(state,x+1,z)-BaseHeight(state,x-1,z),dz=BaseHeight(state,x,z+1)-BaseHeight(state,x,z-1);
        float slope=MathF.Atan(MathF.Sqrt(dx*dx+dz*dz)/2)*180/MathF.PI;
        if(delta<=-1.2f||slope>38)return TerrainSurface.Rock;
        return slope>24?TerrainSurface.Gravel:MathF.Abs(x-27)<6?TerrainSurface.Sand:TerrainSurface.Soil;
    }
    public static TerrainSample Query(WorldState state,float x,float z)
    {
        float height=Height(state,x,z),basis=BaseHeight(state,x,z);
        Vector3 normal=Normal(state,x,z);
        float slope=MathF.Acos(Math.Clamp(normal.Y,-1,1))*180/MathF.PI;
        // Generator 1 has one authored river. This is a query adapter, not simulated hydrology.
        float water=Math.Max(0,MathF.Abs(x-27)-4);
        float moisture=Math.Clamp(1-water/35,.15f,1);
        var material=MaterialAt(state,x,z,height);
        float fertility=material==TerrainSurface.Soil?Math.Clamp(moisture*(1-slope/60),0,1):.1f;
        return new(height,basis,height-basis,normal,slope,moisture,fertility,water,material);
    }
    public static float[,] SampleRegion(WorldState state,TerrainRegion region,float spacing)
    {
        if(!float.IsFinite(spacing)||spacing<.25f||spacing>4||Math.Abs(TerrainRegion.Size/spacing-MathF.Round(TerrainRegion.Size/spacing))>.0001f)
            throw new ArgumentOutOfRangeException(nameof(spacing),"Sample spacing must evenly divide the region.");
        int cells=(int)MathF.Round(TerrainRegion.Size/spacing);var values=new float[cells+1,cells+1];
        for(int x=0;x<=cells;x++)for(int z=0;z<=cells;z++)values[x,z]=Height(state,region.MinX+x*spacing,region.MinZ+z*spacing);
        return values;
    }
    public static IEnumerable<TerrainRegion> Affected(float x,float z,float radius)
    {
        if(!float.IsFinite(x)||!float.IsFinite(z)||!float.IsFinite(radius)||radius<=0||radius>64)throw new ArgumentOutOfRangeException(nameof(radius));
        // Include normal samples and both owners of shared border vertices.
        float halo=radius+QuerySpacing;
        var low=TerrainRegion.At(x-halo,z-halo);var high=TerrainRegion.At(x+halo,z+halo);
        for(int rx=low.X;rx<=high.X;rx++)for(int rz=low.Z;rz<=high.Z;rz++)yield return new(rx,rz);
    }
    public static bool Worked(WorldState state,float x,float z)
    {
        foreach(var edit in state.TerrainEdits)
        {float dx=x-edit.X,dz=z-edit.Z;if(dx*dx+dz*dz<edit.Radius*edit.Radius)return true;}
        return TerrainGrid.Worked(state,x,z);
    }
}