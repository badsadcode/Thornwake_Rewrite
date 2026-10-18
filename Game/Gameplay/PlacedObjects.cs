using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Thornwake.Gameplay;

/// <summary>An object in the world (a campfire, a log pile, a fallen trunk): where, which way, and its state.</summary>
public sealed class PlacedObject
{
    public long Id { get; init; }
    public required string Kind { get; init; }
    private double _x, _z;
    /// <summary>The collection that holds it (it keeps its grid of where things are up to date when it moves).</summary>
    internal PlacedObjects? Home;
    /// <summary>When it came into its collection (Near lists in that order, as before the grid).</summary>
    internal long Sequence;
    public double X { get => _x; set { if (Home == null || value == _x) { _x = value; return; } Home.Unindex(this); _x = value; Home.Index(this); } }
    public double Y { get; set; }
    public double Z { get => _z; set { if (Home == null || value == _z) { _z = value; return; } Home.Unindex(this); _z = value; Home.Index(this); } }
    public float Yaw { get; init; }
    /// <summary>Full orientation (a quaternion) for objects that move (a trunk rolls); identity otherwise.</summary>
    public float Qx { get; set; }
    public float Qy { get; set; }
    public float Qz { get; set; }
    public float Qw { get; set; } = 1;
    /// <summary>Size (a trunk: its tree's scale).</summary>
    public float Size { get; set; } = 1;
    /// <summary>The player whose PC moves it (physics, D112); 0 = nobody (it stays where it is).</summary>
    public int Owner { get; set; }
    /// <summary>Axe hits taken (a trunk being cut up).</summary>
    public int Hits { get; set; }
    /// <summary>Which one of its kind (a trunk: its tree's species, for the bark).</summary>
    public int Variant { get; set; }
    /// <summary>Minutes of fuel left (fires).</summary>
    public double Fuel { get; set; }
    public bool Lit => Fuel > 0;
    /// <summary>Items lying in it to pick up (a log pile left by a felled tree; what butchering a carcass gives).</summary>
    public List<(string Item, int Count)> Contents { get; } = new();
    /// <summary>A building piece not yet finished (D121): its materials brought so far are in <see cref="Supplied"/>.</summary>
    public bool Blueprint { get; set; }
    /// <summary>Materials brought to a blueprint (or that went into a finished piece).</summary>
    public List<(string Item, int Count)> Supplied { get; } = new();
    /// <summary>A small state: a door open (1) or shut (0).</summary>
    public int State { get; set; }
    /// <summary>What it came from: a carcass's animal kind (D120).</summary>
    public string Ref { get; set; } = "";
}

/// <summary>
/// Objects placed into the world — a world change like dug ground and felled trees (§2): the host keeps them, steps
/// them (fires burn down) and sends the changed ones; a player joining later gets them all.
/// </summary>
public sealed class PlacedObjects
{
    private readonly Dictionary<long, PlacedObject> _all = new();
    private readonly HashSet<long> _changed = new();
    private long _next = 1, _sequence;
    // Where things are, in 16 m cells (D138: animals ask what is near them ten times a second, and with a few thousand
    // pieces built by twelve players a walk over all of them was most of the host's time).
    private const double Cell = 16;
    private readonly Dictionary<(long, long), List<PlacedObject>> _grid = new();

    private static (long, long) CellOf(double x, double z) => ((long)Math.Floor(x / Cell), (long)Math.Floor(z / Cell));

    internal void Index(PlacedObject o)
    {
        var key = CellOf(o.X, o.Z);
        if (!_grid.TryGetValue(key, out var list)) _grid[key] = list = new List<PlacedObject>();
        list.Add(o);
    }

    internal void Unindex(PlacedObject o)
    {
        var key = CellOf(o.X, o.Z);
        if (_grid.TryGetValue(key, out var list) && list.Remove(o) && list.Count == 0) _grid.Remove(key);
    }

    private void Put(PlacedObject o)
    {
        if (_all.Remove(o.Id, out var old)) { Unindex(old); old.Home = null; o.Sequence = old.Sequence; }
        else o.Sequence = ++_sequence;
        _all[o.Id] = o;
        Index(o);
        o.Home = this;
    }

    private bool Take(long id)
    {
        if (!_all.Remove(id, out var o)) return false;
        Unindex(o);
        o.Home = null;
        return true;
    }

    public IReadOnlyCollection<PlacedObject> All => _all.Values;
    public PlacedObject? Get(long id) => _all.GetValueOrDefault(id);

    public PlacedObject Add(string kind, double x, double y, double z, float yaw, double fuel)
    {
        var o = new PlacedObject { Id = _next++, Kind = kind, X = x, Y = y, Z = z, Yaw = yaw, Fuel = fuel };
        Put(o);
        _changed.Add(o.Id);
        return o;
    }

    public void Remove(long id) { if (Take(id)) _changed.Add(id); }
    public void Touch(long id) => _changed.Add(id);

    /// <summary>The objects within <paramref name="reach"/> m (horizontally) of a point.</summary>
    public IEnumerable<PlacedObject> Near(double x, double z, double reach)
    {
        if (reach > 40 * Cell || !double.IsFinite(x + z + reach))
            return _all.Values.Where(o => (o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) <= reach * reach);
        var (x0, z0) = CellOf(x - reach, z - reach);
        var (x1, z1) = CellOf(x + reach, z + reach);
        var found = new List<PlacedObject>();
        for (long cz = z0; cz <= z1; cz++)
            for (long cx = x0; cx <= x1; cx++)
                if (_grid.TryGetValue((cx, cz), out var list))
                    foreach (var o in list)
                        if ((o.X - x) * (o.X - x) + (o.Z - z) * (o.Z - z) <= reach * reach) found.Add(o);
        if (found.Count > 1) found.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
        return found;
    }

    /// <summary>Fires burn their fuel (game minutes). A fire going out counts as a change (everyone sees it die).</summary>
    public void Step(double minutes)
    {
        foreach (var o in _all.Values)
        {
            if (o.Fuel <= 0) continue;
            o.Fuel = Math.Max(0, o.Fuel - minutes);
            if (o.Fuel == 0) _changed.Add(o.Id);
        }
    }

    public List<long> TakeChanged() { var l = _changed.ToList(); _changed.Clear(); return l; }

    // ---- moving objects (a trunk rolling): their owner's PC reports where they are; the host passes it on unreliably,
    // often, apart from the full (reliable) changes.
    private readonly HashSet<long> _moved = new();

    /// <summary>The owner moved it: the new place and orientation. False if it is not theirs (or gone).</summary>
    public bool Move(int player, long id, double x, double y, double z, float qx, float qy, float qz, float qw)
    {
        if (!_all.TryGetValue(id, out var o) || o.Owner != player) return false;
        o.X = x; o.Y = y; o.Z = z; o.Qx = qx; o.Qy = qy; o.Qz = qz; o.Qw = qw;
        _moved.Add(id);
        return true;
    }

    public List<long> TakeMoved() { var l = _moved.ToList(); _moved.Clear(); return l; }

    public void Clear()
    {
        foreach (var (id, o) in _all) { _changed.Add(id); o.Home = null; }
        _all.Clear(); _grid.Clear();
    }

    // ---- network: an object's full state, or its id with no kind for a removed one.
    public void Write(BinaryWriter w, long id)
    {
        w.Write(id);
        if (_all.TryGetValue(id, out var o))
        {
            w.Write(o.Kind); w.Write(o.X); w.Write(o.Y); w.Write(o.Z); w.Write(o.Yaw); w.Write(o.Fuel);
            w.Write(o.Qx); w.Write(o.Qy); w.Write(o.Qz); w.Write(o.Qw); w.Write(o.Size); w.Write(o.Owner); w.Write(o.Hits); w.Write(o.Variant);
            w.Write((byte)o.Contents.Count);
            foreach (var (item, count) in o.Contents) { w.Write(item); w.Write(count); }
            w.Write(o.Blueprint); w.Write(o.State); w.Write(o.Ref);
            w.Write((byte)o.Supplied.Count);
            foreach (var (item, count) in o.Supplied) { w.Write(item); w.Write(count); }
        }
        else w.Write("");
    }

    /// <summary>The format <see cref="Write"/> writes (saves of format 1 lack blueprints, states and refs).</summary>
    public const int Format = 2;

    /// <summary>Apply one written by the host (not counted as a change): its id, and the object or null if removed.</summary>
    public (long Id, PlacedObject? Object) Read(BinaryReader r, int format = Format)
    {
        long id = r.ReadInt64();
        string kind = r.ReadString();
        if (kind.Length == 0) { Take(id); return (id, null); }
        var o = new PlacedObject { Id = id, Kind = kind, X = r.ReadDouble(), Y = r.ReadDouble(), Z = r.ReadDouble(), Yaw = r.ReadSingle() };
        o.Fuel = r.ReadDouble();
        o.Qx = r.ReadSingle(); o.Qy = r.ReadSingle(); o.Qz = r.ReadSingle(); o.Qw = r.ReadSingle(); o.Size = r.ReadSingle();
        o.Owner = r.ReadInt32(); o.Hits = r.ReadInt32(); o.Variant = r.ReadInt32();
        for (int n = r.ReadByte(); n > 0; n--) o.Contents.Add((r.ReadString(), r.ReadInt32()));
        if (format >= 2)
        {
            o.Blueprint = r.ReadBoolean(); o.State = r.ReadInt32(); o.Ref = r.ReadString();
            for (int n = r.ReadByte(); n > 0; n--) o.Supplied.Add((r.ReadString(), r.ReadInt32()));
        }
        Put(o);
        _next = Math.Max(_next, id + 1);
        return (id, o);
    }
}
