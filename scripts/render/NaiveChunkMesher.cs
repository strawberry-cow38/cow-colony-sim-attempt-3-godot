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

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();
        var up = Vector3.Up;

        for (var lz = 0; lz < size; lz += step)
        for (var lx = 0; lx < size; lx += step)
        {
            var hi = -1;
            var kind = TileKind.Empty;
            for (var dz = 0; dz < step && lz + dz < size; dz++)
            for (var dx = 0; dx < step && lx + dx < size; dx++)
            {
                var h = colHeight[lx + dx, lz + dz];
                if (h > hi) { hi = h; kind = colKind[lx + dx, lz + dz]; }
            }
            if (hi < 0) continue;
            var cell = TileAtlas.CellForTop(kind, baseWx + lx, baseWz + lz);
            var (u0, v0, u1, v1) = TileAtlas.CellUV(cell);

            var wSpan = step * tw;
            var ox = lx * tw;
            var oy = (hi + 1) * th;
            var oz = lz * tw;

            var baseIndex = verts.Count;
            verts.Add(new Vector3(ox,         oy, oz));
            verts.Add(new Vector3(ox + wSpan, oy, oz));
            verts.Add(new Vector3(ox + wSpan, oy, oz + wSpan));
            verts.Add(new Vector3(ox,         oy, oz + wSpan));
            uvs.Add(new Vector2(u0, v0));
            uvs.Add(new Vector2(u1, v0));
            uvs.Add(new Vector2(u1, v1));
            uvs.Add(new Vector2(u0, v1));
            for (var k = 0; k < 4; k++) { normals.Add(up); colors.Add(Godot.Colors.White); }
            indices.Add(baseIndex); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
            indices.Add(baseIndex); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);
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
