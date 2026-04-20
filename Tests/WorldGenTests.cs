using CowColonySim.Sim;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;
using Xunit;

namespace CowColonySim.Tests;

public class WorldGenTests
{
    [Fact]
    public void Generate_Produces_Tiles_And_Standable_Surface()
    {
        var world = new TileWorld();
        WorldGen.Generate(world, seed: 1234, sizeX: 32, sizeZ: 32);

        Assert.True(world.ChunkCount > 0);

        var standable = 0;
        for (var x = -16; x < 16; x++)
        for (var z = -16; z < 16; z++)
        {
            var y = WorldGen.SurfaceY(world, x, z);
            if (Walkability.IsStandable(world, new TilePos(x, y, z))) standable++;
        }
        Assert.True(standable > 0);
    }

    [Fact]
    public void Generate_Is_Deterministic_For_Same_Seed()
    {
        var a = new TileWorld();
        var b = new TileWorld();
        WorldGen.Generate(a, seed: 42, sizeX: 16, sizeZ: 16);
        WorldGen.Generate(b, seed: 42, sizeX: 16, sizeZ: 16);

        for (var x = -8; x < 8; x++)
        for (var z = -8; z < 8; z++)
        {
            Assert.Equal(WorldGen.SurfaceY(a, x, z), WorldGen.SurfaceY(b, x, z));
        }
    }

    [Fact]
    public void Generate_Covers_Full_Bounds()
    {
        var world = new TileWorld();
        WorldGen.Generate(world, seed: 7, sizeX: 8, sizeZ: 8);

        for (var x = -4; x < 4; x++)
        for (var z = -4; z < 4; z++)
        {
            Assert.True(WorldGen.SurfaceY(world, x, z) >= 1);
        }
    }
}
