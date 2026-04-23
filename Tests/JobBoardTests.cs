using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Jobs;
using Xunit;

namespace CowColonySim.Tests;

public class JobBoardTests
{
    private static JobBoard MakeBoard(int side = 8)
    {
        return new JobBoard(new TilePos(0, 0, 0), new TilePos(79, 0, 79), side);
    }

    [Fact]
    public void Add_Stamps_Dirty_Tick_On_Target_Cell()
    {
        var board = MakeBoard();
        var id = board.Add(JobTier.Auto, new TilePos(5, 0, 5), tick: 42);

        Assert.Equal(42, board.DirtySinceNear(new TilePos(5, 0, 5)));
        Assert.True(board.Tasks.ContainsKey(id));
    }

    [Fact]
    public void Remove_Bumps_Dirty_Tick_And_Drops_Task()
    {
        var board = MakeBoard();
        var id = board.Add(JobTier.Auto, new TilePos(5, 0, 5), tick: 10);
        var removed = board.Remove(id, tick: 25);

        Assert.True(removed);
        Assert.False(board.Tasks.ContainsKey(id));
        Assert.Equal(25, board.DirtySinceNear(new TilePos(5, 0, 5)));
    }

    [Fact]
    public void TasksNear_Returns_Own_Cell_Plus_Eight_Neighbours()
    {
        var board = MakeBoard();
        // Cell size = 80/8 = 10 tiles per side. Place tasks in centre cell (4,4)
        // and the 8 neighbours; one task in a far cell should not be returned.
        var centre = new TilePos(45, 0, 45);
        board.Add(JobTier.Auto, centre, 1);
        board.Add(JobTier.Auto, new TilePos(35, 0, 35), 1); // neighbour (3,3)
        board.Add(JobTier.Auto, new TilePos(55, 0, 55), 1); // neighbour (5,5)
        board.Add(JobTier.Auto, new TilePos(5, 0, 5), 1);   // far cell (0,0)

        var near = board.TasksNear(centre).ToList();
        Assert.Equal(3, near.Count);
    }

    [Fact]
    public void DirtySinceNear_Reflects_Most_Recent_Change_In_Window()
    {
        var board = MakeBoard();
        board.Add(JobTier.Auto, new TilePos(45, 0, 45), tick: 100);
        board.Add(JobTier.Auto, new TilePos(35, 0, 35), tick: 200);

        Assert.Equal(200, board.DirtySinceNear(new TilePos(45, 0, 45)));
    }

    [Fact]
    public void DirtySinceNear_Ignores_Changes_Outside_Window()
    {
        var board = MakeBoard();
        board.Add(JobTier.Auto, new TilePos(5, 0, 5), tick: 500);

        // Far corner of the map — (5,5) in cell space from (0,0); outside 3×3
        // window around the (7,7) corner.
        Assert.Equal(0, board.DirtySinceNear(new TilePos(75, 0, 75)));
    }

    [Fact]
    public void SetWallsDirty_Records_Tick()
    {
        var board = MakeBoard();
        board.SetWallsDirty(tick: 999);
        Assert.Equal(999, board.WallsDirtyAtTick);
    }

    [Fact]
    public void Out_Of_Bounds_Positions_Clamp_To_Edge()
    {
        var board = MakeBoard();
        // Inserts into the (0,0) cell.
        var near = board.Add(JobTier.Auto, new TilePos(-1000, 0, -1000), tick: 7);
        Assert.True(board.Tasks.ContainsKey(near));
        Assert.Equal(7, board.DirtySinceNear(new TilePos(0, 0, 0)));
    }
}
