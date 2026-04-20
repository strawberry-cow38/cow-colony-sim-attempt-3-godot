using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Pathfinding;

public static class Walkability
{
    public static bool IsSupport(Tile t) => t.Kind == TileKind.Solid || t.Kind == TileKind.Floor;

    public static bool IsStandable(TileWorld world, TilePos pos)
    {
        if (!world.Get(pos).IsEmpty) return false;
        return IsSupport(world.Get(pos.Offset(0, -1, 0)));
    }

    public static bool HasHeadroom(TileWorld world, TilePos pos)
        => world.Get(pos.Offset(0, 1, 0)).IsEmpty;

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
