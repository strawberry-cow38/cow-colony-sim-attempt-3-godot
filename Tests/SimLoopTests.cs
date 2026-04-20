using CowColonySim.Sim;
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

}
