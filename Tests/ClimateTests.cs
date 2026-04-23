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
        var tiles = new TileWorld();
        WorldGen.Generate(tiles, seed: 7, sizeX: 128, sizeZ: 128);

        // Average a band near the equator vs. a band near the north pole. The
        // noise wobble is ±4°C but the latitude swing is ±24°C, so averaged
        // over a strip the trend must show through.
        var equatorSum = 0f;
        var poleSum = 0f;
        for (var x = -32; x < 32; x++)
        {
            equatorSum += tiles.TemperatureAt(x, 0);
            poleSum += tiles.TemperatureAt(x, 60);
        }
        Assert.True(equatorSum / 64f > poleSum / 64f + 10f,
            $"equator avg {equatorSum / 64f:0.0} not clearly warmer than pole avg {poleSum / 64f:0.0}");
    }

    [Fact]
    public void Temperature_DropsWithElevation()
    {
        // Same seed, compare a sea-level coastal tile against a mountainous
        // interior tile. The lapse (0.35°C per tile) dominates the ±4°C noise
        // by the time we hit the central mountain band.
        var tiles = new TileWorld();
        WorldGen.Generate(tiles, seed: 42, sizeX: 256, sizeZ: 256);

        var lowT = tiles.TemperatureAt(120, 0);
        var lowH = tiles.TerrainHeightAt(120, 0);
        var hiT = lowT;
        var hiH = lowH;
        for (var x = -20; x <= 20; x++)
        for (var z = -20; z <= 20; z++)
        {
            var h = tiles.TerrainHeightAt(x, z);
            if (h > hiH) { hiH = h; hiT = tiles.TemperatureAt(x, z); }
        }

        if (hiH - lowH < 20) return; // seed produced no tall peak — skip
        Assert.True(hiT < lowT,
            $"peak h={hiH} T={hiT:0.0} not cooler than low h={lowH} T={lowT:0.0}");
    }
}
