namespace CowColonySim.Sim.Grid;

/// <summary>
/// Immutable copy of a <see cref="TerrainChunk"/>'s per-tile corners + kinds,
/// plus the corner rows from the +X / +Z neighbor tiles needed to resolve
/// cliff walls at the chunk's east and north boundaries. A mesher worker can
/// build the full 16×16 tile mesh (tops + walls) without touching live data.
///
/// Cliff walls are emergent: wherever the owning tile's facing corners differ
/// from the neighbor tile's opposite corners, the mesher emits a vertical
/// quad. Inside the chunk, each tile reads its east neighbor from its own
/// <see cref="Corners"/> array. At the +X / +Z seams, it reads from the rim
/// arrays below.
/// </summary>
public sealed class TerrainSnapshot
{
    public const int Size = Chunk.Size;
    public readonly int ChunkX;
    public readonly int ChunkZ;

    // Own-tile corners. Corners[lx, lz, c] for c ∈ {SW, SE, NE, NW}.
    public readonly short[,,] Corners = new short[Size, Size, 4];

    // Surface kind per tile.
    public readonly byte[,] Kinds = new byte[Size, Size];

    // Water-surface Y per tile, in tile-height units. Meaningful only for
    // tiles with Kind == Water; rivers at elevation write their local bed+1
    // here so the mesher can emit a water plane that sits on the river bed
    // rather than at global sea level.
    public readonly short[,] WaterTops = new short[Size, Size];

    // East rim — for my tile at lx = Size-1, compare my SE/NE against the
    // +X neighbor's tile at lx=0 SW/NW. EastRim[lz, 0] = neighbor SW,
    // EastRim[lz, 1] = neighbor NW. When no +X neighbor exists (world edge),
    // rim mirrors my own east-edge corners so the comparison produces no wall.
    public readonly short[,] EastRim = new short[Size, 2];

    // North rim — for my tile at lz = Size-1, compare my NW/NE against the
    // +Z neighbor's tile at lz=0 SW/SE. NorthRim[lx, 0] = neighbor SW,
    // NorthRim[lx, 1] = neighbor SE.
    public readonly short[,] NorthRim = new short[Size, 2];

    // Per-row neighbor kind + water-top at the east / north chunk seams, so
    // the mesher can detect water↔water boundaries across chunk edges (for
    // waterfall curtains). When no neighbor chunk exists, kind defaults to
    // Empty (0) so no curtain emits at the world border.
    public readonly byte[]  EastRimKind     = new byte[Size];
    public readonly short[] EastRimWaterTop = new short[Size];
    public readonly byte[]  NorthRimKind    = new byte[Size];
    public readonly short[] NorthRimWaterTop = new short[Size];

    public readonly int Revision;

    public TerrainSnapshot(int cx, int cz, int revision)
    {
        ChunkX = cx;
        ChunkZ = cz;
        Revision = revision;
    }
}
