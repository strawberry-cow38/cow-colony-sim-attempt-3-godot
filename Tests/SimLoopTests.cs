using CowColonySim.Sim;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Systems;
using fennecs;
using Xunit;

namespace CowColonySim.Tests;

public class SimLoopTests
{
    [Fact]
    public void Accumulator_ExactlyNTicks_After_NFrames()
    {
        var steps = 0;
        var loop = new SimLoop(_ => steps++);

        for (var i = 0; i < 60; i++)
        {
            loop.Advance(SimConstants.SimDt);
        }

        Assert.Equal(60, loop.Tick);
        Assert.Equal(60, steps);
    }

    [Fact]
    public void Speed2x_Doubles_Ticks()
    {
        var steps = 0;
        var loop = new SimLoop(_ => steps++) { Speed = SimSpeed.X2 };

        for (var i = 0; i < 60; i++)
        {
            loop.Advance(SimConstants.SimDt);
        }

        Assert.Equal(120, loop.Tick);
        Assert.Equal(120, steps);
    }

    [Fact]
    public void DemoWander_MovesByOne_PerTick()
    {
        using var world = new World();
        var entity = world.Spawn().Add(new Position(0, 5));

        for (var i = 0; i < 10; i++)
        {
            DemoWanderSystem.Step(world);
        }

        var pos = entity.Ref<Position>();
        Assert.Equal(10, pos.X);
        Assert.Equal(5, pos.Y);
    }
}
