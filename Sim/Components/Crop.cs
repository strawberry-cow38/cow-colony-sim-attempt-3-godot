using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Components;

/// <summary>
/// Plant entity — the single component shape covering trees, bushes, wheat,
/// and anything else that grows over time, occupies a tile, and eventually
/// gets harvested.
///
/// <paramref name="KindId"/> indexes into <see cref="Sim.Crops.CropRegistry"/>
/// for growth rate, yield, and visuals. <paramref name="Growth"/> is 0..1
/// where 1 is mature. <paramref name="MarkedJobId"/> is the id of a pending
/// chop/harvest job on the <see cref="Sim.Jobs.JobBoard"/> (0 = unmarked) —
/// designators set it, the chop system reads it.
/// </summary>
public record struct Crop(byte KindId, float Growth, int MarkedJobId);

/// <summary>Tile this crop occupies. Separate from <see cref="Position"/>
/// so the growth / chop systems can find neighbours without rounding.</summary>
public record struct CropTile(TilePos Pos);
