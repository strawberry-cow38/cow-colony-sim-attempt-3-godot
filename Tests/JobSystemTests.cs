using fennecs;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Jobs;
using CowColonySim.Sim.Systems;
using Xunit;

namespace CowColonySim.Tests;

public class JobSystemTests
{
    private static JobBoard MakeBoard()
    {
        return new JobBoard(new TilePos(0, 0, 0), new TilePos(79, 0, 79), cellsPerSide: 8);
    }

    private static Entity SpawnColonist(World world, TilePos at, byte bucket, JobTier tier = JobTier.Idle)
    {
        var e = world.Spawn();
        e.Add(new Colonist());
        e.Add(new Position(at.X + 0.5f, at.Y, at.Z + 0.5f));
        e.Add(tier == JobTier.Idle ? CurrentJob.None : new CurrentJob(0, tier, default));
        e.Add(JobEvalState.Fresh(bucket));
        return e;
    }

    private static CurrentJob JobOf(World world, Entity e)
    {
        CurrentJob result = default;
        world.Stream<CurrentJob>().For((in Entity ent, ref CurrentJob j) =>
        {
            if (ent == e) result = j;
        });
        return result;
    }

    private static JobEvalState StateOf(World world, Entity e)
    {
        JobEvalState result = default;
        world.Stream<JobEvalState>().For((in Entity ent, ref JobEvalState s) =>
        {
            if (ent == e) result = s;
        });
        return result;
    }

    [Fact]
    public void JobDirty_Forces_Eval_Outside_Stagger_Bucket()
    {
        var world = new World();
        var board = MakeBoard();
        var cow = SpawnColonist(world, new TilePos(10, 0, 10), bucket: 5);
        cow.Add(new JobDirty());
        board.Add(JobTier.Auto, new TilePos(11, 0, 11), tick: 0);

        // tick 1, bucket=5 — bucket turn is tick%20==5, so tick 1 is NOT the
        // bucket's turn. Forced eval should still happen because of JobDirty.
        JobSystem.Step(world, board, tick: 1);

        Assert.False(cow.Has<JobDirty>());
        var job = JobOf(world, cow);
        Assert.Equal(JobTier.Auto, job.Tier);
    }

    [Fact]
    public void Non_Bucket_Turn_Without_JobDirty_Is_Skipped()
    {
        var world = new World();
        var board = MakeBoard();
        var cow = SpawnColonist(world, new TilePos(10, 0, 10), bucket: 5);
        board.Add(JobTier.Emergency, new TilePos(11, 0, 11), tick: 0);

        JobSystem.Step(world, board, tick: 1);

        Assert.Equal(CurrentJob.None, JobOf(world, cow));
        Assert.Equal(-1, StateOf(world, cow).LastEvalTick);
    }

    [Fact]
    public void Bucket_Turn_With_Dirty_Window_Assigns_Highest_Tier_Task()
    {
        var world = new World();
        var board = MakeBoard();
        var cow = SpawnColonist(world, new TilePos(10, 0, 10), bucket: 0);
        board.Add(JobTier.Auto, new TilePos(12, 0, 12), tick: 0);
        board.Add(JobTier.Emergency, new TilePos(15, 0, 15), tick: 0);

        // tick 0, bucket=0 → eval
        JobSystem.Step(world, board, tick: 0);

        var job = JobOf(world, cow);
        Assert.Equal(JobTier.Emergency, job.Tier);
        Assert.Equal(new TilePos(15, 0, 15), job.Target);
    }

    [Fact]
    public void Ties_On_Tier_Break_By_Closest_Target()
    {
        var world = new World();
        var board = MakeBoard();
        var cow = SpawnColonist(world, new TilePos(10, 0, 10), bucket: 0);
        board.Add(JobTier.Auto, new TilePos(15, 0, 15), tick: 0);
        board.Add(JobTier.Auto, new TilePos(11, 0, 11), tick: 0);

        JobSystem.Step(world, board, tick: 0);

        var job = JobOf(world, cow);
        Assert.Equal(new TilePos(11, 0, 11), job.Target);
    }

    [Fact]
    public void Tasks_At_Or_Below_Current_Tier_Are_Ignored()
    {
        var world = new World();
        var board = MakeBoard();
        // Colonist already on an Urgent job → only Emergency (tier 0) should
        // interrupt; Assigned/Auto/Idle are strictly equal or below.
        var cow = SpawnColonist(world, new TilePos(10, 0, 10), bucket: 0, tier: JobTier.Urgent);
        board.Add(JobTier.Assigned, new TilePos(11, 0, 11), tick: 0);
        board.Add(JobTier.Auto, new TilePos(12, 0, 12), tick: 0);

        JobSystem.Step(world, board, tick: 0);

        var job = JobOf(world, cow);
        Assert.Equal(JobTier.Urgent, job.Tier);
        Assert.Equal(0, job.JobId);
    }

    [Fact]
    public void Emergency_Task_Preempts_Auto_Job()
    {
        var world = new World();
        var board = MakeBoard();
        var cow = SpawnColonist(world, new TilePos(10, 0, 10), bucket: 0, tier: JobTier.Auto);
        board.Add(JobTier.Emergency, new TilePos(11, 0, 11), tick: 0);

        JobSystem.Step(world, board, tick: 0);

        Assert.Equal(JobTier.Emergency, JobOf(world, cow).Tier);
    }

    [Fact]
    public void Clean_Window_Since_Last_Eval_Skips_Scan()
    {
        var world = new World();
        var board = MakeBoard();
        var cow = SpawnColonist(world, new TilePos(10, 0, 10), bucket: 0);
        board.Add(JobTier.Auto, new TilePos(11, 0, 11), tick: 0);

        // First pass picks up the task (tier 0, bucket 0).
        JobSystem.Step(world, board, tick: 0);
        Assert.Equal(JobTier.Auto, JobOf(world, cow).Tier);

        // Forcibly downgrade the colonist so the next pass WOULD scan if it
        // ran — but no new dirty ticks landed on the board, so it shouldn't.
        cow.Remove<CurrentJob>();
        cow.Add(CurrentJob.None);

        // 20 ticks later (bucket 0's next turn).
        JobSystem.Step(world, board, tick: 20);

        // Board hasn't been dirtied since LastEvalTick=0 — scan was skipped,
        // job stays None.
        Assert.Equal(CurrentJob.None, JobOf(world, cow));
        Assert.Equal(20, StateOf(world, cow).LastEvalTick);
    }

    [Fact]
    public void Walls_Dirty_Triggers_Rescan_Even_On_Clean_Cells()
    {
        var world = new World();
        var board = MakeBoard();
        var cow = SpawnColonist(world, new TilePos(10, 0, 10), bucket: 0);
        board.Add(JobTier.Auto, new TilePos(11, 0, 11), tick: 0);

        JobSystem.Step(world, board, tick: 0);
        cow.Remove<CurrentJob>();
        cow.Add(CurrentJob.None);

        // Walls change → pathing is invalidated — force a rescan.
        board.SetWallsDirty(tick: 15);
        JobSystem.Step(world, board, tick: 20);

        Assert.Equal(JobTier.Auto, JobOf(world, cow).Tier);
    }

    [Fact]
    public void Stagger_Distributes_Evals_Across_StaggerPeriod()
    {
        var world = new World();
        var board = MakeBoard();
        // Spawn one colonist per bucket 0..N-1 and count how many evaluate
        // per tick over a full period.
        var cows = new List<Entity>();
        for (byte b = 0; b < JobSystem.StaggerPeriod; b++)
            cows.Add(SpawnColonist(world, new TilePos(10, 0, 10), bucket: b));
        // Dirty the window so every scheduled colonist actually enters the
        // scan branch (rather than skipping due to clean window).
        board.Add(JobTier.Emergency, new TilePos(10, 0, 10), tick: 0);

        var evalCountByTick = new int[JobSystem.StaggerPeriod];
        for (var t = 0; t < JobSystem.StaggerPeriod; t++)
        {
            var before = new Dictionary<Entity, long>();
            foreach (var c in cows) before[c] = StateOf(world, c).LastEvalTick;

            JobSystem.Step(world, board, tick: t);

            foreach (var c in cows)
                if (StateOf(world, c).LastEvalTick != before[c]) evalCountByTick[t]++;
        }

        // Exactly one colonist per tick.
        Assert.All(evalCountByTick, n => Assert.Equal(1, n));
    }
}
