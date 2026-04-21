using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using CowColonySim.Sim.Grid;
using CowColonySim.UI;
using GArray = Godot.Collections.Array;

namespace CowColonySim.Render;

public sealed partial class GridRenderer : Node3D
{
    // Chebyshev chunk-distance cutoffs for LOD selection.
    private const int Tier0Range = 2;    // L0: per-chunk voxel
    private const int Tier1Range = 6;    // L1: per-chunk heightmap step=1
    private const int Tier3Range = 16;   // L3: G4 group (4x4 chunks) heightmap step=4
    private const int Tier4Range = 64;   // L4: G8 group (8x8 chunks) heightmap step=32
    // else L5: G16 group (16x16 chunks) heightmap step=64, skip cliffs under 6 tiles

    private const int Group4 = 4;
    private const int Group8 = 8;
    private const int Group16 = 16;

    // Radial draw-distance cap (chebyshev chunks).
    public static int MaxChunkDistance { get; set; } = 128;

    // When true, G16 slots render as a flat patch mesh driven by a vertex-
    // displacement shader sampling an R8 heightmap texture. CPU meshing is
    // skipped for that tier entirely. Flip at runtime before _Ready or via
    // settings menu — existing slots are not hot-rebuilt.
    public static bool GpuTerrainEnabled = true;

    private readonly Dictionary<TilePos, ChunkRenderSlot> _slots = new();
    private readonly Dictionary<TilePos, GroupRenderSlot> _g4Slots = new();
    private readonly Dictionary<TilePos, GroupRenderSlot> _g8Slots = new();
    private readonly Dictionary<TilePos, GroupRenderSlot> _g16Slots = new();

    private readonly ConcurrentQueue<(TilePos Key, MeshBuildResult? Result, int Lod)> _completedChunk = new();
    private readonly ConcurrentQueue<(TilePos Key, int GroupSize, MeshBuildResult? Result, int Lod, long MaskHash)> _completedGroup = new();
    private readonly ConcurrentQueue<(TilePos Key, int GroupSize, NaiveChunkMesher.HeightmapPatch? Patch, int Lod, long MaskHash)> _completedGpuGroup = new();

    private readonly NaiveChunkMesher _mesher = new();

    private SimHost? _simHost;
    private StandardMaterial3D? _material;
    private Shader? _terrainShader;
    private Texture2D? _grassTex;
    private ArrayMesh? _g8PatchMesh;
    private ArrayMesh? _g16PatchMesh;
    private const int G8CellsPerSide = 16;  // 12m per cell at 192m patch
    private const int G16CellsPerSide = 32; // 12m per cell at 384m patch

    // Classify cache. The 27k-chunk walk was ~1.4ms every frame even when
    // nothing moved. We cache the outputs and only redo the work when the
    // camera crosses a chunk boundary, the chunk set changes (paging or
    // worldgen), or the max draw distance slider moves.
    private int _cacheCamChunkX = int.MinValue;
    private int _cacheCamChunkZ = int.MinValue;
    private int _cacheChunkCount = -1;
    private int _cacheMaxDist = -1;
    private Dictionary<TilePos, int>? _cachePerChunkTier;
    private Dictionary<TilePos, List<TilePos>>? _cacheG4Masks;
    private Dictionary<TilePos, List<TilePos>>? _cacheG8Masks;
    private Dictionary<TilePos, List<TilePos>>? _cacheG16Masks;

    public override void _Ready()
    {
        _simHost = GetNode<SimHost>("/root/SimHost");
        _grassTex = GD.Load<Texture2D>("res://textures/grass-atlas.jpg");
        _material = new StandardMaterial3D
        {
            AlbedoTexture = _grassTex,
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        if (GpuTerrainEnabled)
        {
            _terrainShader = GD.Load<Shader>("res://scripts/render/shaders/terrain_heightmap.gdshader");
            var g16Width = Group16 * Chunk.Size * TileCoord.TileW;
            var g8Width  = Group8  * Chunk.Size * TileCoord.TileW;
            _g16PatchMesh = GpuTerrain.BuildPatchMesh(G16CellsPerSide, g16Width);
            // G8 is stepped — matches blocky look of L0/L1/G4 at the tier boundary.
            _g8PatchMesh  = GpuTerrain.BuildPatchMeshStepped(G8CellsPerSide, g8Width);
        }
    }

    public override void _Process(double delta)
    {
        if (_simHost == null) return;

        Profiler.Begin("Drain");
        DrainCompleted();
        Profiler.End("Drain");

        Profiler.Begin("Classify");
        var cam = GetViewport()?.GetCamera3D();
        var camPos = cam?.GlobalPosition ?? Vector3.Zero;
        var camChunkX = Mathf.FloorToInt(camPos.X / (Chunk.Size * TileCoord.TileW));
        var camChunkZ = Mathf.FloorToInt(camPos.Z / (Chunk.Size * TileCoord.TileW));

        var chunkCount = _simHost.Tiles.ChunkCount;
        var cacheHit = _cachePerChunkTier != null
            && _cacheCamChunkX == camChunkX
            && _cacheCamChunkZ == camChunkZ
            && _cacheChunkCount == chunkCount
            && _cacheMaxDist == MaxChunkDistance;

        Dictionary<TilePos, int> perChunkTier;
        Dictionary<TilePos, List<TilePos>> g4Masks, g8Masks, g16Masks;
        if (cacheHit)
        {
            perChunkTier = _cachePerChunkTier!;
            g4Masks = _cacheG4Masks!;
            g8Masks = _cacheG8Masks!;
            g16Masks = _cacheG16Masks!;
        }
        else
        {
            perChunkTier = new Dictionary<TilePos, int>();
            g4Masks = new Dictionary<TilePos, List<TilePos>>();
            g8Masks = new Dictionary<TilePos, List<TilePos>>();
            g16Masks = new Dictionary<TilePos, List<TilePos>>();

            var camCellX = FloorDiv(camChunkX, Cell.SizeChunks);
            var camCellZ = FloorDiv(camChunkZ, Cell.SizeChunks);
            var cellRange = (MaxChunkDistance + Cell.SizeChunks - 1) / Cell.SizeChunks;
            for (var cx = camCellX - cellRange; cx <= camCellX + cellRange; cx++)
            for (var cz = camCellZ - cellRange; cz <= camCellZ + cellRange; cz++)
            {
                var chunks = _simHost.Tiles.GetChunksInCell(new CellKey(cx, cz));
                if (chunks == null) continue;
                for (var i = 0; i < chunks.Count; i++)
                {
                    var ck = chunks[i];
                    var dx = System.Math.Abs(ck.X - camChunkX);
                    var dz = System.Math.Abs(ck.Z - camChunkZ);
                    var d = System.Math.Max(dx, dz);
                    if (d > MaxChunkDistance) continue;

                    var g4Key  = GroupKey(ck, Group4);
                    var g8Key  = GroupKey(ck, Group8);
                    var g16Key = GroupKey(ck, Group16);
                    var g4d  = GroupMinChebyshev(g4Key,  Group4,  camChunkX, camChunkZ);
                    var g8d  = GroupMinChebyshev(g8Key,  Group8,  camChunkX, camChunkZ);
                    var g16d = GroupMinChebyshev(g16Key, Group16, camChunkX, camChunkZ);

                    if (g16d > Tier4Range)        AddToBucket(g16Masks, g16Key, ck);
                    else if (g8d  > Tier3Range)   AddToBucket(g8Masks,  g8Key,  ck);
                    else if (g4d  > Tier1Range)   AddToBucket(g4Masks,  g4Key,  ck);
                    else                          perChunkTier[ck] = d <= Tier0Range ? 0 : 1;
                }
            }

            _cachePerChunkTier = perChunkTier;
            _cacheG4Masks = g4Masks;
            _cacheG8Masks = g8Masks;
            _cacheG16Masks = g16Masks;
            _cacheCamChunkX = camChunkX;
            _cacheCamChunkZ = camChunkZ;
            _cacheChunkCount = chunkCount;
            _cacheMaxDist = MaxChunkDistance;
        }
        Profiler.End("Classify");

        Profiler.Begin("PerChunk");
        UpdatePerChunkSlots(perChunkTier);
        Profiler.End("PerChunk");

        Profiler.Begin("Groups");
        UpdateGroupSlots(_g4Slots, g4Masks, Group4, lod: 3, step: 4, cliffMinDelta: 1);
        if (GpuTerrainEnabled)
        {
            UpdateGpuGroupSlots(_g8Slots, g8Masks, Group8, lod: 4);
            UpdateGpuGroupSlots(_g16Slots, g16Masks, Group16, lod: 5);
        }
        else
        {
            UpdateGroupSlots(_g8Slots, g8Masks, Group8, lod: 4, step: 32, cliffMinDelta: 3);
            UpdateGroupSlots(_g16Slots, g16Masks, Group16, lod: 5, step: 64, cliffMinDelta: 6);
        }
        Profiler.End("Groups");

        Profiler.SetCounter("L0+L1 slots", _slots.Count);
        Profiler.SetCounter("G4 slots", _g4Slots.Count);
        Profiler.SetCounter("G8 slots", _g8Slots.Count);
        Profiler.SetCounter("G16 slots", _g16Slots.Count);
        Profiler.SetCounter("InFlight", CountInFlight());

        long l0 = 0, l1 = 0;
        foreach (var kv in perChunkTier) { if (kv.Value == 0) l0++; else l1++; }
        Profiler.SetCounter("L0 classed", l0);
        Profiler.SetCounter("L1 classed", l1);
        Profiler.SetCounter("G4 classed", g4Masks.Count);
        Profiler.SetCounter("G8 classed", g8Masks.Count);
        Profiler.SetCounter("G16 classed", g16Masks.Count);

        long visible = 0;
        foreach (var kv in _slots) if (kv.Value.MeshInstance.Visible && kv.Value.MeshInstance.Mesh != null) visible++;
        foreach (var kv in _g4Slots) if (kv.Value.MeshInstance.Visible && kv.Value.MeshInstance.Mesh != null) visible++;
        foreach (var kv in _g8Slots) if (kv.Value.MeshInstance.Visible && kv.Value.MeshInstance.Mesh != null) visible++;
        foreach (var kv in _g16Slots) if (kv.Value.MeshInstance.Visible && kv.Value.MeshInstance.Mesh != null) visible++;
        Profiler.SetCounter("Visible slots", visible);

        Profiler.SetCounter("Chunks mem", _simHost.Tiles.ChunkCount);
        long cellsMem = 0;
        foreach (var _ in _simHost.Tiles.InMemoryCells) cellsMem++;
        Profiler.SetCounter("Cells mem", cellsMem);
        Profiler.SetCounter("Cell states", _simHost.Tiles.CellStates.Count);
        Profiler.SetCounter("Page save IF", _simHost.Paging.SaveInFlightCount);
        Profiler.SetCounter("Page load IF", _simHost.Paging.LoadInFlightCount);
    }

    private long CountInFlight()
    {
        long n = 0;
        foreach (var kv in _slots) if (kv.Value.InFlight) n++;
        foreach (var kv in _g4Slots) if (kv.Value.InFlight) n++;
        foreach (var kv in _g8Slots) if (kv.Value.InFlight) n++;
        foreach (var kv in _g16Slots) if (kv.Value.InFlight) n++;
        return n;
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
            Profiler.IncRate(lod == 0 ? "L0 up/s" : "L1 up/s");
        }
        while (_completedGroup.TryDequeue(out var pack))
        {
            var (key, groupSize, result, lod, maskHash) = pack;
            var table = groupSize == Group4 ? _g4Slots : groupSize == Group8 ? _g8Slots : _g16Slots;
            if (!table.TryGetValue(key, out var slot)) continue;
            slot.InFlight = false;
            slot.MeshInstance.Mesh = result != null ? AssembleArrayMesh(result) : null;
            slot.UploadedRevision = slot.RequestedRevision;
            slot.UploadedMaskHash = maskHash;
            slot.CurrentLod = result?.LodLevel ?? lod;
            Profiler.IncRate(groupSize == Group4 ? "G4 up/s" : groupSize == Group8 ? "G8 up/s" : "G16 up/s");
        }
        while (_completedGpuGroup.TryDequeue(out var pack))
        {
            var (key, groupSize, patch, lod, maskHash) = pack;
            var table = groupSize == Group16 ? _g16Slots : _g8Slots;
            if (!table.TryGetValue(key, out var slot)) continue;
            slot.InFlight = false;
            if (patch == null)
            {
                slot.MeshInstance.Mesh = null;
            }
            else
            {
                var tex = GpuTerrain.BuildHeightmapTexture(
                    patch.Heights, patch.SizeX, patch.SizeZ, patch.MaxHeightMeters);
                slot.MeshInstance.Mesh = groupSize == Group8 ? _g8PatchMesh : _g16PatchMesh;
                slot.MeshInstance.MaterialOverride = BuildTerrainMaterial(groupSize, patch.MaxHeightMeters, tex);
            }
            slot.UploadedRevision = slot.RequestedRevision;
            slot.UploadedMaskHash = maskHash;
            slot.CurrentLod = patch?.LodLevel ?? lod;
            Profiler.IncRate(groupSize == Group8 ? "G8gpu up/s" : "G16gpu up/s");
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
            // L0 voxel mesher also needs +Y/-Y neighbors so tops/bottoms at the
            // chunk-Y boundary don't double-emit faces when the column is solid
            // across stacked chunks.
            var nPosY = tier == 0 ? _simHost!.Tiles.GetChunkOrNull(new TilePos(chunkKey.X, chunkKey.Y + 1, chunkKey.Z)) : null;
            var nNegY = tier == 0 ? _simHost!.Tiles.GetChunkOrNull(new TilePos(chunkKey.X, chunkKey.Y - 1, chunkKey.Z)) : null;
            long combinedRev = chunk.Revision;
            combinedRev += nPosX?.Revision ?? 0;
            combinedRev += nNegX?.Revision ?? 0;
            combinedRev += nPosZ?.Revision ?? 0;
            combinedRev += nNegZ?.Revision ?? 0;
            combinedRev += nPosY?.Revision ?? 0;
            combinedRev += nNegY?.Revision ?? 0;
            if (slot.CurrentLod == tier && slot.UploadedRevision == combinedRev) continue;

            var snap = chunk.Snapshot();
            ChunkSnapshot? snapPosX = nPosX?.Snapshot();
            ChunkSnapshot? snapNegX = nNegX?.Snapshot();
            ChunkSnapshot? snapPosZ = nPosZ?.Snapshot();
            ChunkSnapshot? snapNegZ = nNegZ?.Snapshot();
            ChunkSnapshot? snapPosY = nPosY?.Snapshot();
            ChunkSnapshot? snapNegY = nNegY?.Snapshot();
            slot.InFlight = true;
            slot.RequestedRevision = combinedRev;
            var key = chunkKey;
            var lod = tier;
            Task.Run(() =>
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                MeshBuildResult? r;
                if (lod == 0)
                    r = _mesher.BuildFullVoxelWithNeighbors(snap, key,
                        snapPosX, snapNegX, snapPosY, snapNegY, snapPosZ, snapNegZ);
                else
                    r = _mesher.BuildChunkHeightmapWithBorders(snap, key, step: 1,
                        snapPosX, snapNegX, snapPosZ, snapNegZ);
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                Profiler.RecordMs(lod == 0 ? "Build L0" : "Build L1", ms);
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
        int groupSize, int lod, int step, int cliffMinDelta)
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
            var cliffMinCaptured = cliffMinDelta;
            Task.Run(() =>
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var r = _mesher.BuildGroupMesh(entries, key, size, stepCaptured, lodCaptured, cliffMinCaptured);
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                Profiler.RecordMs(size == Group4 ? "Build G4" : size == Group8 ? "Build G8" : "Build G16", ms);
                _completedGroup.Enqueue((key, size, r, lodCaptured, maskHashCaptured));
            });
        }
        foreach (var kv in table)
        {
            if (!masks.ContainsKey(kv.Key)) kv.Value.MeshInstance.Visible = false;
        }
    }

    private void UpdateGpuGroupSlots(
        Dictionary<TilePos, GroupRenderSlot> table,
        Dictionary<TilePos, List<TilePos>> masks,
        int groupSize, int lod)
    {
        var patchMesh = groupSize == Group8 ? _g8PatchMesh! : _g16PatchMesh!;
        foreach (var (groupKey, chunkKeys) in masks)
        {
            if (!table.TryGetValue(groupKey, out var slot))
            {
                slot = new GroupRenderSlot
                {
                    MeshInstance = BuildGpuInstance(groupKey, patchMesh),
                    CurrentLod = -1,
                };
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
            Task.Run(() =>
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var patch = _mesher.BuildGroupHeightmapPatch(entries, size, lodCaptured);
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                Profiler.RecordMs(size == Group8 ? "Build G8 GPU" : "Build G16 GPU", ms);
                _completedGpuGroup.Enqueue((key, size, patch, lodCaptured, maskHashCaptured));
            });
        }
        foreach (var kv in table)
        {
            if (!masks.ContainsKey(kv.Key)) kv.Value.MeshInstance.Visible = false;
        }
    }

    private MeshInstance3D BuildGpuInstance(TilePos originChunkKey, ArrayMesh patchMesh)
    {
        return new MeshInstance3D
        {
            Position = TileCoord.ChunkOrigin(originChunkKey),
            Mesh = patchMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private ShaderMaterial BuildTerrainMaterial(int groupSize, float maxHeightMeters, ImageTexture heightmap)
    {
        var patchWidth = groupSize * Chunk.Size * TileCoord.TileW;
        var m = new ShaderMaterial { Shader = _terrainShader };
        m.SetShaderParameter("heightmap", heightmap);
        m.SetShaderParameter("albedo_tex", _grassTex);
        m.SetShaderParameter("patch_width_m", patchWidth);
        m.SetShaderParameter("height_scale_m", System.Math.Max(1f, maxHeightMeters));
        return m;
    }

    private static TilePos GroupKey(TilePos chunkKey, int groupSize)
    {
        return new TilePos(FloorDiv(chunkKey.X, groupSize) * groupSize, 0, FloorDiv(chunkKey.Z, groupSize) * groupSize);
    }

    private static int GroupMinChebyshev(TilePos groupKey, int size, int camX, int camZ)
    {
        var xMin = groupKey.X;
        var xMax = groupKey.X + size - 1;
        var zMin = groupKey.Z;
        var zMax = groupKey.Z + size - 1;
        var dx = System.Math.Max(0, System.Math.Max(camX - xMax, xMin - camX));
        var dz = System.Math.Max(0, System.Math.Max(camZ - zMax, zMin - camZ));
        return System.Math.Max(dx, dz);
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
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
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
