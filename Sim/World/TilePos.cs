namespace CowColonySim.Sim.World;

public readonly record struct TilePos(int X, int Y, int Z)
{
    public TilePos Offset(int dx, int dy, int dz) => new(X + dx, Y + dy, Z + dz);

    public static TilePos operator +(TilePos a, TilePos b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static TilePos operator -(TilePos a, TilePos b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
}
