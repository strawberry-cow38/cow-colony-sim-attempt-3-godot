using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;
using Xunit;

namespace CowColonySim.Tests;

public class AStarPathfinderTests
{
    private static TileWorld BuildFlatFloor(int size)
    {
        var world = new TileWorld();
        for (var x = 0; x < size; x++)
        for (var z = 0; z < size; z++)
        {
            world.Set(new TilePos(x, 0, z), new Tile(TileKind.Solid));
        }
        return world;
    }

    [Fact]
    public void Straight_Line_On_Flat_Floor()
    {
        var world = BuildFlatFloor(5);
        var start = new TilePos(0, 1, 0);
        var goal = new TilePos(4, 1, 0);

        var path = AStarPathfinder.FindPath(world, start, goal);

        Assert.NotNull(path);
        Assert.Equal(start, path![0]);
        Assert.Equal(goal, path[^1]);
        Assert.Equal(5, path.Length);
    }

    [Fact]
    public void Returns_Null_When_Start_Unstandable()
    {
        var world = BuildFlatFloor(5);
        var path = AStarPathfinder.FindPath(world, new TilePos(0, 5, 0), new TilePos(4, 1, 0));
        Assert.Null(path);
    }

    [Fact]
    public void Returns_Null_When_Goal_Unstandable()
    {
        var world = BuildFlatFloor(5);
        var path = AStarPathfinder.FindPath(world, new TilePos(0, 1, 0), new TilePos(4, 5, 0));
        Assert.Null(path);
    }

    [Fact]
    public void Step_Up_One_Tile_Supported()
    {
        var world = new TileWorld();
        for (var x = 0; x < 4; x++) world.Set(new TilePos(x, 0, 0), new Tile(TileKind.Solid));
        world.Set(new TilePos(3, 1, 0), new Tile(TileKind.Solid));

        var path = AStarPathfinder.FindPath(world, new TilePos(0, 1, 0), new TilePos(3, 2, 0));

        Assert.NotNull(path);
        Assert.Equal(new TilePos(3, 2, 0), path![^1]);
        Assert.Contains(new TilePos(2, 1, 0), path);
    }

    [Fact]
    public void Step_Up_Blocked_By_Ceiling()
    {
        var world = new TileWorld();
        for (var x = 0; x < 4; x++) world.Set(new TilePos(x, 0, 0), new Tile(TileKind.Solid));
        world.Set(new TilePos(3, 1, 0), new Tile(TileKind.Solid));
        world.Set(new TilePos(2, 2, 0), new Tile(TileKind.Solid));

        var path = AStarPathfinder.FindPath(world, new TilePos(0, 1, 0), new TilePos(3, 2, 0));

        Assert.Null(path);
    }

    [Fact]
    public void Step_Down_Onto_Lower_Floor()
    {
        var world = new TileWorld();
        for (var x = 0; x < 4; x++) world.Set(new TilePos(x, 0, 0), new Tile(TileKind.Solid));
        world.Set(new TilePos(0, 1, 0), new Tile(TileKind.Solid));
        world.Set(new TilePos(1, 1, 0), new Tile(TileKind.Solid));

        var path = AStarPathfinder.FindPath(world, new TilePos(1, 2, 0), new TilePos(3, 1, 0));

        Assert.NotNull(path);
        Assert.Equal(new TilePos(3, 1, 0), path![^1]);
    }

    [Fact]
    public void Start_Equals_Goal_Returns_Single_Tile()
    {
        var world = BuildFlatFloor(3);
        var pos = new TilePos(1, 1, 1);
        var path = AStarPathfinder.FindPath(world, pos, pos);
        Assert.NotNull(path);
        Assert.Single(path!);
        Assert.Equal(pos, path![0]);
    }

    [Fact]
    public void Unreachable_Island_Returns_Null()
    {
        var world = new TileWorld();
        world.Set(new TilePos(0, 0, 0), new Tile(TileKind.Solid));
        world.Set(new TilePos(10, 0, 10), new Tile(TileKind.Solid));

        var path = AStarPathfinder.FindPath(world, new TilePos(0, 1, 0), new TilePos(10, 1, 10));
        Assert.Null(path);
    }
}
