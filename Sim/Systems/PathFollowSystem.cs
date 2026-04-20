using fennecs;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Pathfinding;

namespace CowColonySim.Sim.Systems;

public static class PathFollowSystem
{
    public const float SpeedMetersPerSecond = 3.0f;
    public const float ArriveThresholdMeters = 0.05f;

    public static void Step(World world, float dt)
    {
        var arrived = new List<Entity>();
        var step = SpeedMetersPerSecond * dt;

        world.Stream<Position, PathCurrent>().For((Entity e, ref Position p, ref PathCurrent path) =>
        {
            if (path.NextIndex >= path.Nodes.Length)
            {
                arrived.Add(e);
                return;
            }
            var target = TileMath.FeetOfTile(path.Nodes[path.NextIndex]);
            var dx = target.X - p.X;
            var dy = target.Y - p.Y;
            var dz = target.Z - p.Z;
            var dist = MathF.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dist <= step || dist <= ArriveThresholdMeters)
            {
                p = target;
                path.NextIndex++;
                if (path.NextIndex >= path.Nodes.Length) arrived.Add(e);
            }
            else
            {
                var inv = step / dist;
                p = new Position(p.X + dx * inv, p.Y + dy * inv, p.Z + dz * inv);
            }
        });

        foreach (var e in arrived) e.Remove<PathCurrent>();
    }
}
