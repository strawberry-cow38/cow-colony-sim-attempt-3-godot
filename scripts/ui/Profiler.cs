using System.Collections.Generic;
using System.Diagnostics;

namespace CowColonySim.UI;

public static class Profiler
{
    private const double Alpha = 0.1;
    private static readonly object _lock = new();

    private static readonly Dictionary<string, long> _starts = new();
    private static readonly Dictionary<string, double> _smoothed = new();
    private static readonly List<string> _phaseOrder = new();
    private static readonly Dictionary<string, long> _counters = new();
    private static readonly List<string> _counterOrder = new();
    private static readonly Dictionary<string, long> _rateAccum = new();
    private static readonly Dictionary<string, double> _rateSmoothed = new();
    private static readonly List<string> _rateOrder = new();
    private static long _lastRateStamp;

    public static void Begin(string name)
    {
        _starts[name] = Stopwatch.GetTimestamp();
    }

    public static void End(string name)
    {
        if (!_starts.TryGetValue(name, out var start)) return;
        var ms = (Stopwatch.GetTimestamp() - start) * 1000.0 / Stopwatch.Frequency;
        RecordMs(name, ms);
    }

    public static void RecordMs(string name, double ms)
    {
        lock (_lock)
        {
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
    }

    public static void SetCounter(string name, long value)
    {
        lock (_lock)
        {
            if (!_counters.ContainsKey(name)) _counterOrder.Add(name);
            _counters[name] = value;
        }
    }

    public static void IncRate(string name, long delta = 1)
    {
        lock (_lock)
        {
            if (!_rateAccum.ContainsKey(name))
            {
                _rateAccum[name] = 0;
                _rateSmoothed[name] = 0;
                _rateOrder.Add(name);
            }
            _rateAccum[name] += delta;
        }
    }

    // Called once per HUD frame. When ≥1s elapsed, fold accumulators into
    // smoothed rates and reset. Keeps rate numbers stable instead of jittering
    // with frame cadence.
    public static void TickRates()
    {
        lock (_lock)
        {
            var now = Stopwatch.GetTimestamp();
            if (_lastRateStamp == 0) { _lastRateStamp = now; return; }
            var dt = (now - _lastRateStamp) / (double)Stopwatch.Frequency;
            if (dt < 1.0) return;
            foreach (var name in _rateOrder)
            {
                var r = _rateAccum[name] / dt;
                _rateSmoothed[name] = _rateSmoothed.TryGetValue(name, out var prev) && prev > 0
                    ? prev * (1 - Alpha) + r * Alpha
                    : r;
                _rateAccum[name] = 0;
            }
            _lastRateStamp = now;
        }
    }

    public static IReadOnlyList<string> PhaseOrder => _phaseOrder;
    public static double GetPhaseMs(string name) => _smoothed.TryGetValue(name, out var v) ? v : 0;
    public static IReadOnlyList<string> CounterOrder => _counterOrder;
    public static long GetCounter(string name) => _counters.TryGetValue(name, out var v) ? v : 0;
    public static IReadOnlyList<string> RateOrder => _rateOrder;
    public static double GetRate(string name) => _rateSmoothed.TryGetValue(name, out var v) ? v : 0;
}
