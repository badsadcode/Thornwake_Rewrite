using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace Thornwake.Core;

public sealed partial class Simulation
{
    public const double CropGrowthSeconds = 60;
    public WorldState State { get; }
    public ContentCatalog Catalog { get; }
    private readonly Dictionary<string, ResourceSpawn> resources;

    public Simulation(WorldState state, ContentCatalog catalog)
    {
        LandscapeGenerator.Validate(state);
        State = state;
        Catalog = catalog;
        resources = WorldGenerator.Generate(state).ToDictionary(x => x.Id);
        if(state.Player.Inventory.Keys.Any(id=>!catalog.Items.ContainsKey(id)) || state.Structures.Any(s=>!catalog.Buildings.ContainsKey(s.Kind)||s.PaidCosts.Keys.Any(id=>!catalog.Items.ContainsKey(id))) || state.RemovedResources.Any(id=>!resources.ContainsKey(id)))
            throw new InvalidDataException("This world refers to content that is not available in this build.");
        if(state.CraftJob is {} job&&(!catalog.Recipes.TryGetValue(job.RecipeId,out var recipe)||job.PaidCosts.Keys.Any(id=>!catalog.Items.ContainsKey(id))||recipe.Station!="hand"&&Workstations.Find(state,job.StationId)?.Kind!=recipe.Station))throw new InvalidDataException("This world refers to an unavailable crafting task or station.");
        ValidateCraftQueue();
        foreach(var piece in state.Structures)
        {
            if(piece.Selection!=null&&BuildingVariants.Resolve(catalog,piece.Selection,piece.Level).Id!=piece.Kind)throw new InvalidDataException("A saved building variant does not match its piece.");
            if(piece.Selection!=null&&!catalog.BuildingTools[piece.Selection.DefinitionId].AllowedRotations.Any(a=>MathF.Abs(MathF.IEEERemainder(piece.Rotation-a*MathF.PI/180,MathF.Tau))<.001f))throw new InvalidDataException("A saved building variant uses an unsupported rotation.");
            if(piece.Kind=="roof_module"&&piece.Selection==null)throw new InvalidDataException("A roof module is missing its shape and pitch.");
        }
        RoofGeometry.ValidateConnections(state,catalog);
        HotbarRules.Validate(state,catalog);
        if(state.CompletedQuests==null||state.CompletedQuests.Any(id=>!QuestTracker.Definitions.Any(q=>q.Id==id)))throw new InvalidDataException("A saved quest is unavailable.");
        QuestTracker.Refresh(state);
        TerrainGrid.Validate(state);
        Vegetation.Validate(state);
    }

    public void Tick(double seconds)
    {
        if (!double.IsFinite(seconds) || seconds < 0 || seconds > 86400) throw new ArgumentOutOfRangeException(nameof(seconds));
        double previous = State.Time;
        State.Time += seconds;
        State.CalendarMinutes+=seconds*24/State.Settings.DayLengthMinutes;
        TickVegetation(seconds);
        TickCooldowns((float)seconds);
        State.Player.Stamina = Math.Clamp(State.Player.Stamina + (float)seconds * 9*State.Settings.StaminaRegenMultiplier, 0, 100);
        // Fixed boundaries make economy independent of render frame rate, including after loading.
        for (long tick = (long)(previous / 15) + 1; tick <= (long)(State.Time / 15); tick++)
        {
            var town = State.Settlement;
            town.Food = Math.Clamp(town.Food - 1, 0, 200);
            town.Timber = Math.Clamp(town.Timber + 1, 0, 200);
            town.Prosperity = Math.Clamp(town.Prosperity + (town.Food == 0 ? -2 : 1), 0, 100);
            if (tick % 6 == 0)
            {
                if (State.EnemyDefeated || tick % 12 == 0)
                {
                    town.Food = Math.Min(200, town.Food + 12);
                    town.Timber = Math.Max(0, town.Timber - 4);
                    town.CaravanArrivals++;
                    Journal("A caravan reached Harthwood: food delivered, timber traded.");
                }
                else Journal("The caravan waits beyond the woods. A bandit stalks the road.");
            }
        }
    }

    public ActionResult Gather(string resourceId, string kind)
    {
        var plant=State.Vegetation.FirstOrDefault(p=>p.Id==resourceId);
        if(plant!=null)return plant.Kind==kind?HarvestVegetation(plant):Fail("That plant has a different kind.");
        if (!resources.TryGetValue(resourceId, out var spawn) || spawn.Kind != kind || !Catalog.Resources.TryGetValue(kind, out var definition))
            return Fail("This resource cannot be gathered.");
        if (State.RemovedResources.Contains(resourceId)) return Fail("This resource has already been gathered.");
        State.RemovedResources.Add(resourceId);
        foreach (var reward in definition.Rewards) Add(reward.Key, ResourceYield(reward.Value));
        if(kind=="tree"&&Woodland.Natural(spawn).StartsWith("Pine_",StringComparison.Ordinal))Add("pine_cone",ResourceYield(1));
        if(kind=="berries")State.Vegetation.Add(new(){Id=spawn.Id,Kind="berries",X=spawn.X,Z=spawn.Z,Growth=1,Fruit=0});
        Improve(definition.Skill);
        return Done($"Gathered {string.Join(", ", definition.Rewards.Select(x => $"{ResourceYield(x.Value)} {Catalog.Items[x.Key].Name.ToLowerInvariant()}"))}.");
    }

    public BuildPlan PreviewBuild(string kind,float x,float z,float rotation=0,int level=0,BuildSnapMode snapMode=BuildSnapMode.Object,BuildSelection? selection=null)
    {
        var plan=Construction.Plan(State,Catalog,resources.Values,kind,x,z,rotation,level,snapMode,selection);
        if(plan.Valid)
        {
            var definition=plan.Selection==null?Catalog.Buildings[kind]:BuildingVariants.Resolve(Catalog,plan.Selection,level);
            var candidate=new StructureState{Kind=kind,X=plan.X,Z=plan.Z,Rotation=plan.Rotation,Level=level};
            if(State.Vegetation.Any(p=>Construction.Occupies(candidate,definition,p.X,p.Z,p.Kind=="tree"?.7f:.5f)))return plan with{Valid=false,Message="Clear this growing plant or stump before building here."};
        }
        return plan;
    }
    public ActionResult Build(string kind, float x, float z, float rotation = 0,int level=0,BuildSnapMode snapMode=BuildSnapMode.Object,BuildSelection? selection=null)
    {
        var plan=PreviewBuild(kind,x,z,rotation,level,snapMode,selection);
        if(!plan.Valid)return Fail(plan.Message);
        var building=plan.Selection==null?Catalog.Buildings[plan.Kind]:BuildingVariants.Resolve(Catalog,plan.Selection,level);var costs=plan.Costs??building.Costs;Pay(costs);
        State.Structures.Add(new StructureState { Id=Guid.NewGuid().ToString("N"),Kind=plan.Kind,X=plan.X,Z=plan.Z,Rotation=plan.Rotation,Elevation=plan.Elevation,SupportId=plan.SupportId,RoofConnectionId=plan.RoofConnectionId,Selection=plan.Selection?.Copy(),Level=plan.Level,Modular=true,PaidCosts=new Dictionary<string,int>(costs) });
        Improve("carpentry");return Done($"Built {building.Name.ToLowerInvariant()}.");
    }
    public Dictionary<string,int> ReclaimYield(StructureState structure)
    {
        var paid=structure.PaidCosts.Count>0?structure.PaidCosts:Catalog.Buildings[structure.Kind].Costs;
        if(Catalog.Buildings[structure.Kind].Category=="Decor")return new(paid);
        return paid.Where(c=>c.Value>=2).ToDictionary(c=>c.Key,c=>c.Value/2);
    }
    public ActionResult Dismantle(string id)
    {
        var piece=State.Structures.Find(s=>s.Id==id);
        if(piece==null)return Fail("That piece has already been removed.");
        if(Construction.Distance(piece.X,piece.Z,State.Player.X,State.Player.Z)>Construction.Reach)return Fail("Move closer to dismantle this piece.");
        if(State.CraftJob?.StationId==id||State.CraftQueue.Any(q=>q.StationId==id))return Fail("Finish or cancel queued work at this station first.");
        if(!Construction.CanRemove(State,piece,out string reason))return Fail(reason);
        var refund=ReclaimYield(piece);State.Structures.Remove(piece);
        foreach(var item in refund)Add(item.Key,item.Value);
        return Done($"Dismantled {Catalog.Buildings[piece.Kind].Name.ToLowerInvariant()}. Reclaimed "+(refund.Count>0?string.Join(", ",refund.Select(c=>$"{c.Value} {Catalog.Items[c.Key].Name.ToLowerInvariant()}")):"no materials")+".");
    }
    public ActionResult ToggleDoor(string id)
    {
        var door=State.Structures.Find(s=>s.Id==id&&s.Kind=="door");
        if(door==null)return Fail("There is no door here.");
        if(Construction.Distance(door.X,door.Z,State.Player.X,State.Player.Z)>2.7f)return Fail("Move closer to the door.");
        if(Math.Abs((State.Player.Y??Construction.WalkHeight(State,State.Player.X,State.Player.Z))-Construction.Base(State,door))>1.6f)return Fail("Use the door on your current floor.");
        if(door.DoorOpen&&Construction.Occupies(door,Catalog.Buildings[door.Kind],State.Player.X,State.Player.Z,.35f))return Fail("Step out of the doorway before closing it.");
        door.DoorOpen=!door.DoorOpen;return Done(door.DoorOpen?"Opened your door.":"Closed your door.");
    }

    public ActionResult ModifyTerrain(float x, float z, float delta)
    {
        if (!InBounds(x, z, 3) || !float.IsFinite(delta) || Math.Abs(delta) > 1) return Fail("The ground cannot be changed here.");
        if (Math.Abs(x - 27) < 4) return Fail("The riverbed cannot be cultivated.");
        if(State.Structures.Any(s=>Construction.Occupies(s,Catalog.Buildings[s.Kind],x,z,3)))return Fail("Keep soil work clear of existing structures.");
        if(State.Vegetation.Any(p=>Construction.Distance(x,z,p.X,p.Z)<3))return Fail("Keep soil work clear of growing plants and roots.");
        if (State.Player.Stamina < 8) return Fail("Catch your breath before working the soil.");
        float total = State.TerrainEdits.Where(e => DistanceSquared(x, z, e.X, e.Z) < 4).Sum(e => e.Delta);
        if (Math.Abs(total + delta) > 2.5f) return Fail("This ground has reached its shaping limit.");
        State.Player.Stamina -= 8;
        State.TerrainEdits.Add(new TerrainEdit { X = x, Z = z, Radius = 3, Delta = delta });
        Improve("farming");
        return Done(delta > 0 ? "Raised the ground." : "Prepared the soil for planting.");
    }

    public ActionResult Plant(float x, float z)
    {
        if (!InBounds(x, z, 1) || Math.Abs(x - 27) < 4) return Fail("Choose dry ground within the valley.");
        if(State.Structures.Any(s=>Construction.Occupies(s,Catalog.Buildings[s.Kind],x,z,.5f)))return Fail("Leave room around your structures.");
        if(State.Vegetation.Any(p=>Construction.Distance(x,z,p.X,p.Z)<2))return Fail("Leave room around growing trees and bushes.");
        if (!State.TerrainEdits.Any(e => e.Delta <= 0 && DistanceSquared(x, z, e.X, e.Z) < e.Radius * e.Radius))
            return Fail("Prepare the soil with your hoe first.");
        if (State.Crops.Any(c => DistanceSquared(x, z, c.X, c.Z) < 2.25f)) return Fail("Leave room for the neighbouring crop.");
        if (Count("seeds") < 1) return Fail("You need a berry seed.");
        Add("seeds", -1);
        State.Crops.Add(new CropState { Id = Guid.NewGuid().ToString("N"), X = x, Z = z, PlantedAt = State.Time });
        Improve("farming");
        return Done($"Planted berry seeds. Harvest after {State.Settings.CropGrowthMinutes:0.#} active minutes.");
    }

    public ActionResult Harvest(string id)
    {
        var crop = State.Crops.Find(c => c.Id == id);
        if (crop is null) return Fail("There is no crop here.");
        if (State.Time - crop.PlantedAt < CropDuration) return Fail("This crop needs more time to grow.");
        State.Crops.Remove(crop);
        Add("berries", 4);
        Add("seeds", 2);
        Improve("farming");
        return Done("Harvested 4 berries and 2 seeds.");
    }

    public int Price(string item, bool buy)
    {
        if (!Catalog.Items.TryGetValue(item, out var definition) || item == "coins") return 0;
        int shortage = item is "berries" or "meal" && State.Settlement.Food < 12 ? 1 : 0;
        return (buy ? definition.BuyPrice : definition.SellPrice) + shortage;
    }

    public ActionResult Trade(string item, bool buy)
    {
        if (!Catalog.Items.TryGetValue(item, out var definition) || item == "coins") return Fail("That item is not traded here.");
        int price = Price(item, buy);
        if (price <= 0) return Fail("That item is not traded here.");
        bool food = item is "berries" or "meal";
        if (buy)
        {
            if (Count("coins") < price) return Fail("You need more crowns.");
            if ((food && State.Settlement.Food == 0) || (item == "wood" && State.Settlement.Timber == 0))
                return Fail("The market has run out. A caravan will bring more.");
            Add("coins", -price); Add(item, 1);
            if (food) State.Settlement.Food--;
            if (item == "wood") State.Settlement.Timber--;
        }
        else
        {
            if (Count(item) < 1) return Fail($"You have no {definition.Name.ToLowerInvariant()} to sell.");
            if ((food && State.Settlement.Food >= 200) || (item == "wood" && State.Settlement.Timber >= 200))
                return Fail("The market storehouse is full.");
            Add(item, -1); Add("coins", price);
            if (food) State.Settlement.Food++;
            if (item == "wood") State.Settlement.Timber++;
            State.Settlement.Prosperity = Math.Min(100, State.Settlement.Prosperity + 1);
        }
        Improve("trading");
        return Done($"{(buy ? "Bought" : "Sold")} {definition.Name.ToLowerInvariant()} for {price} crowns.");
    }

    public ActionResult Eat()
    {
        string item = Count("meal") > 0 ? "meal" : Count("bread")>0?"bread":"berries";
        if (Count(item) == 0) return Fail("Find berries or prepare a meal first.");
        return ConsumeItem(item);
    }

    public ActionResult DefeatEnemy()
    {
        if (State.EnemyDefeated) return Fail("The road is already safe.");
        State.EnemyDefeated = true;
        Add("coins", 15); Add("hide", 1);
        Improve("combat");
        State.Settlement.Prosperity = Math.Min(100, State.Settlement.Prosperity + 8);
        return Done("The road is safe. Caravans can travel freely. Recovered a hide and 15 crowns.");
    }

    public int Count(string item) => State.Player.Inventory.GetValueOrDefault(item);
    private void Add(string item, int amount) => State.Player.Inventory[item] = Count(item) + amount;
    private void Improve(string skill) => State.Player.Skills[skill] = State.Player.Skills.GetValueOrDefault(skill) + 1;
    private bool CanPay(Dictionary<string, int> costs) => costs.All(c => Count(c.Key) >= c.Value);
    private void Pay(Dictionary<string, int> costs) { foreach (var cost in costs) Add(cost.Key, -cost.Value); }
    private ActionResult Missing(Dictionary<string, int> costs) => Fail("Need " + string.Join(", ", costs.Where(c => Count(c.Key) < c.Value).Select(c => $"{c.Value - Count(c.Key)} more {Catalog.Items[c.Key].Name.ToLowerInvariant()}")) + ".");
    private static ActionResult Fail(string message) => new(false, message);
    private ActionResult Done(string message)
    {
        if(State.Player.EquippedItem!=""&&Count(State.Player.EquippedItem)<=0)State.Player.EquippedItem="";
        QuestTracker.Refresh(State);Journal(message);return new(true,message);
    }
    private void Journal(string message)
    {
        State.Journal.Add(message);
        if (State.Journal.Count > 80) State.Journal.RemoveRange(0, State.Journal.Count - 80);
    }
    private static bool InBounds(float x, float z, float margin) => float.IsFinite(x) && float.IsFinite(z) && Math.Abs(x) <= WorldGenerator.WorldLimit - margin && Math.Abs(z) <= WorldGenerator.WorldLimit - margin;
    private static float DistanceSquared(float x, float z, float otherX, float otherZ) => (x - otherX) * (x - otherX) + (z - otherZ) * (z - otherZ);
}

