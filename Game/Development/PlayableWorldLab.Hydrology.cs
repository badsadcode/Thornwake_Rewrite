using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using Thornwake.Core;

namespace Thornwake.Development;

public partial class PlayableWorldLab
{
    private Task<(RegionalDrainageTile Tile,HydrologyTileInspection Inspection,LabRiverWindow[] Windows)>? _hydroJob;
    private CancellationTokenSource? _hydroCancel;
    private int _hydroRevision;
    private RegionalDrainageTile? _inspectedTile;
    private HydrologyTileInspection? _inspection;
    private LabRiverWindow[] _windows=Array.Empty<LabRiverWindow>();
    private Node3D? _hydroDrawing;
    private string _hydroSummary="Hydrology: not inspected. F5 requests one bounded tile; unknown does not mean dry.";
    private int _overlayBuilds=-1;
    private WorldRegion _overlayTile;
    private void CancelInspection()
    {
        _hydroCancel?.Cancel();
        // Completion is observed by PollInspection. A new query cannot start while an old worker is alive.
    }
    private void RequestInspection()
    {
        if(_regional==null || _world==null)return;
        if(_hydroJob!=null){_notice="Hydrology worker busy; wait for this bounded request.";return;}
        if(!_regional.Drainage.Grid.TryCell(_position.X,_position.Z,out int cell)){_notice="Outside coarse hydrology domain; no water is inferred.";return;}
        _hydroCancel=new();var token=_hydroCancel.Token;_hydroRevision=_worldRevision;var regional=_regional;var world=_world;
        _notice=$"Inspecting coarse cell {cell}; no terrain changes.";
        _hydroJob=Task.Run(()=>
        {
            var tile=regional.Build(cell,token);var inspection=RegionalHydrologyInspector.Create(regional).Inspect(cell,token);
            var windows=inspection.Corridors.Take(16).Select(c=>PlayableWaterPresentation.InspectReach(c,(x,z)=>world.Sample(x,z).Elevation,token)).ToArray();
            return(tile,inspection,windows);
        },token);
    }
    private void PollInspection()
    {
        if(_hydroJob is not {IsCompleted:true})return;
        try
        {
            var result=_hydroJob.GetAwaiter().GetResult();
            if(_hydroRevision==_worldRevision)
            {
                _inspectedTile=result.Tile;_inspection=result.Inspection;_windows=result.Windows;
                int provisional=_windows.Count(w=>w.Status==LabWaterStatus.ProvisionalReach);
                _hydroSummary=$"Regional/inspection v1 | selected cell {_inspection.Cell} | {_inspection.Corridors.Count} proposals | {provisional} provisional reaches\n"+
                    (_inspectedTile.Lake is {} lake?$"UNRESOLVED basin {_regional!.Drainage.BasinIndex[_inspection.Cell]} / stage {lake.CandidateSpillElevation:F2} / {lake.UnresolvedShoreCells} shore cells":"No lake candidate in selected tile; final flow/storage unavailable.");
                _notice=provisional==0?"No checked river window has terrain-supported banks. Showing proposals only.":"Gold river window: static provisional representation; no flow/storage claim.";
                RebuildHydrology();
            }
        }
        catch(OperationCanceledException){}
        catch(Exception e){_hydroSummary="Inspection failed: "+e.Message;GD.PushError(e.ToString());}
        finally{_hydroJob=null;_hydroCancel?.Dispose();_hydroCancel=null;}
    }
    private void ClearHydrology(){if(_hydroDrawing!=null){RemoveChild(_hydroDrawing);_hydroDrawing.QueueFree();_hydroDrawing=null;}}
    private void RebuildHydrology()
    {
        ClearHydrology();if(_world==null)return;
        var vertices=new List<Vector3>();var colors=new List<Color>();
        void Line(WorldPoint a,WorldPoint b,Color color)
        {
            // Clip globally in doubles before local conversion. Long diagnostic links can cross the view.
            double lo=0,hi=1,dx=b.X-a.X,dz=b.Z-a.Z;
            bool Clip(double p,double q){if(p==0)return q>=0;double t=q/p;if(p<0){if(t>hi)return false;lo=Math.Max(lo,t);}else{if(t<lo)return false;hi=Math.Min(hi,t);}return true;}
            const double radius=1750;
            if(!Clip(-dx,a.X-(_position.X-radius))||!Clip(dx,_position.X+radius-a.X)||!Clip(-dz,a.Z-(_position.Z-radius))||!Clip(dz,_position.Z+radius-a.Z))return;
            WorldPoint At(double t)=>new(a.X+dx*t,a.Y+(b.Y-a.Y)*t,a.Z+dz*t);
            foreach(double t in new[]{lo,hi}){var p=At(t);if(Math.Abs(p.Y-_origin.Y)>8000)return;}
            vertices.Add(PlayableWorldTerrain.ToGodot(At(lo).RelativeTo(_origin)));vertices.Add(PlayableWorldTerrain.ToGodot(At(hi).RelativeTo(_origin)));colors.Add(color);colors.Add(color);
        }
        if(_corridors && _inspection!=null)
        foreach(var corridor in _inspection.Corridors)
        for(int i=1;i<corridor.Points.Count;i++)
        {
            var a=corridor.Points[i-1];var b=corridor.Points[i];var c=corridor.Assessment.Compatibility==CorridorCompatibility.DownhillAtSampleSpacing?new Color("d5d5d5"):new Color("df6184");
            Line(new(a.X,a.BedElevation+1,a.Z),new(b.X,b.BedElevation+1,b.Z),c);
        }
        if(_basins && _inspectedTile?.Lake is {} lake)
        {
            double step=_inspectedTile.SampleSpacing;var cells=lake.InteriorCells.Select(c=>(c.X,c.Z)).ToHashSet();
            foreach(var c in lake.InteriorCells)
            {
                double x=_inspectedTile.Min.X+c.X*step,z=_inspectedTile.Min.Z+c.Z*step,y=lake.CandidateSpillElevation;
                if(Math.Abs(x-_position.X)>1750+step || Math.Abs(z-_position.Z)>1750+step)continue;
                var colour=new Color("e465da");
                if(!cells.Contains((c.X-1,c.Z)))Line(new(x,y,z),new(x,y,z+step),colour);
                if(!cells.Contains((c.X+1,c.Z)))Line(new(x+step,y,z),new(x+step,y,z+step),colour);
                if(!cells.Contains((c.X,c.Z-1)))Line(new(x,y,z),new(x+step,y,z),colour);
                if(!cells.Contains((c.X,c.Z+1)))Line(new(x,y,z+step),new(x+step,y,z+step),colour);
            }
        }
        if(_rings)
        {
            var center=PlayableWorldPolicy.TileAt(_position);
            foreach(int radius in new[]{3,20})
            {
                double x=(center.X-radius)*256d,z=(center.Z-radius)*256d,span=(radius*2+1)*256d;
                // Ring outlines are clipped to the local view; terrain-colour mode also identifies actual retained detail.
                WorldPoint P(double dx,double dz)=>new(x+dx,_world.Sample(x+dx,z+dz).Elevation+4,z+dz);
                var color=radius==3?new Color("5eff8a"):new Color("9a83ff");
                Line(P(0,0),P(span,0),color);Line(P(span,0),P(span,span),color);Line(P(span,span),P(0,span),color);Line(P(0,span),P(0,0),color);
            }
            _terrain.ShowRings(true);
        }
        else _terrain.ShowRings(false);
        if(_streamOverlay)
        {
            var tile=PlayableWorldPolicy.TileAt(_position);
            for(int z=-6;z<=6;z++)for(int x=-6;x<=6;x++)
            {
                var t=new WorldRegion(tile.X+x,tile.Z+z);double px=t.X*256d,pz=t.Z*256d;var color=_terrain.Has(t)?new Color("55cc77"):new Color("eeaa44");
                WorldPoint P(double dx,double dz)=>new(px+dx,_world.Sample(px+dx,pz+dz).Elevation+2,pz+dz);
                Line(P(0,0),P(256,0),color);Line(P(0,0),P(0,256),color);
            }
            _overlayTile=tile;_overlayBuilds=_terrain.Builds;
        }
        _overlayTile=PlayableWorldPolicy.TileAt(_position);_overlayBuilds=_terrain.Builds;
        _hydroDrawing=new Node3D();AddChild(_hydroDrawing);
        if(vertices.Count>0)
        {
            using var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);arrays[(int)Mesh.ArrayType.Vertex]=vertices.ToArray();arrays[(int)Mesh.ArrayType.Color]=colors.ToArray();
            var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines,arrays);
            _hydroDrawing.AddChild(new MeshInstance3D{Mesh=mesh,MaterialOverride=new StandardMaterial3D{VertexColorUseAsAlbedo=true,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,NoDepthTest=true},CastShadow=GeometryInstance3D.ShadowCastingSetting.Off});
        }
        // Only accepted local presentation windows receive a surface. Candidate lake polygons never do.
        foreach(var window in _windows.Where(w=>w.Status==LabWaterStatus.ProvisionalReach))
        {
            if(window.Left.Any(p=>Math.Abs(p.X-_origin.X)>2000||Math.Abs(p.Z-_origin.Z)>2000))continue;
            var points=new List<Vector3>();
            for(int i=1;i<window.Left.Count;i++)foreach(var p in new[]{window.Left[i-1],window.Right[i-1],window.Left[i],window.Right[i-1],window.Right[i],window.Left[i]})points.Add(PlayableWorldTerrain.ToGodot(p.RelativeTo(_origin)));
            using var arrays=new Godot.Collections.Array();arrays.Resize((int)Mesh.ArrayType.Max);arrays[(int)Mesh.ArrayType.Vertex]=points.ToArray();arrays[(int)Mesh.ArrayType.Normal]=Enumerable.Repeat(Vector3.Up,points.Count).ToArray();
            var mesh=new ArrayMesh();mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles,arrays);
            _hydroDrawing.AddChild(new MeshInstance3D{Mesh=mesh,MaterialOverride=new StandardMaterial3D{AlbedoColor=_waterStatus?new Color("b59a40"):new Color("286577"),Roughness=.3f,CullMode=BaseMaterial3D.CullModeEnum.Disabled}});
        }
    }
}
