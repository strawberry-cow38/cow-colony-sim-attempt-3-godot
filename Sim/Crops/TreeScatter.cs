using fennecs;
using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;

namespace CowColonySim.Sim.Crops;

/// <summary>
/// One-shot initial tree scatter, called from <c>SimHost.SettleAt</c> after
/// <see cref="WorldGen.Generate"/> has laid down terrain. Picks tree-friendly
/// biomes, requires flat ground (all four neighbour surface columns at the
/// same Y), respects the colony claim exclusion, and spawns a mix of growth
/// stages so the pocket isn't uniformly mature.
///
/// Sapling refill over time is a separate long-tier system (not in phase A).
/// </summary>
public static class TreeScatter
{
    /// <summary>Fraction of the pocket's flat-grass tiles to seed as trees.</summary>
    public const float DensityFraction = 0.06f;

    /// <summary>Minimum Chebyshev distance from colony origin (0,0,0) to allow
    /// a tree — keeps the initial spawn area clear.</summary>
    public const int ColonyClearRadius = 12;

    /// <summary>Biomes that grow trees. Desert / snow / stone / tundra don't.</summary>
    private static readonly HashSet<byte> TreeBiomes = new()
    {
        BiomeBuiltins.GrasslandId,
        BiomeBuiltins.TemperateForestId,
        BiomeBuiltins.TaigaId,
        BiomeBuiltins.SavannaId,
        BiomeBuiltins.JungleId,
    };

    public static int Populate(
        fennecs.World world,
        TileWorld tiles,
        int pocketHalfSize,
        Random rng)
    {
        // Pre-sample every candidate tile. Flat ground check walks 4 neighbours
        // so we can't easily parallelise without sharing tiles state writes;
        // fine for the one-shot settle cost.
        var candidates = new List<(int x, int z, int surfaceY, byte biome)>();
        for (var x = -pocketHalfSize; x < pocketHalfSize; x++)
        for (var z = -pocketHalfSize; z < pocketHalfSize; z++)
        {
            if (Math.Max(Math.Abs(x), Math.Abs(z)) < ColonyClearRadius) continue;
            var biome = tiles.BiomeAt(x, z);
            if (!TreeBiomes.Contains(biome)) continue;
            var sy = WorldGen.SurfaceY(tiles, x, z);
            if (!IsFlatGround(tiles, x, z, sy)) continue;
            candidates.Add((x, z, sy, biome));
        }

        if (candidates.Count == 0) return 0;
        var target = (int)(candidates.Count * DensityFraction);
        // Shuffle in place, take first `target`.
        for (var i = candidates.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }

        var placed = 0;
        for (var i = 0; i < target && i < candidates.Count; i++)
        {
            var (x, z, sy, _) = candidates[i];
            var feet = new TilePos(x, sy, z);
            if (tiles.IsBlocked(feet)) continue;
            var kindId = BuiltinCrops.RandomTreeKind(rng);
            // Mix of growth stages: uniform 0.1..1.
            var growth = 0.1f + 0.9f * (float)rng.NextDouble();
            SpawnTree(world, tiles, feet, kindId, growth);
            placed++;
        }
        return placed;
    }

    public static Entity SpawnTree(fennecs.World world, TileWorld tiles, TilePos feet, byte kindId, float growth)
    {
        tiles.SetBlocked(feet, true);
        var e = world.Spawn();
        e.Add(new Crop(kindId, growth, MarkedJobId: 0));
        e.Add(new CropTile(feet));
        e.Add(new Position(feet.X + 0.5f, feet.Y, feet.Z + 0.5f));
        return e;
    }

    public static void DespawnTree(fennecs.World world, TileWorld tiles, Entity e, TilePos feet)
    {
        tiles.SetBlocked(feet, false);
        e.Despawn();
    }

    private static bool IsFlatGround(TileWorld tiles, int x, int z, int sy)
    {
        // Surface column must be land (not water/ocean) — water returns
        // surface at its water top which we'd otherwise happily scatter on.
        var under = tiles.Get(new TilePos(x, sy - 1, z));
        if (under.Kind != TileKind.Solid && under.Kind != TileKind.Floor) return false;
        if (!Walkability.IsStandable(tiles, new TilePos(x, sy, z))) return false;
        // Visual corners can slope even when the tile column's Y matches.
        // Each 4-corner delta must be 0 on this tile AND the 4 neighbors so
        // the tree sits flush on a truly level patch, not a ramp face.
        if (tiles.TerrainSlope(x, z) != 0) return false;
        if (tiles.TerrainSlope(x + 1, z) != 0) return false;
        if (tiles.TerrainSlope(x - 1, z) != 0) return false;
        if (tiles.TerrainSlope(x, z + 1) != 0) return false;
        if (tiles.TerrainSlope(x, z - 1) != 0) return false;
        var s1 = WorldGen.SurfaceY(tiles, x + 1, z);
        var s2 = WorldGen.SurfaceY(tiles, x - 1, z);
        var s3 = WorldGen.SurfaceY(tiles, x, z + 1);
        var s4 = WorldGen.SurfaceY(tiles, x, z - 1);
        return s1 == sy && s2 == sy && s3 == sy && s4 == sy;
    }
}
