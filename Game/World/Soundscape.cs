using Godot;
using System;

namespace Thornwake.World;

/// <summary>Small original synthesized sound palette; no external audio dependencies.</summary>
public partial class Soundscape : Node
{
    public bool Enabled=true;
    private readonly Random _random=new(91);
    public void Play(string kind)
    {
        if(!Enabled) return;
        int rate=22050;float duration=kind=="craft"?.48f:.22f;int count=(int)(rate*duration);byte[] data=new byte[count*2];
        float frequency=kind switch {"hit"=>95,"wood"=>160,"stone"=>730,"craft"=>520,"step"=>70,"hurt"=>65,_=>360};
        for(int i=0;i<count;i++) {float t=(float)i/rate;float env=Mathf.Exp(-t*(kind=="craft"?8:23));float signal=Mathf.Sin(t*Mathf.Tau*frequency)*(kind=="craft"?.7f:.4f)+(float)(_random.NextDouble()*2-1)*(kind=="craft"?.03f:.45f); short value=(short)(signal*env*8000);data[i*2]=(byte)(value&255);data[i*2+1]=(byte)((value>>8)&255);}
        var p=new AudioStreamPlayer{Stream=new AudioStreamWav{Data=data,Format=AudioStreamWav.FormatEnum.Format16Bits,MixRate=rate},VolumeDb=kind=="step"?-18:-7};AddChild(p);p.Finished+=()=>p.QueueFree();p.Play();
    }
}
