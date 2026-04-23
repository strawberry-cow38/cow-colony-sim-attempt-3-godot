using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Biomes;

/// <summary>
/// Initial Whittaker-style biome set. Ids are stable so save files and
/// renderer palettes can key off them. Adding more later means picking the
/// next free id and extending <see cref="BiomeClassifier"/>.
/// </summary>
public static class BiomeBuiltins
{
    public const byte UnknownId          = 0;
    public const byte SnowId             = 1;
    public const byte TundraId           = 2;
    public const byte TaigaId            = 3;
    public const byte GrasslandId        = 4;
    public const byte TemperateForestId  = 5;
    public const byte DesertId           = 6;
    public const byte SavannaId          = 7;
    public const byte JungleId           = 8;
}

public static class BuiltinBiomes
{
    public static readonly BiomeDef Unknown =
        new(BiomeBuiltins.UnknownId, "Unknown", TileKind.Floor, 1f, 0f, 1f);

    public static readonly BiomeDef Snow =
        new(BiomeBuiltins.SnowId, "Snow", TileKind.Floor, 0.95f, 0.98f, 1.00f);

    public static readonly BiomeDef Tundra =
        new(BiomeBuiltins.TundraId, "Tundra", TileKind.Floor, 0.72f, 0.78f, 0.70f);

    public static readonly BiomeDef Taiga =
        new(BiomeBuiltins.TaigaId, "Taiga", TileKind.Floor, 0.30f, 0.50f, 0.35f);

    public static readonly BiomeDef Grassland =
        new(BiomeBuiltins.GrasslandId, "Grassland", TileKind.Floor, 0.55f, 0.80f, 0.35f);

    public static readonly BiomeDef TemperateForest =
        new(BiomeBuiltins.TemperateForestId, "Temperate Forest", TileKind.Floor, 0.20f, 0.55f, 0.25f);

    public static readonly BiomeDef Desert =
        new(BiomeBuiltins.DesertId, "Desert", TileKind.Sand, 0.95f, 0.85f, 0.55f);

    public static readonly BiomeDef Savanna =
        new(BiomeBuiltins.SavannaId, "Savanna", TileKind.Floor, 0.85f, 0.75f, 0.35f);

    public static readonly BiomeDef Jungle =
        new(BiomeBuiltins.JungleId, "Jungle", TileKind.Floor, 0.10f, 0.45f, 0.20f);

    public static void RegisterAll()
    {
        BiomeRegistry.Register(Unknown);
        BiomeRegistry.Register(Snow);
        BiomeRegistry.Register(Tundra);
        BiomeRegistry.Register(Taiga);
        BiomeRegistry.Register(Grassland);
        BiomeRegistry.Register(TemperateForest);
        BiomeRegistry.Register(Desert);
        BiomeRegistry.Register(Savanna);
        BiomeRegistry.Register(Jungle);
    }
}
