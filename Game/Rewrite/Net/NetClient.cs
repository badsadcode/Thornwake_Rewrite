using System;
using System.Collections.Generic;

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
    /// <summary>This player's state now (null while there is none yet: the world is still loading).</summary>
    PlayerState? LocalState();
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

    public void Tick(double dt)
    {
        if (!_welcomed || _isHost) return; // the host's own state goes to its server part directly
        if ((_stateTimer -= dt) > 0) return;
        _stateTimer = .05;
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
            case Msg.Joined: { int id = r.ReadInt32(); string name = r.ReadString(); Names[id] = name; _game.PlayerJoined(id, name); break; }
            case Msg.Left: { int id = r.ReadInt32(); Names.Remove(id); _game.PlayerLeft(id); break; }
            case Msg.Players:
                for (int i = r.ReadInt32(); i > 0; i--)
                {
                    int id = r.ReadInt32();
                    var state = r.ReadState();
                    if (id != Id) _game.PlayerMoved(id, state);
                }
                break;
            case Msg.Chunks when !_isHost: _game.ApplyChunks(r.ReadChunks()); break;
            case Msg.Clock when !_isHost: _game.ApplyClock(r.ReadClock(), snap: false); break;
            case Msg.Settings when !_isHost: _game.ApplySettings(r.ReadSettings()); break;
            case Msg.DigDone: _game.DigDone(r.ReadDouble(), r.ReadDouble()); break;
        }
    }
}
