using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.Core;

namespace Thornwake.World;

/// <summary>Disposable presentation of the persistent world. Nothing here owns save data.</summary>
public partial class Valley : Node3D
{
    public readonly Dictionary<string, Node3D> Resources = new();
    public readonly Dictionary<string, ResourceSpawn> Spawns = new();
    public readonly List<(Vector3 Position, float Radius)> Obstacles = new();
    public readonly List<Node3D> Villagers = new();
    public readonly Dictionary<string, Node3D> Planted = new();
    public readonly Dictionary<string,Node3D> StructureNodes=new();
    private ContentCatalog _catalog=null!;
    public readonly Dictionary<string,Node3D> VillageStationNodes=new();
    public Node3D? StationNode(string id)=>VillageStationNodes.GetValueOrDefault(id)??StructureNodes.GetValueOrDefault(id);
    public Node3D Buildings = new();
    public Node3D Enemy = null!;
    public Node3D Cart = null!;
    public DirectionalLight3D Sun = null!;
    public WorldEnvironment Atmosphere = null!;
    private Node3D _terrain = null!;
    private WorldState _state = null!;
    private double _lastCropRefresh;
    private bool _decorated;

    private readonly Dictionary<string,Node3D> _stumps=new();
    public Vector3 EnemyHome => At(-29, -23);
    public Vector3 At(float x, float z) => new(x, Height(x, z), z);

    public float Height(float x, float z)
    {
        return Construction.Ground(_state,x,z);
    }
    public float WalkHeight(float x,float z)=>Construction.WalkHeight(_state,x,z);
    public float WalkHeight(float x,float z,float currentY)=>Construction.WalkHeight(_state,x,z,currentY);

    public void Generate(WorldState state,ContentCatalog catalog)
    {
        _state = state;_catalog=catalog;
        Atmosphere = new WorldEnvironment { Environment = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color("a8bab8"),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color("9cabae"), AmbientLightEnergy = .35f,
            TonemapMode = Godot.Environment.ToneMapper.Linear,
            FogEnabled = true, FogLightColor = new Color("a1b4b1"), FogDensity = .0045f,
        }};
        AddChild(Atmosphere);
        Sun = new DirectionalLight3D { RotationDegrees = new Vector3(-38,-30,0), LightColor = new Color("ffe8c5"), LightEnergy = .85f, ShadowEnabled = true, DirectionalShadowMaxDistance = 95 };
        AddChild(Sun);
        RebuildTerrain();
        // Roads and settlement lots are reserved before resources are generated.
        for (int i=-55;i<55;i+=2)
        {
            Patch(i,0,2.1f,3.2f,new Color("827b60"));
            Patch(0,i,2.7f,2.1f,new Color("827b60"));
        }
        var water = new MeshInstance3D { Mesh = new PlaneMesh { Size = new Vector2(8,120) }, Position = new Vector3(27,-.10f,0), MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(.24f,.43f,.45f,.85f), Roughness = .2f, Metallic = .35f, Transparency = BaseMaterial3D.TransparencyEnum.Alpha } };
        AddChild(water);
        for (int i=0;i<17;i++) Visuals.Box(this,new Vector3(23+i*.5f,.28f,0),new Vector3(.46f,.2f,3.6f),new Color("70604c"));
        for (int s=-1;s<=1;s+=2) {
            Visuals.Box(this,new Vector3(27,1,s*1.6f),new Vector3(9,.15f,.15f),new Color("4c4439"));
            foreach(float x in new[]{23f,25f,27f,29f,31f}) Visuals.Box(this,new Vector3(x,.6f,s*1.6f),new Vector3(.17f,1.3f,.17f),new Color("51473b"));
        }
        AddHouse(-9,-8,6,5,false); AddHouse(9,-9,6,6,true); AddHouse(-11,9,5,5,false); AddHouse(10,10,5,5,false);
        Place(Visuals.Well(),-3,0); var carpenter=Visuals.Station("carpenter");Place(carpenter,6,1);VillageStationNodes["village:carpenter"]=carpenter; Place(Visuals.Campfire(),-4,6);
        foreach(var site in Workstations.Village.Where(s=>s.Kind!="carpenter")){var station=Visuals.Station(site.Kind);Place(station,site.X,site.Z);VillageStationNodes[site.Id]=station;Obstacles.Add((station.Position,1.1f));}
        Obstacles.Add((At(6,1),1.1f));
        for(int i=0;i<6;i++) { var f=Visuals.Fence(); Place(f,16+i*3,15); }
        for(int x=16;x<23;x+=2) for(int z=7;z<13;z+=2) { Patch(x,z,1.7f,1.7f,new Color("514630")); Place(Visuals.Crop(true),x,z); }
        // Far hills frame the playable space without creating inaccessible foreground cliffs.
        var rng = new Random(state.Seed);
        for(int i=0;i<32;i++)
        {
            float a=i*Mathf.Tau/32; var hill=new MeshInstance3D { Mesh = new SphereMesh { Radius=12,Height=24,RadialSegments=12,Rings=6 }, Position = new Vector3(Mathf.Cos(a)*85,-7,Mathf.Sin(a)*85),Scale = new Vector3(1.3f,.7f+(float)rng.NextDouble(),1.3f), MaterialOverride=Visuals.Material(new Color("536856")) }; AddChild(hill);
        }
        foreach(var r in WorldGenerator.Generate(state))
        {
            Spawns[r.Id]=r;
            if(state.RemovedResources.Contains(r.Id)) {if(r.Kind=="tree"&&!state.RemovedStumps.Contains(r.Id)){var stump=WildStump(r);Place(stump,r.X,r.Z);_stumps[r.Id]=stump;}continue;}
            Node3D n=r.Kind=="tree" ? WildTree(r) : r.Kind=="stone" ? Visuals.Rock(r.Scale) : BerryBush(r.Scale);
            Place(n,r.X,r.Z); Resources[r.Id]=n;
        }
        RefreshVegetation();
        AddChild(Buildings);
        foreach(var s in state.Structures) AddStructure(s);
        foreach(var c in state.Crops) AddCrop(c);
        foreach(var p in new[]{new Vector2(-2,-5),new Vector2(5,3),new Vector2(-6,4),new Vector2(4,-4)}) { var n=Visuals.Person(new Color(.40f+(float)rng.NextDouble()*.15f,.37f,.25f)); Place(n,p.X,p.Y); Villagers.Add(n); }
        Enemy=Visuals.Person(new Color("673e35"),true); Place(Enemy,-29,-23); Enemy.Visible=!state.EnemyDefeated;
        Place(Visuals.Campfire(),-31,-26);
        for(int i=0;i<5;i++) Place(Visuals.Rock(.7f),-34+i*2,-28);
        Cart=new Node3D(); AddChild(Cart);
        Visuals.Box(Cart,new Vector3(0,.65f,0),new Vector3(2,.3f,3),new Color("635443"));
        for(int s=-1;s<=1;s+=2) {Visuals.Box(Cart,new Vector3(s*.95f,1,0),new Vector3(.13f,.7f,3),new Color("766249"));for(int z=-1;z<=1;z+=2) { var wheel=new MeshInstance3D { Mesh=new CylinderMesh { TopRadius=.48f,BottomRadius=.48f,Height=.18f,RadialSegments=12 }, Position=new Vector3(s*1.15f,.5f,z),RotationDegrees=new Vector3(0,0,90),MaterialOverride=Visuals.Material(new Color("403b33")) }; Cart.AddChild(wheel); }}
        for(int i=0;i<3;i++) Visuals.Box(Cart,new Vector3(0,1,i*.7f-.7f),new Vector3(1.4f,.55f,.6f),new Color("958468"));
        RefreshGroundCover();
        _decorated=true;
    }

    public void RefreshGroundCover(){AddGrass();foreach(var stump in _stumps.Values)stump.Visible=!_state.Structures.Any(s=>Construction.IsFloor(s)&&s.Level==0&&Construction.OnFloor(s,stump.Position.X,stump.Position.Z));}
    public void AddStump(ResourceSpawn spawn){if(_state.RemovedStumps.Contains(spawn.Id)||_stumps.ContainsKey(spawn.Id))return;var stump=WildStump(spawn);Place(stump,spawn.X,spawn.Z);_stumps[spawn.Id]=stump;}

    private Node3D BerryBush(float scale,bool fruit=true)
    {
        var n = new Node3D();
        for(int i=0;i<3;i++) { var m=new MeshInstance3D { Mesh=new SphereMesh { Radius=.5f,Height=.9f,RadialSegments=7,Rings=4 },Position=new Vector3((i-1)*.3f,.55f,0),MaterialOverride=Visuals.Material(new Color("526943")) };n.AddChild(m); }
        if(fruit)for(int i=0;i<8;i++) {float a=i*2.4f;var b=new MeshInstance3D { Mesh=new SphereMesh {Radius=.085f,Height=.17f,RadialSegments=5,Rings=3}, Position=new Vector3(Mathf.Cos(a)*.55f,.5f+i*.035f,Mathf.Sin(a)*.35f),MaterialOverride=Visuals.Material(new Color("a45f56"))}; n.AddChild(b);}
        n.Scale=Vector3.One*scale; return n;
    }

    public void Place(Node3D node,float x,float z) { AddChild(node); node.Position=At(x,z); }
    private void AddHouse(float x,float z,float width,float depth,bool workshop) { var h=Visuals.House(width,depth,workshop);Place(h,x,z); Obstacles.Add((h.Position,Mathf.Max(width,depth)*.53f)); }

    private void Patch(float x,float z,float width,float depth,Color color)
    {
        var st=new SurfaceTool(); st.Begin(Mesh.PrimitiveType.Triangles);
        Vector3[] p={At(x-width/2,z-depth/2),At(x+width/2,z-depth/2),At(x-width/2,z+depth/2),At(x+width/2,z+depth/2)};
        foreach(int i in new[]{0,1,2,1,3,2}) st.AddVertex(p[i]+Vector3.Up*.035f);
        st.GenerateNormals(); AddChild(new MeshInstance3D { Mesh=st.Commit(),MaterialOverride=new StandardMaterial3D{AlbedoColor=color,Roughness=1,CullMode=BaseMaterial3D.CullModeEnum.Disabled} });
    }

    public Node3D StructureVisual(string kind,bool open=false,BuildSelection? selection=null,float gableBottom=0) { var visual = selection!=null&&(selection.DefinitionId=="roof"||kind=="roof_module") ? Visuals.BuildVariant(_catalog,selection,gableBottom:gableBottom) : kind switch { "campfire"=>Visuals.Campfire(),"workbench"=>Visuals.Workbench(),"fence"=>Visuals.Fence(),"wall"=>Visuals.WallPanel(),"wall_diagonal"=>Visuals.DiagonalWall(),"foundation_triangle" or "upper_floor_triangle"=>Visuals.TriangleFloor(),"roof_triangle"=>Visuals.TriangleRoof(),"door"=>Visuals.WallPanel(true,open),"roof" or "roof_flat" or "roof_slope" or "roof_corner"=>Visuals.RoofTile(kind,true,true,true,true),"stairs"=>Visuals.Stairs(),"upper_floor"=>Visuals.UpperFloor(),"carpenter" or "kiln" or "forge" or "cooking" or "oven"=>Visuals.Station(kind),"chair" or "table" or "planter" or "painting" or "lantern" or "rug"=>Visuals.Decoration(kind),_=>Visuals.Foundation() };
        return selection==null||selection.DefinitionId=="roof"||kind=="roof_module"?visual:Visuals.FinishLegacyBuilding(_catalog,selection,visual,open);
    }
    public Node3D StructureVisual(StructureState piece)=>StructureVisual(piece.Kind,piece.DoorOpen,piece.Selection,FillBottom(piece));
    private float FillBottom(StructureState piece)=>piece.Selection!=null&&RoofGeometry.IsFill(BuildingVariants.Variant(_catalog,piece.Selection).Geometry)?RoofGeometry.GableBottom(_state,piece):0;
    public float RoofHeight(StructureState roof,float x,float z)
    {
        var local=Construction.Local(roof,x,z);
        if(roof.Selection!=null&&RoofGeometry.IsRoofModule(roof))
        {
            var variant=BuildingVariants.Variant(_catalog,roof.Selection);
            return Construction.Base(_state,roof)+RoofGeometry.SurfaceHeight(variant.Geometry,roof.Selection.Slope,local.X,local.Z);
        }
        float rise=roof.Kind=="roof_slope"?(local.Z+1.5f)*.36f:roof.Kind=="roof_corner"?Mathf.Min(local.X+1.5f,local.Z+1.5f)*.36f:0;
        return Construction.Base(_state,roof)+.17f+rise;
    }
    public void AddStructure(StructureState s)
    {
        var polygon=Construction.Polygon(s);
        float RoofSurface(StructureState roof,(float X,float Z) point)
        {
            return RoofHeight(roof,point.X,point.Z);
        }
        bool Edge(int index)
        {
            var a=polygon[index];var b=polygon[(index+1)%polygon.Length];
            foreach(var other in _state.Structures.Where(o=>o.Id!=s.Id&&Construction.IsRoof(o)&&o.Level==s.Level&&!(o.Selection!=null&&RoofGeometry.IsTrim(_catalog,o.Selection))))
            {var points=Construction.Polygon(other);for(int i=0;i<points.Length;i++){var c=points[i];var d=points[(i+1)%points.Length];var middle=((a.X+b.X)/2,(a.Z+b.Z)/2);var otherMiddle=((c.X+d.X)/2,(c.Z+d.Z)/2);if(Construction.Distance(a.X,a.Z,d.X,d.Z)<.05f&&Construction.Distance(b.X,b.Z,c.X,c.Z)<.05f&&Mathf.Abs(RoofSurface(s,a)-RoofSurface(other,d))<.02f&&Mathf.Abs(RoofSurface(s,b)-RoofSurface(other,c))<.02f&&Mathf.Abs(RoofSurface(s,middle)-RoofSurface(other,otherMiddle))<.02f)return false;}}
            return true;
        }
        var n=s.Selection!=null&&(s.Selection.DefinitionId=="roof"||s.Kind=="roof_module")?Visuals.BuildVariant(_catalog,s.Selection,Edge(0),Edge(1),Edge(2),Edge(3),FillBottom(s)):s.Kind=="roof_triangle"?Visuals.TriangleRoof(Edge(0),Edge(1),Edge(2)):Construction.IsRoof(s)?Visuals.RoofTile(s.Kind,Edge(0),Edge(1),Edge(2),Edge(3)):StructureVisual(s.Kind,s.DoorOpen,s.Selection);Buildings.AddChild(n);n.Position=new Vector3(s.X,Construction.Base(_state,s),s.Z);n.Rotation=new Vector3(0,s.Rotation,0);n.Name=s.Id;StructureNodes[s.Id]=n;
        if(s.Kind is "foundation" or "foundation_triangle")
        {
            float low=polygon.Min(p=>Height(p.X,p.Z));
            float depth=Mathf.Max(0,n.Position.Y-low);if(depth>.02f){if(Construction.IsTriangle(s))n.AddChild(Visuals.TrianglePrism(-depth,0,new Color("73766b")));else Visuals.Box(n,new Vector3(0,-depth/2,0),new Vector3(2.98f,depth,2.98f),new Color("73766b"));}
        }
    }
    public void RebuildStructures(){foreach(var n in StructureNodes.Values){Buildings.RemoveChild(n);n.QueueFree();}StructureNodes.Clear();foreach(var s in _state.Structures)AddStructure(s);RefreshGroundCover();}
    public void UpdateShelterVisibility(Vector3 player)
    {
        foreach(var s in _state.Structures)if(StructureNodes.TryGetValue(s.Id,out var n))n.Visible=!(Construction.Distance(player.X,player.Z,s.X,s.Z)<4.3f&&Construction.Base(_state,s)>player.Y+1.5f&&(Construction.IsRoof(s)||s.Level>0));
        foreach(var s in _state.Structures)if(s.RoofConnectionId!=""&&s.Selection!=null&&RoofGeometry.IsTrim(_catalog,s.Selection)&&StructureNodes.TryGetValue(s.Id,out var trim)&&StructureNodes.TryGetValue(s.RoofConnectionId,out var parent))trim.Visible=parent.Visible;
    }
    public void AddCrop(CropState c) { bool mature=_state.Time-c.PlantedAt>=_state.Settings.CropGrowthMinutes*60;var n=BerryBush(mature?.8f:.35f,mature); Place(n,c.X,c.Z); Planted[c.Id]=n; }
    public void RefreshCrops() { foreach(var n in Planted.Values) {RemoveChild(n);n.QueueFree();} Planted.Clear();foreach(var c in _state.Crops) AddCrop(c); }
    public bool CanWalk(Vector3 point)
    {
        if(Mathf.Abs(point.X)>54||Mathf.Abs(point.Z)>54) return false;
        if(point.X>23&&point.X<31&&Mathf.Abs(point.Z)>1.3f) return false;
        foreach(var o in Obstacles) if(new Vector2(point.X-o.Position.X,point.Z-o.Position.Z).Length()<o.Radius+.28f) return false;
        foreach(var s in _state.Structures)if(Construction.SolidAt(_state,s,_catalog.Buildings[s.Kind],point.X,point.Z,point.Y,catalog:_catalog))return false;
        foreach(var pair in Resources) if(Spawns[pair.Key].Kind=="tree"&&new Vector2(point.X-pair.Value.Position.X,point.Z-pair.Value.Position.Z).Length()<TreeCollisionRadius(pair.Key)) return false;
        return true;
    }
    public bool CanPlace(Vector3 p,float radius=1.8f)
    {
        if(Mathf.Abs(p.X)>50||Mathf.Abs(p.Z)>50 ||Mathf.Abs(p.X-27)<6||Mathf.Abs(p.X)<2||Mathf.Abs(p.Z)<2) return false;
        foreach(var o in Obstacles) if(new Vector2(p.X-o.Position.X,p.Z-o.Position.Z).Length()<o.Radius+radius) return false;
        foreach(var s in _state.Structures)if(Construction.Occupies(s,_catalog.Buildings[s.Kind],p.X,p.Z,radius))return false;
        foreach(var r in Resources.Values) if(new Vector2(p.X-r.Position.X,p.Z-r.Position.Z).Length()<radius+1) return false;
        return true;
    }
    public void Animate(double time,double delta)
    {
        AnimateVegetation();
        for(int i=0;i<Villagers.Count;i++)
        {
            var n=Villagers[i];float phase=(float)time*.14f+i*Mathf.Tau/4;Vector3 target=At(Mathf.Sin(phase)*3,Mathf.Cos(phase)*4-1); Vector3 d=target-n.Position; n.Position=target;if(d.LengthSquared()>.00001) n.Rotation=new Vector3(0,Mathf.Atan2(d.X,d.Z),0); AnimatePerson(n,(float)time*4,true);
        }
        float cx=(float)(time%180)/180*96-48; Cart.Position=At(cx,-.2f);if(cx>23&&cx<31)Cart.Position=new Vector3(cx,.4f,-.2f);
        if(time-_lastCropRefresh>5) {_lastCropRefresh=time;RefreshCrops();}
        float hour=(float)(_state.CalendarMinutes/60%24);
        float daylight=Mathf.Clamp(Mathf.Sin((hour-6)/12*Mathf.Pi),0,1);
        Sun.LightEnergy=.10f+daylight*.75f;
        Sun.LightColor=new Color("acbbdb").Lerp(new Color("ffe8c5"),daylight);
        Atmosphere.Environment.AmbientLightEnergy=.19f+daylight*.16f;
    }
    public static void AnimatePerson(Node3D n,float phase,bool walking)
    {
        float swing=walking?Mathf.Sin(phase)*.5f:0;
        var l=n.GetNodeOrNull<Node3D>("LeftLeg");var r=n.GetNodeOrNull<Node3D>("RightLeg");if(l!=null)l.Rotation=new Vector3(swing,0,0);if(r!=null)r.Rotation=new Vector3(-swing,0,0);
    }
}


