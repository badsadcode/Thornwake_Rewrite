using System.Diagnostics;

/// <summary>Minimal named-test runner: each test reports its check count and time; failures stop the run.</summary>
internal static class T
{
    private static long _checks;
    private static readonly List<(string Name, double Ms, long Checks)> Results = new();

    public static void Run(string name, Action test, string? filter)
    {
        if (filter != null && !name.Contains(filter, StringComparison.OrdinalIgnoreCase)) return;
        long before = _checks;
        var sw = Stopwatch.StartNew();
        test();
        Results.Add((name, sw.Elapsed.TotalMilliseconds, _checks - before));
        Console.WriteLine($"PASS {name,-58} {_checks - before,9} checks {sw.Elapsed.TotalMilliseconds,9:0.0} ms");
    }

    public static void Check(bool condition, string message)
    {
        if (!condition) throw new TestFailure(message);
        _checks++;
    }

    public static void Near(double actual, double expected, double tolerance, string message) =>
        Check(Math.Abs(actual - expected) <= tolerance, $"{message}: expected {expected:R} ± {tolerance:R}, got {actual:R}");

    public static void Note(string text) => Console.WriteLine("     " + text);

    public static long TotalChecks => _checks;
    public static int Count => Results.Count;
}

internal sealed class TestFailure : Exception { public TestFailure(string message) : base(message) { } }
