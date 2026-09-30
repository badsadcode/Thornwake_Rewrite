using Godot;
using System;
using Thornwake.Core;

namespace Thornwake;

public partial class Main
{
    private EarthworkMode _earthworkMode;
    private float _earthworkRadius=1.5f,_earthworkTarget;
    private Vector3? _earthworkAim;
    private EarthworkPlan? _earthworkPlan;
    private MeshInstance3D? _earthworkMarker;
    private string _earthworkMarkerStamp="";
    private bool _earthworkPose;
    private void ResetEarthworkPresentation()
    {
        if(_earthworkMarker!=null){RemoveChild(_earthworkMarker);_earthworkMarker.QueueFree();_earthworkMarker=null;}
        _earthworkAim=null;_earthworkPlan=null;_earthworkMarkerStamp="";_earthworkPose=false;
    }
    private void SetEarthworkMode(EarthworkMode mode)
    {
        if(_state.EarthworkJob!=null)return;
        _earthworkMode=mode;UpdateEarthworkPreview();
    }
    private void HandleEarthworkCommand(string command)
    {
        if(_slot==5&&Enum.TryParse<EarthworkMode>(command[10..],out var mode)&&Enum.IsDefined(mode))SetEarthworkMode(mode);
    }
    private bool HandleEarthworkWheel(InputEventMouseButton button)
    {
        if(!_cameraRig.CanControl||_slot!=5||(!button.CtrlPressed&&!button.AltPressed)||button.ButtonIndex is not (MouseButton.WheelUp or MouseButton.WheelDown))return false;
        if(!button.Pressed||_state.EarthworkJob!=null)return true;
        int direction=button.ButtonIndex==MouseButton.WheelUp?1:-1;
        if(button.CtrlPressed)_earthworkRadius=Mathf.Clamp(_earthworkRadius+direction*.25f,1,2.5f);
        else{_earthworkMode=EarthworkMode.Level;_earthworkTarget=Mathf.Clamp(_earthworkTarget+direction*.1f,-20,20);}
        UpdateEarthworkPreview();return true;
    }
    private void StartShoveling()
    {
        UpdateEarthworkPreview();if(_earthworkAim is not {} point)return;
        var result=_sim.StartEarthwork(_earthworkMode,point.X,point.Z,_earthworkRadius,_earthworkTarget);Report(result);
        if(result.Success){Face(point);_velocity=Vector3.Zero;}
    }
    private void TickEarthworkPresentation(float dt)
    {
        var job=_state.EarthworkJob;
        var result=_sim.TickEarthwork(dt);
        if(result is {} outcome)
        {
            Report(outcome);
            if(outcome.Success&&job!=null){_valley.RefreshTerrain(job.X,job.Z,job.Radius+TerrainGrid.SupportRadius);_map.RefreshTerrain();_sound.Play("stone");}
        }
    }
    private void UpdateEarthworkPresentation()
    {
        var job=_state.EarthworkJob;
        _hud.SetEarthworkTools(_started&&_slot==5,job?.Mode??_earthworkMode,job?.Radius??_earthworkRadius,job?.TargetHeight??_earthworkTarget,_sim.Count("soil"),_earthworkPlan,job);
        if(_earthworkMarker!=null)_earthworkMarker.Visible=_started&&_slot==5&&!_hud.IsPanelOpen;
        if(job!=null)
        {
            float progress=1-job.Remaining/job.Duration;
            var arm=_player.GetNodeOrNull<Node3D>("RightArm");
            if(arm!=null)arm.Rotation=new Vector3(-.25f-Mathf.Sin(progress*Mathf.Pi)*1.4f,0,-.15f);
            _earthworkPose=true;
        }
        else if(_earthworkPose)
        {
            var arm=_player.GetNodeOrNull<Node3D>("RightArm");if(arm!=null)arm.Rotation=Vector3.Zero;_earthworkPose=false;
        }
    }
    private void UpdateEarthworkPreview()
    {
        var job=_state.EarthworkJob;
        Vector3 point=job!=null?new Vector3(job.X,_valley.Height(job.X,job.Z),job.Z):_hud.IsEarthworkHovered&&_earthworkAim.HasValue?_earthworkAim.Value:MouseWorld();
        _earthworkAim=point;
        float radius=job?.Radius??_earthworkRadius,target=job?.TargetHeight??_earthworkTarget;
        var mode=job?.Mode??_earthworkMode;
        _earthworkPlan=_sim.PreviewEarthwork(mode,point.X,point.Z,radius,target);
        string stamp=$"{point.X:0.00}:{point.Z:0.00}:{radius}:{mode}:{target}:{_earthworkPlan.Valid}:{_state.Earthworks.Revision}:{_state.TerrainEdits.Count}";
        if(stamp==_earthworkMarkerStamp&&_earthworkMarker!=null)return;
        _earthworkMarkerStamp=stamp;
        if(_earthworkMarker==null)
        {
            _earthworkMarker=new MeshInstance3D{Name="ShovelWorkArea",CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_earthworkMarker);
            _earthworkMarker.MaterialOverride=new StandardMaterial3D{VertexColorUseAsAlbedo=true,ShadingMode=BaseMaterial3D.ShadingModeEnum.Unshaded,CullMode=BaseMaterial3D.CullModeEnum.Disabled,Transparency=BaseMaterial3D.TransparencyEnum.Alpha};
        }
        using var surface=new SurfaceTool();surface.Begin(Mesh.PrimitiveType.Triangles);
        Color color=_earthworkPlan.Valid?new Color("b4db8d"):new Color("e9896e");
        Vector3 At(float x,float z,float? height=null)=>new(x,(height??_valley.Height(x,z))+.055f,z);
        void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color tint)
        {surface.SetColor(tint);surface.AddVertex(a);surface.AddVertex(b);surface.AddVertex(c);surface.AddVertex(a);surface.AddVertex(c);surface.AddVertex(d);}
        void Ring(float r,bool dashed)
        {
            for(int i=0;i<64;i++)
            {
                if(dashed&&i%4>=2)continue;
                float a=i*Mathf.Tau/64,b=(i+1)*Mathf.Tau/64,w=dashed?.025f:.045f;
                Quad(At(point.X+Mathf.Cos(a)*(r-w),point.Z+Mathf.Sin(a)*(r-w)),At(point.X+Mathf.Cos(a)*(r+w),point.Z+Mathf.Sin(a)*(r+w)),At(point.X+Mathf.Cos(b)*(r+w),point.Z+Mathf.Sin(b)*(r+w)),At(point.X+Mathf.Cos(b)*(r-w),point.Z+Mathf.Sin(b)*(r-w)),dashed?new Color(color,.5f):color);
            }
        }
        Ring(radius,false);Ring(radius+TerrainGrid.SupportRadius,true);
        Quad(At(point.X-.3f,point.Z-.025f),At(point.X+.3f,point.Z-.025f),At(point.X+.3f,point.Z+.025f),At(point.X-.3f,point.Z+.025f),color);
        Quad(At(point.X-.025f,point.Z-.3f),At(point.X+.025f,point.Z-.3f),At(point.X+.025f,point.Z+.3f),At(point.X-.025f,point.Z+.3f),color);
        if(mode==EarthworkMode.Level)
        {
            float r=radius*.65f;
            Quad(At(point.X-r,point.Z-r,target),At(point.X+r,point.Z-r,target),At(point.X+r,point.Z+r,target),At(point.X-r,point.Z+r,target),new Color("ddc98b",.2f));
            Quad(At(point.X-.025f,point.Z),At(point.X+.025f,point.Z),At(point.X+.025f,point.Z,target),At(point.X-.025f,point.Z,target),new Color("ddc98b"));
        }
        var old=_earthworkMarker.Mesh;_earthworkMarker.Mesh=surface.Commit();old?.Dispose();
    }
}
