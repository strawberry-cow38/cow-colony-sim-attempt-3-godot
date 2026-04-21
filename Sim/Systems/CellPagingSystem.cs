using System.Collections.Concurrent;
using System.Threading.Tasks;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Systems;

/// <summary>
/// Streams cells between RAM and disk based on their computed tier.
///
/// - A cell that has been Dormant for <see cref="ColdEvictTicks"/> continuously
///   gets its chunks evicted from memory and written to the <see cref="CellStore"/>.
/// - A cell that is newly Ambient/Live but not in memory gets loaded
///   asynchronously from the store. While the load is in-flight tiles read
///   as <see cref="Tile.Empty"/>.
///
/// Eviction and load both happen on background <see cref="Task.Run"/>s.
/// Completion is drained on the tick thread and applied to <see cref="TileWorld"/>.
/// The scan itself runs at 1Hz to keep the main-thread overhead tiny.
/// </summary>
public sealed class CellPagingSystem
{
    public static readonly int ColdEvictTicks = SimConstants.SimHz * 30;   // 30s
    public static readonly int ScanEveryTicks = SimConstants.SimHz;        // 1Hz scan

    private readonly CellStore _store;
    private readonly Dictionary<CellKey, long> _coldSince = new();
    private readonly HashSet<CellKey> _saveInFlight = new();
    private readonly HashSet<CellKey> _loadInFlight = new();
    private readonly ConcurrentQueue<CellKey> _saveComplete = new();
    private readonly ConcurrentQueue<(CellKey Key, List<(TilePos, Chunk)>? Chunks)> _loadComplete = new();

    public CellPagingSystem(CellStore store) { _store = store; }

    public int SaveInFlightCount => _saveInFlight.Count;
    public int LoadInFlightCount => _loadInFlight.Count;

    public void Step(TileWorld tiles, long tick)
    {
        DrainCompletions(tiles);

        if (tick % ScanEveryTicks != 0) return;

        ScanForEviction(tiles, tick);
        ScanForLoad(tiles);
    }

    private void ScanForEviction(TileWorld tiles, long tick)
    {
        var inMem = new List<CellKey>(tiles.InMemoryCells);
        foreach (var key in inMem)
        {
            if (_saveInFlight.Contains(key)) continue;
            var state = tiles.GetCellState(key);
            if (state != ChunkState.Dormant)
            {
                _coldSince.Remove(key);
                continue;
            }
            if (!_coldSince.TryGetValue(key, out var since))
            {
                _coldSince[key] = tick;
                continue;
            }
            if (tick - since < ColdEvictTicks) continue;

            var chunks = tiles.TryEvictCell(key);
            _coldSince.Remove(key);
            if (chunks == null) continue;

            _saveInFlight.Add(key);
            var k = key;
            var cs = chunks;
            Task.Run(() =>
            {
                try { _store.Save(k, cs); }
                catch { /* swallow: cell is regen-able via worldgen seed */ }
                _saveComplete.Enqueue(k);
            });
        }
    }

    private void ScanForLoad(TileWorld tiles)
    {
        foreach (var kv in tiles.CellStates)
        {
            if (kv.Value == ChunkState.Dormant) continue;
            var key = kv.Key;
            if (tiles.CellHasChunks(key)) continue;
            if (_saveInFlight.Contains(key)) continue;
            if (_loadInFlight.Contains(key)) continue;
            if (!_store.Exists(key)) continue;
            _loadInFlight.Add(key);
            var k = key;
            Task.Run(() =>
            {
                try
                {
                    var chunks = _store.Load(k);
                    _loadComplete.Enqueue((k, chunks));
                }
                catch
                {
                    _loadComplete.Enqueue((k, null));
                }
            });
        }
    }

    private void DrainCompletions(TileWorld tiles)
    {
        while (_saveComplete.TryDequeue(out var key)) _saveInFlight.Remove(key);
        while (_loadComplete.TryDequeue(out var pack))
        {
            _loadInFlight.Remove(pack.Key);
            if (pack.Chunks == null) continue;
            if (tiles.CellHasChunks(pack.Key)) continue;
            tiles.InstallCell(pack.Key, pack.Chunks);
        }
    }

    /// <summary>Synchronous variant for tests: skip background tasks.</summary>
    public void StepSync(TileWorld tiles, long tick)
    {
        DrainCompletions(tiles);
        if (tick % ScanEveryTicks != 0) return;

        var inMem = new List<CellKey>(tiles.InMemoryCells);
        foreach (var key in inMem)
        {
            var state = tiles.GetCellState(key);
            if (state != ChunkState.Dormant) { _coldSince.Remove(key); continue; }
            if (!_coldSince.TryGetValue(key, out var since)) { _coldSince[key] = tick; continue; }
            if (tick - since < ColdEvictTicks) continue;
            var chunks = tiles.TryEvictCell(key);
            _coldSince.Remove(key);
            if (chunks == null) continue;
            _store.Save(key, chunks);
        }

        foreach (var kv in tiles.CellStates)
        {
            if (kv.Value == ChunkState.Dormant) continue;
            if (tiles.CellHasChunks(kv.Key)) continue;
            if (!_store.Exists(kv.Key)) continue;
            var loaded = _store.Load(kv.Key);
            tiles.InstallCell(kv.Key, loaded);
        }
    }
}
