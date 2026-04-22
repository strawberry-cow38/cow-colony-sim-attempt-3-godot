using Godot;
using CowColonySim.Sim;
using CowColonySim.Sim.Systems;

namespace CowColonySim.UI;

public partial class TimeHud : CanvasLayer
{
    private static readonly SimSpeed[] Speeds =
        { SimSpeed.X1, SimSpeed.X2, SimSpeed.X3, SimSpeed.X6 };

    private SimHost? _sim;
    private Label? _clock;
    private Button? _pauseButton;
    private Button[]? _speedButtons;

    public override void _Ready()
    {
        Layer = 10;
        _sim = GetNode<SimHost>("/root/SimHost");

        var bar = new HBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 0f, AnchorBottom = 0f,
            OffsetLeft = -260, OffsetRight = 260,
            OffsetTop = 42, OffsetBottom = 76,
        };
        bar.AddThemeConstantOverride("separation", 8);
        AddChild(bar);

        _clock = new Label
        {
            Text = "1999-01-01 Fri 08:00",
            CustomMinimumSize = new Vector2(260, 24),
            VerticalAlignment = VerticalAlignment.Center,
        };
        _clock.AddThemeColorOverride("font_color", new Color(1f, 1f, 1f));
        _clock.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
        _clock.AddThemeConstantOverride("outline_size", 4);
        _clock.AddThemeFontSizeOverride("font_size", 16);
        bar.AddChild(_clock);

        _pauseButton = new Button { Text = "Pause", ToggleMode = true };
        _pauseButton.Pressed += TogglePause;
        bar.AddChild(_pauseButton);

        _speedButtons = new Button[Speeds.Length];
        for (var i = 0; i < Speeds.Length; i++)
        {
            var speed = Speeds[i];
            var b = new Button { Text = $"{(int)speed}x" };
            b.Pressed += () => SetSpeed(speed);
            bar.AddChild(b);
            _speedButtons[i] = b;
        }
    }

    public override void _Process(double delta)
    {
        if (_sim == null || _clock == null) return;
        var dt = CalendarSystem.ToDateTime(_sim.TimeOfDay.Ticks);
        _clock.Text = $"{dt:yyyy-MM-dd} {dt:ddd} {dt:HH:mm}";
        UpdateHighlights();
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (_sim == null) return;
        if (ev is not InputEventKey k || !k.Pressed || k.Echo) return;
        switch (k.Keycode)
        {
            case Key.Space: TogglePause(); break;
            case Key.Key1: SetSpeed(SimSpeed.X1); break;
            case Key.Key2: SetSpeed(SimSpeed.X2); break;
            case Key.Key3: SetSpeed(SimSpeed.X3); break;
            case Key.Key4: SetSpeed(SimSpeed.X6); break;
            default: return;
        }
        GetViewport().SetInputAsHandled();
    }

    private void TogglePause()
    {
        if (_sim == null) return;
        _sim.Loop.IsPaused = !_sim.Loop.IsPaused;
    }

    private void SetSpeed(SimSpeed s)
    {
        if (_sim == null) return;
        _sim.Loop.IsPaused = false;
        _sim.Loop.Speed = s;
    }

    private void UpdateHighlights()
    {
        if (_sim == null) return;
        if (_pauseButton != null) _pauseButton.ButtonPressed = _sim.Loop.IsPaused;
        if (_speedButtons == null) return;
        var active = new Color(1f, 0.92f, 0.10f);
        var idle = new Color(1f, 1f, 1f);
        for (var i = 0; i < Speeds.Length; i++)
        {
            var on = !_sim.Loop.IsPaused && _sim.Loop.Speed == Speeds[i];
            _speedButtons[i].Modulate = on ? active : idle;
        }
    }
}
