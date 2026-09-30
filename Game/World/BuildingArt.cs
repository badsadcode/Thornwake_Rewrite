using Godot;
using System;
using System.Collections.Generic;
using Thornwake.Core;
namespace Thornwake.World;
public static partial class Visuals
{
    private static readonly HashSet<string> ModelWarnings = new();
    private static void ModelWarning(string message)
    { if(ModelWarnings.Add(message)) GD.PushWarning(message); }

    // Copy only artwork into the live world; imported collisions, lights, cameras and
    // animations have no gameplay authority. Exact mappings avoid distorting pitches.
    private static Node3D? ImportedBuilding(ContentCatalog catalog, BuildSelection selection,
        bool north, bool east, bool south, bool west, bool open, float gableBottom)
    {
        var model = BuildingAppearances.Model(catalog, selection);
        if(model == null) return null;
        var geometry = BuildingVariants.Variant(catalog,selection).Geometry;
        if(RoofGeometry.IsFill(geometry) && gableBottom < -.001f)
        { ModelWarning($"Building art '{model.Scene}' uses a fixed gable height. Using the procedural profile for this raised fill.");return null; }
        if(!ResourceLoader.Exists(model.Scene))
        { ModelWarning($"Missing building art '{model.Scene}'. Using the matching procedural shape.");return null; }
        var scene = ResourceLoader.Load<PackedScene>(model.Scene);
        if(scene == null) { ModelWarning($"Building art '{model.Scene}' is not a scene. Using procedural geometry.");return null; }
        var source = scene.Instantiate();
        try
        {
            bool hasLeaf = false; int meshes = 0;
            Node3D Copy(Node node)
            {
                var name = node.Name.ToString();Node3D copy;
                if(node is MeshInstance3D mesh && mesh.Mesh != null)
                {
                    var visual = new MeshInstance3D { Mesh=mesh.Mesh, MaterialOverride=mesh.MaterialOverride, CastShadow=mesh.CastShadow };
                    for(int i=0;i<mesh.GetSurfaceOverrideMaterialCount();i++) visual.SetSurfaceOverrideMaterial(i,mesh.GetSurfaceOverrideMaterial(i));
                    copy=visual; meshes++;
                }
                else copy=new Node3D();
                copy.Name=name;
                if(node is Node3D transform) {copy.Transform=transform.Transform;copy.Visible=transform.Visible;}
                bool? edge = name switch {"EdgeNorth"=>north,"EdgeEast"=>east,"EdgeSouth"=>south,"EdgeWest"=>west,_=>null};
                if(edge.HasValue)copy.Visible=edge.Value;
                if(name=="DoorLeaf") {hasLeaf=true;if(open)copy.RotateY(-Mathf.Pi/2);}
                foreach(var child in node.GetChildren())copy.AddChild(Copy(child));
                return copy;
            }
            var art=Copy(source);
            if(meshes==0 || BuildingVariants.Variant(catalog,selection).Kind=="door"&&!hasLeaf)
            { art.Free();ModelWarning($"Building art '{model.Scene}' needs meshes and door variants need a DoorLeaf pivot. Using procedural geometry.");return null; }
            var root=Root("ImportedBuilding");root.AddChild(art);return root;
        }
        finally { source.Free(); }
    }
    public static Node3D FinishLegacyBuilding(ContentCatalog catalog, BuildSelection selection, Node3D original, bool open)
    {
        var art=ImportedBuilding(catalog,selection,true,true,true,true,open,0);
        if(art!=null){original.Free();return art;}
        var appearance=BuildingAppearances.Find(catalog,selection);
        if(appearance==null || selection.DefinitionId!="wall")return original;
        var color=new Color(appearance.ColorHex);
        void Recolor(Node node)
        {
            if(node is MeshInstance3D mesh && mesh.MaterialOverride is StandardMaterial3D material && material.AlbedoColor.IsEqualApprox(new Color("9a8c6f")))mesh.MaterialOverride=Material(color);
            foreach(var child in node.GetChildren())Recolor(child);
        }
        Recolor(original);
        var kind=BuildingVariants.Variant(catalog,selection).Kind;
        if(appearance.Pattern is "plank" or "masonry")
        {
            var spans=kind=="door"?new[]{(-1.48f,-.7f),(.7f,1.48f)}:new[]{(-1.48f,1.48f)};
            foreach(var span in spans) foreach(float face in new[]{-.097f,.097f})
            for(int row=1;row<10;row++)
            {
                float y=row*.26f;
                Box(original,new((span.Item1+span.Item2)/2,y,face),new(span.Item2-span.Item1,.017f,.009f),color.Darkened(.27f));
                if(appearance.Pattern=="masonry")for(float x=span.Item1+(row%2==0?.32f:.04f);x<span.Item2;x+=.62f)
                    Box(original,new(x,y-.13f,face),new(.017f,.24f,.009f),color.Darkened(.25f));
            }
        }
        return original;
    }
}
