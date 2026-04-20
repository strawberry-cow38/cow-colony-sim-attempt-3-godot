using Godot;
using CowColonySim.Sim;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

public static class TileCoord
{
    public const float TileW = SimConstants.TileWidthMeters;
    public const float TileH = SimConstants.TileHeightMeters;

    public static Vector3 TileCenter(TilePos pos) => new(
        pos.X * TileW + TileW * 0.5f,
        pos.Y * TileH + TileH * 0.5f,
        pos.Z * TileW + TileW * 0.5f
    );

    public static Vector3 ChunkOrigin(TilePos chunkKey) => new(
        chunkKey.X * Chunk.Size * TileW,
        chunkKey.Y * Chunk.Size * TileH,
        chunkKey.Z * Chunk.Size * TileW
    );
}
