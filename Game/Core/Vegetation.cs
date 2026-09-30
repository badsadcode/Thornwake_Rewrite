using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Thornwake.Core;

public sealed class VegetationState
{
    public string Id {get;set;}="";
    public string Kind {get;set;}="tree";
    public float X {get;set;}
    public float Z {get;set;}
    public double Growth {get;set;}
    public double Fruit {get;set;}=1;
    public bool IsStump {get;set;}
}

public static class Vegetation
{
    public static void Validate(WorldState state)
    {
        if(state.Settings==null)throw new InvalidDataException("Missing world sandbox settings.");state.Settings.Validate();
        if(!double.IsFinite(state.CalendarMinutes)||state.CalendarMinutes<0||state.CalendarMinutes>1e12)throw new InvalidDataException("Invalid world calendar.");
        if(state.Vegetation==null||state.RemovedStumps==null||state.Vegetation.Count>10000||state.RemovedStumps.Count>10000)throw new InvalidDataException("Invalid vegetation collection.");
        var wild=WorldGenerator.Generate(state).ToDictionary(r=>r.Id);
        foreach(var plant in state.Vegetation)
        {
            if(plant==null||string.IsNullOrWhiteSpace(plant.Id)||plant.Id.Length>200||plant.Kind is not ("tree" or "berries")||!float.IsFinite(plant.X)||!float.IsFinite(plant.Z)||MathF.Abs(plant.X)>55||MathF.Abs(plant.Z)>55||!double.IsFinite(plant.Growth)||plant.Growth<0||plant.Growth>1||!double.IsFinite(plant.Fruit)||plant.Fruit<0||plant.Fruit>1||plant.IsStump&&plant.Kind!="tree")throw new InvalidDataException("Invalid growing plant.");
            if(wild.TryGetValue(plant.Id,out var original)&&(original.Kind!="berries"||plant.Kind!="berries"||!state.RemovedResources.Contains(plant.Id)||MathF.Abs(original.X-plant.X)>.001f||MathF.Abs(original.Z-plant.Z)>.001f))throw new InvalidDataException("A growing plant conflicts with a natural resource.");
        }
        if(state.Vegetation.Select(p=>p.Id).Distinct().Count()!=state.Vegetation.Count)throw new InvalidDataException("Duplicate plant identity.");
        if(state.RemovedStumps.Any(id=>!wild.TryGetValue(id,out var tree)||tree.Kind!="tree"||!state.RemovedResources.Contains(id)))throw new InvalidDataException("A removed stump has no felled tree.");
    }
    public static string Status(WorldState state,VegetationState plant)
    {
        if(plant.IsStump)return "Tree stump";
        bool growing=plant.Growth<1;double remaining=(growing?1-plant.Growth:1-plant.Fruit)*(growing?(plant.Kind=="tree"?state.Settings.TreeGrowthHours:state.Settings.BushGrowthHours):state.Settings.BushRegrowHours);
        string name=plant.Kind=="tree"?"Pine":"Berry bush";
        if(!growing&&(plant.Kind=="tree"||plant.Fruit>=1))return name+" · ready";
        if(state.Settings.FreezeVegetation)return name+" · growth paused in sandbox settings";
        string time=remaining>=1?$"{remaining:0.0} hours":$"{Math.Max(1,Math.Ceiling(remaining*60)):0} minutes";
        return name+$" · {(growing?plant.Growth*100:plant.Fruit*100):0}% · {time} to "+(growing?"mature":"new berries");
    }
}

public sealed partial class Simulation
{
    private void TickVegetation(double seconds)
    {
        if(State.Settings.FreezeVegetation)return;
        foreach(var plant in State.Vegetation)
        {
            if(plant.IsStump)continue;
            double left=seconds;
            if(plant.Growth<1)
            {
                double duration=(plant.Kind=="tree"?State.Settings.TreeGrowthHours:State.Settings.BushGrowthHours)*3600;
                double needed=(1-plant.Growth)*duration;plant.Growth=Math.Min(1,plant.Growth+left/duration);left=Math.Max(0,left-needed);
            }
            if(plant.Kind=="berries"&&plant.Growth>=1&&plant.Fruit<1)plant.Fruit=Math.Min(1,plant.Fruit+left/(State.Settings.BushRegrowHours*3600));
        }
    }
    public string PlantingProblem(string item,float x,float z)
    {
        if(item is not ("pine_sapling" or "bush_seedling"))return "Choose a tree sapling or bush seedling.";
        if(Count(item)<1)return "You need a seedling in your satchel.";
        if(!InBounds(x,z,5)||Math.Abs(x)<3||Math.Abs(z)<3||Math.Abs(x-27)<6)return "Choose open ground away from roads and water.";
        if(Construction.Distance(x,z,State.Player.X,State.Player.Z)>5.5f)return "Move closer to plant.";
        if(Construction.Distance(x,z,State.Player.X,State.Player.Z)<.8f)return "Step clear of the seedling before planting.";
        if(Construction.VillageLots.Any(o=>Construction.Distance(x,z,o.X,o.Z)<o.Radius+2)||Workstations.Village.Any(s=>Construction.Distance(x,z,s.X,s.Z)<3)||x>14&&x<25&&z>4&&z<17)return "Leave room around the village and its fields.";
        if(State.Structures.Any(s=>Construction.Occupies(s,Catalog.Buildings[s.Kind],x,z,1.5f)))return "Leave room for the plant to grow beside your buildings.";
        if(State.Crops.Any(c=>Construction.Distance(x,z,c.X,c.Z)<2))return "Leave room around your crops.";
        float spacing=State.Settings.PlantSpacing;
        if(resources.Values.Any(r=>(!State.RemovedResources.Contains(r.Id)||r.Kind=="tree"&&!State.RemovedStumps.Contains(r.Id))&&Construction.Distance(x,z,r.X,r.Z)<spacing)||State.Vegetation.Any(p=>Construction.Distance(x,z,p.X,p.Z)<spacing))return "Leave more room between plants, rocks and stumps.";
        if(State.Player.Y.HasValue&&MathF.Abs(State.Player.Y.Value-Construction.Ground(State,State.Player.X,State.Player.Z))>1)return "Plant while standing on the ground.";
        return "";
    }
    public ActionResult PlantVegetation(string item,float x,float z)
    {
        string problem=PlantingProblem(item,x,z);if(problem!="")return Fail(problem);
        if(State.Vegetation.Count>=10000)return Fail("This valley has reached its plant limit.");
        Add(item,-1);State.Vegetation.Add(new(){Id="plant-"+Guid.NewGuid().ToString("N"),Kind=item=="pine_sapling"?"tree":"berries",X=x,Z=z});Improve("farming");
        return Done(item=="pine_sapling"?"Planted a pine sapling. Give it time to take root.":"Planted a berry seedling. It will grow and bear fruit over time.");
    }
    private ActionResult HarvestVegetation(VegetationState plant)
    {
        if(Construction.Distance(plant.X,plant.Z,State.Player.X,State.Player.Z)>3.3f)return Fail("Move closer to this plant.");
        if(plant.IsStump)return Fail("Use an axe to remove the stump.");
        if(plant.Growth<1||plant.Kind=="berries"&&plant.Fruit<1)return Fail(Vegetation.Status(State,plant));
        if(plant.Kind=="tree")
        {
            if(HotbarRules.ToolMode(Catalog,State.Player.EquippedItem)!=0)return Fail("Equip an axe to chop this tree.");
            plant.IsStump=true;Add("wood",ResourceYield(4));Add("pine_cone",ResourceYield(1));Improve("forestry");
            return Done("Felled your mature pine. Collected timber and a pine cone.");
        }
        plant.Fruit=0;Add("berries",ResourceYield(3));Add("seeds",ResourceYield(1));Improve("foraging");
        return Done("Gathered berries. The bush will slowly grow new fruit.");
    }
    public ActionResult RemoveStump(string id)
    {
        if(HotbarRules.ToolMode(Catalog,State.Player.EquippedItem)!=0)return Fail("Equip an axe to remove a stump.");
        var planted=State.Vegetation.FirstOrDefault(p=>p.Id==id&&p.IsStump);
        float x,z;
        if(planted!=null){x=planted.X;z=planted.Z;}
        else if(resources.TryGetValue(id,out var tree)&&tree.Kind=="tree"&&State.RemovedResources.Contains(id)&&!State.RemovedStumps.Contains(id)){x=tree.X;z=tree.Z;}
        else return Fail("There is no stump here.");
        if(Construction.Distance(x,z,State.Player.X,State.Player.Z)>3.3f)return Fail("Move closer to the stump.");
        if(planted!=null)State.Vegetation.Remove(planted);else State.RemovedStumps.Add(id);
        Add("wood",State.Settings.StumpWoodYield);Improve("forestry");return Done($"Cleared the stump. Recovered {State.Settings.StumpWoodYield} timber.");
    }
    public ActionResult UprootPlant(string id)
    {
        if(HotbarRules.ToolMode(Catalog,State.Player.EquippedItem) is not (0 or 2))return Fail("Equip an axe or hoe to remove a plant.");
        var plant=State.Vegetation.FirstOrDefault(p=>p.Id==id&&!p.IsStump);
        if(plant==null)return Fail("Choose a planted sapling or berry bush.");
        if(plant.Kind=="tree"&&plant.Growth>=1)return Fail("Chop the mature tree, then clear its stump.");
        if(Construction.Distance(plant.X,plant.Z,State.Player.X,State.Player.Z)>3.3f)return Fail("Move closer to the plant.");
        State.Vegetation.Remove(plant);return Done("Removed the plant and roots. No seedling was recovered.");
    }
}
