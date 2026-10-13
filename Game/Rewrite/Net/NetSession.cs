using System;
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
    private ENetMultiplayerPeer? _peer;
    private SceneMultiplayer _mp = null!;
    private Func<PlayerState?> _hostState = () => null;
    private string _name = "", _version = "";

    /// <summary>Host on <paramref name="port"/>: the server part plus this player's client part.</summary>
    public Error Host(int port, IWorldHost world, IClientGame game, string name, string version)
    {
        Setup(name, version);
        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateServer(port, 32);
        if (error != Error.Ok) { Status = $"Could not host on port {port}: {error} (is another game hosting already?)"; return error; }
        _mp.MultiplayerPeer = _peer;
        Server = new NetServer(world, SendFromServer, version);
        Server.SetHostName(name);
        Client = new NetClient(game, (data, _) => Server.Receive(NetServer.HostPeer, data), isHost: true);
        _hostState = game.LocalState;
        _mp.PeerConnected += id => { GD.Print($"NET peer {id} connected"); Server.Connected(id); };
        _mp.PeerDisconnected += id => { GD.Print($"NET peer {id} disconnected"); Server.Disconnected(id); };
        Status = $"Hosting on UDP port {port}";
        GD.Print("NET " + Status);
        return Error.Ok;
    }

    /// <summary>Join a host at <paramref name="address"/>:<paramref name="port"/>.</summary>
    public Error Join(string address, int port, IClientGame game, string name, string version)
    {
        Setup(name, version);
        _peer = new ENetMultiplayerPeer();
        var error = _peer.CreateClient(address, port);
        if (error != Error.Ok) { Status = $"Could not connect to {address}:{port}: {error}"; return error; }
        _mp.MultiplayerPeer = _peer;
        Client = new NetClient(game, (data, reliable) => Send(NetServer.HostPeer, data, reliable), isHost: false);
        _mp.ConnectedToServer += () => { Status = $"Connected to {address}:{port}"; GD.Print("NET " + Status); Client.Hello(_name, _version); };
        _mp.ConnectionFailed += () => { Status = $"Could not reach {address}:{port} (wrong address, the host not running, or a port not forwarded)"; GD.Print("NET " + Status); };
        _mp.ServerDisconnected += () => { Status = "The host closed the game or the connection dropped."; GD.Print("NET " + Status); };
        Status = $"Connecting to {address}:{port}…";
        GD.Print("NET " + Status);
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
        if (packet.Length > 0 && (Msg)packet[0] is not (Msg.State or Msg.Players or Msg.Clock)) GD.Print($"NET {(Msg)packet[0]} from {id} ({packet.Length} bytes)");
        if (Server != null) Server.Receive(id, packet);     // host: everything from the network is for the server part
        else if (id == NetServer.HostPeer) Client.Receive(packet); // client: only the host speaks
    }

    private void SendFromServer(long peer, byte[] data, bool reliable)
    {
        if (peer == NetServer.HostPeer) Client.Receive(data); // the host's own player: no network
        else Send(peer, data, reliable);
    }

    private void Send(long peer, byte[] data, bool reliable) =>
        _mp.SendBytes(data, (int)peer, reliable ? MultiplayerPeer.TransferModeEnum.Reliable : MultiplayerPeer.TransferModeEnum.Unreliable, reliable ? 0 : 1);

    public override void _Process(double delta)
    {
        if (_peer == null) return;
        Server?.Tick(delta, _hostState());
        Client.Tick(delta);
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
