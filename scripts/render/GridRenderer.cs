using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using CowColonySim.Sim.Grid;
using GArray = Godot.Collections.Array;

namespace CowColonySim.Render;

public sealed partial class GridRenderer : Node3D
{
    private readonly Dictionary<TilePos, ChunkRenderSlot> _slots = new();
    private readonly ConcurrentQueue<(TilePos ChunkKey, MeshBuildResult? Result)> _completed = new();
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

        while (_completed.TryDequeue(out var pack))
        {
            var (chunkKey, result) = pack;
            if (!_slots.TryGetValue(chunkKey, out var slot)) continue;
            slot.InFlight = false;
            slot.MeshInstance.Mesh = result != null ? AssembleArrayMesh(result) : null;
            slot.UploadedRevision = result?.Revision ?? slot.RequestedRevision;
        }

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
            if (slot.InFlight) continue;
            if (chunk.Revision == slot.UploadedRevision) continue;

            var snap = chunk.Snapshot();
            slot.InFlight = true;
            slot.RequestedRevision = snap.Revision;
            var key = chunkKey;
            Task.Run(() =>
            {
                var r = _mesher.BuildMeshData(snap, 0);
                _completed.Enqueue((key, r));
            });
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

    private static ArrayMesh AssembleArrayMesh(MeshBuildResult r)
    {
        var arrays = new GArray();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = r.Verts;
        arrays[(int)Mesh.ArrayType.Normal] = r.Normals;
        arrays[(int)Mesh.ArrayType.Color] = r.Colors;
        arrays[(int)Mesh.ArrayType.Index] = r.Indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private sealed class ChunkRenderSlot
    {
        public MeshInstance3D MeshInstance = null!;
        public int UploadedRevision = -1;
        public int RequestedRevision = -1;
        public bool InFlight;
    }
}
