namespace CowColonySim.Sim.Grid;

/// <summary>
/// 2D heightmap + kindmap + cliff descriptors for a <see cref="Chunk.Size"/>
/// tile footprint. Heights are indexed by the south-west corner of each tile in
/// integer tile-height units (each step = <see cref="SimConstants.TileHeightMeters"/>),
/// so a chunk owns 16 corners per axis and shares one row/column with its +X / +Z
/// neighbours (whoever meshes a seam reads the neighbour chunk's edge).
///
/// Per-tile <see cref="TileKind"/> is the surface material at the top corner of
/// that tile column — Floor (grass), Sand, Water. Underground rock is implicit;
/// anything below the surface is considered solid fill and not stored.
///
/// Per-tile cliff descriptors extend the single-height-per-corner heightmap into
/// a piecewise-discontinuous surface so true vertical walls can render. Each
/// tile can flag its +X (east) and +Z (south) edges as cliff edges; when set,
/// the tile is the UPPER platform and the neighbor across that edge is the
/// lower floor. The cliff floor height stored per side is the Y the neighbor's
/// shared edge corners actually render at, even though those corners share the
/// same (upper) entry in <see cref="Heights"/>.
/// </summary>
public sealed class TerrainChunk
{
    public const int Size = Chunk.Size;

    /// <summary>
    /// Corner heights in tile units. <c>Heights[lx, lz]</c> = Y of the vertex
    /// at world-space corner (chunkX*Size + lx, chunkZ*Size + lz). This is the
    /// UPPER height at cliff edges.
    /// </summary>
    public readonly short[,] Heights = new short[Size, Size];

    /// <summary>
    /// Surface kind per tile. <c>Kinds[lx, lz]</c> = <see cref="TileKind"/> byte
    /// for the tile whose south-west corner is at (chunkX*Size + lx, chunkZ*Size + lz).
    /// </summary>
    public readonly byte[,] Kinds = new byte[Size, Size];

    /// <summary>
    /// Per-tile cliff mask. Bit 0 = east edge is a cliff (tile is upper, +X
    /// neighbor is lower). Bit 1 = south edge is a cliff (tile is upper, +Z
    /// neighbor is lower). West/North cliffs are represented as the -X / -Z
    /// neighbor's E / S flag — never stored here.
    /// </summary>
    public readonly byte[,] CliffMask = new byte[Size, Size];

    /// <summary>
    /// East-edge cliff floor height (meaningful when <see cref="CliffMask"/>
    /// bit 0 is set). The +X neighbor renders its west edge corners at this Y
    /// instead of the shared <see cref="Heights"/> value.
    /// </summary>
    public readonly short[,] CliffLowerE = new short[Size, Size];

    /// <summary>
    /// South-edge cliff floor height (meaningful when <see cref="CliffMask"/>
    /// bit 1 is set). The +Z neighbor renders its north edge corners at this Y.
    /// </summary>
    public readonly short[,] CliffLowerS = new short[Size, Size];

    public const byte CliffBitE = 1 << 0;
    public const byte CliffBitS = 1 << 1;

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

    public void SetCliffE(int lx, int lz, short lowerHeight)
    {
        var mask = (byte)(CliffMask[lx, lz] | CliffBitE);
        if (CliffMask[lx, lz] == mask && CliffLowerE[lx, lz] == lowerHeight) return;
        CliffMask[lx, lz] = mask;
        CliffLowerE[lx, lz] = lowerHeight;
        Revision++;
    }

    public void SetCliffS(int lx, int lz, short lowerHeight)
    {
        var mask = (byte)(CliffMask[lx, lz] | CliffBitS);
        if (CliffMask[lx, lz] == mask && CliffLowerS[lx, lz] == lowerHeight) return;
        CliffMask[lx, lz] = mask;
        CliffLowerS[lx, lz] = lowerHeight;
        Revision++;
    }

    public void ClearCliffE(int lx, int lz)
    {
        if ((CliffMask[lx, lz] & CliffBitE) == 0) return;
        CliffMask[lx, lz] &= unchecked((byte)~CliffBitE);
        CliffLowerE[lx, lz] = 0;
        Revision++;
    }

    public void ClearCliffS(int lx, int lz)
    {
        if ((CliffMask[lx, lz] & CliffBitS) == 0) return;
        CliffMask[lx, lz] &= unchecked((byte)~CliffBitS);
        CliffLowerS[lx, lz] = 0;
        Revision++;
    }
}
