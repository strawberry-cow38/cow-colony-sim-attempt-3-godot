using System;
using System.IO;
using Godot;
using fennecs;
using CowColonySim.Sim;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Systems;
using CowColonySim.UI;

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
	public CellStore CellStore { get; }
	public CellPagingSystem Paging { get; }

	private readonly Random _rng = new(WorldSeed);

	public SimHost()
	{
		Loop = new SimLoop(Step);
		var cellsDir = Path.Combine(OS.GetUserDataDir(), "cells");
		WipeDir(cellsDir); // pre-alpha: worldgen may change between runs, don't reuse stale cells
		CellStore = new CellStore(cellsDir);
		Paging = new CellPagingSystem(CellStore);
	}

	public override void _Ready()
	{
		WorldGen.Generate(Tiles, WorldSeed, WorldSize, WorldSize);
		SeedColonyClaim();
		SeedColonists();
		ChunkTierSystem.Step(World, Tiles);
		TimeOfDay.SetTicks((long)(SimConstants.TicksPerDay * 0.30f));
		GD.Print($"SimHost ready. SimHz={SimConstants.SimHz}, speed={Loop.Speed}, chunks={Tiles.ChunkCount}, tieredChunks={Tiles.ChunkStates.Count}, cellsDir={CellStore.PathFor(new CellKey(0, 0))}.");
	}

	public override void _Process(double delta)
	{
		Loop.Advance(delta);
	}

	private void Step(int tick)
	{
		Profiler.Begin("Sim tick");
		TimeOfDay.Step();
		Profiler.Begin("Wander");
		WanderSystem.Step(World, Tiles, _rng, tick);
		Profiler.End("Wander");
		Profiler.Begin("PathPlan");
		PathPlanSystem.Step(World, Tiles);
		Profiler.End("PathPlan");
		Profiler.Begin("PathFollow");
		PathFollowSystem.Step(World, (float)SimConstants.SimDt);
		Profiler.End("PathFollow");
		if (tick % SimConstants.SimHz == 0)
		{
			Profiler.Begin("ChunkTier");
			ChunkTierSystem.Step(World, Tiles);
			Profiler.End("ChunkTier");
		}
		Profiler.Begin("Paging");
		Paging.Step(Tiles, tick);
		Profiler.End("Paging");
		Profiler.End("Sim tick");
		Profiler.IncRate("Sim ticks/s");
	}

	private static void WipeDir(string dir)
	{
		try
		{
			if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
		}
		catch (Exception e)
		{
			GD.PushWarning($"SimHost: failed to wipe {dir}: {e.Message}");
		}
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
