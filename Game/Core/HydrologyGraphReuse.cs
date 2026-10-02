using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Developer observations, separate from recipes, continuations and geographic identity.</summary>
public sealed record HydrologyGraphPass(int Pass,int WetVisits,int SpillVisits,int RepeatedWetVisits,int RepeatedSpillVisits,
    int CachedExpansionsConsidered,int UnchangedRepeatExpansions,int InvalidatedExpansions,int InvalidatedPreviouslyReusedExpansions,int ComputedExpansions,int ReusedExpansions,
    string? RestartPhase,HydrologyCell? CorrectionTrigger,int CorrectedLeaves,int CorrectedSpillParents,int ReusableExpansionsAtRestart,
    int DistanceObservations,double MinimumDistanceMetres,double MaximumDistanceMetres,double DistanceSumMetres);
public sealed record HydrologyGraphProfile(IReadOnlyList<HydrologyGraphPass> Passes,int PeakCachedCells,int PeakCachedEdges,int CacheRefusals);
public sealed record ProfiledHydrologyGraph(HydrologyTopologyResult Result,HydrologyGraphProfile Graph);

internal sealed partial class AdaptiveBasinTopology
{
    // Only complete local expansions are cached. Membership, costs, parents and outlets ALWAYS restart.
    private const int CacheCells=131072,CacheEdges=1048576;
    private sealed record Expansion(Cell Owner,int Generation,Cell[] Neighbours,int[] Generations)
    { internal int LastUsedPass,Reuses; }
    private Dictionary<HydrologyCell,Expansion>? _expansions;
    private int _cachedEdges;
    private bool _reuseExpansions,_balancing;
    private HydrologyCell? _lastCorrection;
    private GraphCollector? _graph;
    private Dictionary<HydrologyCell,HydrologyCell>? _graphParents;
    private void RemoveExpansion(HydrologyCell key)
    {
        if(_expansions!=null && _expansions.Remove(key,out var old))
        {_cachedEdges-=old.Neighbours.Length;_graph?.Invalidated(old.Reuses>0);_cancel.ThrowIfCancellationRequested();}
    }
    // Proof boundary: terrain samples/leaf values are immutable for this query; refinement only
    // replaces a leaf with children and increments that object's generation. A complete face walk
    // depends only on its owner and listed neighbours. Any replacement of either invalidates it.
    // No failures/partial walks are cached, and global reachability/cost chains are never retained.
    private bool Valid(Expansion e)
    {
        _cancel.ThrowIfCancellationRequested();
        if(e.Owner.Generation!=e.Generation || e.Owner.Children!=null)return false;
        for(int i=0;i<e.Neighbours.Length;i++)
        {
            _cancel.ThrowIfCancellationRequested();
            if(e.Neighbours[i].Generation!=e.Generations[i] || e.Neighbours[i].Children!=null)return false;
        }
        return true;
    }
    private IEnumerable<(Cell? Cell,int? Patch,string? Failure)> Neighbours(Cell c)
    {
        if(_expansions==null){foreach(var n in NeighboursRaw(c))yield return n;yield break;}
        if(_expansions.TryGetValue(c.Key,out var saved))
        {
            _graph?.Considered();
            if(Valid(saved))
            {
                if(saved.LastUsedPass<_passes)_graph?.Unchanged(c.Key);
                if(_reuseExpansions)
                {
                    saved.LastUsedPass=_passes;saved.Reuses++;_graph?.Reused();
                    foreach(var next in saved.Neighbours){_cancel.ThrowIfCancellationRequested();yield return(next,Owner(next.Key),null);}
                    yield break;
                }
            }
            else RemoveExpansion(c.Key);
        }
        _graph?.Computed();
        if(_expansions.Count>=Math.Min(CacheCells,_r.Limits.Cells) && !_expansions.ContainsKey(c.Key))
        {
            _graph?.Refused();foreach(var n in NeighboursRaw(c))yield return n;yield break;
        }
        var adjacent=new List<Cell>();bool complete=true;
        // Preserve original lazy execution/order, including failures and topology correction throws.
        foreach(var n in NeighboursRaw(c))
        {
            if(n.Failure!=null)complete=false;else adjacent.Add(n.Cell!);
            yield return n;
        }
        if(!complete || c.Children!=null)yield break;
        int oldEdges=_expansions.TryGetValue(c.Key,out var old)?old.Neighbours.Length:0;
        if((_expansions.Count>=Math.Min(CacheCells,_r.Limits.Cells) && old==null) || _cachedEdges-oldEdges+adjacent.Count>CacheEdges)
        {_graph?.Refused();yield break;}
        var cells=adjacent.ToArray();
        _expansions[c.Key]=new(c,c.Generation,cells,cells.Select(n=>n.Generation).ToArray()){LastUsedPass=_passes};
        _cachedEdges+=cells.Length-oldEdges;_graph?.Peak(_expansions.Count,_cachedEdges);
    }
    private bool BalanceForRestart()
    {
        _balancing=true;
        try{return BalanceKnownFaces();}
        finally
        {
            _balancing=false;
            if(_graph!=null && _expansions!=null)
                foreach(var e in _expansions.Values)
                    if(e.LastUsedPass==_passes && Valid(e))_graph.Reusable();
        }
    }
    internal static HydrologyTopologyResult RunGraphExperiment(ReconciliationDomain d,HydrologyTopologyRecipe r,int patch,int? quad,
        bool reuse,CancellationToken cancellation=default)
    {
        var search=new AdaptiveBasinTopology(d,r,cancellation);
        search._expansions=reuse?new():null;search._reuseExpansions=reuse;
        return search.Execute(patch,quad);
    }
    internal static ProfiledHydrologyGraph ProfileGraph(ReconciliationDomain d,HydrologyTopologyRecipe r,int patch,int? quad,
        bool reuse=false,CancellationToken cancellation=default,Action<string>? observer=null)
    {
        var search=new AdaptiveBasinTopology(d,r,cancellation){_expansions=new(),_reuseExpansions=reuse};
        search._graph=new(search._step,cancellation,observer);
        var result=search.Execute(patch,quad);return new(result,search._graph.Finish());
    }
    private sealed class GraphCollector
    {
        private sealed class Pass
        {
            internal int Number,Wet,Spill,RepeatedWet,RepeatedSpill,Considered,Unchanged,Invalidated,InvalidatedReused,Computed,Reused,Corrected,CorrectedParents,Reusable,Distances;
            internal string? Restart;internal HydrologyCell? Trigger;
            internal double Minimum=double.PositiveInfinity,Maximum,Sum;
        }
        private readonly List<Pass> _passes=new();
        // At most one entry per allocated/visited leaf. No cross-query/global state.
        private readonly Dictionary<HydrologyCell,(int Wet,int Spill)> _seen=new();
        private readonly double _step;private readonly CancellationToken _cancel;private readonly Action<string>? _observer;
        private Pass _p=null!;private HydrologyCell? _previousTrigger;
        private int _peakCells,_peakEdges,_refusals;
        internal GraphCollector(double step,CancellationToken cancel,Action<string>? observer){_step=step;_cancel=cancel;_observer=observer;}
        internal void BeginPass(int number){_previousTrigger=_passes.LastOrDefault()?.Trigger;_passes.Add(_p=new(){Number=number});}
        internal void Visit(HydrologyCell key,bool spill)
        {
            _seen.TryGetValue(key,out var previous);
            if(spill){_p.Spill++;if(previous.Spill>0 && previous.Spill<_p.Number)_p.RepeatedSpill++;previous.Spill=_p.Number;}
            else{_p.Wet++;if(previous.Wet>0 && previous.Wet<_p.Number)_p.RepeatedWet++;previous.Wet=_p.Number;}
            _seen[key]=previous;
        }
        internal void Restart(bool spill,HydrologyCell? trigger){_p.Restart=spill?"spill":"component";_p.Trigger=trigger;}
        internal void Corrected(bool parent){_p.Corrected++;if(parent)_p.CorrectedParents++;}
        internal void Unchanged(HydrologyCell key)
        {
            _p.Unchanged++;
            if(_previousTrigger is not {} c)return;
            // L1 rectangle gap to the previous restart-triggering cell, NOT every balance correction.
            double distance=(Math.Max(0,Math.Max(c.X-key.X-key.Size,key.X-c.X-c.Size))+
                Math.Max(0,Math.Max(c.Z-key.Z-key.Size,key.Z-c.Z-c.Size)))*_step;
            _p.Distances++;_p.Minimum=Math.Min(_p.Minimum,distance);_p.Maximum=Math.Max(_p.Maximum,distance);_p.Sum+=distance;
        }
        internal void Invalidated(bool reused){_p.Invalidated++;if(reused)_p.InvalidatedReused++;_observer?.Invoke("invalidate");}
        internal void Considered()=>_p.Considered++;
        internal void Computed()=>_p.Computed++;
        internal void Reused(){_p.Reused++;_observer?.Invoke("reuse");}
        internal void Reusable()=>_p.Reusable++;
        internal void Refused()=>_refusals++;
        internal void Peak(int cells,int edges){_peakCells=Math.Max(_peakCells,cells);_peakEdges=Math.Max(_peakEdges,edges);}
        internal HydrologyGraphProfile Finish()
        {
            var result=new List<HydrologyGraphPass>();
            foreach(var p in _passes)
            {
                _cancel.ThrowIfCancellationRequested();
                result.Add(new(p.Number,p.Wet,p.Spill,p.RepeatedWet,p.RepeatedSpill,p.Considered,p.Unchanged,p.Invalidated,p.InvalidatedReused,p.Computed,p.Reused,
                    p.Restart,p.Trigger,p.Corrected,p.CorrectedParents,p.Reusable,p.Distances,p.Distances==0?0:p.Minimum,p.Maximum,p.Sum));
            }
            return new(result.AsReadOnly(),_peakCells,_peakEdges,_refusals);
        }
    }
}
