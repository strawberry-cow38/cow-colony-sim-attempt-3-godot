using System.Collections.Generic;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Jobs;

/// <summary>
/// Global task registry backing the colonist job evaluator.
///
/// Tasks live in an XZ spatial grid of <see cref="CellsPerSide"/>×<see cref="CellsPerSide"/>
/// cells over the settled area. A colonist re-evaluating its job queries its
/// own cell plus the 8 neighbours (9 cells total) instead of the full task
/// list, so scan cost is O(nearby) rather than O(all).
///
/// Each cell carries a <c>DirtyAtTick</c> stamp. <see cref="Sim.Systems.JobSystem"/>
/// compares it against the colonist's <see cref="JobEvalState.LastEvalTick"/> —
/// if nothing in the 9-cell window has changed since the last look the entire
/// scan is skipped.
/// </summary>
public sealed class JobBoard
{
    public const int DefaultCellsPerSide = 8;

    private readonly int _minX;
    private readonly int _minZ;
    private readonly int _cellTilesX;
    private readonly int _cellTilesZ;

    private readonly HashSet<int>[,] _cellTasks;
    private readonly long[,] _cellDirtyAtTick;
    private readonly Dictionary<int, JobTask> _tasks = new();
    private readonly Dictionary<int, (int cx, int cz)> _taskCell = new();
    private int _nextId = 1;

    public int CellsPerSide { get; }
    public long WallsDirtyAtTick { get; private set; }

    public IReadOnlyDictionary<int, JobTask> Tasks => _tasks;

    public JobBoard(TilePos min, TilePos max, int cellsPerSide = DefaultCellsPerSide)
    {
        if (cellsPerSide < 1) throw new ArgumentOutOfRangeException(nameof(cellsPerSide));
        if (max.X < min.X || max.Z < min.Z)
            throw new ArgumentException("max must be >= min on X and Z");

        CellsPerSide = cellsPerSide;
        _minX = min.X;
        _minZ = min.Z;
        var spanX = max.X - min.X + 1;
        var spanZ = max.Z - min.Z + 1;
        // Ceil-divide so the grid fully covers the bounds even when span isn't
        // divisible by cellsPerSide.
        _cellTilesX = (spanX + cellsPerSide - 1) / cellsPerSide;
        _cellTilesZ = (spanZ + cellsPerSide - 1) / cellsPerSide;

        _cellTasks = new HashSet<int>[cellsPerSide, cellsPerSide];
        _cellDirtyAtTick = new long[cellsPerSide, cellsPerSide];
        for (var x = 0; x < cellsPerSide; x++)
            for (var z = 0; z < cellsPerSide; z++)
                _cellTasks[x, z] = new HashSet<int>();
    }

    public int Add(JobTier tier, TilePos target, long tick)
    {
        var id = _nextId++;
        var (cx, cz) = CellOf(target);
        _tasks[id] = new JobTask(id, tier, target);
        _taskCell[id] = (cx, cz);
        _cellTasks[cx, cz].Add(id);
        _cellDirtyAtTick[cx, cz] = tick;
        return id;
    }

    public bool Remove(int id, long tick)
    {
        if (!_taskCell.TryGetValue(id, out var cell)) return false;
        _cellTasks[cell.cx, cell.cz].Remove(id);
        _cellDirtyAtTick[cell.cx, cell.cz] = tick;
        _tasks.Remove(id);
        _taskCell.Remove(id);
        return true;
    }

    public void SetWallsDirty(long tick) => WallsDirtyAtTick = tick;

    /// <summary>Most recent dirty tick across the 9 cells centred on
    /// <paramref name="pos"/>. 0 if no changes have landed there yet.</summary>
    public long DirtySinceNear(TilePos pos)
    {
        var (cx, cz) = CellOf(pos);
        long max = 0;
        for (var dx = -1; dx <= 1; dx++)
        for (var dz = -1; dz <= 1; dz++)
        {
            var nx = cx + dx;
            var nz = cz + dz;
            if (nx < 0 || nx >= CellsPerSide || nz < 0 || nz >= CellsPerSide) continue;
            var t = _cellDirtyAtTick[nx, nz];
            if (t > max) max = t;
        }
        return max;
    }

    /// <summary>Enumerate every task in the 9-cell window around <paramref name="pos"/>.</summary>
    public IEnumerable<JobTask> TasksNear(TilePos pos)
    {
        var (cx, cz) = CellOf(pos);
        for (var dx = -1; dx <= 1; dx++)
        for (var dz = -1; dz <= 1; dz++)
        {
            var nx = cx + dx;
            var nz = cz + dz;
            if (nx < 0 || nx >= CellsPerSide || nz < 0 || nz >= CellsPerSide) continue;
            foreach (var id in _cellTasks[nx, nz]) yield return _tasks[id];
        }
    }

    internal (int cx, int cz) CellOf(TilePos pos)
    {
        var cx = (pos.X - _minX) / _cellTilesX;
        var cz = (pos.Z - _minZ) / _cellTilesZ;
        // Clamp OOB — lets out-of-bounds actors query the edge cell rather
        // than throw. In practice sim logic keeps tasks and colonists inside.
        if (cx < 0) cx = 0; else if (cx >= CellsPerSide) cx = CellsPerSide - 1;
        if (cz < 0) cz = 0; else if (cz >= CellsPerSide) cz = CellsPerSide - 1;
        return (cx, cz);
    }
}
