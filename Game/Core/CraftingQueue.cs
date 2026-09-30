using System;
using System.IO;
using System.Linq;

namespace Thornwake.Core;

public sealed partial class Simulation
{
    public void ValidateCraftQueue()
    {
        if(State.CraftQueue==null||State.CraftQueue.Count>50)throw new InvalidDataException("Invalid crafting queue.");
        foreach(var entry in State.CraftQueue)
        {
            if(entry==null||string.IsNullOrWhiteSpace(entry.Id)||entry.Id.Length>200||string.IsNullOrWhiteSpace(entry.RecipeId)||string.IsNullOrWhiteSpace(entry.StationId)||entry.RemainingCount<0||entry.RemainingCount>999||entry.UntilMaterials&&entry.RemainingCount!=0||!entry.UntilMaterials&&entry.RemainingCount==0&&State.CraftJob?.QueueEntryId!=entry.Id||!Catalog.Recipes.TryGetValue(entry.RecipeId,out var recipe)||!MatchesStation(recipe,entry.StationId))
                throw new InvalidDataException("This world refers to an invalid crafting request or unavailable station.");
        }
        if(State.CraftQueue.Select(q=>q.Id).Distinct().Count()!=State.CraftQueue.Count)throw new InvalidDataException("Duplicate crafting queue requests.");
        if(State.CraftJob is {} job&&(job.QueueEntryId==null||job.QueueEntryId!=""&&(State.CraftQueue.Count==0||State.CraftQueue[0].Id!=job.QueueEntryId||State.CraftQueue[0].RecipeId!=job.RecipeId||State.CraftQueue[0].StationId!=job.StationId)))
            throw new InvalidDataException("The active crafting task has lost its queue request.");
    }

    private bool MatchesStation(RecipeDefinition recipe,string stationId) => recipe.Station=="hand" ? stationId=="hand" : Workstations.Find(State,stationId)?.Kind==recipe.Station;
    private bool AtCraftStation(string stationId) => stationId=="hand" || Workstations.Find(State,stationId) is {} station&&Workstations.InReach(State,station);

    public bool IsCraftInReach => State.CraftJob is {} job && AtCraftStation(job.StationId);

    public string CraftQueueStatus
    {
        get
        {
            if(State.CraftJob is {} active)return AtCraftStation(active.StationId)?"Working on "+Catalog.Recipes[active.RecipeId].Name.ToLowerInvariant()+".":"Paused — return to the station to continue.";
            if(State.CraftQueue.Count==0)return "Your crafting queue is empty.";
            if(State.EarthworkJob!=null)return "Waiting for the shovel stroke to finish.";
            var entry=State.CraftQueue[0];var recipe=Catalog.Recipes[entry.RecipeId];
            if(!AtCraftStation(entry.StationId))return "Waiting — return to the station to continue.";
            if(!CanPay(recipe.Costs))return "Waiting for ingredients. "+Missing(recipe.Costs).Message;
            return "Ready to make "+recipe.Name.ToLowerInvariant()+".";
        }
    }

    // Kept for existing one-shot interactions: another request must finish or be cancelled first.
    public ActionResult Craft(string recipeId,string stationId="hand")
    {
        if(State.CraftJob!=null||State.CraftQueue.Count>0)return Fail("Finish or cancel the current task first.");
        if(!Catalog.Recipes.TryGetValue(recipeId,out var recipe))return Fail("Unknown recipe.");
        if(!CanPay(recipe.Costs))return Missing(recipe.Costs);
        return EnqueueCraft(recipeId,stationId,1);
    }

    public ActionResult EnqueueCraft(string recipeId,string stationId,int quantity,bool untilMaterials=false)
    {
        if(!Catalog.Recipes.TryGetValue(recipeId,out var recipe))return Fail("Unknown recipe.");
        if(quantity<(untilMaterials?0:1)||quantity>999)return Fail("Choose between 1 and 999 batches.");
        if(State.CraftQueue.Count>=50)return Fail("The crafting queue is full. Finish or remove a request first.");
        stationId=recipe.Station=="hand"?"hand":stationId;
        if(!MatchesStation(recipe,stationId)||!AtCraftStation(stationId))return Fail("Use a nearby "+recipe.Station+" station for this recipe.");
        if(untilMaterials&&State.CraftJob==null&&State.CraftQueue.Count==0&&!CanPay(recipe.Costs))return Missing(recipe.Costs);
        var entry=new CraftQueueEntry{Id=Guid.NewGuid().ToString("N"),RecipeId=recipeId,StationId=stationId,RemainingCount=untilMaterials?0:quantity,UntilMaterials=untilMaterials};
        State.CraftQueue.Add(entry);
        StartNextCraft();
        return Done(untilMaterials?$"Queued {recipe.Name.ToLowerInvariant()} until ingredients run out.":$"Queued {quantity} {(quantity==1?"batch":"batches")} of {recipe.Name.ToLowerInvariant()}.");
    }

    private void StartNextCraft()
    {
        if(State.CraftJob!=null||State.EarthworkJob!=null)return;
        while(State.CraftQueue.Count>0)
        {
            var entry=State.CraftQueue[0];var recipe=Catalog.Recipes[entry.RecipeId];
            if(!CanPay(recipe.Costs))
            {
                if(entry.UntilMaterials){State.CraftQueue.RemoveAt(0);continue;}
                return;
            }
            if(!MatchesStation(recipe,entry.StationId)||!AtCraftStation(entry.StationId))return;
            Pay(recipe.Costs);
            if(!entry.UntilMaterials)entry.RemainingCount--;
            State.CraftJob=new CraftJob{QueueEntryId=entry.Id,RecipeId=entry.RecipeId,StationId=entry.StationId,Duration=CraftDuration(recipe),Remaining=CraftDuration(recipe),PaidCosts=new(recipe.Costs)};
            return;
        }
    }

    public ActionResult? TickCraft(float seconds)
    {
        if(!float.IsFinite(seconds)||seconds<0||seconds>86400)throw new ArgumentOutOfRangeException(nameof(seconds));
        double remainingTime=seconds;int completed=0;string lastName="";
        // Recipes last at least one second. The time bound plus at most 50 requests bounds this loop.
        for(int iteration=0;iteration<100000;iteration++)
        {
            StartNextCraft();
            var job=State.CraftJob;if(job==null||!AtCraftStation(job.StationId))break;
            if(remainingTime<job.Remaining){job.Remaining-=(float)remainingTime;break;}
            remainingTime-=job.Remaining;
            var recipe=Catalog.Recipes[job.RecipeId];
            State.CraftJob=null;Add(recipe.Output,recipe.Quantity);Improve("crafting");QuestTracker.RecordCraft(State,recipe.Output);completed++;lastName=recipe.Name.ToLowerInvariant();
            if(job.QueueEntryId!=""&&State.CraftQueue.Count>0&&State.CraftQueue[0].Id==job.QueueEntryId&&!State.CraftQueue[0].UntilMaterials&&State.CraftQueue[0].RemainingCount==0)State.CraftQueue.RemoveAt(0);
            // Start the next batch even on an exact boundary so reservations and saves are deterministic.
        }
        return completed==0?null:Done(completed==1?$"Made {lastName}.":$"Finished {completed} crafting batches.");
    }

    public ActionResult CancelCraft()
    {
        var job=State.CraftJob;if(job==null)return Fail("No task is in progress.");
        foreach(var cost in job.PaidCosts)Add(cost.Key,cost.Value);
        State.CraftJob=null;
        if(job.QueueEntryId!="")State.CraftQueue.RemoveAll(q=>q.Id==job.QueueEntryId);
        return Done("Task cancelled. Reserved materials returned; remaining batches removed.");
    }

    public ActionResult RemoveQueuedCraft(string id)
    {
        if(State.CraftJob?.QueueEntryId==id)return Fail("Cancel the active task to remove its remaining batches.");
        var entry=State.CraftQueue.Find(q=>q.Id==id);if(entry==null)return Fail("That request is no longer queued.");
        State.CraftQueue.Remove(entry);
        return Done("Queued request removed. No materials had been reserved.");
    }

    public ActionResult ClearCraftQueue()
    {
        if(State.CraftJob==null&&State.CraftQueue.Count==0)return Fail("The crafting queue is already empty.");
        if(State.CraftJob is {} job)foreach(var cost in job.PaidCosts)Add(cost.Key,cost.Value);
        State.CraftJob=null;State.CraftQueue.Clear();
        return Done("Crafting queue cleared. Reserved materials returned.");
    }
}
