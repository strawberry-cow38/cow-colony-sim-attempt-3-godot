using Godot;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

public interface IChunkMesher
{
    ArrayMesh? BuildMesh(ChunkSnapshot snapshot, int lodLevel);
}
