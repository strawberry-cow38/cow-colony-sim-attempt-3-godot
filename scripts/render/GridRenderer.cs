using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using CowColonySim.Sim.Grid;
using GArray = Godot.Collections.Array;

namespace CowColonySim.Render;

public sealed partial class GridRenderer : Node3D
{
    // Chebyshev chunk-distance cutoffs for LOD selection.
    private const int Tier0Range = 4;    // L0: per-chunk voxel
    private const int Tier1Range = 16;   // L1: per-chunk heightmap step=1
    private const int Tier3Range = 64;   // L3: G4 group (4x4 chunks) heightmap step=4
    // else L4: G8 group (8x8 chunks) heightmap step=8

    private const int Group4 = 4;
    private const int Group8 = 8;

    // Radial draw-distance cap (chebyshev chunks).
    public static int MaxChunkDistance { get; set; } = 128;

    private readonly Dictionary<TilePos, ChunkRenderSlot> _slots = new();
    private readonly Dictionary<TilePos, GroupRenderSlot> _g4Slots = new();
    private readonly Dictionary<TilePos, GroupRenderSlot> _g8Slots = new();

    private readonly ConcurrentQueue<(TilePos Key, MeshBuildResult? Result, int Lod)> _completedChunk = new();
    private readonly ConcurrentQueue<(TilePos Key, int GroupSize, MeshBuildResult? Result, int Lod, long MaskHash)> _completedGroup = new();

    private readonly NaiveChunkMesher _mesher = new();

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

        DrainCompleted();

        var cam = GetViewport()?.GetCamera3D();
        var camPos = cam?.GlobalPosition ?? Vector3.Zero;
        var camChunkX = Mathf.FloorToInt(camPos.X / (Chunk.Size * TileCoord.TileW));
        var camChunkZ = Mathf.FloorToInt(camPos.Z / (Chunk.Size * TileCoord.TileW));

        var g4Masks = new Dictionary<TilePos, List<TilePos>>();
        var g8Masks = new Dictionary<TilePos, List<TilePos>>();
        var perChunkTier = new Dictionary<TilePos, int>();

        foreach (var kv in _simHost.Tiles.EnumerateChunks())
        {
            var ck = kv.Key;
            var dx = System.Math.Abs(ck.X - camChunkX);
            var dz = System.Math.Abs(ck.Z - camChunkZ);
            var d = System.Math.Max(dx, dz);
            if (d > MaxChunkDistance) continue;
            if (d <= Tier1Range)
            {
                perChunkTier[ck] = d <= Tier0Range ? 0 : 1;
            }
            else if (d <= Tier3Range)
            {
                AddToBucket(g4Masks, GroupKey(ck, Group4), ck);
            }
            else
            {
                AddToBucket(g8Masks, GroupKey(ck, Group8), ck);
            }
        }

        UpdatePerChunkSlots(perChunkTier);
        UpdateGroupSlots(_g4Slots, g4Masks, Group4, lod: 3, step: 4);
        UpdateGroupSlots(_g8Slots, g8Masks, Group8, lod: 4, step: 8);
    }

    private void DrainCompleted()
    {
        while (_completedChunk.TryDequeue(out var pack))
        {
            var (key, result, lod) = pack;
            if (!_slots.TryGetValue(key, out var slot)) continue;
            slot.InFlight = false;
            slot.MeshInstance.Mesh = result != null ? AssembleArrayMesh(result) : null;
            slot.UploadedRevision = slot.RequestedRevision;
            slot.CurrentLod = result?.LodLevel ?? lod;
        }
        while (_completedGroup.TryDequeue(out var pack))
        {
            var (key, groupSize, result, lod, maskHash) = pack;
            var table = groupSize == Group4 ? _g4Slots : _g8Slots;
            if (!table.TryGetValue(key, out var slot)) continue;
            slot.InFlight = false;
            slot.MeshInstance.Mesh = result != null ? AssembleArrayMesh(result) : null;
            slot.UploadedRevision = slot.RequestedRevision;
            slot.UploadedMaskHash = maskHash;
            slot.CurrentLod = result?.LodLevel ?? lod;
        }
    }

    private void UpdatePerChunkSlots(Dictionary<TilePos, int> perChunkTier)
    {
        var active = new HashSet<TilePos>();
        foreach (var (chunkKey, tier) in perChunkTier)
        {
            active.Add(chunkKey);
            var chunk = _simHost!.Tiles.GetChunkOrNull(chunkKey);
            if (chunk == null) continue;
            if (!_slots.TryGetValue(chunkKey, out var slot))
            {
                slot = new ChunkRenderSlot { MeshInstance = BuildInstance(chunkKey), CurrentLod = -1 };
                AddChild(slot.MeshInstance);
                _slots[chunkKey] = slot;
            }
            slot.MeshInstance.Visible = true;
            if (slot.InFlight) continue;

            var nPosX = _simHost!.Tiles.GetChunkOrNull(new TilePos(chunkKey.X + 1, chunkKey.Y, chunkKey.Z));
            var nNegX = _simHost!.Tiles.GetChunkOrNull(new TilePos(chunkKey.X - 1, chunkKey.Y, chunkKey.Z));
            var nPosZ = _simHost!.Tiles.GetChunkOrNull(new TilePos(chunkKey.X, chunkKey.Y, chunkKey.Z + 1));
            var nNegZ = _simHost!.Tiles.GetChunkOrNull(new TilePos(chunkKey.X, chunkKey.Y, chunkKey.Z - 1));
            long combinedRev = chunk.Revision;
            if (tier >= 1)
            {
                combinedRev += nPosX?.Revision ?? 0;
                combinedRev += nNegX?.Revision ?? 0;
                combinedRev += nPosZ?.Revision ?? 0;
                combinedRev += nNegZ?.Revision ?? 0;
            }
            if (slot.CurrentLod == tier && slot.UploadedRevision == combinedRev) continue;

            var snap = chunk.Snapshot();
            ChunkSnapshot? snapPosX = tier >= 1 ? nPosX?.Snapshot() : null;
            ChunkSnapshot? snapNegX = tier >= 1 ? nNegX?.Snapshot() : null;
            ChunkSnapshot? snapPosZ = tier >= 1 ? nPosZ?.Snapshot() : null;
            ChunkSnapshot? snapNegZ = tier >= 1 ? nNegZ?.Snapshot() : null;
            slot.InFlight = true;
            slot.RequestedRevision = combinedRev;
            var key = chunkKey;
            var lod = tier;
            Task.Run(() =>
            {
                MeshBuildResult? r;
                if (lod == 0)
                    r = _mesher.BuildMeshData(snap, key, lod);
                else
                    r = _mesher.BuildChunkHeightmapWithBorders(snap, key, step: 1,
                        snapPosX, snapNegX, snapPosZ, snapNegZ);
                _completedChunk.Enqueue((key, r, lod));
            });
        }
        foreach (var kv in _slots)
        {
            if (!active.Contains(kv.Key)) kv.Value.MeshInstance.Visible = false;
        }
    }

    private void UpdateGroupSlots(
        Dictionary<TilePos, GroupRenderSlot> table,
        Dictionary<TilePos, List<TilePos>> masks,
        int groupSize, int lod, int step)
    {
        foreach (var (groupKey, chunkKeys) in masks)
        {
            if (!table.TryGetValue(groupKey, out var slot))
            {
                slot = new GroupRenderSlot { MeshInstance = BuildInstance(groupKey), CurrentLod = -1 };
                AddChild(slot.MeshInstance);
                table[groupKey] = slot;
            }
            slot.MeshInstance.Visible = true;
            if (slot.InFlight) continue;

            long maskHash = 0;
            long revHash = 0;
            foreach (var ck in chunkKeys)
            {
                var chunk = _simHost!.Tiles.GetChunkOrNull(ck);
                if (chunk == null) continue;
                var lx = ck.X - groupKey.X;
                var lz = ck.Z - groupKey.Z;
                unchecked
                {
                    maskHash = maskHash * 31 + ((lx * groupSize + lz) * 1024 + ck.Y + 1);
                    revHash += chunk.Revision;
                }
            }
            if (slot.CurrentLod == lod && slot.UploadedRevision == revHash && slot.UploadedMaskHash == maskHash) continue;

            var entries = new List<NaiveChunkMesher.GroupChunkEntry>(chunkKeys.Count);
            foreach (var ck in chunkKeys)
            {
                var chunk = _simHost!.Tiles.GetChunkOrNull(ck);
                if (chunk == null) continue;
                var lx = ck.X - groupKey.X;
                var lz = ck.Z - groupKey.Z;
                entries.Add(new NaiveChunkMesher.GroupChunkEntry(lx, lz, ck.Y, chunk.Snapshot()));
            }

            slot.InFlight = true;
            slot.RequestedRevision = revHash;
            var key = groupKey;
            var size = groupSize;
            var maskHashCaptured = maskHash;
            var lodCaptured = lod;
            var stepCaptured = step;
            Task.Run(() =>
            {
                var r = _mesher.BuildGroupMesh(entries, key, size, stepCaptured, lodCaptured);
                _completedGroup.Enqueue((key, size, r, lodCaptured, maskHashCaptured));
            });
        }
        foreach (var kv in table)
        {
            if (!masks.ContainsKey(kv.Key)) kv.Value.MeshInstance.Visible = false;
        }
    }

    private static TilePos GroupKey(TilePos chunkKey, int groupSize)
    {
        return new TilePos(FloorDiv(chunkKey.X, groupSize) * groupSize, 0, FloorDiv(chunkKey.Z, groupSize) * groupSize);
    }

    private static int FloorDiv(int a, int b) => (a / b) - (a % b < 0 ? 1 : 0);

    private static void AddToBucket(Dictionary<TilePos, List<TilePos>> buckets, TilePos key, TilePos chunkKey)
    {
        if (!buckets.TryGetValue(key, out var list))
        {
            list = new List<TilePos>(4);
            buckets[key] = list;
        }
        list.Add(chunkKey);
    }

    private MeshInstance3D BuildInstance(TilePos originChunkKey)
    {
        return new MeshInstance3D
        {
            Position = TileCoord.ChunkOrigin(originChunkKey),
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
        public long UploadedRevision = -1;
        public long RequestedRevision = -1;
        public int CurrentLod = -1;
        public bool InFlight;
    }

    private sealed class GroupRenderSlot
    {
        public MeshInstance3D MeshInstance = null!;
        public long UploadedRevision = -1;
        public long RequestedRevision = -1;
        public long UploadedMaskHash;
        public int CurrentLod = -1;
        public bool InFlight;
    }
}
