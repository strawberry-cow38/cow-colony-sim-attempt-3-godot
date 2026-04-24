using Godot;

namespace CowColonySim.UI.Selection;

/// <summary>
/// Right-side info panel for the current <see cref="SelectionTarget"/>.
/// Shows the target name (big), a multi-line description, and one button per
/// <see cref="SelectionAction"/>. Rebuilt from scratch every
/// <see cref="SelectionController.SelectionChanged"/> because the action list
/// is small and button churn is cheaper than diff tracking.
/// </summary>
public partial class SelectionPanel : CanvasLayer
{
    private SelectionController? _controller;
    private PanelContainer _panel = null!;
    private Label _nameLabel = null!;
    private Label _descLabel = null!;
    private VBoxContainer _actionBox = null!;

    public override void _Ready()
    {
        _panel = new PanelContainer
        {
            AnchorLeft = 1.0f, AnchorRight = 1.0f,
            AnchorTop = 0.0f, AnchorBottom = 0.0f,
            OffsetLeft = -340, OffsetRight = -16,
            OffsetTop = 16, OffsetBottom = 220,
            GrowHorizontal = Control.GrowDirection.Begin,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(_panel);

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 12);
        margin.AddThemeConstantOverride("margin_right", 12);
        margin.AddThemeConstantOverride("margin_top", 10);
        margin.AddThemeConstantOverride("margin_bottom", 10);
        _panel.AddChild(margin);

        var vbox = new VBoxContainer { CustomMinimumSize = new Vector2(300, 0) };
        margin.AddChild(vbox);

        _nameLabel = new Label
        {
            Text = string.Empty,
            LabelSettings = new LabelSettings { FontSize = 22 },
        };
        vbox.AddChild(_nameLabel);

        _descLabel = new Label
        {
            Text = string.Empty,
            AutowrapMode = TextServer.AutowrapMode.Word,
        };
        vbox.AddChild(_descLabel);

        var sep = new HSeparator();
        vbox.AddChild(sep);

        _actionBox = new VBoxContainer();
        vbox.AddChild(_actionBox);

        _controller = GetNodeOrNull<SelectionController>("/root/Main/SelectionController");
        if (_controller != null) _controller.SelectionChanged += Refresh;
    }

    public override void _ExitTree()
    {
        if (_controller != null) _controller.SelectionChanged -= Refresh;
    }

    private void Refresh()
    {
        var sel = _controller?.Current;
        if (sel == null)
        {
            _panel.Visible = false;
            return;
        }
        _panel.Visible = true;
        _nameLabel.Text = sel.Name;
        _descLabel.Text = sel.Description;

        foreach (var child in _actionBox.GetChildren()) child.QueueFree();
        foreach (var action in sel.Actions)
        {
            var btn = new Button { Text = action.Label };
            var captured = action;
            btn.Pressed += () =>
            {
                captured.Invoke();
                // Re-pick so the panel reflects post-action state (e.g. Chop
                // flipped MarkedJobId and the action list should flip to
                // Cancel). Cheapest path: refire SelectionChanged after a
                // controller refresh.
                _controller?.Refresh();
            };
            _actionBox.AddChild(btn);
        }
    }
}
