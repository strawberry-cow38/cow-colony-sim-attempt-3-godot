using fennecs;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;

namespace CowColonySim.Sim.Systems;

public static class WanderSystem
{
    public const int WanderSearchRadius = 200;
    public const int MinDistanceFromStart = 3;

    public static void Step(World world, TileWorld tiles, Random rng)
    {
        var withPath = new HashSet<Entity>();
        world.Stream<PathRequest>().For((in Entity e, ref PathRequest _) => withPath.Add(e));
        world.Stream<PathCurrent>().For((in Entity e, ref PathCurrent _) => withPath.Add(e));

        var toPlan = new List<(Entity Entity, Position Pos)>();
        world.Stream<Position, Colonist>().For((in Entity e, ref Position p, ref Colonist _) =>
        {
            if (withPath.Contains(e)) return;
            toPlan.Add((e, p));
        });

        foreach (var (e, pos) in toPlan)
        {
            var start = TileMath.TileAt(pos);
            var target = PickRandomReachable(tiles, start, rng);
            if (target.HasValue) e.Add(new PathRequest(target.Value));
        }
    }

    private static TilePos? PickRandomReachable(TileWorld tiles, TilePos start, Random rng)
    {
        if (!Walkability.IsStandable(tiles, start)) return null;

        var visited = new HashSet<TilePos> { start };
        var frontier = new Queue<TilePos>();
        frontier.Enqueue(start);
        var candidates = new List<TilePos>();
        while (frontier.Count > 0 && visited.Count < WanderSearchRadius)
        {
            var cur = frontier.Dequeue();
            var manhattan = Math.Abs(cur.X - start.X) + Math.Abs(cur.Y - start.Y) + Math.Abs(cur.Z - start.Z);
            if (manhattan >= MinDistanceFromStart) candidates.Add(cur);
            foreach (var nb in Walkability.WalkableNeighbors(tiles, cur))
            {
                if (visited.Add(nb)) frontier.Enqueue(nb);
            }
        }
        if (candidates.Count == 0) return null;
        return candidates[rng.Next(candidates.Count)];
    }
}
