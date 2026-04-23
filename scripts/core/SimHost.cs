using System;
using System.Collections.Generic;
using Godot;
using fennecs;
using CowColonySim.Sim;
using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Crops;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Jobs;
using CowColonySim.Sim.Pathfinding;
using CowColonySim.Sim.Systems;
using CowColonySim.UI;

namespace CowColonySim;

public partial class SimHost : Node
{
	// Playable pocket is one overworld cell (256×256 tiles ≈ 384m). The
	// world WorldGen actually populates is a 3×3 neighborhood of cells
	// (768×768 tiles) so GridRenderer's G8 LOD tier can render the 8
	// surrounding cells as coarse terrain without a separate backdrop.
	// The pocket stays centered on (0,0); neighbor subregions get stamped
	// with their own overworld biome/climate.
	public const int PocketSize = Cell.SizeTiles;
	public const int WorldSize = PocketSize * 3;
	public const int ColonyClaimRadius = 24;
	public const int WorldSeed = 0xC0FFEE;

	/// <summary>Emitted after Regenerate(seed) rebuilds the world. Renderers
	/// that cache per-chunk mesh state key off this to drop stale slots.</summary>
	[Signal] public delegate void WorldRegeneratedEventHandler();

	/// <summary>Emitted whenever <see cref="AwaitingWorldSelection"/> flips.
	/// UI layers key off this to show/hide the fullscreen world-select panel
	/// and gate in-sim widgets that can't run before settlement.</summary>
	[Signal] public delegate void WorldSelectionChangedEventHandler();

	/// <summary>True between boot/regen and the first <see cref="SettleAt"/>
	/// call. While true the sim loop is paused and no tiles/entities exist —
	/// only <see cref="Overworld"/> is populated. Flips false once the player
	/// picks a land cell on the world map.</summary>
	public bool AwaitingWorldSelection { get; private set; } = true;

	public World World { get; } = new();
	public TileWorld Tiles { get; } = new();
	public SimLoop Loop { get; }
	public TimeOfDaySystem TimeOfDay { get; } = new();
	public JobBoard JobBoard { get; private set; } = null!;

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
		BuiltinCrops.RegisterAll();
		Overworld = WorldMapGenerator.Generate(WorldSeed);
		TimeOfDay.SetTicks(CalendarSystem.StartTicksOffset);
		LogStartup();
		EmitSignal(SignalName.WorldSelectionChanged);
	}

	/// <summary>Drop current pocket + overworld and roll a fresh world under
	/// <paramref name="seed"/>. Returns to selection mode — caller must
	/// <see cref="SettleAt"/> a land cell before the sim will tick again.</summary>
	public void Regenerate(int seed)
	{
		CurrentSeed = seed;
		DespawnAllEntities();
		Tiles.Clear();
		Overworld = WorldMapGenerator.Generate(seed);
		AwaitingWorldSelection = true;
		EmitSignal(SignalName.WorldRegenerated);
		EmitSignal(SignalName.WorldSelectionChanged);
		GD.Print($"SimHost overworld regenerated. seed={seed}.");
	}

	/// <summary>Commit <paramref name="coord"/> as the playable pocket. Ocean
	/// cells reject (returns false) per "no pocket on ocean" rule; land cells
	/// generate the 3×3 tile neighborhood, seed the colony, resume the sim.</summary>
	public bool SettleAt(WorldMapCoord coord)
	{
		if (!WorldMap.InBounds(coord.X, coord.Z)) return false;
		if (Overworld.IsOcean(coord)) return false;
		CurrentMapCoord = coord;
		DespawnAllEntities();
		Tiles.Clear();
		WorldGen.Generate(Tiles, CurrentSeed, WorldSize, WorldSize,
			overworld: Overworld, center: CurrentMapCoord);
		var half = WorldSize / 2;
		JobBoard = new JobBoard(
			new TilePos(-half, 0, -half),
			new TilePos(half - 1, 0, half - 1));
		SeedColonyClaim();
		SeedColonists();
		var treeCount = TreeScatter.Populate(World, Tiles, PocketSize / 2, _rng);
		GD.Print($"SimHost seeded {treeCount} trees.");
		ChunkTierSystem.Step(World, Tiles);
		AwaitingWorldSelection = false;
		EmitSignal(SignalName.WorldRegenerated);
		EmitSignal(SignalName.WorldSelectionChanged);
		GD.Print($"SimHost settled. seed={CurrentSeed}, coord=({coord.X},{coord.Z}), chunks={Tiles.ChunkCount}.");
		LogCurrentMapCell();
		return true;
	}

	private void LogStartup()
	{
		GD.Print($"SimHost ready. SimHz={SimConstants.SimHz}, speed={Loop.Speed}. Awaiting world selection.");
	}

	private void LogCurrentMapCell()
	{
		var c = Overworld.Get(CurrentMapCoord);
		var biome = BiomeRegistry.Get(c.BiomeId);
		GD.Print($"WorldMap at ({CurrentMapCoord.X},{CurrentMapCoord.Z}): biome={biome.Name}, temp={c.TemperatureC:0.0}C, rain={c.RainfallMm:0}mm.");
	}

	public override void _Process(double delta)
	{
		if (AwaitingWorldSelection) return;
		Loop.Advance(delta);
	}

	private void Step(int tick)
	{
		Profiler.Begin("Sim tick");
		TimeOfDay.Step();
		Profiler.Begin("Jobs");
		JobSystem.Step(World, JobBoard, tick);
		Profiler.End("Jobs");
		Profiler.Begin("Wander");
		WanderSystem.Step(World, Tiles, _rng, tick);
		Profiler.End("Wander");
		Profiler.Begin("PathPlan");
		PathPlanSystem.Step(World, Tiles);
		Profiler.End("PathPlan");
		Profiler.Begin("PathFollow");
		PathFollowSystem.Step(World, (float)SimConstants.SimDt);
		Profiler.End("PathFollow");
		Profiler.Begin("CropGrowth");
		CropGrowthSystem.Step(World);
		Profiler.End("CropGrowth");
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
		World.Stream<Crop>().For((in Entity e, ref Crop _) => toKill.Add(e));
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
		byte bucket = 0;
		foreach (var (x, z) in spawnXZ)
		{
			var y = WorldGen.SurfaceY(Tiles, x, z);
			var cow = World.Spawn();
			cow.Add(new Colonist());
			cow.Add(TileMath.FeetOfTile(new TilePos(x, y, z)));
			cow.Add(CurrentJob.None);
			// Distribute starting stagger buckets across colonists so their
			// job re-evals don't land on the same tick.
			cow.Add(JobEvalState.Fresh((byte)(bucket++ % JobSystem.StaggerPeriod)));
			cow.Add(new JobDirty());
		}
	}
}
