using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using CowColonySim.Sim.Grid;
using CowColonySim.UI;
using GArray = Godot.Collections.Array;

namespace CowColonySim.Render;

public sealed partial class GridRenderer : Node3D
{
    // Euclidean (cylinder) chunk-distance cutoffs for LOD selection. Switching
    // from chebyshev (square) to euclidean (circle) drops the four corner
    // triangles of each ring — roughly 21% fewer chunks considered at any tier.
    public const int Tier0Range = 2;    // L0: per-chunk voxel
    public const int Tier1Range = 6;    // L1: per-chunk heightmap step=1
    public const int Tier3Range = 16;   // L3: G4 group (4x4 chunks) heightmap step=4
    // else L4: G8 group (8x8 chunks) heightmap step=32

    private const int Group4 = 4;
    private const int Group8 = 8;

    // Radial draw-distance cap (chunks). Matches G8 far edge; distance fog
    // hides the horizon so no coarser tier is needed.
    public static int MaxChunkDistance { get; set; } = 64;

    public static bool GpuTerrainEnabled = true;
    // Diagnostic A/B toggle for the old voxel/stepped terrain at L0+L1.
    // Default off post-P1d: voxel L0 and stepped L1 no longer emit
    // Floor/Sand/Water (HeightmapTerrainMesher owns the ground), so flipping
    // this on mostly re-reveals rock columns that terrain obscured. G4/G8
    // far tiers are untouched — they still emit terrain for distance cover.
    public static bool ShowVoxelTerrain = false;

    // Every non-LIVE tier renders this far below its true Y. Keeps coarse
    // tiers from Z-fighting with the L0 voxel mesh across the fade band
    // where both are drawn — LIVE always wins the depth test. 15cm is
    // well above depth-buffer precision at typical view distances but
    // small enough that the dither-crossfade band stays visually flush
    // (1m was > one full 0.75m tile and shimmered at the seam).
    private const float LodYSink = 0.15f;

    private readonly Dictionary<TilePos, ChunkRenderSlot> _slots = new();
    private readonly Dictionary<TilePos, GroupRenderSlot> _g4Slots = new();
    private readonly Dictionary<TilePos, GroupRenderSlot> _g8Slots = new();
    private readonly Dictionary<(int cx, int cz), TerrainRenderSlot> _terrainSlots = new();

    private readonly ConcurrentQueue<(TilePos Key, MeshBuildResult? Result, int Lod)> _completedChunk = new();
    private readonly ConcurrentQueue<(TilePos Key, int GroupSize, MeshBuildResult? Result, int Lod, long MaskHash)> _completedGroup = new();
    private readonly ConcurrentQueue<(TilePos Key, int GroupSize, NaiveChunkMesher.HeightmapPatch? Patch, int Lod, long MaskHash)> _completedGpuGroup = new();
    private readonly ConcurrentQueue<((int cx, int cz) Key, MeshBuildResult? Result, long RevHash)> _completedTerrain = new();

    private readonly NaiveChunkMesher _mesher = new();
    private readonly HeightmapTerrainMesher _terrainMesher = new();

    // Tiny Y offset so the new corner-heightmap mesh renders just above the
    // L0 voxel tops during the P1b A/B — lets master eyeball smooth slopes
    // against stepped cubes. Drops out when the voxel mesher stops emitting
    // terrain kinds in P1c.
    private const float TerrainYBias = 0.02f;

    private SimHost? _simHost;
    private StandardMaterial3D? _material;
    private StandardMaterial3D? _waterMaterial;
    private Shader? _terrainShader;
    private Texture2D? _grassTex;
    private ArrayMesh? _g4PatchMesh;
    private ArrayMesh? _g8PatchMesh;
    private const int G4CellsPerSide = 16;  // 6m per cell at 96m patch (matches CPU step=4)
    private const int G8CellsPerSide = 16;  // 12m per cell at 192m patch

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

    public override void _Ready()
    {
        _simHost = GetNode<SimHost>("/root/SimHost");
        _simHost.WorldRegenerated += OnWorldRegenerated;
        _grassTex = GD.Load<Texture2D>("res://textures/grass-atlas.jpg");
        _material = new StandardMaterial3D
        {
            AlbedoTexture = _grassTex,
            VertexColorUseAsAlbedo = true,
            Roughness = 0.95f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        // Dedicated water material. Kept on its own ArrayMesh surface so a
        // single shared transparent material doesn't punt every opaque
        // terrain triangle through the alpha queue.
        _waterMaterial = new StandardMaterial3D
        {
            AlbedoTexture = _grassTex,
            VertexColorUseAsAlbedo = true,
            Transparency = BaseMaterial3D.TransparencyEnum.AlphaDepthPrePass,
            Roughness = 0.4f,
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
        };
        if (GpuTerrainEnabled)
        {
            _terrainShader = GD.Load<Shader>("res://scripts/render/shaders/terrain_heightmap.gdshader");
            var g8Width  = Group8  * Chunk.Size * TileCoord.TileW;
            var g4Width  = Group4  * Chunk.Size * TileCoord.TileW;
            // G8 and G4 are stepped — matches blocky look of L0/L1 at tier boundaries.
            _g8PatchMesh  = GpuTerrain.BuildPatchMeshStepped(G8CellsPerSide, g8Width, Group8 * Chunk.Size);
            _g4PatchMesh  = GpuTerrain.BuildPatchMeshStepped(G4CellsPerSide, g4Width, Group4 * Chunk.Size);

            if (!RenderingServer.GlobalShaderParameterGetList().Any(n => n.ToString() == "gimbal_pos"))
            {
                RenderingServer.GlobalShaderParameterAdd(
                    "gimbal_pos",
                    RenderingServer.GlobalShaderParameterType.Vec3,
                    Vector3.Zero);
            }
        }
    }

    public override void _Process(double delta)
    {
        if (_simHost == null) return;

        _frameDispatchChunk = 0;
        _frameDispatchG4 = 0;
        _frameDispatchG8 = 0;
        _frameDispatchTerrain = 0;

        Profiler.Begin("Drain");
        DrainCompleted();
        Profiler.End("Drain");

        Profiler.Begin("Classify");
        var cam = GetViewport()?.GetCamera3D();
        // Follow the orbit pivot, not the camera transform, so yaw/pitch/zoom
        // don't thrash the LOD classifier. Only pan (Target move) crosses a
        // chunk boundary and retriggers rebuilds.
        var focus = cam is OrbitCamera orbit ? orbit.Target : cam?.GlobalPosition ?? Vector3.Zero;
        // Feeds the terrain shader's per-fragment fade so coarse tiers dither
        // against the gimbal, not the camera — zoom/pitch no longer shift the
        // boundary, and the fade reads each fragment's real world position
        // instead of the mesh origin.
        if (GpuTerrainEnabled)
            RenderingServer.GlobalShaderParameterSet("gimbal_pos", focus);
        var camChunkX = Mathf.FloorToInt(focus.X / (Chunk.Size * TileCoord.TileW));
        var camChunkZ = Mathf.FloorToInt(focus.Z / (Chunk.Size * TileCoord.TileW));

        var chunkCount = _simHost.Tiles.ChunkCount;
        // 1-chunk hysteresis: cache hits anywhere in a 3×3 chunk window around
        // the cached cam position. Halves the Classify rebuild rate on a
        // steady pan. The ±1 overlap band in Classify absorbs the stale-by-up-
        // to-one-chunk classification before a visible LOD seam appears.
        var dxChunks = camChunkX - _cacheCamChunkX;
        var dzChunks = camChunkZ - _cacheCamChunkZ;
        var cacheHit = _cachePerChunkTier != null
            && _cacheCamChunkX != int.MinValue
            && System.Math.Abs(dxChunks) <= 1
            && System.Math.Abs(dzChunks) <= 1
            && _cacheChunkCount == chunkCount
            && _cacheMaxDist == MaxChunkDistance;

        Dictionary<TilePos, int> perChunkTier;
        Dictionary<TilePos, List<TilePos>> g4Masks, g8Masks;
        if (cacheHit)
        {
            perChunkTier = _cachePerChunkTier!;
            g4Masks = _cacheG4Masks!;
            g8Masks = _cacheG8Masks!;
        }
        else
        {
            perChunkTier = new Dictionary<TilePos, int>();
            g4Masks = new Dictionary<TilePos, List<TilePos>>();
            g8Masks = new Dictionary<TilePos, List<TilePos>>();

            var camCellX = FloorDiv(camChunkX, Cell.SizeChunks);
            var camCellZ = FloorDiv(camChunkZ, Cell.SizeChunks);
            var cellRange = (MaxChunkDistance + Cell.SizeChunks - 1) / Cell.SizeChunks;
            long maxDistSq = (long)MaxChunkDistance * MaxChunkDistance;
            long tier0Sq = (long)Tier0Range * Tier0Range;
            long tier1Sq = (long)Tier1Range * Tier1Range;
            // Extend LIVE two chunks past tier1 so L1 meshes overlap the band
            // where G4's fade-in has already reached full opacity. Without the
            // overlap, L1 ends sharply at tier1m and any height disagreement
            // between L1's per-tile voxel cliffs and G4's per-cell (4×4 tile
            // avg) cliffs punches a visible crack. A 2-chunk overlap also
            // masks the camera-pan pop when a freshly classified L1 chunk
            // replaces the G4 surface — the swap happens inside the overlap
            // band where the eye is already reading fog, so it reads as a
            // soft detail-add instead of a sharp geometry flip.
            long tier1OuterSq = (long)(Tier1Range + 2) * (Tier1Range + 2);
            // Overlap bands. L1/G4 boundary: G4 extends 1 chunk INTO L1
            // territory so it can fade in where L1 covers it (tier1InnerSq).
            // G4/G8 boundary: G4's fade_out covers [tier3, tier3+1 chunk], so
            // Classify extends G4 groups a few chunks FURTHER (Prewarm) to let
            // the async mesh builder finish before the camera pans into the
            // fade band — otherwise a new G4 group entering range renders a
            // few frames late and leaves a visible "pop" where G8 alone was
            // still in its fade-in partial state. Same trick on G8's far-side
            // entry so G8 is built while it's still inside tier3 interior.
            const int Prewarm = 3;
            long tier1InnerSq = (long)(Tier1Range - 1) * (Tier1Range - 1);
            long tier3OuterPrewarmSq = (long)(Tier3Range + Prewarm) * (Tier3Range + Prewarm);
            long tier3InnerPrewarmSq = (long)(Tier3Range - Prewarm) * (Tier3Range - Prewarm);
            for (var cx = camCellX - cellRange; cx <= camCellX + cellRange; cx++)
            for (var cz = camCellZ - cellRange; cz <= camCellZ + cellRange; cz++)
            {
                var chunks = _simHost.Tiles.GetChunksInCell(new CellKey(cx, cz));
                if (chunks == null) continue;
                for (var i = 0; i < chunks.Count; i++)
                {
                    var ck = chunks[i];
                    long dx = ck.X - camChunkX;
                    long dz = ck.Z - camChunkZ;
                    long dSq = dx * dx + dz * dz;
                    if (dSq > maxDistSq) continue;

                    var g4Key  = GroupKey(ck, Group4);
                    var g8Key  = GroupKey(ck, Group8);
                    long g4MinSq  = GroupMinDistSq(g4Key, Group4, camChunkX, camChunkZ);
                    long g4MaxSq  = GroupMaxDistSq(g4Key, Group4, camChunkX, camChunkZ);
                    long g8MinSq  = GroupMinDistSq(g8Key, Group8, camChunkX, camChunkZ);
                    long g8MaxSq  = GroupMaxDistSq(g8Key, Group8, camChunkX, camChunkZ);

                    if (dSq <= tier1OuterSq)
                        perChunkTier[ck] = dSq <= tier0Sq ? 0 : 1;
                    // Group needed if ANY chunk in it falls past the tier's fade-in
                    // edge (use MAX dist); cull if WHOLLY past the coarse tier's end
                    // (use MIN dist). Using MIN for both caused straddling groups
                    // (near corner in close zone, far corner past tier1) to be
                    // skipped entirely, leaving uncovered holes between L1 and G4.
                    if (g4MaxSq > tier1InnerSq && g4MinSq <= tier3OuterPrewarmSq)
                        AddToBucket(g4Masks, g4Key, ck);
                    if (g8MaxSq > tier3InnerPrewarmSq)
                        AddToBucket(g8Masks, g8Key, ck);
                }
            }

            _cachePerChunkTier = perChunkTier;
            _cacheG4Masks = g4Masks;
            _cacheG8Masks = g8Masks;
            _cacheCamChunkX = camChunkX;
            _cacheCamChunkZ = camChunkZ;
            _cacheChunkCount = chunkCount;
            _cacheMaxDist = MaxChunkDistance;
        }
        Profiler.End("Classify");

        Profiler.Begin("PerChunk");
        UpdatePerChunkSlots(perChunkTier);
        Profiler.End("PerChunk");

        Profiler.Begin("Terrain");
        UpdateTerrainSlots(perChunkTier);
        Profiler.End("Terrain");

        Profiler.Begin("Groups");
        if (GpuTerrainEnabled)
        {
            UpdateGpuGroupSlots(_g4Slots, g4Masks, Group4, lod: 3);
            UpdateGpuGroupSlots(_g8Slots, g8Masks, Group8, lod: 4);
        }
        else
        {
            UpdateGroupSlots(_g4Slots, g4Masks, Group4, lod: 3, step: 4,  cliffMinDelta: 1);
            UpdateGroupSlots(_g8Slots, g8Masks, Group8, lod: 4, step: 32, cliffMinDelta: 3);
        }
        Profiler.End("Groups");

        Profiler.SetCounter("L0+L1 slots", _slots.Count);
        Profiler.SetCounter("G4 slots", _g4Slots.Count);
        Profiler.SetCounter("G8 slots", _g8Slots.Count);
        Profiler.SetCounter("Terrain slots", _terrainSlots.Count);
        Profiler.SetCounter("InFlight", CountInFlight());

        long l0 = 0, l1 = 0;
        foreach (var kv in perChunkTier) { if (kv.Value == 0) l0++; else l1++; }
        Profiler.SetCounter("L0 classed", l0);
        Profiler.SetCounter("L1 classed", l1);
        Profiler.SetCounter("G4 classed", g4Masks.Count);
        Profiler.SetCounter("G8 classed", g8Masks.Count);

        long visible = 0;
        foreach (var kv in _slots) if (kv.Value.MeshInstance.Visible && kv.Value.MeshInstance.Mesh != null) visible++;
        foreach (var kv in _g4Slots) if (kv.Value.MeshInstance.Visible && kv.Value.MeshInstance.Mesh != null) visible++;
        foreach (var kv in _g8Slots) if (kv.Value.MeshInstance.Visible && kv.Value.MeshInstance.Mesh != null) visible++;
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
        foreach (var kv in _terrainSlots) if (kv.Value.InFlight) n++;
        return n;
    }

    // Per-frame budgets. During a big pan hundreds of chunks can complete in
    // a single frame; draining them all stalls the main thread on
    // AssembleArrayMesh + heightmap texture upload. Keep the budget tight so
    // the pan stays smooth — leftover work surfaces next frame.
    private const int DrainChunkBudget = 4;
    private const int DrainGroupBudget = 2;
    private const int DrainTerrainBudget = 4;
    private const int DispatchChunkBudget = 4;
    // Per-group-size budget so a G4 burst can't starve G8 (or vice versa)
    // when both need rebuilds on the same pan.
    private const int DispatchGroupBudget = 2;
    private const int DispatchTerrainBudget = 4;
    private int _frameDispatchChunk;
    private int _frameDispatchG4;
    private int _frameDispatchG8;
    private int _frameDispatchTerrain;

    private void DrainCompleted()
    {
        var drained = 0;
        while (drained < DrainChunkBudget && _completedChunk.TryDequeue(out var pack))
        {
            var (key, result, lod) = pack;
            if (!_slots.TryGetValue(key, out var slot)) continue;
            slot.InFlight = false;
            slot.MeshInstance.Mesh = result != null ? AssembleArrayMesh(result) : null;
            var effectiveLod = result?.LodLevel ?? lod;
            // L0 renders at true Y; L1 sinks under LIVE to kill Z-fighting in
            // the overlap band. Update Position here because a single slot
            // flips tier as the camera moves.
            var origin = TileCoord.ChunkOrigin(key);
            var sinkY = effectiveLod >= 1 ? LodYSink : 0f;
            slot.MeshInstance.Position = new Vector3(origin.X, origin.Y - sinkY, origin.Z);
            slot.UploadedRevision = slot.RequestedRevision;
            slot.CurrentLod = effectiveLod;
            Profiler.IncRate(lod == 0 ? "L0 up/s" : "L1 up/s");
            drained++;
        }
        drained = 0;
        while (drained < DrainGroupBudget && _completedGroup.TryDequeue(out var pack))
        {
            var (key, groupSize, result, lod, maskHash) = pack;
            var table = groupSize == Group4 ? _g4Slots : _g8Slots;
            if (!table.TryGetValue(key, out var slot)) continue;
            slot.InFlight = false;
            slot.MeshInstance.Mesh = result != null ? AssembleArrayMesh(result) : null;
            slot.UploadedRevision = slot.RequestedRevision;
            slot.UploadedMaskHash = maskHash;
            slot.CurrentLod = result?.LodLevel ?? lod;
            Profiler.IncRate(groupSize == Group4 ? "G4 up/s" : "G8 up/s");
            drained++;
        }
        drained = 0;
        while (drained < DrainGroupBudget && _completedGpuGroup.TryDequeue(out var pack))
        {
            var (key, groupSize, patch, lod, maskHash) = pack;
            var table = groupSize == Group4 ? _g4Slots : _g8Slots;
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
                var kindTex = GpuTerrain.BuildKindmapTexture(
                    patch.Kinds, patch.SizeX, patch.SizeZ);
                slot.MeshInstance.Mesh = groupSize == Group4 ? _g4PatchMesh : _g8PatchMesh;
                slot.MeshInstance.MaterialOverride = BuildTerrainMaterial(groupSize, patch.MaxHeightMeters, tex, kindTex);
            }
            slot.UploadedRevision = slot.RequestedRevision;
            slot.UploadedMaskHash = maskHash;
            slot.CurrentLod = patch?.LodLevel ?? lod;
            Profiler.IncRate(groupSize == Group4 ? "G4gpu up/s" : "G8gpu up/s");
            drained++;
        }
        drained = 0;
        while (drained < DrainTerrainBudget && _completedTerrain.TryDequeue(out var pack))
        {
            var (key, result, revHash) = pack;
            if (!_terrainSlots.TryGetValue(key, out var slot)) continue;
            slot.InFlight = false;
            slot.MeshInstance.Mesh = result != null ? AssembleArrayMesh(result) : null;
            slot.UploadedRevision = revHash;
            Profiler.IncRate("Terrain up/s");
            drained++;
        }
    }

    private void UpdatePerChunkSlots(Dictionary<TilePos, int> perChunkTier)
    {
        var active = new HashSet<TilePos>();
        var mutationTick = _simHost!.Tiles.MutationTick;
        foreach (var (chunkKey, tier) in perChunkTier)
        {
            active.Add(chunkKey);
            var chunk = _simHost!.Tiles.GetChunkOrNull(chunkKey);
            if (chunk == null) continue;
            if (!_slots.TryGetValue(chunkKey, out var slot))
            {
                slot = new ChunkRenderSlot { MeshInstance = BuildInstance(chunkKey, tier), CurrentLod = -1 };
                AddChild(slot.MeshInstance);
                _slots[chunkKey] = slot;
            }
            slot.MeshInstance.Visible = ShowVoxelTerrain;
            if (slot.InFlight) continue;
            if (slot.LastCheckedMutationTick == mutationTick && slot.CurrentLod == tier) continue;
            if (_frameDispatchChunk >= DispatchChunkBudget) continue;

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
            if (slot.CurrentLod == tier && slot.UploadedRevision == combinedRev)
            {
                slot.LastCheckedMutationTick = mutationTick;
                continue;
            }

            var snap = chunk.Snapshot();
            ChunkSnapshot? snapPosX = nPosX?.Snapshot();
            ChunkSnapshot? snapNegX = nNegX?.Snapshot();
            ChunkSnapshot? snapPosZ = nPosZ?.Snapshot();
            ChunkSnapshot? snapNegZ = nNegZ?.Snapshot();
            ChunkSnapshot? snapPosY = nPosY?.Snapshot();
            ChunkSnapshot? snapNegY = nNegY?.Snapshot();
            slot.InFlight = true;
            slot.RequestedRevision = combinedRev;
            slot.LastCheckedMutationTick = mutationTick;
            _frameDispatchChunk++;
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

    private void UpdateTerrainSlots(Dictionary<TilePos, int> perChunkTier)
    {
        var mutationTick = _simHost!.Tiles.MutationTick;
        var active = new HashSet<(int cx, int cz)>();
        // perChunkTier keys include Y layers; terrain is per (cx, cz). Dedupe
        // before dispatching so a stack of 3 vertical voxel chunks doesn't
        // request the same terrain mesh 3×.
        foreach (var kv in perChunkTier)
        {
            var key = (kv.Key.X, kv.Key.Z);
            if (!active.Add(key)) continue;
            var tc = _simHost!.Tiles.GetTerrainChunkOrNull(key.Item1, key.Item2);
            if (tc == null) continue;

            if (!_terrainSlots.TryGetValue(key, out var slot))
            {
                slot = new TerrainRenderSlot { MeshInstance = BuildTerrainInstance() };
                AddChild(slot.MeshInstance);
                _terrainSlots[key] = slot;
            }
            slot.MeshInstance.Visible = true;
            if (slot.InFlight) continue;
            if (slot.LastCheckedMutationTick == mutationTick) continue;

            // Seam neighbors feed the +X/+Z/+XZ corner rows; a neighbor edit
            // shifts our visible seam, so fold their revisions in too.
            var tPx  = _simHost!.Tiles.GetTerrainChunkOrNull(key.Item1 + 1, key.Item2);
            var tPz  = _simHost!.Tiles.GetTerrainChunkOrNull(key.Item1,     key.Item2 + 1);
            var tPxz = _simHost!.Tiles.GetTerrainChunkOrNull(key.Item1 + 1, key.Item2 + 1);
            long revHash = tc.Revision
                + (tPx?.Revision ?? 0)
                + (tPz?.Revision ?? 0)
                + (tPxz?.Revision ?? 0);
            if (slot.UploadedRevision == revHash)
            {
                slot.LastCheckedMutationTick = mutationTick;
                continue;
            }
            if (_frameDispatchTerrain >= DispatchTerrainBudget) continue;

            var snap = _simHost!.Tiles.SnapshotTerrain(key.Item1, key.Item2);
            if (snap == null) continue;
            slot.InFlight = true;
            slot.RequestedRevision = revHash;
            slot.LastCheckedMutationTick = mutationTick;
            _frameDispatchTerrain++;
            var keyCaptured = key;
            var revCaptured = revHash;
            Task.Run(() =>
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var r = _terrainMesher.Build(snap);
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                Profiler.RecordMs("Build Terrain", ms);
                _completedTerrain.Enqueue((keyCaptured, r, revCaptured));
            });
        }
        foreach (var kv in _terrainSlots)
        {
            if (!active.Contains(kv.Key)) kv.Value.MeshInstance.Visible = false;
        }
    }

    private MeshInstance3D BuildTerrainInstance()
    {
        // HeightmapTerrainMesher emits world-space vertices (chunkBaseX/Z in
        // the mesher), so the MeshInstance sits at origin + Y bias only.
        // No MaterialOverride — the assembled ArrayMesh binds surface 0 to
        // the opaque terrain material and (when present) surface 1 to the
        // translucent water material. A MaterialOverride would flatten both
        // surfaces to a single material.
        return new MeshInstance3D
        {
            Position = new Vector3(0f, TerrainYBias, 0f),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private void UpdateGroupSlots(
        Dictionary<TilePos, GroupRenderSlot> table,
        Dictionary<TilePos, List<TilePos>> masks,
        int groupSize, int lod, int step, int cliffMinDelta)
    {
        var mutationTick = _simHost!.Tiles.MutationTick;
        foreach (var (groupKey, chunkKeys) in masks)
        {
            if (!table.TryGetValue(groupKey, out var slot))
            {
                slot = new GroupRenderSlot { MeshInstance = BuildInstance(groupKey, lod), CurrentLod = -1 };
                AddChild(slot.MeshInstance);
                table[groupKey] = slot;
            }
            slot.MeshInstance.Visible = true;
            if (slot.InFlight) continue;
            if (slot.LastCheckedMutationTick == mutationTick && slot.CurrentLod == lod) continue;

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
            if (slot.CurrentLod == lod && slot.UploadedRevision == revHash && slot.UploadedMaskHash == maskHash)
            {
                slot.LastCheckedMutationTick = mutationTick;
                continue;
            }
            ref var dispatchCount = ref (groupSize == Group4 ? ref _frameDispatchG4 : ref _frameDispatchG8);
            if (dispatchCount >= DispatchGroupBudget) continue;

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
            slot.LastCheckedMutationTick = mutationTick;
            dispatchCount++;
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
                Profiler.RecordMs(size == Group4 ? "Build G4" : "Build G8", ms);
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
        var patchMesh = groupSize == Group4 ? _g4PatchMesh! : _g8PatchMesh!;
        var mutationTick = _simHost!.Tiles.MutationTick;
        foreach (var (groupKey, chunkKeys) in masks)
        {
            if (!table.TryGetValue(groupKey, out var slot))
            {
                slot = new GroupRenderSlot
                {
                    MeshInstance = BuildGpuInstance(groupKey, patchMesh, lod),
                    CurrentLod = -1,
                };
                AddChild(slot.MeshInstance);
                table[groupKey] = slot;
            }
            slot.MeshInstance.Visible = true;
            if (slot.InFlight) continue;
            if (slot.LastCheckedMutationTick == mutationTick && slot.CurrentLod == lod) continue;

            long maskHash = 0;
            long revHash = 0;
            var yLevels = new HashSet<int>();
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
                yLevels.Add(ck.Y);
            }
            // Neighbor-border chunks feed border heights to the patch so group
            // seams don't gap. Include them in maskHash/revHash so a neighbor
            // edit retriggers this group's rebuild.
            AccumulateBorderHash(yLevels, groupKey, groupSize, ref maskHash, ref revHash);
            if (slot.CurrentLod == lod && slot.UploadedRevision == revHash && slot.UploadedMaskHash == maskHash)
            {
                slot.LastCheckedMutationTick = mutationTick;
                continue;
            }
            ref var dispatchCount = ref (groupSize == Group4 ? ref _frameDispatchG4 : ref _frameDispatchG8);
            if (dispatchCount >= DispatchGroupBudget) continue;

            var entries = new List<NaiveChunkMesher.GroupChunkEntry>(chunkKeys.Count);
            foreach (var ck in chunkKeys)
            {
                var chunk = _simHost!.Tiles.GetChunkOrNull(ck);
                if (chunk == null) continue;
                var lx = ck.X - groupKey.X;
                var lz = ck.Z - groupKey.Z;
                entries.Add(new NaiveChunkMesher.GroupChunkEntry(lx, lz, ck.Y, chunk.Snapshot()));
            }
            AppendBorderEntries(entries, yLevels, groupKey, groupSize);

            slot.InFlight = true;
            slot.RequestedRevision = revHash;
            slot.LastCheckedMutationTick = mutationTick;
            dispatchCount++;
            var key = groupKey;
            var size = groupSize;
            var maskHashCaptured = maskHash;
            var lodCaptured = lod;
            Task.Run(() =>
            {
                var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
                var patch = _mesher.BuildGroupHeightmapPatch(entries, size, lodCaptured);
                var ms = (System.Diagnostics.Stopwatch.GetTimestamp() - t0) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
                Profiler.RecordMs(size == Group4 ? "Build G4 GPU" : "Build G8 GPU", ms);
                _completedGpuGroup.Enqueue((key, size, patch, lodCaptured, maskHashCaptured));
            });
        }
        foreach (var kv in table)
        {
            if (!masks.ContainsKey(kv.Key)) kv.Value.MeshInstance.Visible = false;
        }
    }

    // Iterate chunk keys around the whole border of a group (±X, ±Z, and the
    // four diagonal corners), one per (lateral, Y) position, at the group's
    // existing Y levels. Local coords use Lx/Lz = -1 for the -X / -Z border
    // and Lx/Lz = groupSize for +X / +Z — BuildGroupHeightmapPatch decodes
    // both to the texture-border slot for that side.
    private void ForEachBorderChunk(HashSet<int> yLevels, TilePos groupKey, int groupSize,
        System.Action<int, int, int, Chunk> visit)
    {
        for (var cz = 0; cz < groupSize; cz++)
        foreach (var y in yLevels)
        {
            var c = _simHost!.Tiles.GetChunkOrNull(new TilePos(groupKey.X + groupSize, y, groupKey.Z + cz));
            if (c != null) visit(groupSize, cz, y, c);
        }
        for (var cz = 0; cz < groupSize; cz++)
        foreach (var y in yLevels)
        {
            var c = _simHost!.Tiles.GetChunkOrNull(new TilePos(groupKey.X - 1, y, groupKey.Z + cz));
            if (c != null) visit(-1, cz, y, c);
        }
        for (var cx = 0; cx < groupSize; cx++)
        foreach (var y in yLevels)
        {
            var c = _simHost!.Tiles.GetChunkOrNull(new TilePos(groupKey.X + cx, y, groupKey.Z + groupSize));
            if (c != null) visit(cx, groupSize, y, c);
        }
        for (var cx = 0; cx < groupSize; cx++)
        foreach (var y in yLevels)
        {
            var c = _simHost!.Tiles.GetChunkOrNull(new TilePos(groupKey.X + cx, y, groupKey.Z - 1));
            if (c != null) visit(cx, -1, y, c);
        }
        foreach (var y in yLevels)
        {
            var c = _simHost!.Tiles.GetChunkOrNull(new TilePos(groupKey.X + groupSize, y, groupKey.Z + groupSize));
            if (c != null) visit(groupSize, groupSize, y, c);
        }
        foreach (var y in yLevels)
        {
            var c = _simHost!.Tiles.GetChunkOrNull(new TilePos(groupKey.X - 1, y, groupKey.Z + groupSize));
            if (c != null) visit(-1, groupSize, y, c);
        }
        foreach (var y in yLevels)
        {
            var c = _simHost!.Tiles.GetChunkOrNull(new TilePos(groupKey.X + groupSize, y, groupKey.Z - 1));
            if (c != null) visit(groupSize, -1, y, c);
        }
        foreach (var y in yLevels)
        {
            var c = _simHost!.Tiles.GetChunkOrNull(new TilePos(groupKey.X - 1, y, groupKey.Z - 1));
            if (c != null) visit(-1, -1, y, c);
        }
    }

    private void AccumulateBorderHash(HashSet<int> yLevels, TilePos groupKey, int groupSize,
        ref long maskHash, ref long revHash)
    {
        long mh = maskHash, rh = revHash;
        var stride = groupSize + 2;
        ForEachBorderChunk(yLevels, groupKey, groupSize, (lx, lz, y, c) =>
        {
            unchecked
            {
                mh = mh * 31 + (((lx + 1) * stride + (lz + 1)) * 1024 + y + 1);
                rh += c.Revision;
            }
        });
        maskHash = mh; revHash = rh;
    }

    private void AppendBorderEntries(List<NaiveChunkMesher.GroupChunkEntry> entries,
        HashSet<int> yLevels, TilePos groupKey, int groupSize)
    {
        ForEachBorderChunk(yLevels, groupKey, groupSize, (lx, lz, y, c) =>
        {
            entries.Add(new NaiveChunkMesher.GroupChunkEntry(lx, lz, y, c.Snapshot()));
        });
    }

    private MeshInstance3D BuildGpuInstance(TilePos originChunkKey, ArrayMesh patchMesh, int lod)
    {
        // Patch verts are centered on their MeshInstance origin (see
        // BuildPatchMeshStepped). Shift Position by half-patch so the mesh
        // still occupies [ChunkOrigin, ChunkOrigin + patchW] in world space
        // while GlobalPosition reports the patch midpoint — lines the
        // MODEL_MATRIX up with the shader's per-fragment fade calc.
        var groupSize = lod == 3 ? Group4 : Group8;
        var half = groupSize * Chunk.Size * TileCoord.TileW * 0.5f;
        return new MeshInstance3D
        {
            Position = TileCoord.ChunkOrigin(originChunkKey) + new Vector3(half, -LodYSink, half),
            Mesh = patchMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private ShaderMaterial BuildTerrainMaterial(int groupSize, float maxHeightMeters, ImageTexture heightmap, ImageTexture kindmap)
    {
        var patchWidth = groupSize * Chunk.Size * TileCoord.TileW;
        var m = new ShaderMaterial { Shader = _terrainShader };
        m.SetShaderParameter("heightmap", heightmap);
        m.SetShaderParameter("kindmap", kindmap);
        m.SetShaderParameter("albedo_tex", _grassTex);
        m.SetShaderParameter("patch_width_m", patchWidth);
        m.SetShaderParameter("height_scale_m", System.Math.Max(1f, maxHeightMeters));
        // Per-tier fade bands (meters from the gimbal). Crossfade band
        // between G4 and G8 sits at [tier3, tier3+chunk]: G4 extends 1 chunk
        // past tier3 and fades out there; G8 starts at tier3 and fades in
        // across the same band. Same hash on both → every pixel drawn once.
        // G4's fade-in at [tier1-chunk, tier1] covers the L1/G4 boundary;
        // L0/L1 don't fade (they use StandardMaterial3D, not this shader).
        const float chunkM = Chunk.Size * TileCoord.TileW;
        const float tier1m = Tier1Range * chunkM;
        const float tier3m = Tier3Range * chunkM;
        // 1 chunk G4 fade_out / G8 fade_in overlap. Residual per-pixel sky
        // leaks where tier heights disagree are masked by a brown skybox
        // below the horizon (see dream_sky.gdshader) rather than fixed
        // geometrically — widening the band only smears the leaks, and
        // recoloring the floor hides them entirely.
        const float fadeMargin = chunkM;
        // Hard fog-end kill: discard any fragment past fog depth-end so G8
        // patches don't leak bright pixels through heavy fog at the horizon.
        // Fog ramp itself comes from Godot's depth fog (FogSkyAffect=0 to
        // keep the sky clean); see DayNightRenderer.
        var fogEnd = MaxChunkDistance * chunkM;
        m.SetShaderParameter("fog_end_m", fogEnd);
        if (groupSize == Group4)
        {
            m.SetShaderParameter("fade_in_start_m", tier1m - fadeMargin);
            m.SetShaderParameter("fade_in_end_m", tier1m);
            m.SetShaderParameter("fade_out_start_m", tier3m);
            m.SetShaderParameter("fade_out_end_m", tier3m + fadeMargin);
        }
        else
        {
            m.SetShaderParameter("fade_in_start_m", tier3m);
            m.SetShaderParameter("fade_in_end_m", tier3m + fadeMargin);
        }
        return m;
    }

    private static TilePos GroupKey(TilePos chunkKey, int groupSize)
    {
        return new TilePos(FloorDiv(chunkKey.X, groupSize) * groupSize, 0, FloorDiv(chunkKey.Z, groupSize) * groupSize);
    }

    // Min euclidean-squared distance (chunks²) from camera to the group's AABB
    // in the XZ plane. Zero if the camera is inside the group footprint.
    private static long GroupMinDistSq(TilePos groupKey, int size, int camX, int camZ)
    {
        var xMin = groupKey.X;
        var xMax = groupKey.X + size - 1;
        var zMin = groupKey.Z;
        var zMax = groupKey.Z + size - 1;
        long dx = System.Math.Max(0, System.Math.Max(camX - xMax, xMin - camX));
        long dz = System.Math.Max(0, System.Math.Max(camZ - zMax, zMin - camZ));
        return dx * dx + dz * dz;
    }

    // Max euclidean-squared distance (chunks²) from camera to any corner of
    // the group's AABB. Used to decide if ANY chunk in the group falls past a
    // tier boundary — straddling groups (near corner close, far corner past
    // tier1) must still emit their coarse mesh to cover the far chunks that
    // no near-tier mesh reaches.
    private static long GroupMaxDistSq(TilePos groupKey, int size, int camX, int camZ)
    {
        var xMin = groupKey.X;
        var xMax = groupKey.X + size - 1;
        var zMin = groupKey.Z;
        var zMax = groupKey.Z + size - 1;
        long dxMax = System.Math.Max(System.Math.Abs((long)(camX - xMin)), System.Math.Abs((long)(camX - xMax)));
        long dzMax = System.Math.Max(System.Math.Abs((long)(camZ - zMin)), System.Math.Abs((long)(camZ - zMax)));
        return dxMax * dxMax + dzMax * dzMax;
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

    private MeshInstance3D BuildInstance(TilePos originChunkKey, int lod)
    {
        _ = lod;
        return new MeshInstance3D
        {
            Position = TileCoord.ChunkOrigin(originChunkKey),
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    private ArrayMesh AssembleArrayMesh(MeshBuildResult r)
    {
        var mesh = new ArrayMesh();
        var arrays = new GArray();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = r.Verts;
        arrays[(int)Mesh.ArrayType.Normal] = r.Normals;
        arrays[(int)Mesh.ArrayType.Color] = r.Colors;
        arrays[(int)Mesh.ArrayType.TexUV] = r.Uvs;
        arrays[(int)Mesh.ArrayType.Index] = r.Indices;
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        // Set per-surface material on the mesh itself. Voxel + group
        // MeshInstances carry a MaterialOverride that wins over this, so the
        // assignment is only load-bearing for terrain MeshInstance (which
        // intentionally has no override so water surface can use its own
        // translucent material).
        mesh.SurfaceSetMaterial(0, _material);

        if (r.WaterIndices != null && r.WaterIndices.Length > 0)
        {
            var wArrays = new GArray();
            wArrays.Resize((int)Mesh.ArrayType.Max);
            wArrays[(int)Mesh.ArrayType.Vertex] = r.WaterVerts;
            wArrays[(int)Mesh.ArrayType.Normal] = r.WaterNormals;
            wArrays[(int)Mesh.ArrayType.Color] = r.WaterColors;
            wArrays[(int)Mesh.ArrayType.TexUV] = r.WaterUvs;
            wArrays[(int)Mesh.ArrayType.Index] = r.WaterIndices;
            mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, wArrays);
            mesh.SurfaceSetMaterial(1, _waterMaterial);
        }
        return mesh;
    }

    private void OnWorldRegenerated()
    {
        // Free every live slot. A regenerated world reuses the same chunk
        // keys but with fresh Chunk objects (Revision resets to 0), so the
        // slot's UploadedRevision comparison would otherwise wrongly report
        // "up to date" and the renderer would keep displaying stale meshes.
        foreach (var kv in _slots) kv.Value.MeshInstance.QueueFree();
        foreach (var kv in _g4Slots) kv.Value.MeshInstance.QueueFree();
        foreach (var kv in _g8Slots) kv.Value.MeshInstance.QueueFree();
        foreach (var kv in _terrainSlots) kv.Value.MeshInstance.QueueFree();
        _slots.Clear();
        _g4Slots.Clear();
        _g8Slots.Clear();
        _terrainSlots.Clear();
        // Drop the Classify cache so the next frame re-scans the new chunk set.
        _cacheCamChunkX = int.MinValue;
        _cacheChunkCount = -1;
        _cachePerChunkTier = null;
        _cacheG4Masks = null;
        _cacheG8Masks = null;
    }

    private sealed class ChunkRenderSlot
    {
        public MeshInstance3D MeshInstance = null!;
        public long UploadedRevision = -1;
        public long RequestedRevision = -1;
        public int CurrentLod = -1;
        public bool InFlight;
        // TileWorld.MutationTick observed when this slot was last reconciled.
        // Matches current tick + correct lod → skip the neighbor revision walk
        // entirely. Steady-state frames do zero per-chunk work.
        public long LastCheckedMutationTick = -1;
    }

    private sealed class GroupRenderSlot
    {
        public MeshInstance3D MeshInstance = null!;
        public long UploadedRevision = -1;
        public long RequestedRevision = -1;
        public long UploadedMaskHash;
        public int CurrentLod = -1;
        public bool InFlight;
        public long LastCheckedMutationTick = -1;
    }

    private sealed class TerrainRenderSlot
    {
        public MeshInstance3D MeshInstance = null!;
        public long UploadedRevision = -1;
        public long RequestedRevision = -1;
        public bool InFlight;
        public long LastCheckedMutationTick = -1;
    }
}
