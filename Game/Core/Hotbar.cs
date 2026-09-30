using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Thornwake.Core;

public sealed class HotbarBinding
{
    public string Kind { get; set; } = "item";
    public string Id { get; set; } = "";
    public HotbarBinding Copy()=>new(){Kind=Kind,Id=Id};
}
public sealed class AbilityDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Action { get; set; } = "dodge";
    public float StaminaCost { get; set; } = 24;
    public float Cooldown { get; set; } = 1;
}
public readonly record struct HotbarUseResult(bool Success,string Message,string Action,string Id);

public static class HotbarRules
{
    public const int SlotCount=10;
    public static readonly string[] StarterItems={"basic_axe","hammer","hoe","basic_sword"};
    public static List<HotbarBinding?> DefaultBindings()=>new()
    {
        new(){Id="basic_axe"},new(){Id="hammer"},new(){Id="hoe"},new(){Id="seeds"},new(){Id="basic_sword"},
        new(){Id="berries"},null,new(){Kind="skill",Id="dodge"},null,null
    };
    public static int ToolMode(ContentCatalog catalog,string itemId)=>catalog.Items.TryGetValue(itemId??"",out var item)?item.UseAction switch{"axe"=>0,"hammer"=>1,"hoe"=>2,"seeds"=>3,"sword"=>4,"shovel"=>5,_=>-1}:-1;
    public static string CooldownKey(string kind,string id)=>kind+":"+id;
    public static float Remaining(WorldState state,string kind,string id)=>state.Player.Cooldowns.GetValueOrDefault(CooldownKey(kind,id));
    public static bool KnownBinding(ContentCatalog catalog,string kind,string id)=>kind=="item"?catalog.Items.TryGetValue(id??"",out var item)&&item.UseAction!="":kind=="skill"&&catalog.Abilities.ContainsKey(id??"");
    public static bool CanBind(WorldState state,ContentCatalog catalog,string kind,string id)=>KnownBinding(catalog,kind,id)&&(kind=="skill"||state.Player.Inventory.GetValueOrDefault(id)>0);
    public static bool Available(WorldState state,ContentCatalog catalog,HotbarBinding? binding)=>binding!=null&&CanBind(state,catalog,binding.Kind,binding.Id);
    public static bool KnownCooldown(ContentCatalog catalog,string key)
    {
        int split=key?.IndexOf(':')??-1;if(split<=0||split==key!.Length-1)return false;string kind=key[..split],id=key[(split+1)..];
        return kind=="item"?catalog.Items.TryGetValue(id,out var item)&&item.UseAction=="consume"&&item.Cooldown>0:kind=="skill"&&catalog.Abilities.TryGetValue(id,out var ability)&&ability.Cooldown>0;
    }
    public static void AddStarterItems(PlayerState player)
    {foreach(string id in StarterItems)player.Inventory[id]=Math.Max(1,player.Inventory.GetValueOrDefault(id));}
    public static void Validate(WorldState state,ContentCatalog catalog)
    {
        var player=state.Player;
        if(player.Hotbar==null||player.Hotbar.Count!=SlotCount||player.Hotbar.Any(b=>b!=null&&!KnownBinding(catalog,b.Kind,b.Id)))throw new InvalidDataException("The hotbar contains an unavailable item or skill.");
        if(player.EquippedItem==null||player.EquippedItem!=""&&ToolMode(catalog,player.EquippedItem)<0)throw new InvalidDataException("The equipped item is not an available tool or weapon.");
        if(player.Cooldowns==null||player.Cooldowns.Any(c=>!KnownCooldown(catalog,c.Key)||!float.IsFinite(c.Value)||c.Value<0||c.Value>86400))throw new InvalidDataException("An item or skill cooldown is invalid.");
        if(player.EquippedItem!=""&&player.Inventory.GetValueOrDefault(player.EquippedItem)<=0)player.EquippedItem="";
    }
}

public sealed partial class Simulation
{
    public ActionResult AssignHotbar(int index,string kind,string id)
    {
        if(index<0||index>=HotbarRules.SlotCount)return Fail("Choose a hotbar slot from 1 to 0.");
        if(!HotbarRules.KnownBinding(Catalog,kind,id))return Fail("Only usable items and available skills can go on the hotbar.");
        if(!HotbarRules.CanBind(State,Catalog,kind,id))return Fail("You do not have that item in your inventory.");
        State.Player.Hotbar[index]=new(){Kind=kind,Id=id};return new(true,"Hotbar updated.");
    }
    public ActionResult SwapHotbar(int source,int target)
    {
        if(source<0||target<0||source>=HotbarRules.SlotCount||target>=HotbarRules.SlotCount)return Fail("Choose two hotbar slots.");
        if(State.Player.Hotbar[source]==null)return Fail("That hotbar slot is empty.");
        (State.Player.Hotbar[source],State.Player.Hotbar[target])=(State.Player.Hotbar[target],State.Player.Hotbar[source]);return new(true,"Hotbar updated.");
    }
    public ActionResult ClearHotbar(int index)
    {
        if(index<0||index>=HotbarRules.SlotCount)return Fail("Choose a hotbar slot.");
        State.Player.Hotbar[index]=null;return new(true,"Hotbar slot cleared.");
    }
    public ActionResult EquipItem(string id)
    {
        if(id==""){State.EarthworkJob=null;State.Player.EquippedItem="";return new(true,"Hands free.");}
        if(HotbarRules.ToolMode(Catalog,id)<0)return Fail("That item cannot be equipped as a tool or weapon.");
        if(Count(id)<=0)return Fail("You do not have that item in your inventory.");
        if(id!="shovel")State.EarthworkJob=null;State.Player.EquippedItem=id;return new(true,"Equipped "+Catalog.Items[id].Name.ToLowerInvariant()+".");
    }
    public HotbarUseResult ActivateHotbar(int index)
    {
        if(index<0||index>=HotbarRules.SlotCount||State.Player.Hotbar[index] is not {} binding)return new(false,"That hotbar slot is empty.","","");
        if(!HotbarRules.Available(State,Catalog,binding))return new(false,"That item or skill is unavailable.","",binding.Id);
        if(binding.Kind=="skill")
        {var result=UseAbility(binding.Id);return new(result.Success,result.Message,result.Success?Catalog.Abilities[binding.Id].Action:"",binding.Id);}
        var item=Catalog.Items[binding.Id];
        if(item.UseAction=="consume")
        {var result=ConsumeItem(item.Id);return new(result.Success,result.Message,result.Success?"consume":"",item.Id);}
        if(item.UseAction=="place")return new(true,"Choose where to place "+item.Name.ToLowerInvariant()+".","place",item.Id);
        var equipped=EquipItem(item.Id);return new(equipped.Success,equipped.Message,equipped.Success?item.UseAction:"",item.Id);
    }
    public ActionResult ConsumeItem(string id)
    {
        if(!Catalog.Items.TryGetValue(id??"",out var item)||item.UseAction!="consume")return Fail("That item cannot be consumed.");
        if(Count(item.Id)<=0)return Fail("You do not have any "+item.Name.ToLowerInvariant()+".");
        if(HotbarRules.Remaining(State,"item",item.Id)>0)return Fail("Wait for "+item.Name.ToLowerInvariant()+" to be ready again.");
        bool health=item.RestoreHealth>0&&State.Player.Health<100,stamina=item.RestoreStamina>0&&State.Player.Stamina<100;
        if(!health&&!stamina)return Fail("You do not need this item right now.");
        Add(item.Id,-1);State.Player.Health=Math.Min(100,State.Player.Health+item.RestoreHealth);State.Player.Stamina=Math.Min(100,State.Player.Stamina+item.RestoreStamina);
        if(item.Cooldown>0)State.Player.Cooldowns[HotbarRules.CooldownKey("item",item.Id)]=item.Cooldown;
        return Done("Used "+item.Name.ToLowerInvariant()+".");
    }
    public ActionResult UseAbility(string id)
    {
        if(!Catalog.Abilities.TryGetValue(id??"",out var ability))return Fail("That skill is unavailable.");
        if(HotbarRules.Remaining(State,"skill",ability.Id)>0)return Fail(ability.Name+" is not ready yet.");
        if(State.Player.Stamina<ability.StaminaCost)return Fail("You need more stamina to "+ability.Name.ToLowerInvariant()+".");
        State.Player.Stamina-=ability.StaminaCost;
        if(ability.Cooldown>0)State.Player.Cooldowns[HotbarRules.CooldownKey("skill",ability.Id)]=ability.Cooldown;
        return new(true,ability.Name+".");
    }
    private void TickCooldowns(float seconds)
    {
        foreach(var key in State.Player.Cooldowns.Keys.ToArray())
        {float remaining=State.Player.Cooldowns[key]-seconds;if(remaining<=0)State.Player.Cooldowns.Remove(key);else State.Player.Cooldowns[key]=remaining;}
    }
}
