namespace CowColonySim.Sim.Grid;

/// <summary>
/// One river from coast to coast: the ordered list of tile cells it occupies,
/// plus a flow direction. Flow is a unit (dx, dz) pointing downstream — the
/// direction water runs along the path. Unused by sim currently; stored so
/// future systems (water wheels, fish migration) can consult it.
/// </summary>
public sealed class RiverPath
{
    public readonly IReadOnlyList<(int X, int Z)> Cells;
    public readonly int FlowDx;
    public readonly int FlowDz;

    public RiverPath(IReadOnlyList<(int X, int Z)> cells, int flowDx, int flowDz)
    {
        Cells = cells;
        FlowDx = flowDx;
        FlowDz = flowDz;
    }
}
