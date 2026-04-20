using Godot;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

public sealed class MeshBuildResult
{
    public Vector3[] Verts = null!;
    public Vector3[] Normals = null!;
    public Color[] Colors = null!;
    public int[] Indices = null!;
    public int Revision;
}

public interface IChunkMesher
{
    MeshBuildResult? BuildMeshData(ChunkSnapshot snapshot, int lodLevel);
}
