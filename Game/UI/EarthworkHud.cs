using Godot;
using System;
using System.Collections.Generic;
using Thornwake.Core;

namespace Thornwake.UI;

public partial class GameHud
{
    private Panel _earthworkCard=null!;
    private Label _earthworkTitle=null!,_earthworkStatus=null!;
    private ProgressBar _earthworkProgress=null!;
    private readonly Dictionary<EarthworkMode,Button> _earthworkButtons=new();
    public bool IsEarthworkHovered=>_earthworkCard.Visible&&_earthworkCard.GetGlobalRect().HasPoint(_earthworkCard.GetGlobalMousePosition());
    public bool IsEarthworkVisible=>_earthworkCard.Visible;
    public string EarthworkStatus=>_earthworkStatus.Text;
    private void BuildEarthworkTools()
    {
        _earthworkCard=Card(650,116);Place(_earthworkCard,.5f,1,-325,-366,650,116);_hud.AddChild(_earthworkCard);
        var stack=Stack(_earthworkCard,14,10,6);
        _earthworkTitle=Text("EARTHWORKS",11,Gold);stack.AddChild(_earthworkTitle);
        var row=new HBoxContainer();row.AddThemeConstantOverride("separation",6);stack.AddChild(row);
        foreach(var mode in Enum.GetValues<EarthworkMode>())
        {
            var button=ActionButton(mode.ToString(),()=>Command?.Invoke("earthwork:"+mode));
            button.CustomMinimumSize=new Vector2(0,32);button.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;button.FocusMode=Control.FocusModeEnum.None;
            button.TooltipText=mode switch{EarthworkMode.Dig=>"Remove loose soil and collect it.",EarthworkMode.Fill=>"Spend loose soil to raise the ground.",EarthworkMode.Level=>"Cut or fill toward your chosen height. G samples the ground; Alt + wheel adjusts it.",_=>"Soften uneven ground, reusing excavated soil where possible."};
            row.AddChild(button);_earthworkButtons[mode]=button;
        }
        _earthworkStatus=Text("",12,Cream);_earthworkStatus.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;stack.AddChild(_earthworkStatus);
        _earthworkProgress=new ProgressBar{MinValue=0,MaxValue=1,ShowPercentage=false,CustomMinimumSize=new Vector2(0,5),MouseFilter=Control.MouseFilterEnum.Ignore};
        _earthworkProgress.AddThemeStyleboxOverride("fill",Box(Gold,Colors.Transparent));_earthworkProgress.AddThemeStyleboxOverride("background",Box(new Color("111c16"),Colors.Transparent));stack.AddChild(_earthworkProgress);
        _earthworkCard.Hide();
    }
    public void SetEarthworkTools(bool visible,EarthworkMode mode,float radius,float target,int soil,EarthworkPlan? plan,EarthworkJob? job)
    {
        _earthworkCard.Visible=visible&&!IsPanelOpen&&!IsTitleVisible;
        if(!_earthworkCard.Visible)return;
        _earthworkTitle.Text=$"EARTHWORKS  ·  {radius:0.0} m brush  ·  {soil} soil (10 L each)"+(mode==EarthworkMode.Level?$"  ·  Target {target:0.00} m":"  ·  Dashed ring: clearance");
        _earthworkStatus.Text=job!=null?$"{job.Mode} in progress · {job.Remaining:0.0} sec · Move / right click to cancel":plan?.Message??"Point at open ground within five paces.";
        _earthworkStatus.Modulate=job!=null||plan?.Valid==true?Colors.White:new Color("e5b095");
        _earthworkProgress.Value=job==null?0:1-job.Remaining/job.Duration;
        foreach(var pair in _earthworkButtons){pair.Value.Modulate=pair.Key==mode?Gold:Colors.White;pair.Value.Disabled=job!=null;}
    }
}
