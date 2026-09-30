using Godot;
using System;
using Thornwake.Core;

namespace Thornwake.UI;
public partial class GameHud
{
    private Control _worldSetup=null!;
    private GenerationSettings _newGeneration=new(),_generationDraft=new();
    private LandscapePreview _landscapePreview=null!;
    private LineEdit _landscapeSeed=null!;
    private Label _landscapeNote=null!;
    private Button _landscapeCreate=null!;
    private VBoxContainer _generationRows=null!;
    private float _previewDelay=-1;
    public bool IsWorldSetupOpen=>_worldSetup?.Visible??false;
    public GenerationSettings NewWorldGeneration=>_newGeneration.Copy();
    public void ShowWorldSetup()
    {
        _generationDraft=_newGeneration.Copy();_landscapeSeed.Text=_seed.Text;
        RenderGenerationControls();_worldSetup.Show();_previewDelay=0;
    }
    public void HideWorldSetup(){_worldSetup?.Hide();_previewDelay=-1;}
    private void BuildWorldSetup()
    {
        _worldSetup=Modal(new Color(.025f,.045f,.035f,.96f));_worldSetup.Name="NewWorldSetup";
        var card=Card(1050,700);Place(card,.5f,.5f,-525,-350,1050,700);_worldSetup.AddChild(card);
        var stack=Stack(card,30,24,14);
        stack.AddChild(Text("Shape your valley",34,Cream,true));
        stack.AddChild(Text("Choose the land before you arrive. The preview shows your seed and settings.",15,Muted));
        var body=new HBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};body.AddThemeConstantOverride("separation",30);stack.AddChild(body);
        var options=new VBoxContainer{CustomMinimumSize=new Vector2(450,0),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};options.AddThemeConstantOverride("separation",14);body.AddChild(options);
        var seedRow=new HBoxContainer();seedRow.AddThemeConstantOverride("separation",12);seedRow.AddChild(Text("WORLD SEED",12,Gold));
        _landscapeSeed=new LineEdit{Name="LandscapeSeed",MaxLength=11,CustomMinimumSize=new Vector2(175,36)};_landscapeSeed.TextChanged+=_=>_previewDelay=.15f;seedRow.AddChild(_landscapeSeed);
        seedRow.AddChild(ActionButton("New seed",()=>_landscapeSeed.Text=unchecked((int)GD.Randi()).ToString()));options.AddChild(seedRow);
        var presets=new HBoxContainer();presets.AddThemeConstantOverride("separation",6);options.AddChild(presets);
        foreach(string label in new[]{"Gentle","Harthwood","Rugged"})
        {
            var button=ActionButton(label,()=>
            {
                _generationDraft=label switch{"Gentle"=>new(){Relief=.55f,Roughness=.2f,ForestDensity=.65f},"Rugged"=>new(){Relief=1.5f,Roughness=.85f,ForestDensity=.95f},_=>new()};
                RenderGenerationControls();_previewDelay=0;
            });button.Name="LandscapePreset_"+label;button.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;presets.AddChild(button);
        }
        _generationRows=new VBoxContainer();_generationRows.AddThemeConstantOverride("separation",17);options.AddChild(_generationRows);
        var help=Text("Landscape settings are fixed once this world is created. Existing saves keep their original terrain."+System.Environment.NewLine+System.Environment.NewLine+"Growth, crafting and survival rules remain adjustable in Escape → World sandbox.",13,Muted);help.AutowrapMode=TextServer.AutowrapMode.WordSmart;options.AddChild(help);
        var previewColumn=new VBoxContainer{CustomMinimumSize=new Vector2(440,0)};previewColumn.AddThemeConstantOverride("separation",9);body.AddChild(previewColumn);
        _landscapePreview=new LandscapePreview{Name="LandscapeMapPreview",CustomMinimumSize=new Vector2(440,440),TextureFilter=Control.TextureFilterEnum.Nearest,MouseFilter=Control.MouseFilterEnum.Ignore};previewColumn.AddChild(_landscapePreview);
        previewColumn.AddChild(Text("Lowlands → uplands  ·  Contours: 1.5 m  ·  Dots: trees",12,Muted));
        _landscapeNote=Text("",12,Gold);_landscapeNote.AutowrapMode=TextServer.AutowrapMode.WordSmart;previewColumn.AddChild(_landscapeNote);
        var footer=new HBoxContainer();footer.AddThemeConstantOverride("separation",12);stack.AddChild(footer);
        var back=ActionButton("Back",HideWorldSetup);back.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;footer.AddChild(back);
        _landscapeCreate=ActionButton("Create this world",()=>
        {
            if(!int.TryParse(_landscapeSeed.Text,out int seed))return;
            _generationDraft.Validate();_newGeneration=_generationDraft.Copy();_seed.Text=seed.ToString();HideWorldSetup();StartRequested?.Invoke(seed);
        },true);_landscapeCreate.Name="LandscapeCreate";_landscapeCreate.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;footer.AddChild(_landscapeCreate);
        _worldSetup.Hide();
    }
    private void RenderGenerationControls()
    {
        ClearChildren(_generationRows);
        GenerationSlider("Relief","Height of hills and ridges",_generationDraft.Relief,.4,1.6,.05,v=>_generationDraft.Relief=v);
        GenerationSlider("Roughness","Small variations across the slopes",_generationDraft.Roughness,0,1,.05,v=>_generationDraft.Roughness=v);
        GenerationSlider("ForestDensity","Woodland density",_generationDraft.ForestDensity,.4,1,.05,v=>_generationDraft.ForestDensity=v);
    }
    private void GenerationSlider(string id,string label,float value,double min,double max,double step,Action<float> apply)
    {
        var row=new VBoxContainer();row.AddThemeConstantOverride("separation",5);_generationRows.AddChild(row);
        var caption=Text(label+"  ·  "+value.ToString("0.00")+"×",15,Cream);row.AddChild(caption);
        var slider=new HSlider{Name="Landscape_"+id,MinValue=min,MaxValue=max,Step=step,Value=value,CustomMinimumSize=new Vector2(0,24)};
        slider.ValueChanged+=v=>{apply((float)v);caption.Text=label+"  ·  "+v.ToString("0.00")+"×";_previewDelay=.15f;};row.AddChild(slider);
    }
    private void UpdateLandscapePreview(double delta)
    {
        if(!IsWorldSetupOpen||_previewDelay<0)return;
        _previewDelay-=(float)delta;if(_previewDelay>0)return;_previewDelay=-1;
        bool valid=int.TryParse(_landscapeSeed.Text,out int seed);_landscapeCreate.Disabled=!valid;
        if(!valid){_landscapeNote.Text="Enter a whole-number seed between −2147483648 and 2147483647.";return;}
        var world=LandscapeGenerator.Create(seed,_generationDraft,new());_landscapePreview.SetWorld(world);
        _landscapeNote.Text=$"{_landscapePreview.TreeCount} trees · 110 m valley · Prepared village and roads";
    }
}
