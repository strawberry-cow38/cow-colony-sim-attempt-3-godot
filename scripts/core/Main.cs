using Godot;

namespace CowColonySim;

public partial class Main : Node3D
{
	public override void _Ready()
	{
		DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
		Engine.MaxFps = 0;
		GD.Print("Cow Colony Sim — attempt 3, day 0.");
	}
}
