namespace CowColonySim.Sim.Grid;

public sealed class Chunk
{
    public const int Size = SimConstants.ChunkSize;
    public const int Volume = Size * Size * Size;

    private readonly Tile[] _tiles = new Tile[Volume];

    public int Revision { get; private set; }

    public Tile this[int lx, int ly, int lz]
    {
        get => _tiles[Index(lx, ly, lz)];
        set
        {
            var i = Index(lx, ly, lz);
            if (_tiles[i].Equals(value)) return;
            _tiles[i] = value;
            Revision++;
        }
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

    public ChunkSnapshot Snapshot()
    {
        var copy = new Tile[Volume];
        Array.Copy(_tiles, copy, Volume);
        return new ChunkSnapshot(copy, Revision);
    }

    public static int IndexOf(int lx, int ly, int lz) => Index(lx, ly, lz);

    private static int Index(int lx, int ly, int lz)
    {
        if ((uint)lx >= Size || (uint)ly >= Size || (uint)lz >= Size)
            throw new ArgumentOutOfRangeException($"local coord out of range: ({lx},{ly},{lz})");
        return (ly * Size + lz) * Size + lx;
    }
}

public readonly struct ChunkSnapshot
{
    public readonly Tile[] Tiles;
    public readonly int Revision;

    public ChunkSnapshot(Tile[] tiles, int revision)
    {
        Tiles = tiles;
        Revision = revision;
    }

    public Tile this[int lx, int ly, int lz] => Tiles[Chunk.IndexOf(lx, ly, lz)];
}
