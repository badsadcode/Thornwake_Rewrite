using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Frozen error/length limits. Reaching them certifies sampled resolution, never absence of sub-sample terrain.</summary>
public sealed record AdaptiveHydrologySettings(int MaximumDepth = 8, double MinimumSpacing = 2,
    double MaximumProbeSpacing = 64, double HeightTolerance = .25)
{
    public void Validate()
    {
        if (MaximumDepth < 1 || MaximumDepth > 12 || !double.IsFinite(MinimumSpacing) || MinimumSpacing < .001 ||
            !double.IsFinite(MaximumProbeSpacing) || MaximumProbeSpacing < MinimumSpacing || MaximumProbeSpacing > 262144 ||
            !double.IsFinite(HeightTolerance) || HeightTolerance <= 0 || HeightTolerance > 100)
            throw new InvalidDataException("Unsupported adaptive hydrology settings.");
    }
}
public enum AdaptiveStop { SampledTolerance, DepthOrSpacingLimit, SampleBudget }
public sealed record AdaptiveHydrologyProfile(IReadOnlyList<CorridorPoint> Points, CorridorAssessment Assessment,
    AdaptiveStop Stop, int Samples, int Subdivisions, double LargestUnprobedSpan);

/// <summary>Only inserts real terrain samples into an anchored polyline. Does not smooth or carve.</summary>
public static class AdaptiveHydrologySampler
{
    public static AdaptiveHydrologyProfile Inspect(IReadOnlyList<CorridorPoint> anchors, Func<MacroPoint,double> sample,
        AdaptiveHydrologySettings settings, int sampleBudget, double? candidateStage = null, CancellationToken cancellation = default)
    {
        settings.Validate();
        if (anchors == null || anchors.Count < 2 || anchors.Count > 65536 || sample == null || sampleBudget < 0 || sampleBudget > 100000)
            throw new ArgumentException("Invalid adaptive profile or budget.");
        _ = RegionalHydrologyInspector.Assess(anchors, candidateStage);
        cancellation.ThrowIfCancellationRequested();
        var points = new List<CorridorPoint> { anchors[0] }; int samples = 0, splits = 0;
        var stop = AdaptiveStop.SampledTolerance; double largest = 0;
        void Segment(CorridorPoint a, CorridorPoint b, int depth, bool nearTurn)
        {
            cancellation.ThrowIfCancellationRequested();
            double dx = b.X-a.X, dz = b.Z-a.Z, length = Math.Sqrt(dx*dx+dz*dz);
            if (length == 0) { points.Add(b); return; }
            if (samples == sampleBudget)
            { stop = AdaptiveStop.SampleBudget; largest = Math.Max(largest,length); points.Add(b); return; }
            double x = a.X+dx*.5, z = a.Z+dz*.5;
            if ((x == a.X && z == a.Z) || (x == b.X && z == b.Z))
            { if (stop != AdaptiveStop.SampleBudget) stop=AdaptiveStop.DepthOrSpacingLimit; largest=Math.Max(largest,length); points.Add(b); return; }
            double bed = sample(new(x,z)); samples++;
            if (!double.IsFinite(bed) || Math.Abs(bed)>100000) throw new InvalidDataException("Nonfinite adaptive terrain.");
            var mid = new CorridorPoint(x,z,bed);
            double deviation = Math.Abs(bed-(a.BedElevation+b.BedElevation)*.5);
            bool rise = Math.Max(bed-a.BedElevation,b.BedElevation-bed)>settings.HeightTolerance;
            bool stageCrossing = candidateStage is double level && Math.Min(Math.Min(a.BedElevation,b.BedElevation),bed)<level &&
                Math.Max(Math.Max(a.BedElevation,b.BedElevation),bed)>=level;
            bool refine = length>settings.MaximumProbeSpacing || deviation>settings.HeightTolerance || rise || stageCrossing || nearTurn;
            if (refine && depth<settings.MaximumDepth && length*.5>settings.MinimumSpacing)
            { splits++; Segment(a,mid,depth+1,false); Segment(mid,b,depth+1,false); }
            else
            {
                if (refine && stop!=AdaptiveStop.SampleBudget) stop=AdaptiveStop.DepthOrSpacingLimit;
                points.Add(mid); points.Add(b); largest=Math.Max(largest,length*.5);
            }
        }
        for (int i=1;i<anchors.Count;i++)
        {
            bool turn=false;
            if(i+1<anchors.Count)
            {
                double ax=anchors[i].X-anchors[i-1].X,az=anchors[i].Z-anchors[i-1].Z;
                double bx=anchors[i+1].X-anchors[i].X,bz=anchors[i+1].Z-anchors[i].Z;
                turn=Math.Abs(ax*bz-az*bx)>1e-8;
            }
            Segment(anchors[i-1],anchors[i],0,turn);
        }
        return new(points.AsReadOnly(),RegionalHydrologyInspector.Assess(points,candidateStage),stop,samples,splits,largest);
    }
}
