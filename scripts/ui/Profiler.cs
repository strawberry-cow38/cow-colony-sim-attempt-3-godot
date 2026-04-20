using System.Collections.Generic;
using System.Diagnostics;

namespace CowColonySim.UI;

public static class Profiler
{
    private const double Alpha = 0.1;

    private static readonly Dictionary<string, long> _starts = new();
    private static readonly Dictionary<string, double> _smoothed = new();
    private static readonly List<string> _phaseOrder = new();
    private static readonly Dictionary<string, long> _counters = new();
    private static readonly List<string> _counterOrder = new();

    public static void Begin(string name)
    {
        _starts[name] = Stopwatch.GetTimestamp();
    }

    public static void End(string name)
    {
        if (!_starts.TryGetValue(name, out var start)) return;
        var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        if (_smoothed.TryGetValue(name, out var prev))
        {
            _smoothed[name] = prev * (1 - Alpha) + ms * Alpha;
        }
        else
        {
            _smoothed[name] = ms;
            _phaseOrder.Add(name);
        }
    }

    public static void SetCounter(string name, long value)
    {
        if (!_counters.ContainsKey(name)) _counterOrder.Add(name);
        _counters[name] = value;
    }

    public static IReadOnlyList<string> PhaseOrder => _phaseOrder;
    public static double GetPhaseMs(string name) => _smoothed.TryGetValue(name, out var v) ? v : 0;
    public static IReadOnlyList<string> CounterOrder => _counterOrder;
    public static long GetCounter(string name) => _counters.TryGetValue(name, out var v) ? v : 0;
}
