using System.Numerics;
using Thornwake.Core;

internal static class CaveBrushTests
{
    public static int Run()
    {
        int checks=0;
        void Check(bool ok,string why) { if(!ok) throw new Exception("CAVE BRUSH: "+why); checks++; }
        var cut=new CaveCut(0,-2,0,1.5f,0,-2,3);
        Check(cut.Valid,"continuous stroke valid");
        Check(!new CaveCut(0,0,0,1.5f,0,null,1).Valid,"partial stroke endpoint rejected");
        Check(!new CaveCut(0,0,0,1.5f,0,0,10).Valid,"unbounded stroke rejected");
        var state=new CaveSpikeState(); var field=new CaveSpikeField(state);
        Check(field.TryCut(cut),"continuous stroke accepted");
        for(float z=0;z<=3;z+=.1f) Check(field.Density(new(0,-2,z))< -1,"continuous channel has no ridges between end caps");
        var hard=new CaveSpikeField(new CaveSpikeState { Version=1, Cuts=new(){new(0,-2,0,1.5f),new(0,-2,2.7f,1.5f)} });
        var smooth=new CaveSpikeField(new CaveSpikeState { Cuts=hard.State.Cuts.ToList() });
        for(float x=-2;x<=2;x+=.2f) for(float z=-2;z<=5;z+=.2f)
        {
            var p=new Vector3(x,-1,z); float delta=hard.Density(p)-smooth.Density(p);
            Check(delta>=-.00001f && delta<=.20001f,"brush only removes material within its bounded blend");
        }
        var reversed=new CaveSpikeField(new CaveSpikeState { Cuts=smooth.State.Cuts.AsEnumerable().Reverse().ToList() });
        Check(reversed.Density(new(0,-1,1.35f))==smooth.Density(new(0,-1,1.35f)),"blend is independent of cut order");
        string json=System.Text.Json.JsonSerializer.Serialize(state);
        var loaded=System.Text.Json.JsonSerializer.Deserialize<CaveSpikeState>(json)!; loaded.Validate();
        Check(new CaveSpikeField(loaded).Density(new(0,-2,1.5f))==field.Density(new(0,-2,1.5f)),"stroke shape survives JSON round trip");
        return checks;
    }
}
