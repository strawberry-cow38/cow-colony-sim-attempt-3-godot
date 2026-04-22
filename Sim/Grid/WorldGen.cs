using System.Threading.Tasks;

namespace CowColonySim.Sim.Grid;

public static class WorldGen
{
    public const int DefaultMinHeight = -10;
    public const int DefaultMaxHeight = 103;
    public const float DefaultFrequency = 0.02f;

    // Sea level. Columns below this fill with Water tiles up to (WaterLevelY-1).
    public const int WaterLevelY = 0;

    // Mountain region onset. MountainMask noise above 0.55 begins ramping into
    // ridge-shaped peaks; above 0.80 is fully mountainous. Smoothstep-blended.
    // Raised from 0.45/0.70 to reduce overall mountain coverage per request.
    private const float MountainOnset = 0.55f;
    private const float MountainFull  = 0.80f;

    // Lake carve. Mask above onset pulls height below sea level; only fires
    // when base elevation is already low AND mountain mask is low so cliff
    // feet stay dry (no lakes touching mountain walls). Wide smoothstep
    // band → long gradual bank slopes instead of abrupt depth jumps.
    // Thresholds raised 2026-04-22 to make lakes rarer — rivers carry most
    // of the surface water now.
    private const float LakeOnset = 0.70f;
    private const float LakeFull  = 0.95f;
    private const float LakeMaxBaseH = 6f;
    private const float LakeMountainBuffer = 0.25f;

    // River pass. Sources sampled on a coarse grid; each walks *strict*
    // downhill with a meander bias among qualifying (nh < curH) neighbors
    // so rivers never flow uphill. Flow count per cell accumulates across
    // walks — tributaries that merge onto the same spine fatten naturally.
    // Walks that step onto a lake cell or reach sea level terminate.
    // MinH raised to 15 so sources sit in highlands; MaxH 60 to allow
    // mountain-adjacent springs without dropping into snow-capped peaks.
    private const int   RiverSampleStride      = 14;
    private const float RiverSourceThreshold   = 0.55f;
    private const int   RiverSourceMinH        = 15;
    private const int   RiverSourceMaxH        = 60;
    private const int   RiverMaxWalkSteps      = 8192;
    private const float RiverMeanderWeight     = 1.6f;

    // Detail noise amplitude. Low enough that plains and hills read as
    // gently rolling rather than jagged.
    private const float DetailAmplitude = 1.3f;

    // Coastal border. Every map is ringed by ocean — tiles within
    // CoastRadiusTiles of the map edge blend from normal terrain toward a
    // submerged floor so a natural shoreline ringed with sand shores forms.
    // The "infinite" far ocean is a flat quad in scene.cs; this fade handles
    // the in-map transition from land to coast.
    private const int   CoastRadiusTiles  = 48;
    private const float CoastFloor        = -5f;

    public static int Generate(TileWorld tiles, int seed, int sizeX, int sizeZ,
        int minHeight = DefaultMinHeight, int maxHeight = DefaultMaxHeight,
        float frequency = DefaultFrequency)
    {
        _ = frequency;
        var noise = new NoiseStack(seed);
        var halfX = sizeX / 2;
        var halfZ = sizeZ / 2;

        // Scale coast radius down on small maps so the whole grid doesn't get
        // submerged. On a 32x32 test map, a 48-tile rim would flood everything;
        // clamp so at least half the map stays dry by default.
        var coastRadius = Math.Min(CoastRadiusTiles, Math.Min(sizeX, sizeZ) / 4);

        var heights = new int[sizeX, sizeZ];
        Parallel.For(0, sizeX, xi =>
        {
            for (var zi = 0; zi < sizeZ; zi++)
            {
                var x = xi - halfX;
                var z = zi - halfZ;

                // Continent: slow, gentle base elevation — rolling pasture.
                // Baseline shifted up so interior stays dry by default;
                // coast fade below pulls the outer rim under water to form
                // a shoreline.
                var continent = noise.Continent.GetNoise(x, z);      // -1..1
                var baseH = 6f + continent * 4f;                      // +2..+10

                // Mountain lift. Smooth mask picks out mountainous regions.
                // Ridged FBm within the mask produces dramatic peak-and-valley
                // shapes; quantization below converts the resulting gradient
                // into clean terraced plateaus with continuous cliff edges.
                var maskRaw = (noise.MountainMask.GetNoise(x, z) + 1f) * 0.5f;
                var mountainWeight = Smoothstep(MountainOnset, MountainFull, maskRaw);
                if (mountainWeight > 0f)
                {
                    var ridge = (noise.Ridge.GetNoise(x, z) + 1f) * 0.5f;
                    var mountainH = 12f + ridge * 90f;                // 12..102
                    baseH = Lerp(baseH, mountainH, mountainWeight);
                }

                // Mountains stay smooth — no plateau quantization. Cliffs
                // only emerge along lake shorelines (where the lake carve
                // drops a tile below sea level while its neighbor stays on
                // land) via the Cap rule below. Ridge noise supplies all the
                // peak/valley drama directly.

                // Coastal fade: cells near the map edge blend toward a
                // submerged floor so every map has a natural shore. Uses
                // Chebyshev distance so the entire rim is submerged evenly
                // instead of only the four cardinal edges.
                var edgeDist = Math.Min(Math.Min(xi, sizeX - 1 - xi),
                                        Math.Min(zi, sizeZ - 1 - zi));
                if (coastRadius > 0 && edgeDist < coastRadius)
                {
                    var coastT = 1f - (edgeDist / (float)coastRadius);
                    var coastBlend = Smoothstep(0f, 1f, coastT);
                    baseH = Lerp(baseH, CoastFloor, coastBlend);
                }

                // Detail pass. Capped < CliffDelta so flat plains never spike.
                // Muted on mountain plateaus (scaled by 1 - mountainWeight) so
                // quantized tiers stay dead flat — no fuzz on cliff tops.
                var detail = noise.Detail.GetNoise(x, z)
                             * DetailAmplitude
                             * (1f - mountainWeight);

                // Lake carve. Mask-weighted pull toward a negative floor only
                // when base is low enough to sit near sea level AND mountain
                // influence is weak — keeps lakes away from cliff feet.
                if (baseH < LakeMaxBaseH && maskRaw < LakeMountainBuffer)
                {
                    var lakeMask = (noise.Lake.GetNoise(x, z) + 1f) * 0.5f;
                    var lakeWeight = Smoothstep(LakeOnset, LakeFull, lakeMask);
                    if (lakeWeight > 0f)
                    {
                        var lakeFloor = -4f - lakeMask * 4f;          // -4..-8
                        baseH = Lerp(baseH, lakeFloor, lakeWeight);
                    }
                }

                int hi = (int)MathF.Round(baseH + detail);
                if (hi < minHeight) hi = minHeight;
                if (hi > maxHeight) hi = maxHeight;
                heights[xi, zi] = hi;
            }
        });

        // Snapshot lake mask BEFORE river carve so rivers can never paint over
        // a lake cell even if they would have routed through one.
        var isLake = new bool[sizeX, sizeZ];
        for (var xi = 0; xi < sizeX; xi++)
        for (var zi = 0; zi < sizeZ; zi++)
            isLake[xi, zi] = heights[xi, zi] < WaterLevelY;

        var riverTop = CarveRivers(heights, isLake, noise, sizeX, sizeZ, halfX, halfZ, minHeight);

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

            // River cells have a locally elevated water surface carved by
            // CarveRivers. rTop > 0 means water fills from `height` up to
            // `rTop - 1`; the mesher renders the surface plane at rTop.
            var rTop = riverTop[xi, zi];
            var isRiver = rTop > 0;

            // Shore detection: a land column within 2 tiles of sea level with
            // any submerged 8-neighbor is painted sand instead of grass.
            // Lake-bed columns (height < WaterLevelY) are always sand — the
            // water plane renders separately on top.
            var isLakeBed = height < WaterLevelY;
            var isShore = false;
            if (!isLakeBed && !isRiver && height <= WaterLevelY + 2)
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

            var floorTile = (isLakeBed || isShore || isRiver) ? sand : grass;
            var rockBase = Math.Min(0, height - 3);
            for (var y = rockBase; y < height - 1; y++)
            {
                tiles.Set(new TilePos(x, y, z), solid);
            }
            tiles.Set(new TilePos(x, height - 1, z), floorTile);
            // Water fill: up to local river top if river, otherwise up to
            // global sea level. For dry land both loops are empty.
            var localWaterTop = isRiver ? rTop : WaterLevelY;
            for (var y = height; y < localWaterTop; y++)
            {
                tiles.Set(new TilePos(x, y, z), water);
            }

            tiles.SetTerrainHeight(x, z, (short)height);
            // Water-kind columns (lake, ocean, river) tag Water so the mesher
            // emits a water plane. WaterTop carries the per-column surface Y
            // so rivers at elevation render at their bed+1 rather than sea.
            var surfaceKind = (isLakeBed || isRiver)
                ? TileKind.Water
                : (isShore ? TileKind.Sand : TileKind.Floor);
            tiles.SetTerrainKind(x, z, surfaceKind);
            if (isRiver)
                tiles.SetWaterTop(x, z, (short)rTop);

            surfaceTiles++;
        }

        // Per-tile corner derivation. SW = own column. SE / NE / NW sample the
        // east / NE-diagonal / north neighbors but clamp back to own when the
        // gap exceeds CliffDelta — producing the discontinuity the mesher
        // auto-walls. Tier quantization above guarantees most within-tier
        // neighbors land at identical heights so cliff boundaries are crisp.
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

    // v2 rivers. Sources seed on a coarse grid inside a highland band; each
    // walks *strict*-downhill (no uphill, no flat) with a meander bias among
    // qualifying neighbors. Flow accumulates per cell so tributaries fatten
    // merges downstream. Returns a per-cell waterTop array — 0 = not a
    // river, >0 = the river's water-surface Y in tile units. The caller
    // writes per-column water-top into TileWorld so the mesher emits local
    // water planes instead of one global sea-level quad.
    //
    // Rivers never flow uphill — if no descending neighbor exists, the walk
    // terminates at that cell. They also terminate at lake cells or at any
    // cell already at / below sea level (ocean / estuary mouth).
    private static int[,] CarveRivers(
        int[,] heights, bool[,] isLake, NoiseStack noise,
        int sizeX, int sizeZ, int halfX, int halfZ, int minHeight)
    {
        var flow = new int[sizeX, sizeZ];
        var riverTop = new int[sizeX, sizeZ];
        var visited = new int[sizeX, sizeZ];
        var gen = 0;
        int[] dxs = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] dzs = { -1, -1, -1, 0, 0, 1, 1, 1 };

        for (var sxi = RiverSampleStride / 2; sxi < sizeX; sxi += RiverSampleStride)
        for (var szi = RiverSampleStride / 2; szi < sizeZ; szi += RiverSampleStride)
        {
            var bestXi = -1; var bestZi = -1; var bestScore = float.NegativeInfinity;
            var lo = -RiverSampleStride / 2;
            var hi = RiverSampleStride / 2;
            for (var dx = lo; dx < hi; dx++)
            for (var dz = lo; dz < hi; dz++)
            {
                var xi = sxi + dx; var zi = szi + dz;
                if ((uint)xi >= (uint)sizeX || (uint)zi >= (uint)sizeZ) continue;
                if (isLake[xi, zi]) continue;
                var h = heights[xi, zi];
                if (h < RiverSourceMinH || h > RiverSourceMaxH) continue;
                var x = xi - halfX; var z = zi - halfZ;
                var r = (noise.RiverSource.GetNoise(x, z) + 1f) * 0.5f;
                if (r < RiverSourceThreshold) continue;
                var score = r + h * 0.001f;
                if (score > bestScore) { bestScore = score; bestXi = xi; bestZi = zi; }
            }
            if (bestXi < 0) continue;

            gen++;
            var cxi = bestXi; var czi = bestZi;
            for (var step = 0; step < RiverMaxWalkSteps; step++)
            {
                if (visited[cxi, czi] == gen) break;
                visited[cxi, czi] = gen;
                flow[cxi, czi]++;

                // Terminate on lake, ocean (sub-sea) or already visited.
                if (isLake[cxi, czi]) break;
                if (heights[cxi, czi] <= WaterLevelY) break;

                var wx = cxi - halfX; var wz = czi - halfZ;
                var mAng = (noise.Meander.GetNoise(wx, wz) + 1f) * MathF.PI;
                var pdx = MathF.Cos(mAng);
                var pdz = MathF.Sin(mAng);

                var curH = heights[cxi, czi];
                var bestK = -1; var bestKScore = float.PositiveInfinity;
                for (var k = 0; k < 8; k++)
                {
                    var nxi = cxi + dxs[k]; var nzi = czi + dzs[k];
                    if ((uint)nxi >= (uint)sizeX || (uint)nzi >= (uint)sizeZ) continue;
                    if (visited[nxi, nzi] == gen) continue;
                    var nh = heights[nxi, nzi];
                    // Strict: neighbor must be lower than current cell.
                    // Equal-height or uphill steps are forbidden.
                    if (nh >= curH) continue;

                    var invLen = 1f / MathF.Sqrt(dxs[k] * dxs[k] + dzs[k] * dzs[k]);
                    var align = (dxs[k] * pdx + dzs[k] * pdz) * invLen;
                    // Lower nh is better; alignment with meander is a small
                    // tiebreaker that picks between equally-low neighbors.
                    var score = nh - align * RiverMeanderWeight;
                    if (score < bestKScore) { bestKScore = score; bestK = k; }
                }
                if (bestK < 0) break;  // local pit — river ends here
                cxi += dxs[bestK];
                czi += dzs[bestK];
            }
        }

        // Carve pass. For each cell with accumulated flow, record the river
        // surface = pre-carve terrain height and drop the bed by one tile so
        // a water tile forms. The mesher emits the water plane at the
        // recorded surface; banks remain at surrounding terrain, giving a
        // waterline that sits just below grass. Cross-cell steps in surface
        // Y become natural waterfalls via the cliff Cap rule.
        for (var xi = 0; xi < sizeX; xi++)
        for (var zi = 0; zi < sizeZ; zi++)
        {
            var f = flow[xi, zi];
            if (f <= 0) continue;
            if (isLake[xi, zi]) continue;
            if (heights[xi, zi] <= WaterLevelY) continue;

            var surface = heights[xi, zi];
            var bedH = surface - 1;
            if (bedH < minHeight) bedH = minHeight;
            heights[xi, zi] = bedH;
            riverTop[xi, zi] = surface;
        }
        return riverTop;
    }

    // Shore- + waterfall-aware clamp. Default: use the neighbor's height so
    // land-land and lake-lake boundaries share a corner and produce smooth
    // slopes. Clamp to own height in two cases:
    //   1) The boundary crosses sea level (shore ↔ lakebed). Keeps a crisp
    //      waterline between dry land and submerged basin.
    //   2) The gap is ≥ <see cref="SimConstants.CliffDelta"/> tiles. Lets
    //      river waterfalls emerge as vertical walls when a river bed drops
    //      multiple tiles between adjacent cells, without relying on
    //      plateau quantization.
    // Ridge-noise mountains don't produce 3+ tile gaps cell-to-cell at the
    // current frequency/gain, so (2) rarely fires outside river cliffs.
    private static int Cap(int candidate, int own)
    {
        var ownDry = own >= WaterLevelY;
        var candDry = candidate >= WaterLevelY;
        if (ownDry != candDry) return own;
        if (Math.Abs(candidate - own) >= SimConstants.CliffDelta) return own;
        return candidate;
    }

    public static int SurfaceY(TileWorld tiles, int x, int z, int maxProbe = 128, int minProbe = -16)
    {
        for (var y = maxProbe; y >= minProbe; y--)
        {
            if (!tiles.Get(new TilePos(x, y, z)).IsEmpty) return y + 1;
        }
        return minProbe;
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
