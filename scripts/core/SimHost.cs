using Godot;
using fennecs;
using CowColonySim.Sim;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Systems;

namespace CowColonySim;

public partial class SimHost : Node
{
	public World World { get; } = new();
	public SimLoop Loop { get; }

	public SimHost()
	{
		Loop = new SimLoop(Step);
	}

	public override void _Ready()
	{
		World.Spawn().Add(new Position(0, 0));
		GD.Print($"SimHost ready. SimHz={SimConstants.SimHz}, speed={Loop.Speed}.");
	}

	public override void _Process(double delta)
	{
		Loop.Advance(delta);
	}

	private void Step(int tick)
	{
		DemoWanderSystem.Step(World);
	}
}
