using System.IO;
using fennecs;
using CowColonySim.Sim;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Systems;
using Xunit;

namespace CowColonySim.Tests;

public class CellTests
{
    [Fact]
    public void Cell_Size_Is_Consistent()
    {
        Assert.Equal(16, Cell.SizeChunks);
        Assert.Equal(16 * Chunk.Size, Cell.SizeTiles);
        Assert.Equal(SimConstants.CellSizeTiles, Cell.SizeTiles);
    }

    [Fact]
    public void Chunk_To_Cell_Rounds_Correctly()
    {
        Assert.Equal(new CellKey(0, 0), Cell.FromChunk(new TilePos(0, 0, 0)));
        Assert.Equal(new CellKey(0, 0), Cell.FromChunk(new TilePos(Cell.SizeChunks - 1, 0, Cell.SizeChunks - 1)));
        Assert.Equal(new CellKey(1, 1), Cell.FromChunk(new TilePos(Cell.SizeChunks, 0, Cell.SizeChunks)));
        Assert.Equal(new CellKey(-1, -1), Cell.FromChunk(new TilePos(-1, 0, -1)));
    }

    [Fact]
    public void Tile_To_Cell_Rounds_Correctly()
    {
        Assert.Equal(new CellKey(0, 0), Cell.FromTile(new TilePos(0, 0, 0)));
        Assert.Equal(new CellKey(0, 0), Cell.FromTile(new TilePos(Cell.SizeTiles - 1, 0, Cell.SizeTiles - 1)));
        Assert.Equal(new CellKey(1, 0), Cell.FromTile(new TilePos(Cell.SizeTiles, 0, 0)));
        Assert.Equal(new CellKey(-1, -1), Cell.FromTile(new TilePos(-1, 0, -1)));
    }

    [Fact]
    public void Cells_Touched_By_Bounds_Covers_Boundary_Crossings()
    {
        var keys = Cell.CellsTouchedByBounds(
            new TilePos(-1, 0, -1),
            new TilePos(Cell.SizeTiles, 0, Cell.SizeTiles)).ToHashSet();

        Assert.Contains(new CellKey(-1, -1), keys);
        Assert.Contains(new CellKey(0, 0), keys);
        Assert.Contains(new CellKey(1, 1), keys);
        Assert.Contains(new CellKey(-1, 0), keys);
        Assert.Contains(new CellKey(0, -1), keys);
        Assert.Contains(new CellKey(1, 0), keys);
        Assert.Contains(new CellKey(0, 1), keys);
        Assert.Equal(9, keys.Count);
    }

    [Fact]
    public void Cell_State_Derived_From_Max_Chunk_State()
    {
        var world = new World();
        var tiles = new TileWorld();
        world.Spawn().Add(new LiveAnchor(new TilePos(Chunk.Size / 2, Chunk.Size / 2, Chunk.Size / 2), 1));

        ChunkTierSystem.Step(world, tiles);

        Assert.Equal(ChunkState.Live, tiles.GetCellState(new CellKey(0, 0)));
        Assert.Equal(ChunkState.Dormant, tiles.GetCellState(new CellKey(5, 5)));
    }

    [Fact]
    public void Cell_State_Cleared_When_Anchors_Removed()
    {
        var world = new World();
        var tiles = new TileWorld();
        world.Spawn().Add(new LiveAnchor(new TilePos(0, 0, 0), 1));

        ChunkTierSystem.Step(world, tiles);
        Assert.Equal(ChunkState.Live, tiles.GetCellState(new CellKey(0, 0)));

        ChunkTierSystem.Step(new World(), tiles);
        Assert.Equal(ChunkState.Dormant, tiles.GetCellState(new CellKey(0, 0)));
        Assert.Empty(tiles.CellStates);
    }

    [Fact]
    public void Cell_Gating_Live_Every_Tick_Dormant_Never()
    {
        for (long t = 0; t < 120; t++)
        {
            Assert.True(CellGating.ShouldStep(ChunkState.Live, t));
            Assert.False(CellGating.ShouldStep(ChunkState.Dormant, t));
        }
    }

    [Fact]
    public void Cell_Gating_Ambient_Runs_At_1Hz()
    {
        var hits = 0;
        for (long t = 0; t < CellGating.AmbientEveryN * 3; t++)
            if (CellGating.ShouldStep(ChunkState.Ambient, t)) hits++;
        Assert.Equal(3, hits);
    }

    [Fact]
    public void ChunksByCell_Indexes_Chunks_On_Create()
    {
        var tiles = new TileWorld();
        tiles.Set(new TilePos(0, 0, 0), new Tile(TileKind.Solid));
        tiles.Set(new TilePos(Cell.SizeTiles, 0, 0), new Tile(TileKind.Solid));

        var cell00 = tiles.GetChunksInCell(new CellKey(0, 0));
        var cell10 = tiles.GetChunksInCell(new CellKey(1, 0));
        Assert.NotNull(cell00);
        Assert.NotNull(cell10);
        Assert.Single(cell00);
        Assert.Single(cell10);
        Assert.Null(tiles.GetChunksInCell(new CellKey(9, 9)));
    }

    [Fact]
    public void ChunksByCell_Groups_Multiple_Chunks_In_Same_Cell()
    {
        var tiles = new TileWorld();
        for (var cx = 0; cx < Cell.SizeChunks; cx++)
            tiles.Set(new TilePos(cx * Chunk.Size, 0, 0), new Tile(TileKind.Solid));

        var cell = tiles.GetChunksInCell(new CellKey(0, 0));
        Assert.NotNull(cell);
        Assert.Equal(Cell.SizeChunks, cell.Count);
    }

    [Fact]
    public void Evict_Removes_Chunks_From_Memory()
    {
        var tiles = new TileWorld();
        tiles.Set(new TilePos(0, 0, 0), new Tile(TileKind.Solid));
        tiles.Set(new TilePos(1, 0, 0), new Tile(TileKind.Floor));
        Assert.Equal(1, tiles.ChunkCount);

        var chunks = tiles.TryEvictCell(new CellKey(0, 0));
        Assert.NotNull(chunks);
        Assert.Single(chunks);
        Assert.Equal(0, tiles.ChunkCount);
        Assert.Equal(Tile.Empty, tiles.Get(new TilePos(0, 0, 0)));
        Assert.False(tiles.CellHasChunks(new CellKey(0, 0)));
    }

    [Fact]
    public void Install_Restores_Chunks_After_Evict()
    {
        var tiles = new TileWorld();
        tiles.Set(new TilePos(5, 5, 5), new Tile(TileKind.Solid));
        var chunks = tiles.TryEvictCell(new CellKey(0, 0));
        Assert.NotNull(chunks);

        tiles.InstallCell(new CellKey(0, 0), chunks);
        Assert.Equal(TileKind.Solid, tiles.Get(new TilePos(5, 5, 5)).Kind);
    }

    [Fact]
    public void Install_Fails_When_Cell_Already_Populated()
    {
        var tiles = new TileWorld();
        tiles.Set(new TilePos(0, 0, 0), new Tile(TileKind.Solid));
        Assert.Throws<InvalidOperationException>(() =>
            tiles.InstallCell(new CellKey(0, 0), new[] { (new TilePos(0, 0, 0), new Chunk()) }));
    }

    [Fact]
    public void CellStore_Roundtrip_Preserves_Tiles_And_Revision()
    {
        using var tmp = new TempDir();
        var store = new CellStore(tmp.Path);

        var tiles = new TileWorld();
        for (var i = 0; i < 5; i++) tiles.Set(new TilePos(i, 0, 0), new Tile(TileKind.Solid));
        tiles.Set(new TilePos(0, 0, 1), new Tile(TileKind.Floor));
        var revBefore = tiles.GetChunkOrNull(new TilePos(0, 0, 0))!.Revision;

        var chunks = tiles.TryEvictCell(new CellKey(0, 0))!;
        store.Save(new CellKey(0, 0), chunks);
        Assert.True(store.Exists(new CellKey(0, 0)));

        var loaded = store.Load(new CellKey(0, 0));
        Assert.Equal(chunks.Count, loaded.Count);

        tiles.InstallCell(new CellKey(0, 0), loaded);
        Assert.Equal(TileKind.Solid, tiles.Get(new TilePos(4, 0, 0)).Kind);
        Assert.Equal(TileKind.Floor, tiles.Get(new TilePos(0, 0, 1)).Kind);
        Assert.Equal(revBefore, tiles.GetChunkOrNull(new TilePos(0, 0, 0))!.Revision);
    }

    [Fact]
    public void CellStore_Delete_Removes_File()
    {
        using var tmp = new TempDir();
        var store = new CellStore(tmp.Path);
        store.Save(new CellKey(1, 1), new[] { (new TilePos(0, 0, 0), new Chunk()) });
        Assert.True(store.Exists(new CellKey(1, 1)));
        store.Delete(new CellKey(1, 1));
        Assert.False(store.Exists(new CellKey(1, 1)));
    }

    [Fact]
    public void Paging_Evicts_After_Cold_Threshold()
    {
        using var tmp = new TempDir();
        var store = new CellStore(tmp.Path);
        var paging = new CellPagingSystem(store);

        var tiles = new TileWorld();
        tiles.Set(new TilePos(0, 0, 0), new Tile(TileKind.Solid));
        // Cell starts Dormant (no tier entry). Paging should arm on first scan
        // then evict on scan past threshold.
        paging.StepSync(tiles, 0);
        Assert.True(tiles.CellHasChunks(new CellKey(0, 0)));

        paging.StepSync(tiles, CellPagingSystem.ColdEvictTicks);
        Assert.False(tiles.CellHasChunks(new CellKey(0, 0)));
        Assert.True(store.Exists(new CellKey(0, 0)));
    }

    [Fact]
    public void Paging_Loads_Cell_On_Promote()
    {
        using var tmp = new TempDir();
        var store = new CellStore(tmp.Path);
        var paging = new CellPagingSystem(store);

        var tiles = new TileWorld();
        tiles.Set(new TilePos(7, 0, 7), new Tile(TileKind.Solid));

        // Evict first.
        paging.StepSync(tiles, 0);
        paging.StepSync(tiles, CellPagingSystem.ColdEvictTicks);
        Assert.False(tiles.CellHasChunks(new CellKey(0, 0)));

        // Promote via LiveAnchor → cell becomes Live → load.
        var world = new World();
        world.Spawn().Add(new LiveAnchor(new TilePos(7, 0, 7), 1));
        ChunkTierSystem.Step(world, tiles);
        Assert.Equal(ChunkState.Live, tiles.GetCellState(new CellKey(0, 0)));

        paging.StepSync(tiles, CellPagingSystem.ColdEvictTicks * 2);
        Assert.True(tiles.CellHasChunks(new CellKey(0, 0)));
        Assert.Equal(TileKind.Solid, tiles.Get(new TilePos(7, 0, 7)).Kind);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; }
        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cow-cell-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }
        public void Dispose()
        {
            try { Directory.Delete(Path, recursive: true); } catch { /* ignore */ }
        }
    }
}
