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

    // Rivers. A fixed small count of rivers run coast-to-coast across the
    // map. Each picks a random start on one coast side, a random end on a
    // different side, then winds toward the end with a meander bias. The
    // path is forced to make progress toward the goal each step so it
    // always reaches the far coast — no half-rivers, no merges, no tribs.
    private const int   RiverCount             = 3;
    // Bed geometry. Fixed width 5 tiles (RiverBedRadius = 2 → 5-diameter
    // disc). Fixed depth below sea level so water surface is flat.
    private const int   RiverBedRadius         = 2;
    private const int   RiverBedDepth          = 2;
    // Meander bias weight vs goal-progress weight. Higher = curlier path.
    // Kept below progress weight so the walk is guaranteed to converge.
    private const float RiverMeanderWeight     = 0.75f;
    // Max walk length fudge factor. Path must fit in manhattan(start,end)
    // steps plus this slack for chebyshev/diagonal moves and meander.
    private const int   RiverWalkSlack         = 64;

    // Bank-lowering pass. Iterative single-step dilation: every cell must
    // sit at most one tile above its lowest 8-neighbor. Sub-sea cells
    // (river beds, lake beds, coast-fade rim) act as implicit seeds — the
    // dilation pulls inland heights down along a pyramid of slope 1 tile
    // per tile until the ramp reaches ambient terrain and the sweep
    // terminates. Guarantees no cliff-jump at bank edges.

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

        CarveRivers(heights, noise, seed, sizeX, sizeZ, halfX, halfZ, minHeight);
        LowerBanks(heights, sizeX, sizeZ);

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

            // Shore detection: a land column within 2 tiles of sea level with
            // any submerged 8-neighbor is painted sand instead of grass.
            // Lake-bed columns (height < WaterLevelY) are always sand — the
            // water plane renders separately on top.
            var isLakeBed = height < WaterLevelY;
            var isShore = false;
            if (!isLakeBed && height <= WaterLevelY + 2)
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

            var floorTile = (isLakeBed || isShore) ? sand : grass;
            var rockBase = Math.Min(0, height - 3);
            for (var y = rockBase; y < height - 1; y++)
            {
                tiles.Set(new TilePos(x, y, z), solid);
            }
            tiles.Set(new TilePos(x, height - 1, z), floorTile);
            for (var y = height; y < WaterLevelY; y++)
            {
                tiles.Set(new TilePos(x, y, z), water);
            }

            tiles.SetTerrainHeight(x, z, (short)height);
            // Lake-bed columns tag kind = Water to signal the mesher to emit
            // a water-plane overlay at WaterLevelY; the bed itself still
            // renders with sand top + walls.
            var surfaceKind = isLakeBed
                ? TileKind.Water
                : (isShore ? TileKind.Sand : TileKind.Floor);
            tiles.SetTerrainKind(x, z, surfaceKind);

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

    // Pick RiverCount random coast-to-coast paths and stamp each as a
    // flat-bedded trench. Each river starts on a random coast side,
    // ends on a different side, and meanders between the two with a
    // noise-perturbed walk. Progress toward the goal is forced on each
    // step so the path always reaches the far coast — no half-rivers.
    private static void CarveRivers(
        int[,] heights, NoiseStack noise, int seed,
        int sizeX, int sizeZ, int halfX, int halfZ, int minHeight)
    {
        int[] dxs = { -1, 0, 1, -1, 1, -1, 0, 1 };
        int[] dzs = { -1, -1, -1, 0, 0, 1, 1, 1 };
        var floorH = Math.Max(minHeight, WaterLevelY - RiverBedDepth);

        for (var riverIdx = 0; riverIdx < RiverCount; riverIdx++)
        {
            var u1 = HashUnit(seed, riverIdx * 7 + 1);
            var u2 = HashUnit(seed, riverIdx * 7 + 2);
            var u3 = HashUnit(seed, riverIdx * 7 + 3);
            var u4 = HashUnit(seed, riverIdx * 7 + 4);

            var sideA = (int)(u1 * 4) % 4;
            var sideB = (sideA + 1 + (int)(u2 * 3)) % 4;
            var (xiS, ziS) = CoastPoint(sideA, u3, sizeX, sizeZ);
            var (xiE, ziE) = CoastPoint(sideB, u4, sizeX, sizeZ);

            var cxi = xiS; var czi = ziS;
            var maxSteps = Math.Abs(xiE - xiS) + Math.Abs(ziE - ziS) + RiverWalkSlack;
            for (var step = 0; step < maxSteps; step++)
            {
                CarveDisc(heights, cxi, czi, RiverBedRadius, floorH, sizeX, sizeZ);
                if (cxi == xiE && czi == ziE) break;

                var dxGoal = xiE - cxi;
                var dzGoal = ziE - czi;
                var sgX = Math.Sign(dxGoal);
                var sgZ = Math.Sign(dzGoal);
                var goalLen = MathF.Sqrt(dxGoal * dxGoal + dzGoal * dzGoal);

                var mAng = noise.Meander.GetNoise(cxi * 0.08f, czi * 0.08f) * MathF.PI;
                var pdx = MathF.Cos(mAng);
                var pdz = MathF.Sin(mAng);

                var bestK = -1;
                var bestScore = float.NegativeInfinity;
                for (var k = 0; k < 8; k++)
                {
                    var stepX = dxs[k]; var stepZ = dzs[k];
                    // Forced progress: never step away from goal on either axis.
                    if (sgX != 0 && stepX * sgX < 0) continue;
                    if (sgZ != 0 && stepZ * sgZ < 0) continue;
                    var invLen = 1f / MathF.Sqrt(stepX * stepX + stepZ * stepZ);
                    var progAlign = goalLen > 0f
                        ? (stepX * dxGoal + stepZ * dzGoal) * invLen / goalLen
                        : 0f;
                    var meanderAlign = (stepX * pdx + stepZ * pdz) * invLen;
                    var score = progAlign + meanderAlign * RiverMeanderWeight;
                    if (score > bestScore) { bestScore = score; bestK = k; }
                }
                if (bestK < 0) break;
                cxi += dxs[bestK];
                czi += dzs[bestK];
            }
        }
    }

    private static (int, int) CoastPoint(int side, float t, int sizeX, int sizeZ)
    {
        // Stay a tile off each corner so degenerate (corner → corner) paths
        // don't collapse to zero length.
        var mx = Math.Min(2, Math.Max(0, sizeX / 4));
        var mz = Math.Min(2, Math.Max(0, sizeZ / 4));
        var spanX = Math.Max(1, sizeX - 1 - 2 * mx);
        var spanZ = Math.Max(1, sizeZ - 1 - 2 * mz);
        return side switch
        {
            0 => (mx + (int)(t * spanX), 0),                 // south
            1 => (sizeX - 1, mz + (int)(t * spanZ)),         // east
            2 => (mx + (int)(t * spanX), sizeZ - 1),         // north
            _ => (0, mz + (int)(t * spanZ)),                  // west
        };
    }

    private static void CarveDisc(int[,] heights, int cx, int cz, int r, int floorH, int sizeX, int sizeZ)
    {
        for (var dz = -r; dz <= r; dz++)
        for (var dx = -r; dx <= r; dx++)
        {
            if (dx * dx + dz * dz > r * r + 1) continue;
            var nxi = cx + dx; var nzi = cz + dz;
            if ((uint)nxi >= (uint)sizeX || (uint)nzi >= (uint)sizeZ) continue;
            if (heights[nxi, nzi] <= floorH) continue;
            heights[nxi, nzi] = floorH;
        }
    }

    // Iterative single-step dilation. After each pass every cell sits at
    // most one tile above its lowest 8-neighbor; repeat until the heightmap
    // stabilizes. Sub-sea-level cells (river beds, lake beds, coast rim)
    // seed the dilation implicitly — they're already lower than their
    // neighbors, so the sweep pulls inland terrain down in a pyramid of
    // slope 1 until it meets ambient and terminates. Guarantees no
    // single-tile cliff jumps > 1 between any bank cell and its neighbor.
    private static void LowerBanks(int[,] heights, int sizeX, int sizeZ)
    {
        var changed = true;
        var maxPasses = 256;
        for (var pass = 0; pass < maxPasses && changed; pass++)
        {
            changed = false;
            for (var xi = 0; xi < sizeX; xi++)
            for (var zi = 0; zi < sizeZ; zi++)
            {
                var minN = int.MaxValue;
                for (var dz = -1; dz <= 1; dz++)
                for (var dx = -1; dx <= 1; dx++)
                {
                    if (dx == 0 && dz == 0) continue;
                    var nxi = xi + dx; var nzi = zi + dz;
                    if ((uint)nxi >= (uint)sizeX || (uint)nzi >= (uint)sizeZ) continue;
                    if (heights[nxi, nzi] < minN) minN = heights[nxi, nzi];
                }
                if (minN == int.MaxValue) continue;
                // Floor at water level so the dilation never drags dry
                // cells below sea level — sub-sea beds stay local, the
                // ramp always rises from the waterline upward.
                var target = Math.Max(WaterLevelY, minN + 1);
                if (heights[xi, zi] > target)
                {
                    heights[xi, zi] = target;
                    changed = true;
                }
            }
        }
    }

    private static uint HashUint(int seed, int salt)
    {
        unchecked
        {
            var h = (uint)seed * 2654435761u + (uint)salt * 40503u;
            h ^= h >> 13;
            h *= 1274126177u;
            h ^= h >> 16;
            return h;
        }
    }

    private static float HashUnit(int seed, int salt) =>
        (HashUint(seed, salt) & 0xFFFFFFu) / (float)0x1000000;

    // Shore-aware clamp. Use the neighbor's height directly so land-land and
    // lake-lake boundaries produce a shared corner — smooth mountains, no
    // random mid-slope cliffs. Only clamp when the boundary crosses sea
    // level (shore ↔ lakebed) so the waterline is a crisp cliff the mesher
    // can pick up, while the rest of the terrain blends continuously.
    private static int Cap(int candidate, int own)
    {
        var ownDry = own >= WaterLevelY;
        var candDry = candidate >= WaterLevelY;
        return ownDry != candDry ? own : candidate;
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
