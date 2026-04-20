using CowColonySim.Sim.Grid;
using Xunit;

namespace CowColonySim.Tests;

public class TileWorldTests
{
    [Fact]
    public void Empty_World_Returns_Empty_Default()
    {
        var world = new TileWorld();
        Assert.Equal(Tile.Empty, world.Get(new TilePos(0, 0, 0)));
        Assert.Equal(Tile.Empty, world.Get(new TilePos(999, -5000, 42)));
        Assert.Equal(0, world.ChunkCount);
    }

    [Fact]
    public void Setting_Empty_Never_Allocates_Chunk()
    {
        var world = new TileWorld();
        world.Set(new TilePos(10, 10, 10), Tile.Empty);
        Assert.Equal(0, world.ChunkCount);
    }

    [Fact]
    public void Roundtrip_Across_Chunk_Boundaries()
    {
        var world = new TileWorld();
        var positions = new[]
        {
            new TilePos(0, 0, 0),
            new TilePos(15, 15, 15),
            new TilePos(16, 0, 0),
            new TilePos(-1, 0, 0),
            new TilePos(-17, -33, 48),
            new TilePos(1000, -1000, 1000),
        };

        foreach (var pos in positions)
        {
            world.Set(pos, new Tile(TileKind.Solid));
        }

        foreach (var pos in positions)
        {
            Assert.Equal(new Tile(TileKind.Solid), world.Get(pos));
        }
    }

    [Fact]
    public void Chunk_Reused_For_Adjacent_Tiles()
    {
        var world = new TileWorld();
        for (var i = 0; i < Chunk.Size; i++)
        {
            world.Set(new TilePos(i, 0, 0), new Tile(TileKind.Floor));
        }
        Assert.Equal(1, world.ChunkCount);
    }

    [Fact]
    public void Neighbors_Yields_Six_Axis_Aligned()
    {
        var world = new TileWorld();
        var origin = new TilePos(5, 5, 5);
        world.Set(origin.Offset(1, 0, 0), new Tile(TileKind.Solid));
        world.Set(origin.Offset(0, -1, 0), new Tile(TileKind.Floor));

        var n = world.Neighbors(origin).ToList();
        Assert.Equal(6, n.Count);
        Assert.Contains(n, x => x.Pos == origin.Offset(1, 0, 0) && x.Tile.Kind == TileKind.Solid);
        Assert.Contains(n, x => x.Pos == origin.Offset(0, -1, 0) && x.Tile.Kind == TileKind.Floor);
        var emptyCount = n.Count(x => x.Tile.IsEmpty);
        Assert.Equal(4, emptyCount);
    }

    [Fact]
    public void Bulk_Fill_Stress()
    {
        var world = new TileWorld();
        const int side = 40;
        for (var x = 0; x < side; x++)
        for (var y = 0; y < side; y++)
        for (var z = 0; z < side; z++)
        {
            world.Set(new TilePos(x, y, z), new Tile(TileKind.Solid));
        }

        var count = 0;
        for (var x = 0; x < side; x++)
        for (var y = 0; y < side; y++)
        for (var z = 0; z < side; z++)
        {
            if (world.Get(new TilePos(x, y, z)).Kind == TileKind.Solid) count++;
        }

        Assert.Equal(side * side * side, count);
        Assert.True(world.ChunkCount <= 27, $"expected <=27 chunks for 40³ fill, got {world.ChunkCount}");
    }
}
