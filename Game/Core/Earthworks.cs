using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Thornwake.Core;

public enum EarthworkMode { Dig, Fill, Level, Smooth }
public sealed class EarthworkTerrain
{
    public int Version {get;set;}=1;
    public float SampleSpacing {get;set;}=1;
    // One step = 1 cm over one 1 m² sample basis = 10 litres of loose soil.
    public Dictionary<int,int> Steps {get;set;}=new();
    public int Revision {get;set;}
}
public sealed class EarthworkJob
{
    public EarthworkMode Mode {get;set;}
    public float X {get;set;}
    public float Z {get;set;}
    public float Radius {get;set;}=1.5f;
    public float TargetHeight {get;set;}
    public float OriginX {get;set;}
    public float OriginZ {get;set;}
    public float Duration {get;set;}=1.6f;
    public float Remaining {get;set;}=1.6f;
    public int Revision {get;set;}
    public int LegacyEdits {get;set;}
}
public sealed record EarthworkPlan(bool Valid,string Message,Dictionary<int,int> Changes,int SoilCost,float Radius)
{
    public static EarthworkPlan Fail(string reason,float radius)=>new(false,reason,new(),0,radius);
}
public static class TerrainGrid
{
    public const float StepHeight=.01f;
    public const float SupportRadius=1.5f; // Covers the diagonal corners of the triangular interpolation basis.
    public static int Key(int x,int z)=>unchecked((x<<16)|(z&65535));
    public static (int X,int Z) Position(int key)=>((short)(key>>16),(short)(key&65535));
    public static int Steps(WorldState state,int x,int z)=>state.Earthworks.Steps.GetValueOrDefault(Key(x,z));
    public static float Offset(WorldState state,float x,float z)
    {
        int ix=(int)MathF.Floor(x),iz=(int)MathF.Floor(z);
        float fx=x-ix,fz=z-iz,a=Steps(state,ix,iz),b=Steps(state,ix+1,iz),c=Steps(state,ix,iz+1),d=Steps(state,ix+1,iz+1);
        return StepHeight*(fx+fz<=1?a+(b-a)*fx+(c-a)*fz:d+(c-d)*(1-fx)+(b-d)*(1-fz));
    }
    public static bool Worked(WorldState state,float x,float z)
    {
        int ix=(int)MathF.Floor(x),iz=(int)MathF.Floor(z);
        return Steps(state,ix,iz)!=0||Steps(state,ix+1,iz)!=0||Steps(state,ix,iz+1)!=0||Steps(state,ix+1,iz+1)!=0;
    }
    public static void Validate(WorldState state)
    {
        var data=state.Earthworks;
        if(data==null||data.Version!=1||data.SampleSpacing!=1||data.Revision<0||data.Revision==int.MaxValue||data.Steps==null||data.Steps.Count>13000)throw new InvalidDataException("Invalid saved earthworks grid.");
        foreach(var entry in data.Steps)
        {var p=Position(entry.Key);if(Math.Abs(p.X)>55||Math.Abs(p.Z)>55||entry.Value==0||Math.Abs((long)entry.Value)>500)throw new InvalidDataException("Invalid saved earthworks sample.");}
        if(state.EarthworkJob is {} job)
        {
            bool Finite(float n)=>float.IsFinite(n)&&Math.Abs(n)<60;
            if(!Enum.IsDefined(job.Mode)||!Finite(job.X)||!Finite(job.Z)||!Finite(job.TargetHeight)||!Finite(job.OriginX)||!Finite(job.OriginZ)||!float.IsFinite(job.Radius)||job.Radius<1||job.Radius>2.5f||job.Duration!=1.6f||!float.IsFinite(job.Remaining)||job.Remaining<=0||job.Remaining>job.Duration||job.Revision<0||job.Revision!=data.Revision||job.LegacyEdits!=state.TerrainEdits.Count||state.CraftJob!=null)
                throw new InvalidDataException("Invalid saved earthwork task.");
        }
    }
}

public sealed partial class Simulation
{
    public EarthworkPlan PreviewEarthwork(EarthworkMode mode,float x,float z,float radius,float target)
    {
        EarthworkPlan No(string reason)=>EarthworkPlan.Fail(reason,radius);
        if(!Enum.IsDefined(mode)||!float.IsFinite(x)||!float.IsFinite(z)||!float.IsFinite(radius)||radius<1||radius>2.5f||!float.IsFinite(target)||Math.Abs(target)>50)return No("Choose a valid work area.");
        if(State.Player.EquippedItem!="shovel"||Count("shovel")<1)return No("Equip your shovel.");
        if(State.CraftJob!=null)return No("Finish or cancel crafting before digging.");
        float reach=Construction.Distance(x,z,State.Player.X,State.Player.Z);
        if(reach>5.5f)return No("Move closer to the work area.");
        // Include the entire interpolation basis, including diagonal corners.
        float footprint=radius+TerrainGrid.SupportRadius;
        if(reach<footprint+.3f)return No("Step outside the work area.");
        if(State.Player.Y.HasValue&&Math.Abs(State.Player.Y.Value-Construction.Ground(State,State.Player.X,State.Player.Z))>.7f)return No("Stand on the ground to use your shovel.");
        if(Math.Abs(x)+footprint>52||Math.Abs(z)+footprint>52)return No("Keep the work area inside the valley.");
        if(Math.Abs(x)<footprint+3||Math.Abs(z)<footprint+3||Math.Abs(x-27)<footprint+5)return No("Keep clear of roads, the bridge and river.");
        if(Construction.VillageLots.Any(l=>Construction.Distance(x,z,l.X,l.Z)<l.Radius+footprint)||Workstations.Village.Any(s=>Construction.Distance(x,z,s.X,s.Z)<footprint+1.5f)||x+footprint>14&&x-footprint<25&&z+footprint>4&&z-footprint<17)return No("Keep clear of the village and its fields.");
        if(State.Structures.Any(s=>Construction.Occupies(s,Catalog.Buildings[s.Kind],x,z,footprint)))return No("The work area reaches an existing structure.");
        if(resources.Values.Any(r=>(!State.RemovedResources.Contains(r.Id)||r.Kind=="tree"&&!State.RemovedStumps.Contains(r.Id))&&Construction.Distance(x,z,r.X,r.Z)<footprint+.8f)||State.Vegetation.Any(v=>Construction.Distance(x,z,v.X,v.Z)<footprint+1))return No("Clear trees, stumps, plants and rocks from the work area.");
        if(State.Crops.Any(c=>Construction.Distance(x,z,c.X,c.Z)<footprint+.5f))return No("Keep clear of growing crops.");
        if(State.Player.Stamina<8)return No("Catch your breath before shoveling.");
        var changes=new Dictionary<int,int>();int net=0;
        for(int ix=(int)MathF.Ceiling(x-radius);ix<=MathF.Floor(x+radius);ix++)
        for(int iz=(int)MathF.Ceiling(z-radius);iz<=MathF.Floor(z+radius);iz++)
        {
            float distance=Construction.Distance(x,z,ix,iz);if(distance>=radius)continue;
            float current=TerrainData.Height(State,ix,iz);
            int strength=Math.Max(1,(int)MathF.Round(6*(1-distance*distance/(radius*radius))));
            float desired=mode switch
            {
                EarthworkMode.Dig=>-strength,
                EarthworkMode.Fill=>strength,
                EarthworkMode.Level=>(target-current)/TerrainGrid.StepHeight,
                _=>((TerrainData.Height(State,ix-1,iz)+TerrainData.Height(State,ix+1,iz)+TerrainData.Height(State,ix,iz-1)+TerrainData.Height(State,ix,iz+1))/4-current)*.5f/TerrainGrid.StepHeight
            };
            int step=Math.Clamp((int)MathF.Round(desired),-strength,strength);if(step==0)continue;
            float after=current+step*TerrainGrid.StepHeight;
            float baseHeight=TerrainData.BaseHeight(State,ix,iz);
            if(step<0&&TerrainData.MaterialAt(State,ix,iz,after)==TerrainSurface.Rock)return No("Bedrock cannot be excavated with this shovel.");
            if(Math.Abs(after-baseHeight)>2.5f)return No("This site has reached its safe shaping limit.");
            int key=TerrainGrid.Key(ix,iz);
            changes[key]=step;net+=step;
        }
        if(changes.Count==0)return No("The ground already meets this target.");
        if(net>Count("soil"))return No($"Need {net} loose soil (10 L each). Dig to collect soil first.");
        if(net<0&&Count("soil")>(10000000+net))return No("Make room for the excavated soil.");
        if(State.Earthworks.Revision>=int.MaxValue-1)return No("This world has reached its terrain edit limit.");
        return new(true,net>0?$"Uses {net} soil":net<0?$"Collects {-net} soil":"Reuses soil within the work area",changes,net,footprint);
    }
    public ActionResult StartEarthwork(EarthworkMode mode,float x,float z,float radius,float target)
    {
        if(State.EarthworkJob!=null)return Fail("Finish or cancel the current shovel stroke.");
        var plan=PreviewEarthwork(mode,x,z,radius,target);if(!plan.Valid)return Fail(plan.Message);
        State.EarthworkJob=new(){Mode=mode,X=x,Z=z,Radius=radius,TargetHeight=target,OriginX=State.Player.X,OriginZ=State.Player.Z,Revision=State.Earthworks.Revision,LegacyEdits=State.TerrainEdits.Count};
        return new(true,"Working the ground.");
    }
    public ActionResult CancelEarthwork()
    {State.EarthworkJob=null;return new(true,"Stopped shoveling. No soil or stamina was spent.");}
    public ActionResult? TickEarthwork(float seconds)
    {
        if(!float.IsFinite(seconds)||seconds<0||seconds>86400)throw new ArgumentOutOfRangeException(nameof(seconds));
        var job=State.EarthworkJob;if(job==null)return null;
        if(State.Player.EquippedItem!="shovel"||Construction.Distance(State.Player.X,State.Player.Z,job.OriginX,job.OriginZ)>.35f)
        {State.EarthworkJob=null;return Fail("Shoveling stopped because you moved or changed tools.");}
        if(job.Revision!=State.Earthworks.Revision||job.LegacyEdits!=State.TerrainEdits.Count){State.EarthworkJob=null;return Fail("The ground changed. Choose the work area again.");}
        if(seconds<job.Remaining){job.Remaining-=seconds;return null;}
        State.EarthworkJob=null;
        var plan=PreviewEarthwork(job.Mode,job.X,job.Z,job.Radius,job.TargetHeight);
        if(!plan.Valid)return Fail(plan.Message);
        foreach(var entry in plan.Changes)
        {
            int steps=State.Earthworks.Steps.GetValueOrDefault(entry.Key)+entry.Value;
            if(steps==0)State.Earthworks.Steps.Remove(entry.Key);else State.Earthworks.Steps[entry.Key]=steps;
        }
        Add("soil",-plan.SoilCost);State.Player.Stamina-=8;State.Earthworks.Revision++;Improve("earthworks");
        return Done(job.Mode+" completed. "+plan.Message+".");
    }
    public ActionResult Cultivate(float x,float z)
    {
        if(State.Player.EquippedItem!="hoe")return Fail("Equip a hoe to cultivate the soil.");
        if(Construction.Distance(x,z,State.Player.X,State.Player.Z)>5.5f)return Fail("Move closer to cultivate.");
        if(State.TerrainEdits.Any(e=>e.Delta==0&&Construction.Distance(x,z,e.X,e.Z)<1))return Fail("This soil is already prepared.");
        return ModifyTerrain(x,z,0);
    }
}