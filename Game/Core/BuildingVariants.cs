using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Thornwake.Core;

public sealed class BuildSelection
{
    public string DefinitionId { get; set; } = "";
    public string VariantId { get; set; } = "";
    public float Slope { get; set; }
    public string MaterialId { get; set; } = "";
    public string AppearanceId { get; set; } = "";
    public bool Mirrored { get; set; }
    public BuildSelection Copy()=>new(){DefinitionId=DefinitionId,VariantId=VariantId,Slope=Slope,MaterialId=MaterialId,Mirrored=Mirrored,AppearanceId=AppearanceId};
}
public sealed class BuildingToolDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Structure";
    public string Description { get; set; } = "";
    public string DefaultVariant { get; set; } = "";
    public float DefaultSlope { get; set; }
    public List<BuildingVariantDefinition> Variants { get; set; } = new();
    public List<BuildingAppearanceDefinition> Appearances { get; set; } = new();
    public List<BuildingMaterialDefinition> Materials { get; set; } = new();
    public string[] LegacyKinds { get; set; } = Array.Empty<string>();
    public float[] AllowedRotations { get; set; } = {0,45,90,135,180,225,270,315};
}
public sealed class BuildingVariantDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "";
    public string? UpperKind { get; set; }
    public string Geometry { get; set; } = "legacy";
    public string Mesh { get; set; } = "";
    public float[] Slopes { get; set; } = {0};
    public bool SupportsMirroring { get; set; }
    public Dictionary<string,int>? Costs { get; set; }
}
public sealed class BuildingMaterialDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string ColorHex { get; set; } = "a18b61";
    // Optional extra inputs are added to the variant recipe, never silently substituted.
    public Dictionary<string,int>? Costs { get; set; }
}
public static class BuildingVariants
{
    public static BuildSelection Default(BuildingToolDefinition tool)=>new(){DefinitionId=tool.Id,VariantId=tool.DefaultVariant,Slope=tool.DefaultSlope,MaterialId=tool.Materials[0].Id};
    public static BuildingVariantDefinition Variant(ContentCatalog catalog,BuildSelection selection)
    {
        if(selection==null||!catalog.BuildingTools.TryGetValue(selection.DefinitionId??"",out var tool))throw new InvalidDataException("Unknown building tool.");
        return tool.Variants.FirstOrDefault(v=>v.Id==selection.VariantId)??throw new InvalidDataException("Unknown building variant.");
    }
    public static BuildingMaterialDefinition Material(ContentCatalog catalog,BuildSelection selection)
    {
        if(selection==null||!catalog.BuildingTools.TryGetValue(selection.DefinitionId??"",out var tool))throw new InvalidDataException("Unknown building tool.");
        return tool.Materials.FirstOrDefault(m=>m.Id==selection.MaterialId)??throw new InvalidDataException("Unknown building material.");
    }
    public static void Validate(ContentCatalog catalog,BuildSelection selection,int level=0)
    {
        var variant=Variant(catalog,selection);Material(catalog,selection);
        if(selection.AppearanceId==null||selection.AppearanceId!=""&&BuildingAppearances.Find(catalog,selection)==null)throw new InvalidDataException("Unknown building appearance.");
        if(level<0||level>2||!float.IsFinite(selection.Slope)||!variant.Slopes.Any(s=>MathF.Abs(s-selection.Slope)<.001f)||selection.Mirrored&&!variant.SupportsMirroring)
            throw new InvalidDataException("This building variant does not support the selected slope or mirror option.");
    }
    public static BuildingDefinition Resolve(ContentCatalog catalog,BuildSelection selection,int level=0)
    {
        Validate(catalog,selection,level);var variant=Variant(catalog,selection);var material=Material(catalog,selection);
        string kind=level>0&&variant.UpperKind!=null?variant.UpperKind:variant.Kind;
        var basis=catalog.Buildings[kind];var costs=new Dictionary<string,int>(variant.Costs??basis.Costs);
        if(material.Costs!=null)foreach(var cost in material.Costs)costs[cost.Key]=checked(costs.GetValueOrDefault(cost.Key)+cost.Value);
        return new(){Id=basis.Id,Name=variant.Name,Costs=costs,Radius=basis.Radius,Placement=basis.Placement,Width=basis.Width,Depth=basis.Depth,Category=catalog.BuildingTools[selection.DefinitionId].Category,Description=catalog.BuildingTools[selection.DefinitionId].Description};
    }
    public static void ValidateCatalog(ContentCatalog catalog)
    {
        bool Costs(Dictionary<string,int>? costs,bool emptyAllowed)=>costs==null||((emptyAllowed||costs.Count>0)&&costs.All(c=>catalog.Items.ContainsKey(c.Key)&&c.Value>0&&c.Value<=10000));
        foreach(var tool in catalog.BuildingTools.Values)
        {
            if(string.IsNullOrWhiteSpace(tool.Id)||string.IsNullOrWhiteSpace(tool.Name)||tool.Variants==null||tool.Variants.Count==0||tool.Materials==null||tool.Materials.Count==0||tool.AllowedRotations==null||tool.AllowedRotations.Length==0||tool.AllowedRotations.Any(a=>!float.IsFinite(a)||a<0||a>=360)||tool.AllowedRotations.Distinct().Count()!=tool.AllowedRotations.Length||tool.Variants.Any(v=>v==null)||tool.Materials.Any(m=>m==null))throw new InvalidDataException("Invalid building tool definition.");
            if(tool.Variants.Select(v=>v.Id).Distinct().Count()!=tool.Variants.Count||tool.Materials.Select(m=>m.Id).Distinct().Count()!=tool.Materials.Count)throw new InvalidDataException("Duplicate building variant or material.");
            if(tool.LegacyKinds==null||tool.LegacyKinds.Any(k=>!catalog.Buildings.ContainsKey(k)))throw new InvalidDataException("Unknown legacy building alias.");
            foreach(var variant in tool.Variants)
            {
                if(string.IsNullOrWhiteSpace(variant.Id)||string.IsNullOrWhiteSpace(variant.Name)||!catalog.Buildings.ContainsKey(variant.Kind)||variant.UpperKind!=null&&!catalog.Buildings.ContainsKey(variant.UpperKind)||variant.Mesh==null||variant.Mesh.Length>300||variant.Slopes==null||variant.Slopes.Length==0||variant.Slopes.Any(s=>!float.IsFinite(s)||s<0||s>60)||variant.Slopes.Distinct().Count()!=variant.Slopes.Length||!Costs(variant.Costs,false))throw new InvalidDataException("Invalid building variant definition.");
                if(variant.Geometry!="legacy"&&!RoofGeometry.KnownGeometry(variant.Geometry)||variant.Geometry!="legacy"&&variant.Kind!="roof_module"||variant.Kind=="roof_module"&&variant.Geometry=="legacy"||variant.SupportsMirroring&&variant.Geometry!="roof_gable_edge"&&!RoofGeometry.IsFill(variant.Geometry))throw new InvalidDataException("Unsupported building geometry or mirror option.");
                if(variant.Geometry!="legacy"&&variant.Slopes.Any(s=>s is not (22.5f or 30 or 45 or 60))||variant.Geometry=="legacy"&&variant.Slopes.Any(s=>s!=0))throw new InvalidDataException("Unsupported pitch for building geometry.");
            }
            foreach(var material in tool.Materials)if(string.IsNullOrWhiteSpace(material.Id)||string.IsNullOrWhiteSpace(material.Name)||material.ColorHex==null||!Regex.IsMatch(material.ColorHex,"^#?[0-9a-fA-F]{6}$")||!Costs(material.Costs,true))throw new InvalidDataException("Invalid building material definition.");
            BuildingAppearances.Validate(tool);Validate(catalog,Default(tool));
        }
    }
}
