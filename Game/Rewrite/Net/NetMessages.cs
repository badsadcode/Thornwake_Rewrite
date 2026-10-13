using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite.Net;

// Multiplayer spike (docs/GAMEPLAY_DESIGN.md §14.0): what crosses the network, as plain bytes. No Godot types here, so
// the server part (NetServer) can later run in a dedicated, headless server unchanged.

/// <summary>Message kinds: 1–9 client → server, 10+ server → client.</summary>
public enum Msg : byte
{
    Hello = 1,      // name, game version
    State = 2,      // my position, facing, motion (unreliable, ~20 a second)
    Dig = 3,        // please change the ground (the server applies it and sends the changed chunks to everyone)
    Welcome = 10,   // your id, the world, its settings, the clock, the players, the place to start
    Players = 11,   // everyone's state (unreliable, ~20 a second)
    Joined = 12,    // a player came in (id, name)
    Left = 13,      // a player left (id)
    Chunks = 14,    // terrain edit chunks (changed ones; all of them for a player joining late)
    Clock = 15,     // the host's time, ocean time, wind time and cloud drift (once a second)
    Settings = 16,  // world-affecting settings (on joining and whenever the host changes one)
    DigDone = 17,   // the result of your dig (volume moved, hardness)
    Refused = 18,   // you cannot join (reason)
}

/// <summary>One player's movement as others need it to draw them (world coordinates, not the floating origin).</summary>
public readonly record struct PlayerState(double X, double Y, double Z, float Facing, float Vx, float Vy, float Vz, PlayerFlags Flags);

[Flags]
public enum PlayerFlags : byte { None = 0, Flying = 1, Torch = 2, Swimming = 4 }

/// <summary>
/// Everything about time that each PC would otherwise count up on its own: the day and its phase (GameClock), the
/// waves' clock (Atmosphere.OceanTime), the gusts' clock (Wind) and the clouds' accumulated drift (Atmosphere.CloudDrift).
/// </summary>
public readonly record struct ClockState(int Day, double Phase, double OceanTime, double WindTime, double[] CloudDrift);

/// <summary>A dig as the client's tool resolved it (TerrainSculpt.Apply's arguments), for the server to apply.</summary>
public readonly record struct DigRequest(BrushMode Mode, BrushShape Shape, double Radius, double Strength, double RockFactor, double Budget,
    double X, double Y, double Z, float AimX, float AimY, float AimZ);

public static class NetMessages
{
    /// <summary>Changes whenever the messages change: a client and host with different versions do not connect.</summary>
    public const int Protocol = 1;

    public static byte[] Build(Msg kind, Action<BinaryWriter> body)
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            w.Write((byte)kind);
            body(w);
        }
        return stream.ToArray();
    }

    public static (Msg Kind, BinaryReader Reader) Open(byte[] data)
    {
        var r = new BinaryReader(new MemoryStream(data), Encoding.UTF8);
        return ((Msg)r.ReadByte(), r);
    }

    public static void Write(this BinaryWriter w, in PlayerState s)
    {
        w.Write(s.X); w.Write(s.Y); w.Write(s.Z); w.Write(s.Facing);
        w.Write(s.Vx); w.Write(s.Vy); w.Write(s.Vz); w.Write((byte)s.Flags);
    }

    public static PlayerState ReadState(this BinaryReader r) =>
        new(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), (PlayerFlags)r.ReadByte());

    public static void Write(this BinaryWriter w, in ClockState c)
    {
        w.Write(c.Day); w.Write(c.Phase); w.Write(c.OceanTime); w.Write(c.WindTime);
        w.Write((byte)c.CloudDrift.Length);
        foreach (double v in c.CloudDrift) w.Write(v);
    }

    public static ClockState ReadClock(this BinaryReader r)
    {
        int day = r.ReadInt32(); double phase = r.ReadDouble(), ocean = r.ReadDouble(), wind = r.ReadDouble();
        var drift = new double[r.ReadByte()];
        for (int i = 0; i < drift.Length; i++) drift[i] = r.ReadDouble();
        return new ClockState(day, phase, ocean, wind, drift);
    }

    public static void Write(this BinaryWriter w, in DigRequest d)
    {
        w.Write((byte)d.Mode); w.Write((byte)d.Shape); w.Write(d.Radius); w.Write(d.Strength); w.Write(d.RockFactor); w.Write(d.Budget);
        w.Write(d.X); w.Write(d.Y); w.Write(d.Z); w.Write(d.AimX); w.Write(d.AimY); w.Write(d.AimZ);
    }

    public static DigRequest ReadDig(this BinaryReader r) =>
        new((BrushMode)r.ReadByte(), (BrushShape)r.ReadByte(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(),
            r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());

    public static void WriteSettings(this BinaryWriter w, IReadOnlyList<(string Key, double Value)> settings)
    {
        w.Write(settings.Count);
        foreach (var (key, value) in settings) { w.Write(key); w.Write(value); }
    }

    public static List<(string Key, double Value)> ReadSettings(this BinaryReader r)
    {
        var list = new List<(string, double)>();
        for (int i = r.ReadInt32(); i > 0; i--) list.Add((r.ReadString(), r.ReadDouble()));
        return list;
    }

    /// <summary>Terrain edit chunks: key, then 32 × 32 offsets or none (the chunk holds no edits any more).</summary>
    public static void WriteChunks(this BinaryWriter w, IReadOnlyList<(long X, long Z, float[]? Offsets)> chunks)
    {
        w.Write(chunks.Count);
        foreach (var (x, z, offsets) in chunks)
        {
            w.Write(x); w.Write(z); w.Write(offsets != null);
            if (offsets != null) foreach (float v in offsets) w.Write(v);
        }
    }

    public static List<(long X, long Z, float[]? Offsets)> ReadChunks(this BinaryReader r)
    {
        var list = new List<(long, long, float[]?)>();
        for (int i = r.ReadInt32(); i > 0; i--)
        {
            long x = r.ReadInt64(), z = r.ReadInt64();
            float[]? offsets = null;
            if (r.ReadBoolean())
            {
                offsets = new float[TerrainEdits.ChunkVertices * TerrainEdits.ChunkVertices];
                for (int k = 0; k < offsets.Length; k++) offsets[k] = r.ReadSingle();
            }
            list.Add((x, z, offsets));
        }
        return list;
    }
}
