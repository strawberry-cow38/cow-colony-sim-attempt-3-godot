using CowColonySim.Sim;
using CowColonySim.Sim.Systems;
using Xunit;

namespace CowColonySim.Tests;

public class TimeOfDayTests
{
    [Fact]
    public void DayFraction_StartsAtZero()
    {
        var tod = new TimeOfDaySystem();
        Assert.Equal(0f, tod.DayFraction);
    }

    [Fact]
    public void DayFraction_HalfwayThroughDay_IsHalf()
    {
        var tod = new TimeOfDaySystem();
        tod.SetTicks(SimConstants.TicksPerDay / 2);
        Assert.Equal(0.5f, tod.DayFraction, precision: 4);
    }

    [Fact]
    public void DayFraction_Wraps()
    {
        var tod = new TimeOfDaySystem();
        tod.SetTicks(SimConstants.TicksPerDay);
        Assert.Equal(0f, tod.DayFraction);
    }

    [Fact]
    public void HourAndMinute_AtNoon()
    {
        var tod = new TimeOfDaySystem();
        tod.SetTicks(SimConstants.TicksPerDay / 2);
        Assert.Equal(12, tod.Hour);
        Assert.Equal(0, tod.Minute);
    }

    [Fact]
    public void OneDay_Is_24RealMinutes_At_60Hz()
    {
        Assert.Equal(60 * 60 * 24, SimConstants.TicksPerDay);
    }
}
