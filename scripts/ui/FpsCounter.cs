using Godot;

namespace CowColonySim.UI;

public partial class FpsCounter : CanvasLayer
{
	private Label? _label;

	public override void _Ready()
	{
		Layer = 10;
		_label = new Label
		{
			Text = "FPS: 0",
			Position = new Vector2(8, 6),
			Modulate = new Color(1.0f, 0.92f, 0.10f),
		};
		_label.AddThemeColorOverride("font_color", new Color(1.0f, 0.92f, 0.10f));
		_label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
		_label.AddThemeConstantOverride("outline_size", 4);
		_label.AddThemeFontSizeOverride("font_size", 18);
		AddChild(_label);
	}

	public override void _Process(double delta)
	{
		if (_label == null) return;
		var fps = Engine.GetFramesPerSecond();
		_label.Text = $"FPS: {fps:0}";
	}
}
