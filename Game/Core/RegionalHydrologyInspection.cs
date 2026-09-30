using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;

namespace Thornwake.Core;

/// <summary>Versioned diagnostic refinement of an immutable regional proposal; no fluid state or terrain mutation.</summary>
public sealed record HydrologyInspectionRecipe(int Format,int Algorithm,int Factor,RegionalDrainageRecipe Regional)
{
    public void Validate()
    {
        if(Format!=1 || Algorithm!=RegionalHydrologyInspector.Algorithm || Factor is not (2 or 4) || Regional==null || Regional.Subdivisions*Factor>256)
            throw new InvalidDataException("Unsupported hydrology inspection budget/version.");
        Regional.Validate();
    }
    public string Digest() { Validate();return Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(this))).ToLowerInvariant(); }
}
public enum CorridorCompatibility { DownhillAtSampleSpacing, SubmergedAtCandidateStage, TerrainBarrier }
public sealed record CorridorAssessment(CorridorCompatibility Compatibility,double MaximumRise,double RequiredStage,double? CandidateStage);
public sealed record InspectedCorridor(string Id,IReadOnlyList<string> SourceLinks,IReadOnlyList<CorridorPoint> Points,
    bool CornerRefined,CorridorAssessment Assessment);
/// <summary>The face minimum is a proposal. LocalSpill is a bounded minimax path to this explicit network face, not the final whole-basin spill.</summary>
public sealed record InspectedSill(DrainagePort OriginalPort,IReadOnlyList<CorridorPoint> FaceSamples,CorridorPoint FaceMinimum,
    CorridorPoint LocalLowPoint,double LocalSpill,CorridorPoint ReachableCrossing,double? CoarseCandidateSpill);
public sealed record HydrologyTileInspection(string LayerId,int Cell,double SampleSpacing,InspectedSill? Outlet,IReadOnlyList<InspectedCorridor> Corridors);
public sealed record LakeComponentLink(int FirstComponent,int SecondComponent);
public sealed record LakePatchConnection(string LayerId,string CandidateLakeId,int FirstCell,int SecondCell,double CandidateStage,
    int FirstComponents,int SecondComponents,int JoinedComponents,IReadOnlyList<LakeComponentLink> Links,bool OpenPatchBoundary);

/// <summary>
/// Acceptance layer v1: finer canonical face profiles, bounded local spill diagnostics, guarded corner refinement,
/// and conservative cross-patch candidate connectivity. All decisions remain conditional on sample spacing.
/// </summary>
public sealed class RegionalHydrologyInspector
{
    public const int Algorithm=1;
    public HydrologyInspectionRecipe Recipe { get; }
    public string LayerId { get; }
    private readonly RegionalDrainageRefiner _regional;
    private readonly MacroWorldGenerator _world;
    private static readonly (int X,int Z)[] Neighbours={(-1,0),(1,0),(0,-1),(0,1)};
    private RegionalHydrologyInspector(HydrologyInspectionRecipe recipe,RegionalDrainageRefiner regional)
    {
        recipe.Validate();Recipe=recipe;_regional=regional;_world=new(recipe.Regional.Coarse.World);
        LayerId="hydrology-inspection:"+recipe.Digest();
    }
    public static RegionalHydrologyInspector Create(RegionalDrainageRefiner regional,int factor=2)
        =>new(new(1,Algorithm,factor,regional.Recipe),regional);
    public static RegionalHydrologyInspector Restore(HydrologyInspectionRecipe recipe,CancellationToken cancellation=default)
    { recipe.Validate();return new(recipe,RegionalDrainageRefiner.Restore(recipe.Regional,cancellation)); }
    private MacroPoint Node(int cell,int x,int z)
    {
        var g=_regional.Drainage.Grid;int s=Recipe.Regional.Subdivisions*Recipe.Factor;double step=g.Spacing/s;
        return new(g.MinX+((long)(cell%g.Width)*s+x)*step,g.MinZ+((long)(cell/g.Width)*s+z)*step);
    }
    private CorridorPoint Point(MacroPoint p)=>new(p.X,p.Z,_world.Sample(p.X,p.Z).Elevation);
    private double[] SampleGrid(int cell,CancellationToken cancellation)
    {
        int s=Recipe.Regional.Subdivisions*Recipe.Factor,side=s+1;var bed=new double[side*side];
        for(int z=0;z<=s;z++) { cancellation.ThrowIfCancellationRequested();for(int x=0;x<=s;x++)bed[z*side+x]=Point(Node(cell,x,z)).BedElevation; }
        return bed;
    }
    public HydrologyTileInspection Inspect(int cell,CancellationToken cancellation=default)
    {
        cancellation.ThrowIfCancellationRequested();var tile=_regional.Build(cell,cancellation);
        int s=Recipe.Regional.Subdivisions*Recipe.Factor,side=s+1;double step=tile.Span/s;
        InspectedSill? sill=null;
        if(tile.Outgoing is { } port)
        {
            var bed=SampleGrid(cell,cancellation);int width=_regional.Drainage.Grid.Width;
            int dx=port.ToCell%width-cell%width,dz=port.ToCell/width-cell/width;
            var indices=Enumerable.Range(1,s-1).Select(i=>(dz==0?i:dz>0?s:0)*side+(dx==0?i:dx>0?s:0)).ToArray();
            var face=indices.Select(i=>new CorridorPoint(Node(cell,i%side,i/side).X,Node(cell,i%side,i/side).Z,bed[i])).ToArray();
            var minimum=face.OrderBy(p=>p.BedElevation).First();
            int low=Enumerable.Range(0,bed.Length).Where(i=>i%side>0 && i%side<s && i/side>0 && i/side<s).OrderBy(i=>bed[i]).First();
            // Reuse the validated minimax flood with ONLY the explicit downstream face as outlets.
            // This is a vertex graph expressed as cell-centred metadata for the existing solver.
            var min=Node(cell,0,0);var grid=new DrainageGrid(min.X-step*.5,min.Z-step*.5,step,side,side,bed);
            var spill=InspectSpill(grid,indices,low,Recipe.Digest(),cancellation);
            int crossing=spill.Exit;var lowPoint=Node(cell,low%side,low/side);var crossingPoint=Node(cell,crossing%side,crossing/side);
            sill=new(port,Array.AsReadOnly(face),minimum,new(lowPoint.X,lowPoint.Z,bed[low]),spill.Level,
                new(crossingPoint.X,crossingPoint.Z,bed[crossing]),tile.Lake?.CandidateSpillElevation);
        }
        var corridors=RefineCorridors(tile.Links,p=>_world.Sample(p.X,p.Z).Elevation,Recipe.Factor,tile.Lake?.CandidateSpillElevation,LayerId,cancellation);
        return new(LayerId,cell,step,sill,corridors);
    }
    public LakePatchConnection InspectLakePair(int first,int second,CancellationToken cancellation=default)
    {
        if(first>second)(first,second)=(second,first);
        var d=_regional.Drainage;_ =d.Grid.Center(first);_=d.Grid.Center(second);
        if(Math.Abs(first%d.Grid.Width-second%d.Grid.Width)+Math.Abs(first/d.Grid.Width-second/d.Grid.Width)!=1 || d.BasinIndex[first]<0 || d.BasinIndex[first]!=d.BasinIndex[second])
            throw new ArgumentException("Lake inspection requires adjacent patches of the same candidate basin.");
        cancellation.ThrowIfCancellationRequested();int s=Recipe.Regional.Subdivisions*Recipe.Factor;
        var a=SampleGrid(first,cancellation);var b=SampleGrid(second,cancellation);double level=d.Basins[d.BasinIndex[first]].SpillLevel;
        var joined=ConnectPatches(a,b,s,second==first+1,level);
        return new(LayerId,_regional.LakeId(d.BasinIndex[first]),first,second,level,joined.FirstComponents,joined.SecondComponents,joined.JoinedComponents,joined.Links,joined.OpenBoundary);
    }

    public static (double Level,int Exit) InspectSpill(DrainageGrid grid,IReadOnlyList<int> exits,int source,string dependency,CancellationToken cancellation=default)
    {
        _=grid.Center(source);
        if(exits==null || exits.Count==0 || exits.Any(i=>i<0 || i>=grid.Count))throw new ArgumentException("Explicit spill exits required.");
        var flood=DrainageAnalysis.Build(grid,exits.Select(i=>new DrainageOutlet(i,grid.Bed[i])),dependency,cancellation);
        return(flood.SpillHeight[source],flood.OutletCell[source]);
    }
    public static CorridorAssessment Assess(IReadOnlyList<CorridorPoint> points,double? candidateStage=null)
    {
        if(points==null || points.Count<2 || points.Count>262144 || candidateStage.HasValue && !double.IsFinite(candidateStage.Value) ||
            points.Any(p=>!double.IsFinite(p.X)||!double.IsFinite(p.Z)||!double.IsFinite(p.BedElevation)))throw new ArgumentException("Invalid bounded corridor profile.");
        double rise=0;for(int i=1;i<points.Count;i++)rise=Math.Max(rise,points[i].BedElevation-points[i-1].BedElevation);
        double stage=points.Max(p=>p.BedElevation);
        // A bed rise below a hypothetical lake stage is not a hydraulic barrier, but no lake is filled here.
        var status=candidateStage.HasValue && stage<candidateStage.Value?CorridorCompatibility.SubmergedAtCandidateStage:
            rise<=1e-8?CorridorCompatibility.DownhillAtSampleSpacing:CorridorCompatibility.TerrainBarrier;
        return new(status,rise,stage,candidateStage);
    }

    public static IReadOnlyList<InspectedCorridor> RefineCorridors(IReadOnlyList<RegionalCorridorLink> links,Func<MacroPoint,double> sample,
        int factor,double? stage,string layer,CancellationToken cancellation=default)
    {
        if(links==null || sample==null || factor is not (2 or 4) || links.Count>16641 || stage.HasValue && !double.IsFinite(stage.Value) || string.IsNullOrWhiteSpace(layer))throw new ArgumentException("Invalid refinement input/budget.");
        if(links.Select(l=>l.Id).Distinct(StringComparer.Ordinal).Count()!=links.Count)throw new ArgumentException("Duplicate corridor identity.");
        var outgoing=links.ToDictionary(l=>l.From);var indegree=links.GroupBy(l=>l.To).ToDictionary(g=>g.Key,g=>g.Count());
        var used=new HashSet<string>();var result=new List<InspectedCorridor>();
        // Source order, not caller order. Never smooth through a junction or a declared port endpoint.
        foreach(var first in links.OrderBy(l=>l.Id,StringComparer.Ordinal))
        {
            cancellation.ThrowIfCancellationRequested();if(!used.Add(first.Id))continue;
            RegionalCorridorLink? second=null;
            if(indegree.GetValueOrDefault(first.To)==1 && outgoing.TryGetValue(first.To,out var following) && !used.Contains(following.Id)) second=following;
            CorridorPoint At(CorridorPoint a,CorridorPoint b,double t)
            {if(t==0)return a;if(t==1)return b;double x=a.X+(b.X-a.X)*t,z=a.Z+(b.Z-a.Z)*t;return new(x,z,sample(new(x,z)));}
            var original=Enumerable.Range(0,factor+1).Select(i=>At(first.From,first.To,i/(double)factor)).ToList();
            var ids=new List<string>{first.Id};bool changed=false;
            if(second is { } next)
            {
                used.Add(next.Id);ids.Add(next.Id);original.AddRange(Enumerable.Range(1,factor).Select(i=>At(next.From,next.To,i/(double)factor)));
                // Quadratic corner candidate is inside the convex hull of the two source links.
                // Keep endpoints exact and retain the original proposal when finer terrain is worse.
                var curve=new List<CorridorPoint>{first.From};
                for(int i=1;i<factor*2;i++)
                {
                    double t=i/(double)(factor*2),u=1-t;
                    double x=first.From.X+2*u*t*(first.To.X-first.From.X)+t*t*(next.To.X-first.From.X);
                    double z=first.From.Z+2*u*t*(first.To.Z-first.From.Z)+t*t*(next.To.Z-first.From.Z);
                    curve.Add(new(x,z,sample(new(x,z))));
                }
                curve.Add(next.To);var before=Assess(original,stage);var after=Assess(curve,stage);
                if(curve.Max(p=>p.BedElevation)<=original.Max(p=>p.BedElevation)+1e-8 && after.MaximumRise<=before.MaximumRise+1e-8 &&
                    (before.Compatibility==CorridorCompatibility.TerrainBarrier || after.Compatibility!=CorridorCompatibility.TerrainBarrier))
                {changed=!curve.SequenceEqual(original);original=curve;}
            }
            // Sampled endpoint heights are immutable authoritative baseline values; no bed flattening.
            result.Add(new(layer+":"+first.Id,ids.AsReadOnly(),original.AsReadOnly(),changed,Assess(original,stage)));
        }
        return result.AsReadOnly();
    }

    public sealed record PatchConnections(int FirstComponents,int SecondComponents,int JoinedComponents,IReadOnlyList<LakeComponentLink> Links,bool OpenBoundary);
    public static PatchConnections ConnectPatches(IReadOnlyList<double> first,IReadOnlyList<double> second,int subdivisions,bool east,double stage)
    {
        int side=subdivisions+1;
        if(subdivisions<1 || subdivisions>256 || first==null || second==null || first.Count!=side*side || second.Count!=side*side ||
            !double.IsFinite(stage) || first.Any(h=>!double.IsFinite(h)) || second.Any(h=>!double.IsFinite(h)))throw new ArgumentException("Invalid candidate lake patches.");
        for(int i=0;i<side;i++)if(first[east?i*side+subdivisions:subdivisions*side+i]!=second[east?i*side:i])throw new ArgumentException("Lake patches disagree on shared terrain.");
        (int[] Labels,int Count,bool Open) Components(IReadOnlyList<double> bed,bool isFirst)
        {
            int n=subdivisions;var labels=Enumerable.Repeat(-1,n*n).ToArray();var wet=new bool[n*n];bool open=false;
            for(int z=0;z<n;z++)for(int x=0;x<n;x++) {int i=z*side+x;wet[z*n+x]=Math.Max(Math.Max(bed[i],bed[i+1]),Math.Max(bed[i+side],bed[i+side+1]))<stage;}
            int count=0;var queue=new Queue<int>();
            for(int i=0;i<wet.Length;i++)
            {
                if(!wet[i] || labels[i]>=0)continue;labels[i]=count;queue.Enqueue(i);
                while(queue.TryDequeue(out int c))
                {
                    int x=c%n,z=c/n;if(x==0 && (!east || isFirst) || x==n-1 && (!east || !isFirst) || z==0 && (east || isFirst) || z==n-1 && (east || !isFirst))open=true;
                    foreach(var (dx,dz) in Neighbours)
                    {int nx=x+dx,nz=z+dz;if(nx<0||nx>=n||nz<0||nz>=n)continue;int j=nz*n+nx;if(wet[j]&&labels[j]<0){labels[j]=count;queue.Enqueue(j);}}
                }
                count++;
            }
            return(labels,count,open);
        }
        var a=Components(first,true);var b=Components(second,false);var edges=new HashSet<LakeComponentLink>();int s=subdivisions;
        for(int i=0;i<s;i++) {int ac=a.Labels[east?i*s+s-1:(s-1)*s+i],bc=b.Labels[east?i*s:i];if(ac>=0&&bc>=0)edges.Add(new(ac,bc));}
        var parents=Enumerable.Range(0,a.Count+b.Count).ToArray();int Root(int c){while(parents[c]!=c)c=parents[c];return c;}
        foreach(var e in edges){int x=Root(e.FirstComponent),y=Root(a.Count+e.SecondComponent);if(x!=y)parents[Math.Max(x,y)]=Math.Min(x,y);}
        int joined=Enumerable.Range(0,parents.Length).Select(Root).Distinct().Count();
        return new(a.Count,b.Count,joined,Array.AsReadOnly(edges.OrderBy(e=>e.FirstComponent).ThenBy(e=>e.SecondComponent).ToArray()),a.Open||b.Open);
    }
}
