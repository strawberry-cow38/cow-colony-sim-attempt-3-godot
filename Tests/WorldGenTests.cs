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
            // Lake columns surface at WaterLevelY (water-top); land columns
            // sit higher. Anything below WaterLevelY would mean an empty gap.
            Assert.True(WorldGen.SurfaceY(world, x, z) >= WorldGen.WaterLevelY);
        }
    }

    [Fact]
    public void Generate_Is_Seamless_Across_Cell_Borders()
    {
        // Big enough to span several cells; cell borders are at
        // x = cellHalf + k*cellSize = 128 + 256k relative to world center.
        var world = new TileWorld();
        WorldGen.Generate(world, seed: 1111, sizeX: 768, sizeZ: 64);

        var maxStep = 0;
        for (var z = -16; z < 16; z++)
        for (var x = -383; x < 383; x++)
        {
            var a = WorldGen.SurfaceY(world, x, z);
            var b = WorldGen.SurfaceY(world, x + 1, z);
            var step = Math.Abs(a - b);
            if (step > maxStep) maxStep = step;
        }
        // Bilerp of 4 continuous features — step between adjacent tiles must
        // stay small. A hard cell-border seam would produce >> PlateauStep.
        // Mountain plateau terrace allows a single 10-tile band jump at ramp
        // edges; cubby gradients may add a few tiles on top. 20 covers the
        // combined case but still catches any actual cell-border seam.
        Assert.True(maxStep <= 20, $"max per-tile step {maxStep} > 20");
    }

    [Fact]
    public void FeatureResolver_Is_Deterministic()
    {
        var a = FeatureResolver.Pick(42, new CellKey(3, -2));
        var b = FeatureResolver.Pick(42, new CellKey(3, -2));
        Assert.Equal(a, b);
    }
}
