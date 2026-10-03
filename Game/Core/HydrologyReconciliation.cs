using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace Thornwake.Core;

public sealed record HydrologyReconciliationRecipe(int Format, int Algorithm, HydrologyInspectionRecipe InspectionDependency,
    int PatchBudget = 12, int SampleBudget = 300000, int NodeBudget = 400000, int FrontierBudget = 8192,
    int AdaptiveSamplingBudget = 20000, AdaptiveHydrologySettings? Adaptive = null, int FaceSamplingBudget = 127)
{
    [System.Text.Json.Serialization.JsonIgnore]
    public AdaptiveHydrologySettings Sampling => Adaptive ?? throw new InvalidDataException("Adaptive settings must be frozen explicitly.");
    public void Validate()
    {
        if (Format!=1 || Algorithm!=HydrologyReconciler.Algorithm || InspectionDependency==null || PatchBudget<1 || PatchBudget>64 ||
            SampleBudget<1 || SampleBudget>4000000 || NodeBudget<1 || NodeBudget>2000000 || FrontierBudget<1 || FrontierBudget>65536 ||
            AdaptiveSamplingBudget<0 || AdaptiveSamplingBudget>100000 || FaceSamplingBudget<1 || FaceSamplingBudget>4096)
            throw new InvalidDataException("Unsupported reconciliation identity/budget.");
        InspectionDependency.Validate(); Sampling.Validate();
        int s=InspectionDependency.Regional.Subdivisions*InspectionDependency.Factor;
        if ((long)PatchBudget*s*s>1048576) throw new InvalidDataException("Reconciliation patch memory budget too large.");
    }
    public string Digest() { Validate(); return Hash(JsonSerializer.SerializeToUtf8Bytes(this)); }
    internal static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
public readonly record struct HydrologyQuad(int Patch,int Index) : IComparable<HydrologyQuad>
{
    public int CompareTo(HydrologyQuad other) { int p=Patch.CompareTo(other.Patch); return p!=0?p:Index.CompareTo(other.Index); }
    public override string ToString()=>FormattableString.Invariant($"{Patch}:{Index}");
}
public enum ComponentDiscovery { Complete, TruncatedByBudget, OpenDomainBoundary, Blocked, Contradictory }
public enum SpillResolution { ReconciledAtSampleSpacing, TruncatedByBudget, UnresolvedDomainBoundary, NoReachableDeclaredOutlet, Contradictory }
public sealed record ReconciliationFrontier(HydrologyQuad From, HydrologyQuad? To, string Reason, double LowerBound);
public sealed record ReconciledComponentLink(HydrologyQuad First,HydrologyQuad Second);
public sealed record ReconciledLocalComponent(int Patch,int Anchor,int VisitedQuads);
public sealed record ReconciledCrossing(string Id,int FirstPatch,int SecondPatch,CorridorPoint Point,string? OriginalPortId,AdaptiveHydrologyProfile FaceProfile);
public sealed record ReconciledSpill(string DestinationId,double DestinationLevel,double RequiredElevation,double FixedGraphElevation,double ProfileRequiredElevation,IReadOnlyList<CorridorPoint> SampledPath,
    IReadOnlyList<ReconciledCrossing> Crossings,AdaptiveHydrologyProfile AdaptiveProfile,string OutletAgreement);
public sealed record ReconciliationMetrics(int PatchesInspected,int ComponentsVisited,int TerrainSamples,int NodeVisits,
    int AdaptiveSamples,int AdaptiveSubdivisions,int FrontierEntriesOmitted,int BaselinePortSamples);
/// <summary>FinalComponentId is deliberately absent for a partial/contradictory component. No stored volume is implied.</summary>
public sealed record HydrologyReconciliationResult(string LayerId,string CandidateId,double CandidateStage,HydrologyQuad? QueryAnchor,
    ComponentDiscovery ComponentStatus,string? FinalComponentId,IReadOnlyList<ReconciledLocalComponent> Components,IReadOnlyList<ReconciledComponentLink> Connections,
    bool GridReachesDeclaredOutletAtCandidateStage,SpillResolution SpillStatus,ReconciledSpill? Spill,
    IReadOnlyList<DrainagePort> OriginalOutlets,IReadOnlyList<ReconciliationFrontier> Frontier,
    ReconciliationMetrics Metrics,IReadOnlyList<string> Contradictions);

// Internal analytic seam: production callers can only obtain terrain/outlets through the frozen world adapter.
internal sealed record ReconciliationExit(string Id,HydrologyQuad Quad,MacroPoint Position,double Level);
internal sealed record ReconciliationDomain(string Identity,string CandidateId,double Stage,double MinX,double MinZ,double PatchSpan,
    int Width,int Depth,int Subdivisions,Func<MacroPoint,double> Sample,Func<int,IReadOnlyList<ReconciliationExit>> Exits,
    Func<int,int,IReadOnlyList<DrainagePort>> Ports,Func<int,IReadOnlyList<DrainagePort>> OriginalOutlets,Func<int>? BaselineSampleCount=null,Func<int,ReceiverPatchCertificate?>? ReceiverCertificate=null);

/// <summary>Separate diagnostic algorithm. Existing regional ports, inspection outputs and terrain are never mutated.</summary>
public sealed class HydrologyReconciler
{
    public const int Algorithm=1;
    public HydrologyReconciliationRecipe Recipe { get; }
    public string LayerId { get; }
    private readonly RegionalDrainageRefiner _regional;
    private readonly MacroWorldGenerator _world;
    private HydrologyReconciler(HydrologyReconciliationRecipe recipe,RegionalDrainageRefiner regional)
    { recipe.Validate(); Recipe=recipe; _regional=regional; _world=new(recipe.InspectionDependency.Regional.Coarse.World); LayerId="hydrology-reconciliation:"+recipe.Digest(); }
    public static HydrologyReconciler Restore(HydrologyReconciliationRecipe recipe,CancellationToken cancellation=default)
    {
        recipe.Validate(); cancellation.ThrowIfCancellationRequested();
        return new(recipe,RegionalDrainageRefiner.Restore(recipe.InspectionDependency.Regional,cancellation));
    }
    public static HydrologyReconciler Create(RegionalDrainageRefiner regional,int factor=2)
        =>new(new(1,Algorithm,new(1,RegionalHydrologyInspector.Algorithm,factor,regional.Recipe),Adaptive:new()),regional);

    // Ports/ocean centres are exact lattice vertices by construction. Round back to that integer address;
    // Floor/Ceiling on an inverted far-origin double can incorrectly put both candidates outside a face.
    internal static IReadOnlyList<HydrologyQuad> TargetQuads(DrainageGrid grid,int subdivisions,int patch,MacroPoint position)
    {
        _=grid.Center(patch);double step=grid.Spacing/subdivisions;
        int x=(int)((long)Math.Round((position.X-grid.MinX)/step)-(long)(patch%grid.Width)*subdivisions);
        int z=(int)((long)Math.Round((position.Z-grid.MinZ)/step)-(long)(patch/grid.Width)*subdivisions);
        var result=new List<HydrologyQuad>();
        foreach(int qz in new[]{z-1,z})foreach(int qx in new[]{x-1,x})
            if(qx>=0&&qx<subdivisions&&qz>=0&&qz<subdivisions)result.Add(new(patch,qz*subdivisions+qx));
        return result.AsReadOnly();
    }

    public HydrologyReconciliationResult Reconcile(int seedPatch,int? seedQuad=null,CancellationToken cancellation=default)
    {
        var domain=CreateDomain(seedPatch,cancellation);
        return BasinReconciliationSearch.Run(domain,Recipe,seedPatch,seedQuad,cancellation);
    }

    internal ReconciliationDomain CreateDomain(int seedPatch,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested(); var d=_regional.Drainage; _=d.Grid.Center(seedPatch);
        int basinIndex=d.BasinIndex[seedPatch]; if(basinIndex<0)throw new ArgumentException("Select a coarse candidate basin patch.");
        var basin=d.Basins[basinIndex]; int s=Recipe.InspectionDependency.Regional.Subdivisions*Recipe.InspectionDependency.Factor;
        // These callbacks are invocation-local. A recipient must have a coarse downstream path that never returns to this basin.
        var destinations=new Dictionary<int,IReadOnlyList<ReconciliationExit>>();
        var portsBySource=new Dictionary<int,DrainagePort>();int baselineSamples=0;
        DrainagePort Port(int patch)
        {
            if(portsBySource.TryGetValue(patch,out var value))return value;
            value=_regional.Port(patch,cancellation);portsBySource.Add(patch,value);
            baselineSamples+=Recipe.InspectionDependency.Regional.Subdivisions-1;return value;
        }
        IReadOnlyList<ReconciliationExit> Exits(int patch)
        {
            if(destinations.TryGetValue(patch,out var cached))return cached;
            var exits=new List<ReconciliationExit>();
            if(d.BasinIndex[patch]!=basinIndex)
            {
                bool returns=false;int at=patch;
                for(int n=0;at>=0;n++)
                {
                    if((n&255)==0)cancellation.ThrowIfCancellationRequested();
                    if(n>d.Grid.Count)throw new InvalidDataException("Cyclic coarse destination.");
                    if(d.BasinIndex[at]==basinIndex){returns=true;break;}at=d.Downstream[at];
                }
                if(!returns)
                {
                    MacroPoint position;string id;double level;
                    if(d.Downstream[patch]<0) { position=d.Grid.Center(patch);id=_regional.LayerId+":ocean:"+patch.ToString(CultureInfo.InvariantCulture);level=_world.Manifest.Profile.SeaLevel; }
                    else { var port=Port(patch);position=port.Position;id=port.Id;level=port.BedElevation; }
                    foreach(var quad in TargetQuads(d.Grid,s,patch,position))exits.Add(new(id,quad,position,level));
                }
            }
            return destinations[patch]=exits.AsReadOnly();
        }
        IReadOnlyList<DrainagePort> Ports(int a,int b)
        {
            var ports=new List<DrainagePort>();
            if(d.Downstream[a]==b)ports.Add(Port(a));
            if(d.Downstream[b]==a)ports.Add(Port(b));
            return ports;
        }
        IReadOnlyList<DrainagePort> Originals(int patch) => d.BasinIndex[patch]==basinIndex && d.Downstream[patch]>=0 &&
            d.BasinIndex[d.Downstream[patch]]!=basinIndex ? new[]{Port(patch)} : Array.Empty<DrainagePort>();
        var domain=new ReconciliationDomain(LayerId,_regional.LakeId(basinIndex),basin.SpillLevel,d.Grid.MinX,d.Grid.MinZ,d.Grid.Spacing,
            d.Grid.Width,d.Grid.Depth,s,p=>_world.Sample(p.X,p.Z).Elevation,Exits,Ports,Originals,()=>baselineSamples);
        // Algorithm 3 never invokes this callback. No new terrain samples or whole-world index.
        ReceiverPatchCertificate? Certificate(int patch)
        {
            cancellation.ThrowIfCancellationRequested();
            if(patch<0 || patch>=d.Grid.Count || d.Downstream[patch]>=0 || d.BasinIndex[patch]==basinIndex)return null;
            double x=d.Grid.MinX+(patch%d.Grid.Width)*d.Grid.Spacing,z=d.Grid.MinZ+(patch/d.Grid.Width)*d.Grid.Spacing;
            if(!OceanPatchProof.Covers(_world,x,z,d.Grid.Spacing))return null;
            var exit=Exits(patch).FirstOrDefault();
            return exit==null?null:new(1,5,domain.Identity,domain.CandidateId,patch,exit.Id,exit.Level,"whole-patch-ocean-v1");
        }
        return domain with {ReceiverCertificate=Certificate};
    }
}
