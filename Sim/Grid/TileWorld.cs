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

    /// <summary>
    /// Remove every chunk belonging to the cell and return them so the caller
    /// can persist them. Returns null if the cell has no chunks.
    /// </summary>
    public List<(TilePos ChunkKey, Chunk Chunk)>? TryEvictCell(CellKey key)
    {
        if (!_chunksByCell.TryGetValue(key, out var list) || list.Count == 0)
            return null;
        var evicted = new List<(TilePos, Chunk)>(list.Count);
        foreach (var ck in list)
        {
            if (_chunks.TryGetValue(ck, out var chunk))
            {
                evicted.Add((ck, chunk));
                _chunks.Remove(ck);
            }
        }
        _chunksByCell.Remove(key);
        return evicted;
    }

    /// <summary>
    /// Reinstate chunks that were previously evicted. Throws if the cell
    /// already holds chunks in memory — callers must evict first.
    /// </summary>
    public void InstallCell(CellKey key, IReadOnlyList<(TilePos ChunkKey, Chunk Chunk)> chunks)
    {
        if (_chunksByCell.ContainsKey(key))
            throw new InvalidOperationException($"cell {key} still has in-memory chunks");
        if (chunks.Count == 0) return;
        var list = new List<TilePos>(chunks.Count);
        foreach (var (ck, chunk) in chunks)
        {
            _chunks[ck] = chunk;
            list.Add(ck);
        }
        _chunksByCell[key] = list;
    }

    public bool CellHasChunks(CellKey key) => _chunksByCell.ContainsKey(key);

    public IEnumerable<CellKey> InMemoryCells => _chunksByCell.Keys;

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
