using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Pathfinding;

public static class TileMath
{
    public static TilePos TileAt(Position p)
    {
        var t = SimConstants.TileSizeMeters;
        return new TilePos(
            (int)MathF.Floor(p.X / t),
            (int)MathF.Floor(p.Y / t),
            (int)MathF.Floor(p.Z / t));
    }

    public static Position FeetOfTile(TilePos pos)
    {
        var t = SimConstants.TileSizeMeters;
        return new Position(pos.X * t + t * 0.5f, pos.Y * t, pos.Z * t + t * 0.5f);
    }

    public static float HorizontalDistance(Position a, Position b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
