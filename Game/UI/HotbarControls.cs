using Godot;
using System;
using System.Collections.Generic;
using Thornwake.Core;

namespace Thornwake.UI;

/// <summary>Shared native drag source for inventory, abilities and quick-access slots.</summary>
public partial class HotbarDragButton : Button
{
    public string BindingKind { get; set; } = "item";
    public string BindingId { get; set; } = "";
    public int SourceSlot { get; set; } = -1;
    public bool DragAllowed { get; set; } = true;
    public Func<Godot.Collections.Dictionary,bool>? ValidateDrop { get; set; }
    public Action<Godot.Collections.Dictionary>? ReceiveDrop { get; set; }
    public override Variant _GetDragData(Vector2 atPosition)
    {
        if(!DragAllowed||string.IsNullOrEmpty(BindingId))return default;
        var preview=new PanelContainer{CustomMinimumSize=new Vector2(68,68),MouseFilter=MouseFilterEnum.Ignore};
        preview.AddThemeStyleboxOverride("panel",new StyleBoxFlat{BgColor=new Color("34402de8"),BorderColor=new Color("c5ad73"),BorderWidthLeft=1,BorderWidthRight=1,BorderWidthTop=1,BorderWidthBottom=1});
        preview.AddChild(new ItemIcon{Item=BindingId,CustomMinimumSize=new Vector2(60,60)});SetDragPreview(preview);
        return new Godot.Collections.Dictionary{{"kind",BindingKind},{"id",BindingId},{"source",SourceSlot}};
    }
    public override bool _CanDropData(Vector2 atPosition,Variant data)
    {
        if(data.VariantType!=Variant.Type.Dictionary)return false;
        var map=data.AsGodotDictionary();
        return map.ContainsKey("kind")&&map.ContainsKey("id")&&map.ContainsKey("source")&&ValidateDrop?.Invoke(map)==true;
    }
    public override void _DropData(Vector2 atPosition,Variant data)
    {if(_CanDropData(atPosition,data))ReceiveDrop?.Invoke(data.AsGodotDictionary());}
}

public partial class GameHud
{
    private sealed record HotbarView(int Index,bool Editor,HotbarDragButton Button,ItemIcon Icon,Label Count,Label Cooldown,ColorRect Shade)
    {public bool? LastEquipped {get;set;}}
    private readonly List<HotbarView> _hotbarViews=new();
    private WorldState? _hotbarState;
    private ContentCatalog? _hotbarCatalog;

    private void BuildHudHotbar()
    {
        var row=MakeHotbarRow(false);Place(row,.5f,1,-391,-112,782,76);_hud.AddChild(row);
        var navigation=new HBoxContainer();navigation.AddThemeConstantOverride("separation",6);
        Place(navigation,.5f,1,-269,-146,538,28);_hud.AddChild(navigation);
        foreach(var entry in new[]{("inventory","Satchel  [I]"),("character","Character  [P]"),("skills","Skills  [K]"),("journal","Journal  [J]")})
        {
            string screen=entry.Item1;
            var button=new Button{Name="PlayerNav_"+screen,Text=entry.Item2,CustomMinimumSize=new Vector2(130,28),FocusMode=Control.FocusModeEnum.None};button.AddThemeFontSizeOverride("font_size",12);
            foreach(string style in new[]{"normal","hover","pressed","focus"})
            {var box=Box(style=="normal"?new Color("1c2722ef"):new Color("38452f"),style=="normal"?new Color("566148"):Gold);box.ContentMarginTop=box.ContentMarginBottom=4;box.ContentMarginLeft=box.ContentMarginRight=7;button.AddThemeStyleboxOverride(style,box);}
            button.Pressed+=()=>Command?.Invoke("screen:"+screen);navigation.AddChild(button);
        }
    }
    public Control CreateHotbarEditor(WorldState state,ContentCatalog catalog)
    {
        _hotbarState=state;_hotbarCatalog=catalog;
        var panel=Card(0,115);panel.CustomMinimumSize=new Vector2(0,115);
        var stack=Stack(panel,13,9,6);
        var heading=new HBoxContainer();var title=Text("QUICK ACCESS",11,Gold);heading.AddChild(title);
        var help=Text("Drag items or skills here · Drag slots to reorder · Right-click to clear",11,Muted);help.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;help.HorizontalAlignment=HorizontalAlignment.Right;heading.AddChild(help);stack.AddChild(heading);
        stack.AddChild(MakeHotbarRow(true));UpdateHotbar(state,catalog);return panel;
    }
    private HBoxContainer MakeHotbarRow(bool editor)
    {
        var row=new HBoxContainer{MouseFilter=Control.MouseFilterEnum.Ignore};row.AddThemeConstantOverride("separation",6);
        for(int index=0;index<10;index++)
        {
            int slot=index;
            var button=new HotbarDragButton{Name=(editor?"HotbarEditor_":"HotbarSlot_")+slot,SourceSlot=slot,CustomMinimumSize=new Vector2(72,76),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,FocusMode=Control.FocusModeEnum.None};
            button.AddThemeStyleboxOverride("normal",Box(new Color("1c2722ef"),new Color("566148")));
            button.Pressed+=()=>{if(!editor)Command?.Invoke("hotbar:use:"+slot);};
            button.GuiInput+=input=>{if(input is InputEventMouseButton{ButtonIndex:MouseButton.Right,Pressed:true}){button.AcceptEvent();Command?.Invoke("hotbar:clear:"+slot);}};
            button.ValidateDrop=data=>CanDropHotbar(data);
            button.ReceiveDrop=data=>
            {
                int source=data["source"].AsInt32();
                Command?.Invoke(source>=0?$"hotbar:swap:{source}:{slot}":$"hotbar:assign:{slot}:{data["kind"].AsString()}:{data["id"].AsString()}");
            };
            var icon=new ItemIcon{CustomMinimumSize=Vector2.Zero};Full(icon);icon.OffsetLeft=13;icon.OffsetRight=-13;icon.OffsetTop=9;icon.OffsetBottom=-14;button.AddChild(icon);
            var key=Text(index==9?"0":(index+1).ToString(),10,Gold);Place(key,0,0,7,3,20,16);button.AddChild(key);
            var count=Text("",11,Cream);count.HorizontalAlignment=HorizontalAlignment.Right;Place(count,1,1,-49,-19,42,16);button.AddChild(count);
            var shade=Full(new ColorRect{Color=new Color(0,0,0,.50f),MouseFilter=Control.MouseFilterEnum.Ignore});button.AddChild(shade);shade.Hide();
            var cooldown=Text("",18,Cream);Full(cooldown);cooldown.HorizontalAlignment=HorizontalAlignment.Center;cooldown.VerticalAlignment=VerticalAlignment.Center;button.AddChild(cooldown);
            _hotbarViews.Add(new(slot,editor,button,icon,count,cooldown,shade));row.AddChild(button);
        }
        return row;
    }
    private bool CanDropHotbar(Godot.Collections.Dictionary data)
    {
        if(_hotbarState==null||_hotbarCatalog==null)return false;
        if(data["kind"].VariantType!=Variant.Type.String||data["id"].VariantType!=Variant.Type.String||data["source"].VariantType!=Variant.Type.Int)return false;
        string kind=data["kind"].AsString(),id=data["id"].AsString();int source=data["source"].AsInt32();
        if(source>=0)
        {
            if(source>=_hotbarState.Player.Hotbar.Count)return false;
            var existing=_hotbarState.Player.Hotbar[source];return existing!=null&&existing.Kind==kind&&existing.Id==id;
        }
        return source==-1&&HotbarRules.CanBind(_hotbarState,_hotbarCatalog,kind,id);
    }
    public void UpdateHotbar(WorldState state,ContentCatalog catalog)
    {
        _hotbarState=state;_hotbarCatalog=catalog;
        for(int i=_hotbarViews.Count-1;i>=0;i--)
        {
            var view=_hotbarViews[i];
            if(!GodotObject.IsInstanceValid(view.Button)||view.Button.IsQueuedForDeletion()){_hotbarViews.RemoveAt(i);continue;}
            var binding=view.Index<state.Player.Hotbar.Count?state.Player.Hotbar[view.Index]:null;
            view.Button.BindingKind=binding?.Kind??"item";view.Button.BindingId=binding?.Id??"";
            bool available=binding!=null&&HotbarRules.Available(state,catalog,binding);
            bool equipped=binding?.Kind=="item"&&binding.Id==state.Player.EquippedItem;
            string icon=binding?.Id??"";if(view.Icon.Item!=icon){view.Icon.Item=icon;view.Icon.QueueRedraw();}
            view.Icon.Visible=binding!=null;view.Icon.Modulate=available?Colors.White:new Color(1,1,1,.3f);
            view.Count.Text=binding?.Kind=="item"?state.Player.Inventory.GetValueOrDefault(binding.Id).ToString():"";
            float seconds=binding==null?0:state.Player.Cooldowns.GetValueOrDefault(HotbarRules.CooldownKey(binding.Kind,binding.Id));
            view.Cooldown.Text=seconds>0?MathF.Ceiling(seconds).ToString():"";view.Shade.Visible=seconds>0;
            string name=binding==null?"Empty slot":binding.Kind=="skill"?catalog.Abilities.GetValueOrDefault(binding.Id)?.Name??binding.Id:catalog.Items.GetValueOrDefault(binding.Id)?.Name??binding.Id;
            view.Button.TooltipText=$"{(view.Index==9?"0":(view.Index+1).ToString())} · {name}"+(binding==null?"\nDrag an item or skill here.":(view.Editor?"\nDrag to reorder · Right-click to clear":"\nClick to use · Drag to reorder · Right-click to clear")+(available?"":"\nCurrently unavailable")+(seconds>0?$"\nReady in {MathF.Ceiling(seconds)} seconds":""));
            if(view.LastEquipped!=equipped){view.LastEquipped=equipped;view.Button.AddThemeStyleboxOverride("normal",Box(equipped?new Color("4c5339"):new Color("1c2722ef"),equipped?Gold:new Color("566148")));}
        }
    }
}
