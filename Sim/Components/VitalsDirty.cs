namespace CowColonySim.Sim.Components;

/// <summary>
/// Tag component signalling that a colonist's hunger / sleep / mood inputs
/// changed and the tier-1 (Urgent) vitals-driven jobs (eat, sleep) should be
/// reconsidered. Scaffold: not yet consumed — reserved for the vitals system.
/// </summary>
public record struct VitalsDirty;
