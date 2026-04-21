namespace CowColonySim.Sim.Grid;

/// <summary>
/// 2D heightmap + kindmap for a <see cref="Chunk.Size"/>×<see cref="Chunk.Size"/>
/// tile footprint. Heights are indexed by the south-west corner of each tile in
/// integer tile-height units (each step = <see cref="SimConstants.TileHeightMeters"/>),
/// so a chunk owns 16 corners per axis and shares one row/column with its +X / +Z
/// neighbours (whoever meshes a seam reads the neighbour chunk's edge).
///
/// Per-tile <see cref="TileKind"/> is the surface material at the top corner of
/// that tile column — Floor (grass), Sand, Water. Underground rock is implicit;
/// anything below the surface is considered solid fill and not stored.
/// </summary>
public sealed class TerrainChunk
{
    public const int Size = Chunk.Size;

    /// <summary>
    /// Corner heights in tile units. <c>Heights[lx, lz]</c> = Y of the vertex
    /// at world-space corner (chunkX*Size + lx, chunkZ*Size + lz).
    /// </summary>
    public readonly short[,] Heights = new short[Size, Size];

    /// <summary>
    /// Surface kind per tile. <c>Kinds[lx, lz]</c> = <see cref="TileKind"/> byte
    /// for the tile whose south-west corner is at (chunkX*Size + lx, chunkZ*Size + lz).
    /// </summary>
    public readonly byte[,] Kinds = new byte[Size, Size];

    public int Revision { get; private set; }

    public void SetHeight(int lx, int lz, short h)
    {
        if (Heights[lx, lz] == h) return;
        Heights[lx, lz] = h;
        Revision++;
    }

    public void SetKind(int lx, int lz, byte kind)
    {
        if (Kinds[lx, lz] == kind) return;
        Kinds[lx, lz] = kind;
        Revision++;
    }
}
