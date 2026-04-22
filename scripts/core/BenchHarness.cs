using Godot;
using System.Collections.Generic;
using System.Linq;

namespace CowColonySim;

public partial class BenchHarness : Node
{
    private const double WarmupSec = 3.0;
    private const double SampleSec = 20.0;

    private bool _enabled;
    private double _elapsed;
    private readonly List<double> _fps = new();

    public override void _Ready()
    {
        var args = OS.GetCmdlineUserArgs();
        foreach (var a in args)
            if (a == "--bench") _enabled = true;

        if (!_enabled) return;
        GD.Print($"[BENCH] enabled — warmup {WarmupSec}s, sample {SampleSec}s");
        ProcessMode = ProcessModeEnum.Always;
    }

    public override void _Process(double delta)
    {
        if (!_enabled) return;
        _elapsed += delta;

        if (_elapsed < WarmupSec) return;

        var fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
        _fps.Add(fps);

        if (_elapsed - WarmupSec < SampleSec) return;

        var avg = _fps.Average();
        var min = _fps.Min();
        var max = _fps.Max();
        _fps.Sort();
        var p01 = _fps[(int)(_fps.Count * 0.01)];
        var p99 = _fps[(int)(_fps.Count * 0.99)];
        var frame = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        var draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        var vram = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed) / (1024.0 * 1024.0);

        GD.Print("[BENCH] RESULTS");
        GD.Print($"[BENCH] samples={_fps.Count}");
        GD.Print($"[BENCH] fps avg={avg:0.0} min={min:0.0} max={max:0.0} p01={p01:0.0} p99={p99:0.0}");
        GD.Print($"[BENCH] last frame process={frame:0.00}ms draws={draws:0} prims={prims:0} vram={vram:0.0}MB");
        GD.Print("[BENCH] done, quitting");
        GetTree().Quit(0);
        _enabled = false;
    }
}
