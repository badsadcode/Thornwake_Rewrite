using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Thornwake.Core;
using Thornwake.World;

namespace Thornwake.Tools;

/// <summary>Offline art reference exporter. Not part of the gameplay scene.</summary>
public partial class AssetTemplateExporter : Node
{
    private readonly List<object> _manifest=new();
    private string _output="";
    public override void _Ready()
    {
        try
        {
            _output=ProjectSettings.GlobalizePath("res://ArtKit");Directory.CreateDirectory(_output);
            var catalog=ContentCatalog.LoadFromJson(f=>Godot.FileAccess.GetFileAsString("res://Content/"+f));
            var valley=new Valley();AddChild(valley);valley.Generate(new WorldState{Seed=7319},catalog);
            foreach(var tool in catalog.BuildingTools.Values)
            foreach(var variant in tool.Variants)
            foreach(float pitch in variant.Slopes)
            foreach(bool mirrored in variant.SupportsMirroring?new[]{false,true}:new[]{false})
            foreach(int level in variant.UpperKind!=null?new[]{0,1}:new[]{0})
            {
                var selection=BuildingVariants.Default(tool);selection.VariantId=variant.Id;selection.Slope=pitch;selection.Mirrored=mirrored;
                var definition=BuildingVariants.Resolve(catalog,selection,level);
                string name=Pascal(tool.Id)+"_"+Pascal(variant.Id)+(pitch>0?"_"+pitch.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture).Replace('.','p'):"")+(variant.SupportsMirroring?(mirrored?"_Right":"_Left"):"")+(variant.UpperKind!=null?(level==0?"_Foundation":"_Upper"):"");
                Node3D art=tool.Id=="roof"||definition.Id=="roof_module"?Visuals.BuildVariant(catalog,selection,false,false,false,false):valley.StructureVisual(definition.Id,false,selection);
                Export(name,tool.Id=="roof"?"01_Roofs":tool.Id=="wall"?"02_Walls":"03_Structure",art,
                    definition.Id,tool.Id,variant.Id,pitch,mirrored,definition.Width,definition.Depth,
                    "Modular origin; preserve the grid, profile and pivots. Roof templates exclude optional exposed-edge overhangs.",
                    "building_appearance_mapping",variant.Geometry);
            }
            var covered=catalog.BuildingTools.Values.SelectMany(t=>t.Variants.SelectMany(v=>new[]{v.Kind,v.UpperKind??v.Kind})).ToHashSet();
            foreach(var definition in catalog.Buildings.Values.Where(b=>!covered.Contains(b.Id)&&b.Id!="roof_module"))
                Export(definition.Id=="roof"?"Roof_LegacyConnected":Pascal(definition.Category)+"_"+Pascal(definition.Id),definition.Category=="Roofs"?"01_Roofs":definition.Category=="Decor"?"05_Decor":"04_Stations",valley.StructureVisual(definition.Id),definition.Id,"","",0,false,definition.Width,definition.Depth,"Preserve existing local origin; dimensions are measured from prototype artwork.","future_visual_adapter");
            Export("Nature_Tree_Pine","06_Nature",Visuals.Tree("pine"),"tree:pine",notes:"Base-size mature tree. World instances vary in scale; growth scales this model from its root.");
            Export("Nature_Tree_Birch","06_Nature",Visuals.Tree("birch"),"tree:birch",notes:"Base-size mature tree; local origin at soil contact.");
            Export("Nature_Tree_Stump","06_Nature",Visuals.Stump(),"stump",notes:"Stump origin matches the felled tree's soil origin.");
            Export("Nature_Tree_PineSapling_Reference","06_Nature",Visuals.Tree("pine",.18f),"pine_sapling",notes:"Initial growth-size reference only; mature tree and growth scaling are the production asset route.");
            Export("Nature_Rock","06_Nature",Visuals.Rock(),"stone",notes:"Base-size gatherable rock. World instances vary in scale.");
            var berryId=valley.Spawns.First(p=>p.Value.Kind=="berries").Key;
            var bush=(Node3D)valley.Resources[berryId].Duplicate();bush.Transform=Transform3D.Identity;
            Export("Nature_BerryBush_Fruiting","06_Nature",bush,"berries",notes:"Mature bush at base scale. Provide a separate bare state or fruit group in future art.");
            var bare=(Node3D)valley.Resources[berryId].Duplicate();bare.Transform=Transform3D.Identity;
            foreach(var child in bare.GetChildren().Skip(3).ToArray()){bare.RemoveChild(child);child.Free();}
            Export("Nature_BerryBush_Bare","06_Nature",bare,"berries:bare",notes:"Same bush without fruit; identical root and base foliage scale.");
            var grass=new Node3D();grass.AddChild(new MeshInstance3D{Mesh=Visuals.GrassMesh()});
            Export("Nature_Grass_Tuft","06_Nature",grass,"grass",notes:"One instanced tuft; match soil origin. Avoid dense geometry for repeated grass.");
            Export("Nature_Crop_Mature","06_Nature",Visuals.Crop(true),"village_crop:mature");
            Export("Nature_Crop_Young","06_Nature",Visuals.Crop(false),"village_crop:young");
            Export("Character_Player_Reference","07_Characters",Visuals.Person(new Color("526b61")),"player",notes:"Scale/pose reference with rigid named limb pivots. Not a production animation rig. Preserve the foot origin; coordinate a skeleton before replacing animated characters.");
            Export("Character_Villager_Reference","07_Characters",Visuals.Person(new Color("7f7055")),"villager",notes:"Scale reference; shares prototype character proportions and named limb pivots.");
            Export("Character_Bandit_Reference","07_Characters",Visuals.Person(new Color("673e35"),true),"bandit",notes:"Scale reference including prototype weapon; separate weapon art before final rigging.");
            string[] tools={"Axe","Hammer","Hoe","SeedBag","Sword"};
            for(int i=0;i<tools.Length;i++)Export("Tool_"+tools[i],"08_Tools",Visuals.Tool(i),tools[i].ToLowerInvariant(),notes:"Origin is the existing hand/arm attachment pivot, NOT the bottom of the mesh. Preserve offsets for compatibility; hand grip is (0,-0.49,0) in Godot / (0,0,-0.49) in Blender.");
            Export("Village_Cottage_5x5","09_Village",Visuals.House(5,5),"cottage:5x5",notes:"Whole-prefab size reference. Current village collision uses reserved lots; modular replacement is a later step.");
            Export("Village_Cottage_6x5","09_Village",Visuals.House(6,5),"cottage:6x5");
            Export("Village_Workshop_6x6","09_Village",Visuals.House(6,6,true),"workshop:6x6",notes:"Includes outside bench/logs and porch; measured visual bounds exceed the nominal 6x6 footprint.");
            Export("Village_Well","09_Village",Visuals.Well(),"well");
            var cart=(Node3D)valley.Cart.Duplicate();cart.Transform=Transform3D.Identity;
            Export("Village_Caravan_Cart","09_Village",cart,"caravan_cart");
            Export("Prop_Shrine_Optional","09_Village",Visuals.Shrine(),"shrine:unused",notes:"Existing visual helper, not currently placed in the playable valley.");
            var bridge=new Node3D();
            for(int i=0;i<17;i++)Visuals.Box(bridge,new(-4+i*.5f,.28f,0),new(.46f,.2f,3.6f),new Color("70604c"));
            foreach(float side in new[]{-1f,1f}){Visuals.Box(bridge,new(0,1,side*1.6f),new(9,.15f,.15f),new Color("4c4439"));foreach(float x in new[]{-4f,-2f,0f,2f,4f})Visuals.Box(bridge,new(x,.6f,side*1.6f),new(.17f,1.3f,.17f),new Color("51473b"));}
            Export("Village_Bridge","09_Village",bridge,"bridge",notes:"Current river crossing centred on its span; keep walking deck height and clear width.");
            var options=new JsonSerializerOptions{WriteIndented=true};
            File.WriteAllText(Path.Combine(_output,"asset_manifest.json"),JsonSerializer.Serialize(_manifest,options));
            File.WriteAllText(Path.Combine(_output,"icon_manifest.json"),JsonSerializer.Serialize(catalog.Items.Values.Select(item=>new{id="Icon_"+Pascal(item.Id),itemId=item.Id,name=item.Name,category=item.Category,suggestedPixels=256,format="PNG RGBA",status="Future icon replacement; current icons are procedurally drawn. Inventory-only items need new 3D designs if world models are desired."}),options));
            GD.Print($"ART_TEMPLATE_EXPORT_PASS: {_manifest.Count} named references with measured bounds");
            valley.QueueFree();GetTree().Quit();
        }
        catch(Exception e){GD.PushError(e.ToString());GetTree().Quit(2);}
    }
    private static string Pascal(string value)=>string.Join("",value.Split('_').Select(s=>char.ToUpperInvariant(s[0])+s[1..]));
    private void Export(string id,string category,Node3D node,string gameId,string tool="",string variant="",float pitch=0,bool mirrored=false,float gridWidth=0,float gridDepth=0,string notes="Origin at the existing game asset root; preserve it when replacing the artwork.",string integration="future_visual_adapter",string geometry="")
    {
        node.Name=id;
        var min=new Vector3(float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity);var max=-min;int meshes=0;
        void Inspect(Node n,Transform3D transform)
        {
            if(n is Node3D spatial)transform*=spatial.Transform;
            if(n is MeshInstance3D mesh&&mesh.Mesh!=null){meshes++;foreach(var vertex in mesh.Mesh.GetFaces()){var p=transform*vertex;min=min.Min(p);max=max.Max(p);}}
            foreach(var child in n.GetChildren().ToArray())
            {if(child is Light3D||child is Camera3D||child is WorldEnvironment){n.RemoveChild(child);child.Free();}else Inspect(child,transform);}
        }
        Inspect(node,Transform3D.Identity);
        if(meshes==0)throw new Exception("No geometry for "+id);
        string relative="References/"+category+"/"+id+".glb";
        string file=Path.Combine(_output,relative);Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        using var document=new GltfDocument();using var state=new GltfState();
        var error=document.AppendFromScene(node,state);if(error==Error.Ok)error=document.WriteToFilesystem(state,file);
        if(error!=Error.Ok)throw new Exception("GLB export failed for "+id+": "+error);
        var size=max-min;
        _manifest.Add(new{id,category,gameId,tool,variant,pitch,mirrored,geometry,gridWidthM=gridWidth,gridDepthM=gridDepth,
            widthM=size.X,depthM=size.Z,heightM=size.Y,godotMin=new[]{min.X,min.Y,min.Z},godotMax=new[]{max.X,max.Y,max.Z},reference=relative,integration,notes});
        node.Free();
    }
}
