using Godot;
using Thornwake.Core;
namespace Thornwake;
public partial class Main
{
    private bool _refinishing;
    private void ToggleFinishBrush()
    {
        if(_slot!=1||_buildSelection==null||_catalog.BuildingTools[_buildSelection.DefinitionId].Appearances.Count==0)return;
        _refinishing=!_refinishing;_dismantling=false;_heightAnchor=null;_lastPlacementAim=null;
        UpdateGhost();UpdateContext();
        _hud.SetBuildOptions(_catalog,_buildSelection,_rotation,BuildLevel,_refinishing);
    }
    private void ApplyFinishBrush()
    {
        var piece=PointedStructure();
        var selected=piece==null?null:BuildingAppearances.SelectionFor(_catalog,piece);
        if(piece==null||selected==null||selected.DefinitionId!=_buildSelection?.DefinitionId)
        {_hud.Notify("Point at a piece matching the selected Roof or Wall tool.");return;}
        var result=_sim.Refinish(piece.Id,_buildSelection.AppearanceId);Report(result);
        if(result.Success){_valley.RebuildStructures();_sound.Play("wood");}
        _attackCooldown=.3f;
    }
    private void UpdateFinishGhost()
    {
        var piece=PointedStructure();
        var selected=piece==null?null:BuildingAppearances.SelectionFor(_catalog,piece);
        if(piece==null||selected==null||selected.DefinitionId!=_buildSelection?.DefinitionId)
        {if(_ghost!=null)_ghost.Visible=false;_placementHint="Finish brush: point at a matching piece  ·  U chooses finish  ·  Z returns to building";return;}
        var selection=selected;selection.AppearanceId=_buildSelection.AppearanceId;
        EnsureGhost(piece.Kind,selection,RoofGeometry.GableBottom(_state,piece),piece.DoorOpen);
        _ghost.Visible=true;_ghost.Position=new(piece.X,Construction.Base(_state,piece),piece.Z);_ghost.Rotation=new(0,piece.Rotation,0);
        TintGhost(_ghost,_validGhost);
        string name=BuildingAppearances.Find(_catalog,selection)?.Name??"Original material";
        _placementHint=$"Apply {name}  ·  Left click  ·  Cosmetic, no materials used  ·  Z returns to building";
    }
}
