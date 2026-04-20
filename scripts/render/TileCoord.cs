using Godot;
using CowColonySim.Sim;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

public static class TileCoord
{
    public const float Tile = SimConstants.TileSizeMeters;

    public static Vector3 TileCenter(TilePos pos) => new(
        pos.X * Tile + Tile * 0.5f,
        pos.Y * Tile + Tile * 0.5f,
        pos.Z * Tile + Tile * 0.5f
    );

    public static Vector3 ChunkOrigin(TilePos chunkKey) => new(
        chunkKey.X * Chunk.Size * Tile,
        chunkKey.Y * Chunk.Size * Tile,
        chunkKey.Z * Chunk.Size * Tile
    );
}
