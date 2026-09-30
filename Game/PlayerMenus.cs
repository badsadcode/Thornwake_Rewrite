using Godot;
using System;
using Thornwake.Core;

namespace Thornwake;

public partial class Main
{
    private void ShowPlayerScreen(string screen)
    {
        switch(screen)
        {
            case "inventory":ShowInventory();break;
            case "craft":ShowCrafting();break;
            case "character":_hud.ShowCharacter(_state,_catalog);break;
            case "skills":_hud.ShowSkills(_state,_catalog);break;
            case "journal":_hud.ShowJournal(_state,_catalog);break;
            default:return;
        }
        _velocity=Vector3.Zero;
        SyncCameraControl();
    }

    private bool HandlePlayerMenuKey(Key key)
    {
        string screen=key switch{Key.I=>"inventory",Key.C=>"craft",Key.P=>"character",Key.K=>"skills",Key.J=>"journal",_=>""};
        if(screen=="")return false;
        bool same=screen=="inventory"&&_hud.IsInventoryOpen||screen=="craft"&&_hud.IsWorkshopOpen||_hud.IsPlayerScreenOpen&&_hud.ActivePlayerScreen==screen;
        if(same){_hud.HidePanel();SyncCameraControl();}else ShowPlayerScreen(screen);
        return true;
    }

    private void ActivateHotbar(int index)
    {
        if(!_started||_hud.IsPanelOpen)return;
        if(_state.CraftJob!=null){_hud.Notify("Finish the current task or cancel it in the crafting menu.");return;}
        var result=_sim.ActivateHotbar(index);
        _hud.Notify(result.Message);
        if(result.Success)
        {
            if(result.Action=="dodge")AnimateDodge();
            else if(result.Action=="place")Command("place:"+result.Id);
            else if(result.Action=="consume")_sound.Play("craft");
            else ApplyEquipment();
        }
        UpdateHud();
    }

    private void UseAbility(string id)
    {
        if(_state.CraftJob!=null){_hud.Notify("Finish the current task before using a skill.");return;}
        var result=_sim.UseAbility(id);
        if(result.Success&&_catalog.Abilities[id].Action=="dodge")AnimateDodge();
        else if(!result.Success)_hud.Notify(result.Message);
        UpdateHud();
    }
    private void AnimateDodge(){_dodge=.32f;_sound.Play("step");}

    private void HandleHotbarCommand(string[] parts)
    {
        if(parts.Length<3||!int.TryParse(parts[2],out int index))return;
        if(parts[1]=="use"){ActivateHotbar(index);return;}
        ActionResult result;
        if(parts[1]=="assign"&&parts.Length==5)result=_sim.AssignHotbar(index,parts[3],parts[4]);
        else if(parts[1]=="swap"&&parts.Length==4&&int.TryParse(parts[3],out int target))result=_sim.SwapHotbar(index,target);
        else if(parts[1]=="clear")result=_sim.ClearHotbar(index);
        else return;
        Report(result);
        if(result.Success&&!_test)Save(false);
    }

    private void UseInventoryItem(string id)
    {
        if(!_catalog.Items.TryGetValue(id,out var item))return;
        if(item.UseAction=="consume")Report(_sim.ConsumeItem(id));
        else if(item.UseAction=="place"){if(_sim.Count(id)>0)Command("place:"+id);return;}
        else
        {
            var result=_sim.EquipItem(id);Report(result);
            if(result.Success)ApplyEquipment();
        }
        ShowInventory();
    }
}
