using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Thornwake.Rewrite.Net;

/// <summary>
/// The connection (multiplayer spike, GAMEPLAY_DESIGN.md §14.0): Godot's ENet (UDP) with raw byte packets. A host runs
/// the server part (NetServer) and a client part for its own player, joined without the network; a joining player runs
/// only a client part. Channel 0 carries reliable messages (welcome, edits, settings, digs), channel 1 the frequent
/// unreliable ones (positions, the clock): a lost position is replaced by the next one.
/// </summary>
public sealed partial class NetSession : Node
{
    public const int DefaultPort = 7777;
    public NetServer? Server { get; private set; }
    public NetClient Client { get; private set; } = null!;
    public bool IsHost => Server != null;
    public string Status { get; private set; } = "";
    public NetStats Stats { get; } = new();
    private ENetMultiplayerPeer? _peer;
    private SceneMultiplayer _mp = null!;
    private Func<PlayerState?> _hostState = () => null;
    private string _name = "", _version = "";
    private readonly System.Diagnostics.Stopwatch _watch = new();

    /// <summary>Host on <paramref name="port"/>: the server part plus this player's client part.</summary>
    public Error Host(int port, IWorldHost world, IClientGame game, string name, string version)
    {
        Setup(name, version);
        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateServer(port, 32);
        Stats.Role = $"host {name} port {port}";
        if (error != Error.Ok) { Status = $"Could not host on port {port}: {error} (is another game hosting already?)"; Stats.Event(Status); return error; }
        _mp.MultiplayerPeer = _peer;
        Server = new NetServer(world, SendFromServer, version);
        Server.SetHostName(name);
        Server.Log = Stats.Event;
        Client = new NetClient(game, (data, _) => Server.Receive(NetServer.HostPeer, data), isHost: true);
        _hostState = game.LocalState;
        _mp.PeerConnected += id => { Stats.Event($"peer #{id} connected"); Server.Connected(id); };
        _mp.PeerDisconnected += id => { Stats.Event($"peer #{id} ({Client.Names.GetValueOrDefault((int)id, "?")}) disconnected"); Server.Disconnected(id); };
        Status = $"Hosting on UDP port {port}";
        Stats.Event(Status + $" (version {version})");
        return Error.Ok;
    }

    /// <summary>Join a host at <paramref name="address"/>:<paramref name="port"/>.</summary>
    public Error Join(string address, int port, IClientGame game, string name, string version)
    {
        Setup(name, version);
        // The client part first: the game asks it about the join even when the connection could not be made (a friend's
        // game crashed there on an address it could not use).
        Client = new NetClient(game, (data, reliable) => Send(NetServer.HostPeer, data, reliable), isHost: false);
        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateClient(address, port);
        Stats.Role = $"client {name} to {address}:{port}";
        if (error != Error.Ok) { _peer = null; Status = $"Could not connect to {address}:{port}: {error}"; Stats.Event(Status); return error; }
        _mp.MultiplayerPeer = _peer;
        _mp.ConnectedToServer += () => { Status = $"Connected to {address}:{port}"; Stats.Event(Status); Client.Hello(_name, _version); };
        _mp.ConnectionFailed += () => { Status = $"Could not reach {address}:{port} (wrong address, the host not running, or a port not forwarded)"; Stats.Event(Status); };
        _mp.ServerDisconnected += () => { Status = "The host closed the game or the connection dropped."; Stats.Event(Status); };
        Status = $"Connecting to {address}:{port}…";
        Stats.Event(Status + $" (version {version})");
        return Error.Ok;
    }

    private void Setup(string name, string version)
    {
        _name = name; _version = version;
        _mp = new SceneMultiplayer { AllowObjectDecoding = false };
        GetTree().SetMultiplayer(_mp);
        _mp.PeerPacket += OnPacket;
    }

    private void OnPacket(long id, byte[] packet)
    {
        if (packet.Length == 0) return;
        Stats.Counted((Msg)packet[0], false, packet.Length);
        if ((Msg)packet[0] is Msg.Hello or Msg.Welcome or Msg.World or Msg.Refused) Stats.Event($"{(Msg)packet[0]} from #{id} ({packet.Length} bytes)");
        if (Server != null) Server.Receive(id, packet);     // host: everything from the network is for the server part
        else if (id == NetServer.HostPeer) Client.Receive(packet); // client: only the host speaks
    }

    private void SendFromServer(long peer, byte[] data, bool reliable)
    {
        if (peer == NetServer.HostPeer) Client.Receive(data); // the host's own player: no network
        else Send(peer, data, reliable);
    }

    private void Send(long peer, byte[] data, bool reliable)
    {
        // Not (or no longer) connected: nothing to send to (it used to fill the log with errors after a drop).
        if (_peer == null || _peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return;
        if (Lag.Hold(peer, data, reliable)) return;
        SendNow(peer, data, reliable);
    }

    private void SendNow(long peer, byte[] data, bool reliable)
    {
        if (_peer == null || _peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return;
        // (a peer leaving — its connection closing — or gone while its messages waited in the simulated lag: ENet would
        // print "Unable to send packet on channel 0, max channels: 0" for each, as at the end of the 12-player test)
        if (_peer.GetPeer((int)peer) is not { } to || to.GetState() != ENetPacketPeer.PeerState.Connected) return;
        Stats.Counted((Msg)data[0], true, data.Length);
        _mp.SendBytes(data, (int)peer, reliable ? MultiplayerPeer.TransferModeEnum.Reliable : MultiplayerPeer.TransferModeEnum.Unreliable, reliable ? 0 : 1);
    }

    /// <summary>A simulated slow line for tests (<c>--net-lag=ms[,loss%]</c>; D138): what this PC sends waits that long.</summary>
    public NetLag Lag { get; } = NetLag.FromArgs();

    /// <summary>Every connected peer with its name, round trip, jitter (ms) and packet loss (%) as ENet measures them.</summary>
    public List<(long Id, string Name, int Rtt, int Jitter, double Loss)> Peers()
    {
        var list = new List<(long, string, int, int, double)>();
        if (_peer == null || _peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return list;
        foreach (long id in IsHost ? _mp.GetPeers().Select(p => (long)p) : new[] { NetServer.HostPeer })
        {
            var p = _peer.GetPeer((int)id);
            if (p == null) continue;
            list.Add((id, Client.Names.GetValueOrDefault((int)id, IsHost ? "?" : "host"),
                (int)p.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime), (int)p.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTimeVariance),
                p.GetStatistic(ENetPacketPeer.PeerStatistic.PacketLoss) / 65536.0 * 100));
        }
        return list;
    }

    public override void _Process(double delta)
    {
        if (_peer == null) return;
        Lag.Release(SendNow);
        _watch.Restart();
        Server?.Tick(delta, _hostState());
        Stats.Frame(delta, Server != null ? _watch.Elapsed.TotalMilliseconds : 0);
        Client.Tick(delta);
        Stats.Tick(delta, Peers());
    }

    /// <summary>Round trip to the host in ms (a joining client), or -1.</summary>
    public int Ping()
    {
        if (_peer == null || IsHost || _peer.GetConnectionStatus() != MultiplayerPeer.ConnectionStatus.Connected) return -1;
        return (int)_peer.GetPeer(1).GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
    }

    public override void _ExitTree()
    {
        _peer?.Close();
        if (_mp != null) _mp.MultiplayerPeer = null;
    }
}
