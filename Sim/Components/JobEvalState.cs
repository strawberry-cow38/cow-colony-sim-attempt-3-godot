namespace CowColonySim.Sim.Components;

/// <summary>
/// Per-colonist bookkeeping for the staggered job evaluator. <see cref="Bucket"/>
/// is the colonist's assigned stagger slot in [0, <see cref="Sim.Systems.JobSystem.StaggerPeriod"/>).
/// <see cref="LastEvalTick"/> is the sim tick of the last re-evaluation; combined
/// with the job board's per-cell dirty stamp it lets the system skip work when
/// nothing nearby has changed since the last look.
///
/// Use <see cref="Fresh"/> for newly-spawned colonists so the first eval always
/// runs — otherwise an initial <c>LastEvalTick=0</c> races with tasks added at
/// tick 0 and the colonist mistakenly skips its first scan.
/// </summary>
public record struct JobEvalState(byte Bucket, long LastEvalTick)
{
    public static JobEvalState Fresh(byte bucket) => new(bucket, -1);
}
