using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Components;

public record struct ClaimedRegion(TilePos Min, TilePos Max, ChunkState MinTier);
