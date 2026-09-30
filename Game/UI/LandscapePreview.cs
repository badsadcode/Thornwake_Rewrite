using Godot;
using Thornwake.Core;

namespace Thornwake.UI;

/// <summary>Height shading sampled from the same saved world used by gameplay.</summary>
public static class TerrainMapImage
{
    public static ImageTexture Create(WorldState world,int size=128)
    {
        using var image=Image.CreateEmpty(size,size,false,Image.Format.Rgba8);
        float step=110f/(size-1);var heights=new float[size+2,size+2];
        for(int x=0;x<size+2;x++)for(int z=0;z<size+2;z++)heights[x,z]=TerrainData.Height(world,-55+(x-1)*step,-55+(z-1)*step);
        for(int x=0;x<size;x++)for(int z=0;z<size;z++)
        {
            float wx=-55+x*step,wz=-55+z*step,h=heights[x+1,z+1];
            float dx=(heights[x+2,z+1]-heights[x,z+1])/(step*2),dz=(heights[x+1,z+2]-heights[x+1,z])/(step*2);
            float light=Mathf.Clamp(.92f+(-dx-dz)*.32f,.55f,1.22f);
            var color=new Color("677951").Lerp(new Color("b6b391"),Mathf.Clamp(h/8,0,1));
            if(Mathf.Abs(wx-27)<4)color=new Color("527e82");
            else if(Mathf.Abs(wx)<1.5f||Mathf.Abs(wz)<1.6f)color=new Color("b6a47b");
            else if(Mathf.Abs(h/1.5f-Mathf.Round(h/1.5f))<.035f&&h>.4f)light*=.82f;
            image.SetPixel(x,z,new Color(color.R*light,color.G*light,color.B*light,1));
        }
        return ImageTexture.CreateFromImage(image);
    }
}
public partial class LandscapePreview : Control
{
    private WorldState? _world;
    private ImageTexture? _terrain;
    private System.Collections.Generic.List<ResourceSpawn> _resources=new();
    public void SetWorld(WorldState world)
    {
        _world=world;var old=_terrain;_terrain=TerrainMapImage.Create(world);old?.Dispose();
        _resources=WorldGenerator.Generate(world);QueueRedraw();
    }
    public int TreeCount=>_resources.FindAll(r=>r.Kind=="tree").Count;
    public override void _Draw()
    {
        if(_terrain==null)return;
        float side=Mathf.Min(Size.X,Size.Y);Vector2 origin=(Size-new Vector2(side,side))*.5f;
        DrawTextureRect(_terrain,new Rect2(origin,new Vector2(side,side)),false);
        Vector2 Map(float x,float z)=>origin+new Vector2(x+55,z+55)*side/110;
        foreach(var r in _resources)if(r.Kind=="tree")DrawCircle(Map(r.X,r.Z),side*.0045f,new Color("314c35"));
        foreach(var p in new[]{new Vector2(-9,-8),new Vector2(9,-9),new Vector2(-11,9),new Vector2(10,10)})DrawRect(new Rect2(Map(p.X-3,p.Y-2.5f),new Vector2(6,5)*side/110),new Color("ebd29d"));
        DrawLine(Map(23,0),Map(31,0),new Color("e7d0a1"),side*.026f);
        DrawCircle(Map(0,8),4,new Color("ffedbe"));
        DrawRect(new Rect2(origin,new Vector2(side,side)),new Color("aeb28c"),false,1);
        DrawString(ThemeDB.FallbackFont,origin+new Vector2(side-22,21),"N",HorizontalAlignment.Left,-1,15,new Color("eee7ca"));
    }
    public override void _ExitTree(){_terrain?.Dispose();_terrain=null;}
}
