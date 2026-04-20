namespace CowColonySim.Sim;

public enum SimSpeed
{
    X1 = 1,
    X2 = 2,
    X3 = 3,
    X6 = 6,
}

public static class SimConstants
{
    public const int SimHz = 60;
    public const double SimDt = 1.0 / SimHz;

    public const float TileWidthMeters = 1.5f;
    public const float TileHeightMeters = 0.75f;
    public const int HeadroomTiles = 2;
    public const int ChunkSize = 16;

    public const int SecondsPerDay = 60 * 24;
    public const int TicksPerDay = SimHz * SecondsPerDay;

    public static readonly SimSpeed[] SpeedSteps = { SimSpeed.X1, SimSpeed.X2, SimSpeed.X3, SimSpeed.X6 };
}
