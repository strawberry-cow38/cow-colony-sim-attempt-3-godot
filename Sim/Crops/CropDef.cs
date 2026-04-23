namespace CowColonySim.Sim.Crops;

/// <summary>
/// Shape of one canopy in world space. Trees are just two primitives — a
/// trunk cylinder and a canopy of <see cref="CanopyShape"/>. Crops that
/// aren't trees (wheat, berries, etc. later) also reuse this record with
/// cube/sphere shapes.
/// </summary>
public enum CanopyShape : byte { Sphere, Cone, Cube }

/// <summary>
/// Metadata for one kind of crop (tree, bush, or grown produce). Registered
/// at startup via <see cref="CropRegistry"/> and referenced from
/// <see cref="Components.Crop"/> by <see cref="Id"/>.
///
/// Growth: <see cref="GrowthTicksToMature"/> sim ticks from sapling
/// (growth=0) to mature (growth=1). Yield at harvest scales linearly from
/// zero at <see cref="MinYieldGrowth"/> to <see cref="MaxYield"/> at 1.0.
/// </summary>
public readonly record struct CropDef(
    byte Id,
    string Name,
    int GrowthTicksToMature,
    float MinYieldGrowth,
    int MaxYield,
    uint TrunkColor,
    uint CanopyColor,
    CanopyShape CanopyShape,
    float TrunkHeightMeters,
    float TrunkRadiusMeters,
    float CanopyHeightMeters,
    float CanopyRadiusMeters);
