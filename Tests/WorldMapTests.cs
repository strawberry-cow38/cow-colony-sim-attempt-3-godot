using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Grid;
using Xunit;

namespace CowColonySim.Tests;

public class WorldMapTests
{
    [Fact]
    public void Generate_IsDeterministic()
    {
        BuiltinBiomes.RegisterAll();
        var a = WorldMapGenerator.Generate(seed: 1337);
        var b = WorldMapGenerator.Generate(seed: 1337);
        for (var z = 0; z < WorldMap.Height; z++)
        for (var x = 0; x < WorldMap.Width; x++)
            Assert.Equal(a.Get(x, z), b.Get(x, z));
    }

    [Fact]
    public void Generate_DifferentSeedsDiffer()
    {
        BuiltinBiomes.RegisterAll();
        var a = WorldMapGenerator.Generate(seed: 1);
        var b = WorldMapGenerator.Generate(seed: 2);
        var differs = false;
        for (var z = 0; z < WorldMap.Height && !differs; z++)
        for (var x = 0; x < WorldMap.Width  && !differs; x++)
            if (a.Get(x, z) != b.Get(x, z)) differs = true;
        Assert.True(differs, "two different seeds produced byte-identical maps");
    }

    [Fact]
    public void Generate_CoversMultipleBiomes()
    {
        BuiltinBiomes.RegisterAll();
        var map = WorldMapGenerator.Generate(seed: 42);
        var distinct = new System.Collections.Generic.HashSet<byte>();
        for (var z = 0; z < WorldMap.Height; z++)
        for (var x = 0; x < WorldMap.Width; x++)
            distinct.Add(map.BiomeAt(x, z));
        distinct.Remove(BiomeBuiltins.UnknownId);
        Assert.True(distinct.Count >= 4,
            $"expected at least 4 biomes across a 100x100 map, got {distinct.Count}");
    }

    [Fact]
    public void Temperature_DropsFromEquatorToPole()
    {
        BuiltinBiomes.RegisterAll();
        var map = WorldMapGenerator.Generate(seed: 7);
        var equatorSum = 0f;
        var poleSum = 0f;
        var halfZ = WorldMap.Height / 2;
        for (var x = 0; x < WorldMap.Width; x++)
        {
            equatorSum += map.Get(x, halfZ).TemperatureC;
            poleSum += map.Get(x, 0).TemperatureC;
        }
        var eq = equatorSum / WorldMap.Width;
        var pole = poleSum / WorldMap.Width;
        Assert.True(eq > pole + 20f,
            $"equator avg {eq:0.0} not clearly warmer than pole avg {pole:0.0}");
    }

    [Fact]
    public void InBounds_RejectsNegativeAndOversize()
    {
        Assert.True(WorldMap.InBounds(0, 0));
        Assert.True(WorldMap.InBounds(WorldMap.Width - 1, WorldMap.Height - 1));
        Assert.False(WorldMap.InBounds(-1, 0));
        Assert.False(WorldMap.InBounds(0, -1));
        Assert.False(WorldMap.InBounds(WorldMap.Width, 0));
        Assert.False(WorldMap.InBounds(0, WorldMap.Height));
    }
}
