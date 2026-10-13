using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Godot;

namespace Thornwake.Rewrite.Net;

/// <summary>
/// Network diagnostics (Tom: "a diagnostic network multiplayer dump … record the progress, the stats"). While a
/// multiplayer session runs, one line a second goes to user://debug/net-&lt;time&gt;-&lt;role&gt;.log: every peer's round trip,
/// its jitter and packet loss, bytes and messages in and out by kind, how far the clock is from the host's, how
/// regularly the other players' positions arrive and how often they had to be guessed ahead, terrain chunks; plus every
/// event (connected, joined, left, refused, world changed, errors) with its time. A debug dump (F9) adds a summary, the
/// latest events and the log's path, so one file from each player shows what happened on both ends.
/// </summary>
public sealed class NetStats
{
    private readonly ulong _start = Time.GetTicksMsec();
    private readonly Dictionary<(Msg, bool Out), (long Count, long Bytes)> _total = new(), _second = new();
    private readonly List<string> _events = new();
    private readonly Dictionary<int, (double Last, double MaxGap, double SumGap, int Count)> _arrivals = new();
    private string? _path;
    private double _timer;

    public string Role { get; set; } = "";
    /// <summary>Client: the host's clock minus ours when the last clock arrived (game-time seconds).</summary>
    public double ClockOffset { get; set; }
    /// <summary>Frames a remote player was drawn guessed ahead (a position came late), this second.</summary>
    public int Extrapolated { get; set; }
    public int ChunksIn { get; set; }
    public string? LogPath => _path;

    private double Now => (Time.GetTicksMsec() - _start) / 1000.0;

    public void Counted(Msg kind, bool outgoing, int bytes)
    {
        foreach (var d in new[] { _total, _second })
        {
            d.TryGetValue((kind, outgoing), out var v);
            d[(kind, outgoing)] = (v.Count + 1, v.Bytes + bytes);
        }
    }

    public void Event(string text)
    {
        string line = string.Format(CultureInfo.InvariantCulture, "{0,8:0.00} s  {1}", Now, text);
        _events.Add(line);
        if (_events.Count > 400) _events.RemoveAt(0);
        GD.Print("NET " + text);
        Write("EVENT " + line);
    }

    /// <summary>A position of player <paramref name="id"/> arrived (client side): gaps between arrivals.</summary>
    public void Arrived(int id)
    {
        double now = Now;
        if (_arrivals.TryGetValue(id, out var a))
        {
            double gap = now - a.Last;
            _arrivals[id] = (now, Math.Max(a.MaxGap, gap), a.SumGap + gap, a.Count + 1);
        }
        else _arrivals[id] = (now, 0, 0, 0);
    }

    /// <summary>Once a second: the line for the log. <paramref name="peers"/>: id, name, round trip, jitter, loss %.</summary>
    public void Tick(double dt, IEnumerable<(long Id, string Name, int Rtt, int Jitter, double Loss)> peers)
    {
        if ((_timer -= dt) > 0) return;
        _timer = 1;
        var c = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();
        sb.Append(c, $"{Now,8:0.0} s  {Role}");
        foreach (var p in peers) sb.Append(c, $" | {p.Name}#{p.Id} rtt {p.Rtt} ms ±{p.Jitter} loss {p.Loss:0.#}%");
        long inBytes = _second.Where(e => !e.Key.Out).Sum(e => e.Value.Bytes), outBytes = _second.Where(e => e.Key.Out).Sum(e => e.Value.Bytes);
        sb.Append(c, $" | in {inBytes / 1024.0:0.0} KB/s out {outBytes / 1024.0:0.0} KB/s");
        sb.Append(" | " + string.Join(" ", _second.OrderBy(e => e.Key.Item1).Select(e => $"{(e.Key.Out ? ">" : "<")}{e.Key.Item1}×{e.Value.Count}")));
        if (Role.StartsWith("client")) sb.Append(c, $" | clock {ClockOffset:+0.0;-0.0} s");
        foreach (var (id, a) in _arrivals.Where(a => a.Value.Count > 0))
            sb.Append(c, $" | positions of #{id} every {a.SumGap / a.Count * 1000:0} ms (max {a.MaxGap * 1000:0})");
        if (Extrapolated > 0) sb.Append(c, $" | guessed ahead {Extrapolated} frames");
        if (ChunksIn > 0) sb.Append(c, $" | chunks in {ChunksIn}");
        Write(sb.ToString());
        _second.Clear();
        foreach (var id in _arrivals.Keys.ToList()) _arrivals[id] = (_arrivals[id].Last, 0, 0, 0);
        Extrapolated = 0; ChunksIn = 0;
    }

    /// <summary>For a debug dump: totals, the latest events and where the full log is.</summary>
    public string Summary(IEnumerable<(long Id, string Name, int Rtt, int Jitter, double Loss)> peers)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"Network: {Role}, session {Now:0} s, log {(_path != null ? ProjectSettings.GlobalizePath(_path) : "none")}");
        foreach (var p in peers) sb.AppendLine(CultureInfo.InvariantCulture, $"  peer {p.Name}#{p.Id}: round trip {p.Rtt} ms ±{p.Jitter}, loss {p.Loss:0.#} %");
        foreach (var ((kind, outgoing), v) in _total.OrderBy(e => e.Key.Item1))
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {(outgoing ? "sent" : "received")} {kind}: {v.Count} messages, {v.Bytes / 1024.0:0.0} KB");
        sb.AppendLine($"  local addresses: {string.Join(", ", IP.GetLocalAddresses().Where(a => !a.Contains(':') && a != "127.0.0.1"))}");
        sb.AppendLine("  latest events:");
        foreach (var e in _events.TakeLast(40)) sb.AppendLine("    " + e);
        return sb.ToString();
    }

    private void Write(string line)
    {
        try
        {
            if (_path == null)
            {
                DirAccess.MakeDirRecursiveAbsolute("user://debug");
                _path = $"user://debug/net-{DateTime.Now:yyyyMMdd-HHmmss}-{(Role.Length > 0 ? Role.Split(' ')[0] : "session")}.log";
                File.WriteAllText(ProjectSettings.GlobalizePath(_path), $"Thornwake network log · version {ProjectSettings.GetSetting("application/config/version", "?")} · {DateTime.Now:yyyy-MM-dd HH:mm:ss}\n");
            }
            File.AppendAllText(ProjectSettings.GlobalizePath(_path), line + "\n");
        }
        catch (Exception error) { GD.PushWarning("Network log not written: " + error.Message); }
    }
}
