using fennecs;
using CowColonySim.Sim.Components;

namespace CowColonySim.Sim.Systems;

public static class DemoWanderSystem
{
    public static void Step(World world)
    {
        world.Stream<Position>().For(static (ref Position p) => p = p with { X = p.X + 1 });
    }
}
