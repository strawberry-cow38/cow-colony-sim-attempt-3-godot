namespace CowColonySim.Sim.World;

public sealed class Chunk
{
    public const int Size = SimConstants.ChunkSize;
    private const int Volume = Size * Size * Size;

    private readonly Tile[] _tiles = new Tile[Volume];

    public Tile this[int lx, int ly, int lz]
    {
        get => _tiles[Index(lx, ly, lz)];
        set => _tiles[Index(lx, ly, lz)] = value;
    }

    public int NonEmptyCount
    {
        get
        {
            var count = 0;
            for (var i = 0; i < Volume; i++)
            {
                if (_tiles[i].Kind != TileKind.Empty) count++;
            }
            return count;
        }
    }

    private static int Index(int lx, int ly, int lz)
    {
        if ((uint)lx >= Size || (uint)ly >= Size || (uint)lz >= Size)
            throw new ArgumentOutOfRangeException($"local coord out of range: ({lx},{ly},{lz})");
        return (ly * Size + lz) * Size + lx;
    }
}
