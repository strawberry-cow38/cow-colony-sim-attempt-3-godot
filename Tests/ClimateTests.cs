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
    public void Climate_FromMapCell_StampsCellClimateAcrossPocket()
    {
        // WorldGen now pulls biome + base climate from the supplied
        // WorldMapCell. Every tile in the pocket carries that temp; rain
        // = cell rain + river boost, so it stays ≥ cell rain everywhere.
        var tiles = new TileWorld();
        var mapCell = new WorldMapCell(
            BiomeBuiltins.GrasslandId, TemperatureC: 12f, RainfallMm: 500f,
            Elevation: 0.5f, IsOcean: false);
        WorldGen.Generate(tiles, seed: 7, sizeX: 64, sizeZ: 64, mapCell: mapCell);

        for (var x = -32; x < 32; x += 8)
        for (var z = -32; z < 32; z += 8)
        {
            Assert.Equal(12f, tiles.TemperatureAt(x, z));
            Assert.True(tiles.RainfallAt(x, z) >= 500f);
        }
    }

    [Fact]
    public void Climate_From33Neighborhood_StampsPerSubregion()
    {
        // WorldGen in 3×3 mode paints each 256-wide subregion with the
        // climate of the overworld cell it represents. Center subregion
        // (world coord 0) carries center-cell temp; the neighbor to the
        // east (world coord > +128) carries that neighbor cell's temp.
        var overworld = WorldMapGenerator.Generate(seed: 2026);
        var center = WorldMap.Center;
        var size = Cell.SizeTiles * 3;
        var tiles = new TileWorld();
        WorldGen.Generate(tiles, seed: 2026, sizeX: size, sizeZ: size,
            overworld: overworld, center: center);

        var centerCell = overworld.Get(center);
        var eastCell   = overworld.Get(center.X + 1, center.Z);

        Assert.Equal(centerCell.TemperatureC, tiles.TemperatureAt(0, 0));
        Assert.Equal(eastCell.TemperatureC,   tiles.TemperatureAt(200, 0));
    }

    [Fact]
    public void SnowBiome_AppearsOnTallPeaks()
    {
        // Tiles rising SnowPeakHeightTiles above sea level re-tag as Snow,
        // except inside Savanna/Desert cells where the hot biome wins. So
        // every tall peak should come back either Snow or the host cell's
        // hot-band biome — never anything else.
        var tiles = new TileWorld();
        WorldGen.Generate(tiles, seed: 42, sizeX: 256, sizeZ: 256);

        var sawPeak = false;
        for (var x = -128; x < 128; x++)
        for (var z = -128; z < 128; z++)
        {
            var h = tiles.TerrainHeightAt(x, z);
            if (h - WorldGen.WaterLevelY < WorldGen.SnowPeakHeightTiles) continue;
            sawPeak = true;
            var b = tiles.BiomeAt(x, z);
            Assert.True(
                b == BiomeBuiltins.SnowId
                || b == BiomeBuiltins.SavannaId
                || b == BiomeBuiltins.DesertId,
                $"peak at ({x},{z}) h={h} came back biome {b}");
        }
        if (!sawPeak) return; // seed produced no tall peak — skip
    }
}
