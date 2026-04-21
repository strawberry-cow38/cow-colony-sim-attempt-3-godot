using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Systems;

/// <summary>
/// Cell-tier rate gating helpers. Systems that iterate entities or tiles can
/// call <see cref="ShouldStep"/> to throttle themselves based on which cell
/// the work belongs to:
///   Live    → every tick (full fidelity)
///   Ambient → every AmbientEveryN ticks (snail pace)
///   Dormant → never (frozen)
/// </summary>
public static class CellGating
{
    public const int AmbientEveryN = SimConstants.SimHz; // 60 → 1Hz

    public static bool ShouldStep(ChunkState cellState, long tick)
    {
        return cellState switch
        {
            ChunkState.Live => true,
            ChunkState.Ambient => tick % AmbientEveryN == 0,
            _ => false,
        };
    }

    public static bool ShouldStepForTile(TileWorld tiles, TilePos tile, long tick)
        => ShouldStep(tiles.GetCellState(Cell.FromTile(tile)), tick);
}
