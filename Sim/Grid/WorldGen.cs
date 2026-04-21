using System.Threading.Tasks;

namespace CowColonySim.Sim.Grid;

public static class WorldGen
{
    public const int DefaultMinHeight = -10;
    public const int DefaultMaxHeight = 103;
    public const float DefaultFrequency = 0.02f;

    // Mesa: rare, low-frequency mask that clamps height to a flat top only
    // where the mask exceeds threshold. Scattered buildable plateaus on top
    // of otherwise jagged mountains — no global staircase terracing, so
    // default mountains keep the imposing ridged-noise shape.
    private const float MesaThreshold = 0.72f;
    private const float MesaBlendBand = 0.10f;
    private const float MesaMinHeight = 35f;
    private const float MesaStepTiles = 15f;

    // Cubby: shallow flat pocket anywhere on the map. Anywhere the cubby
    // noise exceeds the threshold the local surface is forced to a flat
    // depressed level, carving hideable outpost nooks into hills/mountains.
    private const float CubbyThreshold = 0.55f;
    private const float CubbyBlendBand = 0.15f;
    private const float CubbyDepthTiles = 6f;

    // Sea level. Lake cells sample heights below 0 so they dig real basins
    // into the world; every other feature stays positive. Water tiles fill
    // every column whose surface y < WaterLevelY up to (WaterLevelY - 1).
    // No bilerp gate needed — plains/hills/mountains never cross zero.
    public const int WaterLevelY = 0;

    public static int Generate(TileWorld tiles, int seed, int sizeX, int sizeZ,
        int minHeight = DefaultMinHeight, int maxHeight = DefaultMaxHeight,
        float frequency = DefaultFrequency)
    {
        var noises = new FeatureNoises(seed);
        var halfX = sizeX / 2;
        var halfZ = sizeZ / 2;
        const int cellSize = SimConstants.CellSizeTiles;
        const int cellHalf = cellSize / 2;

        var heights = new int[sizeX, sizeZ];
        Parallel.For(0, sizeX, xi =>
        {
            for (var zi = 0; zi < sizeZ; zi++)
            {
                var x = xi - halfX;
                var z = zi - halfZ;

                // 4-cell bilerp anchored on cell centers. Guarantees C0
                // continuity across cell borders because weights sum to 1
                // and each corner's feature height is a continuous global
                // noise sampled at (x, z).
                float cxf = (x - (float)cellHalf) / cellSize;
                float czf = (z - (float)cellHalf) / cellSize;
                int cx0 = (int)MathF.Floor(cxf);
                int cz0 = (int)MathF.Floor(czf);
                float fx = cxf - cx0;
                float fz = czf - cz0;
                float wx = Smooth(fx);
                float wz = Smooth(fz);

                var f00 = FeatureResolver.Pick(seed, new CellKey(cx0, cz0));
                var f10 = FeatureResolver.Pick(seed, new CellKey(cx0 + 1, cz0));
                var f01 = FeatureResolver.Pick(seed, new CellKey(cx0, cz0 + 1));
                var f11 = FeatureResolver.Pick(seed, new CellKey(cx0 + 1, cz0 + 1));

                float h00 = noises.SampleHeight(f00, x, z);
                float h10 = noises.SampleHeight(f10, x, z);
                float h01 = noises.SampleHeight(f01, x, z);
                float h11 = noises.SampleHeight(f11, x, z);

                float h = (1f - wx) * (1f - wz) * h00
                        + wx         * (1f - wz) * h10
                        + (1f - wx) * wz         * h01
                        + wx         * wz         * h11;

                // Mesa pass. Rare low-freq mask — only flat-tops scattered
                // peaks. Leaves the rest of a mountain cell as natural
                // jagged ridged noise instead of the old global staircase.
                var mesaN = (noises.Mesa.GetNoise(x, z) + 1f) * 0.5f;
                if (mesaN > MesaThreshold && h > MesaMinHeight)
                {
                    var over = h - MesaMinHeight;
                    var band = MathF.Floor(over / MesaStepTiles) * MesaStepTiles;
                    var mesaTop = MesaMinHeight + band;
                    var t = Smooth(MathF.Min(1f, (mesaN - MesaThreshold) / MesaBlendBand));
                    h = h + (mesaTop - h) * t;
                }

                // Cubby pass. noise in [-1, 1]; above threshold force a flat
                // depressed plateau. Blend band keeps edges walkable instead
                // of a sharp wall.
                var cubbyN = (noises.Cubby.GetNoise(x, z) + 1f) * 0.5f;
                if (cubbyN > CubbyThreshold)
                {
                    var floor = MathF.Max(minHeight + 1f, h - CubbyDepthTiles);
                    var t = Smooth(MathF.Min(1f, (cubbyN - CubbyThreshold) / CubbyBlendBand));
                    h = h + (floor - h) * t;
                }

                int hi = (int)MathF.Round(h);
                if (hi < minHeight) hi = minHeight;
                if (hi > maxHeight) hi = maxHeight;
                heights[xi, zi] = hi;
            }
        });

        var solid = new Tile(TileKind.Solid);
        var grass = new Tile(TileKind.Floor);
        var water = new Tile(TileKind.Water);
        var sand  = new Tile(TileKind.Sand);
        var surfaceTiles = 0;
        for (var xi = 0; xi < sizeX; xi++)
        for (var zi = 0; zi < sizeZ; zi++)
        {
            var height = heights[xi, zi];
            var x = xi - halfX;
            var z = zi - halfZ;

            // Shore detection: a land column is "shore" if its surface is
            // within 2 tiles of sea level AND any 8-neighbor column is a
            // submerged lake column. Paints a sandy band around every lake
            // instead of grass running right to the waterline.
            var isShore = false;
            if (height >= WaterLevelY && height <= WaterLevelY + 2)
            {
                for (var dz = -1; dz <= 1 && !isShore; dz++)
                for (var dx = -1; dx <= 1 && !isShore; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var nxi = xi + dx;
                    var nzi = zi + dz;
                    if ((uint)nxi >= (uint)sizeX || (uint)nzi >= (uint)sizeZ) continue;
                    if (heights[nxi, nzi] < WaterLevelY) isShore = true;
                }
            }

            // Dual write during vertex-terrain rollout. Voxel column still
            // feeds the current mesher / pathfinder; the corner-heightmap is
            // what P1's mesher + P2's slope A* will read. Once both readers
            // move, the voxel column writes go away.
            var rockBase = Math.Min(0, height - 3);
            for (var y = rockBase; y < height - 1; y++)
            {
                tiles.Set(new TilePos(x, y, z), solid);
            }
            tiles.Set(new TilePos(x, height - 1, z), isShore ? sand : grass);
            for (var y = height; y < WaterLevelY; y++)
            {
                tiles.Set(new TilePos(x, y, z), water);
            }

            // Column height (walkable floor Y) + surface kind. Pathfinding
            // reads column height; render corners are derived below.
            tiles.SetTerrainHeight(x, z, (short)height);
            var surfaceKind = height < WaterLevelY
                ? TileKind.Water
                : (isShore ? TileKind.Sand : TileKind.Floor);
            tiles.SetTerrainKind(x, z, surfaceKind);

            surfaceTiles++;
        }

        // Per-tile corner derivation. SW corner is always the tile's own
        // column. SE/NE/NW sample the east / NE-diagonal / north neighbor
        // columns, but clamp to own if the gap exceeds CliffDelta. Neighbor-
        // matching corners blend smoothly wherever terrain is gradual; big
        // gaps force the corner flat at own Y, creating a discontinuity the
        // mesher auto-walls.
        for (var xi = 0; xi < sizeX; xi++)
        for (var zi = 0; zi < sizeZ; zi++)
        {
            var x = xi - halfX;
            var z = zi - halfZ;
            var own = heights[xi, zi];
            var east  = (xi + 1 < sizeX) ? heights[xi + 1, zi]     : own;
            var ne    = (xi + 1 < sizeX && zi + 1 < sizeZ) ? heights[xi + 1, zi + 1] : own;
            var north = (zi + 1 < sizeZ) ? heights[xi,     zi + 1] : own;
            tiles.SetTileCorners(x, z,
                sw: (short)own,
                se: (short)Cap(east,  own),
                ne: (short)Cap(ne,    own),
                nw: (short)Cap(north, own));
        }
        return surfaceTiles;
    }

    private static int Cap(int candidate, int own)
        => Math.Abs(candidate - own) > SimConstants.CliffDelta ? own : candidate;

    public static int SurfaceY(TileWorld tiles, int x, int z, int maxProbe = 128, int minProbe = -16)
    {
        for (var y = maxProbe; y >= minProbe; y--)
        {
            if (!tiles.Get(new TilePos(x, y, z)).IsEmpty) return y + 1;
        }
        return minProbe;
    }

    private static float Smooth(float t) => t * t * (3f - 2f * t);
}
