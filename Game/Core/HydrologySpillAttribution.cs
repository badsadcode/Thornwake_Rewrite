using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Thornwake.Core;

public enum SpillFrontierBinding { PotentiallyBinding, NonBindingGivenCurrentBounds, UnknownBinding }
public sealed record SpillCellEvidence(HydrologyCell Cell,int Patch,int Depth,double Spacing,double Low,double High,bool Unresolved,double? ProbeResidual=null);
public sealed record SpillFaceEvidence(SpillCellEvidence First,SpillCellEvidence Second,double Residual);
public sealed record SpillCandidateEvidence(string ReceiverId,double ReceiverLevel,double Elevation,MacroPoint Outlet,
    HydrologyCell Destination,bool ResolvedSampledPath,int InspectedPathCells,SpillCellEvidence? FirstUnresolvedPathCell);
public sealed record SpillFrontierEvidence(int Pass,string Phase,string Reason,SpillCellEvidence? Cell,int? Patch,
    double Stage,double SolverLowerBound,double? ConservativeLowerBound,string BoundProof,bool RetainedBySolver,
    double? ObservedBestElevation,double? ObservedReceiverLevel,double? DistanceFromSource,double? DistanceToObservedOutlet,
    SpillFaceEvidence? Face,SpillFrontierBinding Binding,SpillFrontierBinding EndOfPassBinding);
public sealed record SpillAttributionPass(int Pass,bool Discarded,string Termination,HydrologyCell? Source,
    int WetVisits,int SpillVisits,int BalanceVisits,int? ComponentCompletedAtVisit,double? LastMinimaxCost,int LastHeapCount,
    SpillCandidateEvidence? BestCandidate,SpillCandidateEvidence? BestResolvedCandidate,int CandidateObservations,
    IReadOnlyList<SpillFrontierEvidence> Frontiers);
public sealed record HydrologySpillAttribution(bool Complete,string? Truncation,int RecordLimit,long OperationLimit,long Operations,
    int Records,int? FirstComponentCompletedAtVisit,IReadOnlyList<SpillAttributionPass> Passes);
public sealed record ProfiledHydrologySpill(HydrologyTopologyResult Result,HydrologyGraphProfile Graph,HydrologySpillAttribution Attribution);

// No result from this collector is consumed by search, refinement, queues or work accounting.
internal sealed class SpillAttributionCollector
{
    private sealed class Pass
    {
        internal int Number,Wet,Spill,Balance,Heap,Candidates;internal int? Completed;
        internal bool Discarded;internal string End="running";
        internal SpillCellEvidence? Source;internal double? Cost;
        internal double UnknownFloor=double.PositiveInfinity;
        internal SpillCandidateEvidence? Best,Resolved;
        internal readonly List<SpillFrontierEvidence> Frontiers=new();
    }
    private readonly List<Pass> _passes=new();private Pass _p=null!;
    private readonly double _step,_stage;private readonly CancellationToken _cancel;private readonly Action<string>? _observer;
    private readonly int _recordLimit;private readonly long _operationLimit;
    private int _records;private long _operations;private string? _truncated;private int? _firstCompleted;
    private SpillFaceEvidence? _face;
    internal SpillAttributionCollector(double step,double stage,CancellationToken cancel,int recordLimit=65536,long operationLimit=2000000,Action<string>? observer=null)
    {
        if(recordLimit<0 || recordLimit>262144 || operationLimit<0 || operationLimit>8000000)throw new ArgumentOutOfRangeException(nameof(recordLimit));
        _step=step;_stage=stage;_cancel=cancel;_recordLimit=recordLimit;_operationLimit=operationLimit;_observer=observer;
    }
    private bool Work(string moment)
    {
        _observer?.Invoke(moment);_cancel.ThrowIfCancellationRequested();if(_truncated!=null)return false;
        if(_operations>=_operationLimit){_truncated="diagnostic-work-limit";return false;}_operations++;return true;
    }
    private bool Record(string moment)
    {
        if(!Work(moment))return false;if(_records>=_recordLimit){_truncated="diagnostic-record-limit";return false;}_records++;return true;
    }
    internal void Begin(int pass){_p=new(){Number=pass};_passes.Add(_p);_face=null;}
    internal void Visit(bool spill,bool balancing)
    {if(balancing)_p.Balance++;else if(spill)_p.Spill++;else _p.Wet++;}
    internal void Spill(SpillCellEvidence source,int visits)
    {_p.Source=source;_p.Completed=visits;_firstCompleted??=visits;}
    internal void Step(double cost,int heap){_p.Cost=cost;_p.Heap=heap;}
    internal void Restart(){_p.Discarded=true;_p.End="topology-correction";_face=null;}
    internal void End(string reason){_p.End=reason;}
    internal void Face(SpillFaceEvidence face){_face=face;}
    private double Distance(HydrologyCell a,HydrologyCell b)
    {double x=(a.X-b.X)+(a.Size-b.Size)*.5,z=(a.Z-b.Z)+(a.Size-b.Size)*.5;return _step*Math.Sqrt(x*x+z*z);}
    internal static SpillFrontierBinding Classify(double? lower,double? supportedCandidate,bool valid=true)
    {
        if(!valid || lower==null || supportedCandidate==null || !double.IsFinite(lower.Value) || !double.IsFinite(supportedCandidate.Value))
            return SpillFrontierBinding.UnknownBinding;
        // Equal elevation can change the receiver-ID tie break: it is not safely non-binding.
        return lower>supportedCandidate?SpillFrontierBinding.NonBindingGivenCurrentBounds:SpillFrontierBinding.PotentiallyBinding;
    }
    internal void Frontier(SpillCellEvidence? cell,int? patch,string reason,double lower,bool retained,bool balancing,Func<MacroPoint,HydrologyCell> outletCell)
    {
        var face=_face;_face=null;if(!Record("frontier"))return;
        string phase=balancing?"balance":_p.Source!=null?"spill":"component";
        double? proof=null;string proofKind="no-canonical-spill-prefix";
        if(phase=="spill" && !_p.Discarded && _p.Cost is double cost)
        {
            // Earlier unresolved exits can supply a cheaper alternate entrance. Carry their minimum
            // forward; never use unresolved cell sample minima as continuous-terrain lower bounds.
            proof=Math.Min(cost,_p.UnknownFloor);proofKind="minimax-prefix-with-earlier-unknown-floor";
            _p.UnknownFloor=Math.Min(_p.UnknownFloor,proof.Value);
        }
        double? sourceDistance=cell!=null && _p.Source!=null?Distance(cell.Cell,_p.Source.Cell):null;
        double? outletDistance=cell!=null && _p.Best!=null?Distance(cell.Cell,outletCell(_p.Best.Outlet)):null;
        var binding=Classify(proof,_p.Resolved?.Elevation,!_p.Discarded);
        _p.Frontiers.Add(new(_p.Number,phase,reason,cell,patch,_stage,lower,proof,proofKind,retained,
            _p.Best?.Elevation,_p.Best?.ReceiverLevel,sourceDistance,outletDistance,face,binding,binding));
    }
    internal void Candidate(ReconciliationExit exit,double elevation,HydrologyCell cell,
        IReadOnlyDictionary<HydrologyCell,HydrologyCell> parents,Func<HydrologyCell,SpillCellEvidence?> lookup)
    {
        if(!Record("candidate"))return;_p.Candidates++;
        bool Better(SpillCandidateEvidence? old)=>old==null || elevation<old.Elevation || elevation==old.Elevation &&
            (string.CompareOrdinal(exit.Id,old.ReceiverId)<0 || exit.Id==old.ReceiverId && cell.CompareTo(old.Destination)<0);
        bool best=Better(_p.Best),resolved=Better(_p.Resolved);if(!best && !resolved)return;
        bool supported=true;int count=0;var key=cell;SpillCellEvidence? failure=null;
        for(;;)
        {
            if(!Work("path"))return;count++;var leaf=lookup(key);
            if(leaf==null || leaf.Cell!=key || leaf.Unresolved){supported=false;failure=leaf;break;}
            if(!parents.TryGetValue(key,out var parent))
            {supported=_p.Source?.Cell==key;break;}key=parent;
        }
        var candidate=new SpillCandidateEvidence(exit.Id,exit.Level,elevation,exit.Position,cell,supported,count,failure);
        if(best)_p.Best=candidate;if(resolved && supported)_p.Resolved=candidate;
    }
    internal HydrologySpillAttribution Finish()
    {
        var result=new List<SpillAttributionPass>();
        foreach(var p in _passes)
        {
            _cancel.ThrowIfCancellationRequested();var frontiers=new List<SpillFrontierEvidence>();
            foreach(var f in p.Frontiers)
            {
                _cancel.ThrowIfCancellationRequested();
                // A later lower unknown also prevents a stronger proof for an earlier side branch.
                double? bound=f.ConservativeLowerBound is double b?Math.Min(b,p.UnknownFloor):null;
                frontiers.Add(f with {ConservativeLowerBound=bound,EndOfPassBinding=Classify(bound,p.Resolved?.Elevation,!p.Discarded && _truncated==null)});
            }
            result.Add(new(p.Number,p.Discarded,p.End,p.Source?.Cell,p.Wet,p.Spill,p.Balance,p.Completed,p.Cost,p.Heap,
                p.Best,p.Resolved,p.Candidates,frontiers.AsReadOnly()));
        }
        return new(_truncated==null,_truncated,_recordLimit,_operationLimit,_operations,_records,_firstCompleted,result.AsReadOnly());
    }
}

internal sealed partial class AdaptiveBasinTopology
{
    private SpillAttributionCollector? _spillDiagnostics;
    private SpillCellEvidence SpillEvidence(Cell cell)
    {
        int depth=0;for(int n=_unit;n>cell.Key.Size;n/=2)depth++;
        double? residual=null;
        if(cell.Unresolved)
        {
            long x=cell.Key.X,z=cell.Key.Z;int n=cell.Key.Size,h=n/2;
            // Existing probes only. No Height/At/Sample callback, admission or refinement.
            double a=_samples[(x,z)],b=_samples[(x+n,z)],c=_samples[(x,z+n)],d=_samples[(x+n,z+n)];
            residual=Math.Max(Math.Max(Math.Abs(_samples[(x+h,z)]-(a+b)*.5),Math.Abs(_samples[(x+h,z+n)]-(c+d)*.5)),
                Math.Max(Math.Max(Math.Abs(_samples[(x,z+h)]-(a+c)*.5),Math.Abs(_samples[(x+n,z+h)]-(b+d)*.5)),Math.Abs(_samples[(x+h,z+h)]-(a+b+c+d)*.25)));
        }
        return new(cell.Key,Owner(cell.Key),depth,cell.Key.Size*_step,cell.Low,cell.High,cell.Unresolved,residual);
    }
    private SpillCellEvidence? SpillLookup(HydrologyCell key)
    {var cell=LocalityCell(key.X,key.Z,out _);return cell?.Key==key?SpillEvidence(cell):null;}
    private string ObserveFaceFailure(string reason,Cell a,Cell b,double residual)
    {_spillDiagnostics?.Face(new(SpillEvidence(a),SpillEvidence(b),residual));return reason;}
    private void ObserveFrontier(Cell? cell,int? patch,string reason,double lower)
    {
        if(_spillDiagnostics==null)return;
        HydrologyCell Outlet(MacroPoint point)=>new((long)Math.Round((point.X-_d.MinX)/_step),(long)Math.Round((point.Z-_d.MinZ)/_step),0);
        _spillDiagnostics.Frontier(cell==null?null:SpillEvidence(cell),patch,reason,lower,_frontier.Count<_r.Limits.FrontierEntries,_balancing,Outlet);
    }
    internal static ProfiledHydrologySpill ProfileSpill(ReconciliationDomain d,HydrologyTopologyRecipe r,int patch,int? quad,
        CancellationToken cancellation=default,int recordLimit=65536,long operationLimit=2000000,Action<string>? observer=null)
    {
        if(r.Algorithm!=3)throw new ArgumentException("Spill attribution requires retained algorithm 3.");
        var query=new AdaptiveBasinTopology(d,r,cancellation);query._graph=new(query._step,cancellation,null);
        query._spillDiagnostics=new(query._step,d.Stage,cancellation,recordLimit,operationLimit,observer);
        var result=query.Execute(patch,quad);return new(result,query._graph.Finish(),query._spillDiagnostics.Finish());
    }
}
