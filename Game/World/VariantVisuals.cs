using Godot;
using System;
using System.Collections.Generic;
using Thornwake.Core;

namespace Thornwake.World;

/// <summary>Data-selected procedural building geometry. The placement grid remains exactly three metres.</summary>
public static partial class Visuals
{
    private const float RoofOverhang = .18f;
    private const float RoofThickness = .10f;
    private static readonly HashSet<string> MissingVariantWarnings = new(StringComparer.Ordinal);

    private sealed record VariantContext(Node3D Root, BuildSelection Selection, string Geometry, string Kind, Color Color,
        bool North, bool East, bool South, bool West, float GableBottom, string Pattern)
    {
        public bool Slate => Pattern == "tile" || Pattern == "" && Selection.MaterialId.Contains("slate", StringComparison.OrdinalIgnoreCase);
        public float MinX => -1.5f - (West ? RoofOverhang : 0);
        public float MaxX => 1.5f + (East ? RoofOverhang : 0);
        public float Surface(float x, float z) => RoofGeometry.SurfaceHeight(Geometry, Selection.Slope, x, z);
        public Vector3 Point(float x, float z, float offset = 0) => new(x, Surface(x, z) + offset, z);
    }

    // Adding geometry does not require adding another branch to the world's structure renderer.
    private static readonly IReadOnlyDictionary<string, Action<VariantContext>> VariantGeometryFactories =
        new Dictionary<string, Action<VariantContext>>(StringComparer.Ordinal)
        {
            ["roof_slope"] = BuildSlopeVariant,
            ["roof_ridge"] = BuildRidgeVariant,
            ["roof_eave"] = BuildEaveVariant,
            ["roof_gable_edge"] = BuildGableVariant,
            ["roof_hip"] = BuildFoldedRoofVariant,
            ["roof_valley"] = BuildFoldedRoofVariant,
            ["roof_gable_fill"] = BuildGableFillVariant,
            ["roof_gable_ridge_fill"] = BuildGableFillVariant,
            ["legacy"] = BuildLegacyRoofVariant
        };

    private static readonly IReadOnlyDictionary<string, Func<VariantContext, Node3D>> LegacyRoofFactories =
        new Dictionary<string, Func<VariantContext, Node3D>>(StringComparer.Ordinal)
        {
            ["roof"] = c => RoofTile("roof",c.North,c.East,c.South,c.West),
            ["roof_flat"] = c => RoofTile("roof_flat",c.North,c.East,c.South,c.West),
            ["roof_corner"] = c => RoofTile("roof_corner",c.North,c.East,c.South,c.West),
            ["roof_slope"] = c => RoofTile("roof_slope",c.North,c.East,c.South,c.West),
            ["roof_triangle"] = c => TriangleRoof(c.North,c.East,c.South)
        };

    // Existing non-roof assets are owned by Valley.StructureVisual until their tool families migrate.
    private static readonly HashSet<string> LegacyWorldKinds = new(StringComparer.Ordinal)
    {
        "foundation","foundation_triangle","upper_floor","upper_floor_triangle","wall","wall_diagonal",
        "door","stairs","campfire","workbench","fence","carpenter","kiln","forge","cooking","oven",
        "chair","table","planter","painting","lantern","rug"
    };

    private static string VariantFactoryKey(BuildingVariantDefinition definition)
        => string.IsNullOrEmpty(definition.Mesh) ? definition.Geometry : definition.Mesh;

    /// <summary>Checks visual asset references independently of the validated physical geometry.</summary>
    public static string[] ValidateBuildingAssets(ContentCatalog catalog)
    {
        var diagnostics = new List<string>();
        foreach(var tool in catalog.BuildingTools.Values)
        foreach(var variant in tool.Variants)
        {
            string key=VariantFactoryKey(variant);
            // Legacy tools outside the roof family still use Valley's established visual mapping.
            if(key=="legacy"&&tool.Id!="roof"&&LegacyWorldKinds.Contains(variant.Kind))continue;
            if(!VariantGeometryFactories.ContainsKey(key))
                diagnostics.Add($"Building tool '{tool.Id}', variant '{variant.Id}' references missing visual asset '{key}'. A magenta marker will be shown; physical geometry '{variant.Geometry}' remains available.");
            else if(key=="legacy"&&!LegacyRoofFactories.ContainsKey(variant.Kind))
                diagnostics.Add($"Building tool '{tool.Id}', variant '{variant.Id}' has no registered legacy visual for kind '{variant.Kind}'. A magenta marker is available through BuildVariant.");
        }
        foreach(var tool in catalog.BuildingTools.Values)
        foreach(var appearance in tool.Appearances)
        foreach(var model in appearance.Models)
            if(!ResourceLoader.Exists(model.Scene))diagnostics.Add($"Missing cosmetic model '{model.Scene}' for {tool.Id}/{appearance.Id}/{model.Variant}/{model.Slope}. Procedural geometry will be retained.");
        return diagnostics.ToArray();
    }

    public static Node3D BuildVariant(ContentCatalog catalog, BuildSelection selection,
        bool north = true, bool east = true, bool south = true, bool west = true, float gableBottom = 0)
    {
        var definition = BuildingVariants.Variant(catalog, selection);
        var material = BuildingVariants.Material(catalog, selection);
        var imported=ImportedBuilding(catalog,selection,north,east,south,west,false,gableBottom);
        if(imported!=null)return imported;
        var appearance=BuildingAppearances.Find(catalog,selection);
        var root = Root("BuildingVariant");
        string factoryKey=VariantFactoryKey(definition);
        if (!VariantGeometryFactories.TryGetValue(factoryKey, out var factory))
        {
            MissingVariantGeometry(root, factoryKey);
            return root;
        }
        factory(new VariantContext(root, selection, definition.Geometry, definition.Kind, new Color(appearance?.ColorHex??material.ColorHex),
            north, east, south, west, gableBottom, appearance?.Pattern??""));
        return root;
    }

    private static void BuildLegacyRoofVariant(VariantContext context)
    {
        if(!LegacyRoofFactories.TryGetValue(context.Kind,out var factory))
        {
            MissingVariantGeometry(context.Root,"legacy:"+context.Kind);
            return;
        }
        var roof=factory(context);context.Root.AddChild(roof);
        void Tint(Node node)
        {
            if(node is MeshInstance3D mesh&&mesh.MaterialOverride is StandardMaterial3D material)
            {
                var color=material.AlbedoColor;
                // Existing roof surfaces use moss/slate hues; structural brown timber stays brown.
                if(color.G>color.R*1.035f&&color.G>color.B)
                {
                    float shade=(color.R+color.G+color.B)/3-.37f;
                    mesh.MaterialOverride=Material(shade<0?context.Color.Darkened(Math.Min(-shade,.2f)):context.Color.Lightened(Math.Min(shade,.2f)));
                }
            }
            foreach(var child in node.GetChildren())Tint(child);
        }
        Tint(roof);
    }

    private sealed class RoofSurfaceBatch
    {
        private readonly SurfaceTool _surface = new();
        public RoofSurfaceBatch(){_surface.Begin(Godot.Mesh.PrimitiveType.Triangles);_surface.SetSmoothGroup(uint.MaxValue);}
        public void Triangle(Vector3 a,Vector3 b,Vector3 c,Color color)
        {
            if((b-a).Cross(c-a).LengthSquared()<.000000001f)return;
            // Callers describe outward faces with a conventional cross product. Godot's
            // front faces and generated normals use clockwise winding, so reverse here.
            foreach(var point in new[]{a,c,b}){_surface.SetColor(color);_surface.AddVertex(point);}
        }
        public void Face(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
        {
            Triangle(a,b,c,color);Triangle(a,c,d,color);
        }
        public void Finish(Node3D root)
        {
            _surface.GenerateNormals();
            root.AddChild(new MeshInstance3D{Mesh=_surface.Commit(),MaterialOverride=new StandardMaterial3D{
                AlbedoColor=Colors.White,VertexColorUseAsAlbedo=true,VertexColorIsSrgb=true,Roughness=.94f,CullMode=BaseMaterial3D.CullModeEnum.Disabled}});
            _surface.Dispose();
        }
    }

    private static void VariantQuad(Node3D root,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
    {
        var surface=new RoofSurfaceBatch();surface.Face(a,b,c,d,color);surface.Finish(root);
    }

    private static void MissingVariantGeometry(Node3D root, string geometry)
    {
        if(MissingVariantWarnings.Add(geometry))
            GD.PushWarning($"Building variant has no visual asset factory: '{geometry}'. Showing a diagnostic marker.");
        var magenta = new Color("ff28c8");
        foreach (float x in new[] { -1.5f, 1.5f })
        foreach (float z in new[] { -1.5f, 1.5f })
            Box(root, new(x, .5f, z), new(.045f, 1, .045f), magenta);
        foreach (float y in new[] { 0f, 1f })
        {
            foreach (float z in new[] { -1.5f, 1.5f }) Box(root, new(0, y, z), new(3, .045f, .045f), magenta);
            foreach (float x in new[] { -1.5f, 1.5f }) Box(root, new(x, y, 0), new(.045f, .045f, 3), magenta);
        }
        Beam(root, new(-1.4f, .03f, -1.4f), new(1.4f, 1, 1.4f), .045f, magenta);
        Beam(root, new(-1.4f, 1, 1.4f), new(1.4f, .03f, -1.4f), .045f, magenta);
    }

    private static void BuildSlopeVariant(VariantContext context)
    {
        float minZ = -1.5f - (context.North ? RoofOverhang : 0), maxZ = 1.5f;
        RoofPlane(context, minZ, maxZ);
        RoofEdge(context, context.MinX, minZ, context.MaxX, minZ, context.North);
        RoofEdge(context, context.MaxX, minZ, context.MaxX, maxZ, context.East);
        RoofEdge(context, context.MaxX, maxZ, context.MinX, maxZ, context.South);
        RoofEdge(context, context.MinX, maxZ, context.MinX, minZ, context.West);
    }

    private static void BuildRidgeVariant(VariantContext context)
    {
        float minZ = -1.5f - (context.North ? RoofOverhang : 0);
        float maxZ = 1.5f + (context.South ? RoofOverhang : 0);
        // Each face ends on the exact shared ridge. A quad across it would smooth away the pitch.
        RoofPlane(context, minZ, 0);
        RoofPlane(context, 0, maxZ);
        RoofEdge(context, context.MinX, minZ, context.MaxX, minZ, context.North);
        RoofEdge(context, context.MaxX, maxZ, context.MinX, maxZ, context.South);
        foreach ((float x, bool exposed) in new[] { (context.MinX, context.West), (context.MaxX, context.East) })
        {
            RoofEdge(context, x, minZ, x, 0, exposed);
            RoofEdge(context, x, 0, x, maxZ, exposed);
        }
        // The cap follows +X, and stops at adjoining gables without a projecting end plug.
        float peak = context.Surface(0, 0);
        float capDrop = context.Surface(0, 0) - context.Surface(0, .11f);
        var cap = context.Color.Lightened(context.Slate ? .06f : .13f);
        VariantQuad(context.Root, new(context.MinX, peak - capDrop + .035f, -.11f),
            new(context.MinX, peak + .04f, 0), new(context.MaxX, peak + .04f, 0),
            new(context.MaxX, peak - capDrop + .035f, -.11f), cap);
        VariantQuad(context.Root, new(context.MinX, peak + .04f, 0),
            new(context.MinX, peak - capDrop + .035f, .11f), new(context.MaxX, peak - capDrop + .035f, .11f),
            new(context.MaxX, peak + .04f, 0), cap.Darkened(.05f));
    }

    private static void RoofPlane(VariantContext context, float minZ, float maxZ)
    {
        float minX = context.MinX, maxX = context.MaxX;
        var surface = new RoofSurfaceBatch();
        // Top, underside and edge thickness are separate faces: roofs remain readable from inside.
        RoofPatch(surface,context,minX,minZ,maxX,maxZ,0,context.Color.Darkened(.07f));
        RoofPatch(surface,context,minX,minZ,maxX,maxZ,-RoofThickness,DarkWood,true);

        float course = context.Pattern=="thatch" ? .7f : context.Slate ? .38f : .48f;
        float tileWidth = context.Pattern=="thatch" ? .085f : context.Slate ? .46f : .235f;
        int row = 0;
        // Stable grid anchoring gives neighbouring modules matching courses and colour variation.
        float firstRow = MathF.Floor((minZ + 1.5f) / course) * course - 1.5f;
        for (float z = firstRow; z < maxZ - .001f; z += course, row++)
        {
            float z0 = Math.Max(minZ, z + .008f), z1 = Math.Min(maxZ, z + course - .008f);
            if (z1 <= z0) continue;
            int column = 0;
            float firstColumn = -1.5f - tileWidth + (row % 2) * tileWidth / 2;
            for (float x = firstColumn; x < maxX - .001f; x += tileWidth, column++)
            {
                float x0 = Math.Max(minX, x + .006f), x1 = Math.Min(maxX, x + tileWidth - .006f);
                if (x1 <= x0) continue;
                float shade = ((column * 7 + row * 3) % 5 - 2) * (context.Slate ? .016f : .034f);
                var color = shade < 0 ? context.Color.Darkened(-shade) : context.Color.Lightened(shade);
                RoofPatch(surface,context,x0,z0,x1,z1,.012f,color);
                if (!context.Slate && x1 - x0 > .12f && z1-z0>.06f)
                {
                    float grainX = x0 + (x1 - x0) * .38f;
                    RoofPatch(surface,context,grainX,z0+.025f,grainX+.009f,z1-.025f,.014f,color.Darkened(.1f));
                }
            }
        }
        surface.Finish(context.Root);
        // Rafters stop flush at shared edges and sit wholly below the sampled roof face.
        foreach (float x in new[] { -1.2f, 0, 1.2f })
        {
            if(IsFoldedRoof(context)&&x>minZ&&x<maxZ)
            {
                Beam(context.Root,context.Point(x,minZ,-.13f),context.Point(x,x,-.13f),.047f,Wood);
                Beam(context.Root,context.Point(x,x,-.13f),context.Point(x,maxZ,-.13f),.047f,Wood);
            }
            else Beam(context.Root, context.Point(x, minZ, -.13f), context.Point(x, maxZ, -.13f), .047f, Wood);
        }
    }

    private static bool IsFoldedRoof(VariantContext context)=>context.Geometry is "roof_hip" or "roof_valley";

    private static void RoofPatch(RoofSurfaceBatch surface,VariantContext context,float x0,float z0,float x1,float z1,float offset,Color color,bool underside=false)
    {
        var polygon=new List<Vector2>{new(x0,z0),new(x0,z1),new(x1,z1),new(x1,z0)};
        void Emit(List<Vector2> points)
        {
            if(points.Count<3)return;
            Vector3 Point(Vector2 p)=>context.Point(p.X,p.Y,offset);
            for(int i=1;i<points.Count-1;i++)
            {
                if(underside)surface.Triangle(Point(points[0]),Point(points[i+1]),Point(points[i]),color);
                else surface.Triangle(Point(points[0]),Point(points[i]),Point(points[i+1]),color);
            }
        }
        if(!IsFoldedRoof(context)){Emit(polygon);return;}
        // Tiles can straddle the crease. Clip their actual footprint before sampling either plane,
        // otherwise a triangulated quad can bridge a valley or flatten the hip's diagonal ridge.
        List<Vector2> Half(bool lower)
        {
            var result=new List<Vector2>();
            Vector2 previous=polygon[^1];float previousSide=previous.X-previous.Y;
            bool previousInside=lower?previousSide<=0:previousSide>=0;
            foreach(var point in polygon)
            {
                float side=point.X-point.Y;bool inside=lower?side<=0:side>=0;
                if(inside!=previousInside)
                {
                    float t=previousSide/(previousSide-side);
                    result.Add(previous.Lerp(point,t));
                }
                if(inside)result.Add(point);
                previous=point;previousSide=side;previousInside=inside;
            }
            return result;
        }
        Emit(Half(true));Emit(Half(false));
    }

    private static void BuildFoldedRoofVariant(VariantContext context)
    {
        float minZ=-1.5f-(context.North?RoofOverhang:0),maxZ=1.5f+(context.South?RoofOverhang:0);
        RoofPlane(context,minZ,maxZ);
        RoofEdge(context,context.MinX,minZ,context.MaxX,minZ,context.North);
        RoofEdge(context,context.MaxX,minZ,context.MaxX,maxZ,context.East);
        RoofEdge(context,context.MaxX,maxZ,context.MinX,maxZ,context.South);
        RoofEdge(context,context.MinX,maxZ,context.MinX,minZ,context.West);
        float begin=Math.Max(context.MinX,minZ),end=Math.Min(context.MaxX,maxZ);
        // A restrained hip cap or valley flashing follows the exact diagonal, with no bridging plane.
        var seam=context.Geometry=="roof_valley"?Iron.Lightened(.10f):context.Color.Lightened(.11f);
        Beam(context.Root,context.Point(begin,begin,.035f),context.Point(end,end,.035f),context.Geometry=="roof_valley"?.023f:.035f,seam);
    }

    private static void RoofEdge(VariantContext context, float x0, float z0, float x1, float z1, bool exposed)
    {
        if (!exposed) return;
        float side0=x0-z0,side1=x1-z1;
        if(IsFoldedRoof(context)&&side0*side1<-.000001f)
        {
            float t=side0/(side0-side1),x=Mathf.Lerp(x0,x1,t),z=Mathf.Lerp(z0,z1,t);
            RoofEdge(context,x0,z0,x,z,true);RoofEdge(context,x,z,x1,z1,true);return;
        }
        Vector3 a = context.Point(x0, z0), b = context.Point(x1, z1);
        VariantQuad(context.Root, a, b, b - Vector3.Up * RoofThickness, a - Vector3.Up * RoofThickness, Wood);
        Beam(context.Root, a - Vector3.Up * .08f, b - Vector3.Up * .08f, .038f, DarkWood);
    }

    private static void BuildEaveVariant(VariantContext context)
    {
        float x0 = context.MinX, x1 = context.MaxX, z = -1.5f - (context.North?RoofOverhang:0);
        // Decorative fascia wraps the parent's low edge; it does not add another roof plane.
        float y = RoofGeometry.SurfaceHeight("roof_slope", context.Selection.Slope, 0, z);
        Box(context.Root, new((x0 + x1) / 2, y - .075f, z), new(x1 - x0, .19f, .105f), DarkWood);
        Box(context.Root, new((x0 + x1) / 2, y - .018f, z - .058f), new(x1 - x0, .052f, .023f), context.Color.Lightened(.13f));
        Box(context.Root, new((x0 + x1) / 2, y - .148f, z - .037f), new(x1 - x0, .034f, .055f), PaleWood);
        for (int i = 0; i < 6; i++)
        {
            float x = -1.25f + i * .5f;
            Box(context.Root, new(x, y - .23f, z + .075f), new(.065f, .15f, .19f), Wood);
        }
    }

    private static void BuildGableVariant(VariantContext context)
    {
        bool mirrored = context.Selection.Mirrored;
        bool exposed=mirrored?context.East:context.West;
        float x = (mirrored ? 1 : -1) * (1.5f + (exposed?RoofOverhang:0));
        float minZ = -1.5f - (context.North ? RoofOverhang : 0), maxZ = 1.5f;
        Vector3 low = context.Point(x, minZ), high = context.Point(x, maxZ);
        if(mirrored)
            VariantQuad(context.Root, low + Vector3.Up * .045f, high + Vector3.Up * .045f,
                high - Vector3.Up * .17f, low - Vector3.Up * .17f, context.Color.Darkened(.12f));
        else
            VariantQuad(context.Root, high + Vector3.Up * .045f, low + Vector3.Up * .045f,
                low - Vector3.Up * .17f, high - Vector3.Up * .17f, context.Color.Darkened(.12f));
        Beam(context.Root, low - Vector3.Up * .115f, high - Vector3.Up * .115f, .049f, DarkWood);
        Beam(context.Root, low + Vector3.Up * .033f, high + Vector3.Up * .033f, .026f, context.Color.Lightened(.16f));
        // Small end blocks make the separately crafted bargeboard recognizable at normal zoom.
        foreach (var end in new[] { low, high })
            Box(context.Root, end - Vector3.Up * .075f, new(.095f, .28f, .095f), PaleWood);
    }

    private static void BuildGableFillVariant(VariantContext context)
    {
        float x=context.Selection.Mirrored?1.5f:-1.5f;
        float bottom=Math.Min(0,context.GableBottom);
        float Top(float z)=>RoofGeometry.GableTop(context.Geometry,context.Selection.Slope,z);
        var surface=new RoofSurfaceBatch();var plaster=context.Color.Lightened(.08f);
        float[] divisions=context.Geometry=="roof_gable_ridge_fill"?new[]{-1.5f,0f,1.5f}:new[]{-1.5f,1.5f};
        for(int i=0;i<divisions.Length-1;i++)
        {
            float z0=divisions[i],z1=divisions[i+1],h0=Top(z0),h1=Top(z1);
            // Both plaster faces, underside and top form a closed .18 m thick panel.
            surface.Face(new(x-.09f,bottom,z0),new(x-.09f,bottom,z1),new(x-.09f,h1,z1),new(x-.09f,h0,z0),plaster);
            surface.Face(new(x+.09f,bottom,z1),new(x+.09f,bottom,z0),new(x+.09f,h0,z0),new(x+.09f,h1,z1),plaster.Darkened(.035f));
            surface.Face(new(x-.09f,h0,z0),new(x-.09f,h1,z1),new(x+.09f,h1,z1),new(x+.09f,h0,z0),Wood);
            surface.Face(new(x-.09f,bottom,z1),new(x-.09f,bottom,z0),new(x+.09f,bottom,z0),new(x+.09f,bottom,z1),DarkWood);
            // The timber upper frame is clipped to the same profile; it never protrudes through a steep roof.
            foreach(float side in new[]{-1f,1f})
            {
                float face=x+side*.092f;
                Vector3 a=new(face,Math.Max(bottom,h0-.13f),z0),b=new(face,Math.Max(bottom,h1-.13f),z1),c=new(face,h1,z1),d=new(face,h0,z0);
                if(side<0)surface.Face(a,b,c,d,DarkWood);else surface.Face(d,c,b,a,DarkWood);
            }
        }
        foreach(float z in new[]{-1.5f,1.5f})
        {
            Vector3 a=new(x-.09f,bottom,z),b=new(x-.09f,Top(z),z),c=new(x+.09f,Top(z),z),d=new(x+.09f,bottom,z);
            if(z<0)surface.Face(a,b,c,d,Wood);else surface.Face(d,c,b,a,Wood);
        }
        surface.Finish(context.Root);
        Box(context.Root,new(x,bottom+.0275f,0),new(.20f,.055f,3),DarkWood);
        foreach(float z in new[]{-1.42f,-.75f,0f,.75f,1.42f})
        {
            float height=Math.Min(Top(z-.055f),Top(z+.055f))-bottom;
            if(height>.075f)Box(context.Root,new(x,bottom+height/2,z),new(.20f,height,.11f),Wood);
        }
        void Brace(float z0,float z1)
        {
            float y0=bottom+Math.Min(.18f,(Top(z0)-bottom)*.3f),y1=bottom+(Top(z1)-bottom)*.65f;
            foreach(float side in new[]{-1f,1f})Beam(context.Root,new(x+side*.105f,y0,z0),new(x+side*.105f,y1,z1),.026f,Wood);
        }
        Brace(-1.1f,-.15f);
        if(context.Geometry=="roof_gable_ridge_fill")Brace(1.1f,.15f);
        else Brace(.35f,1.27f);
    }
}
