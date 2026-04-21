namespace CowColonySim.Sim.Grid;

public sealed class TileWorld
{
    private readonly Dictionary<TilePos, Chunk> _chunks = new();
    private readonly Dictionary<TilePos, ChunkState> _chunkStates = new();
    private readonly Dictionary<CellKey, ChunkState> _cellStates = new();
    private readonly Dictionary<CellKey, List<TilePos>> _chunksByCell = new();

    // 2D terrain heightmap + kindmap keyed by chunk XZ. Exists alongside the
    // 3D voxel grid — buildings/walls/stairs still live in _chunks, terrain
    // itself (ground surface, slope, sand/water/grass) lives here. Paging
    // eviction currently only touches voxel chunks; terrain stays resident.
    private readonly Dictionary<(int cx, int cz), TerrainChunk> _terrainChunks = new();

    public int ChunkCount => _chunks.Count;
    public int TerrainChunkCount => _terrainChunks.Count;
    public IEnumerable<KeyValuePair<(int cx, int cz), TerrainChunk>> EnumerateTerrainChunks() => _terrainChunks;

    // Monotonic counter bumped whenever a tile mutates, a chunk is paged in, or
    // a chunk is evicted. Consumers (notably the renderer) snapshot this and
    // can skip per-chunk walks when the value is unchanged since last frame —
    // steady-state has no per-chunk work at all.
    public long MutationTick { get; private set; }

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
        if (evicted.Count > 0) MutationTick++;
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
        MutationTick++;
    }

    public bool CellHasChunks(CellKey key) => _chunksByCell.ContainsKey(key);

    /// <summary>
    /// Wipe every chunk, cell list, and per-chunk/per-cell tier. For the
    /// "regenerate world" path — the caller then reruns WorldGen on the
    /// empty TileWorld.
    /// </summary>
    public void Clear()
    {
        _chunks.Clear();
        _chunkStates.Clear();
        _cellStates.Clear();
        _chunksByCell.Clear();
        _terrainChunks.Clear();
        MutationTick++;
    }

    // ---- Terrain heightmap API -------------------------------------------

    /// <summary>
    /// Corner height at world-space (x, z) in integer tile-height units
    /// (one unit = <see cref="SimConstants.TileHeightMeters"/>). Returns 0 if
    /// the owning terrain chunk is not resident.
    /// </summary>
    public short TerrainHeightAt(int x, int z)
    {
        var (cx, cz, lx, lz) = SplitXZ(x, z);
        return _terrainChunks.TryGetValue((cx, cz), out var tc) ? tc.Heights[lx, lz] : (short)0;
    }

    /// <summary>
    /// Surface kind for tile (x, z). Tile (x, z) spans corners
    /// (x, z)→(x+1, z+1). Returns <see cref="TileKind.Empty"/> if the chunk
    /// isn't resident.
    /// </summary>
    public TileKind TerrainKindAt(int x, int z)
    {
        var (cx, cz, lx, lz) = SplitXZ(x, z);
        return _terrainChunks.TryGetValue((cx, cz), out var tc) ? (TileKind)tc.Kinds[lx, lz] : TileKind.Empty;
    }

    /// <summary>
    /// Max absolute Δh (in tile units) between any two of tile (x, z)'s four
    /// corner heights. 0 = flat, 1 = gentle ramp, ≥2 = cliff or wall.
    /// </summary>
    public int TerrainSlope(int x, int z)
    {
        var h00 = TerrainHeightAt(x, z);
        var h10 = TerrainHeightAt(x + 1, z);
        var h01 = TerrainHeightAt(x, z + 1);
        var h11 = TerrainHeightAt(x + 1, z + 1);
        var lo = System.Math.Min(System.Math.Min(h00, h10), System.Math.Min(h01, h11));
        var hi = System.Math.Max(System.Math.Max(h00, h10), System.Math.Max(h01, h11));
        return hi - lo;
    }

    public void SetTerrainHeight(int x, int z, short h)
    {
        var (cx, cz, lx, lz) = SplitXZ(x, z);
        GetOrCreateTerrainChunk(cx, cz).SetHeight(lx, lz, h);
        MutationTick++;
    }

    public void SetTerrainKind(int x, int z, TileKind kind)
    {
        var (cx, cz, lx, lz) = SplitXZ(x, z);
        GetOrCreateTerrainChunk(cx, cz).SetKind(lx, lz, (byte)kind);
        MutationTick++;
    }

    /// <summary>
    /// Flag the east edge of tile (x, z) as a cliff. Tile (x, z) is the upper
    /// platform; tile (x+1, z) renders its west edge corners at
    /// <paramref name="lowerHeight"/> instead of the shared heightmap value.
    /// </summary>
    public void SetTerrainCliffE(int x, int z, short lowerHeight)
    {
        var (cx, cz, lx, lz) = SplitXZ(x, z);
        GetOrCreateTerrainChunk(cx, cz).SetCliffE(lx, lz, lowerHeight);
        MutationTick++;
    }

    /// <summary>
    /// Flag the south edge of tile (x, z) as a cliff. Upper platform is
    /// (x, z); tile (x, z+1) is the lower floor.
    /// </summary>
    public void SetTerrainCliffS(int x, int z, short lowerHeight)
    {
        var (cx, cz, lx, lz) = SplitXZ(x, z);
        GetOrCreateTerrainChunk(cx, cz).SetCliffS(lx, lz, lowerHeight);
        MutationTick++;
    }

    public TerrainChunk? GetTerrainChunkOrNull(int cx, int cz)
        => _terrainChunks.TryGetValue((cx, cz), out var tc) ? tc : null;

    /// <summary>
    /// Copy of a terrain chunk plus its +X / +Z / +XZ seam corners, for
    /// worker-thread meshing. Returns null if the chunk isn't resident; the
    /// seam falls back to the chunk's own edge corners if a neighbor is
    /// missing (world border) so a solitary chunk meshes cleanly.
    /// </summary>
    public TerrainSnapshot? SnapshotTerrain(int cx, int cz)
    {
        if (!_terrainChunks.TryGetValue((cx, cz), out var tc)) return null;
        const int s = TerrainChunk.Size;
        var snap = new TerrainSnapshot(cx, cz, tc.Revision);
        for (var lx = 0; lx < s; lx++)
        for (var lz = 0; lz < s; lz++)
        {
            snap.Heights[lx, lz] = tc.Heights[lx, lz];
            snap.Kinds[lx, lz]   = tc.Kinds[lx, lz];
            // Copy own E/S cliff bits; W/N bits + lower heights are derived
            // below from -X / -Z neighbors.
            var ownMask = (byte)(tc.CliffMask[lx, lz] & (TerrainChunk.CliffBitE | TerrainChunk.CliffBitS));
            snap.CliffMask[lx, lz] = ownMask;
            snap.CliffLowerE[lx, lz] = tc.CliffLowerE[lx, lz];
            snap.CliffLowerS[lx, lz] = tc.CliffLowerS[lx, lz];
        }
        _terrainChunks.TryGetValue((cx + 1, cz),     out var px);
        _terrainChunks.TryGetValue((cx,     cz + 1), out var pz);
        _terrainChunks.TryGetValue((cx + 1, cz + 1), out var pxz);
        _terrainChunks.TryGetValue((cx - 1, cz),     out var nx);
        _terrainChunks.TryGetValue((cx,     cz - 1), out var nz);
        for (var lz = 0; lz < s; lz++)
            snap.Heights[s, lz] = px?.Heights[0, lz] ?? tc.Heights[s - 1, lz];
        for (var lx = 0; lx < s; lx++)
            snap.Heights[lx, s] = pz?.Heights[lx, 0] ?? tc.Heights[lx, s - 1];
        snap.Heights[s, s] =
            pxz?.Heights[0, 0] ??
            px?.Heights[0, s - 1] ??
            pz?.Heights[s - 1, 0] ??
            tc.Heights[s - 1, s - 1];

        // Derive W / N bits from -X / -Z neighbors. Tile (lx, lz)'s W cliff
        // exists when the owning tile to the west (either in this chunk at
        // lx-1 or in the -X neighbor at s-1) has its E bit set. Mirror its
        // stored E lower-height into our W slot so the mesher reads a
        // self-contained snapshot.
        for (var lz = 0; lz < s; lz++)
        for (var lx = 0; lx < s; lx++)
        {
            byte westMask; short westLower;
            if (lx == 0)
            {
                westMask = nx != null ? (byte)(nx.CliffMask[s - 1, lz] & TerrainChunk.CliffBitE) : (byte)0;
                westLower = nx?.CliffLowerE[s - 1, lz] ?? (short)0;
            }
            else
            {
                westMask = (byte)(tc.CliffMask[lx - 1, lz] & TerrainChunk.CliffBitE);
                westLower = tc.CliffLowerE[lx - 1, lz];
            }
            if (westMask != 0)
            {
                snap.CliffMask[lx, lz] |= TerrainSnapshot.CliffBitW;
                snap.CliffLowerW[lx, lz] = westLower;
            }

            byte northMask; short northLower;
            if (lz == 0)
            {
                northMask = nz != null ? (byte)(nz.CliffMask[lx, s - 1] & TerrainChunk.CliffBitS) : (byte)0;
                northLower = nz?.CliffLowerS[lx, s - 1] ?? (short)0;
            }
            else
            {
                northMask = (byte)(tc.CliffMask[lx, lz - 1] & TerrainChunk.CliffBitS);
                northLower = tc.CliffLowerS[lx, lz - 1];
            }
            if (northMask != 0)
            {
                snap.CliffMask[lx, lz] |= TerrainSnapshot.CliffBitN;
                snap.CliffLowerN[lx, lz] = northLower;
            }
        }
        return snap;
    }

    private TerrainChunk GetOrCreateTerrainChunk(int cx, int cz)
    {
        if (_terrainChunks.TryGetValue((cx, cz), out var tc)) return tc;
        tc = new TerrainChunk();
        _terrainChunks[(cx, cz)] = tc;
        return tc;
    }

    private static (int cx, int cz, int lx, int lz) SplitXZ(int x, int z)
    {
        const int s = Chunk.Size;
        var cx = FloorDiv(x, s);
        var cz = FloorDiv(z, s);
        return (cx, cz, x - cx * s, z - cz * s);
    }

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
            MutationTick++;
        }
        var prevRev = chunk.Revision;
        chunk[lx, ly, lz] = tile;
        if (chunk.Revision != prevRev) MutationTick++;
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
