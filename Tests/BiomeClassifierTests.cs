using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Grid;
using Xunit;

namespace CowColonySim.Tests;

public class BiomeClassifierTests
{
    [Theory]
    [InlineData(-30f, 500f,  BiomeBuiltins.SnowId)]
    [InlineData(-5f,  100f,  BiomeBuiltins.TundraId)]
    [InlineData(-5f,  900f,  BiomeBuiltins.TaigaId)]
    [InlineData(10f,  200f,  BiomeBuiltins.GrasslandId)]
    [InlineData(10f,  1200f, BiomeBuiltins.TemperateForestId)]
    [InlineData(28f,  100f,  BiomeBuiltins.DesertId)]
    [InlineData(28f,  900f,  BiomeBuiltins.SavannaId)]
    [InlineData(28f,  2000f, BiomeBuiltins.JungleId)]
    public void Classifies_ExpectedBand(float tempC, float rainMm, byte expected)
    {
        Assert.Equal(expected, BiomeClassifier.Pick(tempC, rainMm));
    }
}

public class BiomeRegistryTests
{
    [Fact]
    public void UnknownIdReturnsUnknown_WhenUnregistered()
    {
        BiomeRegistry.Clear();
        var b = BiomeRegistry.Get(200);
        Assert.Equal("Unknown", b.Name);
    }

    [Fact]
    public void RegisterAll_PopulatesByName()
    {
        BiomeRegistry.Clear();
        BuiltinBiomes.RegisterAll();
        Assert.True(BiomeRegistry.TryGetByName("Desert", out var id));
        Assert.Equal(BiomeBuiltins.DesertId, id);
    }
}

public class BiomeWorldGenTests
{
    [Fact]
    public void Generate_StampsNonZeroBiomeIds()
    {
        var tiles = new TileWorld();
        WorldGen.Generate(tiles, seed: 99, sizeX: 64, sizeZ: 64);
        var distinct = new System.Collections.Generic.HashSet<byte>();
        for (var x = -16; x < 16; x++)
        for (var z = -16; z < 16; z++)
            distinct.Add(tiles.BiomeAt(x, z));
        distinct.Remove(BiomeBuiltins.UnknownId);
        Assert.NotEmpty(distinct);
    }
}
