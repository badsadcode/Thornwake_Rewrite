using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.Core;

namespace Thornwake.UI;

public partial class GameHud
{
    private Control _collection = null!;
    private VBoxContainer _collectionBody = null!, _details = null!;
    private HBoxContainer _collectionTabs = null!;
    private GridContainer _collectionGrid = null!;
    private Label _collectionTitle = null!, _collectionSubtitle = null!, _collectionSummary = null!;
    private Label? _workLabel;
    private ProgressBar? _workProgress;
    private Button? _craftAction;
    private Panel _queuePanel = null!;
    private VBoxContainer _queueContents = null!;
    private readonly Dictionary<string,Button> _recipeCards = new();
    private readonly Dictionary<string,HotbarDragButton> _inventoryCards = new();
    private VBoxContainer _collectionNavigation = null!, _inventoryEditor = null!;
    private int _craftQuantity = 1;
    private bool _craftUntil;
    private string _queueStatus="Waiting for ingredients or the required station";
    private WorldState? _menuState;
    private ContentCatalog? _menuCatalog;
    private string _collectionMode = "inventory", _category = "All", _selectedEntry = "", _station = "hand";
    public bool IsWorkshopOpen => (_collection?.Visible ?? false) && _collectionMode == "craft";
    public bool IsInventoryOpen => (_collection?.Visible ?? false) && _collectionMode == "inventory";
    public bool IsBuildingOpen => (_collection?.Visible ?? false) && _collectionMode == "build";
    public string ActiveCraftStation => _station;

    private void BuildCollection()
    {
        _collection = Modal(new Color(.025f,.045f,.035f,.82f));
        var card = Card(1120,730);
        Place(card,.5f,.5f,-560,-365,1120,730);
        _collection.AddChild(card);
        _collectionBody = Stack(card,28,25,14);
        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation",18);
        var titles = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        titles.AddThemeConstantOverride("separation",4);
        _collectionTitle = Text("Your satchel",32,Cream,true);
        _collectionSubtitle = Text("Everything you carry, ready for the journey.",13,Muted);
        titles.AddChild(_collectionTitle); titles.AddChild(_collectionSubtitle); header.AddChild(titles);
        _watchWork=ActionButton("Watch work",()=>Command?.Invoke("watchwork"));_watchWork.Name="WatchWork";_watchWork.Hide();header.AddChild(_watchWork);
        header.AddChild(ActionButton("Close  [Esc]",HidePanel));
        _collectionBody.AddChild(header);
        _collectionNavigation=new VBoxContainer();_collectionBody.AddChild(_collectionNavigation);
        var separator = new HSeparator();
        separator.AddThemeStyleboxOverride("separator",new StyleBoxLine{Color=new Color("566047"),Thickness=1});
        _collectionBody.AddChild(separator);
        _collectionTabs = new HBoxContainer();_collectionTabs.AddThemeConstantOverride("separation",7);_collectionBody.AddChild(_collectionTabs);
        var body = new HBoxContainer{SizeFlagsVertical=Control.SizeFlags.ExpandFill};body.AddThemeConstantOverride("separation",20);
        var scroll = new ScrollContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled};
        _collectionGrid=new GridContainer{Columns=6,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ShrinkBegin};
        _collectionGrid.AddThemeConstantOverride("h_separation",9);_collectionGrid.AddThemeConstantOverride("v_separation",9);
        scroll.AddChild(_collectionGrid);body.AddChild(scroll);
        var detailCard = Card(320,0);detailCard.SizeFlagsVertical=Control.SizeFlags.ExpandFill;detailCard.CustomMinimumSize=new Vector2(320,0);
        detailCard.AddThemeStyleboxOverride("panel",Box(new Color("202a22"),new Color("48563f")));
        var detailMargin=Full(new MarginContainer());
        detailMargin.AddThemeConstantOverride("margin_left",18);detailMargin.AddThemeConstantOverride("margin_right",18);
        detailMargin.AddThemeConstantOverride("margin_top",16);detailMargin.AddThemeConstantOverride("margin_bottom",16);detailCard.AddChild(detailMargin);
        var detailScroll=new ScrollContainer{HorizontalScrollMode=ScrollContainer.ScrollMode.Disabled,SizeFlagsVertical=Control.SizeFlags.ExpandFill};detailMargin.AddChild(detailScroll);
        _details=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ExpandFill};_details.AddThemeConstantOverride("separation",9);
        detailScroll.AddChild(_details);body.AddChild(detailCard);_collectionBody.AddChild(body);
        _queuePanel=Card(0,120);_queuePanel.CustomMinimumSize=new Vector2(0,120);
        _queueContents=Stack(_queuePanel,13,9,7);_collectionBody.AddChild(_queuePanel);_queuePanel.Hide();
        _inventoryEditor=new VBoxContainer();_collectionBody.AddChild(_inventoryEditor);_inventoryEditor.Hide();
        _collectionSummary=Text("",12,Muted);_collectionBody.AddChild(_collectionSummary);
        _collection.Hide();
    }

    public void ShowInventory(WorldState state,ContentCatalog catalog)
    {
        SetCollection("inventory",state,catalog);
        _collectionTitle.Text="Your satchel";
        _collectionSubtitle.Text="A place for what you gather, make and carry.";
        RenderCollection();
    }
    public void ShowCrafting(WorldState state,ContentCatalog catalog,string station="hand")
    {
        bool changedStation=_station!=station;
        SetCollection("craft",state,catalog);_station=station;
        if(changedStation){_selectedEntry="";_category="All";_craftQuantity=1;_craftUntil=false;}
        _collectionTitle.Text=station=="hand"?"Made by hand":StationName(station);
        _collectionSubtitle.Text=station=="hand"?"Simple tools and useful things, wherever the road takes you.":"Choose a recipe. Good work takes a little time.";
        RenderCollection();
    }
    public void ShowBuilding(WorldState state,ContentCatalog catalog)
    {
        SetCollection("build",state,catalog);
        _collectionTitle.Text="Make yourself at home";
        _collectionSubtitle.Text="Build a shelter, furnish a room, shape a place of your own.";
        RenderCollection();
    }
    private void SetCollection(string mode,WorldState state,ContentCatalog catalog)
    {
        HideSandbox();
        if(_collectionMode!=mode){_selectedEntry="";_category="All";_craftQuantity=1;_craftUntil=false;}
        _collectionMode=mode;_menuState=state;_menuCatalog=catalog;
        _panel.Hide();_pause.Hide();HidePlayerScreens();_collection.Show();
    }
    private static void ClearChildren(Node parent)
    {
        foreach(Node child in parent.GetChildren()){parent.RemoveChild(child);child.QueueFree();}
    }
    private void RenderCollection()
    {
        if(_menuState==null||_menuCatalog==null)return;
        ClearChildren(_collectionTabs);ClearChildren(_collectionGrid);ClearChildren(_details);
        ClearChildren(_collectionNavigation);_collectionNavigation.AddChild(CreatePlayerNavigation(_collectionMode));
        ClearChildren(_inventoryEditor);_inventoryEditor.Hide();_inventoryCards.Clear();
        _recipeCards.Clear();_queuePanel.Hide();ClearChildren(_queueContents);
        _workLabel=null;_workProgress=null;_craftAction=null;
        var state=_menuState;var catalog=_menuCatalog;
        string[] categories=_collectionMode switch
        {
            "inventory"=>state.Player.Inventory.Where(x=>x.Value>0&&x.Key!="coins").Select(x=>ItemCategory(x.Key)).Distinct().OrderBy(x=>x).ToArray(),
            "craft"=>catalog.Recipes.Values.Select(x=>ItemCategory(x.Output)).Distinct().OrderBy(x=>x).ToArray(),
            _=>BuildCatalogue(catalog).Select(x=>Category(x.Category)).Distinct().OrderBy(x=>x).ToArray()
        };
        if(_category!="All"&&!categories.Contains(_category))_category="All";
        foreach(string category in new[]{"All"}.Concat(categories))
        {
            string captured=category;
            var tab=ActionButton(category,()=>{_category=captured;_selectedEntry="";RenderCollection();});
            tab.CustomMinimumSize=new Vector2(83,36);tab.AddThemeFontSizeOverride("font_size",13);
            if(_category==category)tab.AddThemeStyleboxOverride("normal",Box(new Color("4b5338"),Gold));
            _collectionTabs.AddChild(tab);
        }
        if(_collectionMode=="inventory")RenderInventory(state,catalog);
        else if(_collectionMode=="craft")RenderRecipes(state,catalog);
        else RenderBuildings(state,catalog);
    }

    private void RenderInventory(WorldState state,ContentCatalog catalog)
    {
        _collectionGrid.Columns=6;
        var items=state.Player.Inventory.Where(x=>x.Value>0&&x.Key!="coins"&&catalog.Items.ContainsKey(x.Key)&&(_category=="All"||ItemCategory(x.Key)==_category)).OrderBy(x=>ItemCategory(x.Key)).ThenBy(x=>catalog.Items[x.Key].Name).ToArray();
        if(!items.Any(x=>x.Key==_selectedEntry))_selectedEntry=items.FirstOrDefault().Key??"";
        foreach(var item in items)
        {
            string id=item.Key;var def=catalog.Items[id];
            var b=new HotbarDragButton{Name="InventoryItem_"+id,BindingKind="item",BindingId=id,DragAllowed=HotbarRules.CanBind(state,catalog,"item",id),CustomMinimumSize=new Vector2(104,116),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,FocusMode=Control.FocusModeEnum.All,ClipContents=true};
            b.AddThemeStyleboxOverride("normal",Box(id==_selectedEntry?new Color("3d4933"):new Color("202b23"),id==_selectedEntry?Gold:new Color("46533e")));
            b.Pressed+=()=>SelectInventoryItem(id);_inventoryCards[id]=b;
            var stack=Stack(b,5,5,0);
            var icon=new ItemIcon{Item=id,CustomMinimumSize=new Vector2(0,62),SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};stack.AddChild(icon);
            var name=Text(def.Name,11,id==_selectedEntry?Cream:Muted);name.HorizontalAlignment=HorizontalAlignment.Center;
            name.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;stack.AddChild(name);
            var count=Text(item.Value.ToString(),13,Gold);count.HorizontalAlignment=HorizontalAlignment.Right;stack.AddChild(count);
            b.TooltipText=$"{def.Name} × {item.Value}"+(b.DragAllowed?"\nDrag to a quick-access slot.":"");_collectionGrid.AddChild(b);
        }
        for(int i=items.Length;i<Math.Max(24,items.Length);i++)
        {
            var empty=Card(104,116);empty.AddThemeStyleboxOverride("panel",Box(new Color("1b241f"),new Color("354133")));
            var dot=Text("·",18,new Color("42503d"));dot.HorizontalAlignment=HorizontalAlignment.Center;dot.VerticalAlignment=VerticalAlignment.Center;Full(dot);empty.AddChild(dot);_collectionGrid.AddChild(empty);
        }
        RenderInventoryDetails();
        _inventoryEditor.Show();_inventoryEditor.AddChild(CreateHotbarEditor(state,catalog));
        int coins=state.Player.Inventory.GetValueOrDefault("coins");
        _collectionSummary.Text=$"{state.Player.Inventory.Count(x=>x.Value>0&&x.Key!="coins")} kinds of supplies carried     ·     {coins} crowns     ·     Drag items to quick access; supplies remain in your satchel";
    }
    private void SelectInventoryItem(string id)
    {
        _selectedEntry=id;
        foreach(var card in _inventoryCards)card.Value.AddThemeStyleboxOverride("normal",Box(card.Key==id?new Color("3d4933"):new Color("202b23"),card.Key==id?Gold:new Color("46533e")));
        ClearChildren(_details);RenderInventoryDetails();
    }
    private void RenderInventoryDetails()
    {
        if(_menuState==null||_menuCatalog==null)return;
        var state=_menuState;var catalog=_menuCatalog;
        if(_selectedEntry!=""&&catalog.Items.ContainsKey(_selectedEntry))
        {
            var def=catalog.Items[_selectedEntry];DetailHeader(def.Id,def.Name,ItemCategory(def.Id));
            DetailText(string.IsNullOrWhiteSpace(def.Description)?DefaultDescription(def.Id):def.Description);
            DetailPair("In your satchel",state.Player.Inventory[def.Id].ToString());
            if(def.SellPrice>0)DetailPair("Trade value",$"{def.SellPrice} crowns each");
            if(def.RestoreHealth>0)DetailPair("Restores health",$"+{def.RestoreHealth:0}");
            if(def.RestoreStamina>0)DetailPair("Restores stamina",$"+{def.RestoreStamina:0}");
            if(def.Cooldown>0)DetailPair("Reuse time",$"{def.Cooldown:0.#} seconds");
            if(HotbarRules.CanBind(state,catalog,"item",def.Id))
            {
                string action=state.Player.EquippedItem==def.Id?"Equipped":catalog.Buildings.ContainsKey(def.Id)?"Place in the world":HotbarRules.ToolMode(catalog,def.Id)>=0?"Equip item":"Use item";
                var use=ActionButton(action,()=>Command?.Invoke("item:use:"+def.Id),true);use.Disabled=state.Player.EquippedItem==def.Id;_details.AddChild(use);
                DetailText("Drag this item onto quick access below. The slot keeps its link when you use the last item, ready for your next supply.",12);
            }
        }
        else {DetailHeader("satchel","Room for a new find","SATCHEL");DetailText("Gather resources in the valley to fill your satchel.");}
        _details.AddChild(new Control{SizeFlagsVertical=Control.SizeFlags.ExpandFill,MouseFilter=Control.MouseFilterEnum.Ignore});
        _details.AddChild(Text("IN YOUR HANDS",11,Gold));
        var equipped=state.Player.EquippedItem;
        DetailText(catalog.Items.GetValueOrDefault(equipped)?.Name??"Nothing equipped",14);
        DetailText("Tools, weapons, provisions and skills can share your ten quick-access slots.",12);
    }

    private void RenderRecipes(WorldState state,ContentCatalog catalog)
    {
        _collectionGrid.Columns=3;
        var recipes=catalog.Recipes.Values.Where(x=>_category=="All"||ItemCategory(x.Output)==_category).OrderBy(x=>x.Station==_station?0:1).ThenBy(x=>x.Name).ToArray();
        if(!recipes.Any(x=>x.Id==_selectedEntry)){_selectedEntry=recipes.FirstOrDefault()?.Id??"";_craftQuantity=1;_craftUntil=false;}
        foreach(var recipe in recipes)
        {
            string id=recipe.Id;bool stationAvailable=recipe.Station=="hand"||recipe.Station==_station;bool afford=CanAfford(recipe.Costs);
            var b=EntryButton(id,new Vector2(210,159),()=>SelectRecipe(id));
            b.Name="Recipe_"+id;_recipeCards[id]=b;
            b.GuiInput+=input=>
            {
                if(input is InputEventMouseButton{ButtonIndex:MouseButton.Left,Pressed:true,DoubleClick:true})
                {b.AcceptEvent();SelectRecipe(id);EnqueueRecipe(id);}
            };
            b.TooltipText="Select to inspect. Double-click to add this recipe to your work queue.";
            var stack=Stack(b,12,9,3);
            var top=new HBoxContainer();top.AddChild(new ItemIcon{Item=recipe.Output,CustomMinimumSize=new Vector2(60,59)});
            var count=Text($"×{recipe.Quantity}",15,Gold);count.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;count.HorizontalAlignment=HorizontalAlignment.Right;top.AddChild(count);stack.AddChild(top);
            var name=Text(recipe.Name,15,Cream);name.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;stack.AddChild(name);
            stack.AddChild(Text($"{recipe.Duration*state.Settings.CraftDurationMultiplier:0.#} sec  ·  {StationName(recipe.Station)}",11,Muted));
            stack.AddChild(Text(state.CraftJob?.RecipeId==id?"Work in progress":!stationAvailable?"Requires a station":afford?"Materials ready":"Gather ingredients",11,!stationAvailable?Muted:afford?new Color("b0c28b"):new Color("d6a17d")));
            _collectionGrid.AddChild(b);
        }
        RenderRecipeDetails();
        RenderCraftQueue(state,catalog);
        _collectionSummary.Text=$"{StationName(_station)}     ·     Double-click a recipe to queue it     ·     Ingredients are taken one batch at a time";
    }

    private void SelectRecipe(string id)
    {
        if(_selectedEntry!=id){_craftQuantity=1;_craftUntil=false;}
        _selectedEntry=id;
        foreach(var card in _recipeCards)
            card.Value.AddThemeStyleboxOverride("normal",Box(card.Key==id?new Color("3d4933"):new Color("202b23"),card.Key==id?Gold:new Color("46533e")));
        ClearChildren(_details);_craftAction=null;RenderRecipeDetails();
    }
    private void EnqueueRecipe(string id)
    {
        if(_menuCatalog==null||!_menuCatalog.Recipes.TryGetValue(id,out var recipe))return;
        if(recipe.Station!="hand"&&recipe.Station!=_station){Notify("Visit "+StationName(recipe.Station)+" to queue this recipe.");return;}
        Command?.Invoke($"craft:{id}:{_craftQuantity}:{(_craftUntil?"true":"false")}");
    }
    private int AffordableBatches(RecipeDefinition recipe)=>recipe.Costs.Count==0?0:recipe.Costs.Min(x=>_menuState!.Player.Inventory.GetValueOrDefault(x.Key)/x.Value);
    private void RenderRecipeDetails()
    {
        if(_menuState==null||_menuCatalog==null)return;
        var state=_menuState;var catalog=_menuCatalog;
        if(_selectedEntry!=""&&catalog.Recipes.TryGetValue(_selectedEntry,out var recipe))
        {
            DetailHeader(recipe.Output,recipe.Name,ItemCategory(recipe.Output));
            DetailPair("Per batch",$"{recipe.Quantity} made · {recipe.Duration*state.Settings.CraftDurationMultiplier:0.#} seconds");
            DetailPair("Made at",StationName(recipe.Station));
            AddIngredients(recipe.Costs);
            bool stationAvailable=recipe.Station=="hand"||recipe.Station==_station;
            var quantityRow=new HBoxContainer();quantityRow.AddThemeConstantOverride("separation",8);
            var quantityLabel=Text("Batches",12,Muted);quantityLabel.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;quantityRow.AddChild(quantityLabel);
            var quantity=new SpinBox{Name="CraftQuantity",MinValue=1,MaxValue=999,Step=1,Value=_craftQuantity,Editable=!_craftUntil,CustomMinimumSize=new Vector2(87,34)};
            quantity.ValueChanged+=value=>_craftQuantity=(int)value;quantityRow.AddChild(quantity);
            var max=ActionButton("Max",()=>{_craftQuantity=Math.Clamp(AffordableBatches(recipe),1,999);quantity.Value=_craftQuantity;});
            max.CustomMinimumSize=new Vector2(52,34);max.AddThemeFontSizeOverride("font_size",12);max.Disabled=_craftUntil||AffordableBatches(recipe)<1;max.TooltipText="Use the supplies currently in your satchel.";quantityRow.AddChild(max);_details.AddChild(quantityRow);
            var until=new CheckButton{Name="CraftUntilMaterials",Text="Until materials run out",ButtonPressed=_craftUntil};until.AddThemeFontSizeOverride("font_size",12);
            until.TooltipText="Make one batch at a time until another batch cannot be made, then continue with the next recipe.";
            until.Toggled+=enabled=>{_craftUntil=enabled;quantity.Editable=!enabled;max.Disabled=enabled||AffordableBatches(recipe)<1;};_details.AddChild(until);
            _craftAction=ActionButton(!stationAvailable?"Visit "+StationName(recipe.Station):"Add to work queue",()=>EnqueueRecipe(recipe.Id),true);
            _craftAction.Name="CraftEnqueue";_craftAction.Disabled=!stationAvailable;_details.AddChild(_craftAction);
            if(!CanAfford(recipe.Costs))DetailText("Numbered orders wait for ingredients. 'Until materials run out' finishes when supplies are exhausted.",11);
        }
        else DetailText("No recipes in this category.");
    }

    private void RenderBuildings(WorldState state,ContentCatalog catalog)
    {
        _collectionGrid.Columns=3;
        var buildings=BuildCatalogue(catalog).Where(x=>_category=="All"||Category(x.Category)==_category).ToArray();
        if(!buildings.Any(x=>x.Key==_selectedEntry))_selectedEntry=buildings.FirstOrDefault()?.Key??"";
        foreach(var building in buildings)
        {
            string key=building.Key;var b=EntryButton(key,new Vector2(210,145),()=>{_selectedEntry=key;RenderCollection();});
            b.Name=(building.Tool==null?"Building_":"BuildingTool_")+building.Id;
            var stack=Stack(b,12,9,3);stack.AddChild(new ItemIcon{Item=building.Id,CustomMinimumSize=new Vector2(0,65)});
            var name=Text(building.Name,15,Cream);name.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;stack.AddChild(name);
            bool available=CanAfford(building.Costs);
            stack.AddChild(Text(building.Tool!=null?$"{building.Tool.Variants.Count} shapes · adjust while placing":available?"Materials ready":"Materials needed",11,building.Tool!=null?Gold:available?new Color("b0c28b"):new Color("d6a17d")));
            _collectionGrid.AddChild(b);
        }
        if(_selectedEntry!="")
        {
            var def=buildings.First(x=>x.Key==_selectedEntry);DetailHeader(def.Id,def.Name,Category(def.Category));
            DetailText(string.IsNullOrWhiteSpace(def.Description)?BuildDescription(def.Id,def.Placement):def.Description);
            if(def.Tool!=null)
            {
                _details.AddChild(Text("ONE TOOL · MANY SHAPES",11,Gold));
                DetailText(string.Join(" · ",def.Tool.Variants.Select(x=>x.Name)),12);
            }
            AddIngredients(def.Costs,def.Tool!=null?"DEFAULT SHAPE · MATERIALS":"INGREDIENTS");
            _details.AddChild(Text("ADJUST WHILE PLACING",11,Gold));
            DetailText(def.Tool!=null?"T changes shape · Y changes pitch\nN changes material · G mirrors the piece\nShift + T / Y cycles backwards.":"V cycles Object / Grid / Free snapping. R rotates; Shift + R reverses. Alt + scroll adjusts height; H returns to your floor.",12);
            if(def.Id=="floor")DetailText("Foundations become upper floors when raised. The live panel shows the materials for your current choice.",12);
            var place=ActionButton(def.Tool!=null?"Select "+def.Name+" tool":"Choose & place",()=>Command?.Invoke((def.Tool!=null?"buildtool:":"build:")+def.Id),true);
            _details.AddChild(place);
            DetailText("Move the pointer to position. Green means it is ready to place. Your current shape, pitch and material remain visible beside the world.",12);
        }
        _details.AddChild(new Control{SizeFlagsVertical=Control.SizeFlags.ExpandFill,MouseFilter=Control.MouseFilterEnum.Ignore});
        _details.AddChild(ActionButton("Dismantle a piece",()=>Command?.Invoke("reclaim")));
        _collectionSummary.Text="HAMMER EQUIPPED     ·     Build in layers, furnish your rooms, make it yours     ·     B closes the catalogue";
    }

    private sealed record BuildCatalogueEntry(string Key,string Id,string Name,string Category,string Description,string Placement,Dictionary<string,int> Costs,BuildingToolDefinition? Tool);
    private static IEnumerable<BuildCatalogueEntry> BuildCatalogue(ContentCatalog catalog)
    {
        var covered=catalog.BuildingTools.Values.SelectMany(t=>t.Variants.SelectMany(v=>new[]{v.Kind,v.UpperKind}).Concat(t.LegacyKinds)).Where(x=>!string.IsNullOrWhiteSpace(x)).ToHashSet();
        foreach(var tool in catalog.BuildingTools.Values)
        {
            var def=BuildingVariants.Resolve(catalog,BuildingVariants.Default(tool),0);
            yield return new("tool:"+tool.Id,tool.Id,tool.Name,tool.Category,tool.Description,def.Placement,def.Costs,tool);
        }
        foreach(var def in catalog.Buildings.Values.Where(x=>!covered.Contains(x.Id)&&!x.Id.StartsWith("upper_floor",StringComparison.Ordinal)))
            yield return new(def.Id,def.Id,def.Name,def.Category,def.Description,def.Placement,def.Costs,null);
    }

    private Button EntryButton(string id,Vector2 size,Action selected)
    {
        var button=new Button{CustomMinimumSize=size,SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,FocusMode=Control.FocusModeEnum.All,ClipContents=true};
        button.AddThemeStyleboxOverride("normal",Box(id==_selectedEntry?new Color("3d4933"):new Color("202b23"),id==_selectedEntry?Gold:new Color("46533e")));
        button.Pressed+=selected;return button;
    }
    private void DetailHeader(string icon,string title,string eyebrow)
    {
        var top=new HBoxContainer();top.AddThemeConstantOverride("separation",12);
        top.AddChild(new ItemIcon{Item=icon,CustomMinimumSize=new Vector2(72,72)});
        var titleStack=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill,SizeFlagsVertical=Control.SizeFlags.ShrinkCenter};
        titleStack.AddChild(Text(eyebrow.ToUpperInvariant(),10,Gold));
        var name=Text(title,24,Cream,true);name.AutowrapMode=TextServer.AutowrapMode.WordSmart;titleStack.AddChild(name);top.AddChild(titleStack);_details.AddChild(top);
    }
    private void DetailText(string value,int size=13)
    {var text=Text(value,size,Muted);text.AutowrapMode=TextServer.AutowrapMode.WordSmart;text.AddThemeConstantOverride("line_spacing",3);_details.AddChild(text);}
    private void DetailPair(string name,string value)
    {
        var row=new HBoxContainer();var key=Text(name,12,Muted);key.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;row.AddChild(key);
        var label=Text(value,12,Cream);label.HorizontalAlignment=HorizontalAlignment.Right;row.AddChild(label);_details.AddChild(row);
    }
    private void AddIngredients(Dictionary<string,int> costs,string heading="INGREDIENTS")
    {
        _details.AddChild(Text(heading,11,Gold));
        foreach(var cost in costs)
        {
            int owned=_menuState!.Player.Inventory.GetValueOrDefault(cost.Key);var row=new HBoxContainer();row.AddThemeConstantOverride("separation",8);
            row.AddChild(new ItemIcon{Item=cost.Key,CustomMinimumSize=new Vector2(28,28)});
            var name=Text(_menuCatalog!.Items.GetValueOrDefault(cost.Key)?.Name??Category(cost.Key),13,Cream);name.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;row.AddChild(name);
            var quantity=Text($"{owned} / {cost.Value}",13,owned>=cost.Value?new Color("b0c28b"):new Color("ddaa86"));quantity.TooltipText="Carried / required";row.AddChild(quantity);_details.AddChild(row);
        }
    }
    private void RenderCraftQueue(WorldState state,ContentCatalog catalog)
    {
        _workLabel=null;_workProgress=null;
        ClearChildren(_queueContents);_queuePanel.Visible=state.CraftQueue.Count>0||state.CraftJob!=null;
        if(!_queuePanel.Visible)return;
        var header=new HBoxContainer();header.AddThemeConstantOverride("separation",12);
        header.AddChild(Text("WORK QUEUE",11,Gold));
        _workLabel=Text(_queueStatus,12,Muted);_workLabel.SizeFlagsHorizontal=Control.SizeFlags.ExpandFill;header.AddChild(_workLabel);
        _workProgress=new ProgressBar{MinValue=0,MaxValue=100,ShowPercentage=false,CustomMinimumSize=new Vector2(160,8),SizeFlagsVertical=Control.SizeFlags.ShrinkCenter,MouseFilter=Control.MouseFilterEnum.Ignore};
        var fill=Box(Gold,Colors.Transparent);fill.ContentMarginTop=fill.ContentMarginBottom=0;
        var background=Box(new Color("111c16"),Colors.Transparent);background.ContentMarginTop=background.ContentMarginBottom=0;
        _workProgress.AddThemeStyleboxOverride("fill",fill);_workProgress.AddThemeStyleboxOverride("background",background);header.AddChild(_workProgress);
        var clear=ActionButton("Clear queue",()=>Command?.Invoke("clearqueue"));clear.CustomMinimumSize=new Vector2(108,30);clear.AddThemeFontSizeOverride("font_size",12);header.AddChild(clear);_queueContents.AddChild(header);
        var scroll=new ScrollContainer{VerticalScrollMode=ScrollContainer.ScrollMode.Disabled,SizeFlagsVertical=Control.SizeFlags.ExpandFill};
        var entries=new HBoxContainer();entries.AddThemeConstantOverride("separation",8);scroll.AddChild(entries);_queueContents.AddChild(scroll);
        int position=0;
        foreach(var entry in state.CraftQueue)
        {
            bool active=state.CraftJob?.QueueEntryId==entry.Id;string id=entry.Id;
            string name=catalog.Recipes.GetValueOrDefault(entry.RecipeId)?.Name??entry.RecipeId;
            string output=catalog.Recipes.GetValueOrDefault(entry.RecipeId)?.Output??"wood";
            var card=Card(256,56);card.AddThemeStyleboxOverride("panel",Box(active?new Color("34402c"):new Color("202b23"),active?Gold:new Color("46533e")));
            var row=new HBoxContainer();row.AddThemeConstantOverride("separation",7);var contents=Stack(card,9,6,0);contents.AddChild(row);
            row.AddChild(new ItemIcon{Item=output,CustomMinimumSize=new Vector2(36,36)});
            var info=new VBoxContainer{SizeFlagsHorizontal=Control.SizeFlags.ExpandFill};info.AddThemeConstantOverride("separation",1);
            var title=Text($"{++position}. {name}",12,Cream);title.TextOverrunBehavior=TextServer.OverrunBehavior.TrimEllipsis;info.AddChild(title);
            int remaining=entry.RemainingCount+(active?1:0);
            string count=entry.UntilMaterials?"Until materials run out":$"{remaining} {(remaining==1?"batch":"batches")} left";
            info.AddChild(Text(count,10,Muted));row.AddChild(info);
            var remove=ActionButton("×",()=>Command?.Invoke(active?"cancelcraft":"removequeue:"+id));remove.CustomMinimumSize=new Vector2(29,30);remove.AddThemeFontSizeOverride("font_size",17);remove.TooltipText=active?"Cancel this order and return the active batch's ingredients":"Remove this order";row.AddChild(remove);entries.AddChild(card);
        }
        var job=state.CraftJob;
        if(job!=null)UpdateCraftProgress(catalog.Recipes.GetValueOrDefault(job.RecipeId)?.Name??"Crafting",job.Remaining,job.Duration);
        else _workProgress.Hide();
    }
    public void RefreshCraftQueue(WorldState state,ContentCatalog catalog)
    {if(IsWorkshopOpen){_menuState=state;_menuCatalog=catalog;RenderCollection();}}
    public void SetCraftQueueStatus(string status)
    {
        _queueStatus=status;
        if(_menuState?.CraftJob==null&&_workLabel!=null&&GodotObject.IsInstanceValid(_workLabel))_workLabel.Text=status;
    }
    public void UpdateCraftProgress(string name,float remaining,float total)
    {
        if(_workLabel!=null&&GodotObject.IsInstanceValid(_workLabel))_workLabel.Text=$"{name}  ·  {MathF.Ceiling(remaining)} sec remaining";
        if(_workProgress!=null&&GodotObject.IsInstanceValid(_workProgress))_workProgress.Value=total>0?Math.Clamp((1-remaining/total)*100,0,100):0;
    }
    private bool CanAfford(Dictionary<string,int> costs)=>costs.All(x=>_menuState!.Player.Inventory.GetValueOrDefault(x.Key)>=x.Value);
    private string ItemCategory(string id)=>Category(_menuCatalog!.Items.GetValueOrDefault(id)?.Category??"Materials");
    private static string Category(string value)=>string.IsNullOrWhiteSpace(value)?"Materials":char.ToUpperInvariant(value[0])+value[1..].Replace('_',' ');
    private static string StationName(string station)=>station switch{"hand"=>"Handcraft","carpenter"=>"Carpenter's bench","forge"=>"Forge","kiln"=>"Kiln","cooking"=>"Cooking station","oven"=>"Oven",_=>Category(station)};
    private static string DefaultDescription(string id)=>id switch
    {"wood"=>"Fresh timber from the valley. A beginning for tools, shelter and furniture.","plank"=>"Straight, sawn boards for sturdy joinery and a home of your own.","stone"=>"Rough stone, ready to be shaped into a useful edge or laid into a foundation.","berries"=>"Sweet wild berries. A little nourishment for the road.","seeds"=>"Small beginnings. Sow them in prepared soil and give them time.","meal"=>"A filling meal to restore you after a day's work.",_=>"Gathered or made in the valley. Keep it for your next project, or trade it in Harthwood."};
    private static string BuildDescription(string id,string placement)=>placement switch
    {"floor"=>"A sturdy deck. Join pieces together to fit the shape of your home.","edge"=>"Frame the edges of a floor. Rotate to choose which side to build along.","roof"=>"Finish the room above. Combine roof sections to follow your home's outline.",_=>id.Contains("stairs")?"Connect the floors of your home. Leave room at both ends to walk up and down.":"Choose a spot in your home or its surroundings. Rotate until it feels right."};
}
