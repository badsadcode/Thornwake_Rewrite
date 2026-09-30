using Godot;
using Thornwake.Core;

namespace Thornwake.World;

/// <summary>Disposable action visuals. The save's crafting clock is the sole animation clock.</summary>
public sealed class CraftAnimation
{
    private readonly Node3D _actor;
    private readonly Node3D? _left, _right;
    private Node3D? _tool, _stationProps, _moving;
    private string _action = "";
    private Node3D? _station;
    private Node3D? _storedTool;
    private bool _storedToolVisible;
    private Vector3 _leftRest, _rightRest;
    private bool _posing;
    public string CurrentAction => _action;
    public float LastElapsed { get; private set; }

    public CraftAnimation(Node3D actor)
    {
        _actor=actor;_left=actor.GetNodeOrNull<Node3D>("LeftArm");_right=actor.GetNodeOrNull<Node3D>("RightArm");
    }
    public void Clear()
    {
        RestoreStation();
        if(GodotObject.IsInstanceValid(_tool)){_tool!.GetParent().RemoveChild(_tool);_tool.QueueFree();}_tool=null;
        if(_posing&&GodotObject.IsInstanceValid(_actor))
        {
            if(_left!=null)_left.Rotation=_leftRest;if(_right!=null)_right.Rotation=_rightRest;
            var held=_right?.GetNodeOrNull<Node3D>("HeldTool");if(held!=null)held.Show();
        }
        _posing=false;_action="";LastElapsed=0;
    }
    private void RestoreStation()
    {
        if(GodotObject.IsInstanceValid(_storedTool))_storedTool!.Visible=_storedToolVisible;
        if(GodotObject.IsInstanceValid(_stationProps)){_stationProps!.GetParent().RemoveChild(_stationProps);_stationProps.QueueFree();}
        _stationProps=null;_moving=null;_storedTool=null;_station=null;
    }
    public void Update(RecipeDefinition recipe,CraftJob job,Node3D? station,bool inReach,bool advancing,float dt)
    {
        if(!inReach){Clear();return;}
        string action=CraftActivity.Action(recipe);
        if(action!=_action||station!=_station)
        {
            Clear();_action=action;_station=station;_posing=true;
            _leftRest=_left?.Rotation??Vector3.Zero;_rightRest=_right?.Rotation??Vector3.Zero;
            var held=_right?.GetNodeOrNull<Node3D>("HeldTool");if(held!=null)held.Hide();
            if(station!=null)
            {
                _stationProps=Visuals.CraftingWorkpiece(action,station.GetNodeOrNull<Node3D>("CookingPot")==null);station.AddChild(_stationProps);
                _moving=_stationProps.GetNodeOrNull<Node3D>("Moving");
                _storedTool=station.GetNodeOrNull<Node3D>(action=="saw"?"HandSaw":"SmithHammer");
                if(_storedTool!=null){_storedToolVisible=_storedTool.Visible;_storedTool.Hide();}
            }
            if(_right!=null){_tool=Visuals.CraftingTool(action);_right.AddChild(_tool);}
        }
        var equipped=_right?.GetNodeOrNull<Node3D>("HeldTool");if(equipped!=null)equipped.Hide();
        LastElapsed=CraftActivity.Elapsed(job);
        if(!advancing)return;
        Valley.AnimatePerson(_actor,0,false);
        if(station!=null)
        {
            Vector3 workPoint=action=="smith"?new Vector3(.69f,0,.05f):action=="saw"?new Vector3(.25f,0,0):Vector3.Zero;
            Vector3 direction=station.ToGlobal(workPoint)-_actor.GlobalPosition;direction.Y=0;
            if(direction.LengthSquared()>.01f)_actor.Rotation=new(0,Mathf.LerpAngle(_actor.Rotation.Y,Mathf.Atan2(direction.X,direction.Z),1-Mathf.Exp(-dt*9)),0);
        }
        float t=LastElapsed, wave=Mathf.Sin(t*7), tap=Mathf.Pow((Mathf.Sin(t*8)+1)*.5f,3);
        Vector3 left=new(-1.05f,0,-.12f),right=new(-1.0f,0,.12f);
        switch(action)
        {
            case "saw":right=new(-1.15f+wave*.22f,.1f,.08f);left=new(-.94f,0,-.12f);break;
            case "join":right=new(-.85f-tap*.8f,0,.12f);break;
            case "smith":right=new(-.65f-tap*1.5f,0,.15f);left=new(-1.05f,0,-.12f);break;
            case "stir":right=new(-1.0f+Mathf.Sin(t*3)*.09f,Mathf.Cos(t*3)*.18f,.1f);left=new(-.45f,0,-.1f);break;
            case "fire":right=new(-.72f+Mathf.Sin(t*2)*.16f,0,.08f);left=new(-.5f,0,-.12f);break;
            case "bake":right=new(-1.0f+Mathf.Sin(t*2)*.12f,0,.1f);left=new(-.9f+Mathf.Sin(t*2)*.12f,0,-.12f);break;
            default:right=new(-1.05f+wave*.10f,0,.18f);left=new(-1.05f-wave*.1f,0,-.18f);break;
        }
        float blend=1-Mathf.Exp(-dt*14);
        if(_left!=null)_left.Rotation=_left.Rotation.Lerp(left,blend);
        if(_right!=null)_right.Rotation=_right.Rotation.Lerp(right,blend);
        if(_moving!=null)
        {
            if(action=="saw")_moving.Position=new(wave*.16f,0,0);
            else if(action=="smith"||action=="join")_moving.Scale=Vector3.One*(1-tap*.06f);
            else if(action=="stir")_moving.Rotation=new(0,t*1.6f,0);
            else if(action=="bake"||action=="fire")_moving.Position=new(0,0,Mathf.Sin(t*2)*.04f);
        }
    }
}
