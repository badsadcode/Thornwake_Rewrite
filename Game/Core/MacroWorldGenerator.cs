using System;
using System.Collections.Generic;
using System.Linq;

namespace Thornwake.Core;

public readonly record struct MacroPoint(double X,double Z);
public sealed record MacroLandFeature(string Id,WorldRegion Owner,bool Island,MacroPoint Center,double RadiusX,double RadiusZ,double Angle,ulong NoiseSeed);
public sealed record MacroRange(string Id,IReadOnlyList<MacroPoint> Points,double Width,double Height);
public readonly record struct MacroTerrainSample(double Elevation,double LandInfluence,double MountainInfluence,string? LandId);

/// <summary>Version-3 geography with opt-in version-4 mountain morphology. Stateless global queries; no per-stream-region cache or gameplay state.</summary>
public sealed class MacroWorldGenerator
{
    public MacroWorldManifest Manifest { get; }
    public IReadOnlyList<MacroLandFeature> LandFeatures { get; }
    public IReadOnlyList<MacroRange> Ranges { get; }
    private readonly ulong _noise;
    public MacroMountainsV4? ExperimentalMountains { get; }
    public MacroWorldGenerator(MacroWorldManifest manifest)
    {
        manifest.Validate(); Manifest=manifest;
        // Version 4 deliberately retains the v3 geography/hills stream; only mountain morphology is dispatched.
        var p=manifest.Profile; var identity=new GenerationIdentity(manifest.Seed,3,manifest.ProfileDigest);
        _noise=identity.FeatureSeed("relief",new(0,0),0);
        var land=new List<MacroLandFeature>(); var ranges=new List<MacroRange>();

        for(int i=0;i<p.Continents+p.Islands;i++)
        {
            bool island=i>=p.Continents;
            var random=new RandomStream(identity.FeatureSeed(island?"island-shape":"continent-shape",new(0,0),i));
            double size=(island?p.IslandRadius:p.ContinentRadius)*(.8+random.Unit()*.2);
            double x,z;
            if(!island)
            {
                // Bounded rejection separates major cores without an obvious ring/grid layout.
                double limit=p.WorldSize*.46-size*1.3,best=double.NegativeInfinity; x=0; z=0;
                for(int attempt=0;attempt<160;attempt++)
                {
                    double cx=(random.Unit()*2-1)*limit,cz=(random.Unit()*2-1)*limit;
                    double clearance=double.PositiveInfinity;
                    foreach(var other in land)
                    {
                        double dx=cx-(other.Center.X-manifest.OriginX),dz=cz-(other.Center.Z-manifest.OriginZ);
                        clearance=Math.Min(clearance,Math.Sqrt(dx*dx+dz*dz)-(size+other.RadiusX)*1.15);
                    }
                    if(clearance>best) { best=clearance; x=cx; z=cz; }
                    if(clearance>=0) break;
                }
            }
            else
            {
                // Independent, bounded island groups make chains/archipelagos rather than uniform confetti.
                int groups=Math.Max(1,Math.Min(12,p.Islands/10));
                var group=new RandomStream(identity.FeatureSeed("island-group",new(0,0),(i-p.Continents)%groups));
                double gx=(group.Unit()-.5)*p.WorldSize*.76,gz=(group.Unit()-.5)*p.WorldSize*.76,angle=group.Unit()*Math.PI*2;
                double along=(random.Unit()-.5)*p.IslandRadius*12,across=(random.Unit()-.5)*p.IslandRadius*4;
                x=Math.Clamp(gx+Math.Cos(angle)*along-Math.Sin(angle)*across,-p.WorldSize*.43,p.WorldSize*.43);
                z=Math.Clamp(gz+Math.Sin(angle)*along+Math.Cos(angle)*across,-p.WorldSize*.43,p.WorldSize*.43);
            }

            var center=new MacroPoint(x+manifest.OriginX,z+manifest.OriginZ); var owner=WorldRegion.At(center.X,center.Z);
            var feature=new MacroLandFeature(identity.FeatureId(island?"island":"continent",owner,i),owner,island,center,
                size,size*(.6+random.Unit()*.3),random.Unit()*Math.PI,random.Next());
            land.Add(feature);
            if(island) continue;
            List<MacroPoint>? trunk=null;
            for(int r=0;r<p.RangesPerContinent;r++)
            {
                var spine=new List<MacroPoint>();
                if(r==0)
                {
                    for(int k=0;k<5;k++)
                    {
                        double along=(-.68+k*.34)*feature.RadiusX,across=(random.Unit()-.5)*.3*feature.RadiusZ;
                        spine.Add(new(center.X+Math.Cos(feature.Angle)*along-Math.Sin(feature.Angle)*across,
                            center.Z+Math.Sin(feature.Angle)*along+Math.Cos(feature.Angle)*across));
                    }
                    trunk=spine;
                }
                else
                {
                    // Branches attach to the trunk instead of repeating parallel equal-length ranges.
                    var start=trunk![1+(r-1)%3];
                    double direction=feature.Angle+(r%2==0?1:-1)*(1+random.Unit()*.5),length=feature.RadiusZ*(.4+random.Unit()*.35);
                    for(int k=0;k<5;k++)
                    {
                        double along=length*k/4,across=k==0?0:(random.Unit()-.5)*length*.15;
                        spine.Add(new(start.X+Math.Cos(direction)*along-Math.Sin(direction)*across,
                            start.Z+Math.Sin(direction)*along+Math.Cos(direction)*across));
                    }
                }
                ranges.Add(new(identity.FeatureId("range",owner,i*6+r),spine.AsReadOnly(),p.MountainWidth*(r==0?1:.65),p.MountainHeight*(.7+random.Unit()*.3)));
            }
        }
        LandFeatures=land.AsReadOnly(); Ranges=ranges.AsReadOnly();
        if(manifest.Algorithm==4) { ExperimentalMountains=new(manifest,LandFeatures); Ranges=ExperimentalMountains.Descriptors; }
    }
    public MacroTerrainSample Sample(double x,double z)
    {
        _=WorldRegion.At(x,z);
        var p=Manifest.Profile; double lx=x-Manifest.OriginX,lz=z-Manifest.OriginZ;
        double edge=(p.WorldSize*.5-Math.Max(Math.Abs(lx),Math.Abs(lz)))/(p.WorldSize*.06);
        if(edge<=0) return new(p.SeaLevel-p.OceanDepth,-1,0,null);
        double land=-1; string? owner=null;
        foreach(var feature in LandFeatures)
        {
            double dx=x-feature.Center.X,dz=z-feature.Center.Z;
            if(Math.Abs(dx)>feature.RadiusX*3 || Math.Abs(dz)>feature.RadiusX*3) continue;
            double scale=feature.RadiusX;
            double wx=dx+Noise(dx/(scale*.7),dz/(scale*.7),feature.NoiseSeed)*scale*p.CoastRoughness*.5;
            double wz=dz+Noise(dx/(scale*.7)+31.7,dz/(scale*.7)-18.3,feature.NoiseSeed)*scale*p.CoastRoughness*.5;
            double a=(Math.Cos(feature.Angle)*wx+Math.Sin(feature.Angle)*wz)/feature.RadiusX;
            double b=(-Math.Sin(feature.Angle)*wx+Math.Cos(feature.Angle)*wz)/feature.RadiusZ;
            double coast=(Noise(dx/(scale*.28),dz/(scale*.28),feature.NoiseSeed)*.7+
                Noise(dx/(scale*.09),dz/(scale*.09),feature.NoiseSeed+1)*.3)*p.CoastRoughness;
            double radius=Math.Sqrt(a*a+b*b),theta=Math.Atan2(b,a);
            double phase=(feature.NoiseSeed>>11)*(1.0/9007199254740992.0)*Math.PI*2;
            // Broad lobes create bays and peninsulas before the finer coast noise is applied.
            double lobes=p.CoastRoughness*(.9*Math.Sin(theta*2+phase)+.65*Math.Sin(theta*3-phase)+.4*Math.Sin(theta*5+phase*.7));
            double influence=1-radius+coast+lobes*Smooth(radius/.6)*(feature.Island?.55:1);
            if(influence>land) { land=influence; owner=feature.Id; }
        }
        // A smooth ocean margin is a world boundary policy, not a streaming boundary.
        land=Math.Min(land,edge-.3);
        if(land<=0) return new(p.SeaLevel-p.OceanDepth*Smooth(-land/.6),land,0,null);
        double coastFade=Smooth(land/.18);
        double hills=(Noise(lx/p.HillScale,lz/p.HillScale,_noise)*.7+
            Noise(lx/(p.HillScale*.35),lz/(p.HillScale*.35),_noise+1)*.3)*p.HillHeight;
        double mountain=0;
        if(ExperimentalMountains!=null) mountain=ExperimentalMountains.Sample(lx,lz);
        else foreach(var range in Ranges)
        {
            double distance=double.MaxValue;
            for(int k=1;k<range.Points.Count;k++) distance=Math.Min(distance,SegmentDistance(x,z,range.Points[k-1],range.Points[k]));
            if(distance>range.Width*4) continue;
            double ridge=Math.Exp(-Math.Pow(distance/range.Width,2));
            double peaks=.78+.22*Noise(lx/(range.Width*.7),lz/(range.Width*.7),_noise+2);
            mountain=Math.Max(mountain,range.Height*ridge*peaks);
        }
        double height=p.LowlandHeight*Smooth(land/.4)+coastFade*(hills+mountain);
        // Keep macro land connected; hydrology will introduce explicit inland basins later.
        height=Math.Max(p.LowlandHeight*Smooth(land/.08)*.05,height);
        return new(p.SeaLevel+height,land,p.MountainHeight==0?0:mountain/p.MountainHeight,owner);
    }
    private static double SegmentDistance(double x,double z,MacroPoint a,MacroPoint b)
    {
        double dx=b.X-a.X,dz=b.Z-a.Z;
        double t=Math.Clamp(((x-a.X)*dx+(z-a.Z)*dz)/(dx*dx+dz*dz),0,1);
        return Math.Sqrt(Math.Pow(x-a.X-t*dx,2)+Math.Pow(z-a.Z-t*dz,2));
    }
    private static double Smooth(double t) { t=Math.Clamp(t,0,1); return t*t*t*(t*(t*6-15)+10); }
    private static double Noise(double x,double z,ulong seed)
    {
        long ix=(long)Math.Floor(x),iz=(long)Math.Floor(z); double u=Smooth(x-ix),v=Smooth(z-iz);
        double Value(long a,long b)=>((Mix(unchecked((ulong)a*0x9E3779B97F4A7C15UL^(ulong)b*0xD1B54A32D192ED03UL)^seed)>>11)*(1.0/9007199254740992.0))*2-1;
        double low=Value(ix,iz)*(1-u)+Value(ix+1,iz)*u,high=Value(ix,iz+1)*(1-u)+Value(ix+1,iz+1)*u;
        return low*(1-v)+high*v;
    }
    private static ulong Mix(ulong value)
    {
        value=unchecked((value^(value>>30))*0xBF58476D1CE4E5B9UL);
        value=unchecked((value^(value>>27))*0x94D049BB133111EBUL);
        return value^(value>>31);
    }
    private sealed class RandomStream
    {
        private ulong _state;
        public RandomStream(ulong seed) { _state=seed; }
        public ulong Next() { _state=unchecked(_state+0x9E3779B97F4A7C15UL); return Mix(_state); }
        public double Unit()=>(Next()>>11)*(1.0/9007199254740992.0);
    }
}




