using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Overlapping developer attribution buckets, not proofs of hydraulic relevance or dry terrain.</summary>
public sealed record HydrologyWorkProfile(int SamplesInComponentPatches,int SamplesInComponentRoots,
    int SamplesOutsideVisitedRoots,int SamplesInSampledDryRoots,int SamplesNearCandidateStage,int SamplesInUnclassifiedRoots,
    int SharedFaceSamples,int SharedFaceVisits,int SamplesInSpillVisitedRoots,int SpillVisitedRoots,int WetGraphVisits,int SpillGraphVisits,
    int TopologyRestarts,int FullySampledPatches,int PartiallySampledPatches);
public sealed record ProfiledHydrologyQuery(HydrologyTopologyResult Result,HydrologyWorkProfile Work);

internal sealed class HydrologyWorkCollector
{
    private sealed class Work { public int Samples,Shared;public bool? Dry,Near; }
    private readonly Dictionary<HydrologyCell,Work> _roots=new();
    private readonly HashSet<HydrologyCell> _wet=new(),_spill=new();
    private readonly int _unit,_patchUnits,_width;
    private int _wetVisits,_spillVisits,_sharedVisits;
    internal void SharedVisit()=>_sharedVisits++;
    internal HydrologyWorkCollector(int subdivisions,int depth,int width){_unit=1<<(depth+1);_patchUnits=subdivisions*_unit;_width=width;}
    internal HydrologyCell Root(HydrologyCell c)=>new(c.X/_unit*_unit,c.Z/_unit*_unit,_unit);
    private Work Entry(HydrologyCell c){c=Root(c);if(!_roots.TryGetValue(c,out var w))_roots.Add(c,w=new());return w;}
    internal void Sample(HydrologyCell c,bool shared){var w=Entry(c);w.Samples++;if(shared)w.Shared++;}
    internal void Classify(HydrologyCell c,double low,double high,double stage){var w=Entry(c);w.Dry=low>=stage;w.Near=low<=stage && high>=stage;}
    internal void Visit(HydrologyCell c,bool spill){if(spill){_spill.Add(Root(c));_spillVisits++;}else{_wet.Add(Root(c));_wetVisits++;}}
    internal HydrologyWorkProfile Finish(HydrologyTopologyResult result)
    {
        int Patch(HydrologyCell c)=>(int)(c.Z/_patchUnits)*_width+(int)(c.X/_patchUnits);
        var roots=result.ComponentCells.Select(Root).ToHashSet();var patches=roots.Select(Patch).ToHashSet();
        int Sum(Func<HydrologyCell,Work,bool> test)=>_roots.Where(p=>test(p.Key,p.Value)).Sum(p=>p.Value.Samples);
        return new(Sum((k,w)=>patches.Contains(Patch(k))),Sum((k,w)=>roots.Contains(k)),
            Sum((k,w)=>!_wet.Contains(k)&&!_spill.Contains(k)),Sum((k,w)=>w.Dry==true),Sum((k,w)=>w.Near==true),
            Sum((k,w)=>w.Dry==null),_roots.Values.Sum(w=>w.Shared),_sharedVisits,Sum((k,w)=>_spill.Contains(k)),_spill.Count,
            _wetVisits,_spillVisits,result.Metrics.Passes-1,result.Metrics.CompletedPatches,result.Metrics.Patches-result.Metrics.CompletedPatches);
    }
}
internal sealed partial class AdaptiveBasinTopology
{
    private HydrologyWorkCollector? _work;
    private HydrologyCell _sampleCell;
    private bool _sharedSampling,_spillPhase;
    internal static ProfiledHydrologyQuery Profile(ReconciliationDomain d,HydrologyTopologyRecipe r,int patch,int? quad,CancellationToken cancel=default)
    {
        var search=new AdaptiveBasinTopology(d,r,cancel){_work=new(d.Subdivisions,r.MaximumDepth,d.Width)};
        var result=search.Execute(patch,quad);return new(result,search._work.Finish(result));
    }
}
