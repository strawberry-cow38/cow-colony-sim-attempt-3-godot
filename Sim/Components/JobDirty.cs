namespace CowColonySim.Sim.Components;

/// <summary>
/// Tag component forcing <see cref="Sim.Systems.JobSystem"/> to re-evaluate
/// this colonist on its next tick regardless of stagger bucket or nearby-cell
/// dirty state. The tag is removed after evaluation. Add it when a colonist
/// finishes a job, is newly spawned, or is otherwise kicked (player order,
/// interruption).
/// </summary>
public record struct JobDirty;
