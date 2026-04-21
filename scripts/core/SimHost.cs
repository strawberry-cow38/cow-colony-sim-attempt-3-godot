using System;
using System.Collections.Generic;
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

	/// <summary>Emitted after Regenerate(seed) rebuilds the world. Renderers
	/// that cache per-chunk mesh state key off this to drop stale slots.</summary>
	[Signal] public delegate void WorldRegeneratedEventHandler();

	public World World { get; } = new();
	public TileWorld Tiles { get; } = new();
	public SimLoop Loop { get; }
	public TimeOfDaySystem TimeOfDay { get; } = new();
	public CellStore CellStore { get; }
	public CellPagingSystem Paging { get; }

	public int CurrentSeed { get; private set; } = WorldSeed;
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

	public void Regenerate(int seed)
	{
		CurrentSeed = seed;
		DespawnAllEntities();
		Tiles.Clear();
		// Wipe any paged-out cells on disk — they're stale against the new
		// seed. Caller owns the disk lifecycle via CellStore.
		var cellsDir = Path.Combine(OS.GetUserDataDir(), "cells");
		WipeDir(cellsDir);
		WorldGen.Generate(Tiles, seed, WorldSize, WorldSize);
		SeedColonyClaim();
		SeedColonists();
		ChunkTierSystem.Step(World, Tiles);
		EmitSignal(SignalName.WorldRegenerated);
		GD.Print($"SimHost regenerated. seed={seed}, chunks={Tiles.ChunkCount}.");
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

	private void DespawnAllEntities()
	{
		// Walk every known component marker, collect matching entities, then
		// despawn outside the Stream to avoid mutating during iteration. Every
		// entity in this codebase carries at least one of these, so the
		// distinct union covers the full live set.
		var toKill = new HashSet<Entity>();
		World.Stream<ClaimedRegion>().For((in Entity e, ref ClaimedRegion _) => toKill.Add(e));
		World.Stream<LiveAnchor>().For((in Entity e, ref LiveAnchor _) => toKill.Add(e));
		World.Stream<Colonist>().For((in Entity e, ref Colonist _) => toKill.Add(e));
		foreach (var e in toKill) e.Despawn();
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
