using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;

namespace Thornwake.Rewrite;

/// <summary>
/// Long-frame diagnostics (Tom: the game froze for 12 s and 114 s after teleports in a multiplayer session, and the guest
/// timed out). Each frame marks its steps; a frame (or physics step) over half a second prints how long each step took,
/// to the log and through <see cref="Report"/> (the network log while a session runs). Costs a stopwatch read per step.
/// </summary>
public sealed class Hitches
{
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<(string Step, double Ms)> _steps = new();
    private double _last;
    private string _frame = "";

    public System.Action<string>? Report { get; set; }

    public void Begin(string frame) { _frame = frame; _steps.Clear(); _last = _clock.Elapsed.TotalMilliseconds; }

    public void Mark(string step)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        _steps.Add((step, now - _last));
        if (Totals != null) Totals[step] = Totals.GetValueOrDefault(step) + now - _last;
        _last = now;
    }

    /// <summary>When set, each step's time adds up here (the network log's per-second breakdown; D138).</summary>
    public Dictionary<string, double>? Totals { get; set; }

    public void End(string context)
    {
        Mark("rest");
        double total = _steps.Sum(s => s.Ms);
        if (total < 500) return;
        string line = string.Format(System.Globalization.CultureInfo.InvariantCulture, "HITCH {0} frame {1:0} ms ({2}): {3}", _frame, total, context,
            string.Join(", ", _steps.Where(s => s.Ms >= 20).OrderByDescending(s => s.Ms).Select(s => string.Format(System.Globalization.CultureInfo.InvariantCulture, "{0} {1:0}", s.Step, s.Ms))));
        GD.Print(line);
        Report?.Invoke(line);
    }
}
