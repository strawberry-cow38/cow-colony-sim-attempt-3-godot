namespace CowColonySim.Sim.Components;

// Minimum tick before an idle colonist may request another wander. Without
// this, idle cows file a PathRequest every tick, pinning Parallel.For in
// PathPlanSystem and keeping worker threads hot for no reason.
public record struct WanderCooldown(long NotBeforeTick);
