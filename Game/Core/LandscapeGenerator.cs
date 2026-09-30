using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Thornwake.Core;

public sealed class GenerationSettings
{
    public int Version {get;set;}=1;
    public float Relief {get;set;}=1;
    public float Roughness {get;set;}=.45f;
    public float ForestDensity {get;set;}=.85f;
    public GenerationSettings Copy()=>(GenerationSettings)MemberwiseClone();
    public void Validate()
    {
        bool Range(float x,float lo,float hi)=>float.IsFinite(x)&&x>=lo&&x<=hi;
        if(Version!=1||!Range(Relief,.4f,1.6f)||!Range(Roughness,0,1)||!Range(ForestDensity,.4f,1))throw new InvalidDataException("Unsupported landscape settings.");
    }
}
public readonly record struct LandscapeStages(float Macro,float Shaped,float Prepared,float Height);

/// <summary>Generator 2: pure, independently inspectable passes. Generator 1 remains untouched.</summary>
public static class LandscapeGenerator
{
    public static WorldState Create(int seed,GenerationSettings generation,SandboxSettings rules)
    {
        generation.Validate();rules.Validate();
        return new WorldState{Seed=seed,GeneratorVersion=2,Generation=generation.Copy(),Settings=rules.Copy()};
    }
    public static void Validate(WorldState state)
    {
        if(state.GeneratorVersion is not (1 or 2)||state.Generation==null)throw new InvalidDataException("Unsupported landscape generator.");
        state.Generation.Validate();
    }
    public static float Hash(int seed,int x,int z,int salt=0)
    {
        unchecked
        {
            uint n=(uint)seed^(uint)x*0x9E3779B9u^(uint)z*0x85EBCA6Bu^(uint)salt*0xC2B2AE35u;
            n^=n>>16;n*=0x7FEB352Du;n^=n>>15;n*=0x846CA68Bu;n^=n>>16;
            return (n&0xFFFFFF)/16777216f;
        }
    }
    private static float Smooth(float t){t=Math.Clamp(t,0,1);return t*t*(3-2*t);}
    private static float Mix(float a,float b,float t)=>a+(b-a)*t;
    private static float Noise(float x,float z,int seed,int salt)
    {
        int ix=(int)MathF.Floor(x),iz=(int)MathF.Floor(z);float tx=Smooth(x-ix),tz=Smooth(z-iz);
        return Mix(Mix(Hash(seed,ix,iz,salt),Hash(seed,ix+1,iz,salt),tx),Mix(Hash(seed,ix,iz+1,salt),Hash(seed,ix+1,iz+1,salt),tx),tz);
    }
    public static float Macro(float x,float z,int seed,GenerationSettings settings)
    {
        float side=Hash(seed,0,0,1)<.5f?-1:1;
        float ridgeX=-36+Hash(seed,0,0,2)*9,ridgeZ=side*(29+Hash(seed,0,0,3)*9);
        float dx=x-ridgeX,dz=z-ridgeZ,angle=(Hash(seed,0,0,4)-.5f)*.8f;
        float along=dx*MathF.Sin(angle)+dz*MathF.Cos(angle),across=dx*MathF.Cos(angle)-dz*MathF.Sin(angle);
        float ridge=6*MathF.Exp(-(across*across/190+along*along/750));
        float plateauX=-33+Hash(seed,0,0,5)*6,plateauZ=-side*(32+Hash(seed,0,0,6)*6);
        float distance=MathF.Sqrt((x-plateauX)*(x-plateauX)+(z-plateauZ)*(z-plateauZ));
        float plateau=4.3f*(1-Smooth((distance-6)/18));
        float east=3.5f*MathF.Exp(-((x-47)*(x-47)/360+(z+side*31)*(z+side*31)/540));
        return settings.Relief*(.25f+1.1f*Noise(x/65,z/65,seed,8)+ridge+plateau+east);
    }
    public static float Shape(float x,float z,int seed,GenerationSettings settings,float macro)
    {
        // Broad drainage swales interrupt ridges; this is analytic shaping, not simulated erosion.
        float channel=-34+MathF.Sin(z*.045f+Hash(seed,0,0,9)*6)*11;
        float valley=MathF.Exp(-(x-channel)*(x-channel)/28)*.55f;
        float detail=(Noise(x/9,z/9,seed,10)-.5f)*settings.Roughness*.5f;
        return Math.Max(.08f,macro*(1-valley)+detail);
    }
    private static float Outside(float x,float z,float minX,float minZ,float maxX,float maxZ)
    {
        float dx=Math.Max(0,Math.Max(minX-x,x-maxX)),dz=Math.Max(0,Math.Max(minZ-z,z-maxZ));
        return MathF.Sqrt(dx*dx+dz*dz);
    }
    public static float PrepareSites(float x,float z,float shaped)
    {
        float village=Smooth(Outside(x,z,-17,-16,17,16)/9);
        float fields=Smooth(Outside(x,z,14,5,23,15)/6);
        float road=Smooth((Math.Min(Math.Abs(x),Math.Abs(z))-2)/8);
        float camp=Smooth((MathF.Sqrt((x+29)*(x+29)+(z+23)*(z+23))-6)/7);
        return shaped*Math.Min(Math.Min(village,fields),Math.Min(road,camp));
    }
    public static float River(float x,float prepared)
    {
        float distance=Math.Abs(x-27);
        return prepared*Smooth((distance-3.7f)/5)-1.6f*MathF.Exp(-MathF.Pow((x-27)/3.2f,2));
    }
    public static LandscapeStages Sample(float x,float z,int seed,GenerationSettings settings)
    {
        float macro=Macro(x,z,seed,settings),shaped=Shape(x,z,seed,settings,macro),prepared=PrepareSites(x,z,shaped);
        return new(macro,shaped,prepared,River(x,prepared));
    }
    public static List<ResourceSpawn> Resources(WorldState state)
    {
        // Keep IDs from the stable candidate pass; filtering never renumbers later candidates.
        return WorldGenerator.Generate(state.Seed).Where(r=>
        {
            if(r.Kind=="tree"&&Hash(state.Seed,(int)(r.X*100),(int)(r.Z*100),11)>state.Generation.ForestDensity)return false;
            float dx=Height(r.X+1,r.Z)-Height(r.X-1,r.Z),dz=Height(r.X,r.Z+1)-Height(r.X,r.Z-1);
            return r.Kind=="stone"||dx*dx+dz*dz<2.5f;
        }).ToList();
        float Height(float x,float z)=>Sample(x,z,state.Seed,state.Generation).Height;
    }
}
