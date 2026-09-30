using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;

namespace Thornwake.Core;

public enum MacroMapLayer { Elevation, LandOwners, MountainRanges, DrainageRoutes, DrainageCatchments, DrainageBasins, RegionalRefinement }
public sealed record MacroMapResult(int Resolution,byte[] Pixels,double LandFraction,int SampledLandComponents,double MinimumHeight,double MaximumHeight,double Milliseconds);

/// <summary>Bounded diagnostic raster. Never becomes terrain or hydrology authority.</summary>
public static class MacroWorldMap
{
    public static MacroMapResult Build(MacroWorldGenerator world,double centerX,double centerZ,double span,int resolution,
        MacroMapLayer layer=MacroMapLayer.Elevation,CancellationToken cancellation=default)
    {
        if(resolution is <64 or >1024 || !double.IsFinite(span) || span<16 || span>world.Manifest.Profile.WorldSize*2 || !Enum.IsDefined(layer) || layer>MacroMapLayer.MountainRanges)
            throw new ArgumentOutOfRangeException(nameof(resolution));
        _=WorldRegion.At(centerX-span*.5,centerZ-span*.5); _=WorldRegion.At(centerX+span*.5,centerZ+span*.5);
        var watch=Stopwatch.StartNew(); int count=resolution*resolution,land=0;
        var values=new MacroTerrainSample[count]; double min=double.MaxValue,max=double.MinValue;
        double sea=world.Manifest.Profile.SeaLevel;
        for(int z=0;z<resolution;z++)
        {
            cancellation.ThrowIfCancellationRequested();
            for(int x=0;x<resolution;x++)
            {
                var sample=world.Sample(centerX+((x+.5)/resolution-.5)*span,centerZ+((z+.5)/resolution-.5)*span);
                values[z*resolution+x]=sample; if(sample.Elevation>sea) land++;
                min=Math.Min(min,sample.Elevation); max=Math.Max(max,sample.Elevation);
            }
        }
        var pixels=new byte[count*4]; var owners=world.LandFeatures.Select((f,i)=>(f.Id,i)).ToDictionary(f=>f.Id,f=>f.i);
        (double R,double G,double B) Mix((double R,double G,double B) a,(double R,double G,double B) b,double t)
        { t=Math.Clamp(t,0,1); return(a.R+(b.R-a.R)*t,a.G+(b.G-a.G)*t,a.B+(b.B-a.B)*t); }
        for(int z=0;z<resolution;z++)
        {
            cancellation.ThrowIfCancellationRequested();
            for(int x=0;x<resolution;x++)
            {
                int i=z*resolution+x; var sample=values[i]; double h=sample.Elevation-sea;
                (double R,double G,double B) color;
                if(h<=0) color=Mix((.035,.105,.18),(.16,.43,.49),1-Math.Min(1,-h/world.Manifest.Profile.OceanDepth));
                else if(layer==MacroMapLayer.LandOwners && sample.LandId!=null)
                {
                    int owner=owners[sample.LandId];
                    color=(.3+((owner*73+31)%151)/255.0,.3+((owner*47+99)%151)/255.0,.3+((owner*89+11)%151)/255.0);
                }
                else if(layer==MacroMapLayer.MountainRanges) color=Mix((.17,.28,.27),(.98,.64,.26),sample.MountainInfluence);
                else
                {
                    color=h<700?Mix((.49,.66,.43),(.61,.58,.40),h/700):
                        h<2200?Mix((.61,.58,.40),(.53,.54,.53),(h-700)/1500):Mix((.53,.54,.53),(.94,.95,.93),(h-2200)/2200);
                    double dx=values[z*resolution+Math.Min(x+1,resolution-1)].Elevation-values[z*resolution+Math.Max(x-1,0)].Elevation;
                    double dz=values[Math.Min(z+1,resolution-1)*resolution+x].Elevation-values[Math.Max(z-1,0)*resolution+x].Elevation;
                    double shade=Math.Clamp(1+(dx+dz)/(span/resolution)*.65,.55,1.22);
                    color=(color.R*shade,color.G*shade,color.B*shade);
                }
                pixels[i*4]=(byte)Math.Clamp(color.R*255,0,255); pixels[i*4+1]=(byte)Math.Clamp(color.G*255,0,255);
                pixels[i*4+2]=(byte)Math.Clamp(color.B*255,0,255); pixels[i*4+3]=255;
            }
        }
        // This count is explicitly resolution-dependent; tiny islands/straits may be unresolved.
        var seen=new bool[count]; var queue=new Queue<int>(); int components=0;
        for(int i=0;i<count;i++)
        {
            if(i%resolution==0) cancellation.ThrowIfCancellationRequested();
            if(seen[i] || values[i].Elevation<=sea) continue;
            components++; queue.Enqueue(i); seen[i]=true;
            while(queue.TryDequeue(out int a))
            {
                int x=a%resolution,z=a/resolution;
                void Visit(int n) { if(!seen[n] && values[n].Elevation>sea) { seen[n]=true; queue.Enqueue(n); } }
                if(x>0) Visit(a-1); if(x<resolution-1) Visit(a+1); if(z>0) Visit(a-resolution); if(z<resolution-1) Visit(a+resolution);
            }
        }
        return new(resolution,pixels,(double)land/count,components,min,max,watch.Elapsed.TotalMilliseconds);
    }
}



