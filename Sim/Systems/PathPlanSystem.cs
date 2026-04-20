using fennecs;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;

namespace CowColonySim.Sim.Systems;

public static class PathPlanSystem
{
    public static void Step(World world, TileWorld tiles)
    {
        var toPlan = new List<(Entity Entity, TilePos Start, TilePos Goal)>();
        world.Stream<Position, PathRequest>().For((in Entity e, ref Position p, ref PathRequest req) =>
        {
            toPlan.Add((e, TileMath.TileAt(p), req.Goal));
        });

        foreach (var (e, start, goal) in toPlan)
        {
            var path = AStarPathfinder.FindPath(tiles, start, goal);
            e.Remove<PathRequest>();
            if (path != null && path.Length >= 1)
                e.Add(new PathCurrent(path, path[0] == start ? 1 : 0));
        }
    }
}
