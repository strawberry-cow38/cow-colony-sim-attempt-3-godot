using System;
using CowColonySim.Sim.Biomes;

namespace CowColonySim.Sim.Grid;

/// <summary>
/// Coordinate on the abstract 2D world map. The map is a 100×100 grid of
/// cells — each map cell is a candidate "pocket" the player can drop into
/// for the 3D playable region. Independent from <see cref="CellKey"/>,
/// which addresses tile-cells inside a single playable region.
/// </summary>
public readonly record struct WorldMapCoord(int X, int Z);

/// <summary>
/// Everything the 3D cell generator needs to know about one world-map
/// cell: its biome id (already classified) and the temperature / rainfall
/// the classifier used. Kept as a value-type so callers can pass a whole
/// cell by copy without allocating.
/// </summary>
public readonly record struct WorldMapCell(byte BiomeId, float TemperatureC, float RainfallMm);

/// <summary>
/// 100×100 abstract world map. Each cell carries a biome + climate stamped
/// once at worldgen via <see cref="WorldMapGenerator"/>. The player's
/// playable 3D region (a single <see cref="Cell"/>-sized pocket) is
/// generated on demand from a chosen map cell — terrain, rivers, and
/// structures vary per-visit but the biome + climate stay fixed.
/// </summary>
public sealed class WorldMap
{
    public const int Width  = 100;
    public const int Height = 100;

    private readonly byte[,] _biome = new byte[Width, Height];
    private readonly float[,] _temp = new float[Width, Height];
    private readonly float[,] _rain = new float[Width, Height];

    public int Seed { get; }

    public WorldMap(int seed) { Seed = seed; }

    public WorldMapCell Get(int x, int z)
    {
        if (!InBounds(x, z)) throw new ArgumentOutOfRangeException(nameof(x), $"({x},{z}) outside {Width}×{Height}");
        return new WorldMapCell(_biome[x, z], _temp[x, z], _rain[x, z]);
    }

    public WorldMapCell Get(WorldMapCoord c) => Get(c.X, c.Z);

    public byte BiomeAt(int x, int z) => _biome[x, z];

    internal void SetCell(int x, int z, byte biomeId, float tempC, float rainMm)
    {
        _biome[x, z] = biomeId;
        _temp[x, z] = tempC;
        _rain[x, z] = rainMm;
    }

    public static bool InBounds(int x, int z) =>
        (uint)x < Width && (uint)z < Height;

    public static WorldMapCoord Center =>
        new(Width / 2, Height / 2);
}

/// <summary>
/// Stamps biome + climate into a <see cref="WorldMap"/> once at worldgen.
/// Latitude-driven temperature (equator-to-pole on the Z axis) plus a
/// low-frequency noise wobble; rainfall is a pure noise field. No rivers
/// or mountains at the map level — those emerge inside the playable
/// pocket when a cell is visited.
/// </summary>
public static class WorldMapGenerator
{
    public const float TempEquatorC  = 30f;
    public const float TempPoleC     = -25f;
    public const float TempNoiseAmpC = 5f;
    public const float RainMinMm     = 0f;
    public const float RainMaxMm     = 3000f;

    // Noise scale. NoiseStack octaves are tuned for per-tile sampling; the
    // map is 100× coarser. Multiplying coords by this spread factor keeps
    // the noise varying at roughly one cycle per 3-5 map cells — enough to
    // give interesting biome patchiness without tile-level aliasing.
    private const float NoiseSpread = 32f;

    public static WorldMap Generate(int seed)
    {
        var map = new WorldMap(seed);
        var noise = new NoiseStack(seed);
        var halfZ = WorldMap.Height / 2;
        for (var z = 0; z < WorldMap.Height; z++)
        for (var x = 0; x < WorldMap.Width; x++)
        {
            var nx = x * NoiseSpread;
            var nz = z * NoiseSpread;
            var latFrac = halfZ > 0 ? Math.Min(1f, Math.Abs(z - halfZ) / (float)halfZ) : 0f;
            var tNoise = noise.Temperature.GetNoise(nx, nz) * TempNoiseAmpC;
            var tempC  = Lerp(TempEquatorC, TempPoleC, latFrac) + tNoise;

            var rRaw   = (noise.Rainfall.GetNoise(nx, nz) + 1f) * 0.5f;
            var rainMm = Lerp(RainMinMm, RainMaxMm, rRaw);

            var biome = BiomeClassifier.Pick(tempC, rainMm);
            map.SetCell(x, z, biome, tempC, rainMm);
        }
        return map;
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
