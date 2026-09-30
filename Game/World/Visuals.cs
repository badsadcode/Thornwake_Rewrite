using Godot;
using System;
using System.Collections.Generic;

namespace Thornwake.World;

/// <summary>Original, self-contained scenery. All roots use metres with Y up and feet at zero.</summary>
public static partial class Visuals
{
    private static readonly Color Wood = new("654735");
    private static readonly Color DarkWood = new("3b3028");
    private static readonly Color PaleWood = new("98704a");
    private static readonly Color Stone = new("7c8278");
    private static readonly Color Iron = new("41494b");
    private static readonly Color Plaster = new("c8b899");
    private static readonly Dictionary<Color, StandardMaterial3D> Materials = new();
    public static void ReleaseMaterials(){foreach(var material in Materials.Values)material.Dispose();Materials.Clear();}

    public static StandardMaterial3D Material(Color c)
    {
        if (Materials.TryGetValue(c, out var material)) return material;
        material = new StandardMaterial3D { AlbedoColor = c, Roughness = .94f };
        Materials[c] = material;
        return material;
    }

    public static MeshInstance3D Box(Node3D parent, Vector3 pos, Vector3 size, Color color)
        => Mesh(parent, new BoxMesh { Size = size }, pos, color);

    private static MeshInstance3D Mesh(Node3D parent, Godot.Mesh mesh, Vector3 pos, Color color)
    {
        var node = new MeshInstance3D { Mesh = mesh, Position = pos, MaterialOverride = Material(color) };
        parent.AddChild(node);
        return node;
    }

    private static MeshInstance3D Cylinder(Node3D parent, Vector3 pos, float bottom, float top,
        float height, Color color, int sides = 9)
        => Mesh(parent, new CylinderMesh { BottomRadius = bottom, TopRadius = top,
            Height = height, RadialSegments = sides, Rings = 1 }, pos, color);

    private static MeshInstance3D Ball(Node3D parent, Vector3 pos, Vector3 size, Color color, int sides = 9)
    {
        var node = Mesh(parent, new SphereMesh { Radius = .5f, Height = 1, RadialSegments = sides, Rings = 4 }, pos, color);
        node.Scale = size;
        return node;
    }

    private static void Beam(Node3D parent, Vector3 a, Vector3 b, float radius, Color color, int sides = 7)
    {
        var difference = b - a;
        var node = Cylinder(parent, (a + b) * .5f, radius, radius * .85f, difference.Length(), color, sides);
        var direction = difference.Normalized();
        var axis = Vector3.Up.Cross(direction);
        node.Quaternion = axis.LengthSquared() > .00001f
            ? new Quaternion(axis.Normalized(), Mathf.Acos(Mathf.Clamp(Vector3.Up.Dot(direction), -1, 1)))
            : direction.Y >= 0 ? Quaternion.Identity : new Quaternion(Vector3.Right, Mathf.Pi);
    }

    private static Node3D Root(string name, float scale = 1) => new() { Name = name, Scale = Vector3.One * scale };

    public static Node3D Tree(string kind, float scale = 1)
    {
        var root = Root("Tree", scale);
        bool pine = kind.Contains("pine", StringComparison.OrdinalIgnoreCase)
            || kind.Contains("fir", StringComparison.OrdinalIgnoreCase);
        bool birch = kind.Contains("birch", StringComparison.OrdinalIgnoreCase);
        var bark = birch ? new Color("b9b8a6") : Wood;
        Cylinder(root, new(0, 1.9f, 0), .26f, .12f, 3.8f, bark);
        for (int i = 0; i < 5; i++)
        {
            float a = i * Mathf.Tau / 5f;
            Beam(root, new(0, .35f, 0), new(Mathf.Cos(a) * .52f, .04f, Mathf.Sin(a) * .52f), .13f, bark);
        }
        if (pine)
        {
            // Overlapping tapered whorls retain a conifer silhouette without a voxel canopy.
            for (int i = 0; i < 5; i++)
            {
                float y = 1.55f + i * .73f;
                float r = 1.58f - i * .23f;
                var crown = Cylinder(root, new(.09f * Mathf.Sin(i * 2), y + .8f, .06f * Mathf.Cos(i)), r, .02f,
                    1.9f, new Color(.18f + i * .016f, .30f + i * .016f, .24f + i * .005f), 9);
                crown.Rotation = new(.025f * i, i * .63f, .035f * Mathf.Sin(i));
            }
        }
        else
        {
            for (int i = 0; i < 6; i++)
            {
                float a = i * 2.39996f;
                var tip = new Vector3(Mathf.Cos(a) * (i % 2 == 0 ? 1.05f : .8f), 2.8f + (i % 3) * .45f,
                    Mathf.Sin(a) * 1.03f);
                Beam(root, new(0, 1.6f + i * .16f, 0), tip, .115f, bark);
                var leaf = birch ? new Color(.39f + i * .016f, .48f + i * .012f, .22f)
                    : new Color(.25f + i * .018f, .39f + i * .014f, .22f + i * .007f);
                var crown = Ball(root, tip + Vector3.Up * .38f, new(2.15f, 1.72f + i % 2 * .3f, 1.95f), leaf, 8);
                crown.Rotation = new(0, a, 0);
            }
            Ball(root, new(.12f, 4.2f, -.1f), new(2, 1.6f, 1.9f), new("647d3c"), 8);
            if (birch)
                for (int i = 0; i < 7; i++) Box(root, new(.012f, .52f + i * .4f, .163f), new(.25f, .055f, .035f), DarkWood);
        }
        return root;
    }

    public static Node3D Rock(float scale = 1)
    {
        var root = Root("Rock", scale);
        var main = Ball(root, new(0, .37f, 0), new(1.55f, 1.12f, 1.35f), Stone, 6);
        main.Rotation = new(.13f, .28f, -.17f);
        var side = Ball(root, new(.56f, .18f, .3f), new(.85f, .59f, .75f), new("6c766c"), 6);
        side.Rotation = new(0, .68f, 0);
        Ball(root, new(-.38f, .76f, -.05f), new(.54f, .10f, .6f), new("66744f"), 6);
        return root;
    }

    /// <summary>A single reusable tuft for inexpensive terrain MultiMesh instances.</summary>
    public static ArrayMesh GrassMesh()
    {
        var surface = new SurfaceTool();
        surface.Begin(Godot.Mesh.PrimitiveType.Triangles);
        for (int i = 0; i < 5; i++)
        {
            float angle = i * 2.39996f;
            float height = .25f + i * .05f;
            var across = new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle));
            var bend = new Vector3(-Mathf.Sin(angle), 0, Mathf.Cos(angle)) * (.035f + i * .007f);
            var center = new Vector3(Mathf.Sin(i * 3.1f) * .115f, 0, Mathf.Cos(i * 2.7f) * .11f);
            var left = center - across * .035f;
            var right = center + across * .035f;
            var upperLeft = center + bend * .5f + Vector3.Up * height * .58f - across * .019f;
            var upperRight = center + bend * .5f + Vector3.Up * height * .58f + across * .019f;
            var tip = center + bend + Vector3.Up * height;
            var low = new Color("53673b");
            var high = new Color("8d9a56");
            void Vertex(Vector3 vertex, Color color) { surface.SetColor(color); surface.AddVertex(vertex); }
            Vertex(left, low); Vertex(right, low); Vertex(upperRight, high);
            Vertex(left, low); Vertex(upperRight, high); Vertex(upperLeft, high);
            Vertex(upperLeft, high); Vertex(upperRight, high); Vertex(tip, high.Lightened(.06f));
        }
        surface.GenerateNormals();
        surface.SetMaterial(new StandardMaterial3D {
            AlbedoColor = Colors.White,
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 1,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled
        });
        return surface.Commit();
    }

    public static Node3D Stump(float scale = 1)
    {
        var root = Root("Stump", scale);
        Cylinder(root, new(0, .27f, 0), .40f, .32f, .54f, Wood, 9);
        for (int i = 0; i < 5; i++)
        {
            float angle = i * Mathf.Tau / 5 + .27f;
            Beam(root, new(Mathf.Cos(angle) * .16f, .22f, Mathf.Sin(angle) * .16f),
                new(Mathf.Cos(angle) * .64f, .055f, Mathf.Sin(angle) * .64f), .115f, Wood);
        }
        Cylinder(root, new(0, .547f, 0), .315f, .315f, .018f, new("b39362"), 9);
        Cylinder(root, new(0, .558f, 0), .252f, .252f, .008f, new("9b794f"), 12);
        Cylinder(root, new(0, .564f, 0), .222f, .222f, .007f, new("b39362"), 12);
        Cylinder(root, new(0, .570f, 0), .142f, .142f, .007f, new("9b794f"), 12);
        Cylinder(root, new(0, .576f, 0), .113f, .113f, .007f, new("b39362"), 12);
        Box(root, new(.20f, .584f, .04f), new(.19f, .008f, .013f), Wood);
        return root;
    }

    public static Node3D Tool(int slot)
    {
        var root=Root("HeldTool");
        if(slot==3){Ball(root,new(0,-.5f,.12f),new(.24f,.30f,.20f),new Color("997947"));return root;}
        Beam(root,new(0,-.49f,-.08f),new(0,-.49f,.65f),.035f,Wood);
        if(slot==0) {Box(root,new(.06f,-.49f,.55f),new(.34f,.07f,.24f),Iron);Box(root,new(.21f,-.49f,.55f),new(.055f,.07f,.30f),new Color("a5b2ac"));}
        if(slot==1)Box(root,new(0,-.49f,.55f),new(.36f,.18f,.20f),new Color("786752"));
        if(slot==2)Box(root,new(0,-.42f,.58f),new(.29f,.26f,.07f),Iron);
        if(slot==5){Box(root,new(0,-.49f,.74f),new(.28f,.045f,.34f),Wood);Box(root,new(0,-.49f,.9f),new(.24f,.05f,.04f),Iron);Beam(root,new(-.1f,-.49f,-.08f),new(.1f,-.49f,-.08f),.035f,Wood);}
        if(slot==4){Box(root,new(0,-.49f,.22f),new(.24f,.05f,.06f),Iron);Box(root,new(0,-.49f,.66f),new(.09f,.035f,.78f),new Color("a5b2ac"));}
        return root;
    }

    public static Node3D House(float width = 5, float depth = 5, bool workshop = false)
    {
        var root = Root(workshop ? "Workshop" : "Cottage");
        float x = width / 2, z = depth / 2, h = 2.65f, ridge = h + width * .39f;
        Box(root, new(0, .14f, 0), new(width + .22f, .28f, depth + .22f), new("76756b"));
        Box(root, new(0, .29f, 0), new(width, .12f, depth), PaleWood);
        Box(root, new(-x + .1f, h / 2, 0), new(.2f, h, depth), Plaster);
        Box(root, new(x - .1f, h / 2, 0), new(.2f, h, depth), Plaster);
        Box(root, new(0, h / 2, -z + .1f), new(width, h, .2f), Plaster);
        // Open door and small interior make approaching the entrance feel believable.
        float door = 1.1f;
        Box(root, new(-(x + door / 2) / 2, h / 2, z - .1f), new(x - door / 2, h, .2f), Plaster);
        Box(root, new((x + door / 2) / 2, h / 2, z - .1f), new(x - door / 2, h, .2f), Plaster);
        Box(root, new(0, h - .21f, z - .1f), new(door, .42f, .2f), Plaster);
        foreach (float a in new[] { -x, x })
        foreach (float b in new[] { -z, z }) Box(root, new(a, h / 2, b), new(.19f, h + .14f, .19f), DarkWood);
        foreach (float b in new[] { -z - .018f, z + .018f })
        {
            Box(root, new(0, .55f, b), new(width, .15f, .15f), Wood);
            Box(root, new(0, h, b), new(width + .12f, .19f, .18f), DarkWood);
            // Gable plaster and structural rafters.
            Triangle(root, new(-x, h, b), new(x, h, b), new(0, ridge - .06f, b), Plaster);
            Beam(root, new(-x, h, b), new(0, ridge, b), .105f, DarkWood);
            Beam(root, new(x, h, b), new(0, ridge, b), .105f, DarkWood);
            Beam(root, new(0, h, b), new(0, ridge, b), .08f, DarkWood);
        }
        foreach (float a in new[] { -x - .02f, x + .02f })
        {
            Box(root, new(a, .55f, 0), new(.14f, .14f, depth), Wood);
            Box(root, new(a, h, 0), new(.18f, .18f, depth + .16f), DarkWood);
            for (int i = -1; i <= 1; i++) Box(root, new(a, 1.55f, i * depth * .29f), new(.14f, 2.14f, .14f), Wood);
            SideWindow(root, a * 1.014f, 1.6f, 0);
        }
        var roof = workshop ? new Color("555e57") : new Color("755544");
        float overhang = .4f;
        Quad(root, new(-x - overhang, h - .08f, -z - overhang), new(-x - overhang, h - .08f, z + overhang),
            new(0, ridge, z + overhang), new(0, ridge, -z - overhang), roof);
        Quad(root, new(0, ridge, -z - overhang), new(0, ridge, z + overhang),
            new(x + overhang, h - .08f, z + overhang), new(x + overhang, h - .08f, -z - overhang), roof.Darkened(.08f));
        // Rows of low-profile battens catch light and break up the large roof surfaces.
        for (int i = 1; i < 8; i++)
        {
            float t = i / 8f;
            float yy = Mathf.Lerp(h - .07f, ridge, t);
            float xx = (x + overhang) * (1 - t);
            foreach (float sign in new[] { -1f, 1f })
                Box(root, new(xx * sign, yy + .035f, 0), new(.065f, .045f, depth + overhang * 2), roof.Lightened(.07f));
        }
        Beam(root, new(0, ridge + .02f, -z - .46f), new(0, ridge + .02f, z + .46f), .095f, DarkWood);
        Box(root, new(x * .61f, ridge - .25f, -z * .43f), new(.65f, 1.8f, .64f), new("858174"));
        Box(root, new(x * .61f, ridge + .67f, -z * .43f), new(.79f, .16f, .77f), new("aaa08c"));
        Box(root, new(x * .61f, ridge + .755f, -z * .43f), new(.43f, .015f, .43f), DarkWood);
        foreach (float a in new[] { -.64f, .64f }) Box(root, new(a, 1.24f, z + .065f), new(.14f, 2.45f, .22f), DarkWood);
        Box(root, new(0, 2.44f, z + .07f), new(1.42f, .15f, .22f), DarkWood);
        Box(root, new(0, .16f, z + .55f), new(1.55f, .32f, 1.15f), new("8c8776"));
        foreach (float a in new[] { -x * .61f, x * .61f }) FrontWindow(root, a, 1.55f, z + .045f);
        Box(root, new(0, 2.25f, z + .57f), new(1.75f, .13f, 1.23f), roof);
        foreach (float a in new[] { -.79f, .79f })
        {
            Box(root, new(a, 1.17f, z + 1.03f), new(.115f, 2.33f, .115f), Wood);
            Beam(root, new(a, 1.65f, z + 1.03f), new(a, 2.2f, z + .55f), .05f, Wood);
        }
        // Entrance lantern with warm glass is readable in evening light.
        Box(root, new(.87f, 1.8f, z + .16f), new(.17f, .27f, .19f), new("d9ae65"));
        Box(root, new(.87f, 1.96f, z + .16f), new(.23f, .05f, .23f), Iron);
        var glow = new OmniLight3D { Position = new(.87f, 1.85f, z + .45f), LightColor = new("ffd19a"),
            LightEnergy = .4f, OmniRange = 3, ShadowEnabled = false };
        root.AddChild(glow);
        if (workshop)
        {
            var bench = Workbench(); bench.Position = new(x + 1.05f, 0, .4f); bench.Rotation = new(0, Mathf.Pi / 2, 0); root.AddChild(bench);
            Box(root, new(x + 1.0f, .29f, -1.3f), new(.95f, .58f, .8f), Wood);
            for (int i = 0; i < 4; i++) Cylinder(root, new(x + .72f + (i % 2) * .28f, .67f + (i / 2) * .23f, -1.3f), .12f, .12f, .8f, PaleWood).Rotation = new(Mathf.Pi / 2, 0, 0);
        }
        return root;
    }

    private static void FrontWindow(Node3D root, float x, float y, float z)
    {
        Box(root, new(x, y, z), new(.78f, .87f, .06f), DarkWood);
        Box(root, new(x, y, z + .036f), new(.63f, .69f, .04f), new("9b9c80"));
        Box(root, new(x, y, z + .066f), new(.065f, .76f, .05f), Wood);
        Box(root, new(x, y, z + .066f), new(.68f, .065f, .05f), Wood);
        Box(root, new(x, y - .48f, z + .085f), new(.93f, .09f, .23f), PaleWood);
        foreach (float sign in new[] { -1f, 1f }) Box(root, new(x + sign * .53f, y, z), new(.22f, .82f, .07f), new("586d58"));
    }

    private static void SideWindow(Node3D root, float x, float y, float z)
    {
        var wall = new Node3D { Position = new(x, y, z), Rotation = new(0, x > 0 ? Mathf.Pi / 2 : -Mathf.Pi / 2, 0) };
        root.AddChild(wall); FrontWindow(wall, 0, 0, 0);
    }

    private static void Triangle(Node3D root, Vector3 a, Vector3 b, Vector3 c, Color color)
    {
        var st = new SurfaceTool(); st.Begin(Godot.Mesh.PrimitiveType.Triangles);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c); st.GenerateNormals();
        var material = new StandardMaterial3D { AlbedoColor = color, Roughness = .98f, CullMode = BaseMaterial3D.CullModeEnum.Disabled };
        root.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = material });
    }

    private static void Quad(Node3D root, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color color)
    {
        var st = new SurfaceTool(); st.Begin(Godot.Mesh.PrimitiveType.Triangles);
        st.AddVertex(a); st.AddVertex(b); st.AddVertex(c); st.AddVertex(a); st.AddVertex(c); st.AddVertex(d); st.GenerateNormals();
        root.AddChild(new MeshInstance3D { Mesh = st.Commit(), MaterialOverride = new StandardMaterial3D {
            AlbedoColor = color, Roughness = .96f, CullMode = BaseMaterial3D.CullModeEnum.Disabled } });
    }

    public static Node3D Person(Color tunic, bool armed = false)
    {
        var root = Root("Person");
        var skin = new Color("be9877");
        var trousers = new Color("554d41");
        foreach (float sign in new[] { -1f, 1f })
        {
            var leg = new Node3D { Name = sign < 0 ? "LeftLeg" : "RightLeg", Position = new(sign * .13f, .84f, 0) };
            root.AddChild(leg);
            Cylinder(leg, new(0, -.23f, 0), .103f, .11f, .46f, trousers, 7);
            Cylinder(leg, new(0, -.55f, 0), .085f, .1f, .28f, trousers, 7);
            Box(leg, new(0, -.73f, .045f), new(.2f, .20f, .31f), DarkWood);
            var arm = new Node3D { Name = sign < 0 ? "LeftArm" : "RightArm", Position = new(sign * .28f, 1.36f, 0), Rotation = new(0, 0, sign * .09f) };
            root.AddChild(arm);
            Cylinder(arm, new(0, -.15f, 0), .10f, .13f, .32f, tunic.Darkened(.07f), 7);
            Cylinder(arm, new(0, -.38f, 0), .067f, .09f, .20f, skin, 7);
            Ball(arm, new(0, -.51f, 0), new(.15f, .18f, .15f), skin, 7);
            if (armed && sign > 0)
            {
                Beam(arm, new(0, -.49f, -.09f), new(0, -.49f, .36f), .034f, Wood);
                Box(arm, new(0, -.49f, .24f), new(.24f, .05f, .06f), Iron);
                var blade = Box(arm, new(0, -.49f, .64f), new(.092f, .035f, .77f), new("b2c2bf"));
                blade.Rotation = new(0, 0, .3f);
            }
        }
        var torso = Cylinder(root, new(0, 1.10f, 0), .235f, .275f, .55f, tunic, 8); torso.Scale = new(1, 1, .68f);
        var hem = Cylinder(root, new(0, .88f, 0), .25f, .21f, .18f, tunic.Darkened(.05f), 8); hem.Scale = new(1, 1, .75f);
        Box(root, new(0, .99f, 0), new(.46f, .075f, .32f), DarkWood);
        Box(root, new(0, .99f, .17f), new(.10f, .085f, .025f), new("a8955e"));
        Cylinder(root, new(0, 1.435f, 0), .08f, .08f, .14f, skin, 8);
        Ball(root, new(0, 1.62f, 0), new(.34f, .39f, .31f), skin, 9);
        Ball(root, new(0, 1.735f, -.035f), new(.355f, .19f, .31f), new("49372b"), 8);
        Box(root, new(0, 1.65f, -.13f), new(.27f, .20f, .08f), new("49372b"));
        // Facing +Z, eyes and nose remain a quiet detail when the camera is close.
        foreach (float sign in new[] { -1f, 1f }) Box(root, new(sign * .074f, 1.646f, .149f), new(.028f, .025f, .015f), DarkWood);
        Box(root, new(0, 1.605f, .165f), new(.05f, .065f, .055f), skin.Lightened(.08f));
        return root;
    }

    public static Node3D Campfire()
    {
        var root = Root("Campfire");
        for (int i = 0; i < 10; i++)
        {
            float a = i * Mathf.Tau / 10;
            Ball(root, new(Mathf.Cos(a) * .6f, .12f, Mathf.Sin(a) * .6f), new(.31f, .24f, .27f), Stone.Darkened(i % 3 * .05f), 6);
        }
        Cylinder(root, new(0, .015f, 0), .51f, .5f, .028f, new("34312b"));
        for (int i = 0; i < 4; i++)
        {
            float a = .6f + i * 1.3f;
            Beam(root, new(Mathf.Cos(a) * -.45f, .12f, Mathf.Sin(a) * -.45f),
                new(Mathf.Cos(a) * .45f, .20f, Mathf.Sin(a) * .45f), .11f, DarkWood);
        }
        var flameMaterial = new StandardMaterial3D { AlbedoColor = new("ffa347"), EmissionEnabled = true,
            Emission = new("ff762d"), EmissionEnergyMultiplier = 2.2f, Roughness = 1 };
        for (int i = 0; i < 3; i++)
        {
            var flame = Cylinder(root, new((i - 1) * .17f, .39f + i % 2 * .12f, .08f * (i % 2)), .19f, .018f, .61f + i % 2 * .21f, new("ffb549"), 7);
            flame.Name = "Flame" + i; flame.MaterialOverride = flameMaterial;
            flame.Rotation = new(0, 0, (i - 1) * .18f);
        }
        root.AddChild(new OmniLight3D { Name = "Firelight", Position = new(0, .85f, 0), LightColor = new("ffb274"),
            LightEnergy = 1.35f, OmniRange = 6, ShadowEnabled = false });
        return root;
    }

    public static Node3D Workbench()
    {
        var root = Root("Workbench");
        foreach (float x in new[] { -.68f, .68f })
        foreach (float z in new[] { -.3f, .3f }) Box(root, new(x, .46f, z), new(.13f, .92f, .13f), Wood);
        Box(root, new(0, .91f, 0), new(1.75f, .17f, .91f), PaleWood);
        Box(root, new(0, .29f, 0), new(1.42f, .09f, .63f), Wood);
        Box(root, new(0, .48f, -.32f), new(1.4f, .10f, .1f), DarkWood);
        Box(root, new(-.47f, 1.06f, .05f), new(.34f, .15f, .35f), Iron);
        Box(root, new(-.47f, 1.18f, .05f), new(.50f, .10f, .24f), Iron.Lightened(.12f));
        var handle = Box(root, new(.28f, 1.027f, .04f), new(.055f, .05f, .42f), Wood); handle.Rotation = new(0, .7f, 0);
        Box(root, new(.40f, 1.055f, .18f), new(.23f, .12f, .12f), Iron);
        Box(root, new(.42f, .40f, 0), new(.39f, .16f, .44f), new("a29065"));
        return root;
    }

    public static Node3D Fence()
    {
        var root = Root("Fence");
        foreach (float x in new[] { -1.5f, 1.5f })
        {
            Box(root, new(x, .57f, 0), new(.16f, 1.14f, .18f), Wood);
            Cylinder(root, new(x, 1.18f, 0), .13f, 0, .12f, PaleWood, 4).Rotation = new(0, Mathf.Pi / 4, 0);
        }
        Box(root, new(0, .44f, 0), new(3, .12f, .11f), PaleWood);
        Box(root, new(0, .89f, 0), new(3, .12f, .11f), PaleWood);
        return root;
    }

    public static Node3D Foundation()
    {
        var root = Root("Foundation");
        Box(root, new(0, .10f, 0), new(3, .2f, 3), Stone);
        for (int i = 0; i < 10; i++) Box(root, new(-1.35f + i * .3f, .235f, 0), new(.285f, .09f, 2.88f), PaleWood.Darkened(i % 3 * .04f));
        foreach(float x in new[]{-1.45f,1.45f})Box(root,new(x,.22f,0),new(.12f,.14f,3),DarkWood);
        return root;
    }

    public static Node3D WallPanel(bool door=false,bool open=false)
    {
        var root=Root(door?"Doorway":"Wall");
        if(door)
        {
            foreach(float side in new[]{-1f,1f})Box(root,new(side*1.09f,1.3f,0),new(.82f,2.6f,.18f),new Color("9a8c6f"));
            Box(root,new(0,2.43f,0),new(1.35f,.34f,.2f),Wood);
            foreach(float side in new[]{-1f,1f})Box(root,new(side*.69f,1.16f,0),new(.1f,2.32f,.26f),DarkWood);
            var leaf=new Node3D{Name="DoorLeaf",Position=new Vector3(-.65f,0,0),Rotation=new Vector3(0,open?-Mathf.Pi/2:0,0)};root.AddChild(leaf);
            for(int i=0;i<6;i++)Box(leaf,new(.11f+i*.215f,1.12f,0),new(.205f,2.24f,.12f),PaleWood.Darkened(i%2*.05f));
            foreach(float y in new[]{.35f,1.9f})Box(leaf,new(.65f,y,.07f),new(1.25f,.12f,.05f),DarkWood);
            Box(leaf,new(1.13f,1.1f,.105f),new(.08f,.12f,.08f),Iron);
        }
        else
        {
            Box(root,new(0,1.3f,0),new(3,2.6f,.18f),new Color("9a8c6f"));
            Box(root,new(0,1.3f,.11f),new(.13f,2.6f,.08f),Wood);
            Beam(root,new(-1.4f,.18f,.12f),new(1.4f,2.44f,.12f),.05f,Wood);
        }
        foreach(float x in new[]{-1.45f,1.45f})Box(root,new(x,1.3f,0),new(.16f,2.6f,.25f),DarkWood);
        foreach(float y in new[]{.08f,2.54f})Box(root,new(0,y,0),new(3,.15f,.25f),Wood);
        return root;
    }
    public static Node3D DiagonalWall(){var root=WallPanel();root.Name="DiagonalWall";root.Scale=new Vector3(Mathf.Sqrt(2),1,1);return root;}
    public static Node3D TrianglePrism(float bottom,float top,Color color)
    {
        var root=Root("Triangle");Vector3[] p={new(-1.5f,top,-1.5f),new(1.5f,top,-1.5f),new(-1.5f,top,1.5f)};
        Triangle(root,p[0],p[2],p[1],color);
        Triangle(root,new(p[0].X,bottom,p[0].Z),new(p[1].X,bottom,p[1].Z),new(p[2].X,bottom,p[2].Z),color.Darkened(.18f));
        for(int i=0;i<3;i++){var a=p[i];var b=p[(i+1)%3];Quad(root,a,b,new(b.X,bottom,b.Z),new(a.X,bottom,a.Z),color.Darkened(.12f));}
        return root;
    }
    public static Node3D TriangleFloor()
    {
        var root=TrianglePrism(0,.28f,PaleWood);root.Name="TriangleFloor";
        for(int i=1;i<10;i++){float x=-1.5f+i*.3f;Beam(root,new(x,.287f,-1.5f),new(x,.287f,-x),.008f,DarkWood,4);}
        return root;
    }
    public static Node3D TriangleRoof(bool north=true,bool diagonal=true,bool west=true)
    {
        var root=TrianglePrism(-.06f,.17f,new Color("556658"));root.Name="TriangleRoof";
        for(int i=1;i<10;i++){float z=-1.5f+i*.3f;Beam(root,new(-1.5f,.182f,z),new(-z,.182f,z),.008f,new Color("8b998c"),4);}
        void Edge(Vector3 a,Vector3 b,bool exposed)
        {
            if(!exposed)return;
            Quad(root,new(a.X,-.06f,a.Z),new(b.X,-.06f,b.Z),b,a,Wood);
            Beam(root,a+Vector3.Up*.035f,b+Vector3.Up*.035f,.065f,DarkWood);
            Beam(root,a-Vector3.Up*.035f,b-Vector3.Up*.035f,.035f,new Color("a18b61"));
        }
        Edge(new(-1.5f,.17f,-1.5f),new(1.5f,.17f,-1.5f),north);
        Edge(new(1.5f,.17f,-1.5f),new(-1.5f,.17f,1.5f),diagonal);
        Edge(new(-1.5f,.17f,1.5f),new(-1.5f,.17f,-1.5f),west);
        return root;
    }
    public static Node3D Roof()
    {
        var root=Root("Roof");float e=1.67f,h=1.22f;
        Quad(root,new(-e,0,-e),new(-e,0,e),new(0,h,e),new(0,h,-e),new Color("5b644b"));
        Quad(root,new(0,h,-e),new(0,h,e),new(e,0,e),new(e,0,-e),new Color("525e46"));
        foreach(float z in new[]{-1.5f,1.5f})Triangle(root,new(-1.5f,.1f,z),new(1.5f,.1f,z),new(0,h,z),new Color("8a7a5d"));
        Beam(root,new(0,h+.02f,-e),new(0,h+.02f,e),.075f,DarkWood);
        for(int i=1;i<7;i++){float t=i/7f;foreach(float sign in new[]{-1f,1f})Box(root,new(sign*e*(1-t),h*t+.025f,0),new(.055f,.045f,e*2),new Color("73795d"));}
        foreach(float x in new[]{-e,e})Box(root,new(x,-.04f,0),new(.13f,.18f,e*2),Wood);
        return root;
    }

    /// <summary>Three metre roof module. Edge flags are local -Z, +X, +Z, -X respectively.</summary>
    public static Node3D RoofTile(string kind, bool northEdge, bool eastEdge, bool southEdge, bool westEdge)
    {
        var root = Root("RoofTile");
        var slate = new Color("556658");
        float Height(float x, float z)
        {
            if (kind == "roof_slope") return .17f + (z + 1.5f) * .36f;
            if (kind == "roof_corner") return .17f + Mathf.Min(x + 1.5f, z + 1.5f) * .36f;
            return .17f;
        }
        Vector3 Top(float x, float z) => new(x, Height(x,z), z);
        // Shared sides stop exactly on the grid. A flat cap can cover any connected footprint,
        // including inside corners, without adding a new gable to every floor tile.
        Quad(root, Top(-1.5f,-1.5f), Top(-1.5f,1.5f), Top(1.5f,1.5f), Top(1.5f,-1.5f), slate);
        Box(root, new(0,-.035f,0), new(3,.07f,3), DarkWood);
        for (int row = 0; row < 6; row++)
        for (int column = 0; column < 6; column++)
        {
            float x0=-1.5f+column*.5f+.016f, x1=x0+.468f;
            float z0=-1.5f+row*.5f+.012f, z1=z0+.476f;
            Vector3 Tile(float x,float z) => Top(x,z)+Vector3.Up*.012f;
            // The diagonal ridge of the corner module stays continuous through its slate courses.
            if(kind=="roof_corner" && column==row)
            {
                Triangle(root,Tile(x0,z0),Tile(x0,z1),Tile(x1,z1),slate.Lightened((row%3)*.025f));
                Triangle(root,Tile(x0,z0),Tile(x1,z1),Tile(x1,z0),slate.Lightened((row%3)*.025f));
            }
            else Quad(root,Tile(x0,z0),Tile(x0,z1),Tile(x1,z1),Tile(x1,z0),slate.Lightened(((row+column)%3)*.025f));
        }
        void Edge(Vector3 a,Vector3 b,bool exposed)
        {
            if(!exposed)return;
            Quad(root,new(a.X,-.06f,a.Z),new(b.X,-.06f,b.Z),b,a,Wood);
            Beam(root,a+Vector3.Up*.035f,b+Vector3.Up*.035f,.065f,DarkWood);
            Beam(root,a-Vector3.Up*.035f,b-Vector3.Up*.035f,.035f,new Color("a18b61"));
        }
        Edge(Top(-1.5f,-1.5f),Top(1.5f,-1.5f),northEdge);
        Edge(Top(1.5f,-1.5f),Top(1.5f,1.5f),eastEdge);
        Edge(Top(1.5f,1.5f),Top(-1.5f,1.5f),southEdge);
        Edge(Top(-1.5f,1.5f),Top(-1.5f,-1.5f),westEdge);
        return root;
    }

    /// <summary>Timber deck, 3 x 3 metres, with its walking surface at .28 metres.</summary>
    public static Node3D UpperFloor()
    {
        var root=Root("UpperFloor");
        foreach(float z in new[]{-1.40f,0,1.40f})Box(root,new(0,.10f,z),new(3,.20f,.14f),DarkWood);
        foreach(float x in new[]{-1.43f,1.43f})Box(root,new(x,.10f,0),new(.14f,.20f,3),Wood);
        for(int i=0;i<10;i++)Box(root,new(-1.35f+i*.3f,.235f,0),new(.293f,.09f,3),PaleWood.Darkened(i%3*.045f));
        return root;
    }

    /// <summary>1.25 m wide straight stair: local -Z is low, +Z high; depth 3 m, rise 2.88 m.</summary>
    public static Node3D Stairs()
    {
        var root=Root("Stairs");
        const int steps=16;
        for(int i=0;i<steps;i++)
        {
            float z=-1.5f+(i+.5f)*3/steps, y=(i+1)*2.88f/steps;
            Box(root,new(0,y-.055f,z),new(1.25f,.11f,3f/steps+.015f),PaleWood.Darkened(i%3*.035f));
            Box(root,new(0,y-.12f,z-.087f),new(1.18f,.14f,.035f),Wood);
        }
        foreach(float x in new[]{-.565f,.565f})
        {
            Beam(root,new(x,.025f,-1.48f),new(x,2.73f,1.48f),.09f,DarkWood);
            for(int i=0;i<5;i++)
            {
                float z=-1.4f+i*.7f, y=(z+1.5f)/3*2.88f;
                Box(root,new(x,y+.45f,z),new(.07f,.90f,.07f),Wood);
            }
            Beam(root,new(x,.986f,-1.4f),new(x,3.674f,1.4f),.055f,PaleWood);
        }
        return root;
    }

    /// <summary>Dedicated production stations with separate workpiece nodes for future action animation.</summary>
    public static Node3D Station(string kind)
    {
        var root=Root("Station_"+kind);
        var clay=new Color("9b6b50");
        if(kind=="carpenter")
        {
            foreach(float x in new[]{-.86f,.86f})
            foreach(float z in new[]{-.37f,.37f})Box(root,new(x,.44f,z),new(.15f,.88f,.15f),DarkWood);
            Box(root,new(0,.85f,0),new(2.2f,.17f,1.1f),PaleWood);
            Box(root,new(0,.23f,0),new(1.8f,.09f,.78f),Wood);
            for(int i=0;i<4;i++)Box(root,new(.14f,.32f+i*.085f,0),new(1.4f,.075f,.4f),PaleWood.Darkened(i%2*.08f));
            var log=Root("Workpiece");root.AddChild(log);
            Cylinder(log,new(-.22f,1.075f,0),.16f,.16f,1.55f,Wood).Rotation=new(0,0,Mathf.Pi/2);
            Cylinder(log,new(.56f,1.075f,0),.153f,.153f,.012f,new Color("c1a270")).Rotation=new(0,0,Mathf.Pi/2);
            var saw=new Node3D{Name="HandSaw",Position=new(.25f,1.2f,.28f)};root.AddChild(saw);
            Box(saw,Vector3.Zero,new(.7f,.15f,.025f),new Color("a4b0a9"));
            Box(saw,new(.41f,.035f,0),new(.19f,.20f,.05f),Wood);
            for(int i=0;i<9;i++)Triangle(saw,new(-.35f+i*.078f,-.072f,0),new(-.315f+i*.078f,-.105f,0),new(-.28f+i*.078f,-.072f,0),Iron);
            Box(root,new(-.94f,.88f,.43f),new(.22f,.25f,.25f),DarkWood);
            Beam(root,new(-.94f,.84f,.45f),new(-.94f,.84f,.72f),.035f,Iron);
            return root;
        }
        if(kind=="kiln")
        {
            Cylinder(root,new(0,.10f,0),.86f,.86f,.2f,Stone,12);
            Cylinder(root,new(0,.78f,0),.73f,.59f,1.38f,clay,12);
            Cylinder(root,new(0,1.52f,0),.60f,.22f,.30f,clay.Lightened(.09f),12);
            Cylinder(root,new(0,1.85f,0),.21f,.19f,.46f,clay,10);
            Cylinder(root,new(0,2.083f,0),.14f,.14f,.015f,DarkWood,10);
            Box(root,new(0,.52f,.674f),new(.51f,.46f,.08f),new Color("342d27"));
            Box(root,new(0,.31f,.74f),new(.60f,.10f,.32f),Stone);
            foreach(float x in new[]{-.29f,.29f})Box(root,new(x,.53f,.71f),new(.10f,.50f,.12f),clay.Lightened(.13f));
            Box(root,new(0,.81f,.65f),new(.65f,.10f,.14f),clay.Lightened(.13f));
            foreach(float y in new[]{.39f,.74f,1.09f})Cylinder(root,new(0,y,0),.739f-y*.1f,.738f-y*.1f,.035f,clay.Darkened(.20f),12);
            return root;
        }
        if(kind=="forge")
        {
            Box(root,new(-.55f,.38f,0),new(1.25f,.76f,1.35f),Stone);
            Box(root,new(-.55f,.79f,0),new(1.19f,.11f,1.26f),new Color("302d29"));
            foreach(float x in new[]{-1.10f,.0f})Box(root,new(x,.92f,0),new(.15f,.30f,1.35f),Stone.Darkened(.13f));
            Box(root,new(-.55f,.98f,-.59f),new(1.25f,.39f,.15f),Stone.Darkened(.13f));
            Box(root,new(-.55f,1.75f,-.43f),new(.58f,1.42f,.43f),Stone);
            Box(root,new(-.55f,2.48f,-.43f),new(.72f,.15f,.56f),Stone.Lightened(.12f));
            for(int i=0;i<8;i++)Ball(root,new(-.85f+i%4*.2f,.89f,-.17f+i/4*.31f),new(.24f,.11f,.23f),new Color(i%3==0?"c36c38":"50413a"),6);
            Cylinder(root,new(.69f,.36f,.05f),.36f,.32f,.72f,Wood,8);
            Box(root,new(.69f,.78f,.05f),new(.50f,.14f,.48f),Iron);
            Box(root,new(.69f,.91f,.05f),new(.29f,.17f,.32f),Iron);
            Box(root,new(.69f,1.03f,.05f),new(.76f,.13f,.32f),Iron.Lightened(.14f));
            Triangle(root,new(.97f,.98f,-.10f),new(1.21f,1.06f,.05f),new(.97f,1.10f,.20f),Iron.Lightened(.08f));
            var hammer=Root("SmithHammer");root.AddChild(hammer);
            Beam(hammer,new(.51f,1.12f,.12f),new(.82f,1.12f,.42f),.035f,Wood);
            Box(hammer,new(.51f,1.14f,.12f),new(.22f,.14f,.13f),Iron);
            return root;
        }
        if(kind=="cooking")
        {
            var fire=Campfire();fire.Scale=Vector3.One*.78f;root.AddChild(fire);
            foreach(float x in new[]{-.67f,.67f})
            {
                Beam(root,new(x,0,-.42f),new(x,1.15f,0),.055f,Iron);
                Beam(root,new(x,0,.42f),new(x,1.15f,0),.055f,Iron);
            }
            Beam(root,new(-.74f,1.15f,0),new(.74f,1.15f,0),.047f,Iron);
            var pot=Root("CookingPot");root.AddChild(pot);
            Cylinder(pot,new(0,.63f,0),.26f,.36f,.37f,Iron,12);
            Cylinder(pot,new(0,.825f,0),.36f,.36f,.05f,Iron.Lightened(.15f),12);
            Cylinder(pot,new(0,.851f,0),.30f,.30f,.012f,new Color("a67e47"),12);
            foreach(float x in new[]{-.35f,.35f})Beam(pot,new(x,.73f,0),new(x*.85f,1.12f,0),.021f,Iron);
            return root;
        }
        if(kind=="oven")
        {
            Box(root,new(0,.30f,0),new(1.75f,.60f,1.6f),Stone);
            Box(root,new(0,.64f,0),new(1.84f,.12f,1.68f),new Color("b49b76"));
            Ball(root,new(0,.84f,-.13f),new(1.65f,1.6f,1.42f),clay,12);
            Box(root,new(0,.88f,.58f),new(.66f,.47f,.10f),new Color("302c27"));
            Box(root,new(0,.67f,.72f),new(.79f,.08f,.35f),new Color("b49b76"));
            foreach(float x in new[]{-.37f,.37f})Box(root,new(x,.90f,.65f),new(.13f,.48f,.18f),clay.Lightened(.18f));
            Box(root,new(0,1.18f,.61f),new(.88f,.13f,.19f),clay.Lightened(.18f));
            Cylinder(root,new(0,1.60f,-.36f),.20f,.18f,.76f,clay,10);
            Cylinder(root,new(0,2.0f,-.36f),.26f,.26f,.10f,clay.Lightened(.13f),10);
            return root;
        }
        root.Free();
        return Workbench();
    }

    public static Node3D Decoration(string kind)
    {
        var root=Root("Decoration_"+kind);
        if(kind=="table")
        {
            foreach(float x in new[]{-.64f,.64f})foreach(float z in new[]{-.32f,.32f})Box(root,new(x,.39f,z),new(.11f,.78f,.11f),Wood);
            Box(root,new(0,.64f,0),new(1.4f,.13f,.73f),DarkWood);
            for(int i=0;i<4;i++)Box(root,new(0,.80f,-.3375f+i*.225f),new(1.6f,.1f,.218f),PaleWood.Darkened(i%2*.045f));
            return root;
        }
        if(kind=="chair")
        {
            foreach(float x in new[]{-.21f,.21f})foreach(float z in new[]{-.21f,.21f})Box(root,new(x,.235f,z),new(.07f,.47f,.07f),Wood);
            Box(root,new(0,.47f,0),new(.55f,.09f,.54f),PaleWood);
            foreach(float x in new[]{-.21f,.21f})Box(root,new(x,.75f,-.23f),new(.07f,.69f,.08f),DarkWood);
            foreach(float y in new[]{.74f,.96f})Box(root,new(0,y,-.23f),new(.51f,.14f,.065f),PaleWood);
            Box(root,new(0,.53f,.01f),new(.43f,.045f,.4f),new Color("6e7c59"));
            return root;
        }
        if(kind=="planter")
        {
            Cylinder(root,new(0,.21f,0),.23f,.34f,.42f,new Color("9c674c"),10);
            Cylinder(root,new(0,.42f,0),.37f,.37f,.09f,new Color("ba8b65"),10);
            Cylinder(root,new(0,.469f,0),.30f,.30f,.014f,new Color("49392c"),10);
            for(int i=0;i<7;i++)
            {
                float a=i*2.39996f;
                var tip=new Vector3(Mathf.Cos(a)*.24f,.73f+i%3*.12f,Mathf.Sin(a)*.24f);
                Beam(root,new(0,.46f,0),tip,.016f,new Color("526b3b"),5);
                var leaf=Ball(root,tip,new(.33f,.1f,.18f),new Color(i%2==0?"82904f":"627d43"),6);leaf.Rotation=new(0,a,.35f);
                if(i%2==0)Ball(root,tip+Vector3.Up*.065f,new(.11f,.10f,.11f),new Color("be9870"),6);
            }
            return root;
        }
        if(kind=="painting")
        {
            // An easel keeps paintings placeable in the garden as well as inside a home.
            foreach(float x in new[]{-.35f,.35f})Beam(root,new(x,0,.24f),new(x*.63f,1.50f,-.02f),.04f,Wood);
            Beam(root,new(0,0,-.36f),new(0,1.45f,0),.035f,Wood);
            Box(root,new(0,.70f,.09f),new(.91f,.065f,.20f),PaleWood);
            Box(root,new(0,1.10f,.02f),new(.84f,.72f,.055f),new Color("957545"));
            Box(root,new(0,1.10f,.054f),new(.72f,.60f,.025f),new Color("aeb6a0"));
            Quad(root,new(-.35f,.82f,.07f),new(.35f,.82f,.07f),new(.35f,1.02f,.07f),new(-.35f,1.12f,.07f),new Color("5c7666"));
            Triangle(root,new(-.35f,.82f,.075f),new(.24f,.82f,.075f),new(-.14f,1.19f,.075f),new Color("72836e"));
            Ball(root,new(.22f,1.28f,.081f),new(.12f,.12f,.014f),new Color("dcc698"),12);
            return root;
        }
        if(kind=="lantern")
        {
            Box(root,new(0,.04f,0),new(.32f,.08f,.32f),Iron);
            Box(root,new(0,.24f,0),new(.22f,.31f,.22f),new Color("e1bd78"));
            foreach(float x in new[]{-.135f,.135f})foreach(float z in new[]{-.135f,.135f})Box(root,new(x,.24f,z),new(.025f,.36f,.025f),Iron);
            Cylinder(root,new(0,.445f,0),.245f,.065f,.14f,Iron,4).Rotation=new(0,Mathf.Pi/4,0);
            Beam(root,new(-.075f,.48f,0),new(-.075f,.61f,0),.015f,Iron);
            Beam(root,new(.075f,.48f,0),new(.075f,.61f,0),.015f,Iron);
            Beam(root,new(-.075f,.61f,0),new(.075f,.61f,0),.015f,Iron);
            root.AddChild(new OmniLight3D{Name="LanternGlow",Position=new(0,.28f,0),LightColor=new("ffd099"),LightEnergy=.45f,OmniRange=3.5f,ShadowEnabled=false});
            return root;
        }
        if(kind=="rug")
        {
            Box(root,new(0,.015f,0),new(1.8f,.03f,1.3f),new Color("765a45"));
            Box(root,new(0,.032f,0),new(1.6f,.008f,1.1f),new Color("a9976e"));
            Box(root,new(0,.038f,0),new(1.42f,.008f,.92f),new Color("637462"));
            for(int i=0;i<3;i++)
            {
                var diamond=Box(root,new(-.44f+i*.44f,.045f,0),new(.23f,.006f,.23f),new Color("c1ad7e"));diamond.Rotation=new(0,Mathf.Pi/4,0);
            }
            for(int i=0;i<17;i++)foreach(float z in new[]{-.68f,.68f})Box(root,new(-.8f+i*.1f,.013f,z),new(.025f,.016f,.10f),new Color("c1ad7e"));
            return root;
        }
        Box(root,new(0,.24f,0),new(.48f,.48f,.48f),PaleWood);
        return root;
    }

    public static Node3D Crop(bool mature)
    {
        var root = Root(mature ? "RipeCrop" : "YoungCrop");
        Cylinder(root, new(0, .015f, 0), .38f, .36f, .04f, new("66503c"), 9);
        for (int i = 0; i < 5; i++)
        {
            float a = i * 2.39996f;
            var point = new Vector3(Mathf.Cos(a) * .17f, 0, Mathf.Sin(a) * .17f);
            float h = mature ? .70f + i % 2 * .14f : .25f + i % 2 * .12f;
            Beam(root, point, point + new Vector3(.035f * Mathf.Sin(a), h, 0), .014f, mature ? new("bba552") : new("73913f"), 5);
            for (int j = 0; j < 2; j++)
            {
                var leaf = Ball(root, point + new Vector3((j == 0 ? -.10f : .1f), h * .42f, 0), new(.28f, .055f, .10f), new("678742"), 5);
                leaf.Rotation = new(0, 0, j == 0 ? -.55f : .55f);
            }
            if (mature) Ball(root, point + new Vector3(.03f * Mathf.Sin(a), h - .02f, 0), new(.085f, .30f, .085f), new("d1b65c"), 6);
        }
        return root;
    }

    public static Node3D Well()
    {
        var root = Root("Well");
        for (int row = 0; row < 3; row++)
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.Tau / 12 + row % 2 * .26f;
            var brick = Box(root, new(Mathf.Cos(a) * .62f, .14f + row * .25f, Mathf.Sin(a) * .62f), new(.34f, .235f, .23f), Stone.Lightened((i % 3) * .035f));
            brick.Rotation = new(0, -a + Mathf.Pi / 2, 0);
        }
        Cylinder(root, new(0, .14f, 0), .48f, .48f, .04f, new("344949"));
        foreach (float x in new[] { -.84f, .84f }) Box(root, new(x, 1.22f, 0), new(.18f, 2.44f, .18f), Wood);
        Beam(root, new(-.94f, 1.67f, 0), new(.94f, 1.67f, 0), .09f, PaleWood);
        Beam(root, new(0, .46f, 0), new(0, 1.68f, 0), .018f, new("c4ad74"), 5);
        Quad(root, new(-1.1f, 2.30f, -.86f), new(-1.1f, 2.30f, .86f), new(0, 2.83f, .86f), new(0, 2.83f, -.86f), new("755544"));
        Quad(root, new(0, 2.83f, -.86f), new(0, 2.83f, .86f), new(1.1f, 2.30f, .86f), new(1.1f, 2.30f, -.86f), new("69513f"));
        Cylinder(root, new(.98f, .22f, .51f), .19f, .23f, .41f, Wood, 9);
        return root;
    }

    public static Node3D Cart()
    {
        var root = Root("Cart");
        Box(root, new(0, .58f, 0), new(1.1f, .13f, 1.55f), PaleWood);
        foreach (float x in new[] { -.56f, .56f })
        {
            Box(root, new(x, .89f, 0), new(.10f, .52f, 1.6f), Wood);
            var wheel = Cylinder(root, new(x * 1.18f, .43f, .15f), .43f, .43f, .10f, DarkWood, 12);
            wheel.Rotation = new(0, 0, Mathf.Pi / 2);
            var hub = Cylinder(root, new(x * 1.29f, .43f, .15f), .10f, .10f, .13f, PaleWood); hub.Rotation = new(0, 0, Mathf.Pi / 2);
            Beam(root, new(x * .75f, .56f, .67f), new(x * .75f, .7f, 2.1f), .055f, PaleWood);
        }
        Box(root, new(0, .89f, -.75f), new(1.12f, .52f, .10f), Wood);
        return root;
    }

    public static Node3D Shrine()
    {
        var root = Root("Shrine");
        Cylinder(root, new(0, .11f, 0), 1.25f, 1.1f, .22f, Stone, 8);
        Cylinder(root, new(0, .31f, 0), .82f, .76f, .20f, Stone.Lightened(.1f), 8);
        var monolith = Cylinder(root, new(0, 1.15f, 0), .43f, .20f, 1.6f, new("7c897e"), 5);
        monolith.Rotation = new(0, 0, -.09f);
        Box(root, new(0, 1.29f, .31f), new(.055f, .56f, .035f), new("bdc69a"));
        Box(root, new(0, 1.40f, .31f), new(.29f, .055f, .035f), new("bdc69a"));
        return root;
    }
}
