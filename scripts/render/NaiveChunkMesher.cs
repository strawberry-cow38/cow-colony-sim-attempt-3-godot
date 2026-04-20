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
            1    => BuildHeightmap(snapshot, chunkKey, step: 1),
            _    => BuildHeightmap(snapshot, chunkKey, step: 4),
        };
    }

    private static MeshBuildResult? BuildFullVoxel(ChunkSnapshot snapshot, TilePos chunkKey)
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

        for (var ly = 0; ly < Chunk.Size; ly++)
        for (var lz = 0; lz < Chunk.Size; lz++)
        for (var lx = 0; lx < Chunk.Size; lx++)
        {
            var tile = snapshot[lx, ly, lz];
            if (tile.IsEmpty) continue;
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
                var neighborInside = (uint)nx < Chunk.Size && (uint)ny < Chunk.Size && (uint)nz < Chunk.Size;
                if (neighborInside && !snapshot[nx, ny, nz].IsEmpty) continue;

                var cell = n.Y > 0.5f ? topCell : sideCell;
                EmitFace(verts, normals, colors, uvs, indices, ox, oy, oz, tw, th, n, Colors.White, cell);
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

    private static MeshBuildResult? BuildHeightmap(ChunkSnapshot snapshot, TilePos chunkKey, int step)
    {
        var size = Chunk.Size;
        var tw = TileCoord.TileW;
        var th = TileCoord.TileH;
        var baseWx = chunkKey.X * size;
        var baseWz = chunkKey.Z * size;

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

        var cellCount = (size + step - 1) / step;
        var dsHeight = new int[cellCount, cellCount];
        var dsKind = new TileKind[cellCount, cellCount];
        for (var cz = 0; cz < cellCount; cz++)
        for (var cx = 0; cx < cellCount; cx++)
        {
            var lx0 = cx * step;
            var lz0 = cz * step;
            var hi = -1;
            var kind = TileKind.Empty;
            for (var dz = 0; dz < step && lz0 + dz < size; dz++)
            for (var dx = 0; dx < step && lx0 + dx < size; dx++)
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
        var wSpan = step * tw;

        for (var cz = 0; cz < cellCount; cz++)
        for (var cx = 0; cx < cellCount; cx++)
        {
            var hi = dsHeight[cx, cz];
            if (hi < 0) continue;
            var kind = dsKind[cx, cz];
            var lx = cx * step;
            var lz = cz * step;
            var ox = lx * tw;
            var oz = lz * tw;
            var oyTop = (hi + 1) * th;

            var topCell = TileAtlas.CellForTop(kind, baseWx + lx, baseWz + lz);
            var sideCell = TileAtlas.CellForSide(kind);
            var (u0, v0, u1, v1) = TileAtlas.CellUV(topCell);

            var baseIndex = verts.Count;
            verts.Add(new Vector3(ox,         oyTop, oz));
            verts.Add(new Vector3(ox + wSpan, oyTop, oz));
            verts.Add(new Vector3(ox + wSpan, oyTop, oz + wSpan));
            verts.Add(new Vector3(ox,         oyTop, oz + wSpan));
            uvs.Add(new Vector2(u0, v0));
            uvs.Add(new Vector2(u1, v0));
            uvs.Add(new Vector2(u1, v1));
            uvs.Add(new Vector2(u0, v1));
            for (var k = 0; k < 4; k++) { normals.Add(up); colors.Add(Godot.Colors.White); }
            indices.Add(baseIndex); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
            indices.Add(baseIndex); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);

            EmitCliffSide(verts, normals, colors, uvs, indices,
                dsHeight, cellCount, cx, cz, hi, sideCell,
                ox, oz, wSpan, th, dirX: 1, dirZ: 0);
            EmitCliffSide(verts, normals, colors, uvs, indices,
                dsHeight, cellCount, cx, cz, hi, sideCell,
                ox, oz, wSpan, th, dirX: -1, dirZ: 0);
            EmitCliffSide(verts, normals, colors, uvs, indices,
                dsHeight, cellCount, cx, cz, hi, sideCell,
                ox, oz, wSpan, th, dirX: 0, dirZ: 1);
            EmitCliffSide(verts, normals, colors, uvs, indices,
                dsHeight, cellCount, cx, cz, hi, sideCell,
                ox, oz, wSpan, th, dirX: 0, dirZ: -1);
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
            LodLevel = step == 1 ? 1 : 2,
        };
    }

    private static void EmitCliffSide(
        List<Vector3> verts,
        List<Vector3> normals,
        List<Color> colors,
        List<Vector2> uvs,
        List<int> indices,
        int[,] dsHeight, int cellCount,
        int cx, int cz, int hi, int sideCell,
        float ox, float oz, float wSpan, float th,
        int dirX, int dirZ)
    {
        var nx = cx + dirX;
        var nz = cz + dirZ;
        // Out-of-chunk neighbors: assume same height (skip emit). Prevents
        // full-height skirt walls at every chunk border; cross-chunk
        // cliffs remain invisible, tolerable for distant LOD.
        if ((uint)nx >= cellCount || (uint)nz >= cellCount) return;
        var hn = dsHeight[nx, nz];
        if (hn >= hi) return;

        var yTop = (hi + 1) * th;
        var yBot = (hn + 1) * th;
        var (u0, v0, u1, v1) = TileAtlas.CellUV(sideCell);

        Vector3 p0, p1, p2, p3;
        Vector3 normal;
        if (dirX == 1)
        {
            normal = new Vector3(1, 0, 0);
            p0 = new Vector3(ox + wSpan, yBot, oz);
            p1 = new Vector3(ox + wSpan, yBot, oz + wSpan);
            p2 = new Vector3(ox + wSpan, yTop, oz + wSpan);
            p3 = new Vector3(ox + wSpan, yTop, oz);
        }
        else if (dirX == -1)
        {
            normal = new Vector3(-1, 0, 0);
            p0 = new Vector3(ox, yBot, oz + wSpan);
            p1 = new Vector3(ox, yBot, oz);
            p2 = new Vector3(ox, yTop, oz);
            p3 = new Vector3(ox, yTop, oz + wSpan);
        }
        else if (dirZ == 1)
        {
            normal = new Vector3(0, 0, 1);
            p0 = new Vector3(ox + wSpan, yBot, oz + wSpan);
            p1 = new Vector3(ox,         yBot, oz + wSpan);
            p2 = new Vector3(ox,         yTop, oz + wSpan);
            p3 = new Vector3(ox + wSpan, yTop, oz + wSpan);
        }
        else
        {
            normal = new Vector3(0, 0, -1);
            p0 = new Vector3(ox,         yBot, oz);
            p1 = new Vector3(ox + wSpan, yBot, oz);
            p2 = new Vector3(ox + wSpan, yTop, oz);
            p3 = new Vector3(ox,         yTop, oz);
        }

        var baseIndex = verts.Count;
        verts.Add(p0); verts.Add(p1); verts.Add(p2); verts.Add(p3);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        colors.Add(Godot.Colors.White); colors.Add(Godot.Colors.White);
        colors.Add(Godot.Colors.White); colors.Add(Godot.Colors.White);
        uvs.Add(new Vector2(u0, v1));
        uvs.Add(new Vector2(u1, v1));
        uvs.Add(new Vector2(u1, v0));
        uvs.Add(new Vector2(u0, v0));
        indices.Add(baseIndex); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
        indices.Add(baseIndex); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);
    }

    private static void EmitFace(
        List<Vector3> verts,
        List<Vector3> normals,
        List<Color> colors,
        List<Vector2> uvs,
        List<int> indices,
        float ox, float oy, float oz, float tw, float th,
        Vector3 normal, Color color, int cell)
    {
        var baseIndex = verts.Count;
        var (u0, v0, u1, v1) = TileAtlas.CellUV(cell);
        Vector3 v0p, v1p, v2p, v3p;
        if (normal.X > 0.5f)      { v0p = new(ox+tw, oy, oz); v1p = new(ox+tw, oy, oz+tw); v2p = new(ox+tw, oy+th, oz+tw); v3p = new(ox+tw, oy+th, oz); }
        else if (normal.X < -0.5f) { v0p = new(ox, oy, oz+tw); v1p = new(ox, oy, oz); v2p = new(ox, oy+th, oz); v3p = new(ox, oy+th, oz+tw); }
        else if (normal.Y > 0.5f)  { v0p = new(ox, oy+th, oz); v1p = new(ox+tw, oy+th, oz); v2p = new(ox+tw, oy+th, oz+tw); v3p = new(ox, oy+th, oz+tw); }
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
