using System.Diagnostics;
using System.Globalization;
using System.Text;

// Usage: dotnet run -c Release -- [--quick] [--legacy] [--out=path.json]
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
bool quick = args.Contains("--quick");
bool all = !args.Any(a => a is "--legacy" or "--new");
string? output = args.FirstOrDefault(a => a.StartsWith("--out="))?[6..];
var report = new Report();
report.Add("machine.cores", Environment.ProcessorCount);
if (all || args.Contains("--legacy"))
    foreach (var (profile, seed) in new[] { ("mountainous", 42), ("vast-world", 7319) })
        LegacyBench.Run(report, profile, seed, quick);
report.Add("process.peak_working_set_mb", Process.GetCurrentProcess().PeakWorkingSet64 / 1048576.0);
Console.WriteLine(report.Text());
if (output != null) File.WriteAllText(output, report.Json());

internal sealed class Report
{
    private readonly List<(string Key, double Value)> _rows = new();
    public void Add(string key, double value) { _rows.Add((key, value)); Console.Error.WriteLine($"  {key} = {value:0.###}"); }
    public double Get(string key) => _rows.Last(r => r.Key == key).Value;
    public string Text() => string.Join("\n", _rows.Select(r => $"{r.Key,-60} {r.Value,14:0.###}"));
    public string Json()
    {
        var sb = new StringBuilder("{\n");
        sb.Append(string.Join(",\n", _rows.Select(r => $"  \"{r.Key}\": {r.Value:0.######}")));
        return sb.Append("\n}\n").ToString();
    }
}

internal static class Bench
{
    public static long Allocated() => GC.GetTotalAllocatedBytes(true);
}
