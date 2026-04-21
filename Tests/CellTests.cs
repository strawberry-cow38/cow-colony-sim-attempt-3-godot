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
}
