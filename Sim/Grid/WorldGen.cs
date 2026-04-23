using System.Collections.Generic;
using System.Threading.Tasks;
using CowColonySim.Sim.Biomes;

namespace CowColonySim.Sim.Grid;

public static class WorldGen
{
    public const int DefaultMinHeight = -10;
    public const int DefaultMaxHeight = 103;
    public const float DefaultFrequency = 0.02f;

    // Sea level. Columns below this fill with Water tiles up to (WaterLevelY-1).
    public const int WaterLevelY = 0;

    // Mountain region onset. MountainMask noise above MountainOnset begins
    // ramping into ridge-shaped peaks; above MountainFull is fully
    // mountainous. Smoothstep-blended. Raised 0.55/0.80 → 0.75/0.95 on
    // 2026-04-22 so mountains are sparse isolated clusters rather than
    // large swathes — both for aesthetics and to give the strict river
    // walker room to find non-mountain corridors between coasts.
    private const float MountainOnset = 0.75f;
    private const float MountainFull  = 0.95f;

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
    // Rivers never cross each other — subsequent river plans refuse to
    // step into cells claimed by any earlier river.
    private const int   RiverCount             = 3;
    // Bed geometry. Fixed width 5 tiles (RiverBedRadius = 2 → 5-diameter
    // disc). Fixed depth below sea level so water surface is flat.
    private const int   RiverBedRadius         = 2;
    private const int   RiverBedDepth          = 2;
    // Goal bias at leisure. Constant pull toward the far coast while there
    // is budget to spare — river drifts toward the goal, meander dominates
    // the local heading. Smaller values mean curlier rivers.
    private const float RiverGoalBaseBias      = 0.30f;
    // Goal-bias boost ramp. urgency = smoothstep(0.35, 1.0, distLeft/budget)
    // — stays 0 while the river has budget, grows to 1 near the limit,
    // turning the path into a goal-locked line when time is running out.
    private const float RiverGoalUrgencyBoost  = 1.8f;
    // Budget = manhattan * multiplier + slack. Raised so rivers have room
    // to meander laterally without running out before they reach the far
    // coast.
    private const float RiverBudgetMultiplier  = 2.4f;
    private const int   RiverWalkSlack         = 128;
    // River-proximity mask radii. Within RiverMountainInner tiles of any
    // river cell, mountain weight is fully suppressed and baseH is pulled
    // toward plains. Beyond RiverMountainOuter, no influence. Smoothstep
    // blend between. Keeps rivers running through plains / valleys, never
    // through ridgeline cliffs.
    private const int   RiverMountainInner     = 6;
    private const int   RiverMountainOuter     = 24;
    // Plains pull target near rivers — low rolling valley floor. Sits
    // comfortably above sea level so the river's 5-tile bed still cuts a
    // visible channel without drowning the surrounding land.
    private const float RiverValleyH           = 3f;

    // Bank-lowering pass. Iterative single-step dilation limited to the
    // shoreline band — cells at or below (WaterLevelY + BankDilationCap)
    // get pulled toward their lowest neighbor with a slope 1, smoothing
    // the water → plains transition. Mountains (anything above the cap)
    // are untouched so ridgelines do not collapse into pyramids. A ramp
    // never reaches into mountain terrain because the sweep terminates at
    // the cap height.
    private const int BankDilationCap = 6;

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

    // Climate. Latitude-driven base temperature plus a slow noise wobble.
    // Rainfall is low-freq noise biased up near rivers. Both are sampled
    // **per cell** (one value per 256×256 tile cell) so biomes are blocky,
    // one-biome-per-cell — no per-tile crossover fuzz. Sampled once at
    // worldgen from the cell center; a later weather system can animate
    // deltas on top. Elevation no longer affects the stored temperature —
    // snowy peaks come from the SnowPeakHeightTiles override below.
    //
    // Latitude runs along the Z axis: |z|/halfZ = 0 at the equator, 1 at the
    // pole. EquatorC warms the tropics, PoleC cools the arctic rim.
    public const float TempEquatorC   = 28f;
    public const float TempPoleC      = -20f;
    public const float TempNoiseAmpC  = 4f;

    // Snow cap override. Cells are one-biome-each, but any tile rising more
    // than this many tiles above sea level re-tags as Snow — gives mountains
    // white peaks inside e.g. a jungle or grassland cell without splitting
    // the cell into sub-biomes. Explicitly skipped for Savanna and Desert
    // cells so hot-band peaks keep their host biome instead of growing
    // incongruous snow caps.
    public const int   SnowPeakHeightTiles = 60;

    // Biome-border transition band. Tiles within this many tiles of a cell
    // edge noise-dither between their own cell's biome and the nearest
    // neighbor cell's biome, weighted by distance from the edge. Produces
    // a fuzzy / interlocking-finger seam 32 tiles wide (this many on each
    // side of the cell border) while keeping the bulk of each cell firmly
    // single-biome. One chunk (16 tiles) felt natural without eating into
    // the pure-biome interior.
    public const int   BiomeBorderBandTiles = 16;

    public const float RainMinMm      = 0f;
    public const float RainMaxMm      = 2500f;
    public const float RainRiverBoostMm = 800f;
    public const int   RainRiverInner = 10;
    public const int   RainRiverOuter = 30;

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

        // Plan river paths up-front so the heightmap pass can suppress
        // mountains / big hills along the river corridor. Paths store world-
        // space tile coords; the helper converts to array indices when it
        // needs to index heights[].
        var riverPaths = PlanRivers(noise, seed, sizeX, sizeZ, halfX, halfZ);
        var riverDist = BuildRiverDistField(riverPaths, sizeX, sizeZ, halfX, halfZ);

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
                // River-proximity influence: 1 near a river cell, fading
                // smoothly to 0 at RiverMountainOuter. Suppresses mountains
                // and pulls baseH toward a valley floor so rivers run
                // through plains instead of through ridgelines.
                var rd = riverDist[xi, zi];
                var riverInfluence = 1f - Smoothstep(RiverMountainInner, RiverMountainOuter, rd);

                var maskRaw = (noise.MountainMask.GetNoise(x, z) + 1f) * 0.5f;
                var mountainWeight = Smoothstep(MountainOnset, MountainFull, maskRaw);
                mountainWeight *= (1f - riverInfluence);
                if (mountainWeight > 0f)
                {
                    var ridge = (noise.Ridge.GetNoise(x, z) + 1f) * 0.5f;
                    var mountainH = 12f + ridge * 90f;                // 12..102
                    baseH = Lerp(baseH, mountainH, mountainWeight);
                }
                // Pull plains toward the valley floor near rivers — kills
                // big rolling hills that would otherwise sit over the river.
                if (riverInfluence > 0f)
                {
                    baseH = Lerp(baseH, RiverValleyH, riverInfluence);
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

        CarveRiversFromPaths(heights, riverPaths, sizeX, sizeZ, halfX, halfZ, minHeight);
        LowerBanks(heights, sizeX, sizeZ);
        tiles.SetRiverPaths(riverPaths);

        // Climate pass — one sample per 256×256 cell, taken at the cell
        // center. Biome is classified once per cell so the world reads as
        // blocky biome patches instead of per-tile crossover fuzz. Snow on
        // mountain peaks comes from the per-tile SnowPeakHeightTiles
        // override below, not from a separate cell-level sample.
        var firstCellX = FloorDiv(-halfX, Cell.SizeTiles);
        var firstCellZ = FloorDiv(-halfZ, Cell.SizeTiles);
        var lastCellX  = FloorDiv(halfX - 1, Cell.SizeTiles);
        var lastCellZ  = FloorDiv(halfZ - 1, Cell.SizeTiles);
        var cellsX = lastCellX - firstCellX + 1;
        var cellsZ = lastCellZ - firstCellZ + 1;
        var cellTemp  = new float[cellsX, cellsZ];
        var cellRain  = new float[cellsX, cellsZ];
        var cellBiome = new byte [cellsX, cellsZ];
        Parallel.For(0, cellsX, cxi =>
        {
            for (var czi = 0; czi < cellsZ; czi++)
            {
                var cx = firstCellX + cxi;
                var cz = firstCellZ + czi;
                // Cell-center world tile coords (cells span [cx*S, cx*S+S)).
                var wcx = cx * Cell.SizeTiles + Cell.SizeTiles / 2;
                var wcz = cz * Cell.SizeTiles + Cell.SizeTiles / 2;
                var rxi = Math.Clamp(wcx + halfX, 0, sizeX - 1);
                var rzi = Math.Clamp(wcz + halfZ, 0, sizeZ - 1);
                SampleClimate(noise, wcx, wcz, riverDist[rxi, rzi],
                    halfZ, out var tempC, out var rainMm);
                cellTemp[cxi, czi]  = tempC;
                cellRain[cxi, czi]  = rainMm;
                cellBiome[cxi, czi] = BiomeClassifier.Pick(tempC, rainMm);
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
            // One climate + biome per cell, plus two overrides:
            //   (a) Border-band dither — tiles within BiomeBorderBandTiles
            //       of a cell edge may re-home to the nearest neighbor cell
            //       based on a smooth noise value weighted by distance from
            //       the edge. Produces an interlocking-finger transition
            //       between two biomes (~32 tiles wide) without promoting
            //       the tile's cell to a mixed biome.
            //   (b) Snow peaks — tiles rising SnowPeakHeightTiles above sea
            //       level re-tag as Snow, skipped for Savanna and Desert
            //       host cells so hot biomes keep their identity at
            //       altitude.
            var ownCellX = FloorDiv(x, Cell.SizeTiles);
            var ownCellZ = FloorDiv(z, Cell.SizeTiles);
            var cxi = ownCellX - firstCellX;
            var czi = ownCellZ - firstCellZ;
            var lx = x - ownCellX * Cell.SizeTiles;
            var lz = z - ownCellZ * Cell.SizeTiles;
            PickBorderCell(noise, x, z, cxi, czi, lx, lz,
                cellBiome, cellsX, cellsZ,
                out var pickedCxi, out var pickedCzi);
            var tempC  = cellTemp[pickedCxi, pickedCzi];
            var rainMm = cellRain[pickedCxi, pickedCzi];
            var cellB  = cellBiome[pickedCxi, pickedCzi];
            var biome  = cellB;
            var allowSnowPeak = cellB != BiomeBuiltins.SavannaId
                             && cellB != BiomeBuiltins.DesertId;
            if (height - WaterLevelY >= SnowPeakHeightTiles && allowSnowPeak)
                biome = BiomeBuiltins.SnowId;
            tiles.SetTerrainClimate(x, z, tempC, rainMm);
            tiles.SetTerrainBiome(x, z, biome);
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

    // Pick RiverCount coast-to-coast paths using a continuous-position
    // walker. The walker holds a float (px, pz) and advances by a unit
    // heading vector each step; the cell it occupies (floor) is recorded
    // and marked as river. The heading is the normalized sum of the
    // meander-noise direction, a goal-pull (weak while there is budget,
    // strong when nearing exhaustion), and the previous-step heading
    // scaled by RiverWalkerMomentum — momentum damps sharp direction
    // flips so the rasterized cell trail curves smoothly instead of
    // zigzagging between pure 45° diagonals.
    //
    // Hard constraints, zero relaxation:
    //   - mountain-mask cells are never entered
    //   - cells already claimed by another river are never entered
    //   - a river never crosses itself (selfCells)
    //
    // If the walker gets pinned (FindHeadingStep returns null) or runs
    // out of budget before reaching the far coast, the entire attempt
    // is discarded and the next attempt re-rolls the coast sides and
    // endpoint offsets via a different hash salt. Up to RiverMaxAttempts
    // attempts per river — after that, the river is silently dropped
    // rather than forced into a crossing.
    private const float RiverWalkerMomentum = 1.25f;
    private const int   RiverMaxAttempts    = 64;
    private static readonly float[] RiverHeadingRotations =
    {
        0f,
        -0.175f,  0.175f,   // ±10°
        -0.349f,  0.349f,   // ±20°
        -0.524f,  0.524f,   // ±30°
        -0.698f,  0.698f,   // ±40°
        -0.873f,  0.873f,   // ±50°
        -1.047f,  1.047f,   // ±60°
        -1.222f,  1.222f,   // ±70°
        -1.396f,  1.396f,   // ±80°
        -1.571f,  1.571f,   // ±90°
        -1.745f,  1.745f,   // ±100°
        -1.920f,  1.920f,   // ±110°
        -2.094f,  2.094f,   // ±120°
        -2.269f,  2.269f,   // ±130°
        -2.443f,  2.443f,   // ±140°
    };

    private static List<RiverPath> PlanRivers(
        NoiseStack noise, int seed,
        int sizeX, int sizeZ, int halfX, int halfZ)
    {
        var used = new bool[sizeX, sizeZ];
        var paths = new List<RiverPath>(RiverCount);

        for (var riverIdx = 0; riverIdx < RiverCount; riverIdx++)
        {
            RiverPath? committed = null;
            for (var attempt = 0; attempt < RiverMaxAttempts && committed == null; attempt++)
            {
                // Salt mixes riverIdx and attempt so re-rolls explore a
                // different (sideA, sideB, start, end) quadruple each try.
                var salt = (riverIdx * 131 + attempt) * 7;
                var u1 = HashUnit(seed, salt + 1);
                var u2 = HashUnit(seed, salt + 2);
                var u3 = HashUnit(seed, salt + 3);
                var u4 = HashUnit(seed, salt + 4);

                var sideA = (int)(u1 * 4) % 4;
                var sideB = (sideA + 1 + (int)(u2 * 3)) % 4;
                var (xiS, ziS) = CoastPoint(sideA, u3, sizeX, sizeZ);
                var (xiE, ziE) = CoastPoint(sideB, u4, sizeX, sizeZ);

                // Reject endpoints that already collide with prior rivers.
                if ((uint)xiS < (uint)sizeX && (uint)ziS < (uint)sizeZ && used[xiS, ziS]) continue;
                if ((uint)xiE < (uint)sizeX && (uint)ziE < (uint)sizeZ && used[xiE, ziE]) continue;

                committed = TryPlanRiver(noise, sizeX, sizeZ, halfX, halfZ,
                    used, xiS, ziS, xiE, ziE);
            }
            if (committed == null) continue;

            foreach (var c in committed.Cells)
            {
                var xi = c.X + halfX;
                var zi = c.Z + halfZ;
                if ((uint)xi < (uint)sizeX && (uint)zi < (uint)sizeZ)
                    used[xi, zi] = true;
            }
            paths.Add(committed);
        }
        return paths;
    }

    // Run a continuous-walker attempt from start to end. Returns the
    // completed RiverPath only when the walker reaches Chebyshev radius
    // 1 of the endpoint without being pinned. On failure (pinned or
    // budget exhausted), returns null so the caller can re-roll the
    // endpoints. Cells claimed during the attempt are recorded only in
    // the local selfCells set; the shared `used` mask is never mutated
    // here — the caller commits on success.
    private static RiverPath? TryPlanRiver(
        NoiseStack noise, int sizeX, int sizeZ, int halfX, int halfZ,
        bool[,] used, int xiS, int ziS, int xiE, int ziE)
    {
        var cells = new List<(int X, int Z)>();
        var selfCells = new HashSet<int>();
        var px = xiS + 0.5f;
        var pz = ziS + 0.5f;
        var hPrevX = 0f;
        var hPrevZ = 0f;
        var lastCxi = int.MinValue;
        var lastCzi = int.MinValue;
        var manhattan = Math.Abs(xiE - xiS) + Math.Abs(ziE - ziS);
        var maxSteps = (int)(manhattan * RiverBudgetMultiplier) + RiverWalkSlack;
        var reachedEnd = false;
        for (var step = 0; step < maxSteps; step++)
        {
            var cxi = (int)MathF.Floor(px);
            var czi = (int)MathF.Floor(pz);
            if ((uint)cxi < (uint)sizeX && (uint)czi < (uint)sizeZ
                && (cxi != lastCxi || czi != lastCzi))
            {
                // FindHeadingStep already rejects used / self-cross, so a
                // collision here means the start cell itself overlaps —
                // bail rather than record a conflict.
                if (used[cxi, czi]) return null;
                cells.Add((cxi - halfX, czi - halfZ));
                selfCells.Add(cxi * sizeZ + czi);
                lastCxi = cxi;
                lastCzi = czi;
            }
            if (Math.Max(Math.Abs(xiE - cxi), Math.Abs(ziE - czi)) <= 1)
            {
                reachedEnd = true;
                break;
            }

            var dxGoal = xiE - cxi;
            var dzGoal = ziE - czi;
            var chebLeft = Math.Max(Math.Abs(dxGoal), Math.Abs(dzGoal));
            var remaining = Math.Max(1, maxSteps - step);
            var urgencyRaw = chebLeft / (float)remaining;
            var urgency = Smoothstep(0.35f, 1.0f, urgencyRaw);

            var mAng = noise.Meander.GetNoise(px, pz) * MathF.PI;
            var mdx = MathF.Cos(mAng);
            var mdz = MathF.Sin(mAng);

            var goalLen = MathF.Sqrt(dxGoal * dxGoal + dzGoal * dzGoal);
            var gdx = goalLen > 0f ? dxGoal / goalLen : 0f;
            var gdz = goalLen > 0f ? dzGoal / goalLen : 0f;

            var goalW = RiverGoalBaseBias + urgency * RiverGoalUrgencyBoost;
            var hx = mdx + goalW * gdx + RiverWalkerMomentum * hPrevX;
            var hz = mdz + goalW * gdz + RiverWalkerMomentum * hPrevZ;
            var hMag = MathF.Sqrt(hx * hx + hz * hz);
            if (hMag > 1e-6f) { hx /= hMag; hz /= hMag; }
            else { hx = gdx; hz = gdz; }

            var pick = FindHeadingStep(
                px, pz, hx, hz, noise, halfX, halfZ,
                sizeX, sizeZ, used, selfCells, lastCxi, lastCzi);
            if (!pick.HasValue) return null;

            var (rx, rz) = pick.Value;
            px += rx;
            pz += rz;
            hPrevX = rx;
            hPrevZ = rz;
        }

        if (!reachedEnd) return null;
        var flowDx = Math.Sign(xiE - xiS);
        var flowDz = Math.Sign(ziE - ziS);
        return new RiverPath(cells, flowDx, flowDz);
    }

    // Continuous-walker heading pick. Returns a unit-length step vector
    // landing in a legal cell, or null if the walker is fully boxed in
    // by prior rivers (non-crossing is strict). Mountain cells are
    // preferred-but-not-blocked: pass 0 requires non-mountain + non-
    // crossing; pass 1 allows mountain but still rejects any cell
    // claimed by a prior river or this river's own path. The heightmap
    // river-proximity suppression (RiverMountainInner/Outer) zeroes the
    // mountain contribution within 6 tiles of any river cell anyway, so
    // a walker that slips through a mountain noise peak on pass 1 still
    // ends up rendering as a plains corridor — but stays strictly out
    // of another river's cells regardless.
    private static (float rx, float rz)? FindHeadingStep(
        float px, float pz, float hx, float hz,
        NoiseStack noise, int halfX, int halfZ,
        int sizeX, int sizeZ, bool[,] used, HashSet<int> selfCells,
        int curCxi, int curCzi)
    {
        for (var pass = 0; pass < 2; pass++)
        {
            var enforceMountain = pass < 1;
            foreach (var offset in RiverHeadingRotations)
            {
                var c = MathF.Cos(offset);
                var s = MathF.Sin(offset);
                var nhx = hx * c - hz * s;
                var nhz = hx * s + hz * c;
                var nx = (int)MathF.Floor(px + nhx);
                var nz = (int)MathF.Floor(pz + nhz);
                if ((uint)nx >= (uint)sizeX || (uint)nz >= (uint)sizeZ) continue;
                var stayingPut = nx == curCxi && nz == curCzi;
                if (!stayingPut)
                {
                    if (used[nx, nz]) continue;
                    if (selfCells.Contains(nx * sizeZ + nz)) continue;
                }
                if (enforceMountain)
                {
                    var wx = nx - halfX;
                    var wz = nz - halfZ;
                    var maskRaw = (noise.MountainMask.GetNoise(wx, wz) + 1f) * 0.5f;
                    if (maskRaw >= MountainOnset) continue;
                }
                return (nhx, nhz);
            }
        }
        return null;
    }

    // Chebyshev distance transform over the river-cell mask. Two passes
    // (forward + backward) over a 4-neighbor diagonal-aware template give
    // the exact Chebyshev distance to the nearest river cell in O(n).
    private static int[,] BuildRiverDistField(
        IReadOnlyList<RiverPath> paths,
        int sizeX, int sizeZ, int halfX, int halfZ)
    {
        const int INF = 1 << 20;
        var dist = new int[sizeX, sizeZ];
        for (var xi = 0; xi < sizeX; xi++)
        for (var zi = 0; zi < sizeZ; zi++)
            dist[xi, zi] = INF;

        foreach (var path in paths)
        {
            foreach (var cell in path.Cells)
            {
                var xi = cell.X + halfX;
                var zi = cell.Z + halfZ;
                if ((uint)xi < (uint)sizeX && (uint)zi < (uint)sizeZ)
                    dist[xi, zi] = 0;
            }
        }

        for (var xi = 0; xi < sizeX; xi++)
        for (var zi = 0; zi < sizeZ; zi++)
        {
            var v = dist[xi, zi];
            if (xi > 0                  && dist[xi - 1, zi    ] + 1 < v) v = dist[xi - 1, zi    ] + 1;
            if (zi > 0                  && dist[xi,     zi - 1] + 1 < v) v = dist[xi,     zi - 1] + 1;
            if (xi > 0 && zi > 0        && dist[xi - 1, zi - 1] + 1 < v) v = dist[xi - 1, zi - 1] + 1;
            if (xi < sizeX - 1 && zi > 0 && dist[xi + 1, zi - 1] + 1 < v) v = dist[xi + 1, zi - 1] + 1;
            dist[xi, zi] = v;
        }
        for (var xi = sizeX - 1; xi >= 0; xi--)
        for (var zi = sizeZ - 1; zi >= 0; zi--)
        {
            var v = dist[xi, zi];
            if (xi < sizeX - 1                    && dist[xi + 1, zi    ] + 1 < v) v = dist[xi + 1, zi    ] + 1;
            if (zi < sizeZ - 1                    && dist[xi,     zi + 1] + 1 < v) v = dist[xi,     zi + 1] + 1;
            if (xi < sizeX - 1 && zi < sizeZ - 1  && dist[xi + 1, zi + 1] + 1 < v) v = dist[xi + 1, zi + 1] + 1;
            if (xi > 0         && zi < sizeZ - 1  && dist[xi - 1, zi + 1] + 1 < v) v = dist[xi - 1, zi + 1] + 1;
            dist[xi, zi] = v;
        }
        return dist;
    }

    // Stamp each cell of every pre-planned river path as a disc of bed
    // tiles at the fixed river-bed floor. Done after heightmap sampling
    // so the river cuts through whatever terrain the noise produced.
    private static void CarveRiversFromPaths(
        int[,] heights, IReadOnlyList<RiverPath> paths,
        int sizeX, int sizeZ, int halfX, int halfZ, int minHeight)
    {
        var floorH = Math.Max(minHeight, WaterLevelY - RiverBedDepth);
        foreach (var path in paths)
        {
            foreach (var cell in path.Cells)
            {
                var cxi = cell.X + halfX;
                var czi = cell.Z + halfZ;
                CarveDisc(heights, cxi, czi, RiverBedRadius, floorH, sizeX, sizeZ);
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

    // Iterative single-step dilation, restricted to the shoreline band.
    // Only cells at or below (WaterLevelY + BankDilationCap) can be
    // lowered — inland terrain above the cap is left alone so mountains
    // stay tall. Within the band the sweep pulls each cell down to at
    // most 1 tile above its lowest 8-neighbor, giving a gentle slope-1
    // ramp from sub-sea beds up to the cap. Beyond the cap, slopes are
    // whatever the noise stack produced (smooth ridge noise, so per-tile
    // jumps stay small).
    private static void LowerBanks(int[,] heights, int sizeX, int sizeZ)
    {
        var bandCeiling = WaterLevelY + BankDilationCap;
        var changed = true;
        var maxPasses = 256;
        for (var pass = 0; pass < maxPasses && changed; pass++)
        {
            changed = false;
            for (var xi = 0; xi < sizeX; xi++)
            for (var zi = 0; zi < sizeZ; zi++)
            {
                if (heights[xi, zi] > bandCeiling) continue;
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

    private static void SampleClimate(
        NoiseStack noise, int x, int z, int riverDist,
        int halfZ, out float tempC, out float rainMm)
    {
        var latFrac = halfZ > 0 ? Math.Min(1f, Math.Abs(z) / (float)halfZ) : 0f;
        var latT = Lerp(TempEquatorC, TempPoleC, latFrac);
        var tNoise = noise.Temperature.GetNoise(x, z) * TempNoiseAmpC;
        tempC = latT + tNoise;

        var rRaw = (noise.Rainfall.GetNoise(x, z) + 1f) * 0.5f;
        var baseRain = Lerp(RainMinMm, RainMaxMm, rRaw);
        var riverBoost = (1f - Smoothstep(RainRiverInner, RainRiverOuter, riverDist))
                         * RainRiverBoostMm;
        rainMm = Math.Clamp(baseRain + riverBoost, RainMinMm, RainMaxMm + RainRiverBoostMm);
    }

    private static int FloorDiv(int a, int b) => (a / b) - (a % b < 0 ? 1 : 0);

    // Pick the cell a border-band tile belongs to. If the tile is within
    // BiomeBorderBandTiles of its nearest cell edge AND that edge has an
    // in-bounds neighbor of a different biome, a smooth noise dither
    // (weighted by distance to the edge) may re-home the tile to the
    // neighbor's cell. At the edge itself the coin flip is ~50/50; at the
    // inner boundary of the band the tile always stays in its own cell.
    // Tiles outside the band always stay put.
    private static void PickBorderCell(
        NoiseStack noise, int x, int z,
        int selfCxi, int selfCzi, int lx, int lz,
        byte[,] cellBiome, int cellsX, int cellsZ,
        out int pickedCxi, out int pickedCzi)
    {
        pickedCxi = selfCxi;
        pickedCzi = selfCzi;

        var lastLocal = Cell.SizeTiles - 1;
        var distW = lx;
        var distE = lastLocal - lx;
        var distS = lz;
        var distN = lastLocal - lz;
        var minDist = Math.Min(Math.Min(distW, distE), Math.Min(distS, distN));
        if (minDist >= BiomeBorderBandTiles) return;

        // Candidate neighbor along the nearest edge only — corners pick one
        // or the other based on ties, which keeps the fingers unambiguous
        // and avoids a double dither that would wash out the finger shape.
        var nxi = selfCxi;
        var nzi = selfCzi;
        if (distW == minDist && selfCxi > 0)                 nxi = selfCxi - 1;
        else if (distE == minDist && selfCxi < cellsX - 1)   nxi = selfCxi + 1;
        else if (distS == minDist && selfCzi > 0)            nzi = selfCzi - 1;
        else if (distN == minDist && selfCzi < cellsZ - 1)   nzi = selfCzi + 1;
        else return; // at world edge — no neighbor to dither with

        // No transition if neighbor has the same biome: nothing to blend.
        var selfB = cellBiome[selfCxi, selfCzi];
        var nbrB  = cellBiome[nxi, nzi];
        if (selfB == nbrB) return;

        var n01 = (noise.BiomeBorder.GetNoise(x, z) + 1f) * 0.5f; // 0..1
        var t   = minDist / (float)BiomeBorderBandTiles;          // 0..1
        var threshold = 0.5f + 0.5f * t;
        if (n01 > threshold)
        {
            pickedCxi = nxi;
            pickedCzi = nzi;
        }
    }

    private static float Smoothstep(float edge0, float edge1, float x)
    {
        var t = Math.Clamp((x - edge0) / (edge1 - edge0), 0f, 1f);
        return t * t * (3f - 2f * t);
    }

    private static float Lerp(float a, float b, float t) => a + (b - a) * t;
}
