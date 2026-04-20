using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Pathfinding;

public static class TileMath
{
    public static TilePos TileAt(Position p)
    {
        var w = SimConstants.TileWidthMeters;
        var h = SimConstants.TileHeightMeters;
        return new TilePos(
            (int)MathF.Floor(p.X / w),
            (int)MathF.Floor(p.Y / h),
            (int)MathF.Floor(p.Z / w));
    }

    public static Position FeetOfTile(TilePos pos)
    {
        var w = SimConstants.TileWidthMeters;
        var h = SimConstants.TileHeightMeters;
        return new Position(pos.X * w + w * 0.5f, pos.Y * h, pos.Z * w + w * 0.5f);
    }

    public static float HorizontalDistance(Position a, Position b)
    {
        var dx = a.X - b.X;
        var dz = a.Z - b.Z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }
}
