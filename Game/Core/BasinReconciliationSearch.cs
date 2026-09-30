using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Invocation-local conservative quad graph. No render-edge drains or global mutable cache.</summary>
internal sealed class BasinReconciliationSearch
{
    private sealed record Patch(double[] Heights,double[] Costs,int[] Components);
    private readonly ReconciliationDomain _d;
    private readonly HydrologyReconciliationRecipe _r;
    private readonly CancellationToken _cancel;
    private readonly int _s,_side;
    private readonly Dictionary<int,Patch> _patches=new();
    private readonly Dictionary<(long X,long Z),double> _samples=new();
    private readonly SortedDictionary<HydrologyQuad,ReconciliationFrontier> _wetFrontier=new();
    private readonly List<ReconciliationFrontier> _spillFrontier=new();
    private readonly List<string> _contradictions=new();
    private readonly Dictionary<string,DrainagePort> _originals=new(StringComparer.Ordinal);
    private int _nodes,_omitted,_adaptiveSamples,_adaptiveSplits;
    private bool _wetBudget,_wetDomain;
    private static readonly (int X,int Z)[] Offsets={(-1,0),(1,0),(0,-1),(0,1)};
    private BasinReconciliationSearch(ReconciliationDomain d,HydrologyReconciliationRecipe r,CancellationToken cancellation)
    {
        r.Validate(); _d=d;_r=r;_cancel=cancellation;_s=d.Subdivisions;_side=_s+1;
        if(_s<1 || _s>256 || d.Width<1 || d.Depth<1 || (long)d.Width*d.Depth>262144 || !double.IsFinite(d.Stage) ||
            !double.IsFinite(d.PatchSpan) || d.PatchSpan<=0 || string.IsNullOrWhiteSpace(d.Identity))throw new ArgumentException("Invalid reconciliation domain.");
    }
    public static HydrologyReconciliationResult Run(ReconciliationDomain d,HydrologyReconciliationRecipe r,int patch,int? quad,CancellationToken token=default)
        =>new BasinReconciliationSearch(d,r,token).Search(patch,quad);
    private (long X,long Z) Node(int patch,int x,int z)=>((long)(patch%_d.Width)*_s+x,(long)(patch/_d.Width)*_s+z);
    private MacroPoint Position(double x,double z)=>new(_d.MinX+x*(_d.PatchSpan/_s),_d.MinZ+z*(_d.PatchSpan/_s));
    private double Sample(MacroPoint p)
    {
        _cancel.ThrowIfCancellationRequested(); double bed=_d.Sample(p);
        if(!double.IsFinite(bed) || Math.Abs(bed)>100000)throw new InvalidDataException("Invalid reconciliation terrain sample.");return bed;
    }
    private string? Load(int patch)
    {
        if(_patches.ContainsKey(patch))return null;
        _cancel.ThrowIfCancellationRequested();
        if(_patches.Count==_r.PatchBudget)return "patch-budget";
        int required=0;
        for(int z=0;z<=_s;z++)for(int x=0;x<=_s;x++)if(!_samples.ContainsKey(Node(patch,x,z)))required++;
        if(_samples.Count+required>_r.SampleBudget)return "terrain-sample-budget";
        var heights=new double[_side*_side];var costs=new double[_s*_s];var components=Enumerable.Repeat(-1,costs.Length).ToArray();
        for(int z=0;z<=_s;z++)
        {
            _cancel.ThrowIfCancellationRequested();
            for(int x=0;x<=_s;x++)
            {
                var node=Node(patch,x,z);
                if(!_samples.TryGetValue(node,out double h))_samples.Add(node,h=Sample(Position(node.X,node.Z)));
                heights[z*_side+x]=h;
            }
        }
        for(int z=0;z<_s;z++)for(int x=0;x<_s;x++)
        {int i=z*_side+x;costs[z*_s+x]=Math.Max(Math.Max(heights[i],heights[i+1]),Math.Max(heights[i+_side],heights[i+_side+1]));}
        var queue=new Queue<int>();
        for(int i=0;i<costs.Length;i++)
        {
            if(components[i]>=0 || costs[i]>=_d.Stage)continue;
            components[i]=i;queue.Enqueue(i);
            while(queue.TryDequeue(out int q))
            {
                if((q&255)==0)_cancel.ThrowIfCancellationRequested();
                foreach(var (dx,dz) in Offsets)
                {
                    int x=q%_s+dx,z=q/_s+dz;if(x<0||x>=_s||z<0||z>=_s)continue;
                    int next=z*_s+x;if(components[next]<0 && costs[next]<_d.Stage){components[next]=i;queue.Enqueue(next);}
                }
            }
        }
        _patches.Add(patch,new(heights,costs,components));
        foreach(var port in _d.OriginalOutlets(patch))_originals.TryAdd(port.Id,port);
        return null;
    }
    private IEnumerable<HydrologyQuad?> Neighbours(HydrologyQuad q)
    {
        var p=Node(q.Patch,q.Index%_s,q.Index/_s);
        foreach(var (dx,dz) in Offsets)
        {
            long x=p.X+dx,z=p.Z+dz;
            if(x<0||z<0||x>=(long)_d.Width*_s||z>=(long)_d.Depth*_s){yield return null;continue;}
            yield return new((int)(z/_s)*_d.Width+(int)(x/_s),(int)(z%_s)*_s+(int)(x%_s));
        }
    }
    private double Cost(HydrologyQuad q)=>_patches[q.Patch].Costs[q.Index];
    private CorridorPoint Centre(HydrologyQuad q)
    {
        var node=Node(q.Patch,q.Index%_s,q.Index/_s);var p=Position(node.X+.5,node.Z+.5);
        // Heights for profile anchors are sampled below and charged to the adaptive budget.
        return new(p.X,p.Z,Cost(q));
    }
    private void WetUnknown(HydrologyQuad from,HydrologyQuad? to,string why)
    {
        if(why=="domain-boundary")_wetDomain=true;else _wetBudget=true;
        var key=to??from;if(_wetFrontier.ContainsKey(key))return;
        if(_wetFrontier.Count>=_r.FrontierBudget){_omitted++;_wetBudget=true;return;}
        _wetFrontier.Add(key,new(from,to,why,Cost(from)));
    }
    private void SpillUnknown(HydrologyQuad from,HydrologyQuad? to,string why,double cost)
    {
        if(_spillFrontier.Count<_r.FrontierBudget)_spillFrontier.Add(new(from,to,why,cost));else _omitted++;
    }
    private HydrologyReconciliationResult Search(int seedPatch,int? seedQuad)
    {
        _cancel.ThrowIfCancellationRequested();
        if(seedPatch<0||seedPatch>=_d.Width*_d.Depth||seedQuad<0||seedQuad>=_s*_s)throw new ArgumentOutOfRangeException(nameof(seedPatch));
        var initialFailure=Load(seedPatch);
        if(initialFailure!=null)
        {
            _wetBudget=true;_wetFrontier.Add(new(seedPatch,seedQuad??0),new(new(seedPatch,seedQuad??0),new(seedPatch,seedQuad??0),initialFailure,_d.Stage));
            return Result(null,new(),ComponentDiscovery.TruncatedByBudget,false,SpillResolution.TruncatedByBudget,null);
        }
        int seed=seedQuad??Enumerable.Range(0,_s*_s).Where(q=>_patches[seedPatch].Costs[q]<_d.Stage)
            .OrderBy(q=>_patches[seedPatch].Costs[q]).ThenBy(q=>q).DefaultIfEmpty(-1).First();
        if(seed<0 || Cost(new(seedPatch,seed))>=_d.Stage)return Result(seed<0?null:new(seedPatch,seed),new(),ComponentDiscovery.Blocked,false,SpillResolution.NoReachableDeclaredOutlet,null);
        var anchor=new HydrologyQuad(seedPatch,seed);var wet=new HashSet<HydrologyQuad>();var pending=new SortedSet<HydrologyQuad>{anchor};
        bool open=false;
        while(pending.Count>0)
        {
            _cancel.ThrowIfCancellationRequested();var q=pending.Min;pending.Remove(q);
            if(wet.Contains(q))continue;
            if(_nodes++>=_r.NodeBudget)
            {
                _nodes=_r.NodeBudget;WetUnknown(q,q,"node-budget");
                foreach(var more in pending)WetUnknown(more,more,"node-budget");break;
            }
            wet.Add(q);
            if(_d.Exits(q.Patch).Any(e=>e.Quad==q && Math.Max(e.Level,Cost(q))<_d.Stage))open=true;
            foreach(var maybe in Neighbours(q))
            {
                if(maybe is not { } next){WetUnknown(q,null,"domain-boundary");continue;}
                string? failure=Load(next.Patch);
                if(failure!=null){WetUnknown(q,next,failure);continue;}
                if(Cost(next)<_d.Stage && !wet.Contains(next))pending.Add(next);
            }
        }
        var componentStatus=_wetBudget?ComponentDiscovery.TruncatedByBudget:_wetDomain?ComponentDiscovery.OpenDomainBoundary:ComponentDiscovery.Complete;
        if(componentStatus!=ComponentDiscovery.Complete)
            return Result(anchor,wet,componentStatus,open,componentStatus==ComponentDiscovery.OpenDomainBoundary?SpillResolution.UnresolvedDomainBoundary:SpillResolution.TruncatedByBudget,null);
        // Canonical low anchor within the discovered component, not an unrelated tile minimum or an arbitrary face minimum.
        var source=wet.OrderBy(Cost).ThenBy(q=>q).First();
        var distances=new Dictionary<HydrologyQuad,double>{{source,Cost(source)}};
        var parent=new Dictionary<HydrologyQuad,HydrologyQuad>();
        var heap=new PriorityQueue<HydrologyQuad,(double Cost,HydrologyQuad Key)>();heap.Enqueue(source,(Cost(source),source));
        double best=double.PositiveInfinity;ReconciliationExit? chosen=null;HydrologyQuad end=default;bool frontierOverflow=false;
        while(heap.TryDequeue(out var q,out var priority))
        {
            _cancel.ThrowIfCancellationRequested();if(priority.Cost!=distances[q])continue;if(priority.Cost>best)break;
            if(_nodes>=_r.NodeBudget){SpillUnknown(q,q,"node-budget",priority.Cost);break;}_nodes++;
            foreach(var exit in _d.Exits(q.Patch).Where(e=>e.Quad==q).OrderBy(e=>e.Id,StringComparer.Ordinal))
            {
                double level=Math.Max(priority.Cost,exit.Level);
                if(level<best || level==best && (chosen==null || string.CompareOrdinal(exit.Id,chosen.Id)<0 || exit.Id==chosen.Id && q.CompareTo(end)<0))
                {best=level;chosen=exit;end=q;}
            }
            foreach(var maybe in Neighbours(q))
            {
                if(maybe is not { } next){SpillUnknown(q,null,"domain-boundary",priority.Cost);continue;}
                if(distances.TryGetValue(next,out double seen) && seen<=priority.Cost)continue;
                string? failure=Load(next.Patch);
                if(failure!=null){int before=_omitted;SpillUnknown(q,next,failure,priority.Cost);frontierOverflow|=_omitted>before;continue;}
                double level=Math.Max(priority.Cost,Cost(next));if(level>best)continue;
                if(distances.TryGetValue(next,out seen) && seen<=level)continue;
                distances[next]=level;parent[next]=q;heap.Enqueue(next,(level,next));
            }
        }
        bool uncertain=frontierOverflow||_spillFrontier.Any(f=>f.LowerBound<=best);
        var status=uncertain?(_spillFrontier.Any(f=>f.Reason!="domain-boundary")||frontierOverflow?SpillResolution.TruncatedByBudget:SpillResolution.UnresolvedDomainBoundary):
            chosen==null?SpillResolution.NoReachableDeclaredOutlet:SpillResolution.ReconciledAtSampleSpacing;
        if(chosen==null)return Result(anchor,wet,componentStatus,open,status,null);
        var path=new List<HydrologyQuad>{end};while(parent.TryGetValue(path[^1],out var previous))path.Add(previous);path.Reverse();
        var crossings=new List<ReconciledCrossing>();var profileAnchors=new List<CorridorPoint>();
        int remaining=_r.AdaptiveSamplingBudget;
        int anchorCount=path.Count+1+path.Zip(path.Skip(1),(a,b)=>a.Patch!=b.Patch?1:0).Sum();
        if(path.Count+1+(anchorCount-path.Count-1)*_r.FaceSamplingBudget>remaining || anchorCount>65536)
        {
            SpillUnknown(end,end,"adaptive-anchor-budget",best);
            return Result(anchor,wet,componentStatus,open,SpillResolution.TruncatedByBudget,null);
        }
        CorridorPoint Real(CorridorPoint p)
        {
            remaining--;_adaptiveSamples++;return p with { BedElevation=Sample(new(p.X,p.Z)) };
        }
        for(int i=0;i<path.Count;i++)
        {
            profileAnchors.Add(Real(Centre(path[i])));
            if(i+1<path.Count && path[i].Patch!=path[i+1].Patch)
            {
                var crossing=Crossing(path[i],path[i+1]);remaining-=crossing.FaceProfile.Samples;crossings.Add(crossing);profileAnchors.Add(crossing.Point);
            }
        }
        profileAnchors.Add(Real(new(chosen.Position.X,chosen.Position.Z,chosen.Level)));
        var adaptive=AdaptiveHydrologySampler.Inspect(profileAnchors,p=>Sample(p),_r.Sampling,remaining,_d.Stage,_cancel);
        _adaptiveSamples+=adaptive.Samples;_adaptiveSplits+=adaptive.Subdivisions;
        if(adaptive.Stop==AdaptiveStop.SampleBudget || crossings.Any(c=>c.FaceProfile.Stop==AdaptiveStop.SampleBudget))status=SpillResolution.TruncatedByBudget;
        // An explicit receiver/ocean level is a boundary constraint, even when its bed is lower.
        double profileRequired=Math.Max(chosen.Level,adaptive.Assessment.RequiredStage);
        double refined=Math.Max(best,profileRequired);
        if(refined>best+_r.Sampling.HeightTolerance)
        {
            _contradictions.Add("Adaptive samples exceed the minimum on the fixed quad graph; alternative spill ordering must be re-evaluated before accepting this outlet.");
            status=SpillResolution.Contradictory;
        }
        if(profileRequired<best-_r.Sampling.HeightTolerance)
        {
            _contradictions.Add("The adaptively sampled corridor offers a lower escape than the conservative quad graph. Reconcile adaptive topology before assigning a final spill level.");
            if(status==SpillResolution.ReconciledAtSampleSpacing)status=SpillResolution.Contradictory;
        }
        bool InKnownWet(CorridorPoint point)
        {
            double x=(point.X-_d.MinX)/(_d.PatchSpan/_s),z=(point.Z-_d.MinZ)/(_d.PatchSpan/_s);
            foreach(long gx in new[]{(long)Math.Floor(x),(long)Math.Ceiling(x)-1})
            foreach(long gz in new[]{(long)Math.Floor(z),(long)Math.Ceiling(z)-1})
                if(gx>=0 && gz>=0 && gx<(long)_d.Width*_s && gz<(long)_d.Depth*_s &&
                    wet.Contains(new((int)(gz/_s)*_d.Width+(int)(gx/_s),(int)(gz%_s)*_s+(int)(gx%_s))))return true;
            return false;
        }
        if(adaptive.Points.Concat(crossings.SelectMany(c=>c.FaceProfile.Points)).Any(p=>p.BedElevation>=_d.Stage && InKnownWet(p)))
        {componentStatus=ComponentDiscovery.Contradictory;_contradictions.Add("A finer dry sample contradicts the fixed-grid wet component; connectivity is not certified.");}
        string agreement=crossings.Any(c=>c.OriginalPortId!=null && _originals.ContainsKey(c.OriginalPortId))?"original-crossing-confirmed":
            crossings.Count>0?(_originals.Count>0?"alternative-crossing-proposed":"new-crossing-proposed"):"no-interpatch-crossing";
        if(agreement=="alternative-crossing-proposed")_contradictions.Add("The reachable sampled escape differs from the original basin outlet proposal; no historical port was changed.");
        var spill=new ReconciledSpill(chosen.Id,chosen.Level,refined,best,profileRequired,profileAnchors.AsReadOnly(),crossings.AsReadOnly(),adaptive,agreement);
        return Result(anchor,wet,componentStatus,open,status,spill);
    }
    private ReconciledCrossing Crossing(HydrologyQuad a,HydrologyQuad b)
    {
        var aa=Node(a.Patch,a.Index%_s,a.Index/_s);var bb=Node(b.Patch,b.Index%_s,b.Index/_s);
        (long X,long Z) first,second;
        if(aa.X!=bb.X){long x=Math.Max(aa.X,bb.X);first=(x,aa.Z);second=(x,aa.Z+1);}
        else {long z=Math.Max(aa.Z,bb.Z);first=(aa.X,z);second=(aa.X+1,z);}
        var start=Position(first.X,first.Z);var finish=Position(second.X,second.Z);
        var face=AdaptiveHydrologySampler.Inspect(new[]{new CorridorPoint(start.X,start.Z,_samples[first]),new CorridorPoint(finish.X,finish.Z,_samples[second])},
            p=>Sample(p),_r.Sampling,_r.FaceSamplingBudget,_d.Stage,_cancel);
        _adaptiveSamples+=face.Samples;_adaptiveSplits+=face.Subdivisions;
        var minimum=face.Points.OrderBy(p=>p.BedElevation).ThenBy(p=>p.X).ThenBy(p=>p.Z).First();
        double bed=minimum.BedElevation;var p=new MacroPoint(minimum.X,minimum.Z);
        int low=Math.Min(a.Patch,b.Patch),high=Math.Max(a.Patch,b.Patch);
        var original=_d.Ports(low,high).Where(o=>o.Position==p && o.BedElevation==bed).OrderBy(o=>o.Id,StringComparer.Ordinal).ToArray();
        string? id=original.Length>0?original[0].Id:null;
        return new(id??FormattableString.Invariant($"{_d.Identity}:{_d.CandidateId}:face:{low}:{high}:{first.X}:{first.Z}"),low,high,new(p.X,p.Z,bed),id,face);
    }
    private HydrologyReconciliationResult Result(HydrologyQuad? query,HashSet<HydrologyQuad> wet,ComponentDiscovery componentStatus,
        bool open,SpillResolution spillStatus,ReconciledSpill? spill)
    {
        var local=wet.GroupBy(q=>(q.Patch,Anchor:_patches[q.Patch].Components[q.Index])).OrderBy(g=>g.Key.Patch).ThenBy(g=>g.Key.Anchor)
            .Select(g=>new ReconciledLocalComponent(g.Key.Patch,g.Key.Anchor,g.Count())).ToArray();
        var connections=new HashSet<ReconciledComponentLink>();
        foreach(var q in wet)
        {
        if((q.Index&255)==0)_cancel.ThrowIfCancellationRequested();
        foreach(var candidate in Neighbours(q))
            if(candidate is { } next && next.Patch>q.Patch && wet.Contains(next))
                connections.Add(new(new(q.Patch,_patches[q.Patch].Components[q.Index]),new(next.Patch,_patches[next.Patch].Components[next.Index])));
        }
        string? id=componentStatus==ComponentDiscovery.Complete?_d.Identity+":component:"+_d.CandidateId+":"+wet.Min():null;
        var frontier=_wetFrontier.Values.Concat(_spillFrontier).Distinct().OrderBy(f=>f.From).ThenBy(f=>f.To).ThenBy(f=>f.Reason,StringComparer.Ordinal).ToArray();
        return new(_d.Identity,_d.CandidateId,_d.Stage,query,componentStatus,id,Array.AsReadOnly(local),Array.AsReadOnly(connections.OrderBy(c=>c.First).ThenBy(c=>c.Second).ToArray()),open,spillStatus,spill,
            Array.AsReadOnly(_originals.Values.OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray()),Array.AsReadOnly(frontier),
            new(_patches.Count,local.Length,_samples.Count,_nodes,_adaptiveSamples,_adaptiveSplits,_omitted,_d.BaselineSampleCount?.Invoke()??0),_contradictions.AsReadOnly());
    }
}
