namespace CowColonySim.Sim.Grid;

/// <summary>
/// Immutable copy of a <see cref="TerrainChunk"/> plus the +X / +Z / +XZ seam
/// corners borrowed from neighboring chunks. Enough data for a mesher to emit
/// 16×16 tiles' worth of corner-height quads without touching live data, so
/// meshing can run on a worker while the sim keeps mutating terrain.
///
/// Cliff descriptors are consolidated per-tile into a 4-bit mask (E/S/W/N) plus
/// four lower-height arrays. The mesher reads the consolidated view so it
/// doesn't have to reach across chunk boundaries. W/N bits are derived from
/// the -X / -Z neighbor chunk's E / S flags.
/// </summary>
public sealed class TerrainSnapshot
{
    public const int Size = Chunk.Size;
    public readonly int ChunkX;
    public readonly int ChunkZ;
    // 17×17 corner heights. Heights[lx, lz] where lx/lz ∈ [0, 16]. The chunk
    // owns its 16×16 SW corners; the +X / +Z / +XZ corners are copied from
    // whichever neighbor chunk owns them (falling back to the edge value if
    // no neighbor exists).
    public readonly short[,] Heights = new short[Size + 1, Size + 1];
    // 16×16 tile surface kinds — Kinds[lx, lz] for tile (cx*Size+lx, cz*Size+lz).
    public readonly byte[,] Kinds = new byte[Size, Size];

    // Per-tile consolidated cliff mask. Bit 0 = E edge, 1 = S, 2 = W, 3 = N.
    // W / N come from the -X / -Z neighbor chunk's E / S flags so the mesher
    // can handle all four directions from one array.
    public readonly byte[,] CliffMask = new byte[Size, Size];
    // Per-tile cliff floor heights for each edge, meaningful when the matching
    // mask bit is set. W / N values are mirrored from the neighbor chunk's
    // owning tile so the snapshot is self-contained for rendering.
    public readonly short[,] CliffLowerE = new short[Size, Size];
    public readonly short[,] CliffLowerS = new short[Size, Size];
    public readonly short[,] CliffLowerW = new short[Size, Size];
    public readonly short[,] CliffLowerN = new short[Size, Size];

    public const byte CliffBitE = 1 << 0;
    public const byte CliffBitS = 1 << 1;
    public const byte CliffBitW = 1 << 2;
    public const byte CliffBitN = 1 << 3;

    public readonly int Revision;

    public TerrainSnapshot(int cx, int cz, int revision)
    {
        ChunkX = cx;
        ChunkZ = cz;
        Revision = revision;
    }
}
