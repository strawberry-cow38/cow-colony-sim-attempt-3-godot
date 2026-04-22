namespace CowColonySim.Sim.Grid;

/// <summary>
/// Per-tile corner heightmap + kindmap for a <see cref="Chunk.Size"/>×<see cref="Chunk.Size"/>
/// tile footprint. Each tile carries its own four render corners (SW/SE/NE/NW)
/// plus a column floor height used by pathfinding.
///
/// Decoupling render corners from shared grid vertices is what makes cliffs work.
/// When adjacent tiles' facing corners happen to match — as they do on gentle
/// terrain — vertices line up and the surface looks smooth. When they disagree
/// (worldgen clamps a plateau tile's corners to its own column; the neighbor's
/// corners stay at natural column height), the mesher sees a gap and plasters
/// a vertical wall quad over it. No special-case cliff flag; cliff = emergent.
///
/// Heights are in integer tile-height units (step = <see cref="SimConstants.TileHeightMeters"/>).
/// </summary>
public sealed class TerrainChunk
{
    public const int Size = Chunk.Size;

    // Corner indices matching the mesher's winding (SW→SE→NE→NW).
    public const int SW = 0;
    public const int SE = 1;
    public const int NE = 2;
    public const int NW = 3;

    /// <summary>
    /// Column floor Y per tile in tile-units. <c>ColumnHeights[lx, lz]</c> is
    /// the walkable surface Y of tile (chunkX*Size + lx, chunkZ*Size + lz).
    /// Pathfinding reads this; the mesher reads <see cref="Corners"/>.
    /// </summary>
    public readonly short[,] ColumnHeights = new short[Size, Size];

    /// <summary>
    /// Per-tile render corners. <c>Corners[lx, lz, c]</c> where
    /// <c>c ∈ {SW, SE, NE, NW}</c>. Smooth continuity between adjacent tiles
    /// emerges when their facing corners hold the same Y; cliff walls emerge
    /// when they don't.
    /// </summary>
    public readonly short[,,] Corners = new short[Size, Size, 4];

    /// <summary>
    /// Surface kind per tile.
    /// </summary>
    public readonly byte[,] Kinds = new byte[Size, Size];

    /// <summary>
    /// Per-column water-surface Y in tile-height units. Only meaningful when
    /// <see cref="Kinds"/> is <see cref="TileKind.Water"/>; otherwise ignored.
    /// Default 0 means the global sea level (<c>WorldGen.WaterLevelY</c>).
    /// Rivers at elevation overwrite this with their local water-top so the
    /// mesher can emit water planes that actually sit on the river bed.
    /// </summary>
    public readonly short[,] WaterTops = new short[Size, Size];

    public int Revision { get; private set; }

    public void SetColumnHeight(int lx, int lz, short h)
    {
        if (ColumnHeights[lx, lz] == h) return;
        ColumnHeights[lx, lz] = h;
        Revision++;
    }

    public void SetCorners(int lx, int lz, short sw, short se, short ne, short nw)
    {
        var changed = Corners[lx, lz, SW] != sw
                   || Corners[lx, lz, SE] != se
                   || Corners[lx, lz, NE] != ne
                   || Corners[lx, lz, NW] != nw;
        if (!changed) return;
        Corners[lx, lz, SW] = sw;
        Corners[lx, lz, SE] = se;
        Corners[lx, lz, NE] = ne;
        Corners[lx, lz, NW] = nw;
        Revision++;
    }

    public void SetKind(int lx, int lz, byte kind)
    {
        if (Kinds[lx, lz] == kind) return;
        Kinds[lx, lz] = kind;
        Revision++;
    }

    public void SetWaterTop(int lx, int lz, short y)
    {
        if (WaterTops[lx, lz] == y) return;
        WaterTops[lx, lz] = y;
        Revision++;
    }
}
