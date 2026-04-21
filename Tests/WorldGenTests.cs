using CowColonySim.Sim;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Pathfinding;
using Xunit;

namespace CowColonySim.Tests;

public class WorldGenTests
{
    [Fact]
    public void Generate_Produces_Tiles_And_Standable_Surface()
    {
        var world = new TileWorld();
        WorldGen.Generate(world, seed: 1234, sizeX: 32, sizeZ: 32);

        Assert.True(world.ChunkCount > 0);

        var standable = 0;
        for (var x = -16; x < 16; x++)
        for (var z = -16; z < 16; z++)
        {
            var y = WorldGen.SurfaceY(world, x, z);
            if (Walkability.IsStandable(world, new TilePos(x, y, z))) standable++;
        }
        Assert.True(standable > 0);
    }

    [Fact]
    public void Generate_Is_Deterministic_For_Same_Seed()
    {
        var a = new TileWorld();
        var b = new TileWorld();
        WorldGen.Generate(a, seed: 42, sizeX: 16, sizeZ: 16);
        WorldGen.Generate(b, seed: 42, sizeX: 16, sizeZ: 16);

        for (var x = -8; x < 8; x++)
        for (var z = -8; z < 8; z++)
        {
            Assert.Equal(WorldGen.SurfaceY(a, x, z), WorldGen.SurfaceY(b, x, z));
        }
    }

    [Fact]
    public void Generate_Covers_Full_Bounds()
    {
        var world = new TileWorld();
        WorldGen.Generate(world, seed: 7, sizeX: 8, sizeZ: 8);

        for (var x = -4; x < 4; x++)
        for (var z = -4; z < 4; z++)
        {
            // Lake columns surface at WaterLevelY (water-top); land columns
            // sit higher. Anything below WaterLevelY would mean an empty gap.
            Assert.True(WorldGen.SurfaceY(world, x, z) >= WorldGen.WaterLevelY);
        }
    }

    [Fact]
    public void Generate_Is_Seamless_Across_Cell_Borders()
    {
        // Big enough to span several cells; cell borders are at
        // x = cellHalf + k*cellSize = 128 + 256k relative to world center.
        var world = new TileWorld();
        WorldGen.Generate(world, seed: 1111, sizeX: 768, sizeZ: 64);

        var maxStep = 0;
        for (var z = -16; z < 16; z++)
        for (var x = -383; x < 383; x++)
        {
            var a = WorldGen.SurfaceY(world, x, z);
            var b = WorldGen.SurfaceY(world, x + 1, z);
            var step = Math.Abs(a - b);
            if (step > maxStep) maxStep = step;
        }
        // Bilerp of 4 continuous features — step between adjacent tiles must
        // stay small. A hard cell-border seam would produce >> PlateauStep.
        // Mountain plateau terrace allows a single 10-tile band jump at ramp
        // edges; cubby gradients may add a few tiles on top. 20 covers the
        // combined case but still catches any actual cell-border seam.
        Assert.True(maxStep <= 20, $"max per-tile step {maxStep} > 20");
    }

    [Fact]
    public void FeatureResolver_Is_Deterministic()
    {
        var a = FeatureResolver.Pick(42, new CellKey(3, -2));
        var b = FeatureResolver.Pick(42, new CellKey(3, -2));
        Assert.Equal(a, b);
    }

    [Fact]
    public void Generate_Populates_Terrain_Heightmap()
    {
        var world = new TileWorld();
        WorldGen.Generate(world, seed: 1234, sizeX: 32, sizeZ: 32);

        // Every column in the generated footprint must carry a terrain
        // height matching the voxel surface (column top = height - 1, so
        // stored corner height = height).
        for (var x = -16; x < 16; x++)
        for (var z = -16; z < 16; z++)
        {
            var h = world.TerrainHeightAt(x, z);
            var surfaceY = WorldGen.SurfaceY(world, x, z);
            Assert.Equal(surfaceY, h);
        }
    }

    [Fact]
    public void Generate_Heightmap_Kinds_Match_Surface()
    {
        var world = new TileWorld();
        WorldGen.Generate(world, seed: 99, sizeX: 32, sizeZ: 32);

        for (var x = -16; x < 16; x++)
        for (var z = -16; z < 16; z++)
        {
            var h = world.TerrainHeightAt(x, z);
            var kind = world.TerrainKindAt(x, z);
            if (h < WorldGen.WaterLevelY)
            {
                Assert.Equal(TileKind.Water, kind);
            }
            else
            {
                // Land columns carry Floor or Sand — never Water / Solid /
                // Empty — so the mesher can branch on kind alone.
                Assert.True(kind == TileKind.Floor || kind == TileKind.Sand,
                    $"({x},{z}) h={h} kind={kind}");
            }
        }
    }

    [Fact]
    public void Generate_Heightmap_Is_Deterministic()
    {
        var a = new TileWorld();
        var b = new TileWorld();
        WorldGen.Generate(a, seed: 42, sizeX: 16, sizeZ: 16);
        WorldGen.Generate(b, seed: 42, sizeX: 16, sizeZ: 16);

        for (var x = -8; x < 8; x++)
        for (var z = -8; z < 8; z++)
        {
            Assert.Equal(a.TerrainHeightAt(x, z), b.TerrainHeightAt(x, z));
            Assert.Equal(a.TerrainKindAt(x, z), b.TerrainKindAt(x, z));
        }
    }

    [Fact]
    public void SnapshotTerrain_Returns_Null_When_Chunk_Absent()
    {
        var world = new TileWorld();
        Assert.Null(world.SnapshotTerrain(0, 0));
    }

    [Fact]
    public void SnapshotTerrain_Copies_Owned_Corners_And_Kinds()
    {
        var world = new TileWorld();
        // Touching any corner inside chunk (0,0) materializes it.
        for (var lx = 0; lx < TerrainChunk.Size; lx++)
        for (var lz = 0; lz < TerrainChunk.Size; lz++)
        {
            world.SetTerrainHeight(lx, lz, (short)(lx + lz));
            world.SetTerrainKind(lx, lz, lx == 0 ? TileKind.Sand : TileKind.Floor);
        }
        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        for (var lx = 0; lx < TerrainChunk.Size; lx++)
        for (var lz = 0; lz < TerrainChunk.Size; lz++)
        {
            Assert.Equal(lx + lz, snap!.Heights[lx, lz]);
            Assert.Equal(lx == 0 ? (byte)TileKind.Sand : (byte)TileKind.Floor,
                snap.Kinds[lx, lz]);
        }
    }

    [Fact]
    public void SnapshotTerrain_Seam_Reads_From_Neighbor()
    {
        var world = new TileWorld();
        const int s = TerrainChunk.Size;
        // Write a baseline into chunk (0,0) so it materializes.
        world.SetTerrainHeight(0, 0, 1);
        // +X neighbor corner at world (s, 5) — owned by chunk (1,0) at lx=0,lz=5.
        world.SetTerrainHeight(s, 5, 42);
        // +Z neighbor corner at world (7, s).
        world.SetTerrainHeight(7, s, 77);
        // +XZ neighbor corner at world (s, s).
        world.SetTerrainHeight(s, s, 99);

        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        Assert.Equal(42, snap!.Heights[s, 5]);
        Assert.Equal(77, snap.Heights[7, s]);
        Assert.Equal(99, snap.Heights[s, s]);
    }

    [Fact]
    public void SnapshotTerrain_Seam_Falls_Back_At_World_Edge()
    {
        var world = new TileWorld();
        const int s = TerrainChunk.Size;
        // Single chunk, no neighbors. Edge column at lx=s-1 set to 5;
        // expect the seam row at lx=s to mirror it.
        for (var lz = 0; lz < s; lz++)
            world.SetTerrainHeight(s - 1, lz, 5);
        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        for (var lz = 0; lz < s; lz++)
            Assert.Equal(5, snap!.Heights[s, lz]);
    }

    [Fact]
    public void SetTerrainCliffE_Round_Trips_Into_Snapshot()
    {
        var world = new TileWorld();
        world.SetTerrainHeight(5, 5, 10);
        world.SetTerrainCliffE(5, 5, lowerHeight: 3);

        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        Assert.Equal(TerrainSnapshot.CliffBitE,
            snap!.CliffMask[5, 5] & TerrainSnapshot.CliffBitE);
        Assert.Equal(3, snap.CliffLowerE[5, 5]);
    }

    [Fact]
    public void SetTerrainCliffS_Round_Trips_Into_Snapshot()
    {
        var world = new TileWorld();
        world.SetTerrainHeight(2, 3, 8);
        world.SetTerrainCliffS(2, 3, lowerHeight: 1);

        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        Assert.Equal(TerrainSnapshot.CliffBitS,
            snap!.CliffMask[2, 3] & TerrainSnapshot.CliffBitS);
        Assert.Equal(1, snap.CliffLowerS[2, 3]);
    }

    [Fact]
    public void SnapshotTerrain_Derives_W_Bit_From_Same_Chunk_Neighbor()
    {
        var world = new TileWorld();
        // Tile (4, 7) owns its E edge; that edge is tile (5, 7)'s W edge.
        world.SetTerrainHeight(4, 7, 12);
        world.SetTerrainCliffE(4, 7, lowerHeight: 4);

        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        // Upper tile carries E; lower tile carries W + mirrored lower height.
        Assert.Equal(TerrainSnapshot.CliffBitE,
            snap!.CliffMask[4, 7] & TerrainSnapshot.CliffBitE);
        Assert.Equal(TerrainSnapshot.CliffBitW,
            snap.CliffMask[5, 7] & TerrainSnapshot.CliffBitW);
        Assert.Equal(4, snap.CliffLowerW[5, 7]);
    }

    [Fact]
    public void SnapshotTerrain_Derives_N_Bit_From_Same_Chunk_Neighbor()
    {
        var world = new TileWorld();
        world.SetTerrainHeight(6, 2, 15);
        world.SetTerrainCliffS(6, 2, lowerHeight: 6);

        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        Assert.Equal(TerrainSnapshot.CliffBitN,
            snap!.CliffMask[6, 3] & TerrainSnapshot.CliffBitN);
        Assert.Equal(6, snap.CliffLowerN[6, 3]);
    }

    [Fact]
    public void SnapshotTerrain_Derives_W_Bit_Across_Chunk_Seam()
    {
        var world = new TileWorld();
        // -X neighbor chunk (-1, 0) owns world column x=-1. Its E edge is our
        // (lx=0) tile's W edge.
        world.SetTerrainHeight(-1, 4, 20);
        world.SetTerrainCliffE(-1, 4, lowerHeight: 7);
        // Materialize our chunk so it can be snapshotted.
        world.SetTerrainHeight(0, 4, 20);

        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        Assert.Equal(TerrainSnapshot.CliffBitW,
            snap!.CliffMask[0, 4] & TerrainSnapshot.CliffBitW);
        Assert.Equal(7, snap.CliffLowerW[0, 4]);
    }

    [Fact]
    public void SnapshotTerrain_Derives_N_Bit_Across_Chunk_Seam()
    {
        var world = new TileWorld();
        world.SetTerrainHeight(3, -1, 25);
        world.SetTerrainCliffS(3, -1, lowerHeight: 9);
        world.SetTerrainHeight(3, 0, 25);

        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        Assert.Equal(TerrainSnapshot.CliffBitN,
            snap!.CliffMask[3, 0] & TerrainSnapshot.CliffBitN);
        Assert.Equal(9, snap.CliffLowerN[3, 0]);
    }

    [Fact]
    public void SnapshotTerrain_Has_No_WN_Bits_At_World_Edge()
    {
        var world = new TileWorld();
        // Materialize chunk (0,0) but leave no -X / -Z neighbors.
        world.SetTerrainHeight(0, 0, 1);
        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        for (var lz = 0; lz < TerrainChunk.Size; lz++)
        {
            Assert.Equal(0,
                snap!.CliffMask[0, lz] & TerrainSnapshot.CliffBitW);
        }
        for (var lx = 0; lx < TerrainChunk.Size; lx++)
        {
            Assert.Equal(0,
                snap!.CliffMask[lx, 0] & TerrainSnapshot.CliffBitN);
        }
    }

    [Fact]
    public void TerrainChunk_ClearCliff_Clears_Bit_And_Lower()
    {
        var tc = new TerrainChunk();
        tc.SetCliffE(3, 4, lowerHeight: 2);
        Assert.NotEqual(0, tc.CliffMask[3, 4] & TerrainChunk.CliffBitE);
        tc.ClearCliffE(3, 4);
        Assert.Equal(0, tc.CliffMask[3, 4] & TerrainChunk.CliffBitE);
        Assert.Equal(0, tc.CliffLowerE[3, 4]);
    }

    [Fact]
    public void TerrainChunk_SetCliff_Skips_Revision_On_Redundant_Write()
    {
        var tc = new TerrainChunk();
        tc.SetCliffE(0, 0, lowerHeight: 5);
        var rev = tc.Revision;
        tc.SetCliffE(0, 0, lowerHeight: 5);
        Assert.Equal(rev, tc.Revision);
    }

    [Fact]
    public void Generate_Emits_Cliff_Flags_On_Rough_Terrain()
    {
        // 256x256 across many cells covers mesa / cubby / mountain features
        // — the ridged-noise + mesa pass reliably produces corner-delta
        // jumps >= CliffMinDelta somewhere in that footprint.
        var world = new TileWorld();
        WorldGen.Generate(world, seed: 1234, sizeX: 256, sizeZ: 256);

        var flagged = 0;
        for (var cx = -8; cx <= 8; cx++)
        for (var cz = -8; cz <= 8; cz++)
        {
            var tc = world.GetTerrainChunkOrNull(cx, cz);
            if (tc == null) continue;
            for (var lx = 0; lx < TerrainChunk.Size; lx++)
            for (var lz = 0; lz < TerrainChunk.Size; lz++)
                if (tc.CliffMask[lx, lz] != 0) flagged++;
        }
        Assert.True(flagged > 0,
            "expected worldgen to emit at least one cliff flag in a 256x256 footprint");
    }

    [Fact]
    public void Generate_Cliffs_Are_Deterministic_For_Same_Seed()
    {
        var a = new TileWorld();
        var b = new TileWorld();
        WorldGen.Generate(a, seed: 777, sizeX: 64, sizeZ: 64);
        WorldGen.Generate(b, seed: 777, sizeX: 64, sizeZ: 64);

        for (var cx = -2; cx <= 2; cx++)
        for (var cz = -2; cz <= 2; cz++)
        {
            var ta = a.GetTerrainChunkOrNull(cx, cz);
            var tb = b.GetTerrainChunkOrNull(cx, cz);
            if (ta == null && tb == null) continue;
            Assert.NotNull(ta);
            Assert.NotNull(tb);
            for (var lx = 0; lx < TerrainChunk.Size; lx++)
            for (var lz = 0; lz < TerrainChunk.Size; lz++)
            {
                Assert.Equal(ta!.CliffMask[lx, lz], tb!.CliffMask[lx, lz]);
                Assert.Equal(ta.CliffLowerE[lx, lz], tb.CliffLowerE[lx, lz]);
                Assert.Equal(ta.CliffLowerS[lx, lz], tb.CliffLowerS[lx, lz]);
            }
        }
    }

    [Fact]
    public void Generate_Cliff_Edge_Corners_Match_Upper_Height()
    {
        // For every E / S cliff flagged by worldgen, the shared corners on
        // that edge must be stored at the upper tile's own column height —
        // so the mesher can read "stored = upper" without cross-tile math.
        var world = new TileWorld();
        WorldGen.Generate(world, seed: 42, sizeX: 128, sizeZ: 128);

        var checkedCliffs = 0;
        for (var x = -64; x < 63; x++)
        for (var z = -64; z < 63; z++)
        {
            var (cx, cz) = (FloorDivForTest(x, TerrainChunk.Size), FloorDivForTest(z, TerrainChunk.Size));
            var tc = world.GetTerrainChunkOrNull(cx, cz);
            if (tc == null) continue;
            var lx = x - cx * TerrainChunk.Size;
            var lz = z - cz * TerrainChunk.Size;
            var mask = tc.CliffMask[lx, lz];
            if ((mask & TerrainChunk.CliffBitE) != 0)
            {
                var upper = world.TerrainHeightAt(x, z);
                Assert.Equal(upper, world.TerrainHeightAt(x + 1, z));
                Assert.Equal(upper, world.TerrainHeightAt(x + 1, z + 1));
                Assert.True(tc.CliffLowerE[lx, lz] < upper,
                    $"E cliff at ({x},{z}): lower {tc.CliffLowerE[lx, lz]} must be < upper {upper}");
                checkedCliffs++;
            }
            if ((mask & TerrainChunk.CliffBitS) != 0)
            {
                var upper = world.TerrainHeightAt(x, z);
                Assert.Equal(upper, world.TerrainHeightAt(x, z + 1));
                Assert.Equal(upper, world.TerrainHeightAt(x + 1, z + 1));
                Assert.True(tc.CliffLowerS[lx, lz] < upper,
                    $"S cliff at ({x},{z}): lower {tc.CliffLowerS[lx, lz]} must be < upper {upper}");
                checkedCliffs++;
            }
        }
        Assert.True(checkedCliffs > 0, "expected at least one cliff to verify");
    }

    private static int FloorDivForTest(int a, int b)
    {
        var q = a / b;
        var r = a % b;
        if ((r != 0) && ((r < 0) != (b < 0))) q--;
        return q;
    }

    [Fact]
    public void TerrainSlope_Reports_Max_Corner_Delta()
    {
        var world = new TileWorld();
        // Flat quad
        world.SetTerrainHeight(0, 0, 5);
        world.SetTerrainHeight(1, 0, 5);
        world.SetTerrainHeight(0, 1, 5);
        world.SetTerrainHeight(1, 1, 5);
        Assert.Equal(0, world.TerrainSlope(0, 0));

        // 1-step ramp
        world.SetTerrainHeight(1, 0, 6);
        world.SetTerrainHeight(1, 1, 6);
        Assert.Equal(1, world.TerrainSlope(0, 0));

        // Cliff
        world.SetTerrainHeight(1, 1, 9);
        Assert.Equal(4, world.TerrainSlope(0, 0));
    }
}
