using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Thornwake.Rewrite.Net;
using Thornwake.WorldGen;

namespace Thornwake.Rewrite;

/// <summary>
/// Multiplayer spike (docs/GAMEPLAY_DESIGN.md §14.0): the world as a host serves it (IWorldHost) and as a joining player
/// shows it (IClientGame). <c>--host[=port]</c> hosts (default UDP 7777), <c>--join=address[:port]</c> joins,
/// <c>--name=…</c> names the player. The server part and the client part are in Net/; this file only connects them to
/// the world, the clock, the settings, the terrain edits and the people in it.
/// </summary>
public partial class WorldLab : IWorldHost, IClientGame
{
    private NetSession? _net;
    private readonly Dictionary<int, RemotePlayer> _remotes = new();
    private PlayerState? _netSpawn;
    private readonly List<(long X, long Z, float[]? Offsets)> _pendingChunks = new();
    private bool _netWorldLoading;

    /// <summary>A joining player (the world, its settings, time and edits come from the host).</summary>
    private bool IsNetClient => _net is { IsHost: false };

    /// <summary>Host or join (before the world is generated): from the launcher's Multiplayer page, or for tests from the
    /// command line (--host[=port], --join=address[:port], --name=…).</summary>
    private bool StartNetwork()
    {
        var args = OS.GetCmdlineUserArgs();
        if (NetLaunch.Pending == NetLaunch.Role.None) NetLaunch.Load(); // (from the launcher: its values are already here)
        string name = args.FirstOrDefault(a => a.StartsWith("--name="))?[7..] ?? NetLaunch.Name;
        string version = (string)ProjectSettings.GetSetting("application/config/version", "?");
        var pending = NetLaunch.Pending;
        NetLaunch.Pending = NetLaunch.Role.None;
        string? host = args.FirstOrDefault(a => a == "--host" || a.StartsWith("--host="))
            ?? (pending == NetLaunch.Role.Host ? $"--host={NetLaunch.Port}" : null);
        string? join = args.FirstOrDefault(a => a.StartsWith("--join="))
            ?? (pending == NetLaunch.Role.Join ? (NetLaunch.Address.Contains(':') ? $"--join=[{NetLaunch.Address}]:{NetLaunch.Port}" : $"--join={NetLaunch.Address}:{NetLaunch.Port}") : null);
        if (host != null)
        {
            int port = host.Contains('=') ? int.Parse(host[7..], CultureInfo.InvariantCulture) : NetSession.DefaultPort;
            _net = new NetSession { Name = "Net" };
            AddChild(_net);
            _net.Host(port, this, this, name, version);
            _hitch.Report = _physicsHitch.Report = _net.Stats.Event;
            return false; // the host builds its own world as usual
        }
        if (join != null)
        {
            string target = join[7..];
            int port = NetSession.DefaultPort;
            // address:port, [IPv6]:port, or a bare IPv6 address (several colons: no port)
            if (target.StartsWith('[') && target.IndexOf(']') is > 1 and var close)
            {
                if (close + 1 < target.Length && target[close + 1] == ':') port = int.Parse(target[(close + 2)..], CultureInfo.InvariantCulture);
                target = target[1..close];
            }
            else if (target.Count(c => c == ':') == 1)
            {
                int colon = target.IndexOf(':');
                port = int.Parse(target[(colon + 1)..], CultureInfo.InvariantCulture); target = target[..colon];
            }
            _net = new NetSession { Name = "Net" };
            AddChild(_net);
            _net.Join(target, port, this, name, version);
            _hitch.Report = _physicsHitch.Report = _net.Stats.Event;
            _tools.Remote = dig => _net.Client.SendDig(dig);
            _overlay.Text = $"Joining {target}:{port}…";
            _overlay.Visible = true;
            return true; // the world comes from the host
        }
        return false;
    }

    private double _joinWait;

    /// <summary>Joining, before the host's world arrives: show how the connection goes (and give up in automated runs).</summary>
    private void StepJoining(double delta)
    {
        if (!IsNetClient || _sampler != null || _generation != null || _net!.Client.Welcomed) return;
        if (!_overlay.Text.StartsWith("Could not join")) _overlay.Text = _net.Status;
        _joinWait += delta;
        if (_smokeDirectory != null && _joinWait > 90) { GD.PushError("NET join timed out: " + _net.Status); GetTree().Quit(1); }
    }

    // Tests: --net-dig=x,z,radius,depth (a client asks the host to dig there once it stands in the world),
    // --net-probe=x,z (the ground height there, into the network log every 2 s: host and client must agree).
    private bool _netDigSent;
    private double _probeTimer;
    private double _digRepeatTimer;
    private int _digRepeatCount;

    private double _testTime;
    private bool _mapShotDone;

    private void NetTests(double delta)
    {
        if (_sampler == null || _generation != null) return;
        var args = OS.GetCmdlineUserArgs();
        _testTime += delta;
        // --host-here=seconds[,port]: a game started alone opens itself to others that long after its world is ready (the
        // Esc menu's Host this world). --map-shot=png,seconds: the world map open, saved as a picture (the players on it).
        if (_net == null && args.FirstOrDefault(a => a.StartsWith("--host-here=")) is { } hostHere
            && hostHere[12..].Split(',') is var hv && _testTime > double.Parse(hv[0], CultureInfo.InvariantCulture))
            HostHere(hv.Length > 1 ? int.Parse(hv[1], CultureInfo.InvariantCulture) : null);
        if (!_mapShotDone && args.FirstOrDefault(a => a.StartsWith("--map-shot=")) is { } mapShot && mapShot[11..].Split(',') is [var png, var after]
            && _testTime > double.Parse(after, CultureInfo.InvariantCulture))
        {
            _mapShotDone = true;
            _map.Open(_sampler.Plan, PlayerX, PlayerZ, _yaw);
            GetTree().CreateTimer(1).Timeout += () => GetViewport().GetTexture().GetImage().SavePng(png);
        }
        if (_net == null) return;
        if (!_netDigSent && IsNetClient && ((IClientGame)this).LocalState() != null && args.FirstOrDefault(a => a.StartsWith("--net-dig=")) is { } dig)
        {
            var v = dig[10..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            _net.Client.SendDig(new DigRequest(BrushMode.Dig, BrushShape.Round, v[2], v[3], 1, double.PositiveInfinity, v[0], _sampler.Height(v[0], v[1]), v[1], 0, -1, 0));
            _net.Stats.Event($"test: asked the host to dig at {v[0]}, {v[1]}");
            _netDigSent = true;
        }
        // --net-dig-repeat=seconds,radius,strength: a client keeps digging and raising beside itself (terraform-like),
        // the load a guest put on the host in the 2026-10-13 internet test.
        if (IsNetClient && ((IClientGame)this).LocalState() != null && args.FirstOrDefault(a => a.StartsWith("--net-dig-repeat=")) is { } repeat
            && (_digRepeatTimer -= delta) <= 0)
        {
            var v = repeat[17..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            _digRepeatTimer = v[0];
            _digRepeatCount++;
            double x = PlayerX + 6 + (_digRepeatCount % 5) * 2, z = PlayerZ + (_digRepeatCount % 3) * 2;
            _net.Client.SendDig(new DigRequest(_digRepeatCount % 2 == 0 ? BrushMode.Dig : BrushMode.Raise, BrushShape.Round, v[1], v[2], 1,
                double.PositiveInfinity, x, _sampler.Height(x, z), z, 0, -1, 0));
        }
        if (args.FirstOrDefault(a => a.StartsWith("--net-probe=")) is { } probe && (_probeTimer -= delta) <= 0)
        {
            _probeTimer = 2;
            var v = probe[12..].Split(',').Select(t => double.Parse(t, CultureInfo.InvariantCulture)).ToArray();
            _net.Stats.Event(string.Format(CultureInfo.InvariantCulture, "test: ground at {0}, {1} is {2:0.000} m ({3} edit chunks)", v[0], v[1], _sampler.Height(v[0], v[1]), _sampler.Edits.ChunkCount));
            // (and the weather here, to compare between host and client at the same game time)
            var weather = _atmosphere.DebugInfo().Split('\n').Where(l => l.StartsWith("Storm") || l.StartsWith("Wind:") || l.StartsWith("Weather input")).Select(l => l.Trim());
            _net.Stats.Event(string.Format(CultureInfo.InvariantCulture, "test: weather at day {0} hour {1:0.000}: ", _clock.Day, _clock.Hour) + string.Join(" · ", weather));
        }
    }

    /// <summary>Per frame: draw the other players where they are now.</summary>
    private void StepRemotes(double delta)
    {
        NetTests(delta);
        if (_remotes.Count == 0) return;
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var r in _remotes.Values) if (r.Step(now, (float)delta, _originX, _originZ) && _net != null) _net.Stats.Extrapolated++;
    }

    private string NetHud()
    {
        if (_net == null) return "";
        if (_net.Client.Welcomed) _me.SetTunic(Character.TunicFor(_net.Client.Id)); // (the colour the others see me in)
        string others = string.Join(", ", _net.Client.Names.Values);
        string who = others.Length > 0 ? $" · with {others}" : " · nobody else here yet";
        return _net.IsHost ? $"\n{_net.Status} · {_net.Server!.PlayerCount - 1} joined{who}"
            : $"\n{_net.Status}{(_net.Ping() >= 0 ? $" · ping {_net.Ping()} ms" : "")}{who}";
    }

    // ---- IWorldHost: the host's world as the server part sees it

    string IWorldHost.WorldArgs => _worldArgs;
    string? IWorldHost.WorldTypeJson => _worldTypeJson;
    IReadOnlyList<(string Key, double Value)> IWorldHost.WorldSettings() =>
        GameSettings.All.Where(GameSettings.AffectsWorld).Select(s => (s.Key, s.Value)).ToList();
    ClockState IWorldHost.Clock() => new(_clock.Day, _clock.Phase, _atmosphere.OceanTime, _atmosphere.Wind.Time, _atmosphere.CloudDrift, _rain);
    TerrainEdits? IWorldHost.Edits => _sampler?.Edits;

    (double Volume, double Hardness) IWorldHost.Dig(in DigRequest d)
    {
        if (_sampler == null) return (0, 0);
        var result = TerrainSculpt.Apply(_sampler, d.Mode, d.Shape, d.Radius, d.Strength, (d.X, d.Y, d.Z), (d.AimX, d.AimY, d.AimZ), d.RockFactor, d.Budget);
        if (result.Volume != 0)
        {
            _streamer.Invalidate(result.MinX, result.MinZ, result.MaxX, result.MaxZ);
            _water.Sim?.ReloadGround(result.MinX, result.MinZ, result.MaxX, result.MaxZ);
        }
        return (result.Volume, result.Hardness);
    }

    // ---- IClientGame: a joining player follows the host

    void IClientGame.LoadWorld(string worldArgs, string? worldTypeJson, PlayerState? spawn)
    {
        _netSpawn = spawn;
        _net?.Stats.Event($"building the host's world: {worldArgs}");
        if (worldTypeJson != null)
        {
            var type = WorldRecipe.FromJson(worldTypeJson);
            _recipes = _recipes.Where(r => r.Id != type.Id).Append(type).ToList();
            _profileList.Clear();
            foreach (var r in _recipes) _profileList.AddItem(r.Name);
        }
        string preset = "mountainous"; int seed = 42;
        var parts = worldArgs.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var a in parts)
        {
            if (a.StartsWith("--preset=")) preset = a[9..];
            if (a.StartsWith("--seed=")) seed = int.Parse(a[7..], CultureInfo.InvariantCulture);
        }
        int index = Math.Max(0, _recipes.ToList().FindIndex(r => r.Id == preset));
        _profileList.Select(index);
        SelectType(index);
        foreach (var a in parts)
            if (a.StartsWith("--set=gen.") && a[6..].Split('=') is [var key, var value] && _gen.All.FirstOrDefault(x => x.Key == key) is { } g)
                g.Value = double.Parse(value, CultureInfo.InvariantCulture);
        _seedEdit.Text = seed.ToString(CultureInfo.InvariantCulture);
        _netWorldLoading = true;
        Generate();
        _netWorldLoading = false;
    }

    void IClientGame.ApplySettings(IReadOnlyList<(string Key, double Value)> settings)
    {
        foreach (var (key, value) in settings)
            if (GameSettings.All.FirstOrDefault(s => s.Key == key) is { } s && GameSettings.AffectsWorld(s) && s.Value != value)
                GameSettings.Override(s, value);
    }

    void IClientGame.ApplyClock(in ClockState c, bool snap)
    {
        double dt = GetProcessDeltaTime();
        if (_net != null) _net.Stats.ClockOffset = ((c.Day - _clock.Day) + (c.Phase - _clock.Phase)) * GameSettings.DayLength.Value * 60;
        _clock.Follow(c.Day, c.Phase, Math.Max(dt, 1), snap); // (once a second: close the gap over ~2 s)
        _atmosphere.FollowOceanTime(c.OceanTime, 1, snap);
        _atmosphere.Wind.Time = c.WindTime;
        _atmosphere.FollowCloudDrift(c.CloudDrift);
        if (_rain != c.Rain) _net?.Stats.Event($"the host's rain (H) is {(c.Rain ? "on" : "off")}");
        _rain = c.Rain; // rain started with H on the host rains for everyone
    }

    void IClientGame.ApplyChunks(IReadOnlyList<(long X, long Z, float[]? Offsets)> chunks)
    {
        if (_net != null) _net.Stats.ChunksIn += chunks.Count;
        if (_sampler == null || _generation != null) { _pendingChunks.AddRange(chunks); return; }
        ApplyChunksNow(chunks);
    }

    private void ApplyChunksNow(IReadOnlyList<(long X, long Z, float[]? Offsets)> chunks)
    {
        foreach (var (x, z, offsets) in chunks)
        {
            var (x0, z0, x1, z1) = _sampler!.Edits.SetChunk(x, z, offsets);
            _streamer.Invalidate(x0 - 1, z0 - 1, x1 + 1, z1 + 1);
            _water.Sim?.ReloadGround(x0 - 1, z0 - 1, x1 + 1, z1 + 1);
        }
        _sampler!.Edits.TakeChanged(); // (applied from the host: nothing to send back)
    }

    /// <summary>
    /// Esc → Host this world: the world already here (made and changed alone first) is opened to others, on the port and
    /// under the name from the launcher's Multiplayer page. Its digging so far goes to everyone who joins.
    /// </summary>
    private void HostHere(int? port = null)
    {
        if (_net != null || _sampler == null || _generation != null) return;
        NetLaunch.Load();
        if (port is { } p) NetLaunch.Port = p; // (tests: not the player's own port)
        string name = OS.GetCmdlineUserArgs().FirstOrDefault(a => a.StartsWith("--name="))?[7..]
            ?? (NetLaunch.Name.Length > 0 ? NetLaunch.Name : System.Environment.UserName);
        string version = (string)ProjectSettings.GetSetting("application/config/version", "?");
        var net = new NetSession { Name = "Net" };
        AddChild(net);
        if (net.Host(NetLaunch.Port, this, this, name, version) != Error.Ok) { _status = net.Status; net.QueueFree(); return; }
        _net = net;
        StopLocalGameplayLoop(); // (the network's server and client parts take over; the rules stay)
        _hostWorlds = 1; // this world is the first: a later one (New world) is sent to everyone
        _hitch.Report = _physicsHitch.Report = _net.Stats.Event;
        _status = $"Hosting this world on UDP port {NetLaunch.Port} as {name}. Others join with your address.";
    }

    /// <summary>After the host's world finished generating here: its edits so far, and the place next to the host.</summary>
    private int _hostWorlds;

    private void NetWorldReady()
    {
        // The host started another world (Esc menu): everyone builds it and comes back beside the host.
        if (_net?.Server is { } server && _hostWorlds++ > 0)
            server.WorldChanged(new PlayerState(PlayerX, _player.Position.Y, PlayerZ, _me.Facing, 0, 0, 0, PlayerFlags.None));
        if (!IsNetClient || _sampler == null) return;
        _net!.Stats.Event($"the host's world is ready here ({_pendingChunks.Count} edit chunks waiting)");
        if (_pendingChunks.Count > 0) { ApplyChunksNow(_pendingChunks.ToList()); _pendingChunks.Clear(); }
        if (_netSpawn is { } s) Teleport(s.X + 3, s.Z + 2); // beside the host, not inside them
        _netSpawn = null;
    }

    void IClientGame.PlayerJoined(int id, string name)
    {
        if (_remotes.TryGetValue(id, out var known)) { known.PlayerName = name; return; }
        var remote = new RemotePlayer(id, name, _atmosphere.Wind);
        _remotes[id] = remote;
        AddChild(remote);
        _status = $"{name} joined.";
        _net?.Stats.Event($"{name} (#{id}) is in the game");
    }

    void IClientGame.PlayerLeft(int id)
    {
        if (!_remotes.Remove(id, out var gone)) return;
        _status = $"{gone.PlayerName} left.";
        _net?.Stats.Event($"{gone.PlayerName} (#{id}) left");
        gone.QueueFree();
    }

    void IClientGame.PlayerMoved(int id, in PlayerState state)
    {
        if (!_remotes.TryGetValue(id, out var remote))
        {
            ((IClientGame)this).PlayerJoined(id, _net?.Client.Names.GetValueOrDefault(id) ?? $"Player {id}");
            remote = _remotes[id];
        }
        remote.Push(state, Time.GetTicksMsec() / 1000.0);
        _net?.Stats.Arrived(id);
    }

    /// <summary>The other players for the world map (yaw as the camera's: the body's facing − π).</summary>
    private IReadOnlyList<WorldMap.Other> MapOthers() => _remotes.Values
        .Where(r => r.Latest != null)
        .Select(r => { var s = r.Latest!.Value; return new WorldMap.Other(r.Id, r.PlayerName, s.X, s.Z, s.Facing - Mathf.Pi, r.Tunic); })
        .OrderBy(o => o.Name).ToList();

    /// <summary>Go to another player (from the map's list): beside them, on the ground.</summary>
    private void GoToPlayer(int id)
    {
        if (!_remotes.TryGetValue(id, out var r) || r.Latest is not { } s) return;
        Teleport(s.X + 3, s.Z + 2);
        Input.MouseMode = Input.MouseModeEnum.Captured;
        _hud.Visible = _showHud; _help.Visible = true;
        _status = $"Went to {r.PlayerName}.";
    }

    void IClientGame.DigDone(double volume, double hardness) => _tools.RemoteDone(volume, hardness);

    void IClientGame.Refused(string reason) { _overlay.Text = "Could not join: " + reason; _overlay.Visible = true; GD.Print("NET refused: " + reason); }

    PlayerState? IClientGame.LocalState()
    {
        if (_sampler == null || _holding) return null;
        var v = _player.Velocity;
        var flags = (_fly ? PlayerFlags.Flying : 0) | (_me.Torch.Lit ? PlayerFlags.Torch : 0) | (_swimming ? PlayerFlags.Swimming : 0)
            | (_axeHeld ? PlayerFlags.Axe : 0) | (_me.Chopping ? PlayerFlags.Chopping : 0);
        return new PlayerState(PlayerX, _player.Position.Y, PlayerZ, _me.Facing, v.X, v.Y, v.Z, flags);
    }
}
