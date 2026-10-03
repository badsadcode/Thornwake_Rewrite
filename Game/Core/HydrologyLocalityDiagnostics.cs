using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Thornwake.Core;

public sealed record LocalityDistance(int Count,double MinimumMetres,double MeanMetres,double MaximumMetres);
public sealed record HydrologyCorrectionLocality(int Pass,string Phase,bool DuringBalance,HydrologyCell Corrected,int Depth,double SpacingMetres,
    int DiscoveredBefore,int VisitedBefore,int HypotheticalRemainingBefore,bool DiscoveryDependency,bool PreviouslyRemoved,int DirectChildren,
    int Descendants,int NewlyAffected,double AffectedPercent,int SurvivingBoundaryCells,int BoundaryFaces,int AlternateValidFaces,
    int ReconnectableOrphanCells,int UnknownSegments,int UntrackedWetFaces,int DescendantQueuePeak,int AffectedSetPeak,int BoundarySetPeak,
    long DescendantOperations,long BoundaryOperations,long ValidationOperations,long EstimatedLocalOperations,
    LocalityDistance DescendantDistance,LocalityDistance BoundaryDistance,LocalityDistance AlternateFaceDistance,int ReplayWetVisits=0);
public sealed record HydrologyLocalityProfile(bool Complete,string? Truncation,int NodeLimit,long OperationLimit,int CorrectionLimit,
    int PeakNodes,int PeakReverseEdges,int UniqueDiscoveredNodes,long IndexMaintenanceOperations,long EstimatedIndexBytes,long DiagnosticOperations,
    int PeakDescendantQueue,int PeakAffectedSet,int PeakBoundarySet,IReadOnlyList<HydrologyCorrectionLocality> Corrections);
public sealed record ProfiledHydrologyLocality(HydrologyTopologyResult Result,HydrologyGraphProfile Graph,HydrologyLocalityProfile Locality);
internal sealed record LocalityFace(HydrologyCell? Neighbour,bool Supported,bool Wet,int LookupOperations,int ValidationOperations);

// Shadow measurements only. No output or mutable state here is consumed by the hydrology solver.
internal sealed class HydrologyLocalityCollector
{
    private sealed class Node
    {
        internal readonly HydrologyCell Key;internal int FirstChild=-1,NextSibling=-1,Children;
        internal bool Removed,Visited;
        internal Node(HydrologyCell key){Key=key;}
    }
    private sealed class Distance
    {
        private int _count;private double _min=double.PositiveInfinity,_max,_sum;
        internal void Add(double value){_count++;_min=Math.Min(_min,value);_max=Math.Max(_max,value);_sum+=value;}
        internal LocalityDistance Finish()=>new(_count,_count==0?0:_min,_count==0?0:_sum/_count,_max);
    }
    private readonly Dictionary<HydrologyCell,int> _index=new();private readonly List<Node> _nodes=new();
    private readonly HashSet<HydrologyCell> _unique=new();private readonly List<HydrologyCorrectionLocality> _records=new();
    private readonly Func<HydrologyCell,IEnumerable<LocalityFace>> _faces;
    private readonly CancellationToken _cancel;private readonly Action<string>? _observer;
    private readonly double _step;private readonly int _nodeLimit,_recordLimit;private readonly long _workLimit;
    private string? _truncated;private int _removed,_visited,_peakNodes,_peakQueue,_peakAffected,_peakBoundary;
    private long _operations,_indexOperations;
    internal HydrologyLocalityCollector(double step,Func<HydrologyCell,IEnumerable<LocalityFace>> faces,CancellationToken cancel,
        int nodeLimit=524288,long operationLimit=32000000,int correctionLimit=4096,Action<string>? observer=null)
    {
        if(nodeLimit<0 || nodeLimit>1048576 || operationLimit<0 || operationLimit>64000000 || correctionLimit<0 || correctionLimit>8192)
            throw new ArgumentOutOfRangeException(nameof(nodeLimit));
        _step=step;_faces=faces;_cancel=cancel;_nodeLimit=nodeLimit;_workLimit=operationLimit;_recordLimit=correctionLimit;_observer=observer;
    }
    private bool Work(long count=1)
    {
        _cancel.ThrowIfCancellationRequested();if(_truncated!=null)return false;
        if(count>_workLimit-_operations){_truncated="diagnostic-work-limit";return false;}_operations+=count;return true;
    }
    internal void BeginPass(){_index.Clear();_nodes.Clear();_removed=0;_visited=0;}
    internal void Discover(HydrologyCell key,HydrologyCell? parent)
    {
        if(!Work())return;_indexOperations++;if(_index.ContainsKey(key))return;
        if(_nodes.Count>=_nodeLimit || (!_unique.Contains(key) && _unique.Count>=_nodeLimit))
        {_truncated="diagnostic-node-limit";return;}
        int ancestor=-1;
        if(parent.HasValue && !_index.TryGetValue(parent.Value,out ancestor)){_truncated="missing-discovery-parent";return;}
        if(!Work(2))return;_indexOperations+=2;
        var node=new Node(key);int id=_nodes.Count;
        if(ancestor>=0){node.NextSibling=_nodes[ancestor].FirstChild;_nodes[ancestor].FirstChild=id;_nodes[ancestor].Children++;}
        _nodes.Add(node);_index.Add(key,id);_unique.Add(key);_peakNodes=Math.Max(_peakNodes,_nodes.Count);
    }
    internal void Visited(HydrologyCell key)
    {if(_truncated==null && _index.TryGetValue(key,out int i) && !_nodes[i].Visited){_nodes[i].Visited=true;_visited++;}}
    private double Gap(HydrologyCell a,HydrologyCell b)
    {
        double x=(a.X-b.X)+(a.Size-b.Size)*.5,z=(a.Z-b.Z)+(a.Size-b.Size)*.5;
        return _step*Math.Sqrt(x*x+z*z); // Centre distance, translation-independent lattice coordinates.
    }
    internal void Corrected(HydrologyCell key,int depth,int pass,bool spill,bool balancing)
    {
        _observer?.Invoke("correction");if(!Work())return;
        if(_records.Count>=_recordLimit){_truncated="diagnostic-correction-limit";return;}
        bool found=_index.TryGetValue(key,out int root),already=found && _nodes[root].Removed;
        int available=_nodes.Count-_removed;var affected=new List<int>();var queue=new Stack<int>();int peakQueue=0;
        long descendantOps=0,boundaryOps=0,validationOps=0;
        var descendants=new Distance();var boundaries=new Distance();var alternatives=new Distance();
        if(found)queue.Push(root);
        while(queue.Count>0)
        {
            _observer?.Invoke("descendant");if(!Work())return;descendantOps++;peakQueue=Math.Max(peakQueue,queue.Count);
            int id=queue.Pop();var node=_nodes[id];if(node.Removed)continue;
            node.Removed=true;_removed++;affected.Add(id);if(id!=root)descendants.Add(Gap(key,node.Key));
            for(int child=node.FirstChild;child>=0;child=_nodes[child].NextSibling)
            {if(!Work())return;descendantOps++;queue.Push(child);}
        }
        var boundary=new HashSet<HydrologyCell>();var reconnectable=new HashSet<HydrologyCell>();
        int faces=0,alternate=0,unknown=0,untracked=0;
        // A retired dry/spill dependency still has a wet boundary to revalidate even if it is not
        // in the discovery tree. Scanning only tree descendants would omit this source of work.
        IEnumerable<HydrologyCell> Sources()
        {
            if(affected.Count==0)yield return key;
            foreach(int id in affected)yield return _nodes[id].Key;
        }
        foreach(var from in Sources())foreach(var face in _faces(from))
        {
            _observer?.Invoke("boundary");
            if(!Work(face.LookupOperations+face.ValidationOperations+1))return;
            boundaryOps+=face.LookupOperations+1;validationOps+=face.ValidationOperations;
            if(face.Neighbour is not {} to){unknown++;continue;}
            if(!_index.TryGetValue(to,out int other)){if(face.Wet)untracked++;continue;}
            if(_nodes[other].Removed)continue;
            faces++;if(boundary.Add(to))boundaries.Add(Gap(key,to));
            // This includes the non-negotiable closure case: an unchanged face from a surviving
            // cell into an orphan whose discovery ancestor was retired elsewhere.
            if(face.Supported && _index.TryGetValue(from,out int orphan) && _nodes[orphan].Removed)
            {alternate++;reconnectable.Add(from);alternatives.Add(Gap(key,to));}
        }
        _peakQueue=Math.Max(_peakQueue,peakQueue);_peakAffected=Math.Max(_peakAffected,affected.Count);_peakBoundary=Math.Max(_peakBoundary,boundary.Count);
        _records.Add(new(pass,spill?"spill":"component",balancing,key,depth,key.Size*_step,_nodes.Count,_visited,available,found,already,
            found?_nodes[root].Children:0,Math.Max(0,affected.Count-(found&&!already?1:0)),affected.Count,
            available==0?0:100d*affected.Count/available,boundary.Count,faces,alternate,reconnectable.Count,unknown,untracked,
            peakQueue,affected.Count,boundary.Count,descendantOps,boundaryOps,validationOps,descendantOps+boundaryOps+validationOps,
            descendants.Finish(),boundaries.Finish(),alternatives.Finish()));
    }
    internal HydrologyLocalityProfile Finish(HydrologyGraphProfile graph)
    {
        _cancel.ThrowIfCancellationRequested();
        var records=_records.Select(c=>c with {ReplayWetVisits=graph.Passes.FirstOrDefault(p=>p.Pass==c.Pass+1)?.WetVisits??0}).ToArray();
        // Conservative modeled managed storage, not a measurement of CLR allocations: node/hash
        // capacity, reverse links, and worst scratch queues/sets. Excludes existing terrain/cache.
        long bytes=4096+192L*_peakNodes+16L*Math.Max(0,_peakNodes-1)+16L*_peakQueue+96L*(_peakAffected+_peakBoundary);
        return new(_truncated==null,_truncated,_nodeLimit,_workLimit,_recordLimit,_peakNodes,Math.Max(0,_peakNodes-1),_unique.Count,
            _indexOperations,bytes,_operations,_peakQueue,_peakAffected,_peakBoundary,Array.AsReadOnly(records));
    }
}

internal sealed partial class AdaptiveBasinTopology
{
    private HydrologyLocalityCollector? _locality;
    // Read-only lookup: unlike At/EnsureRoot this NEVER admits, samples or schedules anything.
    private Cell? LocalityCell(long x,long z,out int operations)
    {
        operations=1;int patch=(int)(z/_patchUnits)*_d.Width+(int)(x/_patchUnits);
        int root=(int)(z%_patchUnits/_unit)*_d.Subdivisions+(int)(x%_patchUnits/_unit);
        if(!_patches.TryGetValue(patch,out var data) || data.Roots[root] is not {} cell)return null;
        while(cell.Children is {} children)
        {operations++;int half=cell.Key.Size/2;cell=children[(z>=cell.Key.Z+half?2:0)+(x>=cell.Key.X+half?1:0)];}
        return cell;
    }
    private bool LocalitySupportedFace(HydrologyCell from,Cell? a,Cell b,out int probes)
    {
        probes=0;if(a==null || a.Key!=from || a.Unresolved || b.Unresolved || a.High>=_d.Stage || b.High>=_d.Stage)return false;
        if(a.Key.Size==b.Key.Size)return true;
        var coarse=a.Key.Size>b.Key.Size?a:b;var fine=ReferenceEquals(coarse,a)?b:a;
        bool vertical=a.Key.X+a.Key.Size==b.Key.X || b.Key.X+b.Key.Size==a.Key.X;
        long axis=vertical?Math.Max(a.Key.X,b.Key.X):Math.Max(a.Key.Z,b.Key.Z);
        long start=vertical?fine.Key.Z:fine.Key.X,end=start+fine.Key.Size,cs=vertical?coarse.Key.Z:coarse.Key.X;
        bool Sample(long t,out double value)=>_samples.TryGetValue(vertical?(axis,t):(t,axis),out value);
        probes=2;if(!Sample(cs,out double first) || !Sample(cs+coarse.Key.Size,out double last))return false;
        foreach(long t in new[]{start,(start+end)/2,end})
        {probes++;if(!Sample(t,out double h) || Math.Abs(h-(first+(last-first)*((double)(t-cs)/coarse.Key.Size)))>_r.HeightTolerance)return false;}
        return true;
    }
    private IEnumerable<LocalityFace> LocalityFaces(HydrologyCell key)
    {
        var source=LocalityCell(key.X,key.Z,out int sourceWork);
        foreach(var direction in Directions)
        {
            bool horizontal=direction.X!=0;
            long fixedCoordinate=horizontal?(direction.X<0?key.X-1:key.X+key.Size):(direction.Z<0?key.Z-1:key.Z+key.Size);
            long start=horizontal?key.Z:key.X,end=start+key.Size;
            if(fixedCoordinate<0 || fixedCoordinate>=(long)(horizontal?_d.Width:_d.Depth)*_patchUnits)
            {yield return new(null,false,false,1,0);continue;}
            for(long along=start;along<end;)
            {
                _cancel.ThrowIfCancellationRequested();long x=horizontal?fixedCoordinate:along,z=horizontal?along:fixedCoordinate;
                var cell=LocalityCell(x,z,out int lookup);
                if(cell==null)
                {yield return new(null,false,false,lookup+sourceWork,0);along=Math.Min(end,(along/_unit+1)*_unit);continue;}
                bool supported=LocalitySupportedFace(key,source,cell,out int probes);
                yield return new(cell.Key,supported,!cell.Unresolved && cell.High<_d.Stage,lookup+sourceWork,probes);
                along=Math.Min(end,horizontal?cell.Key.Z+cell.Key.Size:cell.Key.X+cell.Key.Size);
            }
        }
    }
    internal static ProfiledHydrologyLocality ProfileLocality(ReconciliationDomain d,HydrologyTopologyRecipe r,int patch,int? quad,
        CancellationToken cancellation=default,int nodeLimit=524288,long operationLimit=32000000,int correctionLimit=4096,Action<string>? observer=null)
    {
        if(r.Algorithm!=3)throw new ArgumentException("Locality diagnostics require the retained algorithm 3.");
        var query=new AdaptiveBasinTopology(d,r,cancellation);
        query._graph=new(query._step,cancellation,null);
        query._locality=new(query._step,query.LocalityFaces,cancellation,Math.Min(nodeLimit,r.Limits.Cells),operationLimit,correctionLimit,observer);
        var result=query.Execute(patch,quad);var graph=query._graph.Finish();
        return new(result,graph,query._locality.Finish(graph));
    }
}
