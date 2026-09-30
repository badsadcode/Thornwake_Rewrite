using Godot;
using System;
using Thornwake.Core;

namespace Thornwake.UI;

public partial class GameHud
{
    private Panel _activityCard=null!;
    private Label _activityTitle=null!,_activityStage=null!,_activityTime=null!;
    private ProgressBar _activityProgress=null!;
    private ItemIcon _activityIcon=null!;
    private Button _watchWork=null!;
    public bool IsActivityVisible=>_activityCard.Visible;
    public string ActivityStatus=>_activityStage.Text;
    public void BuildCraftActivity()
    {
        _activityCard=Card(294,152);Place(_activityCard,0,1,24,-350,294,152);_hud.AddChild(_activityCard);
        var stack=Stack(_activityCard,15,12,7);var row=new HBoxContainer();row.AddThemeConstantOverride("separation",10);
        _activityIcon=new ItemIcon{CustomMinimumSize=new Vector2(38,38)};row.AddChild(_activityIcon);
        var labels=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};labels.AddThemeConstantOverride("separation",3);
        labels.AddChild(Text("CURRENT WORK",10,Gold));_activityTitle=Text("",15,Cream);_activityTitle.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;labels.AddChild(_activityTitle);row.AddChild(labels);stack.AddChild(row);
        _activityStage=Text("",12,Muted);stack.AddChild(_activityStage);
        _activityProgress=new ProgressBar{MinValue=0,MaxValue=100,ShowPercentage=false,CustomMinimumSize=new Vector2(0,7),MouseFilter=Control.MouseFilterEnum.Ignore};
        _activityProgress.AddThemeStyleboxOverride("fill",Box(Gold,Colors.Transparent));_activityProgress.AddThemeStyleboxOverride("background",Box(new Color("111c16"),Colors.Transparent));stack.AddChild(_activityProgress);
        _activityTime=Text("",11,Cream);stack.AddChild(_activityTime);
        stack.AddChild(Text("E  Open work queue  ·  Cancel in queue",11,Muted));_activityCard.Hide();
    }
    public void UpdateCraftActivity(WorldState state,ContentCatalog catalog,bool inReach,bool running)
    {
        var job=state.CraftJob;
        _activityCard.Visible=job!=null&&!IsPanelOpen&&!IsTitleVisible;
        if(_watchWork!=null)_watchWork.Visible=IsWorkshopOpen&&job!=null;
        if(job==null)return;
        var recipe=catalog.Recipes[job.RecipeId];string stage=!inReach?"Paused — return to the station":!running?"Paused":CraftActivity.Stage(recipe,job);
        _activityIcon.Item=recipe.Output;_activityIcon.QueueRedraw();_activityTitle.Text=recipe.Name;_activityStage.Text=stage;
        _activityProgress.Value=CraftActivity.Progress(job)*100;
        _activityTime.Text=$"{MathF.Ceiling(job.Remaining)} sec left in batch  ·  {state.CraftQueue.Count} {(state.CraftQueue.Count==1?"order":"orders")}";
        if(_workLabel!=null&&GodotObject.IsInstanceValid(_workLabel))_workLabel.Text=$"{stage}  ·  {MathF.Ceiling(job.Remaining)} sec left";
    }
}
