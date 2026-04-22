using System;
using CowColonySim.Sim.Systems;
using Xunit;

namespace CowColonySim.Tests;

public class CalendarSystemTests
{
    [Fact]
    public void Epoch_IsJan1_1999()
    {
        var dt = CalendarSystem.ToDateTime(0);
        Assert.Equal(new DateTime(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc), dt);
    }

    [Fact]
    public void StartOffset_Is8AM()
    {
        var dt = CalendarSystem.ToDateTime(CalendarSystem.StartTicksOffset);
        Assert.Equal(1999, dt.Year);
        Assert.Equal(1, dt.Month);
        Assert.Equal(1, dt.Day);
        Assert.Equal(8, dt.Hour);
        Assert.Equal(0, dt.Minute);
    }

    [Fact]
    public void AdvancingOneDay_AdvancesDateByOne()
    {
        var dt = CalendarSystem.ToDateTime(86400);
        Assert.Equal(new DateTime(1999, 1, 2, 0, 0, 0, DateTimeKind.Utc), dt);
    }

    [Fact]
    public void LeapYear_2000_HasFeb29()
    {
        var daysTo_2000_02_29 =
            (new DateTime(2000, 2, 29, 0, 0, 0, DateTimeKind.Utc) - CalendarSystem.Epoch).TotalDays;
        var dt = CalendarSystem.ToDateTime((long)daysTo_2000_02_29 * 86400);
        Assert.Equal(2000, dt.Year);
        Assert.Equal(2, dt.Month);
        Assert.Equal(29, dt.Day);
    }
}
