using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Pathfinding;

public static class Walkability
{
    public static bool IsSupport(Tile t) =>
        t.Kind == TileKind.Solid || t.Kind == TileKind.Floor || t.Kind == TileKind.Sand;

    public static bool IsStandable(TileWorld world, TilePos pos)
    {
        if (world.PlayableBoundsHalf > 0)
        {
            var h = world.PlayableBoundsHalf;
            if (pos.X < -h || pos.X >= h) return false;
            if (pos.Z < -h || pos.Z >= h) return false;
        }
        if (!world.Get(pos).IsEmpty) return false;
        if (world.IsBlocked(pos)) return false;
        if (!IsSupport(world.Get(pos.Offset(0, -1, 0)))) return false;
        for (var dy = 1; dy < SimConstants.HeadroomTiles; dy++)
        {
            if (!world.Get(pos.Offset(0, dy, 0)).IsEmpty) return false;
        }
        return true;
    }

    public static bool HasHeadroom(TileWorld world, TilePos pos)
    {
        for (var dy = 1; dy <= SimConstants.HeadroomTiles; dy++)
        {
            if (!world.Get(pos.Offset(0, dy, 0)).IsEmpty) return false;
        }
        return true;
    }

    private static readonly (int dx, int dz)[] Horiz =
    {
        ( 1, 0), (-1, 0), (0,  1), (0, -1),
    };

    public static IEnumerable<TilePos> WalkableNeighbors(TileWorld world, TilePos from)
    {
        foreach (var (dx, dz) in Horiz)
        {
            for (var dy = -1; dy <= 1; dy++)
            {
                var to = from.Offset(dx, dy, dz);
                if (!IsStandable(world, to)) continue;
                if (dy == 1 && !HasHeadroom(world, from)) continue;
                if (dy == -1 && !HasHeadroom(world, to)) continue;
                yield return to;
            }
        }
    }
}
