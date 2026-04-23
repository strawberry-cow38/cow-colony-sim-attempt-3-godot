using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Jobs;

/// <summary>
/// A task published to the <see cref="JobBoard"/>. Identified by a monotonic
/// integer so tasks can be removed and referenced by <see cref="CurrentJob"/>
/// without holding a reference to the collection.
/// </summary>
public readonly record struct JobTask(int Id, JobTier Tier, TilePos Target);
