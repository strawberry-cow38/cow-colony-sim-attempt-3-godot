using fennecs;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Jobs;
using CowColonySim.Sim.Pathfinding;

namespace CowColonySim.Sim.Systems;

/// <summary>
/// Staggered, dirty-gated job evaluator. Runs every sim tick but each
/// colonist is only revisited every <see cref="StaggerPeriod"/> ticks (modulo
/// their <see cref="JobEvalState.Bucket"/>), or immediately if tagged
/// <see cref="JobDirty"/>. Even when it's a colonist's turn the scan is
/// skipped if nothing in their 9-cell job-board window has changed since
/// their last look (via <see cref="JobBoard.DirtySinceNear"/>).
///
/// Scan rule: only tasks strictly above the colonist's current tier
/// (numerically lower <see cref="JobTier"/>) are considered. An Auto-tier
/// colonist only ever scans Emergency/Urgent/Assigned; an Idle colonist
/// scans everything.
///
/// Scaffold note: assigning a task here only writes <see cref="CurrentJob"/>.
/// Actual task execution (pathing, reservation, completion) is not yet wired.
/// </summary>
public static class JobSystem
{
    public const int StaggerPeriod = 20;

    public static void Step(World world, JobBoard board, long tick)
    {
        // Collect the eligible set first so we can mutate (remove JobDirty,
        // update JobEvalState, replace CurrentJob) without iterating the
        // stream we're reading from.
        var eligible = new List<(Entity Entity, TilePos Pos, JobEvalState State, CurrentJob Job, bool Forced)>();
        world.Stream<Position, Colonist, JobEvalState, CurrentJob>()
            .For((in Entity e, ref Position p, ref Colonist _, ref JobEvalState s, ref CurrentJob j) =>
            {
                var forced = e.Has<JobDirty>();
                var bucketTurn = (tick % StaggerPeriod) == s.Bucket;
                if (!forced && !bucketTurn) return;
                eligible.Add((e, TileMath.TileAt(p), s, j, forced));
            });

        foreach (var entry in eligible)
        {
            var (e, pos, state, job, forced) = entry;
            var dirtySince = board.DirtySinceNear(pos);
            var wallsDirty = board.WallsDirtyAtTick > state.LastEvalTick;

            // Cheap skip: not forced, no nearby changes since last look, walls
            // untouched. Still bump LastEvalTick so next stagger window starts
            // fresh.
            if (!forced && dirtySince <= state.LastEvalTick && !wallsDirty)
            {
                state.LastEvalTick = tick;
                e.Remove<JobEvalState>();
                e.Add(state);
                continue;
            }

            JobTask? best = null;
            long bestDistSq = long.MaxValue;
            foreach (var t in board.TasksNear(pos))
            {
                // Only strictly-higher-priority tiers (numerically lower).
                if ((int)t.Tier >= (int)job.Tier) continue;

                if (best is null || (int)t.Tier < (int)best.Value.Tier)
                {
                    best = t;
                    bestDistSq = DistSq(pos, t.Target);
                    continue;
                }
                if ((int)t.Tier == (int)best.Value.Tier)
                {
                    var d = DistSq(pos, t.Target);
                    if (d < bestDistSq) { best = t; bestDistSq = d; }
                }
            }

            if (best is { } pick)
            {
                e.Remove<CurrentJob>();
                e.Add(new CurrentJob(pick.Id, pick.Tier, pick.Target));
            }

            if (forced) e.Remove<JobDirty>();
            state.LastEvalTick = tick;
            e.Remove<JobEvalState>();
            e.Add(state);
        }
    }

    private static long DistSq(TilePos a, TilePos b)
    {
        long dx = a.X - b.X;
        long dy = a.Y - b.Y;
        long dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz;
    }
}
