using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.Core;

namespace Thornwake.UI;

public partial class GameHud
{
    private Panel _buildOptions = null!;
    private Label _buildOptionTitle = null!, _buildOptionPosition = null!, _buildOptionCost = null!;
    private ItemIcon _buildOptionIcon = null!;
    private ColorRect _buildMaterialSwatch = null!;
    private Button _buildShapeButton = null!, _buildSlopeButton = null!, _buildMaterialButton = null!, _buildMirrorButton = null!, _buildAppearanceButton = null!, _finishBrushButton = null!;
    private bool _buildOptionsActive;
    private string _buildOptionsKey = "";

    public bool IsBuildOptionsHovered => _buildOptionsActive && _buildOptions.Visible && _buildOptions.GetGlobalRect().HasPoint(_buildOptions.GetGlobalMousePosition());

    private void BuildPlacementOptions()
    {
        _buildOptions=Card(284,334);_buildOptions.Name="BuildVariantOptions";
        _buildOptions.MouseFilter=Control.MouseFilterEnum.Stop;
        Place(_buildOptions,0,0,24,184,284,334);_hud.AddChild(_buildOptions);
        var contents=Stack(_buildOptions,14,12,7);
        var header=new HBoxContainer();header.AddThemeConstantOverride("separation",9);
        _buildOptionIcon=new ItemIcon{CustomMinimumSize=new Vector2(42,46)};header.AddChild(_buildOptionIcon);
        var titles=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};titles.AddThemeConstantOverride("separation",2);
        _buildOptionTitle=Text("Roof",21,Cream,true);titles.AddChild(_buildOptionTitle);
        _buildOptionPosition=Text("Ground · 0°",11,Muted);titles.AddChild(_buildOptionPosition);header.AddChild(titles);
        _buildMaterialSwatch=new ColorRect{CustomMinimumSize=new Vector2(10,10),SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,MouseFilter=Control.MouseFilterEnum.Ignore};header.AddChild(_buildMaterialSwatch);contents.AddChild(header);
        _buildShapeButton=BuildOptionButton("BuildShape","Shape","buildshape");contents.AddChild(_buildShapeButton);
        _buildSlopeButton=BuildOptionButton("BuildSlope","Pitch","buildslope");contents.AddChild(_buildSlopeButton);
        _buildMaterialButton=BuildOptionButton("BuildMaterial","Material","buildmaterial");contents.AddChild(_buildMaterialButton);
        _buildMirrorButton=BuildOptionButton("BuildMirror","Mirror","buildmirror");contents.AddChild(_buildMirrorButton);
        _buildAppearanceButton=BuildOptionButton("BuildAppearance","Finish","buildappearance");contents.AddChild(_buildAppearanceButton);
        _finishBrushButton=BuildOptionButton("FinishBrush","Finish brush","finishbrush");contents.AddChild(_finishBrushButton);
        _buildOptionCost=Text("",12,Gold);_buildOptionCost.AutowrapMode=TextServer.AutowrapMode.WordSmart;contents.AddChild(_buildOptionCost);
        var help=Text("R rotate · V snap · Alt + wheel height\nShift + T / Y reverses · H resets height",12,Muted);help.AddThemeConstantOverride("line_spacing",3);contents.AddChild(help);
        _buildOptions.Hide();
    }
    private Button BuildOptionButton(string name,string label,string command)
    {
        var button=ActionButton(label,()=>Command?.Invoke(command));button.Name=name;
        button.CustomMinimumSize=new Vector2(0,32);button.Alignment=HorizontalAlignment.Left;
        button.FocusMode=Control.FocusModeEnum.None;
        button.ClipText=true;button.AddThemeFontSizeOverride("font_size",13);
        return button;
    }

    /// <summary>Refreshes labels only when the placement selection changes; never recreates live controls.</summary>
    public void SetBuildOptions(ContentCatalog catalog,BuildSelection? selection,float rotation,int level,bool refinishing=false)
    {
        _buildOptionsActive=selection!=null&&catalog.BuildingTools.ContainsKey(selection.DefinitionId);
        UpdateBuildOptionsVisibility();
        if(!_buildOptionsActive||selection==null)return;
        int degrees=((int)MathF.Round(Mathf.RadToDeg(rotation))%360+360)%360;
        string key=$"{selection.DefinitionId}|{selection.VariantId}|{selection.Slope}|{selection.MaterialId}|{selection.AppearanceId}|{refinishing}|{selection.Mirrored}|{degrees}|{level}";
        if(_buildOptionsKey==key)return;
        _buildOptionsKey=key;
        var tool=catalog.BuildingTools[selection.DefinitionId];
        var variant=BuildingVariants.Variant(catalog,selection);
        var material=BuildingVariants.Material(catalog,selection);
        var effective=BuildingVariants.Resolve(catalog,selection,level);
        _buildOptionTitle.Text=refinishing?"Finish brush":tool.Name;
        _buildOptionPosition.Text=$"{(level==0?"Ground":"Floor "+(level+1))} · {degrees}° rotation";
        _buildOptionIcon.Item=variant.Geometry=="legacy"?variant.Kind:variant.Geometry;_buildOptionIcon.QueueRedraw();
        var appearance=BuildingAppearances.Find(catalog,selection);
        _buildMaterialSwatch.Color=new Color(appearance?.ColorHex??material.ColorHex);
        _buildShapeButton.Visible=_buildMaterialButton.Visible=!refinishing;
        _buildShapeButton.Text=$"Shape · {variant.Name}   [T]";
        _buildShapeButton.TooltipText=variant.Name+"\nCycle shapes with T; Shift + T goes backwards.";
        _buildShapeButton.Disabled=tool.Variants.Count<2;
        _buildSlopeButton.Visible=!refinishing&&(variant.Slopes.Length>1||selection.Slope>0);
        _buildSlopeButton.Text=$"Pitch · {selection.Slope:0.#}°   [Y]";
        _buildSlopeButton.Disabled=variant.Slopes.Length<2;
        _buildSlopeButton.TooltipText="Cycle roof pitches with Y; Shift + Y goes backwards.";
        _buildMaterialButton.Text=$"Material · {material.Name}   [N]";
        _buildMaterialButton.Disabled=tool.Materials.Count<2;
        _buildMaterialButton.TooltipText=tool.Materials.Count<2?"This tool currently has one material.":"Cycle materials with N.";
        _buildMirrorButton.Visible=!refinishing&&variant.SupportsMirroring;
        _buildMirrorButton.Text=$"Mirror · {(selection.Mirrored?"reversed":"normal")}   [G]";
        _buildMirrorButton.TooltipText="Flip the shape with G.";
        _buildAppearanceButton.Visible=_finishBrushButton.Visible=tool.Appearances.Count>0;
        _buildAppearanceButton.Text=$"Finish · {appearance?.Name??"Original"}   [U]";
        _buildAppearanceButton.TooltipText="Cosmetic only. U cycles finishes; Shift + U goes backwards. Original restores the construction material.";
        _finishBrushButton.Text=refinishing?"Return to building   [Z]":"Repaint existing pieces   [Z]";
        _buildOptionCost.Text="PER PIECE  ·  "+string.Join(" · ",effective.Costs.Select(x=>$"{x.Value} {catalog.Items.GetValueOrDefault(x.Key)?.Name??x.Key}"));
        if(refinishing)_buildOptionCost.Text="COSMETIC ONLY  ·  No materials used";
        int rows=(refinishing?0:2)+(_buildAppearanceButton.Visible?2:0)+(_buildSlopeButton.Visible?1:0)+(_buildMirrorButton.Visible?1:0);
        float height=182+39*rows;
        _buildOptions.CustomMinimumSize=new Vector2(284,height);Place(_buildOptions,0,0,24,184,284,height);
    }
    private void UpdateBuildOptionsVisibility()
    {
        if(_buildOptions!=null)_buildOptions.Visible=_buildOptionsActive&&!IsPanelOpen&&!IsTitleVisible;
    }
}
