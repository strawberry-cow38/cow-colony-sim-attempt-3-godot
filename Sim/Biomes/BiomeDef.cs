using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Biomes;

/// <summary>
/// Data record describing a single biome. Adding a new biome means adding
/// a new <see cref="BiomeDef"/> to <see cref="BiomeRegistry"/> and a classifier
/// rule — no changes in renderers, storage, or systems that only carry the
/// biome byte.
/// </summary>
public sealed record BiomeDef(
    byte Id,
    string Name,
    TileKind DefaultSurface,
    float DebugR,
    float DebugG,
    float DebugB);
