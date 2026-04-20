using System.Threading.Tasks;
using fennecs;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;

namespace CowColonySim.Sim.Systems;

public static class PathPlanSystem
{
    public static void Step(World world, TileWorld tiles)
    {
        var requests = new List<(Entity Entity, TilePos Start, TilePos Goal)>();
        world.Stream<Position, PathRequest>().For((in Entity e, ref Position p, ref PathRequest req) =>
        {
            requests.Add((e, TileMath.TileAt(p), req.Goal));
        });
        if (requests.Count == 0) return;

        var results = new (TilePos[]? Path, TilePos Start)[requests.Count];
        Parallel.For(0, requests.Count, i =>
        {
            var (_, start, goal) = requests[i];
            results[i] = (AStarPathfinder.FindPath(tiles, start, goal), start);
        });

        for (var i = 0; i < requests.Count; i++)
        {
            var (e, _, _) = requests[i];
            var (path, start) = results[i];
            e.Remove<PathRequest>();
            if (path != null && path.Length >= 1)
                e.Add(new PathCurrent(path, path[0] == start ? 1 : 0));
        }
    }
}
