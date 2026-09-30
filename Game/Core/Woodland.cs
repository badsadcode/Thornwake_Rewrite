using System;

namespace Thornwake.Core;

/// <summary>Presentation-only mapping; never consumes the world generator's random stream or changes resource IDs.</summary>
public static class Woodland
{
    public static readonly string[] Assets = {
        "Pine_A","Pine_B","Pine_C","Spruce_A","Spruce_B",
        "Birch_Green_A","Birch_Green_B","Birch_Green_C","Birch_Autumn_A","Birch_Autumn_B",
        "Oak_A","Oak_B","Oak_C","Maple_Autumn_A","Sparse_A","Poplar_A","Willow_A"
    };
    private static readonly float[] Radii = {.28f,.31f,.30f,.32f,.34f,.20f,.19f,.23f,.21f,.24f,.52f,.60f,.55f,.37f,.29f,.27f,.57f};

    // Specified FNV-1a, unlike string.GetHashCode, stays identical after restarting .NET.
    public static uint Hash(string id)
    {
        uint value=2166136261;
        foreach(char c in id)value=unchecked((value^c)*16777619);
        value^=value>>16;value=unchecked(value*0x7feb352d);value^=value>>15;
        return value;
    }
    public static string Natural(ResourceSpawn spawn)
    {
        uint hash=Hash(spawn.Id);
        if(MathF.Abs(spawn.X-27)<12 && hash%4!=0)return "Willow_A";
        // Extra conifers and oak keep the woodland cohesive, with occasional amber-leaf accents.
        int slot=(int)(hash%24);
        return slot<Assets.Length?Assets[slot]:Assets[new[]{0,1,2,3,4,10,11}[slot-Assets.Length]];
    }
    public static string Planted(string id)=>Assets[(int)(Hash(id)%3)];
    public static float Rotation(string id)=>(Hash(id+"-rotation")%65536)/65536f*MathF.Tau;
    public static float NaturalScale(ResourceSpawn spawn)=>.9f+Math.Clamp((spawn.Scale-.75f)/.65f,0,1)*.2f;
    public static float TrunkRadius(string asset)=>Radii[Array.IndexOf(Assets,asset)];
    public static string DisplayName(string asset)=>asset.StartsWith("Birch")?"birch":asset.StartsWith("Maple")?"maple":asset.StartsWith("Sparse")?"woodland tree":asset.StartsWith("Willow")?"willow":asset.Split('_')[0].ToLowerInvariant();
    public static int Detail(float distance,int current)
    {
        // A four-metre dead band prevents flicker when orbiting across a boundary.
        if(current==0)return distance>42?(distance>78?2:1):0;
        if(current==2)return distance<70?(distance<34?0:1):2;
        return distance<34?0:distance>78?2:1;
    }
}