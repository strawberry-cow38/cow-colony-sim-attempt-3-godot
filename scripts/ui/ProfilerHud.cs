using System.Text;
using Godot;

namespace CowColonySim.UI;

public sealed partial class ProfilerHud : CanvasLayer
{
    private Panel _panel = null!;
    private Label _label = null!;
    private bool _shown;

    public override void _Ready()
    {
        Layer = 99;
        _panel = new Panel
        {
            AnchorLeft = 0, AnchorTop = 0,
            OffsetLeft = 8, OffsetTop = 8,
            OffsetRight = 420, OffsetBottom = 720,
        };
        AddChild(_panel);

        _label = new Label
        {
            AnchorLeft = 0, AnchorTop = 0, AnchorRight = 1, AnchorBottom = 1,
            OffsetLeft = 10, OffsetTop = 8, OffsetRight = -10, OffsetBottom = -8,
            LabelSettings = new LabelSettings
            {
                FontSize = 12,
                FontColor = new Color(1, 1, 1),
            },
        };
        _panel.AddChild(_label);
        _panel.Visible = false;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.P)
        {
            _shown = !_shown;
            _panel.Visible = _shown;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        Profiler.TickRates();
        if (!_shown) return;

        var fps = Performance.GetMonitor(Performance.Monitor.TimeFps);
        var frameCpu = Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0;
        var framePhys = Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0;
        var draws = Performance.GetMonitor(Performance.Monitor.RenderTotalDrawCallsInFrame);
        var prims = Performance.GetMonitor(Performance.Monitor.RenderTotalPrimitivesInFrame);
        var objs = Performance.GetMonitor(Performance.Monitor.RenderTotalObjectsInFrame);
        var vram = Performance.GetMonitor(Performance.Monitor.RenderVideoMemUsed);
        var objCount = Performance.GetMonitor(Performance.Monitor.ObjectCount);
        var memStatic = Performance.GetMonitor(Performance.Monitor.MemoryStatic);

        var sb = new StringBuilder();
        sb.Append("FPS ").Append(fps.ToString("0"))
            .Append("   frame ").Append((frameCpu + framePhys).ToString("0.00")).Append("ms\n");
        sb.Append("  process ").Append(frameCpu.ToString("0.00"))
            .Append("   physics ").Append(framePhys.ToString("0.00")).Append('\n');
        sb.Append("Draws ").Append(draws.ToString("0"))
            .Append("   Objects ").Append(objs.ToString("0"))
            .Append("   Prims ").Append(prims.ToString("0")).Append('\n');
        sb.Append("VRAM ").Append(FmtMb(vram))
            .Append("   RAM ").Append(FmtMb(memStatic))
            .Append("   nodes ").Append(objCount.ToString("0")).Append('\n');

        sb.Append("\nPhases (EMA ms)\n");
        foreach (var name in Profiler.PhaseOrder)
            sb.Append("  ").Append(name.PadRight(16)).Append(Profiler.GetPhaseMs(name).ToString("0.00")).Append('\n');

        sb.Append("\nRates (per s)\n");
        foreach (var name in Profiler.RateOrder)
            sb.Append("  ").Append(name.PadRight(16)).Append(Profiler.GetRate(name).ToString("0.0")).Append('\n');

        sb.Append("\nCounts\n");
        foreach (var name in Profiler.CounterOrder)
            sb.Append("  ").Append(name.PadRight(16)).Append(Profiler.GetCounter(name)).Append('\n');

        _label.Text = sb.ToString();
    }

    private static string FmtMb(double bytes)
    {
        if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0") + "KB";
        return (bytes / (1024.0 * 1024.0)).ToString("0") + "MB";
    }
}
