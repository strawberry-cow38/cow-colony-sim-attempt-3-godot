using System;
using System.Collections.Generic;
using Godot;
using fennecs;
using CowColonySim.Sim;
using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Systems;
using CowColonySim.UI;

namespace CowColonySim;

public partial class SimHost : Node
{
	// Phase 1 of the worldmap pivot: the playable world is a single 256×256
	// tile cell (~384m square). The larger world lives in a 2D WorldMap
	// (phase 2) and the cell you're playing is generated on demand from
	// it. No streaming, no paging, no cells-on-disk — the whole playable
	// region lives in memory.
	public const int WorldSize = Cell.SizeTiles;
	public const int ColonyClaimRadius = 24;
	public const int WorldSeed = 0xC0FFEE;

	/// <summary>Emitted after Regenerate(seed) rebuilds the world. Renderers
	/// that cache per-chunk mesh state key off this to drop stale slots.</summary>
	[Signal] public delegate void WorldRegeneratedEventHandler();

	public World World { get; } = new();
	public TileWorld Tiles { get; } = new();
	public SimLoop Loop { get; }
	public TimeOfDaySystem TimeOfDay { get; } = new();

	public int CurrentSeed { get; private set; } = WorldSeed;
	private readonly Random _rng = new(WorldSeed);

	/// <summary>Abstract 100×100 overworld. Biome + climate for every
	/// potential cell is classified once here and the playable 3D region
	/// is generated from whichever map cell the player is currently
	/// standing on (phase 3 wires the actual pull).</summary>
	public WorldMap Overworld { get; private set; } = null!;

	/// <summary>Map coord the 3D playable region currently represents.
	/// Defaults to the center of the world map on new games. Eventually
	/// travel actions will move this and trigger a regen.</summary>
	public WorldMapCoord CurrentMapCoord { get; private set; } = CowColonySim.Sim.Grid.WorldMap.Center;

	public SimHost()
	{
		Loop = new SimLoop(Step);
	}

	public override void _Ready()
	{
		BuiltinBiomes.RegisterAll();
		Overworld = WorldMapGenerator.Generate(WorldSeed);
		WorldGen.Generate(Tiles, WorldSeed, WorldSize, WorldSize);
		SeedColonyClaim();
		SeedColonists();
		ChunkTierSystem.Step(World, Tiles);
		TimeOfDay.SetTicks(CalendarSystem.StartTicksOffset);
		LogStartup();
	}

	public void Regenerate(int seed)
	{
		CurrentSeed = seed;
		DespawnAllEntities();
		Tiles.Clear();
		Overworld = WorldMapGenerator.Generate(seed);
		WorldGen.Generate(Tiles, seed, WorldSize, WorldSize);
		SeedColonyClaim();
		SeedColonists();
		ChunkTierSystem.Step(World, Tiles);
		EmitSignal(SignalName.WorldRegenerated);
		GD.Print($"SimHost regenerated. seed={seed}, chunks={Tiles.ChunkCount}.");
		LogCurrentMapCell();
	}

	private void LogStartup()
	{
		GD.Print($"SimHost ready. SimHz={SimConstants.SimHz}, speed={Loop.Speed}, chunks={Tiles.ChunkCount}, tieredChunks={Tiles.ChunkStates.Count}.");
		LogCurrentMapCell();
	}

	private void LogCurrentMapCell()
	{
		var c = Overworld.Get(CurrentMapCoord);
		var biome = BiomeRegistry.Get(c.BiomeId);
		GD.Print($"WorldMap at ({CurrentMapCoord.X},{CurrentMapCoord.Z}): biome={biome.Name}, temp={c.TemperatureC:0.0}C, rain={c.RainfallMm:0}mm.");
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
