namespace CowColonySim.Sim.Grid;

public enum TileKind : byte
{
    Empty = 0,
    Solid = 1,
    Floor = 2,
}

public readonly record struct Tile(TileKind Kind)
{
    public static readonly Tile Empty = new(TileKind.Empty);
    public bool IsEmpty => Kind == TileKind.Empty;
}
