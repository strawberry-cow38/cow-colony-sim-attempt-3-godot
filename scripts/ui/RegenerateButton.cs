using System;
using Godot;

namespace CowColonySim.UI;

public partial class RegenerateButton : CanvasLayer
{
    private SimHost? _simHost;
    private Button? _button;
    private readonly Random _rng = new();

    public override void _Ready()
    {
        Layer = 10;
        _simHost = GetNode<SimHost>("/root/SimHost");

        _button = new Button
        {
            Text = "Regenerate World",
            // Anchor to top, slightly right of center.
            AnchorLeft = 0.55f,
            AnchorRight = 0.55f,
            AnchorTop = 0.0f,
            AnchorBottom = 0.0f,
            OffsetTop = 6,
            OffsetLeft = 0,
            OffsetRight = 160,
            OffsetBottom = 32,
            Visible = !_simHost.AwaitingWorldSelection,
        };
        _button.AddThemeFontSizeOverride("font_size", 14);
        _button.Pressed += OnPressed;
        AddChild(_button);

        _simHost.WorldSelectionChanged += OnSelectionChanged;
    }

    public override void _ExitTree()
    {
        if (_simHost != null) _simHost.WorldSelectionChanged -= OnSelectionChanged;
    }

    private void OnSelectionChanged()
    {
        if (_simHost == null || _button == null) return;
        _button.Visible = !_simHost.AwaitingWorldSelection;
    }

    private void OnPressed()
    {
        if (_simHost == null || _button == null) return;
        _button.Disabled = true;
        _button.Text = "Regenerating…";
        // Defer one frame so the "Regenerating…" label paints before the
        // synchronous worldgen stalls the main thread.
        CallDeferred(nameof(DoRegenerate));
    }

    private void DoRegenerate()
    {
        if (_simHost == null || _button == null) return;
        _simHost.Regenerate(_rng.Next());
        _button.Disabled = false;
        _button.Text = "Regenerate World";
    }
}
