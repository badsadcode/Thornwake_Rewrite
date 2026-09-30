using Godot;

namespace Thornwake.World;

public static partial class Visuals
{
    public static Node3D CraftingTool(string action)
    {
        var root=new Node3D{Name="CraftTool",Position=new(0,-.51f,0)};
        // Grip uses the prototype hand socket. Authored rigs can replace this visual adapter later.
        if(action is "smith" or "join")
        {
            Beam(root,new(0,0,-.07f),new(0,0,.27f),.028f,Wood);
            Box(root,new(0,0,.27f),new(.24f,.12f,.13f),action=="smith"?Iron:PaleWood);
        }
        else if(action is "stir" or "fire" or "bake")
        {
            float length=action=="bake"?.8f:.6f;
            Beam(root,new(0,0,-.1f),new(0,0,length),.022f,Wood);
            if(action=="bake")Box(root,new(0,0,length),new(.29f,.035f,.30f),PaleWood);
            else if(action=="stir")Ball(root,new(0,0,length),new(.12f,.045f,.17f),Wood);
            else Box(root,new(0,-.03f,length),new(.045f,.09f,.04f),Iron);
        }
        // Saw is animated at the bench; small handwork and joinery use no manufactured inventory item.
        return root;
    }
    public static Node3D CraftingWorkpiece(string action,bool bareFire=false)
    {
        var root=new Node3D{Name="CraftWork"};var moving=new Node3D{Name="Moving"};root.AddChild(moving);
        if(action=="saw")
        {
            var saw=new Node3D{Position=new(.1f,1.19f,0)};moving.AddChild(saw);
            Box(saw,Vector3.Zero,new(.65f,.13f,.025f),new Color("b7c6c2"));
            Box(saw,new(.39f,.025f,0),new(.18f,.19f,.055f),Wood);
            for(int i=0;i<8;i++)Triangle(saw,new(-.32f+i*.08f,-.06f,0),new(-.28f+i*.08f,-.095f,0),new(-.24f+i*.08f,-.06f,0),Iron);
            for(int i=0;i<6;i++)Box(root,new(.06f+i*.035f,.95f,.2f+i%2*.035f),new(.025f,.015f,.04f),PaleWood);
        }
        else if(action=="smith")
        {
            var hot=new StandardMaterial3D{AlbedoColor=new Color("e99a55"),EmissionEnabled=true,Emission=new Color("c74b24"),EmissionEnergyMultiplier=.7f};
            var billet=Box(moving,new(.68f,1.13f,.05f),new(.32f,.075f,.10f),Iron);billet.MaterialOverride=hot;
        }
        else if(action=="join")
        {
            Box(moving,new(.05f,1.28f,.27f),new(.62f,.05f,.16f),PaleWood);
            foreach(float x in new[]{-.2f,.3f})Box(moving,new(x,1.31f,.27f),new(.025f,.018f,.025f),Iron);
        }
        else if(action=="stir")
        {
            if(bareFire){Cylinder(root,new(0,.60f,0),.22f,.31f,.30f,Iron,12);Cylinder(root,new(0,.76f,0),.27f,.27f,.015f,new Color("a67e47"),12);foreach(float x in new[]{-.34f,.34f})Beam(root,new(x,.17f,-.28f),new(x,.68f,.28f),.03f,Iron);}
            for(int i=0;i<5;i++){float a=i*1.26f;Ball(moving,new(Mathf.Sin(a)*.19f,bareFire?.775f:.865f,Mathf.Cos(a)*.19f),new(.06f,.018f,.06f),new Color("bda56b"),6);}
        }
        else if(action=="bake")Ball(moving,new(0,.75f,.72f),new(.42f,.14f,.28f),new Color("d5b579"));
        else if(action=="fire")Box(moving,new(0,.40f,.72f),new(.26f,.13f,.17f),new Color("c58456"));
        return root;
    }
}
