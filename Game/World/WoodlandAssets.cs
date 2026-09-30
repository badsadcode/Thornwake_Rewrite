using Godot;
using System;
using System.Collections.Generic;
using Thornwake.Core;

namespace Thornwake.World;

/// <summary>Imported assets shared by every tree instance. Gameplay remains on the resource root.</summary>
public static class WoodlandAssets
{
    private static readonly Dictionary<string,PackedScene> Scenes=new();
    private static readonly Dictionary<string,BaseMaterial3D> Bark=new();
    private static readonly Dictionary<string,ShaderMaterial> Foliage=new();
    private static Shader? _canopyShader;
    public static IEnumerable<MeshInstance3D> Meshes(Node node)
    {
        if(node is MeshInstance3D mesh)yield return mesh;
        foreach(Node child in node.GetChildren())foreach(var found in Meshes(child))yield return found;
    }
    private static Node3D Instance(string asset,int lod)
    {
        string name="TREE_"+asset+(lod==0?"":"_LOD"+lod);
        if(!Scenes.TryGetValue(name,out var scene))
        {
            scene=GD.Load<PackedScene>("res://Assets/Vegetation/"+name+".glb")
                ??throw new InvalidOperationException("Missing woodland model: "+name);
            Scenes[name]=scene;
        }
        var root=scene.Instantiate<Node3D>();
        foreach(var mesh in Meshes(root))
        {
            mesh.CastShadow=lod==2?GeometryInstance3D.ShadowCastingSetting.Off:GeometryInstance3D.ShadowCastingSetting.On;
            for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
            {
                if(mesh.GetActiveMaterial(surface) is not BaseMaterial3D material)throw new InvalidOperationException("Missing tree material: "+name);
                // Shared imported material, configured once; no per-tree material duplication.
                material.TextureFilter=BaseMaterial3D.TextureFilterEnum.Nearest;
                if(material.ResourceName.Contains("Bark"))Bark[asset]=material;
                else
                {
                    string key=material.ResourceName;
                    if(!Foliage.TryGetValue(key,out var foliage))
                    {
                        _canopyShader??=GD.Load<Shader>("res://Assets/Vegetation/Canopy.gdshader");
                        foliage=new ShaderMaterial{Shader=_canopyShader,ResourceName=key};
                        foliage.SetShaderParameter("pixel_map",material.AlbedoTexture);
                        foliage.SetShaderParameter("tint",material.AlbedoColor);
                        Foliage[key]=foliage;
                    }
                    mesh.SetSurfaceOverrideMaterial(surface,foliage);
                }
            }
        }
        return root;
    }
    public static WoodlandTree Tree(string asset,float scale=1,float rotation=0)
    {
        var tree=new WoodlandTree{Name="TREE_"+asset,Asset=asset,Scale=Vector3.One*scale,Rotation=new Vector3(0,rotation,0)};
        for(int lod=0;lod<3;lod++){var node=Instance(asset,lod);node.Name="LOD"+lod;tree.AddChild(node);tree.Levels[lod]=node;node.Visible=lod==0;}
        return tree;
    }
    public static Node3D Stump(string asset,float scale=1,float rotation=0)
    {
        // Reuse the species' bark and match the mature trunk footprint; retain a visible cut face.
        if(!Bark.ContainsKey(asset)){var sample=Instance(asset,0);sample.Free();}
        var stump=Visuals.Stump();
        float width=Woodland.TrunkRadius(asset)/.32f;
        stump.Name="Stump_"+asset;stump.Scale=new Vector3(width*scale,scale,width*scale);stump.Rotation=new Vector3(0,rotation,0);
        int index=0;
        foreach(var mesh in Meshes(stump)){if(index++<6)mesh.MaterialOverride=Bark[asset];}
        // An outer wrapper lets growing-plant refresh set scale without losing trunk proportions.
        var root=new Node3D{Name="Stump"};root.AddChild(stump);return root;
    }
    public static void UpdateCanopyFocus(Camera3D camera,Vector3 player)
    {
        Vector3 focus=player+Vector3.Up;
        Vector2 screen=camera.IsPositionBehind(focus)?new Vector2(-10000,-10000):camera.UnprojectPosition(focus);
        Vector2 viewport=camera.GetViewport().GetVisibleRect().Size;
        float radius=Mathf.Clamp(camera.UnprojectPosition(focus+Vector3.Up*1.6f).DistanceTo(screen),34,90);
        foreach(var material in Foliage.Values)
        {
            material.SetShaderParameter("player_world",focus);
            material.SetShaderParameter("player_screen",screen);
            material.SetShaderParameter("viewport_pixels",viewport);
            material.SetShaderParameter("clear_radius",radius);
        }
    }
    public static void Release()
    {
        foreach(var scene in Scenes.Values)scene.Dispose();Scenes.Clear();Bark.Clear();
        foreach(var material in Foliage.Values)material.Dispose();Foliage.Clear();
        _canopyShader?.Dispose();_canopyShader=null;
    }
}

public partial class WoodlandTree : Node3D
{
    public string Asset {get;set;}="";
    public Node3D[] Levels {get;}=new Node3D[3];
    public int CurrentLod {get;private set;}
    public void UpdateDetail(Vector3 cameraPosition)
    {
        // Evaluate distance to the middle of the tree, rather than its ground origin.
        float distance=cameraPosition.DistanceTo(GlobalPosition+Vector3.Up*5*Scale.Y);
        int next=Woodland.Detail(distance,CurrentLod);
        if(next==CurrentLod)return;
        Levels[CurrentLod].Visible=false;Levels[next].Visible=true;CurrentLod=next;
    }
}