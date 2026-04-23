using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Components;

/// <summary>
/// The job a colonist is currently executing (or Idle, meaning no job).
/// <paramref name="JobId"/> is 0 when Idle; otherwise the id issued by
/// <see cref="Sim.Jobs.JobBoard"/>.
/// </summary>
public readonly record struct CurrentJob(int JobId, JobTier Tier, TilePos Target)
{
    public static readonly CurrentJob None = new(0, JobTier.Idle, default);
}
