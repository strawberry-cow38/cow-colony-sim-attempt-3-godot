using System;

namespace CowColonySim.Sim.Systems;

public static class CalendarSystem
{
    public static readonly DateTime Epoch =
        new(1999, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    public const long StartTicksOffset = 8L * 3600L;

    public static DateTime ToDateTime(long ticks) =>
        Epoch.AddSeconds(ticks);
}
