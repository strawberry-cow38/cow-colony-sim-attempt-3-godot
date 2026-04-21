using System.Collections.Generic;
using Godot;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

public sealed class NaiveChunkMesher : IChunkMesher
{
    private static readonly (int dx, int dy, int dz, Vector3 n)[] Faces =
    {
        ( 1,  0,  0, new Vector3( 1, 0, 0)),
        (-1,  0,  0, new Vector3(-1, 0, 0)),
        ( 0,  1,  0, new Vector3( 0, 1, 0)),
        ( 0, -1,  0, new Vector3( 0,-1, 0)),
        ( 0,  0,  1, new Vector3( 0, 0, 1)),
        ( 0,  0, -1, new Vector3( 0, 0,-1)),
    };

    public MeshBuildResult? BuildMeshData(ChunkSnapshot snapshot, TilePos chunkKey, int lodLevel)
    {
        return lodLevel switch
        {
            <= 0 => BuildFullVoxel(snapshot, chunkKey),
            1    => BuildSingleChunkHeightmap(snapshot, chunkKey, step: 1),
            _    => BuildSingleChunkHeightmap(snapshot, chunkKey, step: 4),
        };
    }

    public MeshBuildResult? BuildFullVoxelWithNeighbors(
        ChunkSnapshot snapshot, TilePos chunkKey,
        ChunkSnapshot? nPosX, ChunkSnapshot? nNegX,
        ChunkSnapshot? nPosY, ChunkSnapshot? nNegY,
        ChunkSnapshot? nPosZ, ChunkSnapshot? nNegZ)
        => BuildFullVoxel(snapshot, chunkKey, nPosX, nNegX, nPosY, nNegY, nPosZ, nNegZ);

    public MeshBuildResult? BuildChunkHeightmapWithBorders(
        ChunkSnapshot snapshot, TilePos chunkKey, int step,
        ChunkSnapshot? posX, ChunkSnapshot? negX,
        ChunkSnapshot? posZ, ChunkSnapshot? negZ)
    {
        var size = Chunk.Size;
        var colHeight = new int[size, size];
        var colKind = new TileKind[size, size];
        for (var lz = 0; lz < size; lz++)
        for (var lx = 0; lx < size; lx++)
        {
            var top = -1;
            var kind = TileKind.Empty;
            for (var ly = size - 1; ly >= 0; ly--)
            {
                var t = snapshot[lx, ly, lz];
                if (!t.IsEmpty) { top = ly; kind = t.Kind; break; }
            }
            colHeight[lx, lz] = top;
            colKind[lx, lz] = kind;
        }

        var bPosX = BorderColumn(posX, axis: 0, edge: 0);
        var bNegX = BorderColumn(negX, axis: 0, edge: size - 1);
        var bPosZ = BorderColumn(posZ, axis: 2, edge: 0);
        var bNegZ = BorderColumn(negZ, axis: 2, edge: size - 1);

        var baseWx = chunkKey.X * size;
        var baseWz = chunkKey.Z * size;
        var mesh = BuildHeightmapMesh(colHeight, colKind, size, size, baseWx, baseWz, step,
            bPosX, bNegX, bPosZ, bNegZ);
        if (mesh == null) return null;
        mesh.Revision = snapshot.Revision;
        mesh.LodLevel = step == 1 ? 1 : 2;
        return mesh;
    }

    private static int[]? BorderColumn(ChunkSnapshot? neighbor, int axis, int edge)
    {
        if (!neighbor.HasValue) return null;
        var size = Chunk.Size;
        var arr = new int[size];
        var s = neighbor.Value;
        for (var i = 0; i < size; i++)
        {
            var lx = axis == 0 ? edge : i;
            var lz = axis == 2 ? edge : i;
            var top = -1;
            for (var ly = size - 1; ly >= 0; ly--)
            {
                if (!s[lx, ly, lz].IsEmpty) { top = ly; break; }
            }
            arr[i] = top;
        }
        return arr;
    }

    public readonly struct GroupChunkEntry
    {
        public readonly int Lx;
        public readonly int Lz;
        public readonly int ChunkY;
        public readonly ChunkSnapshot Snap;
        public GroupChunkEntry(int lx, int lz, int chunkY, ChunkSnapshot snap)
        {
            Lx = lx; Lz = lz; ChunkY = chunkY; Snap = snap;
        }
    }

    public sealed class HeightmapPatch
    {
        public float[,] Heights = null!;
        // Parallel kind texel per height cell. Empty (0) also marks border
        // fallback cells so the shader can distinguish unmapped regions.
        public byte[,] Kinds = null!;
        public int SizeX;
        public int SizeZ;
        public int Revision;
        public int LodLevel;
        public float MaxHeightMeters;
    }

    public HeightmapPatch? BuildGroupHeightmapPatch(
        List<GroupChunkEntry> entries, int groupChunks, int lodLevel)
    {
        var size = Chunk.Size;
        var sizeX = groupChunks * size;
        var sizeZ = groupChunks * size;
        // Texture is sizeX+2 × sizeZ+2: own tiles at heights[1..sizeX, 1..sizeZ];
        // 1-pixel border on every side carries the adjacent neighbor group's
        // first/last tile column or row. Border data lets the stepped patch
        // mesh emit non-degenerate walls on ALL four group seams — otherwise
        // cliffs at the patch's -X / -Z edge where our side is lower have no
        // wall at all and show sky through the gap.
        var heights = new float[sizeX + 2, sizeZ + 2];
        var kinds = new byte[sizeX + 2, sizeZ + 2];
        var revSum = 0;
        var any = false;

        var byCol = new Dictionary<(int, int), List<GroupChunkEntry>>();
        foreach (var e in entries)
        {
            revSum += e.Snap.Revision;
            if (!byCol.TryGetValue((e.Lx, e.Lz), out var list))
            {
                list = new List<GroupChunkEntry>(4);
                byCol[(e.Lx, e.Lz)] = list;
            }
            list.Add(e);
        }

        var th = TileCoord.TileH;
        // cx/cz = -1 → border entry from the -X / -Z neighbor group (only its
        // last tile col/row is sampled, written to heights[0, ..] / heights[.., 0]).
        // cx/cz in [0, groupChunks-1] → own group chunks, all tiles sampled,
        // written to heights[cx*size+lx+1, cz*size+lz+1].
        // cx/cz = groupChunks → +X / +Z neighbor border (first tile col/row),
        // written to heights[sizeX+1, ..] / heights[.., sizeZ+1].
        foreach (var ((cx, cz), list) in byCol)
        {
            list.Sort((a, b) => b.ChunkY.CompareTo(a.ChunkY));
            int lxStart, lxCount, lxTileBase;
            if (cx == -1)                 { lxStart = size - 1; lxCount = 1; lxTileBase = 1 - size; }  // heights[0]
            else if (cx == groupChunks)   { lxStart = 0; lxCount = 1; lxTileBase = cx * size + 1; }  // heights[sizeX+1]
            else                          { lxStart = 0; lxCount = size; lxTileBase = cx * size + 1; }

            int lzStart, lzCount, lzTileBase;
            if (cz == -1)                 { lzStart = size - 1; lzCount = 1; lzTileBase = 1 - size; }
            else if (cz == groupChunks)   { lzStart = 0; lzCount = 1; lzTileBase = cz * size + 1; }
            else                          { lzStart = 0; lzCount = size; lzTileBase = cz * size + 1; }

            for (var lzOff = 0; lzOff < lzCount; lzOff++)
            for (var lxOff = 0; lxOff < lxCount; lxOff++)
            {
                var lx = lxStart + lxOff;
                var lz = lzStart + lzOff;
                var top = -1;
                var kind = TileKind.Empty;
                foreach (var ent in list)
                {
                    var found = false;
                    for (var ly = size - 1; ly >= 0; ly--)
                    {
                        var t = ent.Snap[lx, ly, lz];
                        if (!t.IsEmpty) { top = ent.ChunkY * size + ly; kind = t.Kind; found = true; break; }
                    }
                    if (found) break;
                }
                if (top >= 0)
                {
                    any = true;
                    heights[lxTileBase + lx, lzTileBase + lz] = (top + 1) * th;
                    kinds[lxTileBase + lx, lzTileBase + lz] = (byte)kind;
                }
            }
        }

        if (!any) return null;

        // Fallback: if any border wasn't provided (world edge, cell not paged
        // in), replicate the nearest interior value so sampling doesn't fetch
        // zero and collapse the wall. Use kinds[]==Empty as the unset sentinel
        // instead of heights[]==0 — now that lake columns legitimately encode
        // heights==0 (water top at y=-1), the height-zero test misfires.
        // Z first, then X — so corners picked up by the X pass inherit
        // already-filled Z-border data.
        for (var x = 1; x <= sizeX; x++)
        {
            if (kinds[x, 0] == 0) { heights[x, 0] = heights[x, 1]; kinds[x, 0] = kinds[x, 1]; }
            if (kinds[x, sizeZ + 1] == 0) { heights[x, sizeZ + 1] = heights[x, sizeZ]; kinds[x, sizeZ + 1] = kinds[x, sizeZ]; }
        }
        for (var z = 0; z <= sizeZ + 1; z++)
        {
            if (kinds[0, z] == 0) { heights[0, z] = heights[1, z]; kinds[0, z] = kinds[1, z]; }
            if (kinds[sizeX + 1, z] == 0) { heights[sizeX + 1, z] = heights[sizeX, z]; kinds[sizeX + 1, z] = kinds[sizeX, z]; }
        }

        float maxH = 0f;
        for (var z = 0; z <= sizeZ + 1; z++)
            for (var x = 0; x <= sizeX + 1; x++)
                if (heights[x, z] > maxH) maxH = heights[x, z];

        return new HeightmapPatch
        {
            Heights = heights,
            Kinds = kinds,
            SizeX = sizeX + 2,
            SizeZ = sizeZ + 2,
            Revision = revSum,
            LodLevel = lodLevel,
            MaxHeightMeters = maxH,
        };
    }

    public MeshBuildResult? BuildGroupMesh(
        List<GroupChunkEntry> entries, TilePos baseChunkKey, int groupChunks, int step, int lodLevel,
        int cliffMinDelta = 1)
    {
        var size = Chunk.Size;
        var sizeX = groupChunks * size;
        var sizeZ = groupChunks * size;
        var colHeight = new int[sizeX, sizeZ];
        var colKind = new TileKind[sizeX, sizeZ];
        for (var i = 0; i < sizeX; i++)
            for (var j = 0; j < sizeZ; j++)
                colHeight[i, j] = -1;
        var revSum = 0;

        var byCol = new Dictionary<(int, int), List<GroupChunkEntry>>();
        foreach (var e in entries)
        {
            revSum += e.Snap.Revision;
            if (!byCol.TryGetValue((e.Lx, e.Lz), out var list))
            {
                list = new List<GroupChunkEntry>(4);
                byCol[(e.Lx, e.Lz)] = list;
            }
            list.Add(e);
        }

        foreach (var ((cx, cz), list) in byCol)
        {
            list.Sort((a, b) => b.ChunkY.CompareTo(a.ChunkY));
            for (var lz = 0; lz < size; lz++)
            for (var lx = 0; lx < size; lx++)
            {
                var top = -1;
                var kind = TileKind.Empty;
                foreach (var ent in list)
                {
                    var found = false;
                    for (var ly = size - 1; ly >= 0; ly--)
                    {
                        var t = ent.Snap[lx, ly, lz];
                        if (!t.IsEmpty) { top = ent.ChunkY * size + ly; kind = t.Kind; found = true; break; }
                    }
                    if (found) break;
                }
                colHeight[cx * size + lx, cz * size + lz] = top;
                colKind[cx * size + lx, cz * size + lz] = kind;
            }
        }

        var baseWx = baseChunkKey.X * size;
        var baseWz = baseChunkKey.Z * size;
        var mesh = BuildHeightmapMesh(colHeight, colKind, sizeX, sizeZ, baseWx, baseWz, step,
            cliffMinDelta: cliffMinDelta);
        if (mesh == null) return null;
        mesh.Revision = revSum;
        mesh.LodLevel = lodLevel;
        return mesh;
    }

    private static MeshBuildResult? BuildFullVoxel(
        ChunkSnapshot snapshot, TilePos chunkKey,
        ChunkSnapshot? nPosX = null, ChunkSnapshot? nNegX = null,
        ChunkSnapshot? nPosY = null, ChunkSnapshot? nNegY = null,
        ChunkSnapshot? nPosZ = null, ChunkSnapshot? nNegZ = null)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();

        var tw = TileCoord.TileW;
        var th = TileCoord.TileH;
        var baseWx = chunkKey.X * Chunk.Size;
        var baseWz = chunkKey.Z * Chunk.Size;
        const int S = Chunk.Size;

        for (var ly = 0; ly < S; ly++)
        for (var lz = 0; lz < S; lz++)
        for (var lx = 0; lx < S; lx++)
        {
            var tile = snapshot[lx, ly, lz];
            if (tile.IsEmpty) continue;
            // HeightmapTerrainMesher owns Floor/Sand/Water geometry at L0.
            // Voxel mesher keeps Solid (rock, walls, buildings). Tile stays
            // non-empty for neighbor occupancy so rock side faces adjacent
            // to terrain still cull correctly below the surface.
            if (tile.Kind == TileKind.Floor || tile.Kind == TileKind.Sand || tile.Kind == TileKind.Water)
                continue;
            var ox = lx * tw;
            var oy = ly * th;
            var oz = lz * tw;
            var wx = baseWx + lx;
            var wz = baseWz + lz;
            var topCell = TileAtlas.CellForTop(tile.Kind, wx, wz);
            var sideCell = TileAtlas.CellForSide(tile.Kind);

            foreach (var (dx, dy, dz, n) in Faces)
            {
                var nx = lx + dx;
                var ny = ly + dy;
                var nz = lz + dz;
                bool neighborEmpty;
                if ((uint)nx < S && (uint)ny < S && (uint)nz < S)
                {
                    neighborEmpty = snapshot[nx, ny, nz].IsEmpty;
                }
                else
                {
                    // Boundary: consult neighbor chunk's edge tile so adjacent
                    // chunks don't both emit faces at their shared plane.
                    ChunkSnapshot? nb = null;
                    if (nx >= S) nb = nPosX;
                    else if (nx < 0) nb = nNegX;
                    else if (ny >= S) nb = nPosY;
                    else if (ny < 0) nb = nNegY;
                    else if (nz >= S) nb = nPosZ;
                    else if (nz < 0) nb = nNegZ;
                    if (nb.HasValue)
                    {
                        var mx = ((nx % S) + S) % S;
                        var my = ((ny % S) + S) % S;
                        var mz = ((nz % S) + S) % S;
                        neighborEmpty = nb.Value[mx, my, mz].IsEmpty;
                    }
                    else
                    {
                        // No neighbor snapshot available → treat as empty
                        // (emit face). Matches previous behavior at world edge.
                        neighborEmpty = true;
                    }
                }
                if (!neighborEmpty) continue;

                var cell = n.Y > 0.5f ? topCell : sideCell;
                // Water tops sit half a tile below the ceiling so the waterline
                // reads clearly against adjacent land. Only the top face is
                // offset; side faces (rarely visible) stay full-height.
                var topDrop = tile.Kind == TileKind.Water ? th * 0.5f : 0f;
                EmitFace(verts, normals, colors, uvs, indices, ox, oy, oz, tw, th, n, TileAtlas.TintFor(tile.Kind), cell, topDrop);
            }
        }

        if (indices.Count == 0) return null;

        return new MeshBuildResult
        {
            Verts = verts.ToArray(),
            Normals = normals.ToArray(),
            Colors = colors.ToArray(),
            Uvs = uvs.ToArray(),
            Indices = indices.ToArray(),
            Revision = snapshot.Revision,
            LodLevel = 0,
        };
    }

    private static MeshBuildResult? BuildSingleChunkHeightmap(ChunkSnapshot snapshot, TilePos chunkKey, int step)
    {
        var size = Chunk.Size;
        var colHeight = new int[size, size];
        var colKind = new TileKind[size, size];
        for (var lz = 0; lz < size; lz++)
        for (var lx = 0; lx < size; lx++)
        {
            var top = -1;
            var kind = TileKind.Empty;
            for (var ly = size - 1; ly >= 0; ly--)
            {
                var t = snapshot[lx, ly, lz];
                if (!t.IsEmpty) { top = ly; kind = t.Kind; break; }
            }
            colHeight[lx, lz] = top;
            colKind[lx, lz] = kind;
        }

        var baseWx = chunkKey.X * size;
        var baseWz = chunkKey.Z * size;
        var mesh = BuildHeightmapMesh(colHeight, colKind, size, size, baseWx, baseWz, step);
        if (mesh == null) return null;
        mesh.Revision = snapshot.Revision;
        mesh.LodLevel = step == 1 ? 1 : 2;
        return mesh;
    }

    private static MeshBuildResult? BuildHeightmapMesh(
        int[,] colHeight, TileKind[,] colKind, int sizeX, int sizeZ,
        int baseWx, int baseWz, int step,
        int[]? bPosX = null, int[]? bNegX = null,
        int[]? bPosZ = null, int[]? bNegZ = null,
        int cliffMinDelta = 1)
    {
        var tw = TileCoord.TileW;
        var th = TileCoord.TileH;

        var cellsX = (sizeX + step - 1) / step;
        var cellsZ = (sizeZ + step - 1) / step;
        var dsHeight = new int[cellsX, cellsZ];
        var dsKind = new TileKind[cellsX, cellsZ];
        for (var cz = 0; cz < cellsZ; cz++)
        for (var cx = 0; cx < cellsX; cx++)
        {
            var lx0 = cx * step;
            var lz0 = cz * step;
            var hi = -1;
            var kind = TileKind.Empty;
            for (var dz = 0; dz < step && lz0 + dz < sizeZ; dz++)
            for (var dx = 0; dx < step && lx0 + dx < sizeX; dx++)
            {
                var h = colHeight[lx0 + dx, lz0 + dz];
                if (h > hi) { hi = h; kind = colKind[lx0 + dx, lz0 + dz]; }
            }
            dsHeight[cx, cz] = hi;
            dsKind[cx, cz] = kind;
        }

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        var up = Vector3.Up;

        var visited = new bool[cellsX, cellsZ];
        for (var cz = 0; cz < cellsZ; cz++)
        for (var cx = 0; cx < cellsX; cx++)
        {
            if (visited[cx, cz]) continue;
            var hi = dsHeight[cx, cz];
            if (hi < 0) { visited[cx, cz] = true; continue; }
            var kind = dsKind[cx, cz];

            var w = 1;
            while (cx + w < cellsX && !visited[cx + w, cz]
                && dsHeight[cx + w, cz] == hi && dsKind[cx + w, cz] == kind) w++;

            var h = 1;
            var canExtend = true;
            while (canExtend && cz + h < cellsZ)
            {
                for (var i = 0; i < w; i++)
                {
                    if (visited[cx + i, cz + h] || dsHeight[cx + i, cz + h] != hi || dsKind[cx + i, cz + h] != kind)
                    { canExtend = false; break; }
                }
                if (canExtend) h++;
            }

            for (var dz = 0; dz < h; dz++)
            for (var dx = 0; dx < w; dx++)
                visited[cx + dx, cz + dz] = true;

            var lx = cx * step;
            var lz = cz * step;
            var ox = lx * tw;
            var oz = lz * tw;
            // Water surface sits half a tile below its containing tile's
            // ceiling so the shore reads as a step above the waterline.
            var topDrop = kind == TileKind.Water ? th * 0.5f : 0f;
            var oyTop = (hi + 1) * th - topDrop;
            var spanX = w * step * tw;
            var spanZ = h * step * tw;

            var topCell = TileAtlas.CellForTop(kind, baseWx + lx, baseWz + lz);
            var (u0, v0, u1, v1) = TileAtlas.CellUV(topCell);
            var tint = TileAtlas.TintFor(kind);

            var baseIndex = verts.Count;
            verts.Add(new Vector3(ox,         oyTop, oz));
            verts.Add(new Vector3(ox + spanX, oyTop, oz));
            verts.Add(new Vector3(ox + spanX, oyTop, oz + spanZ));
            verts.Add(new Vector3(ox,         oyTop, oz + spanZ));
            uvs.Add(new Vector2(u0, v0));
            uvs.Add(new Vector2(u1, v0));
            uvs.Add(new Vector2(u1, v1));
            uvs.Add(new Vector2(u0, v1));
            for (var k = 0; k < 4; k++) { normals.Add(up); colors.Add(tint); }
            indices.Add(baseIndex); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
            indices.Add(baseIndex); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);
        }

        EmitGreedyCliffDir(verts, normals, colors, uvs, indices, dsHeight, dsKind, cellsX, cellsZ, th, tw, step, dirX: 1,  dirZ: 0,  bPosX, bNegX, bPosZ, bNegZ, cliffMinDelta);
        EmitGreedyCliffDir(verts, normals, colors, uvs, indices, dsHeight, dsKind, cellsX, cellsZ, th, tw, step, dirX: -1, dirZ: 0,  bPosX, bNegX, bPosZ, bNegZ, cliffMinDelta);
        EmitGreedyCliffDir(verts, normals, colors, uvs, indices, dsHeight, dsKind, cellsX, cellsZ, th, tw, step, dirX: 0,  dirZ: 1,  bPosX, bNegX, bPosZ, bNegZ, cliffMinDelta);
        EmitGreedyCliffDir(verts, normals, colors, uvs, indices, dsHeight, dsKind, cellsX, cellsZ, th, tw, step, dirX: 0,  dirZ: -1, bPosX, bNegX, bPosZ, bNegZ, cliffMinDelta);

        if (indices.Count == 0) return null;

        return new MeshBuildResult
        {
            Verts = verts.ToArray(),
            Normals = normals.ToArray(),
            Colors = colors.ToArray(),
            Uvs = uvs.ToArray(),
            Indices = indices.ToArray(),
        };
    }

    private static void EmitGreedyCliffDir(
        List<Vector3> verts,
        List<Vector3> normals,
        List<Color> colors,
        List<Vector2> uvs,
        List<int> indices,
        int[,] dsHeight, TileKind[,] dsKind,
        int cellsX, int cellsZ, float th, float tw, int step,
        int dirX, int dirZ,
        int[]? bPosX, int[]? bNegX, int[]? bPosZ, int[]? bNegZ,
        int cliffMinDelta)
    {
        var alongZ = dirX != 0;
        var outerCount = alongZ ? cellsX : cellsZ;
        var innerCount = alongZ ? cellsZ : cellsX;

        for (var outer = 0; outer < outerCount; outer++)
        {
            var inner = 0;
            while (inner < innerCount)
            {
                var cx = alongZ ? outer : inner;
                var cz = alongZ ? inner : outer;
                var hi = dsHeight[cx, cz];
                if (hi < 0) { inner++; continue; }

                var (hn, hasN) = QueryNeighbor(dsHeight, cellsX, cellsZ, cx, cz, dirX, dirZ, bPosX, bNegX, bPosZ, bNegZ, step);
                if (!hasN || hi - hn < cliffMinDelta) { inner++; continue; }

                var kind = dsKind[cx, cz];
                var runStart = inner;
                var innerEnd = inner + 1;
                while (innerEnd < innerCount)
                {
                    var cx2 = alongZ ? outer : innerEnd;
                    var cz2 = alongZ ? innerEnd : outer;
                    if (dsHeight[cx2, cz2] != hi) break;
                    if (dsKind[cx2, cz2] != kind) break;
                    var (hn2, hasN2) = QueryNeighbor(dsHeight, cellsX, cellsZ, cx2, cz2, dirX, dirZ, bPosX, bNegX, bPosZ, bNegZ, step);
                    if (!hasN2 || hn2 != hn || hi - hn2 < cliffMinDelta) break;
                    innerEnd++;
                }

                var runLen = innerEnd - runStart;
                var yTop = (hi + 1) * th;
                var yBot = (hn + 1) * th;

                var ox = (alongZ ? outer : runStart) * step * tw;
                var oz = (alongZ ? runStart : outer) * step * tw;
                var spanX = alongZ ? step * tw : runLen * step * tw;
                var spanZ = alongZ ? runLen * step * tw : step * tw;

                var sideCell = TileAtlas.CellForSide(kind);
                var (u0, v0, u1, v1) = TileAtlas.CellUV(sideCell);
                var tint = TileAtlas.TintFor(kind);

                Vector3 p0, p1, p2, p3;
                Vector3 normal;
                if (dirX == 1)
                {
                    normal = new Vector3(1, 0, 0);
                    p0 = new Vector3(ox + spanX, yBot, oz);
                    p1 = new Vector3(ox + spanX, yBot, oz + spanZ);
                    p2 = new Vector3(ox + spanX, yTop, oz + spanZ);
                    p3 = new Vector3(ox + spanX, yTop, oz);
                }
                else if (dirX == -1)
                {
                    normal = new Vector3(-1, 0, 0);
                    p0 = new Vector3(ox, yBot, oz + spanZ);
                    p1 = new Vector3(ox, yBot, oz);
                    p2 = new Vector3(ox, yTop, oz);
                    p3 = new Vector3(ox, yTop, oz + spanZ);
                }
                else if (dirZ == 1)
                {
                    normal = new Vector3(0, 0, 1);
                    p0 = new Vector3(ox + spanX, yBot, oz + spanZ);
                    p1 = new Vector3(ox,         yBot, oz + spanZ);
                    p2 = new Vector3(ox,         yTop, oz + spanZ);
                    p3 = new Vector3(ox + spanX, yTop, oz + spanZ);
                }
                else
                {
                    normal = new Vector3(0, 0, -1);
                    p0 = new Vector3(ox,         yBot, oz);
                    p1 = new Vector3(ox + spanX, yBot, oz);
                    p2 = new Vector3(ox + spanX, yTop, oz);
                    p3 = new Vector3(ox,         yTop, oz);
                }

                var baseIndex = verts.Count;
                verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
                normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
                colors.Add(tint); colors.Add(tint);
                colors.Add(tint); colors.Add(tint);
                uvs.Add(new Vector2(u0, v1));
                uvs.Add(new Vector2(u1, v1));
                uvs.Add(new Vector2(u1, v0));
                uvs.Add(new Vector2(u0, v0));
                indices.Add(baseIndex); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
                indices.Add(baseIndex); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);

                inner = innerEnd;
            }
        }
    }

    private static (int hn, bool hasNeighbor) QueryNeighbor(
        int[,] dsHeight, int cellsX, int cellsZ,
        int cx, int cz, int dirX, int dirZ,
        int[]? bPosX, int[]? bNegX, int[]? bPosZ, int[]? bNegZ, int step)
    {
        var nx = cx + dirX;
        var nz = cz + dirZ;
        if ((uint)nx < cellsX && (uint)nz < cellsZ)
        {
            return (dsHeight[nx, nz], true);
        }
        int[]? border = null;
        var idx = 0;
        if (dirX == 1) { border = bPosX; idx = cz * step; }
        else if (dirX == -1) { border = bNegX; idx = cz * step; }
        else if (dirZ == 1) { border = bPosZ; idx = cx * step; }
        else if (dirZ == -1) { border = bNegZ; idx = cx * step; }
        if (border == null || (uint)idx >= border.Length) return (-2, false);
        return (border[idx], true);
    }

    private static void EmitFace(
        List<Vector3> verts,
        List<Vector3> normals,
        List<Color> colors,
        List<Vector2> uvs,
        List<int> indices,
        float ox, float oy, float oz, float tw, float th,
        Vector3 normal, Color color, int cell, float topDrop = 0f)
    {
        var baseIndex = verts.Count;
        var (u0, v0, u1, v1) = TileAtlas.CellUV(cell);
        // Top faces optionally drop by topDrop meters so water tops can sit
        // below the integer tile ceiling. Side faces keep their full height.
        var thTop = th - topDrop;
        Vector3 v0p, v1p, v2p, v3p;
        if (normal.X > 0.5f)      { v0p = new(ox+tw, oy, oz); v1p = new(ox+tw, oy, oz+tw); v2p = new(ox+tw, oy+th, oz+tw); v3p = new(ox+tw, oy+th, oz); }
        else if (normal.X < -0.5f) { v0p = new(ox, oy, oz+tw); v1p = new(ox, oy, oz); v2p = new(ox, oy+th, oz); v3p = new(ox, oy+th, oz+tw); }
        else if (normal.Y > 0.5f)  { v0p = new(ox, oy+thTop, oz); v1p = new(ox+tw, oy+thTop, oz); v2p = new(ox+tw, oy+thTop, oz+tw); v3p = new(ox, oy+thTop, oz+tw); }
        else if (normal.Y < -0.5f) { v0p = new(ox, oy, oz+tw); v1p = new(ox+tw, oy, oz+tw); v2p = new(ox+tw, oy, oz); v3p = new(ox, oy, oz); }
        else if (normal.Z > 0.5f)  { v0p = new(ox+tw, oy, oz+tw); v1p = new(ox, oy, oz+tw); v2p = new(ox, oy+th, oz+tw); v3p = new(ox+tw, oy+th, oz+tw); }
        else                        { v0p = new(ox, oy, oz); v1p = new(ox+tw, oy, oz); v2p = new(ox+tw, oy+th, oz); v3p = new(ox, oy+th, oz); }

        verts.Add(v0p); verts.Add(v1p); verts.Add(v2p); verts.Add(v3p);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
        uvs.Add(new Vector2(u0, v1)); uvs.Add(new Vector2(u1, v1)); uvs.Add(new Vector2(u1, v0)); uvs.Add(new Vector2(u0, v0));
        indices.Add(baseIndex); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
        indices.Add(baseIndex); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);
    }
}
