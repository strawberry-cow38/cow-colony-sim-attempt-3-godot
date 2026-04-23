using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Biomes;

/// <summary>
/// Data record describing a single biome. Adding a new biome means adding
/// a new <see cref="BiomeDef"/> to <see cref="BiomeRegistry"/> and a classifier
/// rule — no changes in renderers, storage, or systems that only carry the
/// biome byte.
/// </summary>
/// <summary>
/// <paramref name="TopAtlasCellOverride"/> / <paramref name="SideAtlasCellOverride"/>
/// (−1 = none) let a biome redirect Floor-tile rendering to a neutral atlas cell
/// so the biome tint isn't fighting the green grass texture. Snow → white cell,
/// Desert → sand cell. Does not affect Water overlays or naturally-sandy beach
/// tiles (they already pick sand by TileKind).
/// </summary>
public sealed record BiomeDef(
    byte Id,
    string Name,
    TileKind DefaultSurface,
    float DebugR,
    float DebugG,
    float DebugB,
    int TopAtlasCellOverride = -1,
    int SideAtlasCellOverride = -1);
