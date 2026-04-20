using CowColonySim.Sim;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;
using Xunit;

namespace CowColonySim.Tests;

public class TileMathTests
{
    [Fact]
    public void FeetOfTile_Uses_Anisotropic_Dimensions()
    {
        var feet = TileMath.FeetOfTile(new TilePos(2, 3, 4));
        Assert.Equal(2 * SimConstants.TileWidthMeters + SimConstants.TileWidthMeters * 0.5f, feet.X);
        Assert.Equal(3 * SimConstants.TileHeightMeters, feet.Y);
        Assert.Equal(4 * SimConstants.TileWidthMeters + SimConstants.TileWidthMeters * 0.5f, feet.Z);
    }

    [Fact]
    public void TileAt_Round_Trips_Through_FeetOfTile()
    {
        var tile = new TilePos(7, 11, -3);
        var feet = TileMath.FeetOfTile(tile);
        var bumped = new Position(feet.X, feet.Y + SimConstants.TileHeightMeters * 0.1f, feet.Z);
        Assert.Equal(tile, TileMath.TileAt(bumped));
    }

    [Fact]
    public void Headroom_Requires_Two_Empty_Tiles_Above()
    {
        var world = new TileWorld();
        world.Set(new TilePos(0, 0, 0), new Tile(TileKind.Solid));
        world.Set(new TilePos(0, 2, 0), new Tile(TileKind.Solid));

        Assert.False(Walkability.IsStandable(world, new TilePos(0, 1, 0)));

        var clear = new TileWorld();
        clear.Set(new TilePos(0, 0, 0), new Tile(TileKind.Solid));
        Assert.True(Walkability.IsStandable(clear, new TilePos(0, 1, 0)));
    }
}
