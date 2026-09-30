using System;
using System.Collections.Generic;
using System.Numerics;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Disposable samples for ONE nearby region, independent of total world area.</summary>
public sealed class MacroTerrainChunk
{
    public WorldRegion Region { get; }
    public string WorldId { get; }
    public int Spacing { get; }
    public int Side { get; }
    public IReadOnlyList<double> Elevations { get; }
    public IReadOnlyList<Vector3> Normals { get; }
    public long SampleBufferBytes => (long)Side*Side*(sizeof(double)+3*sizeof(float));
    internal MacroTerrainChunk(WorldRegion region,string worldId,int spacing,double[] heights,Vector3[] normals)
    {
        Region=region; WorldId=worldId; Spacing=spacing; Side=WorldRegion.Size/spacing+1;
        Elevations=Array.AsReadOnly(heights); Normals=Array.AsReadOnly(normals);
    }
    public Vector3 Vertex(int x,int z,WorldPoint renderOrigin)
    {
        if(x<0 || z<0 || x>=Side || z>=Side) throw new ArgumentOutOfRangeException(nameof(x));
        var point=new WorldPoint(Region.X*(double)WorldRegion.Size+x*Spacing,Elevations[z*Side+x],Region.Z*(double)WorldRegion.Size+z*Spacing);
        return point.RelativeTo(renderOrigin);
    }
}

public static class MacroRegionSampler
{
    public static MacroTerrainChunk Build(MacroWorldGenerator world,WorldRegion region,int spacing=1,CancellationToken cancellation=default)
    {
        region.Validate(); if(spacing is not (1 or 2 or 4 or 8)) throw new ArgumentOutOfRangeException(nameof(spacing));
        int side=WorldRegion.Size/spacing+1; var heights=new double[side*side]; var normals=new Vector3[heights.Length];
        var origin=region.Origin();
        for(int z=0;z<side;z++)
        {
            cancellation.ThrowIfCancellationRequested();
            for(int x=0;x<side;x++)
            {
                double wx=origin.X+x*spacing,wz=origin.Z+z*spacing;
                heights[z*side+x]=world.Sample(wx,wz).Elevation;
                // Same global derivative on either side of a chunk, independent of sampling density.
                double dx=world.Sample(wx+.5,wz).Elevation-world.Sample(wx-.5,wz).Elevation;
                double dz=world.Sample(wx,wz+.5).Elevation-world.Sample(wx,wz-.5).Elevation;
                normals[z*side+x]=Vector3.Normalize(new((float)-dx,1,(float)-dz));
            }
        }
        return new(region,world.Manifest.WorldId,spacing,heights,normals);
    }
}
