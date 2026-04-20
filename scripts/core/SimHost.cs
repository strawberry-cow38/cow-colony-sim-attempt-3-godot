using Godot;
using fennecs;
using CowColonySim.Sim;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Systems;

namespace CowColonySim;

public partial class SimHost : Node
{
	public World World { get; } = new();
	public TileWorld Tiles { get; } = new();
	public SimLoop Loop { get; }

	public SimHost()
	{
		Loop = new SimLoop(Step);
	}

	public override void _Ready()
	{
		World.Spawn().Add(new Position(0, 0));
		SeedDemoPyramid();
		GD.Print($"SimHost ready. SimHz={SimConstants.SimHz}, speed={Loop.Speed}, chunks={Tiles.ChunkCount}.");
	}

	public override void _Process(double delta)
	{
		Loop.Advance(delta);
	}

	private void Step(int tick)
	{
		DemoWanderSystem.Step(World);
	}

	private void SeedDemoPyramid()
	{
		const int steps = 8;
		for (var step = 0; step < steps; step++)
		{
			var half = steps - step;
			for (var x = -half; x <= half; x++)
			for (var z = -half; z <= half; z++)
			{
				Tiles.Set(new TilePos(x, step, z), new Tile(step == steps - 1 ? TileKind.Floor : TileKind.Solid));
			}
		}
	}
}
