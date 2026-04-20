namespace CowColonySim.Sim.Systems;

public sealed class TimeOfDaySystem
{
    public long Ticks { get; private set; }

    public float DayFraction => (float)(Ticks % SimConstants.TicksPerDay) / SimConstants.TicksPerDay;

    public int Hour => (int)(DayFraction * 24f);
    public int Minute => (int)(DayFraction * 24f * 60f) % 60;

    public void Step() => Ticks++;

    public void SetTicks(long ticks) => Ticks = ticks < 0 ? 0 : ticks;
}
