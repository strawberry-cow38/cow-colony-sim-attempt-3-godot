using System.Collections.Generic;
using Godot;
using CowColonySim.Sim.Grid;
using GArray = Godot.Collections.Array;

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

    public ArrayMesh? BuildMesh(ChunkSnapshot snapshot, int lodLevel)
    {
        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();

        var t = TileCoord.Tile;
        for (var ly = 0; ly < Chunk.Size; ly++)
        for (var lz = 0; lz < Chunk.Size; lz++)
        for (var lx = 0; lx < Chunk.Size; lx++)
        {
            var tile = snapshot[lx, ly, lz];
            if (tile.IsEmpty) continue;
            var color = TilePalette.ColorOf(tile.Kind);
            var ox = lx * t;
            var oy = ly * t;
            var oz = lz * t;

            foreach (var (dx, dy, dz, n) in Faces)
            {
                var nx = lx + dx;
                var ny = ly + dy;
                var nz = lz + dz;
                var neighborInside = (uint)nx < Chunk.Size && (uint)ny < Chunk.Size && (uint)nz < Chunk.Size;
                if (neighborInside && !snapshot[nx, ny, nz].IsEmpty) continue;

                EmitFace(verts, normals, colors, indices, ox, oy, oz, t, n, color);
            }
        }

        if (indices.Count == 0) return null;

        var arrays = new GArray();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
        arrays[(int)Mesh.ArrayType.Normal] = normals.ToArray();
        arrays[(int)Mesh.ArrayType.Color] = colors.ToArray();
        arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        return mesh;
    }

    private static void EmitFace(
        List<Vector3> verts,
        List<Vector3> normals,
        List<Color> colors,
        List<int> indices,
        float ox, float oy, float oz, float t,
        Vector3 normal, Color color)
    {
        var baseIndex = verts.Count;
        Vector3 v0, v1, v2, v3;
        if (normal.X > 0.5f)      { v0 = new(ox+t, oy, oz); v1 = new(ox+t, oy, oz+t); v2 = new(ox+t, oy+t, oz+t); v3 = new(ox+t, oy+t, oz); }
        else if (normal.X < -0.5f) { v0 = new(ox, oy, oz+t); v1 = new(ox, oy, oz); v2 = new(ox, oy+t, oz); v3 = new(ox, oy+t, oz+t); }
        else if (normal.Y > 0.5f)  { v0 = new(ox, oy+t, oz); v1 = new(ox+t, oy+t, oz); v2 = new(ox+t, oy+t, oz+t); v3 = new(ox, oy+t, oz+t); }
        else if (normal.Y < -0.5f) { v0 = new(ox, oy, oz+t); v1 = new(ox+t, oy, oz+t); v2 = new(ox+t, oy, oz); v3 = new(ox, oy, oz); }
        else if (normal.Z > 0.5f)  { v0 = new(ox+t, oy, oz+t); v1 = new(ox, oy, oz+t); v2 = new(ox, oy+t, oz+t); v3 = new(ox+t, oy+t, oz+t); }
        else                        { v0 = new(ox, oy, oz); v1 = new(ox+t, oy, oz); v2 = new(ox+t, oy+t, oz); v3 = new(ox, oy+t, oz); }

        verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        colors.Add(color); colors.Add(color); colors.Add(color); colors.Add(color);
        indices.Add(baseIndex); indices.Add(baseIndex + 1); indices.Add(baseIndex + 2);
        indices.Add(baseIndex); indices.Add(baseIndex + 2); indices.Add(baseIndex + 3);
    }
}
