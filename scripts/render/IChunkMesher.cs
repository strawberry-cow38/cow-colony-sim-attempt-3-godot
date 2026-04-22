using Godot;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

public sealed class MeshBuildResult
{
    public Vector3[] Verts = null!;
    public Vector3[] Normals = null!;
    public Color[] Colors = null!;
    public Vector2[] Uvs = null!;
    public int[] Indices = null!;
    public int Revision;
    public int LodLevel;

    // Optional second surface for translucent water planes. Kept off the main
    // surface so it can be drawn with a transparent material without punting
    // every opaque terrain triangle through the alpha queue.
    public Vector3[]? WaterVerts;
    public Vector3[]? WaterNormals;
    public Color[]? WaterColors;
    public Vector2[]? WaterUvs;
    public int[]? WaterIndices;
}

public interface IChunkMesher
{
    MeshBuildResult? BuildMeshData(ChunkSnapshot snapshot, TilePos chunkKey, int lodLevel);
}
