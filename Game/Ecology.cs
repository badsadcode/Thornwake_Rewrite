using Godot;
using System;
using System.Text.Json;
using Thornwake.Core;
using Thornwake.World;

namespace Thornwake;

public partial class Main
{
    private string _plantingHint="";
    private bool IsPlantingVegetation=>_slot==3&&_state.Player.EquippedItem is "pine_sapling" or "bush_seedling";
    private void ClearStump(string id)
    {
        if(_state.Player.Stamina<7){_hud.Notify("Catch your breath before clearing a stump.");return;}
        var result=_sim.RemoveStump(id);Report(result);
        if(result.Success){_state.Player.Stamina-=7;_attackCooldown=.55f;_sound.Play("wood");_valley.RefreshVegetation();UpdateContext();}
    }
    private void UpdatePlantGhost()
    {
        string id=_state.Player.EquippedItem,key="plant:"+id;
        if(_ghost==null||_ghostKind!=key)
        {
            if(_ghost!=null){RemoveChild(_ghost);_ghost.QueueFree();}
            foreach(var materials in _variantGhostMaterials.Values){foreach(var material in materials.Valid)material.Dispose();foreach(var material in materials.Invalid)material.Dispose();}
            _variantGhostMaterials.Clear();_ghost=new Node3D();
            if(id=="pine_sapling")_ghost.AddChild(WoodlandAssets.Tree("Pine_A",.15f));
            else _ghost.AddChild(new MeshInstance3D{Mesh=new SphereMesh{Radius=.28f,Height=.45f,RadialSegments=8,Rings=4},Position=new Vector3(0,.2f,0)});
            AddChild(_ghost);_ghostKind=key;
        }
        var point=MouseWorld();string problem=_sim.PlantingProblem(id,point.X,point.Z);
        _ghost.Visible=true;_ghost.Position=_valley.At(point.X,point.Z);_ghost.Rotation=Vector3.Zero;TintGhost(_ghost,problem==""?_validGhost:_invalidGhost);
        float hours=id=="pine_sapling"?_state.Settings.TreeGrowthHours:_state.Settings.BushGrowthHours;
        _plantingHint=problem==""?$"Left click: plant {_catalog.Items[id].Name.ToLowerInvariant()} · {hours:0.##} active hours to mature":problem;
    }
    private void HandleSandboxCommand(string command)
    {
        var parts=command.Split(':',3);
        if(parts.Length<2)return;
        if(parts[1]=="open")_hud.ShowSandbox(_state);
        else if(parts[1]=="cancel")_hud.ShowPause();
        else if(parts[1]=="apply"&&parts.Length==3)
        {
            try
            {
                var settings=JsonSerializer.Deserialize<SandboxSettings>(parts[2]);
                var result=_sim.UpdateSandbox(settings!);Report(result);
                if(result.Success){_valley.RefreshCrops();_valley.RefreshVegetation();if(!_test)Save(false);_hud.ShowPause();}
            }
            catch(JsonException){_hud.Notify("Those world settings could not be read.");}
        }
        SyncCameraControl();
    }
}
