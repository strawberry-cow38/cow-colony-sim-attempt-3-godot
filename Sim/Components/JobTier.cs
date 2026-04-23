namespace CowColonySim.Sim.Components;

/// <summary>
/// Priority tiers for colonist jobs. Lower ordinal = higher priority.
/// A colonist re-evaluating its job only scans tasks at tiers strictly
/// above its current tier (numerically lower). Idle colonists scan everything.
/// </summary>
public enum JobTier : byte
{
    Emergency = 0,
    Urgent = 1,
    Assigned = 2,
    Auto = 3,
    Idle = 4,
}
