using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Thornwake.WorldGen;

namespace Thornwake.Gameplay;

/// <summary>One player in a save: what they carry (the inventory's bytes) and, for the host's own player, where they
/// were and how they were (needs live on each player's PC: a joined player's come back fresh).</summary>
public sealed class PlayerSave
{
    public byte[] Inventory = Array.Empty<byte>();
    public bool HasBody;
    public double X, Y, Z, Food, Warmth, Wet, Health;
    public float Facing;
}

/// <summary>
/// A saved world (D116): the world itself is its generation arguments (it regenerates the same from the seed); on top
/// of it every change — terrain edits, generated objects taken out (felled trees, picked stones and berries), placed
/// objects (fires, trunks, piles, lean-tos) — the world's rules (world-affecting settings), the clock and the clouds'
/// drift, and each player by name. One binary file; engine-free (tested headless). The host saves; a joined player
/// does not.
/// </summary>
public sealed class WorldSave
{
    public const int Version = 1;
    private static readonly byte[] Magic = Encoding.ASCII.GetBytes("TWSAVE");

    public string WorldArgs = "";
    public string? WorldTypeJson;
    public List<(string Key, double Value)> Settings = new();
    public int Day = 1;
    public double Phase;
    public double[] CloudDrift = Array.Empty<double>();
    public string Label = "";
    public DateTime Saved = DateTime.UtcNow;
    public List<(long X, long Z, float[] Offsets)> Edits = new();
    public List<(int Layer, long X, long Z, double At)> Removals = new();
    public byte[] Objects = Array.Empty<byte>();
    public Dictionary<string, PlayerSave> Players = new();

    /// <summary>Take the changes of a running world.</summary>
    public void Capture(TerrainEdits edits, ObjectRemovals removals, PlacedObjects objects)
    {
        Edits = edits.ChunkKeys().Select(k => (k.X, k.Z, edits.GetChunk(k.X, k.Z))).Where(c => c.Item3 != null).Select(c => (c.X, c.Z, c.Item3!)).ToList();
        Removals = removals.Keys().Select(k => (k.Layer, k.X, k.Z, removals.Get(k.Layer, k.X, k.Z) ?? 0)).ToList();
        using var ms = new MemoryStream();
        using (var w = new BinaryWriter(ms, Encoding.UTF8, true))
        {
            var ids = objects.All.Select(o => o.Id).ToList();
            w.Write(ids.Count);
            foreach (long id in ids) objects.Write(w, id);
        }
        Objects = ms.ToArray();
    }

    /// <summary>Put the changes into a freshly generated world (nothing counted as changed: nothing to send).</summary>
    public void Restore(TerrainEdits edits, ObjectRemovals removals, PlacedObjects objects)
    {
        foreach (var (x, z, offsets) in Edits) edits.SetChunk(x, z, offsets);
        foreach (var (layer, x, z, at) in Removals) removals.Set(layer, x, z, at);
        objects.Clear(); objects.TakeChanged();
        using var r = new BinaryReader(new MemoryStream(Objects), Encoding.UTF8);
        for (int n = Objects.Length > 0 ? r.ReadInt32() : 0; n > 0; n--) objects.Read(r);
    }

    public void Write(Stream stream)
    {
        using var w = new BinaryWriter(stream, Encoding.UTF8, true);
        w.Write(Magic); w.Write(Version);
        w.Write(Label); w.Write(Saved.ToBinary());
        w.Write(WorldArgs); w.Write(WorldTypeJson ?? "");
        w.Write(Settings.Count); foreach (var (k, v) in Settings) { w.Write(k); w.Write(v); }
        w.Write(Day); w.Write(Phase);
        w.Write(CloudDrift.Length); foreach (double d in CloudDrift) w.Write(d);
        w.Write(Edits.Count);
        foreach (var (x, z, offsets) in Edits) { w.Write(x); w.Write(z); w.Write(offsets.Length); foreach (float f in offsets) w.Write(f); }
        w.Write(Removals.Count); foreach (var (layer, x, z, at) in Removals) { w.Write(layer); w.Write(x); w.Write(z); w.Write(at); }
        w.Write(Objects.Length); w.Write(Objects);
        w.Write(Players.Count);
        foreach (var (name, p) in Players)
        {
            w.Write(name); w.Write(p.Inventory.Length); w.Write(p.Inventory);
            w.Write(p.HasBody); w.Write(p.X); w.Write(p.Y); w.Write(p.Z); w.Write(p.Facing);
            w.Write(p.Food); w.Write(p.Warmth); w.Write(p.Wet); w.Write(p.Health);
        }
    }

    public static WorldSave Read(Stream stream)
    {
        using var r = new BinaryReader(stream, Encoding.UTF8, true);
        if (!r.ReadBytes(Magic.Length).SequenceEqual(Magic)) throw new InvalidDataException("not a Thornwake save");
        int version = r.ReadInt32();
        if (version != Version) throw new InvalidDataException($"save version {version}, this game reads {Version}");
        var s = new WorldSave { Label = r.ReadString(), Saved = DateTime.FromBinary(r.ReadInt64()), WorldArgs = r.ReadString() };
        string type = r.ReadString(); s.WorldTypeJson = type.Length > 0 ? type : null;
        for (int n = r.ReadInt32(); n > 0; n--) s.Settings.Add((r.ReadString(), r.ReadDouble()));
        s.Day = r.ReadInt32(); s.Phase = r.ReadDouble();
        s.CloudDrift = new double[r.ReadInt32()]; for (int i = 0; i < s.CloudDrift.Length; i++) s.CloudDrift[i] = r.ReadDouble();
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            long x = r.ReadInt64(), z = r.ReadInt64();
            var offsets = new float[r.ReadInt32()]; for (int i = 0; i < offsets.Length; i++) offsets[i] = r.ReadSingle();
            s.Edits.Add((x, z, offsets));
        }
        for (int n = r.ReadInt32(); n > 0; n--) s.Removals.Add((r.ReadInt32(), r.ReadInt64(), r.ReadInt64(), r.ReadDouble()));
        s.Objects = r.ReadBytes(r.ReadInt32());
        for (int n = r.ReadInt32(); n > 0; n--)
        {
            string name = r.ReadString();
            var p = new PlayerSave { Inventory = r.ReadBytes(r.ReadInt32()), HasBody = r.ReadBoolean(), X = r.ReadDouble(), Y = r.ReadDouble(), Z = r.ReadDouble(), Facing = r.ReadSingle(),
                Food = r.ReadDouble(), Warmth = r.ReadDouble(), Wet = r.ReadDouble(), Health = r.ReadDouble() };
            s.Players[name] = p;
        }
        return s;
    }
}
