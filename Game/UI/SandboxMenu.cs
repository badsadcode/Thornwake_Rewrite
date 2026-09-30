using Godot;
using System;
using System.Text.Json;
using Thornwake.Core;

namespace Thornwake.UI;

public partial class GameHud
{
    private Control _sandbox = null!;
    private VBoxContainer _sandboxRows = null!, _sandboxCategories = null!;
    private Label _sandboxHeading = null!, _sandboxNote = null!, _sandboxStatus = null!;
    private SandboxSettings? _sandboxDraft;
    private string _sandboxCategory="Ecology",_sandboxLandscape="";
    public bool IsSandboxOpen=>_sandbox?.Visible??false;

    private void BuildSandbox()
    {
        _sandbox=Modal(new Color(.025f,.045f,.035f,.86f));_sandbox.Name="WorldSandbox";
        var frame=Card(1120,730);Place(frame,.5f,.5f,-560,-365,1120,730);_sandbox.AddChild(frame);
        var stack=Stack(frame,28,24,14);
        var header=new HBoxContainer();var title=Text("A world at your pace",32,Cream,true);title.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;header.AddChild(title);
        header.AddChild(ActionButton("Cancel  [Esc]",()=>Command?.Invoke("sandbox:cancel")));stack.AddChild(header);
        stack.AddChild(Text("Rules for this world. Adjust a draft, then apply it when you are ready.",14,Muted));
        var presets=new HBoxContainer();presets.AddThemeConstantOverride("separation",10);presets.AddChild(Text("PRESETS",11,Gold));
        foreach(string name in new[]{"Slow living","Builder","Harsh"})
        {string preset=name;var button=ActionButton(name,()=>SetSandboxPreset(preset));button.Name="SandboxPreset_"+name.Replace(" ","");button.CustomMinimumSize=new Vector2(135,36);presets.AddChild(button);}
        var presetHint=Text("Presets change this draft only.",12,Muted);presetHint.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;presetHint.HorizontalAlignment=HorizontalAlignment.Right;presets.AddChild(presetHint);stack.AddChild(presets);
        var body=new HBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};body.AddThemeConstantOverride("separation",22);stack.AddChild(body);
        _sandboxCategories=new VBoxContainer{CustomMinimumSize=new Vector2(170,0)};_sandboxCategories.AddThemeConstantOverride("separation",9);body.AddChild(_sandboxCategories);
        var content=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};content.AddThemeConstantOverride("separation",10);body.AddChild(content);
        _sandboxHeading=Text("Ecology",22,Cream,true);content.AddChild(_sandboxHeading);
        _sandboxNote=Text("",12,Muted);_sandboxNote.AutowrapMode=TextServer.AutowrapMode.WordSmart;content.AddChild(_sandboxNote);
        var scroll=new ScrollContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};content.AddChild(scroll);
        _sandboxRows=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};_sandboxRows.AddThemeConstantOverride("separation",8);scroll.AddChild(_sandboxRows);
        var footer=new HBoxContainer();footer.AddThemeConstantOverride("separation",12);stack.AddChild(footer);
        _sandboxStatus=Text("Changes apply to this world only.",12,Gold);_sandboxStatus.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;_sandboxStatus.AutowrapMode=TextServer.AutowrapMode.WordSmart;footer.AddChild(_sandboxStatus);
        var reset=ActionButton("Reset draft",()=>SetSandboxPreset("Slow living"));reset.Name="SandboxReset";footer.AddChild(reset);
        var apply=ActionButton("Apply world settings",()=>{if(_sandboxDraft!=null)Command?.Invoke("sandbox:apply:"+JsonSerializer.Serialize(_sandboxDraft));},true);apply.Name="SandboxApply";footer.AddChild(apply);
        _sandbox.Hide();
    }
    public void ShowSandbox(WorldState state)
    {
        _sandboxLandscape=state.GeneratorVersion==1?$"Original valley · Seed {state.Seed}. This saved landscape is preserved.":$"Seed {state.Seed} · Relief {state.Generation.Relief:0.00}× · Roughness {state.Generation.Roughness:0.00}× · Forest {state.Generation.ForestDensity:0.00}×. Landscape choices are fixed at world creation.";
        HidePanel();_sandboxDraft=state.Settings.Copy();_sandboxCategory="Ecology";
        _sandboxStatus.Text="Editing a copy. Your current world settings stay active until Apply.";
        RenderSandbox();_sandbox.Show();
    }
    private void HideSandbox(){_sandbox?.Hide();_sandboxDraft=null;}
    private void SandboxChanged()=>_sandboxStatus.Text="Unapplied changes · Apply to use this draft, or Cancel to keep your current rules.";
    private void SetSandboxPreset(string name)
    {
        _sandboxDraft=name switch
        {
            "Builder"=>new SandboxSettings{TreeGrowthHours=.1f,BushGrowthHours=.1f,BushRegrowHours=.1f,CropGrowthMinutes=.5f,CraftDurationMultiplier=.25f,ResourceYieldMultiplier=3,StaminaRegenMultiplier=2,DamageTakenMultiplier=.25f,DeathCoinLoss=0},
            "Harsh"=>new SandboxSettings{TreeGrowthHours=12,BushGrowthHours=4,BushRegrowHours=2,CropGrowthMinutes=3,CraftDurationMultiplier=1.5f,ResourceYieldMultiplier=.5f,StaminaRegenMultiplier=.65f,DamageTakenMultiplier=2,DeathCoinLoss=12},
            _=>new SandboxSettings()
        };
        _sandboxStatus.Text=name+" preset loaded into the draft. Apply to use it in this world.";RenderSandbox();
    }
    private void RenderSandbox()
    {
        if(_sandboxDraft==null)return;
        ClearChildren(_sandboxCategories);ClearChildren(_sandboxRows);
        foreach(string name in new[]{"Ecology","Crafting","Survival","World"})
        {
            string category=name;var button=ActionButton(name,()=>{_sandboxCategory=category;RenderSandbox();});button.Name="SandboxCategory_"+name;button.Alignment=HorizontalAlignment.Left;
            if(name==_sandboxCategory)button.AddThemeStyleboxOverride("normal",Box(new Color("394736"),Gold));_sandboxCategories.AddChild(button);
        }
        var draft=_sandboxDraft;_sandboxHeading.Text=_sandboxCategory;
        _sandboxNote.Text=_sandboxCategory switch
        {
            "Ecology"=>"Growth uses active play time. Paused menus and time away do not grow plants. Calendar speed does not change these durations.",
            "Crafting"=>"Adjust the time spent making things and the materials gathered from the valley.",
            "Survival"=>"Tune recovery and the cost of a difficult encounter. Zero damage creates a peaceful combat setting.",
            _=>"Calendar speed changes the day and night cycle. Plant growth continues to use active play time."
        };
        switch(_sandboxCategory)
        {
            case "Ecology":
                SandboxNumber("TreeGrowthHours","Tree growth","Active hours for a planted tree to mature.",draft.TreeGrowthHours,.0167,720,.01,"hours",v=>draft.TreeGrowthHours=(float)v);
                SandboxNumber("BushGrowthHours","Bush growth","Active hours for a newly planted bush to mature.",draft.BushGrowthHours,.0167,720,.01,"hours",v=>draft.BushGrowthHours=(float)v);
                SandboxNumber("BushRegrowHours","Berry regrowth","Active hours until a harvested bush bears berries again.",draft.BushRegrowHours,.0167,720,.01,"hours",v=>draft.BushRegrowHours=(float)v);
                SandboxNumber("CropGrowthMinutes","Crop growth","Active minutes from sowing to harvest.",draft.CropGrowthMinutes,.1,10080,.1,"minutes",v=>draft.CropGrowthMinutes=(float)v);
                SandboxNumber("PlantSpacing","Plant spacing","Minimum spacing between new trees and bushes.",draft.PlantSpacing,2,8,.1,"metres",v=>draft.PlantSpacing=(float)v);
                SandboxNumber("StumpWoodYield","Stump timber","Timber gained by clearing a stump.",draft.StumpWoodYield,0,20,1,"timber",v=>draft.StumpWoodYield=(int)v);
                SandboxToggle("FreezeVegetation","Pause vegetation growth","Pauses tree and bush growth and berry regrowth; existing progress is kept.",draft.FreezeVegetation,v=>draft.FreezeVegetation=v);
                break;
            case "Crafting":
                SandboxNumber("CraftDurationMultiplier","Crafting time","0.5× takes half as long; 2× takes twice as long.",draft.CraftDurationMultiplier,.1,20,.1,"×",v=>draft.CraftDurationMultiplier=(float)v);
                SandboxNumber("ResourceYieldMultiplier","Gathering yield","Scales materials gathered from resources in the valley.",draft.ResourceYieldMultiplier,.25,5,.05,"×",v=>draft.ResourceYieldMultiplier=(float)v);
                break;
            case "Survival":
                SandboxNumber("StaminaRegenMultiplier","Stamina recovery","Scales natural stamina recovery. Zero disables recovery.",draft.StaminaRegenMultiplier,0,5,.1,"×",v=>draft.StaminaRegenMultiplier=(float)v);
                SandboxNumber("DamageTakenMultiplier","Damage taken","Scales incoming damage. Zero prevents combat damage.",draft.DamageTakenMultiplier,0,5,.1,"×",v=>draft.DamageTakenMultiplier=(float)v);
                SandboxNumber("DeathCoinLoss","Defeat cost","Crowns lost when defeated, up to the amount carried.",draft.DeathCoinLoss,0,100,1,"crowns",v=>draft.DeathCoinLoss=(int)v);
                break;
            case "World":
                var landscapeInfo=Text(_sandboxLandscape,13,Gold);landscapeInfo.AutowrapMode=TextServer.AutowrapMode.WordSmart;_sandboxRows.AddChild(landscapeInfo);
                SandboxNumber("DayLengthMinutes","Length of a day","Active minutes for one full day and night cycle.",draft.DayLengthMinutes,2,180,1,"minutes",v=>draft.DayLengthMinutes=(float)v);
                break;
        }
    }
    private HBoxContainer SandboxRow(string title,string description)
    {
        var panel=Card(0,67);panel.CustomMinimumSize=new Vector2(0,67);_sandboxRows.AddChild(panel);
        var margin=Stack(panel,13,9,0);var row=new HBoxContainer();row.AddThemeConstantOverride("separation",14);margin.AddChild(row);
        var text=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};text.AddThemeConstantOverride("separation",4);row.AddChild(text);
        text.AddChild(Text(title,15,Cream));var note=Text(description,11,Muted);note.AutowrapMode=TextServer.AutowrapMode.WordSmart;text.AddChild(note);return row;
    }
    private void SandboxNumber(string id,string title,string description,double value,double min,double max,double step,string unit,Action<double> apply)
    {
        var row=SandboxRow(title,description);
        var number=new SpinBox{Name="Sandbox_"+id,MinValue=min,MaxValue=max,Step=step,Value=value,CustomMinimumSize=new Vector2(112,36),SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};
        number.ValueChanged+=v=>{apply(v);SandboxChanged();};row.AddChild(number);
        var suffix=Text(unit,12,Muted);suffix.CustomMinimumSize=new Vector2(57,0);suffix.VerticalAlignment=VerticalAlignment.Center;row.AddChild(suffix);
    }
    private void SandboxToggle(string id,string title,string description,bool value,Action<bool> apply)
    {
        var row=SandboxRow(title,description);var toggle=new CheckButton{Name="Sandbox_"+id,Text=value?"Paused":"Growing",ButtonPressed=value,CustomMinimumSize=new Vector2(178,36)};
        toggle.Toggled+=v=>{toggle.Text=v?"Paused":"Growing";apply(v);SandboxChanged();};row.AddChild(toggle);
    }
}
