namespace CowColonySim.Sim.Grid;

/// <summary>
/// XZ address of a streaming cell. A cell spans <see cref="SimConstants.CellSizeChunks"/>
/// chunks on X and Z and the full world on Y — the world is effectively 2D for
/// streaming purposes because colonists care about horizontal distance.
/// </summary>
public readonly record struct CellKey(int X, int Z)
{
    public CellKey Offset(int dx, int dz) => new(X + dx, Z + dz);
}

public static class Cell
{
    public const int SizeChunks = SimConstants.CellSizeChunks;
    public const int SizeTiles = SimConstants.CellSizeTiles;

    public static CellKey FromChunk(TilePos chunkKey)
        => new(FloorDiv(chunkKey.X, SizeChunks), FloorDiv(chunkKey.Z, SizeChunks));

    public static CellKey FromTile(TilePos tilePos)
        => new(FloorDiv(tilePos.X, SizeTiles), FloorDiv(tilePos.Z, SizeTiles));

    /// <summary>
    /// Yields every cell whose XZ footprint overlaps the bounds. Y is
    /// ignored because cells span full height.
    /// </summary>
    public static IEnumerable<CellKey> CellsTouchedByBounds(TilePos min, TilePos max)
    {
        var cxMin = FloorDiv(min.X, SizeTiles); var cxMax = FloorDiv(max.X, SizeTiles);
        var czMin = FloorDiv(min.Z, SizeTiles); var czMax = FloorDiv(max.Z, SizeTiles);
        for (var cx = cxMin; cx <= cxMax; cx++)
        for (var cz = czMin; cz <= czMax; cz++)
            yield return new CellKey(cx, cz);
    }

    private static int FloorDiv(int a, int b) => (a / b) - (a % b < 0 ? 1 : 0);
}
