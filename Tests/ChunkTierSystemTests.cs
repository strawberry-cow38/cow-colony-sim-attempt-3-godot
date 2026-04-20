using fennecs;
using CowColonySim.Sim.Components;
using CowColonySim.Sim.Grid;
using CowColonySim.Sim.Systems;
using Xunit;

namespace CowColonySim.Tests;

public class ChunkTierSystemTests
{
    [Fact]
    public void Empty_World_Leaves_All_Chunks_Dormant()
    {
        var world = new World();
        var tiles = new TileWorld();

        ChunkTierSystem.Step(world, tiles);

        Assert.Empty(tiles.ChunkStates);
        Assert.Equal(ChunkState.Dormant, tiles.GetChunkState(new TilePos(0, 0, 0)));
        Assert.Equal(ChunkState.Dormant, tiles.GetChunkState(new TilePos(999, -999, 999)));
    }

    [Fact]
    public void Claimed_Region_Marks_Touched_Chunks_At_Min_Tier()
    {
        var world = new World();
        var tiles = new TileWorld();
        world.Spawn().Add(new ClaimedRegion(
            new TilePos(0, 0, 0),
            new TilePos(Chunk.Size - 1, Chunk.Size - 1, Chunk.Size - 1),
            ChunkState.Ambient));

        ChunkTierSystem.Step(world, tiles);

        Assert.Equal(ChunkState.Ambient, tiles.GetChunkState(new TilePos(0, 0, 0)));
        Assert.Equal(ChunkState.Dormant, tiles.GetChunkState(new TilePos(1, 0, 0)));
    }

    [Fact]
    public void Live_Anchor_Marks_Center_Chunk_Live_And_Halo_Ambient()
    {
        var world = new World();
        var tiles = new TileWorld();
        world.Spawn().Add(new LiveAnchor(new TilePos(Chunk.Size / 2, Chunk.Size / 2, Chunk.Size / 2), 1));

        ChunkTierSystem.Step(world, tiles);

        Assert.Equal(ChunkState.Live, tiles.GetChunkState(new TilePos(0, 0, 0)));
        Assert.Equal(ChunkState.Ambient, tiles.GetChunkState(new TilePos(1, 0, 0)));
        Assert.Equal(ChunkState.Ambient, tiles.GetChunkState(new TilePos(-1, 0, 0)));
        Assert.Equal(ChunkState.Ambient, tiles.GetChunkState(new TilePos(1, 1, 1)));
        Assert.Equal(ChunkState.Dormant, tiles.GetChunkState(new TilePos(2, 0, 0)));
    }

    [Fact]
    public void Live_Beats_Ambient_On_Overlap()
    {
        var world = new World();
        var tiles = new TileWorld();
        world.Spawn().Add(new ClaimedRegion(
            new TilePos(0, 0, 0),
            new TilePos(Chunk.Size * 3, Chunk.Size * 3, Chunk.Size * 3),
            ChunkState.Ambient));
        world.Spawn().Add(new LiveAnchor(new TilePos(Chunk.Size + Chunk.Size / 2, Chunk.Size + Chunk.Size / 2, Chunk.Size + Chunk.Size / 2), 1));

        ChunkTierSystem.Step(world, tiles);

        Assert.Equal(ChunkState.Live, tiles.GetChunkState(new TilePos(1, 1, 1)));
        Assert.Equal(ChunkState.Ambient, tiles.GetChunkState(new TilePos(0, 0, 0)));
    }

    [Fact]
    public void Multiple_Anchors_Contribute_Independently()
    {
        var world = new World();
        var tiles = new TileWorld();
        world.Spawn().Add(new LiveAnchor(new TilePos(Chunk.Size / 2, Chunk.Size / 2, Chunk.Size / 2), 1));
        world.Spawn().Add(new LiveAnchor(new TilePos(Chunk.Size * 10 + Chunk.Size / 2, Chunk.Size * 10 + Chunk.Size / 2, Chunk.Size * 10 + Chunk.Size / 2), 1));

        ChunkTierSystem.Step(world, tiles);

        Assert.Equal(ChunkState.Live, tiles.GetChunkState(new TilePos(0, 0, 0)));
        Assert.Equal(ChunkState.Live, tiles.GetChunkState(new TilePos(10, 10, 10)));
        Assert.Equal(ChunkState.Dormant, tiles.GetChunkState(new TilePos(5, 5, 5)));
    }

    [Fact]
    public void Replace_Clears_Stale_State()
    {
        var world = new World();
        var tiles = new TileWorld();
        world.Spawn().Add(new LiveAnchor(new TilePos(Chunk.Size / 2, Chunk.Size / 2, Chunk.Size / 2), 1));

        ChunkTierSystem.Step(world, tiles);
        Assert.Equal(ChunkState.Live, tiles.GetChunkState(new TilePos(0, 0, 0)));

        var emptyWorld = new World();
        ChunkTierSystem.Step(emptyWorld, tiles);

        Assert.Equal(ChunkState.Dormant, tiles.GetChunkState(new TilePos(0, 0, 0)));
        Assert.Empty(tiles.ChunkStates);
    }

    [Fact]
    public void Chunks_Touched_By_Bounds_Covers_All_Chunks_Crossed()
    {
        var keys = ChunkTierSystem.ChunksTouchedByBounds(
            new TilePos(-1, 0, 0),
            new TilePos(Chunk.Size, 0, 0)).ToHashSet();

        Assert.Contains(new TilePos(-1, 0, 0), keys);
        Assert.Contains(new TilePos(0, 0, 0), keys);
        Assert.Contains(new TilePos(1, 0, 0), keys);
        Assert.Equal(3, keys.Count);
    }
}
