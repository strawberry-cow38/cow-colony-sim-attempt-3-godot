using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Pathfinding;

public static class AStarPathfinder
{
    public const int MaxExpansions = 20_000;

    public static TilePos[]? FindPath(TileWorld world, TilePos start, TilePos goal)
    {
        if (start == goal) return new[] { start };
        if (!Walkability.IsStandable(world, start)) return null;
        if (!Walkability.IsStandable(world, goal)) return null;

        var open = new PriorityQueue<TilePos, int>();
        var cameFrom = new Dictionary<TilePos, TilePos>();
        var gScore = new Dictionary<TilePos, int> { [start] = 0 };
        open.Enqueue(start, Heuristic(start, goal));

        var expansions = 0;
        while (open.Count > 0)
        {
            if (++expansions > MaxExpansions) return null;

            var current = open.Dequeue();
            if (current == goal) return Reconstruct(cameFrom, current);

            var currentG = gScore[current];
            foreach (var neighbor in Walkability.WalkableNeighbors(world, current))
            {
                var tentativeG = currentG + StepCost(current, neighbor);
                if (gScore.TryGetValue(neighbor, out var existingG) && tentativeG >= existingG) continue;
                cameFrom[neighbor] = current;
                gScore[neighbor] = tentativeG;
                open.Enqueue(neighbor, tentativeG + Heuristic(neighbor, goal));
            }
        }
        return null;
    }

    private static int Heuristic(TilePos a, TilePos b)
    {
        var dx = Math.Abs(a.X - b.X);
        var dy = Math.Abs(a.Y - b.Y);
        var dz = Math.Abs(a.Z - b.Z);
        return (dx + dz) * 10 + dy * 12;
    }

    private static int StepCost(TilePos a, TilePos b)
    {
        var dy = Math.Abs(a.Y - b.Y);
        return dy == 0 ? 10 : 14;
    }

    private static TilePos[] Reconstruct(Dictionary<TilePos, TilePos> cameFrom, TilePos goal)
    {
        var path = new List<TilePos> { goal };
        var cur = goal;
        while (cameFrom.TryGetValue(cur, out var prev))
        {
            path.Add(prev);
            cur = prev;
        }
        path.Reverse();
        return path.ToArray();
    }
}
