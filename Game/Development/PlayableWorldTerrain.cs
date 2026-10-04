using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Thornwake.Core;
using Thornwake.World;
using NVector=System.Numerics.Vector3;

namespace Thornwake.Development;

/// <summary>Disposable Godot adapter. Workers own only Core data; engine resources are committed here.</summary>
public partial class PlayableWorldTerrain : Node3D
{
    private readonly Dictionary<WorldRegion,Node3D> _tiles=new();
    private readonly Dictionary<WorldRegion,bool> _detail=new();
    private readonly HashSet<WorldRegion> _failed=new();
    private Task<(PlayableTerrainPatch Patch,double Time)>? _job;
    private CancellationTokenSource? _cancel;
    private WorldRegion _jobTile;
    private long _revision,_jobRevision;
    private MacroWorldGenerator? _world;
    private WorldPoint _origin;
    private ShaderMaterial _terrain=null!,_water=null!;
    private readonly List<(Mesh Mesh,Transform3D Transform)> _treeParts=new();
    private Mesh _grass=null!;
    public int Resident=>_tiles.Count;
    public int DetailedCount=>_detail.Values.Count(d=>d);
    public int Pending=>_job==null?0:1;
    public int Queued {get;private set;}
    public int Unloaded {get;private set;}
    public int Discarded {get;private set;}
    public int Builds {get;private set;}
    public long Triangles=>_detail.Values.Sum(d=>d?2048L:128L);
    public double WorkerMs {get;private set;}
    public double ColliderMs {get;private set;}
    public double CommitMs {get;private set;}
    public double MaxCommitMs {get;private set;}
    public string Failure {get;private set;}="";
    public bool IsDetailed(WorldRegion tile)=>_detail.GetValueOrDefault(tile);
    public override void _Ready()
    {
        _terrain=new ShaderMaterial{Shader=GD.Load<Shader>("res://Game/Development/PlayableTerrain.gdshader")};
        _water=new ShaderMaterial{Shader=GD.Load<Shader>("res://Game/Development/PlayableWater.gdshader")};
        // Imported geometry/materials reused, batched by mesh. No harvestable/gameplay trees are spawned by this lab.
        var prototype=WoodlandAssets.Tree("Pine_A");
        foreach(var source in WoodlandAssets.Meshes(prototype.Levels[2]))
        {
            var mesh=(Mesh)source.Mesh.Duplicate();
            for(int s=0;s<mesh.GetSurfaceCount();s++)mesh.SurfaceSetMaterial(s,source.GetActiveMaterial(s));
            Transform3D transform=source.Transform;Node? parent=source.GetParent();
            while(parent is Node3D p && p!=prototype){transform=p.Transform*transform;parent=p.GetParent();}
            _treeParts.Add((mesh,transform));
        }
        prototype.Free();
        _grass=new QuadMesh{Size=new Vector2(.4f,.55f),Material=new StandardMaterial3D{AlbedoColor=new Color("648044"),CullMode=BaseMaterial3D.CullModeEnum.Disabled,Roughness=1}};
    }
    public void Configure(MacroWorldGenerator world)
    {
        _revision++;_cancel?.Cancel();_world=world;Failure="";_failed.Clear();
        foreach(var tile in _tiles.Values){RemoveChild(tile);tile.QueueFree();} _tiles.Clear();_anchors.Clear();_detail.Clear();
    }
    private readonly Dictionary<WorldRegion,WorldPoint> _anchors=new();
    public void Rebase(WorldPoint origin)
    {
        _origin=origin;
        foreach(var (tile,node) in _tiles.ToArray())
        {
            var anchor=_anchors[tile];
            if(Math.Max(Math.Abs(anchor.X-origin.X),Math.Max(Math.Abs(anchor.Y-origin.Y),Math.Abs(anchor.Z-origin.Z)))>7000)
            {RemoveChild(node);node.QueueFree();_tiles.Remove(tile);_anchors.Remove(tile);_detail.Remove(tile);Unloaded++;}
            else node.Position=ToGodot(anchor.RelativeTo(origin));
        }
    }
    public bool Has(WorldRegion tile)=>_tiles.ContainsKey(tile);
    public bool CanTraverse(WorldPoint from,WorldPoint to)=>PlayableWorldPolicy.CanTraverse(from,to,t=>_detail.GetValueOrDefault(t));
    public void ShowRings(bool show)=>_terrain.SetShaderParameter("show_rings",show);
    public void Overlay(bool boundaries,int mode,bool waterStatus)
    { _terrain.SetShaderParameter("show_grid",boundaries);_terrain.SetShaderParameter("view_mode",mode);_water.SetShaderParameter("show_status",waterStatus); }
    public void Stream(WorldPoint observer,WorldPoint origin)
    {
        if(_world==null)return;_origin=origin;
        var center=PlayableWorldPolicy.TileAt(observer);
        var plan=RegionStreaming.Plan(_tiles.Keys.ToArray(),new[]{center},PlayableWorldPolicy.Radius,PlayableWorldPolicy.RetainRadius,256,
            t=>Math.Abs(t.X*256d)+256<WorldRegion.MaximumIndex*16d && Math.Abs(t.Z*256d)+256<WorldRegion.MaximumIndex*16d);
        foreach(var tile in plan.Unload)
        {var node=_tiles[tile];RemoveChild(node);node.QueueFree();_tiles.Remove(tile);_anchors.Remove(tile);_detail.Remove(tile);Unloaded++;}
        Queued=plan.DesiredCount-_tiles.Keys.Count(t=>PlayableWorldPolicy.Wanted(t,observer,PlayableWorldPolicy.Radius));
        if(_job!=null && !PlayableWorldPolicy.Wanted(_jobTile,observer))_cancel?.Cancel();
        if(_job is {IsCompleted:true})
        {
            try
            {
                var result=_job.GetAwaiter().GetResult();
                if(PlayableWorldPolicy.Accept(_jobRevision,_revision,result.Patch.Tile,observer) && Math.Abs(result.Patch.Anchor.Y-origin.Y)<7000)
                {
                    var timer=Stopwatch.StartNew();var root=Commit(result.Patch);
                    root.Position=ToGodot(result.Patch.Anchor.RelativeTo(_origin));
                    if(_tiles.Remove(result.Patch.Tile,out var old)){RemoveChild(old);old.QueueFree();}
                    AddChild(root);_tiles.Add(result.Patch.Tile,root);_anchors[result.Patch.Tile]=result.Patch.Anchor;_detail[result.Patch.Tile]=result.Patch.Detailed;
                    WorkerMs=result.Time;CommitMs=timer.Elapsed.TotalMilliseconds;MaxCommitMs=Math.Max(MaxCommitMs,CommitMs);Builds++;
                }
                else Discarded++;
            }
            catch(OperationCanceledException){Discarded++;}
            catch(Exception error){Failure=error.Message;_failed.Add(_jobTile);GD.PushError(error.ToString());}
            finally{_job=null;_cancel?.Dispose();_cancel=null;}
            return; // Engine commit budget is one tile, not a promise of a millisecond deadline.
        }
        if(_job==null)
        {
            if(Math.Abs(observer.Y-_world.Sample(observer.X,observer.Z).Elevation)>6500)return;
            bool Detailed(WorldRegion t)=>Math.Max(Math.Abs(t.X-center.X),Math.Abs(t.Z-center.Z))<=3;
            var upgrades=_tiles.Keys.Where(t=>Detailed(t) && !_detail[t]).OrderBy(t=>Math.Abs(t.X-center.X)+Math.Abs(t.Z-center.Z));
            var downgrades=_tiles.Keys.Where(t=>_detail[t] && !PlayableWorldPolicy.Wanted(t,observer,5)).OrderBy(t=>t.X).ThenBy(t=>t.Z);
            var next=upgrades.Concat(plan.Load.Where(Detailed)).Concat(downgrades).Concat(plan.Load).Where(t=>!_failed.Contains(t)).Take(1).ToArray();
            if(next.Length==0)return;
            _jobTile=next[0];_jobRevision=_revision;_cancel=new();var token=_cancel.Token;var world=_world;var tile=_jobTile;bool detailed=Detailed(tile);
            _job=Task.Run(()=>{var time=Stopwatch.StartNew();var patch=PlayableTerrainPatch.Build(world,tile,token,detailed);return(patch,time.Elapsed.TotalMilliseconds);},token);
        }
    }
    public static Vector3 ToGodot(NVector p)=>new(p.X,p.Y,p.Z);
    private Node3D Commit(PlayableTerrainPatch patch)
    {
        var vertices=patch.Vertices.Select(ToGodot).ToArray();var normals=patch.Normals.Select(ToGodot).ToArray();
        var uv=new Vector2[vertices.Length];var info=new Vector2[vertices.Length];
        for(int i=0;i<vertices.Length;i++){uv[i]=new(vertices[i].X/256+(patch.Detailed?0:2),vertices[i].Z/256);info[i]=new((float)patch.Heights[i],normals[i].Y);}
        using var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex]=vertices;arrays[(int)Mesh.ArrayType.Normal]=normals;arrays[(int)Mesh.ArrayType.Index]=patch.Indices;
        arrays[(int)Mesh.ArrayType.TexUV]=uv;arrays[(int)Mesh.ArrayType.TexUV2]=info;
        var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
        var root=new Node3D{Name=$"A4_{patch.Tile.X}_{patch.Tile.Z}"};root.AddChild(new MeshInstance3D{Mesh=mesh,MaterialOverride=_terrain});
        ColliderMs=0;
        if(patch.Detailed)
        {
            var timer=Stopwatch.StartNew();var body=new StaticBody3D{CollisionLayer=1,CollisionMask=0};
            body.AddChild(new CollisionShape3D{Shape=mesh.CreateTrimeshShape()});root.AddChild(body);ColliderMs=timer.Elapsed.TotalMilliseconds;
        }
        if(patch.SeaIndices.Length>0)
        {
            var waterVertices=patch.SeaVertices.Select(ToGodot).ToArray();
            using var waterArrays=new Godot.Collections.Array();waterArrays.Resize((int)Mesh.ArrayType.Max);
            waterArrays[(int)Mesh.ArrayType.Vertex]=waterVertices;waterArrays[(int)Mesh.ArrayType.Index]=patch.SeaIndices;
            waterArrays[(int)Mesh.ArrayType.Normal]=Enumerable.Repeat(Vector3.Up,waterVertices.Length).ToArray();
            var waterMesh=new ArrayMesh();waterMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,waterArrays);
            root.AddChild(new MeshInstance3D{Mesh=waterMesh,MaterialOverride=_water,CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        }
        if(patch.Detailed)Vegetation(root,patch);
        return root;
    }
    private void Vegetation(Node3D root,PlayableTerrainPatch patch)
    {
        var trees=new List<Transform3D>();var grass=new List<Transform3D>();
        var identity=new GenerationIdentity(_world!.Manifest.Seed,4,_world.Manifest.ProfileDigest);
        for(int i=0;i<64;i++)
        {
            ulong bits=identity.FeatureSeed("lab-scatter",patch.Tile,i);int x=2+(int)(bits%28),z=2+(int)((bits>>9)%28),index=z*PlayableTerrainPatch.Side+x;
            if(patch.Normals[index].Y<.87 || patch.Heights[index]<_world.Manifest.Profile.SeaLevel+2 || patch.Heights[index]>_world.Manifest.Profile.SeaLevel+1600)continue;
            var position=ToGodot(patch.Vertices[index]);float yaw=(float)((bits>>20)%628)*.01f;
            grass.Add(new(new Basis(Vector3.Up,yaw),position+Vector3.Up*.275f));
            if(i<48 && (bits&3)!=0)trees.Add(new(new Basis(Vector3.Up,yaw).Scaled(Vector3.One*(.8f+(bits%60)*.01f)),position));
        }
        foreach(var part in _treeParts)Instance(part.Mesh,trees.Select(t=>t*part.Transform).ToArray(),1800);
        Instance(_grass,grass.ToArray(),150);
        void Instance(Mesh mesh,Transform3D[] transforms,float distance)
        {
            if(transforms.Length==0)return;
            var multi=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=mesh,InstanceCount=transforms.Length};
            for(int i=0;i<transforms.Length;i++)multi.SetInstanceTransform(i,transforms[i]);
            root.AddChild(new MultiMeshInstance3D{Multimesh=multi,VisibilityRangeEnd=distance,CastShadow=distance<200?GeometryInstance3D.ShadowCastingSetting.Off:GeometryInstance3D.ShadowCastingSetting.On});
        }
    }
    public override void _ExitTree()
    {
        _revision++;_cancel?.Cancel();if(_job!=null)_=_job.ContinueWith(t=>{_=t.Exception;},TaskScheduler.Default);
    }
}
