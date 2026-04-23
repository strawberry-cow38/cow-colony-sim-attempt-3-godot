using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Grid;
using Xunit;

namespace CowColonySim.Tests;

public class ClimateTests
{
    [Fact]
    public void Generate_PopulatesPerTileClimate()
    {
        var tiles = new TileWorld();
        WorldGen.Generate(tiles, seed: 1234, sizeX: 64, sizeZ: 64);

        var tAt00 = tiles.TemperatureAt(0, 0);
        var rAt00 = tiles.RainfallAt(0, 0);

        Assert.InRange(tAt00, -60f, 50f);
        Assert.InRange(rAt00, 0f, 4000f);
    }

    [Fact]
    public void Temperature_DropsWithLatitude()
    {
        // 1024×1024 so the two sample strips land in different cells (cells
        // are 256 tiles across — on a 128-tile world every sample lands in
        // the same cell and temperature is uniform).
        var tiles = new TileWorld();
        WorldGen.Generate(tiles, seed: 7, sizeX: 1024, sizeZ: 1024);

        var equatorSum = 0f;
        var poleSum = 0f;
        for (var x = -32; x < 32; x++)
        {
            equatorSum += tiles.TemperatureAt(x, 0);
            poleSum += tiles.TemperatureAt(x, 480);
        }
        Assert.True(equatorSum / 64f > poleSum / 64f + 10f,
            $"equator avg {equatorSum / 64f:0.0} not clearly warmer than pole avg {poleSum / 64f:0.0}");
    }

    [Fact]
    public void SnowBiome_AppearsOnTallPeaks()
    {
        // Climate is one-biome-per-cell, but tiles rising above
        // SnowPeakHeightTiles re-tag as Snow regardless of their cell's
        // biome — so every tall peak in any biome should come back Snow.
        var tiles = new TileWorld();
        WorldGen.Generate(tiles, seed: 42, sizeX: 256, sizeZ: 256);

        var sawPeak = false;
        for (var x = -128; x < 128; x++)
        for (var z = -128; z < 128; z++)
        {
            var h = tiles.TerrainHeightAt(x, z);
            if (h - WorldGen.WaterLevelY < WorldGen.SnowPeakHeightTiles) continue;
            sawPeak = true;
            Assert.Equal(BiomeBuiltins.SnowId, tiles.BiomeAt(x, z));
        }
        if (!sawPeak) return; // seed produced no tall peak — skip
    }
}
