namespace CowColonySim.Sim.Grid;

public sealed class TileWorld
{
    private readonly Dictionary<TilePos, Chunk> _chunks = new();
    private readonly Dictionary<TilePos, ChunkState> _chunkStates = new();
    private readonly Dictionary<CellKey, ChunkState> _cellStates = new();
    private readonly Dictionary<CellKey, List<TilePos>> _chunksByCell = new();

    public int ChunkCount => _chunks.Count;

    public IEnumerable<KeyValuePair<TilePos, Chunk>> EnumerateChunks() => _chunks;

    /// <summary>Chunk keys whose XZ column falls inside the cell. Null if none.</summary>
    public IReadOnlyList<TilePos>? GetChunksInCell(CellKey key)
        => _chunksByCell.TryGetValue(key, out var list) ? list : null;

    public Chunk? GetChunkOrNull(TilePos chunkKey) => _chunks.TryGetValue(chunkKey, out var c) ? c : null;

    public ChunkState GetChunkState(TilePos chunkKey)
        => _chunkStates.TryGetValue(chunkKey, out var s) ? s : ChunkState.Dormant;

    public IReadOnlyDictionary<TilePos, ChunkState> ChunkStates => _chunkStates;

    public ChunkState GetCellState(CellKey key)
        => _cellStates.TryGetValue(key, out var s) ? s : ChunkState.Dormant;

    public IReadOnlyDictionary<CellKey, ChunkState> CellStates => _cellStates;

    public void ReplaceChunkStates(IReadOnlyDictionary<TilePos, ChunkState> next)
    {
        _chunkStates.Clear();
        foreach (var kv in next) _chunkStates[kv.Key] = kv.Value;
    }

    public void ReplaceCellStates(IReadOnlyDictionary<CellKey, ChunkState> next)
    {
        _cellStates.Clear();
        foreach (var kv in next) _cellStates[kv.Key] = kv.Value;
    }

    public Tile Get(TilePos pos)
    {
        var (chunkKey, lx, ly, lz) = Split(pos);
        return _chunks.TryGetValue(chunkKey, out var chunk) ? chunk[lx, ly, lz] : Tile.Empty;
    }

    public void Set(TilePos pos, Tile tile)
    {
        var (chunkKey, lx, ly, lz) = Split(pos);
        if (!_chunks.TryGetValue(chunkKey, out var chunk))
        {
            if (tile.IsEmpty) return;
            chunk = new Chunk();
            _chunks.Add(chunkKey, chunk);
            var cellKey = Cell.FromChunk(chunkKey);
            if (!_chunksByCell.TryGetValue(cellKey, out var list))
            {
                list = new List<TilePos>(4);
                _chunksByCell[cellKey] = list;
            }
            list.Add(chunkKey);
        }
        chunk[lx, ly, lz] = tile;
    }

    public IEnumerable<(TilePos Pos, Tile Tile)> Neighbors(TilePos pos)
    {
        yield return (pos.Offset( 1, 0, 0), Get(pos.Offset( 1, 0, 0)));
        yield return (pos.Offset(-1, 0, 0), Get(pos.Offset(-1, 0, 0)));
        yield return (pos.Offset( 0, 1, 0), Get(pos.Offset( 0, 1, 0)));
        yield return (pos.Offset( 0,-1, 0), Get(pos.Offset( 0,-1, 0)));
        yield return (pos.Offset( 0, 0, 1), Get(pos.Offset( 0, 0, 1)));
        yield return (pos.Offset( 0, 0,-1), Get(pos.Offset( 0, 0,-1)));
    }

    private static (TilePos ChunkKey, int lx, int ly, int lz) Split(TilePos pos)
    {
        const int s = Chunk.Size;
        var cx = FloorDiv(pos.X, s);
        var cy = FloorDiv(pos.Y, s);
        var cz = FloorDiv(pos.Z, s);
        var lx = pos.X - cx * s;
        var ly = pos.Y - cy * s;
        var lz = pos.Z - cz * s;
        return (new TilePos(cx, cy, cz), lx, ly, lz);
    }

    private static int FloorDiv(int a, int b) => (a / b) - (a % b < 0 ? 1 : 0);
}
