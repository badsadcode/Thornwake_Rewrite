using System;
using System.Collections.Generic;
using System.IO;

namespace Thornwake.Rewrite.Net;

/// <summary>What the client part drives in the game it shows (WorldLab implements it).</summary>
public interface IClientGame
{
    /// <summary>Build the host's world (a joining client only); start near <paramref name="spawn"/> if given.</summary>
    void LoadWorld(string worldArgs, string? worldTypeJson, PlayerState? spawn);
    void ApplySettings(IReadOnlyList<(string Key, double Value)> settings);
    /// <summary>The host's clock; <paramref name="snap"/> on joining, otherwise ease toward it.</summary>
    void ApplyClock(in ClockState clock, bool snap);
    void ApplyChunks(IReadOnlyList<(long X, long Z, float[]? Offsets)> chunks);
    void PlayerJoined(int id, string name);
    void PlayerLeft(int id);
    void PlayerMoved(int id, in PlayerState state);
    void DigDone(double volume, double hardness);
    void Refused(string reason);
    /// <summary>The host's answer to this player's action (done or why not; food eaten).</summary>
    void ActionDone(ActionKind kind, bool ok, string message, double food);
    /// <summary>This player's inventory as the host has it.</summary>
    void ApplyInventory(BinaryReader r);
    /// <summary>Placed objects that changed (count, then each one as PlacedObjects.Write wrote it).</summary>
    void ApplyObjects(BinaryReader r);
    /// <summary>Generated objects taken out or put back (felled trees).</summary>
    void ApplyRemovals(IReadOnlyList<(int Layer, long X, long Z, double? RemovedAt)> removals);
    /// <summary>A tree was hit (shake it) or felled (it falls that way).</summary>
    void TreeHit(in Thornwake.Gameplay.TreeEvent e);
    /// <summary>A moving object (a trunk) is here now (its owner's PC says so).</summary>
    void ObjectMoved(long id, double x, double y, double z, float qx, float qy, float qz, float qw);
    /// <summary>This player's state now (null while there is none yet: the world is still loading).</summary>
    PlayerState? LocalState();
    /// <summary>The animals near this player (Wildlife.Write's format; D120).</summary>
    void ApplyAnimals(BinaryReader r);
    /// <summary>An animal hurt this player.</summary>
    void Hurt(double damage, double fromX, double fromZ, string who);
    /// <summary>Another player loosed an arrow: draw its flight.</summary>
    void ArrowShot(int player, double x, double y, double z, float vx, float vy, float vz);
}

/// <summary>
/// The client part: receives the host's world, settings, clock, edits and the other players and hands them to the game;
/// sends this player's position ~20 times a second and dig requests. On the host it runs too (for the other players
/// and their names), but the world, settings, clock and edits it already owns are not applied again.
/// </summary>
public sealed class NetClient
{
    private readonly IClientGame _game;
    private readonly Action<byte[], bool> _send;
    private readonly bool _isHost;
    private double _stateTimer;
    private bool _welcomed;

    public int Id { get; private set; }
    public bool Welcomed => _welcomed;
    public readonly Dictionary<int, string> Names = new();

    public NetClient(IClientGame game, Action<byte[], bool> send, bool isHost)
    {
        _game = game; _send = send; _isHost = isHost;
        if (isHost) { Id = (int)NetServer.HostPeer; _welcomed = true; }
    }

    /// <summary>Introduce this player (a joining client, once connected).</summary>
    public void Hello(string name, string version) =>
        _send(NetMessages.Build(Msg.Hello, w => { w.Write(NetMessages.Protocol); w.Write(version); w.Write(name); }), true);

    public void SendDig(in DigRequest dig)
    {
        var d = dig;
        _send(NetMessages.Build(Msg.Dig, w => w.Write(d)), true);
    }

    /// <summary>Where an object this PC moves is now (unreliable).</summary>
    public void SendMove(long id, double x, double y, double z, float qx, float qy, float qz, float qw) =>
        _send(NetMessages.Build(Msg.Move, w => { w.Write(id); w.Write(x); w.Write(y); w.Write(z); w.Write(qx); w.Write(qy); w.Write(qz); w.Write(qw); }), false);

    /// <summary>An arrow loosed here (the others draw it; unreliable).</summary>
    public void SendShot(double x, double y, double z, float vx, float vy, float vz) =>
        _send(NetMessages.Build(Msg.Shot, w => { w.Write(x); w.Write(y); w.Write(z); w.Write(vx); w.Write(vy); w.Write(vz); }), false);

    public void SendAction(in ActionRequest action)
    {
        var a = action;
        _send(NetMessages.Build(Msg.Action, w => w.Write(a)), true);
    }

    public void Tick(double dt)
    {
        if (!_welcomed || _isHost) return; // the host's own state goes to its server part directly
        if ((_stateTimer -= dt) > 0) return;
        _stateTimer = Math.Max(_stateTimer + .05, 0); // (20 a second on average, at most one a frame)
        if (_game.LocalState() is { } s) _send(NetMessages.Build(Msg.State, w => w.Write(s)), false);
    }

    public void Receive(byte[] data)
    {
        var (kind, r) = NetMessages.Open(data);
        switch (kind)
        {
            case Msg.Welcome:
            {
                Id = r.ReadInt32();
                string args = r.ReadString(), type = r.ReadString();
                var settings = r.ReadSettings();
                var clock = r.ReadClock();
                PlayerState? spawn = r.ReadBoolean() ? r.ReadState() : null;
                for (int i = r.ReadInt32(); i > 0; i--) { int id = r.ReadInt32(); string name = r.ReadString(); Names[id] = name; _game.PlayerJoined(id, name); }
                _welcomed = true;
                _game.ApplySettings(settings);
                _game.LoadWorld(args, type.Length > 0 ? type : null, spawn);
                _game.ApplyClock(clock, snap: true);
                break;
            }
            case Msg.Refused: _game.Refused(r.ReadString()); break;
            case Msg.World when !_isHost:
            {
                string args = r.ReadString(), type = r.ReadString();
                PlayerState? spawn = r.ReadBoolean() ? r.ReadState() : null;
                _game.LoadWorld(args, type.Length > 0 ? type : null, spawn);
                break;
            }
            case Msg.Joined: { int id = r.ReadInt32(); string name = r.ReadString(); Names[id] = name; _game.PlayerJoined(id, name); break; }
            case Msg.Left: { int id = r.ReadInt32(); Names.Remove(id); _game.PlayerLeft(id); break; }
            case Msg.Players:
                foreach (var (id, state) in r.ReadPlayers())
                    if (id != Id) _game.PlayerMoved(id, state);
                break;
            case Msg.Chunks when !_isHost: _game.ApplyChunks(r.ReadChunks()); break;
            case Msg.Clock when !_isHost: _game.ApplyClock(r.ReadClock(), snap: false); break;
            case Msg.Settings when !_isHost: _game.ApplySettings(r.ReadSettings()); break;
            case Msg.DigDone: _game.DigDone(r.ReadDouble(), r.ReadDouble()); break;
            // (also on the host: its own player draws objects and its inventory from what the server part sends)
            case Msg.Result: { var k = (ActionKind)r.ReadByte(); bool ok = r.ReadBoolean(); string m = r.ReadString(); _game.ActionDone(k, ok, m, r.ReadDouble()); break; }
            case Msg.Inventory: _game.ApplyInventory(r); break;
            case Msg.Objects: _game.ApplyObjects(r); break;
            case Msg.Removals: _game.ApplyRemovals(r.ReadRemovals()); break;
            case Msg.TreeHits:
                for (int n = r.ReadInt32(); n > 0; n--)
                    _game.TreeHit(new Thornwake.Gameplay.TreeEvent(r.ReadInt64(), r.ReadInt64(), r.ReadInt16(), r.ReadInt16(), r.ReadBoolean(), r.ReadSingle(), r.ReadSingle()));
                break;
            case Msg.Animals: _game.ApplyAnimals(r); break;
            case Msg.Hurt: _game.Hurt(r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadString()); break;
            case Msg.Shots:
            {
                int who = r.ReadInt32();
                if (who != Id) _game.ArrowShot(who, r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                break;
            }
            case Msg.Moves:
                for (int n = r.ReadInt32(); n > 0; n--)
                    _game.ObjectMoved(r.ReadInt64(), r.ReadDouble(), r.ReadDouble(), r.ReadDouble(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle(), r.ReadSingle());
                break;
        }
    }
}
