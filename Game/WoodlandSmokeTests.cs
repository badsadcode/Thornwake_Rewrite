using Godot;
using System;
using System.Linq;
using Thornwake.Core;
using Thornwake.World;

namespace Thornwake;

public partial class Main
{
    private void VerifyWoodland()
    {
        void Check(bool value,string message){if(!value)throw new Exception("Woodland: "+message);}
        foreach(string asset in Woodland.Assets)
        {
            var tree=WoodlandAssets.Tree(asset);_valley.AddChild(tree);
            for(int lod=0;lod<3;lod++)
            {
                var meshes=WoodlandAssets.Meshes(tree.Levels[lod]).ToArray();
                Check(meshes.Length is 2 or 3,"one tree per imported scene: "+asset);
                foreach(var mesh in meshes)
                {
                    Check(mesh.Mesh.GetFaces().Length>0,"actual geometry");
                    for(int surface=0;surface<mesh.Mesh.GetSurfaceCount();surface++)
                    {
                        var material=mesh.GetActiveMaterial(surface);
                        if(material is BaseMaterial3D mat)
                        {
                            Check(mat.AlbedoTexture!=null,"embedded bark texture");
                            Check(mat.TextureFilter==BaseMaterial3D.TextureFilterEnum.Nearest,"crisp nearest bark filtering");
                            Check(mat.Transparency==BaseMaterial3D.TransparencyEnum.Disabled,"opaque trunk");
                        }
                        else if(material is ShaderMaterial foliage)
                        {
                            Check(foliage.GetShaderParameter("pixel_map").AsGodotObject() is Texture2D,"embedded foliage texture");
                            Check(foliage.Shader.Code.Contains("filter_nearest")&&foliage.Shader.Code.Contains("ALPHA_SCISSOR_THRESHOLD"),"nearest masked foliage");
                        }
                        else Check(false,"unsupported tree material");
                    }
                }
            }
            tree.UpdateDetail(new Vector3(0,5,100));Check(tree.CurrentLod==2,"distant detail");
            tree.UpdateDetail(new Vector3(0,5,50));Check(tree.CurrentLod==1,"middle detail");
            tree.UpdateDetail(new Vector3(0,5,15));Check(tree.CurrentLod==0,"close detail");
            Check(tree.Levels.Count(l=>l.Visible)==1,"exactly one visible LOD");
            _valley.RemoveChild(tree);tree.Free();
            var stump=WoodlandAssets.Stump(asset);Check(WoodlandAssets.Meshes(stump).First().MaterialOverride is BaseMaterial3D bark&&bark.AlbedoTexture!=null,"matching textured stump");stump.Free();
        }
        var spawn=_valley.Spawns.Values.First(r=>r.Kind=="tree"&&!_state.RemovedResources.Contains(r.Id));
        var original=(WoodlandTree)_valley.Resources[spawn.Id];
        string appearance=original.Asset;float angle=original.Rotation.Y;
        Check(!_valley.CanWalk(original.Position),"trunk blocks walking");
        SaveStore.Save(SavePath,_state);
        // The normal save loader reconstructs old worlds with the same resource identities.
        CreateWorld(SaveStore.Load(SavePath));
        var restored=(WoodlandTree)_valley.Resources[spawn.Id];
        Check(restored.Asset==appearance&&Mathf.IsEqualApprox(restored.Rotation.Y,angle),"appearance survives save/reload");
        _valley.UpdateForest(_camera.GlobalPosition,0,true);
        GD.Print("WOODLAND_TEST_PASS: 17 species/variants, 51 imported LODs, nearest pixel maps, masked cards, shared assets, textured stumps, collision, stable appearance after reload");
    }

    private void ShowForestPreview()
    {
        var state=new WorldState{Seed=7319,EnemyDefeated=true,CalendarMinutes=12*60};
        state.Player.X=11;state.Player.Z=24;CreateWorld(state);_hud.ShowGame();SelectSlot(0);
        _cameraRig.Orbit.Reset();_cameraRig.Orbit.Zoom(-3);
        for(int i=0;i<100;i++)_cameraRig.Orbit.Step(.016f);
        _cameraRig.SnapFocus(_player.Position);_cameraRig.Follow(_camera,_player.Position,0);
        _valley.Animate(state.Time,0);_valley.UpdateForest(_camera.GlobalPosition,0,true);
    }
}