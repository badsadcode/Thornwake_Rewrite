using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;

namespace Thornwake.Core;

public sealed record WaterColumnState(int X, int Z, float Bed, float Roof, double Volume);
public sealed class LocalWaterSave
{
    public int Version { get; set; } = 1;
    public long Tick { get; set; }
    public List<WaterColumnState> Cells { get; set; } = new();
    public LocalWaterSave Copy() => new() { Version = Version, Tick = Tick, Cells = Cells.ToList() };
    public void Validate()
    {
        if (Version != 1 || Tick < 0 || Cells == null || Cells.Count > 20000 || Cells.Any(c => c == null ||
            Math.Abs((long)c.X) > 10000 || Math.Abs((long)c.Z) > 10000 || !float.IsFinite(c.Bed) || !float.IsFinite(c.Roof) ||
            c.Bed < -1000 || c.Roof > 1000 || c.Roof <= c.Bed || !double.IsFinite(c.Volume) || c.Volume < 0 || c.Volume > c.Roof-c.Bed+.00001) ||
            Cells.Select(c => (c.X,c.Z,c.Bed,c.Roof)).Distinct().Count() != Cells.Count)
            throw new System.IO.InvalidDataException("Invalid local water state.");
        foreach(var column in Cells.GroupBy(c=>(c.X,c.Z)))
        {
            float roof=float.NegativeInfinity;
            foreach(var c in column.OrderBy(c=>c.Bed))
            {
                if(c.Bed<roof) throw new System.IO.InvalidDataException("Overlapping saved water intervals.");
                roof=c.Roof;
            }
        }
    }
}
public sealed record WaterSource(string Id, float X, float Z, float Level, float SupplyPerSecond);
public sealed record WaterOutlet(int X, int Z, float Level, float CapacityPerSecond);
public sealed record WaterColumn(int X, int Z, float Bed, float Roof);
public sealed record WaterConnection(int A, int B, float Sill, float Ceiling);

/// <summary>Finite local 1 m columns with stacked void intervals. Bounded storage and connectivity; no global velocity-field fluid solve.</summary>
public sealed class LocalWaterTopology
{
    public IReadOnlyList<WaterColumn> Cells { get; }
    public IReadOnlyList<WaterConnection> Connections { get; }
    public IReadOnlyList<(int Cell, WaterSource Source)> Sources { get; }
    public IReadOnlyList<(int Cell, WaterOutlet Outlet)> Outlets { get; }
    private LocalWaterTopology(List<WaterColumn> cells, List<WaterConnection> links, List<(int,WaterSource)> sources, List<(int,WaterOutlet)> outlets)
    { Cells=cells; Connections=links; Sources=sources; Outlets=outlets; }
    public static LocalWaterTopology Build(Func<Vector3,float> density, int minX, int minZ, int width, int depth,
        float bottom, float top, IEnumerable<WaterSource> sources, CancellationToken cancellation = default, IEnumerable<WaterOutlet>? outlets = null)
    {
        if (width < 1 || depth < 1 || (long)width*depth > 4096 || !float.IsFinite(bottom) || !float.IsFinite(top) || top<=bottom || top-bottom>64)
            throw new ArgumentOutOfRangeException(nameof(width));
        const float dy = .25f;
        var cells = new List<WaterColumn>(); var columns = new Dictionary<(int,int), List<int>>();
        for (int z=minZ; z<minZ+depth; z++) for (int x=minX; x<minX+width; x++)
        {
            cancellation.ThrowIfCancellationRequested();
            var ids = new List<int>(); columns[(x,z)] = ids;
            float previousY=bottom, previous=density(new(x+.5f,bottom,z+.5f));
            float bed=bottom;
            for (float y=bottom+dy; y<=top+.0001f; y+=dy)
            {
                float current=density(new(x+.5f,Math.Min(y,top),z+.5f));
                if ((previous<0)!=(current<0))
                {
                    float crossing=previousY+(Math.Min(y,top)-previousY)*previous/(previous-current);
                    if (current<0) bed=crossing;
                    else if (crossing-bed>.02f) { ids.Add(cells.Count); cells.Add(new(x,z,bed,crossing)); }
                }
                previous=current; previousY=Math.Min(y,top);
            }
            if (previous<0 && top-bed>.02f) { ids.Add(cells.Count); cells.Add(new(x,z,bed,top)); }
        }
        var links = new List<WaterConnection>();
        foreach (var ((x,z), ids) in columns)
        {
            cancellation.ThrowIfCancellationRequested();
            foreach (var offset in new[] { (1,0), (0,1) })
            {
                if (!columns.TryGetValue((x+offset.Item1,z+offset.Item2),out var others)) continue;
                foreach (int a in ids) foreach (int b in others)
                {
                    var ca=cells[a]; var cb=cells[b]; float low=Math.Max(ca.Bed,cb.Bed), high=Math.Min(ca.Roof,cb.Roof);
                    float start=float.NaN;
                    for (float y=low; y<high; y+=dy)
                    {
                        float upper=Math.Min(y+dy,high), middle=(y+upper)*.5f;
                        bool open=true;
                        // Conservative multi-point face passage: no transfer solely because centres happen to be air.
                        for (int k=0;k<=8;k++)
                            if (density(new(x+.5f+offset.Item1*k/8f,middle,z+.5f+offset.Item2*k/8f))>=-.005f) { open=false; break; }
                        if (open && float.IsNaN(start)) start=y;
                        if (!open && !float.IsNaN(start)) { links.Add(new(a,b,start,y)); start=float.NaN; }
                    }
                    if (!float.IsNaN(start)) links.Add(new(a,b,start,high));
                }
            }
        }
        var attachments = new List<(int,WaterSource)>();
        foreach (var source in sources)
        {
            if (!float.IsFinite(source.Level) || !float.IsFinite(source.SupplyPerSecond) || source.SupplyPerSecond<=0) throw new ArgumentException("Invalid water source");
            if (!columns.TryGetValue(((int)MathF.Floor(source.X),(int)MathF.Floor(source.Z)),out var ids)) continue;
            int index=ids.FirstOrDefault(i=>cells[i].Bed<source.Level && cells[i].Roof>source.Level,-1);
            if (index>=0) attachments.Add((index,source));
        }
        var drains = new List<(int,WaterOutlet)>();
        foreach (var outlet in outlets ?? Array.Empty<WaterOutlet>())
        {
            if (!float.IsFinite(outlet.Level) || !float.IsFinite(outlet.CapacityPerSecond) || outlet.CapacityPerSecond<=0) throw new ArgumentException("Invalid outlet");
            if (!columns.TryGetValue((outlet.X,outlet.Z),out var ids)) continue;
            int index=ids.FirstOrDefault(i=>cells[i].Bed<=outlet.Level && cells[i].Roof>outlet.Level,-1);
            if (index>=0) drains.Add((index,outlet));
        }
        return new(cells,links,attachments,drains);
    }
}

public sealed class LocalWaterSimulation
{
    public LocalWaterTopology Topology { get; }
    private readonly double[] _volume;
    private readonly List<WaterConnection>[] _adjacency;
    public long Tick { get; private set; }
    public double TotalVolume => _volume.Sum();
    public double LastSuppliedVolume { get; private set; }
    public double LastDrainedVolume { get; private set; }
    public double UnmappedVolume { get; private set; }
    public double Volume(int index) => _volume[index];
    public float Level(int index) => Topology.Cells[index].Bed+(float)_volume[index]; // columns have 1 m² area
    public LocalWaterSimulation(LocalWaterTopology topology, LocalWaterSave? saved = null)
    {
        Topology=topology; _volume=new double[topology.Cells.Count];
        _adjacency=Enumerable.Range(0,_volume.Length).Select(_=>new List<WaterConnection>()).ToArray();
        foreach(var e in topology.Connections) { _adjacency[e.A].Add(e); _adjacency[e.B].Add(e); }
        if (saved == null) SeedNaturalBasins(); else Restore(saved);
    }
    private void SeedNaturalBasins()
    {
        var adjacency = Enumerable.Range(0,_volume.Length).Select(_=>new List<WaterConnection>()).ToArray();
        foreach (var e in Topology.Connections) { adjacency[e.A].Add(e); adjacency[e.B].Add(e); }
        foreach (var (cell,source) in Topology.Sources)
        {
            var seen=new HashSet<int>(); var queue=new Queue<int>(); queue.Enqueue(cell); seen.Add(cell);
            while (queue.TryDequeue(out int i))
            {
                var c=Topology.Cells[i]; _volume[i]=Math.Max(_volume[i],Math.Clamp(source.Level-c.Bed,0,c.Roof-c.Bed));
                foreach (var e in adjacency[i])
                {
                    int other=e.A==i?e.B:e.A;
                    if (e.Sill<source.Level && seen.Add(other)) queue.Enqueue(other);
                }
            }
        }
    }
    private void Restore(LocalWaterSave saved)
    {
        saved.Validate(); Tick=saved.Tick;
        var columns=Topology.Cells.Select((c,i)=>(c,i)).GroupBy(p=>(p.c.X,p.c.Z)).ToDictionary(g=>g.Key,g=>g.ToArray());
        foreach (var old in saved.Cells)
        {
            double remaining=old.Volume;
            if (columns.TryGetValue((old.X,old.Z),out var matches))
                foreach (var (c,i) in matches)
                {
                    double overlap=c.Bed<=old.Bed && c.Roof>=old.Roof ? remaining :
                        Math.Max(0,Math.Min(c.Roof,old.Bed+old.Volume)-Math.Max(c.Bed,old.Bed));
                    double amount=Math.Min(remaining,Math.Min(overlap,c.Roof-c.Bed-_volume[i]));
                    _volume[i]+=amount; remaining-=amount;
                }
            UnmappedVolume+=remaining;
        }
        // Dig-only topology expands voids, so existing water must map without loss.
        // Future fill/barrier commands must explicitly relocate displacement before accepting their terrain edit.
        if (UnmappedVolume>.0001) throw new InvalidOperationException("Terrain edit would discard stored water; displacement must be resolved first.");
    }
    public LocalWaterSave Save() => new() { Tick=Tick, Cells=Topology.Cells.Select((c,i)=>new WaterColumnState(c.X,c.Z,c.Bed,c.Roof,_volume[i])).Where(c=>c.Volume>0).ToList() };
    public LocalWaterSimulation Copy() => new(Topology,Save());
    public void Step(float seconds)
    {
        if (!float.IsFinite(seconds) || seconds<=0 || seconds>.25f) throw new ArgumentOutOfRangeException(nameof(seconds));
        LastSuppliedVolume=0;
        foreach (var (i,source) in Topology.Sources)
        {
            var c=Topology.Cells[i]; double target=Math.Clamp(source.Level-c.Bed,0,c.Roof-c.Bed);
            double supplied=Math.Min(Math.Max(0,target-_volume[i]),source.SupplyPerSecond*seconds);
            _volume[i]+=supplied; LastSuppliedVolume+=supplied;
        }
        // A full roofed interval transmits upstream head. Storage stays bounded by its roof;
        // this is a connectivity approximation, not a pressure/velocity fluid solver.
        var heads=Enumerable.Range(0,_volume.Length).Select(Level).ToArray();
        var queue=new PriorityQueue<int,float>();
        for(int i=0;i<_volume.Length;i++) if(_volume[i]>0) queue.Enqueue(i,-heads[i]);
        bool Full(int i)=>_volume[i]>=Topology.Cells[i].Roof-Topology.Cells[i].Bed-.000001;
        while(queue.TryDequeue(out int i,out float priority))
        {
            if(-priority<heads[i]-.00001f) continue;
            foreach(var e in _adjacency[i])
            {
                int other=e.A==i?e.B:e.A;
                if(!Full(other) || Level(i)<=e.Sill || heads[other]>=heads[i] || heads[i]<=e.Sill) continue;
                heads[other]=heads[i]; queue.Enqueue(other,-heads[other]);
            }
        }
        var outgoing=new double[_volume.Length]; var incoming=new double[_volume.Length];
        var transfers=new List<(int From,int To,double Amount)>();
        foreach (var e in Topology.Connections)
        {
            int a=e.A,b=e.B;
            if (heads[a]<heads[b]) (a,b)=(b,a);
            double head=heads[a]-Math.Max(heads[b],e.Sill);
            if (head<=.00001 || _volume[a]<=0) continue;
            double opening=Math.Max(0,Math.Min(heads[a],e.Ceiling)-e.Sill);
            double available=Full(a)?_volume[a]:Math.Min(_volume[a],Math.Max(0,Level(a)-e.Sill));
            double amount=Math.Min(available,head*Math.Min(opening,1)*2*seconds);
            if (amount<=0) continue;
            transfers.Add((a,b,amount)); outgoing[a]+=amount; incoming[b]+=amount;
        }
        var delta=new double[_volume.Length];
        foreach (var (a,b,raw) in transfers)
        {
            var c=Topology.Cells[b]; double capacity=c.Roof-c.Bed-_volume[b];
            double scale=Math.Min(1,Math.Min(_volume[a]/outgoing[a],Math.Max(0,capacity)/incoming[b]));
            double amount=raw*scale; delta[a]-=amount; delta[b]+=amount;
        }
        for (int i=0;i<_volume.Length;i++) _volume[i]=Math.Clamp(_volume[i]+delta[i],0,Topology.Cells[i].Roof-Topology.Cells[i].Bed);
        LastDrainedVolume=0;
        foreach (var (i,outlet) in Topology.Outlets)
        {
            double amount=Math.Min(Math.Max(0,Level(i)-outlet.Level),outlet.CapacityPerSecond*seconds);
            amount=Math.Min(amount,_volume[i]); _volume[i]-=amount; LastDrainedVolume+=amount;
        }
        Tick++;
    }
    public static LocalWaterSimulation ForCave(CaveSpikeField field, CancellationToken token=default)
    {
        // Simulation resolution is fixed, independent of the visual F6 comparison.
        var sampler=new CaveTerrainSampler(field,.5f);
        var sources=field.Reservoirs.Select(r=>new WaterSource(r.Id,r.X,r.Z,r.Level,r.Id=="upper-pool"?8:2));
        var topology=LocalWaterTopology.Build(sampler.Density,-14,-26,28,52,-10,16,sources,token);
        var stored=field.State.Water;
        if(stored==null && field.State.Cuts.Count>0)
        {
            // Legacy terrain-only saves start from the original natural water, not an instant fill of every new excavation.
            var original=new CaveSpikeField(new CaveSpikeState { Version=field.State.Version, Seed=field.State.Seed });
            stored=ForCave(original,token).Save();
        }
        return new(topology,stored);
    }
}






