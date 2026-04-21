using fennecs;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Sim.Systems;

public static class ChunkTierSystem
{
    public static void Step(World world, TileWorld tiles)
    {
        var tiers = new Dictionary<TilePos, ChunkState>();

        world.Stream<ClaimedRegion>().For((ref ClaimedRegion r) =>
        {
            foreach (var key in ChunksTouchedByBounds(r.Min, r.Max))
                Bump(tiers, key, r.MinTier);
        });

        world.Stream<LiveAnchor>().For((ref LiveAnchor a) =>
        {
            var min = new TilePos(
                a.Center.X - a.RadiusTiles,
                a.Center.Y - a.RadiusTiles,
                a.Center.Z - a.RadiusTiles);
            var max = new TilePos(
                a.Center.X + a.RadiusTiles,
                a.Center.Y + a.RadiusTiles,
                a.Center.Z + a.RadiusTiles);
            foreach (var key in ChunksTouchedByBounds(min, max))
                Bump(tiers, key, ChunkState.Live);
        });

        var halo = new List<TilePos>();
        foreach (var kv in tiers)
        {
            if (kv.Value != ChunkState.Live) continue;
            for (var dx = -1; dx <= 1; dx++)
            for (var dy = -1; dy <= 1; dy++)
            for (var dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dy == 0 && dz == 0) continue;
                halo.Add(new TilePos(kv.Key.X + dx, kv.Key.Y + dy, kv.Key.Z + dz));
            }
        }
        foreach (var key in halo) Bump(tiers, key, ChunkState.Ambient);

        tiles.ReplaceChunkStates(tiers);

        var cells = new Dictionary<CellKey, ChunkState>();
        foreach (var kv in tiers)
        {
            var cellKey = Cell.FromChunk(kv.Key);
            if (!cells.TryGetValue(cellKey, out var cur) || kv.Value > cur)
                cells[cellKey] = kv.Value;
        }
        tiles.ReplaceCellStates(cells);
    }

    private static void Bump(Dictionary<TilePos, ChunkState> tiers, TilePos key, ChunkState state)
    {
        if (!tiers.TryGetValue(key, out var cur) || state > cur)
            tiers[key] = state;
    }

    public static IEnumerable<TilePos> ChunksTouchedByBounds(TilePos min, TilePos max)
    {
        const int s = Chunk.Size;
        var cxMin = FloorDiv(min.X, s); var cxMax = FloorDiv(max.X, s);
        var cyMin = FloorDiv(min.Y, s); var cyMax = FloorDiv(max.Y, s);
        var czMin = FloorDiv(min.Z, s); var czMax = FloorDiv(max.Z, s);
        for (var cx = cxMin; cx <= cxMax; cx++)
        for (var cy = cyMin; cy <= cyMax; cy++)
        for (var cz = czMin; cz <= czMax; cz++)
            yield return new TilePos(cx, cy, cz);
    }

    private static int FloorDiv(int a, int b) => (a / b) - (a % b < 0 ? 1 : 0);
}
