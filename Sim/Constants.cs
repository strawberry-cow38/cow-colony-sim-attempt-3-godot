namespace CowColonySim.Sim;

public enum SimSpeed
{
    X1 = 1,
    X2 = 2,
    X3 = 3,
    X6 = 6,
}

public static class SimConstants
{
    public const int SimHz = 60;
    public const double SimDt = 1.0 / SimHz;

    public const float TileWidthMeters = 1.5f;
    public const float TileHeightMeters = 0.75f;
    public const int HeadroomTiles = 2;
    public const int ChunkSize = 16;

    // Per-tile corner Y gap (in tile-height units) above which a cliff wall
    // is emitted instead of a smooth slope. 3 tiles ≈ 2.25m. Below this the
    // corner tracks the neighbor column (smooth). At or above, the corner is
    // clamped to the tile's own column — producing a flat top and a vertical
    // drop at the tile boundary.
    public const int CliffDelta = 3;

    // Cells group chunks for streaming / tier gating. A cell is a square
    // (X/Z) slab of CellSizeChunks × CellSizeChunks chunks, spanning the
    // full vertical extent. 16 chunks = 256 tiles = 384m per side.
    public const int CellSizeChunks = 16;
    public const int CellSizeTiles = CellSizeChunks * ChunkSize;

    // Cells within this many cells of a Live cell stay at least Ambient.
    // Keeps render-radius terrain resident; paging only evicts past this halo.
    // Matches renderer MaxChunkDistance=128 chunks ÷ 16 = 8, plus 1-cell buffer.
    public const int CellRenderHaloCells = 9;

    public const int SecondsPerDay = 60 * 24;
    public const int TicksPerDay = SimHz * SecondsPerDay;

    public static readonly SimSpeed[] SpeedSteps = { SimSpeed.X1, SimSpeed.X2, SimSpeed.X3, SimSpeed.X6 };
}
