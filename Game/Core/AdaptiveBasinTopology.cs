using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Thornwake.Core;

/// <summary>
/// Local, terrain-driven dyadic topology. Algorithm 2 admits fully refined patches; algorithm 3
/// admits only requested base roots. Changed topology requires a fresh stable graph pass.
/// Neighbour patches may have different depths; face overlap, not corner contact, connects leaves.
/// </summary>
internal sealed partial class AdaptiveBasinTopology
{
    private sealed class Cell
    {
        public HydrologyCell Key {get;} public double High {get;} public double Low {get;} public bool Unresolved {get;}
        public Cell[]? Children {get;set;}
        public bool FaceWorkScheduled {get;set;}
        public int Generation {get;set;} // Query-local monotonic topology stamp.
        public Cell(HydrologyCell key,double high,double low,bool unresolved,Cell[]? children)
        {Key=key;High=high;Low=low;Unresolved=unresolved;Children=children;}
    }
    private sealed class TopologyChanged : Exception {}
    private sealed class Patch
    {
        // Null array entries are explicitly unmaterialized base cells in algorithm 3, never dry cells.
        public Cell[] Roots {get;}
        public List<Cell> Leaves {get;}
        public Patch(Cell[] roots,IEnumerable<Cell> leaves){Roots=roots;Leaves=leaves.ToList();}
    }
    private sealed class Limit : Exception {public string Reason {get;} public Limit(string reason){Reason=reason;}}
    private readonly ReconciliationDomain _d;
    private readonly HydrologyTopologyRecipe _r;
    private readonly CancellationToken _cancel;
    private readonly int _unit,_patchUnits;
    private readonly double _step;
    private readonly Dictionary<(long X,long Z),double> _samples=new();
    private readonly Dictionary<int,Patch> _patches=new();
    private readonly HashSet<int> _startedPatches=new();
    private readonly Dictionary<int,string> _failed=new();
    private readonly Dictionary<string,DrainagePort> _originals=new(StringComparer.Ordinal);
    private readonly List<TopologyFrontier> _frontier=new();
    private readonly List<TopologyRefinement> _evidence=new(); // First 64 cells; diagnostic only, never replay state.
    private int _allocated,_splits,_maxDepth,_passes,_visits,_omitted;
    private int _seedPatch;private int? _seedQuad;
    private static readonly (int X,int Z)[] Directions={(-1,0),(1,0),(0,-1),(0,1)};
    private AdaptiveBasinTopology(ReconciliationDomain d,HydrologyTopologyRecipe r,CancellationToken cancel)
    {
        r.Validate();_d=d;_r=r;_cancel=cancel;
        if(r.Algorithm==3){_expansions=new();_reuseExpansions=true;}
        if(d.Subdivisions<1 || d.Subdivisions>256 || d.Width<1 || d.Depth<1 || (long)d.Width*d.Depth>262144 ||
            !double.IsFinite(d.MinX) || !double.IsFinite(d.MinZ) || !double.IsFinite(d.PatchSpan) || d.PatchSpan<=0 || !double.IsFinite(d.Stage))
            throw new ArgumentException("Invalid topology domain.");
        _unit=1<<(r.MaximumDepth+1);_patchUnits=d.Subdivisions*_unit;_step=d.PatchSpan/_patchUnits;
        if(d.MinX+_step==d.MinX || d.MinZ+_step==d.MinZ)throw new ArgumentException("Topology resolution exceeds world-coordinate precision.");
    }
    internal static HydrologyTopologyResult Run(ReconciliationDomain d,HydrologyTopologyRecipe r,int patch,int? quad,CancellationToken cancel=default)
        =>new AdaptiveBasinTopology(d,r,cancel).Execute(patch,quad);
    private HydrologyTopologyResult Execute(int patch,int? quad)
    {
        for(;;)
        {
            _cancel.ThrowIfCancellationRequested();_frontier.Clear();_omitted=0;
            if(_passes>=_r.Limits.Passes)
            {
                Unknown(null,patch,"pass-budget",-100000);
                return Result(null,new(),TopologyStop.WorkBudget,TopologyStop.WorkBudget,null);
            }
            _passes++;_graph?.BeginPass(_passes);_locality?.BeginPass();_spillDiagnostics?.Begin(_passes);
            try {return Search(patch,quad);}
            catch(TopologyChanged)
            {
                // Keep charged samples/topology; discard obsolete graph queues. Algorithm 3 first
                // closes cascading face evidence in already materialized neighbours, avoiding a
                // whole graph replay for each individual correction.
                _graph?.Restart(_spillPhase,_lastCorrection);_spillDiagnostics?.Restart();
                if(_r.Algorithm==3 && !BalanceForRestart())
                    return Result(null,new(),TopologyStop.WorkBudget,TopologyStop.WorkBudget,null);
            }
        }
    }
    private MacroPoint Position(long x,long z)=>new(_d.MinX+x*_step,_d.MinZ+z*_step);
    private double Height(long x,long z)
    {
        _cancel.ThrowIfCancellationRequested();
        if(_samples.TryGetValue((x,z),out double h))return h;
        if(_samples.Count>=_r.Limits.Samples)throw new Limit("sample-budget");
        h=_d.Sample(Position(x,z));if(!double.IsFinite(h) || Math.Abs(h)>100000)throw new InvalidDataException("Invalid authoritative terrain sample.");
        _samples.Add((x,z),h);_work?.Sample(_sampleCell,_sharedSampling);return h;
    }
    private Cell Refine(long x,long z,int size,int depth)
    {
        _cancel.ThrowIfCancellationRequested();
        if(_allocated>=_r.Limits.Cells)throw new Limit("cell-budget");_allocated++;
        _maxDepth=Math.Max(_maxDepth,depth);
        _sampleCell=new(x,z,size);
        int half=size/2;
        double a=Height(x,z),b=Height(x+size,z),c=Height(x,z+size),d=Height(x+size,z+size);
        double top=Height(x+half,z),bottom=Height(x+half,z+size),left=Height(x,z+half),right=Height(x+size,z+half),mid=Height(x+half,z+half);
        double error=Math.Max(Math.Max(Math.Abs(top-(a+b)*.5),Math.Abs(bottom-(c+d)*.5)),
            Math.Max(Math.Max(Math.Abs(left-(a+c)*.5),Math.Abs(right-(b+d)*.5)),Math.Abs(mid-(a+b+c+d)*.25)));
        double high=Math.Max(Math.Max(Math.Max(a,b),Math.Max(c,d)),Math.Max(Math.Max(top,bottom),Math.Max(mid,Math.Max(left,right))));
        double low=Math.Min(Math.Min(Math.Min(a,b),Math.Min(c,d)),Math.Min(Math.Min(top,bottom),Math.Min(mid,Math.Min(left,right))));
        bool needed=error>_r.HeightTolerance || size*_step*.5>_r.MaximumProbeSpacing;
        Cell[]? children=null;
        if(needed && depth<_r.MaximumDepth && half*_step>=_r.MinimumSpacing)
        {
            if(_splits>=_r.Limits.Splits)throw new Limit("split-budget");_splits++;
            children=new[]{Refine(x,z,half,depth+1),Refine(x+half,z,half,depth+1),
                Refine(x,z+half,half,depth+1),Refine(x+half,z+half,half,depth+1)};
        }
        if(needed && _evidence.Count<64)_evidence.Add(new(new(x,z,size),Math.Max(Math.Max(a,b),Math.Max(c,d)),high,low,error,children!=null));
        if(size==_unit)_work?.Classify(new(x,z,size),low,high,_d.Stage);
        return new(new(x,z,size),high,low,needed && children==null,children);
    }
    private string? Load(int patch)
    {
        _cancel.ThrowIfCancellationRequested();if(_patches.ContainsKey(patch))return null;
        if(_failed.TryGetValue(patch,out string? failure))return failure;
        if(_startedPatches.Count>=_r.Limits.Patches)return _failed[patch]="patch-budget";
        _startedPatches.Add(patch);
        if(_r.Algorithm==3)
        {
            _patches.Add(patch,new(new Cell[_d.Subdivisions*_d.Subdivisions],Array.Empty<Cell>()));
            foreach(var port in _d.OriginalOutlets(patch))_originals.TryAdd(port.Id,port);
            return null;
        }
        try
        {
            var roots=new Cell[_d.Subdivisions*_d.Subdivisions];var leaves=new List<Cell>();
            void Collect(Cell c) {if(c.Children==null)leaves.Add(c);else foreach(var child in c.Children)Collect(child);}
            for(int z=0;z<_d.Subdivisions;z++)for(int x=0;x<_d.Subdivisions;x++)
            {
                var cell=Refine((long)(patch%_d.Width)*_patchUnits+x*_unit,(long)(patch/_d.Width)*_patchUnits+z*_unit,_unit,0);
                roots[z*_d.Subdivisions+x]=cell;Collect(cell);
            }
            _patches.Add(patch,new(roots,leaves.ToArray()));
            foreach(var port in _d.OriginalOutlets(patch))_originals.TryAdd(port.Id,port);
            return null;
        }
        catch(Limit exhausted) {return _failed[patch]=exhausted.Reason;}
    }
    private Cell EnsureRoot(int patch,int index)
    {
        var data=_patches[patch];if(data.Roots[index] is {} existing){ScheduleReachedRoot(existing);return existing;}
        int x=index%_d.Subdivisions,z=index/_d.Subdivisions;
        // Refine the whole requested root before exposing any of it. Failure leaves an explicit
        // unknown root; cached samples/abandoned cell allocations remain charged to this query.
        var root=Refine((long)(patch%_d.Width)*_patchUnits+x*_unit,(long)(patch/_d.Width)*_patchUnits+z*_unit,_unit,0);
        data.Roots[index]=root;
        void Collect(Cell c){if(c.Children==null)data.Leaves.Add(c);else foreach(var child in c.Children)Collect(child);}
        Collect(root);ScheduleReachedRoot(root);return root;
    }
    private int Owner(HydrologyCell c)=>(int)(c.Z/_patchUnits)*_d.Width+(int)(c.X/_patchUnits);
    private Cell At(long x,long z)
    {
        int patch=(int)(z/_patchUnits)*_d.Width+(int)(x/_patchUnits);
        int root=(int)(z%_patchUnits/_unit)*_d.Subdivisions+(int)(x%_patchUnits/_unit);
        var c=_r.Algorithm==3?EnsureRoot(patch,root):_patches[patch].Roots[root];
        while(c.Children is {} children)
        {int h=c.Key.Size/2;c=children[(z>=c.Key.Z+h?2:0)+(x>=c.Key.X+h?1:0)];}
        return c;
    }
    // The interval walk skips whole neighbouring leaves; there is no finest-resolution occupancy grid.
    private IEnumerable<(Cell? Cell,int? Patch,string? Failure)> NeighboursRaw(Cell c)
    {
        var k=c.Key;
        foreach(var direction in Directions)
        {
            bool horizontal=direction.X!=0;
            long fixedCoordinate=horizontal?(direction.X<0?k.X-1:k.X+k.Size):(direction.Z<0?k.Z-1:k.Z+k.Size);
            long start=horizontal?k.Z:k.X,end=start+k.Size;
            if(fixedCoordinate<0 || fixedCoordinate>=(long)(horizontal?_d.Width:_d.Depth)*_patchUnits)
            {yield return(null,null,"domain-boundary");continue;}
            for(long along=start;along<end;)
            {
                _cancel.ThrowIfCancellationRequested();long x=horizontal?fixedCoordinate:along,z=horizontal?along:fixedCoordinate;
                int patch=(int)(z/_patchUnits)*_d.Width+(int)(x/_patchUnits);string? failure=Load(patch);
                if(failure!=null){yield return(null,patch,failure);break;}
                Cell? neighbour=null;string? rootFailure=null;
                try {neighbour=At(x,z);}catch(Limit exhausted){rootFailure=exhausted.Reason;}
                if(rootFailure!=null){yield return(null,patch,rootFailure);break;}
                string? seam=ReconcileFace(c,neighbour!);
                if(seam!=null)yield return(null,patch,seam);else yield return(neighbour,patch,null);
                along=Math.Min(end,horizontal?neighbour!.Key.Z+neighbour.Key.Size:neighbour!.Key.X+neighbour.Key.Size);
            }
        }
    }
    private string? ReconcileFace(Cell a,Cell b)
    {
        if(a.Key.Size==b.Key.Size)return null;
        var coarse=a.Key.Size>b.Key.Size?a:b;var fine=ReferenceEquals(coarse,a)?b:a;
        bool vertical=a.Key.X+a.Key.Size==b.Key.X || b.Key.X+b.Key.Size==a.Key.X;
        long fixedCoordinate=vertical?Math.Max(a.Key.X,b.Key.X):Math.Max(a.Key.Z,b.Key.Z);
        long start=vertical?fine.Key.Z:fine.Key.X,end=start+fine.Key.Size;
        long coarseStart=vertical?coarse.Key.Z:coarse.Key.X;
        double H(long t)=>_samples[vertical?(fixedCoordinate,t):(t,fixedCoordinate)];
        double first=H(coarseStart),last=H(coarseStart+coarse.Key.Size),error=0;
        foreach(long t in new[]{start,(start+end)/2,end})
            error=Math.Max(error,Math.Abs(H(t)-(first+(last-first)*((double)(t-coarseStart)/coarse.Key.Size))));
        if(error<=_r.HeightTolerance)return null;
        int half=coarse.Key.Size/2;
        if(half<2 || half*_step<_r.MinimumSpacing)return ObserveFaceFailure("resolution-limit",a,b,error);
        if(_splits>=_r.Limits.Splits)return ObserveFaceFailure("split-budget",a,b,error);
        try
        {
            _sharedSampling=true;
            _splits++;int depth=0;for(int n=_unit;n>coarse.Key.Size;n/=2)depth++;
            long x=coarse.Key.X,z=coarse.Key.Z;
            var children=new[]{Refine(x,z,half,depth+1),Refine(x+half,z,half,depth+1),Refine(x,z+half,half,depth+1),Refine(x+half,z+half,half,depth+1)};
            coarse.Children=children;coarse.Generation++;_lastCorrection=coarse.Key;
            _locality?.Corrected(coarse.Key,depth,_passes,_spillPhase,_balancing);
            _graph?.Corrected(_spillPhase && _graphParents?.ContainsKey(coarse.Key)==true);RemoveExpansion(coarse.Key);
            int patch=Owner(coarse.Key);var old=_patches[patch];var leaves=new List<Cell>();
            void Collect(Cell c){if(c.Children==null)leaves.Add(c);else foreach(var child in c.Children)Collect(child);}
            foreach(var root in old.Roots)if(root!=null)Collect(root);_patches[patch]=new(old.Roots,leaves.ToArray());
            if(_r.Algorithm==3)
            {
                void Schedule(Cell cell){if(cell.Children==null)_balance.Add(cell.Key);else foreach(var child in cell.Children)Schedule(child);}
                foreach(var child in children)Schedule(child);
            }
            if(_evidence.Count<64)_evidence.Add(new(coarse.Key,coarse.High,children.Max(c=>c.High),children.Min(c=>c.Low),error,true));
        }
        catch(Limit exhausted){return ObserveFaceFailure(exhausted.Reason,a,b,error);}
        finally {_sharedSampling=false;}
        throw new TopologyChanged();
    }
    private void Unknown(Cell? cell,int? patch,string reason,double lower)
    {
        ObserveFrontier(cell,patch,reason,lower);
        if(_frontier.Count>=_r.Limits.FrontierEntries){_omitted++;return;}
        _frontier.Add(new(cell?.Key,patch,reason,lower));
    }
    private bool Visit(Cell cell)
    {
        _cancel.ThrowIfCancellationRequested();if(_visits>=_r.Limits.NodeVisits){Unknown(cell,Owner(cell.Key),"node-budget",cell.Low);return false;}
        _visits++;_work?.Visit(cell.Key,_spillPhase);_spillDiagnostics?.Visit(_spillPhase,_balancing);
        if(!_balancing)_graph?.Visit(cell.Key,_spillPhase);return true;
    }
    private TopologyStop Stop(double bound)
    {
        if(_omitted>0)return TopologyStop.WorkBudget;
        var relevant=_frontier.Where(f=>f.LowerBound<=bound).ToArray();
        if(relevant.Any(f=>f.Reason.EndsWith("-budget",StringComparison.Ordinal)))return TopologyStop.WorkBudget;
        if(relevant.Any(f=>f.Reason=="domain-boundary"))return TopologyStop.DomainBoundary;
        if(relevant.Any(f=>f.Reason=="resolution-limit"))return TopologyStop.ResolutionLimit;
        return TopologyStop.SupportedAtSampleSpacing;
    }
    private IEnumerable<ReconciliationExit> Exits(Cell c)
    {
        int patch=Owner(c.Key);int baseQuad=(int)(c.Key.Z%_patchUnits/_unit)*_d.Subdivisions+(int)(c.Key.X%_patchUnits/_unit);
        foreach(var exit in _d.Exits(patch))
        {
            if(exit.Quad.Index!=baseQuad)continue;
            // Production ports and ocean targets are lattice anchors. Round avoids far-origin inverse error.
            long x=(long)Math.Round((exit.Position.X-_d.MinX)/_step),z=(long)Math.Round((exit.Position.Z-_d.MinZ)/_step);
            if(x>=c.Key.X && x<=c.Key.X+c.Key.Size && z>=c.Key.Z && z<=c.Key.Z+c.Key.Size)yield return exit;
        }
    }
    private HydrologyTopologyResult Search(int seedPatch,int? seedQuad)
    {
        _spillPhase=false;_graphParents=null;_cancel.ThrowIfCancellationRequested();_seedPatch=seedPatch;_seedQuad=seedQuad;
        if(seedPatch<0 || seedPatch>=_d.Width*_d.Depth || seedQuad<0 || seedQuad>=_d.Subdivisions*_d.Subdivisions)throw new ArgumentOutOfRangeException(nameof(seedPatch));
        string? fail=Load(seedPatch);
        if(fail==null && _r.Algorithm==3)
        {
            _selectingSeed=true;
            try
            {
                if(seedQuad is int q)EnsureRoot(seedPatch,q);
                else for(int i=0;i<_d.Subdivisions*_d.Subdivisions;i++)EnsureRoot(seedPatch,i);
            }
            catch(Limit exhausted){fail=exhausted.Reason;}
            finally {_selectingSeed=false;}
        }
        if(fail!=null){Unknown(null,seedPatch,fail,_d.Stage);return Result(null,new(),TopologyStop.WorkBudget,TopologyStop.WorkBudget,null);}
        var candidates=_patches[seedPatch].Leaves.Where(c=>seedQuad==null ||
            (c.Key.Z%_patchUnits/_unit)*_d.Subdivisions+c.Key.X%_patchUnits/_unit==seedQuad).ToArray();
        var seed=candidates.Where(c=>c.High<_d.Stage && !c.Unresolved).OrderBy(c=>c.High).ThenBy(c=>c.Key).FirstOrDefault();
        if(seed==null)
        {
            bool uncertain=candidates.Any(c=>c.Unresolved);
            if(uncertain)foreach(var cell in candidates.Where(c=>c.Unresolved))Unknown(cell,seedPatch,"resolution-limit",-100000);
            return Result(null,new(),uncertain?TopologyStop.ResolutionLimit:TopologyStop.Blocked,uncertain?TopologyStop.ResolutionLimit:TopologyStop.Blocked,null);
        }
        var wet=new SortedDictionary<HydrologyCell,Cell>();var pending=new SortedDictionary<HydrologyCell,Cell>{{seed.Key,seed}};
        _locality?.Discover(seed.Key,null);
        while(pending.Count>0)
        {
            var c=pending.First().Value;pending.Remove(c.Key);if(wet.ContainsKey(c.Key))continue;
            if(!Visit(c))break;wet.Add(c.Key,c);_locality?.Visited(c.Key);
            foreach(var n in Neighbours(c))
            {
                if(n.Failure!=null){Unknown(c,n.Patch,n.Failure,c.High);continue;}
                var next=n.Cell!;
                if(next.Unresolved){Unknown(next,n.Patch,"resolution-limit",-100000);continue;}
                if(next.High<_d.Stage)
                {
                    _locality?.Discover(next.Key,c.Key);
                    if(!wet.ContainsKey(next.Key))pending.TryAdd(next.Key,next);
                }
            }
        }
        TopologyStop componentStop=Stop(_d.Stage);
        if(componentStop!=TopologyStop.SupportedAtSampleSpacing)return Result(seed.Key,wet,componentStop,componentStop,null);
        _spillPhase=true;
        // Entire selected component is known before choosing its canonical minimax source.
        var source=wet.Values.OrderBy(c=>c.High).ThenBy(c=>c.Key).First();
        _spillDiagnostics?.Spill(SpillEvidence(source),_visits);
        var distances=new Dictionary<HydrologyCell,double>{{source.Key,source.High}};
        var parent=new Dictionary<HydrologyCell,HydrologyCell>();_graphParents=_graph!=null?parent:null;
        var heap=new PriorityQueue<Cell,(double Cost,HydrologyCell Key)>();heap.Enqueue(source,(source.High,source.Key));
        double best=double.PositiveInfinity;Cell? destination=null;ReconciliationExit? target=null;
        while(heap.TryDequeue(out var c,out var priority))
        {
            _cancel.ThrowIfCancellationRequested();if(priority.Cost!=distances[c.Key])continue;if(priority.Cost>best)break;
            _spillDiagnostics?.Step(priority.Cost,heap.Count);
            if(!Visit(c)){Unknown(c,Owner(c.Key),"node-budget",priority.Cost);break;}
            foreach(var exit in Exits(c).OrderBy(e=>e.Id,StringComparer.Ordinal))
            {
                double required=Math.Max(priority.Cost,exit.Level);
                _spillDiagnostics?.Candidate(exit,required,c.Key,parent,SpillLookup);
                if(required<best || required==best && (target==null || string.CompareOrdinal(exit.Id,target.Id)<0 || exit.Id==target.Id && c.Key.CompareTo(destination!.Key)<0))
                {best=required;target=exit;destination=c;}
            }
            foreach(var n in Neighbours(c))
            {
                if(n.Failure!=null){Unknown(c,n.Patch,n.Failure,priority.Cost);continue;}
                var next=n.Cell!;
                // The interpolation residual is not a bound on unsampled terrain. Do not use a dry
                // sample minimum to dismiss a still-uncertain alternative.
                if(next.Unresolved)Unknown(next,n.Patch,"resolution-limit",priority.Cost);
                double cost=Math.Max(priority.Cost,next.High);if(cost>best)continue;
                if(distances.TryGetValue(next.Key,out double seen) && seen<=cost)continue;
                distances[next.Key]=cost;parent[next.Key]=c.Key;heap.Enqueue(next,(cost,next.Key));
            }
        }
        var spillStop=Stop(best);if(target==null)return Result(seed.Key,wet,componentStop,
            spillStop==TopologyStop.SupportedAtSampleSpacing?TopologyStop.NoDeclaredOutlet:spillStop,null);
        var path=new List<HydrologyCell>{destination!.Key};while(parent.TryGetValue(path[^1],out var previous))path.Add(previous);path.Reverse();
        var crossings=new List<TopologyCrossing>();var sampledPath=new List<CorridorPoint>();
        for(int i=0;i<path.Count;i++)
        {
            _cancel.ThrowIfCancellationRequested();var cell=path[i];long x=cell.X+cell.Size/2,z=cell.Z+cell.Size/2;
            var position=Position(x,z);sampledPath.Add(new(position.X,position.Z,_samples[(x,z)]));
            if(i+1<path.Count && Owner(cell)!=Owner(path[i+1])) {var crossing=Crossing(cell,path[i+1]);crossings.Add(crossing);sampledPath.Add(crossing.Point);}
        }
        long tx=(long)Math.Round((target.Position.X-_d.MinX)/_step),tz=(long)Math.Round((target.Position.Z-_d.MinZ)/_step);
        sampledPath.Add(new(target.Position.X,target.Position.Z,_samples[(tx,tz)]));
        string agreement=crossings.Any(c=>c.OriginalPortId!=null && _originals.ContainsKey(c.OriginalPortId))?"original-crossing-confirmed":
            crossings.Count==0?"no-interpatch-crossing":_originals.Count>0?"alternative-crossing-proposed":"new-crossing-proposed";
        return Result(seed.Key,wet,componentStop,spillStop,new(target.Id,target.Level,best,path.AsReadOnly(),sampledPath.AsReadOnly(),crossings.AsReadOnly(),agreement));
    }
    private TopologyCrossing Crossing(HydrologyCell a,HydrologyCell b)
    {
        bool vertical=a.X+a.Size==b.X || b.X+b.Size==a.X;
        long fixedCoordinate=vertical?Math.Max(a.X,b.X):Math.Max(a.Z,b.Z);
        long start=vertical?Math.Max(a.Z,b.Z):Math.Max(a.X,b.X);
        long end=vertical?Math.Min(a.Z+a.Size,b.Z+b.Size):Math.Min(a.X+a.Size,b.X+b.Size);
        // Dyadic face overlap is a full face of the smaller leaf. All three samples already exist.
        var points=new[]{start,(start+end)/2,end}.Select(t=>vertical?(X:fixedCoordinate,Z:t):(X:t,Z:fixedCoordinate))
            .Select(k=>(Key:k,Height:_samples[k])).OrderBy(p=>p.Height).ThenBy(p=>p.Key.X).ThenBy(p=>p.Key.Z).ToArray();
        var selected=points[0];var point=Position(selected.Key.X,selected.Key.Z);int low=Math.Min(Owner(a),Owner(b)),high=Math.Max(Owner(a),Owner(b));
        var original=_d.Ports(low,high).Where(p=>p.Position==point && p.BedElevation==selected.Height).OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray();
        string? old=original.Length>0?original[0].Id:null;
        string id=old??FormattableString.Invariant($"{_r.LayerId()}:{_d.CandidateId}:crossing:{low}:{high}:{selected.Key.X}:{selected.Key.Z}");
        return new(id,low,high,new(point.X,point.Z,selected.Height),old);
    }
    private HydrologyTopologyResult Result(HydrologyCell? query,SortedDictionary<HydrologyCell,Cell> wet,TopologyStop componentStop,TopologyStop spillStop,TopologySpill? spill)
    {
        _spillDiagnostics?.End(componentStop+"/"+spillStop);
        string layer=_r.LayerId();string? id=componentStop==TopologyStop.SupportedAtSampleSpacing && spillStop==TopologyStop.SupportedAtSampleSpacing?layer+":"+_d.CandidateId+":component:"+wet.Keys.First():null;
        var frontier=_frontier.Distinct().OrderBy(f=>f.From).ThenBy(f=>f.Patch).ThenBy(f=>f.Reason,StringComparer.Ordinal).ThenBy(f=>f.LowerBound).ToArray();
        return new(layer,_r.RequestId(),_d.CandidateId,_d.Stage,query,componentStop,spillStop,id,Array.AsReadOnly(wet.Keys.ToArray()),spill,
            Array.AsReadOnly(_originals.Values.OrderBy(p=>p.Id,StringComparer.Ordinal).ToArray()),Array.AsReadOnly(frontier),_evidence.AsReadOnly(),
            new(_startedPatches.Count,_r.Algorithm==2?_patches.Count:_patches.Values.Count(p=>p.Roots.All(c=>c!=null)),_samples.Count,_allocated,_patches.Values.Sum(p=>p.Leaves.Count),_splits,_maxDepth,_passes,_visits,_omitted,_d.BaselineSampleCount?.Invoke()??0),
            componentStop==TopologyStop.WorkBudget || spillStop==TopologyStop.WorkBudget?new(1,_r,_seedPatch,_seedQuad):null);
    }
}
