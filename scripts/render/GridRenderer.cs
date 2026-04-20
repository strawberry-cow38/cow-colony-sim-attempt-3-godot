using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using CowColonySim.Sim.Grid;
using GArray = Godot.Collections.Array;

namespace CowColonySim.Render;

public sealed partial class GridRenderer : Node3D
{
    // Chunk-distance cutoffs (chebyshev) for LOD selection.
    private const int Lod0Range = 4;   // ≤ 4 chunks: full voxel
    private const int Lod1Range = 16;  // ≤16 chunks: heightmap 1:1
    // else: heightmap 4:1 downsample.

    // Radial draw-distance cap (chebyshev chunks). Chunks outside this
    // radius have their MeshInstance hidden and skip meshing entirely.
    public static int MaxChunkDistance { get; set; } = 32;

    private readonly Dictionary<TilePos, ChunkRenderSlot> _slots = new();
    private readonly ConcurrentQueue<(TilePos ChunkKey, MeshBuildResult? Result, int LodRequested)> _completed = new();
    private readonly IChunkMesher _mesher = new NaiveChunkMesher();

    private SimHost? _simHost;
    private StandardMaterial3D? _material;

    public override void _Ready()
    {
        _simHost = GetNode<SimHost>("/root/SimHost");
        var tex = GD.Load<Texture2D>("res://textures/grass-atlas.jpg");
        _material = new StandardMaterial3D
        {
            AlbedoTexture = tex,
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
    }

    public override void _Process(double delta)
    {
        if (_simHost == null) return;

        while (_completed.TryDequeue(out var pack))
        {
            var (chunkKey, result, lodRequested) = pack;
            if (!_slots.TryGetValue(chunkKey, out var slot)) continue;
            slot.InFlight = false;
            slot.MeshInstance.Mesh = result != null ? AssembleArrayMesh(result) : null;
            slot.UploadedRevision = result?.Revision ?? slot.RequestedRevision;
            slot.CurrentLod = result?.LodLevel ?? lodRequested;
        }

        var cam = GetViewport()?.GetCamera3D();
        var camPos = cam?.GlobalPosition ?? Vector3.Zero;
        var camChunkX = Mathf.FloorToInt(camPos.X / (Chunk.Size * TileCoord.TileW));
        var camChunkZ = Mathf.FloorToInt(camPos.Z / (Chunk.Size * TileCoord.TileW));

        foreach (var kv in _simHost.Tiles.EnumerateChunks())
        {
            var chunkKey = kv.Key;
            var chunk = kv.Value;
            if (!_slots.TryGetValue(chunkKey, out var slot))
            {
                slot = new ChunkRenderSlot { MeshInstance = BuildInstance(chunkKey), CurrentLod = -1 };
                AddChild(slot.MeshInstance);
                _slots[chunkKey] = slot;
            }
            var dx = System.Math.Abs(chunkKey.X - camChunkX);
            var dz = System.Math.Abs(chunkKey.Z - camChunkZ);
            var d = System.Math.Max(dx, dz);

            if (d > MaxChunkDistance)
            {
                slot.MeshInstance.Visible = false;
                continue;
            }
            slot.MeshInstance.Visible = true;

            if (slot.InFlight) continue;

            var desiredLod = d <= Lod0Range ? 0 : d <= Lod1Range ? 1 : 2;

            var needsRemesh = slot.CurrentLod != desiredLod || chunk.Revision != slot.UploadedRevision;
            if (!needsRemesh) continue;

            var snap = chunk.Snapshot();
            slot.InFlight = true;
            slot.RequestedRevision = snap.Revision;
            var key = chunkKey;
            var lod = desiredLod;
            Task.Run(() =>
            {
                var r = _mesher.BuildMeshData(snap, key, lod);
                _completed.Enqueue((key, r, lod));
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
        arrays[(int)Mesh.ArrayType.TexUV] = r.Uvs;
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
        public int CurrentLod = -1;
        public bool InFlight;
    }
}
