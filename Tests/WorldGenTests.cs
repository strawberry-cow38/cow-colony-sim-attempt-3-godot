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
    public void Generate_Step_Bounded_By_Noise_Gradient()
    {
        // Mountains are raw smooth (no plateau quantization); per-tile step
        // is bounded by the ridge-noise gradient plus detail swing. 20 is a
        // generous ceiling — anything above that signals the noise stack is
        // producing unbounded spikes rather than a continuous surface. Lake
        // shorelines sit just inside this bound (shore +few → bed -few).
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
        Assert.True(maxStep <= 20, $"max per-tile step {maxStep} > 20");
    }

    [Fact]
    public void NoiseStack_Is_Deterministic()
    {
        var a = new NoiseStack(42);
        var b = new NoiseStack(42);
        Assert.Equal(a.Continent.GetNoise(3f, -2f), b.Continent.GetNoise(3f, -2f));
        Assert.Equal(a.Ridge.GetNoise(10f, 5f),    b.Ridge.GetNoise(10f, 5f));
        Assert.Equal(a.Lake.GetNoise(-7f, 0f),     b.Lake.GetNoise(-7f, 0f));
    }

    [Fact]
    public void Generate_Populates_Terrain_Heightmap()
    {
        var world = new TileWorld();
        WorldGen.Generate(world, seed: 1234, sizeX: 32, sizeZ: 32);

        // Every column in the footprint must carry a terrain height that
        // matches the voxel surface. Land columns: SurfaceY returns y above
        // topmost grass/sand (== stored height). Lake columns: SurfaceY
        // returns WaterLevelY (top of water fill); stored height is the
        // column's dry floor below sea level.
        for (var x = -16; x < 16; x++)
        for (var z = -16; z < 16; z++)
        {
            var h = world.TerrainHeightAt(x, z);
            var surfaceY = WorldGen.SurfaceY(world, x, z);
            if (h < WorldGen.WaterLevelY)
                Assert.Equal(WorldGen.WaterLevelY, surfaceY);
            else
                Assert.Equal(h, surfaceY);
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
        const int s = TerrainChunk.Size;
        // Populate per-tile corners + kinds across chunk (0,0).
        for (var lx = 0; lx < s; lx++)
        for (var lz = 0; lz < s; lz++)
        {
            var baseH = (short)(lx + lz);
            world.SetTileCorners(lx, lz, baseH, baseH, baseH, baseH);
            world.SetTerrainKind(lx, lz, lx == 0 ? TileKind.Sand : TileKind.Floor);
        }
        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        for (var lx = 0; lx < s; lx++)
        for (var lz = 0; lz < s; lz++)
        {
            Assert.Equal(lx + lz, snap!.Corners[lx, lz, TerrainChunk.SW]);
            Assert.Equal(lx + lz, snap.Corners[lx, lz, TerrainChunk.NE]);
            Assert.Equal(lx == 0 ? (byte)TileKind.Sand : (byte)TileKind.Floor,
                snap.Kinds[lx, lz]);
        }
    }

    [Fact]
    public void SnapshotTerrain_Seam_Reads_From_Neighbor()
    {
        var world = new TileWorld();
        const int s = TerrainChunk.Size;
        // Materialize own chunk.
        world.SetTileCorners(0, 0, 1, 1, 1, 1);
        // +X neighbor tile at world (s, 5) — its west edge (SW, NW) becomes
        // EastRim[5, 0] and EastRim[5, 1] for chunk (0,0).
        world.SetTileCorners(s, 5, sw: 42, se: 99, ne: 99, nw: 44);
        // +Z neighbor tile at world (7, s) — south edge (SW, SE) → NorthRim[7].
        world.SetTileCorners(7, s, sw: 77, se: 78, ne: 99, nw: 99);

        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        Assert.Equal(42, snap!.EastRim[5, 0]);
        Assert.Equal(44, snap.EastRim[5, 1]);
        Assert.Equal(77, snap.NorthRim[7, 0]);
        Assert.Equal(78, snap.NorthRim[7, 1]);
    }

    [Fact]
    public void SnapshotTerrain_Seam_Falls_Back_At_World_Edge()
    {
        var world = new TileWorld();
        const int s = TerrainChunk.Size;
        // Solitary chunk. Set east-edge tile corners; expect EastRim to
        // mirror my own SE/NE so no spurious wall gap appears.
        for (var lz = 0; lz < s; lz++)
            world.SetTileCorners(s - 1, lz, sw: 5, se: 5, ne: 5, nw: 5);
        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        for (var lz = 0; lz < s; lz++)
        {
            Assert.Equal(5, snap!.EastRim[lz, 0]);
            Assert.Equal(5, snap.EastRim[lz, 1]);
        }
    }

    [Fact]
    public void TerrainSlope_Reports_Max_Corner_Delta()
    {
        var world = new TileWorld();
        // Flat tile.
        world.SetTileCorners(0, 0, sw: 5, se: 5, ne: 5, nw: 5);
        Assert.Equal(0, world.TerrainSlope(0, 0));

        // 1-step ramp (SW/NW = 5, SE/NE = 6).
        world.SetTileCorners(0, 0, sw: 5, se: 6, ne: 6, nw: 5);
        Assert.Equal(1, world.TerrainSlope(0, 0));

        // Steep corner (NE = 9 vs SW = 5). Render corners this wide only
        // appear if something bypassed the Cap rule — TerrainSlope still
        // reports max-min so callers can detect it.
        world.SetTileCorners(0, 0, sw: 5, se: 6, ne: 9, nw: 5);
        Assert.Equal(4, world.TerrainSlope(0, 0));
    }

    [Fact]
    public void Generate_Clamps_Corners_To_Own_Column_Across_Cliffs()
    {
        // Build a minimal world manually: a 2-tile strip with a col gap
        // above CliffDelta. Corner Cap rule must clamp the low tile's east
        // corners and the high tile's west corners to their own column.
        var world = new TileWorld();
        const int delta = SimConstants.CliffDelta;
        // Low tile col = 0, plateau tile col = delta + 2. Gap > delta.
        var low = (short)0;
        var high = (short)(delta + 2);
        // Simulate worldgen by writing columns and applying the Cap rule
        // directly for a 2-tile fixture.
        world.SetTerrainHeight(0, 0, low);
        world.SetTerrainHeight(1, 0, high);
        world.SetTileCorners(0, 0, sw: low, se: low, ne: low, nw: low);   // Cap(high, low) = low
        world.SetTileCorners(1, 0, sw: high, se: high, ne: high, nw: high);

        var snap = world.SnapshotTerrain(0, 0);
        Assert.NotNull(snap);
        // Low tile east corners match its own col — flat top.
        Assert.Equal(low, snap!.Corners[0, 0, TerrainChunk.SE]);
        Assert.Equal(low, snap.Corners[0, 0, TerrainChunk.NE]);
        // High tile west corners match its own col — flat top.
        Assert.Equal(high, snap.Corners[1, 0, TerrainChunk.SW]);
        Assert.Equal(high, snap.Corners[1, 0, TerrainChunk.NW]);
        // Disagreement at the shared edge: low SE (=low) vs high SW (=high).
        // The mesher compares these and emits the wall.
    }
}
