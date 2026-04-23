using System;
using CowColonySim.Sim.Biomes;

namespace CowColonySim.Sim.Grid;

/// <summary>
/// Coordinate on the abstract 2D world map. The map is a 400×400 grid of
/// cells — each map cell is a candidate "pocket" the player can drop into
/// for the 3D playable region. Independent from <see cref="CellKey"/>,
/// which addresses tile-cells inside a single playable region.
/// </summary>
public readonly record struct WorldMapCoord(int X, int Z);

/// <summary>
/// Everything the 3D cell generator needs to know about one world-map
/// cell: its biome id (already classified) and the temperature / rainfall
/// the classifier used. Also carries a continent elevation + ocean flag
/// so the overworld hud can render land/sea and phase-3 travel can gate
/// "settle here" on non-ocean cells. BiomeId stays as the underlying
/// land classification even for ocean cells — pocket gen only ever runs
/// on land, so the classified biome is the one we'd use if this cell
/// ever became settleable.
/// </summary>
public readonly record struct WorldMapCell(
    byte BiomeId,
    float TemperatureC,
    float RainfallMm,
    float Elevation,
    bool IsOcean,
    bool HasRiver,
    bool IsLake);

/// <summary>
/// 400×400 abstract world map. Each cell carries a biome + climate stamped
/// once at worldgen via <see cref="WorldMapGenerator"/>. The player's
/// playable 3D region (a single <see cref="Cell"/>-sized pocket) is
/// generated on demand from a chosen map cell — terrain, rivers, and
/// structures vary per-visit but the biome + climate stay fixed.
/// </summary>
public sealed class WorldMap
{
    public const int Width  = 400;
    public const int Height = 400;

    private readonly byte[,] _biome = new byte[Width, Height];
    private readonly float[,] _temp = new float[Width, Height];
    private readonly float[,] _rain = new float[Width, Height];
    private readonly float[,] _elev = new float[Width, Height];
    private readonly bool[,]  _ocean = new bool[Width, Height];
    private readonly bool[,]  _river = new bool[Width, Height];
    private readonly bool[,]  _lake  = new bool[Width, Height];

    public int Seed { get; }

    public WorldMap(int seed) { Seed = seed; }

    public WorldMapCell Get(int x, int z)
    {
        if (!InBounds(x, z)) throw new ArgumentOutOfRangeException(nameof(x), $"({x},{z}) outside {Width}×{Height}");
        return new WorldMapCell(_biome[x, z], _temp[x, z], _rain[x, z], _elev[x, z], _ocean[x, z], _river[x, z], _lake[x, z]);
    }

    public WorldMapCell Get(WorldMapCoord c) => Get(c.X, c.Z);

    public byte BiomeAt(int x, int z) => _biome[x, z];
    public bool IsOcean(int x, int z) => _ocean[x, z];
    public bool IsOcean(WorldMapCoord c) => IsOcean(c.X, c.Z);
    public bool HasRiver(int x, int z) => _river[x, z];
    public bool IsLake(int x, int z) => _lake[x, z];
    public float ElevationAt(int x, int z) => _elev[x, z];

    internal void SetCell(int x, int z, byte biomeId, float tempC, float rainMm, float elevation, bool isOcean)
    {
        _biome[x, z] = biomeId;
        _temp[x, z] = tempC;
        _rain[x, z] = rainMm;
        _elev[x, z] = elevation;
        _ocean[x, z] = isOcean;
    }

    internal void SetRiver(int x, int z, bool v) { _river[x, z] = v; }
    internal void SetLake(int x, int z, bool v) { _lake[x, z] = v; }

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

    // Radial land bias: near map center the continent mask is pushed up by
    // CenterBias so the middle of the map is guaranteed land; near the
    // corners the bias falls to zero so edges are almost always ocean.
    // Threshold then carves the continent shape out of the biased noise.
    // Tuning these three values is the entire "main continent in middle +
    // scattered islands" aesthetic.
    public const float CenterBias = 0.60f;
    public const float OceanThreshold = 0.05f;

    // Lake seeding. Non-ocean cells whose Lake-noise mask clears LakeNoise
    // AND whose elevation sits below LakeElevCeil read as standing water.
    // Ceil keeps lakes off mountain shoulders; noise threshold tunes density.
    public const float LakeNoiseThreshold = 0.72f;
    public const float LakeElevCeil       = 0.35f;

    // River seeding. Cells whose RiverSource-noise mask clears threshold AND
    // whose elevation exceeds mountain floor spawn a downhill walker. Walker
    // follows steepest 8-neighbor descent, stamping HasRiver, until it hits
    // ocean/lake/existing river/local minimum. Local-min termination stamps
    // a lake on the sink so interior drainage reads as a pond.
    public const float RiverSourceNoiseThreshold = 0.75f;
    public const float RiverSourceElevFloor      = 0.55f;
    private const int  MaxRiverLength             = 400;

    public static WorldMap Generate(int seed)
    {
        var map = new WorldMap(seed);
        var noise = new NoiseStack(seed);
        var halfZ = WorldMap.Height / 2;
        var halfX = WorldMap.Width / 2;
        var maxDist = MathF.Sqrt(halfX * halfX + halfZ * halfZ);
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

            var dx = x - halfX;
            var dz = z - halfZ;
            var distFrac = MathF.Sqrt(dx * dx + dz * dz) / maxDist;
            var landBias = CenterBias * (1f - distFrac);
            var elevation = noise.Continent.GetNoise(nx, nz) + landBias;
            var isOcean = elevation < OceanThreshold;

            var biome = BiomeClassifier.Pick(tempC, rainMm);
            map.SetCell(x, z, biome, tempC, rainMm, elevation, isOcean);
        }

        StampLakes(map, noise);
        StampRivers(map, noise);
        return map;
    }

    private static void StampLakes(WorldMap map, NoiseStack noise)
    {
        for (var z = 0; z < WorldMap.Height; z++)
        for (var x = 0; x < WorldMap.Width; x++)
        {
            if (map.IsOcean(x, z)) continue;
            if (map.ElevationAt(x, z) > LakeElevCeil) continue;
            var nx = x * NoiseSpread;
            var nz = z * NoiseSpread;
            var mask = (noise.Lake.GetNoise(nx, nz) + 1f) * 0.5f;
            if (mask > LakeNoiseThreshold) map.SetLake(x, z, true);
        }
    }

    private static void StampRivers(WorldMap map, NoiseStack noise)
    {
        for (var z = 0; z < WorldMap.Height; z++)
        for (var x = 0; x < WorldMap.Width; x++)
        {
            if (map.IsOcean(x, z)) continue;
            if (map.ElevationAt(x, z) < RiverSourceElevFloor) continue;
            var nx = x * NoiseSpread;
            var nz = z * NoiseSpread;
            var src = (noise.RiverSource.GetNoise(nx, nz) + 1f) * 0.5f;
            if (src <= RiverSourceNoiseThreshold) continue;
            WalkRiver(map, x, z);
        }
    }

    private static void WalkRiver(WorldMap map, int startX, int startZ)
    {
        var cx = startX;
        var cz = startZ;
        for (var step = 0; step < MaxRiverLength; step++)
        {
            if (map.HasRiver(cx, cz)) return;
            map.SetRiver(cx, cz, true);
            if (map.IsOcean(cx, cz) || map.IsLake(cx, cz)) return;

            var here = map.ElevationAt(cx, cz);
            var bestX = cx;
            var bestZ = cz;
            var bestH = here;
            for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0) continue;
                var nx = cx + dx;
                var nz = cz + dz;
                if (!WorldMap.InBounds(nx, nz)) continue;
                var h = map.ElevationAt(nx, nz);
                if (h < bestH) { bestH = h; bestX = nx; bestZ = nz; }
            }
            if (bestX == cx && bestZ == cz)
            {
                map.SetLake(cx, cz, true);
                return;
            }
            cx = bestX;
            cz = bestZ;
        }
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
