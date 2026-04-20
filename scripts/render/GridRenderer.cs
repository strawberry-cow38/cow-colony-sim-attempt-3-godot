using Godot;
using System.Collections.Generic;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

public sealed partial class GridRenderer : Node3D
{
    private readonly Dictionary<TilePos, ChunkRenderSlot> _slots = new();
    private readonly IChunkMesher _mesher = new NaiveChunkMesher();

    private SimHost? _simHost;
    private StandardMaterial3D? _material;

    public override void _Ready()
    {
        _simHost = GetNode<SimHost>("/root/SimHost");
        _material = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            Roughness = 0.9f,
        };
    }

    public override void _Process(double delta)
    {
        if (_simHost == null) return;

        foreach (var kv in _simHost.Tiles.EnumerateChunks())
        {
            var chunkKey = kv.Key;
            var chunk = kv.Value;
            if (!_slots.TryGetValue(chunkKey, out var slot))
            {
                slot = new ChunkRenderSlot { MeshInstance = BuildInstance(chunkKey) };
                AddChild(slot.MeshInstance);
                _slots[chunkKey] = slot;
            }
            if (slot.LastRevision == chunk.Revision) continue;
            RebuildSlot(slot, chunk, chunkKey);
        }
    }

    private MeshInstance3D BuildInstance(TilePos chunkKey)
    {
        return new MeshInstance3D
        {
            Position = TileCoord.ChunkOrigin(chunkKey),
            MaterialOverride = _material,
        };
    }

    private void RebuildSlot(ChunkRenderSlot slot, Chunk chunk, TilePos chunkKey)
    {
        var snap = chunk.Snapshot();
        var mesh = _mesher.BuildMesh(snap, lodLevel: 0);
        slot.MeshInstance.Mesh = mesh;
        slot.LastRevision = snap.Revision;
    }

    private sealed class ChunkRenderSlot
    {
        public MeshInstance3D MeshInstance = null!;
        public int LastRevision = -1;
    }
}
