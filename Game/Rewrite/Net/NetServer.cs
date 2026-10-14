using System;
using System.Collections.Generic;
using System.Linq;
using Thornwake.Gameplay;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite.Net;

/// <summary>
/// What the server part needs from the world it runs (multiplayer spike, GAMEPLAY_DESIGN.md §14.0). The host's
/// WorldLab implements it today; a dedicated server would implement it headless (no drawing) later.
/// </summary>
public interface IWorldHost
{
    /// <summary>The world as command-line arguments (preset, seed, every generation parameter: WorldLab's dump line).</summary>
    string WorldArgs { get; }
    /// <summary>A player-made world type (JSON) the world uses, or null for a built-in one.</summary>
    string? WorldTypeJson { get; }
    /// <summary>The world-affecting settings (time, weather, wind, sea, clouds…), not the graphics ones.</summary>
    IReadOnlyList<(string Key, double Value)> WorldSettings();
    ClockState Clock();
    /// <summary>The world's terrain edits (null while the world is still being generated).</summary>
    TerrainEdits? Edits { get; }
    /// <summary>Apply a player's dig to the true world; the changed chunks then go to everyone.</summary>
    (double Volume, double Hardness) Dig(in DigRequest request);
    /// <summary>Generated objects taken out of the world (felled trees; null while the world is being generated).</summary>
    ObjectRemovals? Removals { get; }
    /// <summary>Step 1's rules: inventories, placed objects, actions (D110).</summary>
    GameRules? Rules { get; }
}

/// <summary>
/// The server part: the true state — who is connected and where, the world changes — checks what clients send and tells
/// everyone. Transport-free: <c>send</c>(peer, bytes, reliable) puts bytes on the wire (NetSession: ENet; the host's own
/// player is peer 1 and is reached without the network). Only messages, no drawing, no Godot.
/// </summary>
public sealed class NetServer
{
    public const long HostPeer = 1;
    private readonly IWorldHost _world;
    private readonly Action<long, byte[], bool> _send;
    private readonly string _version;
    private readonly Dictionary<long, Player> _players = new();
    private double _stateTimer, _clockTimer, _settingsTimer, _chunkTimer;

    private sealed class Player
    {
        public string Name = "";
        public bool Welcomed;
        public PlayerState State;
        public bool HasState;
    }

    public NetServer(IWorldHost world, Action<long, byte[], bool> send, string version)
    {
        _world = world; _send = send; _version = version;
    }

    /// <summary>Notable things the server decided (digs applied or refused…), for the network log.</summary>
    public Action<string>? Log { get; set; }

    public int PlayerCount => _players.Count(p => p.Value.Welcomed);
    public IEnumerable<string> PlayerNames => _players.Values.Where(p => p.Welcomed).Select(p => p.Name);

    public void Connected(long peer) => _players[peer] = new Player();

    public void Disconnected(long peer)
    {
        _world.Rules?.Release((int)peer); // (what their PC moved stays where it is)
        if (!_players.Remove(peer, out var gone) || !gone.Welcomed) return;
        Broadcast(NetMessages.Build(Msg.Left, w => w.Write((int)peer)), true, except: peer);
    }

    public void Receive(long peer, byte[] data)
    {
        if (!_players.TryGetValue(peer, out var player)) return;
        var (kind, r) = NetMessages.Open(data);
        switch (kind)
        {
            case Msg.Hello:
            {
                int protocol = r.ReadInt32();
                string version = r.ReadString(), name = r.ReadString();
                if (protocol != NetMessages.Protocol || version != _version)
                {
                    _send(peer, NetMessages.Build(Msg.Refused, w => w.Write($"Different game versions: the host runs {_version}, you run {version}.")), true);
                    return;
                }
                player.Name = Clean(name, peer);
                player.Welcomed = true;
                Welcome(peer);
                Broadcast(NetMessages.Build(Msg.Joined, w => { w.Write((int)peer); w.Write(player.Name); }), true, except: peer);
                break;
            }
            case Msg.State when player.Welcomed:
                player.State = r.ReadState();
                player.HasState = true;
                break;
            case Msg.Dig when player.Welcomed && _world.Edits != null:
            {
                var dig = r.ReadDig();
                // Checks: a sane brush, and digging near where the player stands (no reaching across the world). (A dig can
                // arrive before the player's first position — reliable and unreliable messages travel separately.)
                if (dig.Radius is <= 0 or > 8 || dig.Strength is <= 0 or > 2) { Log?.Invoke(FormattableString.Invariant($"dig from {player.Name} refused: brush {dig.Radius:0.##} m, strength {dig.Strength:0.##}")); break; }
                double dx = dig.X - player.State.X, dz = dig.Z - player.State.Z;
                if (player.HasState && dx * dx + dz * dz > 420 * 420) { Log?.Invoke(FormattableString.Invariant($"dig from {player.Name} refused: {Math.Sqrt(dx * dx + dz * dz):0} m from them")); break; }
                var (volume, hardness) = _world.Dig(dig);
                Log?.Invoke(FormattableString.Invariant($"dig from {player.Name} at {dig.X:0.0}, {dig.Z:0.0}: {volume:0.###} m³ moved"));
                _send(peer, NetMessages.Build(Msg.DigDone, w => { w.Write(volume); w.Write(hardness); }), true);
                SendChanges(); // at once: the digger sees the hole without waiting for the next tick
                break;
            }
            case Msg.Move when player.Welcomed && _world.Rules is { } moving:
            {
                long id = r.ReadInt64(); double x = r.ReadDouble(), y = r.ReadDouble(), z = r.ReadDouble();
                float qx = r.ReadSingle(), qy = r.ReadSingle(), qz = r.ReadSingle(), qw = r.ReadSingle();
                if (double.IsFinite(x + y + z)) moving.Objects.Move((int)peer, id, x, y, z, qx, qy, qz, qw);
                break;
            }
            case Msg.Action when player.Welcomed && _world.Rules is { } rules && _world.Removals != null:
            {
                var a = r.ReadAction();
                // The rules check the player's reach from where the server knows them to be.
                ActionResult result;
                if (!player.HasState) result = ActionResult.Fail("Not in the world yet.");
                else
                {
                    var at = player.State; int id = (int)peer;
                    result = a.Kind switch
                    {
                        ActionKind.Chop => rules.Chop(id, a.A, a.B, at.X, at.Z),
                        ActionKind.Make => rules.Make(id, a.Id, at.X, at.Z, a.X, a.Y, a.Z, a.Yaw),
                        ActionKind.Fuel => rules.Fuel(id, a.A, at.X, at.Z),
                        ActionKind.Eat => rules.Eat(id, a.Id),
                        ActionKind.Gather => rules.Gather(id, a.A, at.X, at.Z),
                        ActionKind.ChopTrunk => rules.ChopTrunk(id, a.A, at.X, at.Z),
                        ActionKind.Claim => new ActionResult(rules.Claim(id, a.A, at.X, at.Z), ""),
                        ActionKind.Forage => rules.Forage(id, a.Id, a.A, a.B, a.X, a.Z, at.X, at.Z),
                        ActionKind.Catch => rules.Catch(id, a.Id, a.X, at.X, at.Z),
                        ActionKind.Die => rules.Die(id, at.X, at.Z),
                        _ => ActionResult.Fail("Unknown action."),
                    };
                }
                if ((peer != HostPeer || !result.Ok) && result.Message.Length > 0) Log?.Invoke($"{a.Kind} from {player.Name}: {result.Message}");
                _send(peer, NetMessages.Build(Msg.Result, w => { w.Write((byte)a.Kind); w.Write(result.Ok); w.Write(result.Message); w.Write(result.Food); }), true);
                SendChanges();
                break;
            }
        }
    }

    /// <summary>Per frame: everyone's positions ~20 a second, edits as they change, the clock every second, the settings every few.</summary>
    public void Tick(double dt, PlayerState? hostState)
    {
        if (hostState is { } hs)
        {
            if (!_players.TryGetValue(HostPeer, out var host)) _players[HostPeer] = host = new Player { Name = "Host", Welcomed = true };
            host.State = hs; host.HasState = true;
        }
        if ((_chunkTimer -= dt) <= 0) { _chunkTimer = .05; SendChanges(); }
        if ((_stateTimer -= dt) <= 0)
        {
            _stateTimer = Math.Max(_stateTimer + .05, 0); // (20 a second on average, at most one a frame)
            var moving = _players.Where(p => p.Value.Welcomed && p.Value.HasState).ToList();
            if (moving.Count > 1)
                Broadcast(NetMessages.Build(Msg.Players, w =>
                {
                    w.Write(moving.Count);
                    foreach (var (id, p) in moving) { w.Write((int)id); w.Write(p.State); }
                }), false);
        }
        if ((_clockTimer -= dt) <= 0) { _clockTimer = 1; Broadcast(NetMessages.Build(Msg.Clock, w => w.Write(_world.Clock())), false, except: HostPeer); }
        if ((_settingsTimer -= dt) <= 0) { _settingsTimer = 5; SettingsChanged(); }
    }

    /// <summary>The host changed a world-affecting setting: everyone takes it.</summary>
    public void SettingsChanged() =>
        Broadcast(NetMessages.Build(Msg.Settings, w => w.WriteSettings(_world.WorldSettings())), true, except: HostPeer);

    /// <summary>The host started another world: everyone builds it; the edits of the old one are gone.</summary>
    public void WorldChanged(PlayerState hostPlace)
    {
        _world.Edits?.TakeChanged();
        var spawn = (PlayerState?)hostPlace;
        if (_players.TryGetValue(HostPeer, out var host)) host.State = hostPlace;
        Broadcast(NetMessages.Build(Msg.World, w =>
        {
            w.Write(_world.WorldArgs);
            w.Write(_world.WorldTypeJson ?? "");
            w.Write(spawn != null);
            if (spawn is { } s) w.Write(s);
        }), true, except: HostPeer);
    }

    /// <summary>The host's own player is named after the host (shown above them to the others).</summary>
    public void SetHostName(string name)
    {
        if (!_players.TryGetValue(HostPeer, out var host)) _players[HostPeer] = host = new Player { Welcomed = true };
        host.Name = Clean(name, HostPeer);
    }

    private void Welcome(long peer)
    {
        var others = _players.Where(p => p.Key != peer && p.Value.Welcomed).ToList();
        var spawn = _players.TryGetValue(HostPeer, out var host) && host.HasState ? host.State : (PlayerState?)null;
        _send(peer, NetMessages.Build(Msg.Welcome, w =>
        {
            w.Write((int)peer);
            w.Write(_world.WorldArgs);
            w.Write(_world.WorldTypeJson ?? "");
            w.WriteSettings(_world.WorldSettings());
            w.Write(_world.Clock());
            w.Write(spawn != null);
            if (spawn is { } s) w.Write(s);
            w.Write(others.Count);
            foreach (var (id, p) in others) { w.Write((int)id); w.Write(p.Name); }
        }), true);
        // Felled trees and placed objects so far; their inventory follows with the next changes (a new one: the start kit).
        if (_world.Removals is { } removals && removals.Count > 0)
            _send(peer, NetMessages.Build(Msg.Removals, w => w.WriteRemovals(removals, removals.Keys())), true);
        if (_world.Rules is { } rules)
        {
            var objects = rules.Objects.All.Select(o => o.Id).ToList();
            if (objects.Count > 0) _send(peer, NetMessages.Build(Msg.Objects, w => { w.Write(objects.Count); foreach (long id in objects) rules.Objects.Write(w, id); }), true);
            rules.Inventory((int)peer);
            _send(peer, NetMessages.Build(Msg.Inventory, w => rules.Inventory((int)peer).Write(w)), true);
        }
        // Every change made before this player came (late joining), a few hundred chunks per message.
        if (_world.Edits is { } edits)
        {
            var all = edits.ChunkKeys().Select(k => (k.X, k.Z, edits.GetChunk(k.X, k.Z))).ToList();
            for (int i = 0; i < all.Count; i += 200)
            {
                var part = all.Skip(i).Take(200).ToList();
                _send(peer, NetMessages.Build(Msg.Chunks, w => w.WriteChunks(part)), true);
            }
        }
    }

    private void SendChanges()
    {
        // Felled trees and placed objects to everyone (the host's own player too: it draws from what it receives),
        // inventories to their owners.
        if (_world.Removals is { } removals && removals.TakeChanged() is { Count: > 0 } keys)
            Broadcast(NetMessages.Build(Msg.Removals, w => w.WriteRemovals(removals, keys)), true);
        if (_world.Rules is { } rules)
        {
            if (rules.TakeTreeEvents() is { Count: > 0 } hits)
                Broadcast(NetMessages.Build(Msg.TreeHits, w =>
                {
                    w.Write(hits.Count);
                    foreach (var e in hits) { w.Write(e.Cx); w.Write(e.Cz); w.Write((short)e.HitsLeft); w.Write((short)e.Total); w.Write(e.Felled); w.Write((float)e.DirX); w.Write((float)e.DirZ); }
                }), true);
            // Moving objects: where their owners' PCs put them (everyone; an owner ignores its own).
            if (rules.Objects.TakeMoved() is { Count: > 0 } moved)
                Broadcast(NetMessages.Build(Msg.Moves, w =>
                {
                    var list = moved.Select(rules.Objects.Get).Where(o => o != null).ToList();
                    w.Write(list.Count);
                    foreach (var o in list) { w.Write(o!.Id); w.Write(o.X); w.Write(o.Y); w.Write(o.Z); w.Write(o.Qx); w.Write(o.Qy); w.Write(o.Qz); w.Write(o.Qw); }
                }), false);
            if (rules.Objects.TakeChanged() is { Count: > 0 } ids)
                Broadcast(NetMessages.Build(Msg.Objects, w => { w.Write(ids.Count); foreach (long id in ids) rules.Objects.Write(w, id); }), true);
            foreach (int owner in rules.TakeInventoryChanges())
            {
                if (_players.TryGetValue(owner, out var p) && p.Welcomed)
                    _send(owner, NetMessages.Build(Msg.Inventory, w => rules.Inventory(owner).Write(w)), true);
                else if (owner == HostPeer || p != null) rules.InventoryChanged(owner); // (not in yet — the host's own player
                                                                                        // before its first frame: later)
            }
        }
        if (_world.Edits is not { } edits) return;
        var changed = edits.TakeChanged();
        if (changed.Count == 0) return;
        var chunks = changed.Select(k => (k.X, k.Z, edits.GetChunk(k.X, k.Z))).ToList();
        for (int i = 0; i < chunks.Count; i += 200)
        {
            var part = chunks.Skip(i).Take(200).ToList();
            Broadcast(NetMessages.Build(Msg.Chunks, w => w.WriteChunks(part)), true, except: HostPeer); // the host holds them already
        }
    }

    private void Broadcast(byte[] data, bool reliable, long except = 0)
    {
        foreach (var (id, p) in _players)
            if (p.Welcomed && id != except) _send(id, data, reliable);
    }

    private static string Clean(string name, long peer)
    {
        name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
        return name.Length == 0 ? $"Player {peer}" : name.Length > 24 ? name[..24] : name;
    }
}
