using Godot;
using CowColonySim.Sim.Grid;

namespace CowColonySim;

public partial class Main : Node3D
{
	private bool _screenshotPending;
	private string _screenshotPath = "";
	private int _screenshotDelayFrames;

	public override void _Ready()
	{
		Engine.MaxFps = 0;
		GD.Print("Cow Colony Sim — attempt 3, day 0.");
		ProcessCommandLine();
	}

	private void ProcessCommandLine()
	{
		var args = OS.GetCmdlineArgs();
		var autoSettle = false;
		foreach (var raw in args)
		{
			if (raw == "--settle-at-center") autoSettle = true;
			else if (raw.StartsWith("--screenshot="))
			{
				_screenshotPending = true;
				_screenshotPath = raw.Substring("--screenshot=".Length);
			}
			else if (raw.StartsWith("--after-frames="))
			{
				int.TryParse(raw.Substring("--after-frames=".Length), out _screenshotDelayFrames);
			}
		}
		if (autoSettle)
		{
			CallDeferred(nameof(AutoSettle));
		}
	}

	private void AutoSettle()
	{
		var sim = GetNode<SimHost>("/root/SimHost");
		sim.SettleAt(WorldMap.Center);
		GD.Print("Main: auto-settled at world center.");
	}

	public override void _Process(double delta)
	{
		if (!_screenshotPending) return;
		if (_screenshotDelayFrames > 0) { _screenshotDelayFrames--; return; }
		_screenshotPending = false;
		var img = GetViewport().GetTexture().GetImage();
		var err = img.SavePng(_screenshotPath);
		GD.Print($"Main: saved screenshot to {_screenshotPath} err={err}.");
		GetTree().Quit();
	}
}
