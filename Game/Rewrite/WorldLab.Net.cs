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

    /// <summary>From the command line: host or join (before the world is generated).</summary>
    private bool StartNetwork()
    {
        var args = OS.GetCmdlineUserArgs();
        string name = args.FirstOrDefault(a => a.StartsWith("--name="))?[7..] ?? System.Environment.UserName;
        string version = (string)ProjectSettings.GetSetting("application/config/version", "?");
        if (args.FirstOrDefault(a => a == "--host" || a.StartsWith("--host=")) is { } host)
        {
            int port = host.Contains('=') ? int.Parse(host[7..], CultureInfo.InvariantCulture) : NetSession.DefaultPort;
            _net = new NetSession { Name = "Net" };
            AddChild(_net);
            _net.Host(port, this, this, name, version);
            return false; // the host builds its own world as usual
        }
        if (args.FirstOrDefault(a => a.StartsWith("--join=")) is { } join)
        {
            string target = join[7..];
            int port = NetSession.DefaultPort;
            int colon = target.LastIndexOf(':');
            if (colon > 0) { port = int.Parse(target[(colon + 1)..], CultureInfo.InvariantCulture); target = target[..colon]; }
            _net = new NetSession { Name = "Net" };
            AddChild(_net);
            _net.Join(target, port, this, name, version);
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

    /// <summary>Per frame: draw the other players where they are now.</summary>
    private void StepRemotes(double delta)
    {
        if (_remotes.Count == 0) return;
        double now = Time.GetTicksMsec() / 1000.0;
        foreach (var r in _remotes.Values) r.Step(now, (float)delta, _originX, _originZ);
    }

    private string NetHud()
    {
        if (_net == null) return "";
        string others = string.Join(", ", _net.Client.Names.Values);
        string who = others.Length > 0 ? $" · with {others}" : " · nobody else here yet";
        return _net.IsHost ? $"\n{_net.Status} · {_net.Server!.PlayerCount - 1} joined{who}"
            : $"\n{_net.Status}{(_net.Ping() >= 0 ? $" · ping {_net.Ping()} ms" : "")}{who}";
    }

    // ---- IWorldHost: the host's world as the server part sees it

    string IWorldHost.WorldArgs => _worldArgs;
    string? IWorldHost.WorldTypeJson => _recipes[Math.Max(0, _profileList.Selected)] is { Custom: true } r ? r.ToJson() : null;
    IReadOnlyList<(string Key, double Value)> IWorldHost.WorldSettings() =>
        GameSettings.All.Where(GameSettings.AffectsWorld).Select(s => (s.Key, s.Value)).ToList();
    ClockState IWorldHost.Clock() => new(_clock.Day, _clock.Phase, _atmosphere.OceanTime, _atmosphere.Wind.Time, _atmosphere.CloudDrift);
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
        _clock.Follow(c.Day, c.Phase, Math.Max(dt, 1), snap); // (once a second: close the gap over ~2 s)
        _atmosphere.FollowOceanTime(c.OceanTime, 1, snap);
        _atmosphere.Wind.Time = c.WindTime;
        _atmosphere.FollowCloudDrift(c.CloudDrift);
    }

    void IClientGame.ApplyChunks(IReadOnlyList<(long X, long Z, float[]? Offsets)> chunks)
    {
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

    /// <summary>After the host's world finished generating here: its edits so far, and the place next to the host.</summary>
    private void NetWorldReady()
    {
        if (!IsNetClient || _sampler == null) return;
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
    }

    void IClientGame.PlayerLeft(int id)
    {
        if (!_remotes.Remove(id, out var gone)) return;
        _status = $"{gone.PlayerName} left.";
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
    }

    void IClientGame.DigDone(double volume, double hardness) => _tools.RemoteDone(volume, hardness);

    void IClientGame.Refused(string reason) { _overlay.Text = "Could not join: " + reason; _overlay.Visible = true; GD.Print("NET refused: " + reason); }

    PlayerState? IClientGame.LocalState()
    {
        if (_sampler == null || _holding) return null;
        var v = _player.Velocity;
        var flags = (_fly ? PlayerFlags.Flying : 0) | (_me.Torch.Lit ? PlayerFlags.Torch : 0) | (_swimming ? PlayerFlags.Swimming : 0);
        return new PlayerState(PlayerX, _player.Position.Y, PlayerZ, _me.Facing, v.X, v.Y, v.Z, flags);
    }
}
