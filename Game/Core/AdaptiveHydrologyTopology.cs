using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Execution limits are frozen in requests but are not part of geographic identity.</summary>
public sealed record HydrologyTopologyBudget(int Patches=16,int Samples=600000,int Cells=300000,
    int NodeVisits=1000000,int FrontierEntries=8192,int Splits=60000,int Passes=64)
{
    public void Validate()
    {
        if(Patches<1 || Patches>64 || Samples<1 || Samples>4000000 || Cells<1 || Cells>1048576 ||
            NodeVisits<1 || NodeVisits>4000000 || FrontierEntries<1 || FrontierEntries>65536 || Splits<0 || Splits>250000 || Passes<1 || Passes>1024)
            throw new InvalidDataException("Unsupported adaptive topology work limits.");
    }
    internal bool Contains(HydrologyTopologyBudget b)=>Patches>=b.Patches && Samples>=b.Samples && Cells>=b.Cells &&
        NodeVisits>=b.NodeVisits && FrontierEntries>=b.FrontierEntries && Splits>=b.Splits && Passes>=b.Passes;
}
public sealed record HydrologyTopologyRecipe(int Format,int Algorithm,HydrologyInspectionRecipe InspectionDependency,
    int MaximumDepth,double MinimumSpacing,double MaximumProbeSpacing,double HeightTolerance,HydrologyTopologyBudget Limits)
{
    public void Validate()
    {
        if(Format!=1 || Algorithm!=2 || InspectionDependency==null || Limits==null || MaximumDepth<1 || MaximumDepth>6 ||
            !double.IsFinite(MinimumSpacing) || MinimumSpacing<.001 || !double.IsFinite(MaximumProbeSpacing) ||
            MaximumProbeSpacing<MinimumSpacing || MaximumProbeSpacing>262144 || !double.IsFinite(HeightTolerance) || HeightTolerance<=0 || HeightTolerance>100)
            throw new InvalidDataException("Unsupported adaptive topology recipe.");
        InspectionDependency.Validate();Limits.Validate();
    }
    public string LayerId()
    {
        Validate();
        // Execution expansion cannot rename already completed sampled geography.
        return "hydrology-topology:"+HydrologyReconciliationRecipe.Hash(JsonSerializer.SerializeToUtf8Bytes(new {
            Format,Algorithm,InspectionDependency,MaximumDepth,MinimumSpacing,MaximumProbeSpacing,HeightTolerance}));
    }
    public string RequestId() { Validate();return HydrologyReconciliationRecipe.Hash(JsonSerializer.SerializeToUtf8Bytes(this)); }
}
/// <summary>Compact deterministic replay continuation, not a serialized heap or trusted terrain cache.</summary>
public sealed record HydrologyTopologyContinuation(int Format,HydrologyTopologyRecipe Recipe,int SeedPatch,int? SeedQuad)
{
    public HydrologyTopologyContinuation Expand(HydrologyTopologyBudget limits)
    {
        Validate();limits.Validate();if(!limits.Contains(Recipe.Limits))throw new InvalidDataException("Continuation limits may only expand.");
        return this with {Recipe=Recipe with {Limits=limits}};
    }
    public void Validate()
    { if(Format!=1 || Recipe==null || SeedPatch<0 || SeedQuad<0)throw new InvalidDataException("Invalid topology continuation.");Recipe.Validate(); }
}
// Integer dyadic addresses, independent of world-coordinate magnitude. Size is in finest-lattice units.
public readonly record struct HydrologyCell(long X,long Z,int Size) : IComparable<HydrologyCell>
{
    public int CompareTo(HydrologyCell b) {int z=Z.CompareTo(b.Z);if(z!=0)return z;int x=X.CompareTo(b.X);return x!=0?x:Size.CompareTo(b.Size);}
    public override string ToString()=>FormattableString.Invariant($"{X}:{Z}:{Size}");
}
public enum TopologyStop { SupportedAtSampleSpacing, Blocked, NoDeclaredOutlet, WorkBudget, DomainBoundary, ResolutionLimit }
public sealed record TopologyFrontier(HydrologyCell? From,int? Patch,string Reason,double LowerBound);
public sealed record TopologyCrossing(string Id,int FirstPatch,int SecondPatch,CorridorPoint Point,string? OriginalPortId);
public sealed record TopologySpill(string DestinationId,double ReceiverLevel,double RequiredElevation,
    IReadOnlyList<HydrologyCell> Cells,IReadOnlyList<CorridorPoint> SampledPath,IReadOnlyList<TopologyCrossing> Crossings,string OutletAgreement);
public sealed record TopologyRefinement(HydrologyCell Parent,double CornerMaximum,double SampledMaximum,double SampledMinimum,double InterpolationError,bool Split);
public sealed record TopologyMetrics(int Patches,int CompletedPatches,int TerrainSamples,int AllocatedCells,int LeafCells,int Splits,int MaximumDepthReached,
    int Passes,int NodeVisits,int FrontierEntriesOmitted,int BaselinePortSamples);
public sealed record HydrologyTopologyResult(string LayerId,string RequestId,string CandidateId,double CandidateStage,
    HydrologyCell? QueryCell,TopologyStop ComponentStop,TopologyStop SpillStop,string? FinalComponentId,
    IReadOnlyList<HydrologyCell> ComponentCells,TopologySpill? SpillProposal,IReadOnlyList<DrainagePort> OriginalOutlets,
    IReadOnlyList<TopologyFrontier> Frontier,IReadOnlyList<TopologyRefinement> RefinementEvidence,TopologyMetrics Metrics,HydrologyTopologyContinuation? Continuation);

/// <summary>Algorithm 2 over immutable inspection dependencies. Algorithm 1 remains available unchanged.</summary>
public sealed class AdaptiveHydrologyReconciler
{
    public HydrologyTopologyRecipe Recipe {get;}
    private readonly HydrologyReconciler _baseline;
    private AdaptiveHydrologyReconciler(HydrologyTopologyRecipe recipe,HydrologyReconciler baseline) {Recipe=recipe;_baseline=baseline;}
    public static AdaptiveHydrologyReconciler Restore(HydrologyTopologyRecipe recipe,CancellationToken cancellation=default)
    {
        recipe.Validate();cancellation.ThrowIfCancellationRequested();
        return new(recipe,HydrologyReconciler.Restore(new(1,1,recipe.InspectionDependency,Adaptive:new()),cancellation));
    }
    public HydrologyTopologyResult Reconcile(int patch,int? quad=null,CancellationToken cancellation=default)
        =>AdaptiveBasinTopology.Run(_baseline.CreateDomain(patch,cancellation),Recipe,patch,quad,cancellation);
    public static HydrologyTopologyResult Continue(HydrologyTopologyContinuation continuation,CancellationToken cancellation=default)
    {
        continuation.Validate();return Restore(continuation.Recipe,cancellation).Reconcile(continuation.SeedPatch,continuation.SeedQuad,cancellation);
    }
}
