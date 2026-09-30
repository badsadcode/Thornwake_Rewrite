using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.Core;

namespace Thornwake.UI;

public partial class GameHud
{
    private Control _playerScreens = null!;
    private VBoxContainer _playerScreenBody = null!;
    private HBoxContainer _playerScreenNavigation = null!;
    private Label _playerScreenTitle = null!, _playerScreenSubtitle = null!;
    private string _playerScreenMode = "";
    public bool IsPlayerScreenOpen => _playerScreens?.Visible ?? false;
    public string ActivePlayerScreen => _playerScreenMode;

    public void BuildPlayerScreens()
    {
        if(_playerScreens!=null)return;
        _playerScreens=Modal(new Color(.025f,.045f,.035f,.86f));
        _playerScreens.Name="PlayerScreens";
        var frame=Card(1120,730);Place(frame,.5f,.5f,-560,-365,1120,730);_playerScreens.AddChild(frame);
        var stack=Stack(frame,28,24,15);
        var header=new HBoxContainer();header.AddThemeConstantOverride("separation",16);
        var titles=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};titles.AddThemeConstantOverride("separation",4);
        _playerScreenTitle=Text("Character",32,Cream,true);titles.AddChild(_playerScreenTitle);
        _playerScreenSubtitle=Text("Your life in Harthwood Valley.",13,Muted);titles.AddChild(_playerScreenSubtitle);
        header.AddChild(titles);
        var close=PlayerButton("Close  [Esc]",HidePanel);close.CustomMinimumSize=new Vector2(125,40);header.AddChild(close);stack.AddChild(header);
        _playerScreenNavigation=new HBoxContainer();stack.AddChild(_playerScreenNavigation);
        _playerScreenBody=new VBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};_playerScreenBody.AddThemeConstantOverride("separation",14);stack.AddChild(_playerScreenBody);
        _playerScreens.Hide();
    }

    public void HidePlayerScreens(){_playerScreens?.Hide();}

    public HBoxContainer CreatePlayerNavigation(string active)
    {
        var navigation=new HBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};navigation.AddThemeConstantOverride("separation",7);
        foreach(var entry in new[]{("inventory","Satchel","I"),("character","Character","P"),("skills","Skills","K"),("journal","Journal","J"),("craft","Handcraft","C")})
        {
            string id=entry.Item1;
            var button=PlayerButton(entry.Item2+"  ["+entry.Item3+"]",()=>Command?.Invoke("screen:"+id));
            button.Name="Navigate_"+id;button.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;button.CustomMinimumSize=new Vector2(0,39);button.AddThemeFontSizeOverride("font_size",13);
            button.AddThemeStyleboxOverride("normal",PlayerStyle(id==active?new Color("394736"):new Color("1c2723"),id==active?Gold:new Color("39493f"),8));
            button.AddThemeColorOverride("font_color",id==active?Cream:Muted);navigation.AddChild(button);
        }
        return navigation;
    }

    private void OpenPlayerScreen(string mode,string title,string subtitle)
    {
        HidePanel();BuildPlayerScreens();_playerScreenMode=mode;
        _playerScreenTitle.Text=title;_playerScreenSubtitle.Text=subtitle;
        ClearChildren(_playerScreenNavigation);_playerScreenNavigation.AddChild(CreatePlayerNavigation(mode));
        ClearChildren(_playerScreenBody);_playerScreens.Show();
    }

    public void ShowCharacter(WorldState state,ContentCatalog catalog)
    {
        OpenPlayerScreen("character","Your character","What you carry, what you have built, and how the journey is treating you.");
        var columns=new HBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};columns.AddThemeConstantOverride("separation",18);_playerScreenBody.AddChild(columns);
        var portraitCard=PlayerSection("THE TRAVELLER",out var portraitBody,320);columns.AddChild(portraitCard);
        string tool=state.Player.EquippedItem;
        portraitBody.AddChild(new PlayerPortrait{EquippedItem=tool,CustomMinimumSize=new Vector2(260,320),SizeFlagsVertical=Control.SizeFlags.ExpandFill});
        var origin=Text("Harthwood Valley",23,Cream,true);origin.HorizontalAlignment=HorizontalAlignment.Center;portraitBody.AddChild(origin);
        var note=PlayerCopy("Your appearance in the valley",12,Muted);note.HorizontalAlignment=HorizontalAlignment.Center;portraitBody.AddChild(note);
        var right=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};right.AddThemeConstantOverride("separation",14);columns.AddChild(right);
        var vitals=PlayerSection("VITALS",out var vitalBody);right.AddChild(vitals);
        PlayerMeter(vitalBody,"Health",state.Player.Health,new Color("b77e6c"));
        PlayerMeter(vitalBody,"Stamina",state.Player.Stamina,new Color("a5ba85"));
        var equipped=PlayerSection("IN YOUR HANDS",out var equippedBody);right.AddChild(equipped);
        var equippedRow=new HBoxContainer();equippedRow.AddThemeConstantOverride("separation",16);
        equippedRow.AddChild(new ItemIcon{Item=tool==""?"hands":tool,CustomMinimumSize=new Vector2(64,64)});
        var equippedInfo=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};equippedInfo.AddThemeConstantOverride("separation",4);
        equippedInfo.AddChild(Text(catalog.Items.TryGetValue(tool,out var item)?item.Name:"Hands free",22,Cream,true));
        equippedInfo.AddChild(PlayerCopy(item?.Description??"Equip a carried tool from your satchel or hotbar.",13,Muted));
        equippedRow.AddChild(equippedInfo);equippedBody.AddChild(equippedRow);
        var facts=new GridContainer{Columns=2,SizeFlagsVertical=Control.SizeFlags.ExpandFill};facts.AddThemeConstantOverride("h_separation",12);facts.AddThemeConstantOverride("v_separation",12);right.AddChild(facts);
        PlayerFact(facts,"CROWNS",state.Player.Inventory.GetValueOrDefault("coins").ToString("N0"),"Available to trade");
        PlayerFact(facts,"CARRIED ITEMS",state.Player.Inventory.Count(p=>p.Key!="coins"&&p.Value>0).ToString(),"Different item types");
        PlayerFact(facts,"PLACED PIECES",state.Structures.Count.ToString(),"Your buildings and furnishings");
        PlayerFact(facts,"TIME IN THE VALLEY",FormatJourneyTime(state.Time),"Active world time");
        _playerScreenBody.AddChild(PlayerCopy("Manage carried tools in your satchel. Your current health, stamina and equipment are shown here.",12,Muted));
    }

    public void ShowSkills(WorldState state,ContentCatalog catalog)
    {
        OpenPlayerScreen("skills","Skills & abilities","Practice comes from doing. Keep useful actions close at hand.");
        var columns=new HBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};columns.AddThemeConstantOverride("separation",18);_playerScreenBody.AddChild(columns);
        var practice=PlayerSection("PRACTICE",out var practiceBody,490);columns.AddChild(practice);
        practiceBody.AddChild(PlayerCopy("Practice earned by gathering, crafting, building and fighting in the valley.",13,Muted));
        PlayerScroll(practiceBody,out var rows);
        var known=new[]{"forestry","mining","foraging","carpentry","crafting","farming","earthworks","trading","combat"};
        foreach(string skill in known.Concat(state.Player.Skills.Keys.Where(k=>!known.Contains(k)).OrderBy(k=>k)))
        {
            var row=new HBoxContainer();row.AddThemeConstantOverride("separation",12);
            row.AddChild(new ItemIcon{Item=PracticeIcon(skill),CustomMinimumSize=new Vector2(36,36)});
            var name=Text(Humanize(skill),15,Cream);name.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;row.AddChild(name);
            var count=Text(state.Player.Skills.GetValueOrDefault(skill).ToString("N0"),18,Gold);count.HorizontalAlignment=HorizontalAlignment.Right;row.AddChild(count);rows.AddChild(row);
            rows.AddChild(PlayerDivider());
        }
        var abilities=PlayerSection("ACTIVE ABILITIES",out var abilityBody);abilities.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;columns.AddChild(abilities);
        abilityBody.AddChild(PlayerCopy("Drag an ability onto a hotbar slot below. Use that slot during play to perform it.",13,Muted));
        PlayerScroll(abilityBody,out var abilityList);
        foreach(var ability in catalog.Abilities.Values)
        {
            var button=new HotbarDragButton{Name="SkillCard_"+ability.Id,BindingKind="skill",BindingId=ability.Id,FocusMode=Control.FocusModeEnum.None,CustomMinimumSize=new Vector2(0,162),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,TooltipText="Drag "+ability.Name+" onto a hotbar slot."};
            button.AddThemeStyleboxOverride("normal",PlayerStyle(new Color("2a382e"),new Color("697b55"),12));
            button.AddThemeStyleboxOverride("hover",PlayerStyle(new Color("354532"),Gold,12));
            var inside=Stack(button,16,14,8);inside.MouseFilter=Control.MouseFilterEnum.Ignore;
            var heading=new HBoxContainer{MouseFilter=Control.MouseFilterEnum.Ignore};heading.AddThemeConstantOverride("separation",12);
            heading.AddChild(new ItemIcon{Item=ability.Id,CustomMinimumSize=new Vector2(43,43)});
            var abilityName=Text(ability.Name,23,Cream,true);abilityName.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;heading.AddChild(abilityName);heading.AddChild(Text("DRAG TO ASSIGN",10,Gold));inside.AddChild(heading);
            inside.AddChild(PlayerCopy(ability.Description,13,Cream));
            var cost=Text($"{ability.StaminaCost:0.#} stamina   ·   {ability.Cooldown:0.#}s cooldown",12,Muted);inside.AddChild(cost);
            abilityList.AddChild(button);
        }
        if(catalog.Abilities.Count==0)abilityList.AddChild(PlayerCopy("No active abilities are available yet.",14,Muted));
        _playerScreenBody.AddChild(CreateHotbarEditor(state,catalog));
    }

    public void ShowJournal(WorldState state,ContentCatalog catalog)
    {
        OpenPlayerScreen("journal","Your journal","A place for beginnings, progress and the stories the valley leaves behind.");
        var columns=new HBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};columns.AddThemeConstantOverride("separation",18);_playerScreenBody.AddChild(columns);
        var journey=PlayerSection("A PLACE TO BEGIN",out var journeyBody,610);columns.AddChild(journey);
        var next=QuestTracker.Definitions.FirstOrDefault(q=>!state.CompletedQuests.Contains(q.Id));
        var nextCard=new PanelContainer();nextCard.AddThemeStyleboxOverride("panel",PlayerStyle(new Color("33422f"),new Color("7d895a"),12));
        var nextBody=new VBoxContainer();nextBody.AddThemeConstantOverride("separation",7);nextCard.AddChild(nextBody);
        nextBody.AddChild(Text(next==null?"THE NEXT CHAPTER IS YOURS":"NEXT OBJECTIVE",10,Gold));
        nextBody.AddChild(Text(next?.Name??"A foothold in the valley",22,Cream,true));
        nextBody.AddChild(PlayerCopy(next?.Objective??"You have completed the valley's starting milestones. Build, craft and explore at your own pace.",14,Cream));journeyBody.AddChild(nextCard);
        int complete=QuestTracker.Definitions.Count(q=>state.CompletedQuests.Contains(q.Id));
        journeyBody.AddChild(Text($"MILESTONES   {complete} / {QuestTracker.Definitions.Count}",11,Muted));
        PlayerScroll(journeyBody,out var quests);
        foreach(var quest in QuestTracker.Definitions)
        {
            bool done=state.CompletedQuests.Contains(quest.Id);
            var card=new PanelContainer();card.AddThemeStyleboxOverride("panel",PlayerStyle(done?new Color("26332c"):new Color("1e2924"),done?new Color("536e50"):new Color("36463d"),10));
            var row=new HBoxContainer();row.AddThemeConstantOverride("separation",13);card.AddChild(row);
            var mark=Text(done?"✓":"○",22,done?new Color("a9c18b"):Muted);mark.CustomMinimumSize=new Vector2(24,0);row.AddChild(mark);
            var copy=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};copy.AddThemeConstantOverride("separation",4);row.AddChild(copy);
            var heading=new HBoxContainer();var title=Text(quest.Name,16,Cream);title.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;heading.AddChild(title);heading.AddChild(Text(done?"COMPLETE":"OPEN",9,done?new Color("a9c18b"):Gold));copy.AddChild(heading);
            copy.AddChild(PlayerCopy(quest.Description,12,Muted));copy.AddChild(PlayerCopy(quest.Objective,12,done?Muted:Cream));quests.AddChild(card);
        }
        var activity=PlayerSection("RECENT ACTIVITY",out var activityBody);activity.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;columns.AddChild(activity);
        activityBody.AddChild(PlayerCopy("The latest entries from your saved world.",12,Muted));
        PlayerScroll(activityBody,out var history);
        if(state.Journal.Count==0)history.AddChild(PlayerCopy("The first page is waiting. Head into the valley and begin your journey.",15,Muted));
        foreach(var entry in state.Journal.AsEnumerable().Reverse().Take(30))
        {
            var row=new HBoxContainer();row.AddThemeConstantOverride("separation",10);row.AddChild(Text("·",22,Gold));
            var text=PlayerCopy(entry,13,Cream);text.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;row.AddChild(text);history.AddChild(row);history.AddChild(PlayerDivider());
        }
    }

    private static StyleBoxFlat PlayerStyle(Color fill,Color border,int padding)
    {
        var style=Box(fill,border);style.CornerRadiusTopLeft=style.CornerRadiusTopRight=style.CornerRadiusBottomLeft=style.CornerRadiusBottomRight=8;
        style.ContentMarginLeft=style.ContentMarginRight=padding;style.ContentMarginTop=style.ContentMarginBottom=padding;return style;
    }
    private static Button PlayerButton(string label,Action pressed)
    {
        var button=ActionButton(label,pressed);button.FocusMode=Control.FocusModeEnum.None;return button;
    }
    private Label PlayerCopy(string text,int size,Color color)
    {
        var label=Text(text,size,color);label.AutowrapMode=TextServer.AutowrapMode.WordSmart;label.AddThemeConstantOverride("line_spacing",3);return label;
    }
    private PanelContainer PlayerSection(string heading,out VBoxContainer body,float width=0)
    {
        var card=new PanelContainer{CustomMinimumSize=new Vector2(width,0),SizeFlagsHorizontal=width==0?Control.SizeFlags.ExpandFill:Control.SizeFlags.Fill};
        card.AddThemeStyleboxOverride("panel",PlayerStyle(new Color("1f2b25"),new Color("425448"),16));
        body=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};body.AddThemeConstantOverride("separation",12);card.AddChild(body);
        body.AddChild(Text(heading,10,Gold));return card;
    }
    private static HSeparator PlayerDivider()
    {
        var separator=new HSeparator();separator.AddThemeStyleboxOverride("separator",new StyleBoxLine{Color=new Color("36463b"),Thickness=1});return separator;
    }
    private static ScrollContainer PlayerScroll(VBoxContainer parent,out VBoxContainer content)
    {
        var scroll=new ScrollContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
        content=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};content.AddThemeConstantOverride("separation",10);scroll.AddChild(content);parent.AddChild(scroll);return scroll;
    }
    private void PlayerMeter(VBoxContainer parent,string label,float value,Color fill)
    {
        var row=new HBoxContainer();var name=Text(label,14,Muted);name.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;row.AddChild(name);row.AddChild(Text($"{MathF.Ceiling(value):0} / 100",14,Cream));parent.AddChild(row);
        var meter=new ProgressBar{MinValue=0,MaxValue=100,Value=value,ShowPercentage=false,CustomMinimumSize=new Vector2(0,8),MouseFilter=Control.MouseFilterEnum.Ignore};
        meter.AddThemeStyleboxOverride("background",PlayerStyle(new Color("131e18"),Colors.Transparent,0));meter.AddThemeStyleboxOverride("fill",PlayerStyle(fill,Colors.Transparent,0));parent.AddChild(meter);
    }
    private void PlayerFact(GridContainer parent,string heading,string value,string detail)
    {
        var card=PlayerSection(heading,out var body);body.AddThemeConstantOverride("separation",5);
        body.AddChild(Text(value,25,Cream,true));body.AddChild(PlayerCopy(detail,11,Muted));parent.AddChild(card);
    }
    private static string FormatJourneyTime(double seconds)=>seconds>=3600?$"{(int)(seconds/3600)}h {(int)(seconds%3600/60)}m":$"{(int)(seconds/60)} min";
    private static string Humanize(string value)=>string.IsNullOrEmpty(value)?"Unknown":char.ToUpperInvariant(value[0])+value[1..].Replace('_',' ');
    private static string PracticeIcon(string skill)=>skill switch{"forestry"=>"wood","mining"=>"stone","foraging"=>"berries","carpentry"=>"hammer","crafting"=>"axe","farming"=>"seeds","earthworks"=>"shovel","trading"=>"coins","combat"=>"sword",_=>"wood"};
}

/// <summary>An original illustrated paper doll matching the traveller's in-world clothing.</summary>
public partial class PlayerPortrait : Control
{
    public string EquippedItem {get;set;}="";
    public PlayerPortrait(){MouseFilter=MouseFilterEnum.Ignore;}
    public override void _Draw()
    {
        float scale=Mathf.Min(Size.X/260,Size.Y/320);DrawSetTransform((Size-new Vector2(260,320)*scale)*.5f,0,Vector2.One*scale);
        void Poly(string color,params Vector2[] points)=>DrawColoredPolygon(points,new Color(color));
        void Line(Vector2 a,Vector2 b,string color,float width=3)=>DrawLine(a,b,new Color(color),width,true);
        DrawCircle(new Vector2(130,137),108,new Color("27382d"));
        DrawArc(new Vector2(130,137),108,Mathf.Pi*.12f,Mathf.Pi*1.88f,80,new Color("52644a"),1,true);
        DrawArc(new Vector2(130,137),99,Mathf.Pi*.55f,Mathf.Pi*1.45f,60,new Color("3b4f3c"),1,true);
        for(int i=0;i<12;i++)
        {
            float a=i*Mathf.Tau/12;var p=new Vector2(130,137)+Vector2.FromAngle(a)*112;var q=new Vector2(130,137)+Vector2.FromAngle(a)*116;DrawLine(p,q,new Color("6f7b54"),1,true);
        }
        var shadow=new Vector2[28];for(int i=0;i<28;i++){float a=i*Mathf.Tau/28;shadow[i]=new Vector2(130+Mathf.Cos(a)*58,292+Mathf.Sin(a)*8);}DrawColoredPolygon(shadow,new Color("14221c"));
        Poly("554d41",new(101,194),new(126,194),new(123,270),new(99,269));
        Poly("655a49",new(132,195),new(155,195),new(163,270),new(139,271));
        Poly("302f26",new(99,257),new(122,258),new(122,285),new(92,285),new(93,276));
        Poly("37352a",new(139,258),new(160,257),new(171,280),new(170,286),new(137,286));
        Poly("354e43",new(95,108),new(111,102),new(111,141),new(89,178),new(74,170),new(78,133));
        Poly("607e69",new(148,103),new(165,109),new(181,155),new(164,162),new(148,133));
        Poly("b79170",new(76,166),new(90,172),new(88,199),new(75,203),new(69,192));
        Poly("c49e7b",new(164,151),new(180,150),new(188,176),new(174,184),new(163,174));
        Poly("526b61",new(107,98),new(147,98),new(162,121),new(149,159),new(161,208),new(98,208),new(107,157),new(94,121));
        Poly("688174",new(128,100),new(146,99),new(155,126),new(144,155),new(147,192),new(129,202));
        Poly("3f594d",new(98,188),new(158,188),new(162,210),new(98,210));
        DrawRect(new Rect2(103,160,51,9),new Color("45382b"));DrawRect(new Rect2(124,158,13,13),new Color("bda572"));DrawRect(new Rect2(127,161,7,7),new Color("65533b"));
        Poly("ae8769",new(118,80),new(139,80),new(140,103),new(129,112),new(117,100));
        DrawCircle(new Vector2(129,68),24,new Color("c09a78"));
        Poly("49382d",new(106,72),new(106,52),new(115,40),new(139,42),new(151,53),new(151,73),new(142,58),new(130,59),new(120,52),new(114,75));
        Line(new(116,68),new(121,68),"43392e",2);Line(new(136,68),new(141,68),"43392e",2);
        Line(new(128,70),new(126,77),"a3785b",2);Line(new(121,84),new(136,84),"835e47",1.5f);
        if(EquippedItem.Contains("axe")||EquippedItem=="hammer"||EquippedItem=="hoe"||EquippedItem=="shovel")
        {
            Line(new(181,213),new(198,114),"a5865b",7);
            if(EquippedItem=="hammer")Poly("86958b",new(181,111),new(208,114),new(212,131),new(180,128));
            else if(EquippedItem=="shovel")Poly("a5865b",new(185,117),new(207,123),new(218,105),new(215,91),new(197,88),new(188,99));
            else if(EquippedItem=="hoe")Poly("8f9d91",new(193,112),new(214,120),new(222,138),new(208,145),new(203,124),new(190,120));
            else Poly("9daaa0",new(188,106),new(211,114),new(219,142),new(207,152),new(193,131),new(185,128));
        }
        else if(EquippedItem.Contains("sword"))
        {
            Line(new(177,192),new(179,168),"776346",6);Line(new(164,164),new(194,167),"a0afa5",5);
            Poly("bac6ba",new(174,166),new(178,104),new(184,91),new(187,108),new(182,167));
        }
        else if(EquippedItem=="seeds")Poly("a0895b",new(177,176),new(188,176),new(196,196),new(188,208),new(175,207),new(170,196));
        // A restrained baseline grounds the illustrated figure without implying equipment slots.
        Line(new(79,301),new(181,301),"73825b",1);
    }
}
