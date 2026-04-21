namespace CowColonySim.Sim.Grid;

/// <summary>
/// Immutable copy of a <see cref="TerrainChunk"/> plus the +X / +Z / +XZ seam
/// corners borrowed from neighboring chunks. Enough data for a mesher to emit
/// 16×16 tiles' worth of corner-height quads without touching live data, so
/// meshing can run on a worker while the sim keeps mutating terrain.
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
    public readonly int Revision;

    public TerrainSnapshot(int cx, int cz, int revision)
    {
        ChunkX = cx;
        ChunkZ = cz;
        Revision = revision;
    }
}
