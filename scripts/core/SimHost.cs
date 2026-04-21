using System;
using Godot;
using fennecs;
using CowColonySim.Sim;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Systems;

namespace CowColonySim;

public partial class SimHost : Node
{
	public const int WorldSize = 1600;
	public const int ColonyClaimRadius = 24;
	public const int WorldSeed = 0xC0FFEE;

	public World World { get; } = new();
	public TileWorld Tiles { get; } = new();
	public SimLoop Loop { get; }
	public TimeOfDaySystem TimeOfDay { get; } = new();

	private readonly Random _rng = new(WorldSeed);

	public SimHost()
	{
		Loop = new SimLoop(Step);
	}

	public override void _Ready()
	{
		WorldGen.Generate(Tiles, WorldSeed, WorldSize, WorldSize);
		SeedColonyClaim();
		SeedColonists();
		ChunkTierSystem.Step(World, Tiles);
		TimeOfDay.SetTicks((long)(SimConstants.TicksPerDay * 0.30f));
		GD.Print($"SimHost ready. SimHz={SimConstants.SimHz}, speed={Loop.Speed}, chunks={Tiles.ChunkCount}, tieredChunks={Tiles.ChunkStates.Count}.");
	}

	public override void _Process(double delta)
	{
		Loop.Advance(delta);
	}

	private void Step(int tick)
	{
		TimeOfDay.Step();
		WanderSystem.Step(World, Tiles, _rng, tick);
		PathPlanSystem.Step(World, Tiles);
		PathFollowSystem.Step(World, (float)SimConstants.SimDt);
		if (tick % SimConstants.SimHz == 0) ChunkTierSystem.Step(World, Tiles);
	}

	private void SeedColonyClaim()
	{
		var r = ColonyClaimRadius;
		World.Spawn().Add(new ClaimedRegion(
			new TilePos(-r, 0, -r),
			new TilePos(r - 1, 32, r - 1),
			ChunkState.Live));
	}

	private void SeedColonists()
	{
		(int x, int z)[] spawnXZ = { (-1, -1), (0, 0), (1, 1) };
		foreach (var (x, z) in spawnXZ)
		{
			var y = WorldGen.SurfaceY(Tiles, x, z);
			var cow = World.Spawn();
			cow.Add(new Colonist());
			cow.Add(TileMath.FeetOfTile(new TilePos(x, y, z)));
		}
	}
}
