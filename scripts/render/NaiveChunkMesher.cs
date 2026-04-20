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

    public MeshBuildResult? BuildMeshData(ChunkSnapshot snapshot, int lodLevel)
    {
        return lodLevel switch
        {
            <= 0 => BuildFullVoxel(snapshot),
            1    => BuildHeightmap(snapshot, step: 1),
            _    => BuildHeightmap(snapshot, step: 4),
        };
    }

    private static MeshBuildResult? BuildFullVoxel(ChunkSnapshot snapshot)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();

        var tw = TileCoord.TileW;
        var th = TileCoord.TileH;
        for (var ly = 0; ly < Chunk.Size; ly++)
        for (var lz = 0; lz < Chunk.Size; lz++)
        for (var lx = 0; lx < Chunk.Size; lx++)
        {
            var tile = snapshot[lx, ly, lz];
            if (tile.IsEmpty) continue;
            var color = TilePalette.ColorOf(tile.Kind);
            var ox = lx * tw;
            var oy = ly * th;
            var oz = lz * tw;

            foreach (var (dx, dy, dz, n) in Faces)
            {
                var nx = lx + dx;
                var ny = ly + dy;
                var nz = lz + dz;
                var neighborInside = (uint)nx < Chunk.Size && (uint)ny < Chunk.Size && (uint)nz < Chunk.Size;
                if (neighborInside && !snapshot[nx, ny, nz].IsEmpty) continue;

                EmitFace(verts, normals, colors, indices, ox, oy, oz, tw, th, n, color);
            }
        }

        if (indices.Count == 0) return null;

        return new MeshBuildResult
        {
            Verts = verts.ToArray(),
            Normals = normals.ToArray(),
            Colors = colors.ToArray(),
            Indices = indices.ToArray(),
            Revision = snapshot.Revision,
            LodLevel = 0,
        };
    }

    private static MeshBuildResult? BuildHeightmap(ChunkSnapshot snapshot, int step)
    {
        var size = Chunk.Size;
        var tw = TileCoord.TileW;
        var th = TileCoord.TileH;

        // Tallest non-empty ly per (lx,lz) column, plus color at that cell.
        var colHeight = new int[size, size];
        var colColor = new Color[size, size];
        for (var lz = 0; lz < size; lz++)
        for (var lx = 0; lx < size; lx++)
        {
            var top = -1;
            for (var ly = size - 1; ly >= 0; ly--)
            {
                var t = snapshot[lx, ly, lz];
                if (!t.IsEmpty) { top = ly; colColor[lx, lz] = TilePalette.ColorOf(t.Kind); break; }
            }
            colHeight[lx, lz] = top;
        }

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();
        var up = Vector3.Up;

        // Emit one flat top quad per stepXstep group, using max height in the group.
        for (var lz = 0; lz < size; lz += step)
        for (var lx = 0; lx < size; lx += step)
        {
            var hi = -1;
            Color avg = default;
            var count = 0;
            for (var dz = 0; dz < step && lz + dz < size; dz++)
            for (var dx = 0; dx < step && lx + dx < size; dx++)
            {
                var h = colHeight[lx + dx, lz + dz];
                if (h > hi) hi = h;
                if (h >= 0) { avg += colColor[lx + dx, lz + dz]; count++; }
            }
            if (hi < 0) continue;
            var color = count > 0 ? avg / count : new Color(0.3f, 0.5f, 0.3f);
            color.A = 1f;

            var wSpan = step * tw;
            var ox = lx * tw;
            var oy = (hi + 1) * th;
            var oz = lz * tw;

            var baseIndex = verts.Count;
            verts.Add(new Vector3(ox,         oy, oz));
            verts.Add(new Vector3(ox + wSpan, oy, oz));
            verts.Add(new Vector3(ox + wSpan, oy, oz + wSpan));
            verts.Add(new Vector3(ox,         oy, oz + wSpan));
            for (var k = 0; k < 4; k++) { normals.Add(up); colors.Add(color); }
            indices.Add(baseIndex); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
            indices.Add(baseIndex); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);
        }

        if (indices.Count == 0) return null;

        return new MeshBuildResult
        {
            Verts = verts.ToArray(),
            Normals = normals.ToArray(),
            Colors = colors.ToArray(),
            Indices = indices.ToArray(),
            Revision = snapshot.Revision,
            LodLevel = step == 1 ? 1 : 2,
        };
    }

    private static void EmitFace(
        List<Vector3> verts,
        List<Vector3> normals,
        List<Color> colors,
        List<int> indices,
        float ox, float oy, float oz, float tw, float th,
        Vector3 normal, Color color)
    {
        var baseIndex = verts.Count;
        Vector3 v0, v1, v2, v3;
        if (normal.X > 0.5f)      { v0 = new(ox+tw, oy, oz); v1 = new(ox+tw, oy, oz+tw); v2 = new(ox+tw, oy+th, oz+tw); v3 = new(ox+tw, oy+th, oz); }
        else if (normal.X < -0.5f) { v0 = new(ox, oy, oz+tw); v1 = new(ox, oy, oz); v2 = new(ox, oy+th, oz); v3 = new(ox, oy+th, oz+tw); }
        else if (normal.Y > 0.5f)  { v0 = new(ox, oy+th, oz); v1 = new(ox+tw, oy+th, oz); v2 = new(ox+tw, oy+th, oz+tw); v3 = new(ox, oy+th, oz+tw); }
        else if (normal.Y < -0.5f) { v0 = new(ox, oy, oz+tw); v1 = new(ox+tw, oy, oz+tw); v2 = new(ox+tw, oy, oz); v3 = new(ox, oy, oz); }
        else if (normal.Z > 0.5f)  { v0 = new(ox+tw, oy, oz+tw); v1 = new(ox, oy, oz+tw); v2 = new(ox, oy+th, oz+tw); v3 = new(ox+tw, oy+th, oz+tw); }
        else                        { v0 = new(ox, oy, oz); v1 = new(ox+tw, oy, oz); v2 = new(ox+tw, oy+th, oz); v3 = new(ox, oy+th, oz); }

        verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
        indices.Add(baseIndex); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
        indices.Add(baseIndex); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);
    }
}
