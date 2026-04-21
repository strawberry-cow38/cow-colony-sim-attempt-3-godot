using Godot;
using GArray = Godot.Collections.Array;

namespace CowColonySim.Render;

/// <summary>
/// GPU heightmap terrain helpers: flat-grid patch mesh + R-channel heightmap
/// texture upload. Used when <see cref="GridRenderer.GpuTerrain"/> is enabled
/// to move far-tier vertex generation off the CPU and into a vertex shader.
/// </summary>
public static class GpuTerrain
{
    /// <summary>
    /// Build a flat grid mesh in local space. Output mesh has
    /// (cellsPerSide+1)^2 vertices spanning [0, patchWidthMeters] on X/Z, Y=0.
    /// </summary>
    /// <summary>
    /// Build a stepped grid mesh with unshared verts per cell plus cliff walls
    /// between adjacent cells. Every vert of a cell's top quad samples the same
    /// cell-center UV, so all 4 verts get the same height — producing flat
    /// plateaus with vertical cliffs where neighbors differ. Walls are emitted
    /// per-side (4 per cell) and backface-cull when the neighbor is taller.
    /// </summary>
    public static ArrayMesh BuildPatchMeshStepped(int cellsPerSide, float patchWidthMeters)
    {
        var cell = patchWidthMeters / cellsPerSide;
        var totalCells = cellsPerSide * cellsPerSide;
        // Per cell: 4 top + 4 walls × 4 = 20 verts; 2 top tris + 4×2 wall tris = 10 tris.
        var vertCount = totalCells * 20;
        var triCount = totalCells * 10;
        var vertexArr = new Vector3[vertCount];
        var uvArr = new Vector2[vertCount];
        var indices = new int[triCount * 3];

        var vi = 0;
        var ii = 0;
        var invN = 1.0f / cellsPerSide;
        for (var cz = 0; cz < cellsPerSide; cz++)
        for (var cx = 0; cx < cellsPerSide; cx++)
        {
            var x0 = cx * cell;
            var x1 = x0 + cell;
            var z0 = cz * cell;
            var z1 = z0 + cell;
            var ownUv = new Vector2((cx + 0.5f) * invN, (cz + 0.5f) * invN);

            // Top quad: 4 verts at corners, all tagged with ownUv so they land at
            // the same height. Winding faces +Y.
            var t0 = vi;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi++] = ownUv;
            indices[ii++] = t0 + 0; indices[ii++] = t0 + 2; indices[ii++] = t0 + 1;
            indices[ii++] = t0 + 1; indices[ii++] = t0 + 2; indices[ii++] = t0 + 3;

            // +X side: neighbor (cx+1, cz). Top verts sample ownUv, bottom verts
            // sample neighbor UV — when own > nbr the wall shows; otherwise the
            // triangle flips and is backface-culled.
            var nbrPxUv = cx + 1 < cellsPerSide
                ? new Vector2((cx + 1.5f) * invN, (cz + 0.5f) * invN)
                : ownUv;
            var wPx = vi;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi++] = nbrPxUv;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi++] = nbrPxUv;
            indices[ii++] = wPx + 0; indices[ii++] = wPx + 1; indices[ii++] = wPx + 2;
            indices[ii++] = wPx + 2; indices[ii++] = wPx + 1; indices[ii++] = wPx + 3;

            // -X side: neighbor (cx-1, cz).
            var nbrNxUv = cx - 1 >= 0
                ? new Vector2((cx - 0.5f) * invN, (cz + 0.5f) * invN)
                : ownUv;
            var wNx = vi;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi++] = nbrNxUv;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi++] = nbrNxUv;
            indices[ii++] = wNx + 0; indices[ii++] = wNx + 2; indices[ii++] = wNx + 1;
            indices[ii++] = wNx + 1; indices[ii++] = wNx + 2; indices[ii++] = wNx + 3;

            // +Z side: neighbor (cx, cz+1).
            var nbrPzUv = cz + 1 < cellsPerSide
                ? new Vector2((cx + 0.5f) * invN, (cz + 1.5f) * invN)
                : ownUv;
            var wPz = vi;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi++] = nbrPzUv;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi++] = nbrPzUv;
            indices[ii++] = wPz + 0; indices[ii++] = wPz + 2; indices[ii++] = wPz + 1;
            indices[ii++] = wPz + 1; indices[ii++] = wPz + 2; indices[ii++] = wPz + 3;

            // -Z side: neighbor (cx, cz-1).
            var nbrNzUv = cz - 1 >= 0
                ? new Vector2((cx + 0.5f) * invN, (cz - 0.5f) * invN)
                : ownUv;
            var wNz = vi;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi++] = nbrNzUv;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi++] = nbrNzUv;
            indices[ii++] = wNz + 0; indices[ii++] = wNz + 1; indices[ii++] = wNz + 2;
            indices[ii++] = wNz + 2; indices[ii++] = wNz + 1; indices[ii++] = wNz + 3;
        }

        var arrays = new GArray();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertexArr;
        arrays[(int)Mesh.ArrayType.TexUV] = uvArr;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.CustomAabb = new Aabb(
            new Vector3(-1f, -2f, -1f),
            new Vector3(patchWidthMeters + 2f, 260f, patchWidthMeters + 2f));
        return mesh;
    }

    public static ArrayMesh BuildPatchMesh(int cellsPerSide, float patchWidthMeters)
    {
        var verts = new int[(cellsPerSide + 1) * (cellsPerSide + 1)];
        var vertexArr = new Vector3[verts.Length];
        var uvArr = new Vector2[verts.Length];
        var cell = patchWidthMeters / cellsPerSide;
        for (var z = 0; z <= cellsPerSide; z++)
        for (var x = 0; x <= cellsPerSide; x++)
        {
            var idx = z * (cellsPerSide + 1) + x;
            vertexArr[idx] = new Vector3(x * cell, 0f, z * cell);
            uvArr[idx] = new Vector2((float)x / cellsPerSide, (float)z / cellsPerSide);
        }

        var indices = new int[cellsPerSide * cellsPerSide * 6];
        var ii = 0;
        for (var z = 0; z < cellsPerSide; z++)
        for (var x = 0; x < cellsPerSide; x++)
        {
            var a = z * (cellsPerSide + 1) + x;
            var b = a + 1;
            var c = a + (cellsPerSide + 1);
            var d = c + 1;
            indices[ii++] = a; indices[ii++] = c; indices[ii++] = b;
            indices[ii++] = b; indices[ii++] = c; indices[ii++] = d;
        }

        var arrays = new GArray();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertexArr;
        arrays[(int)Mesh.ArrayType.TexUV] = uvArr;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        // Godot's automatic AABB uses Y=0 for every vertex, which would cull the
        // patch whenever the camera's frustum doesn't clip Y=0. Inflate so the
        // GPU-displaced geometry stays visible.
        mesh.CustomAabb = new Aabb(
            new Vector3(-1f, -2f, -1f),
            new Vector3(patchWidthMeters + 2f, 260f, patchWidthMeters + 2f));
        return mesh;
    }

    /// <summary>
    /// Encode a float[,] heightmap into an ImageTexture. Heights are normalized
    /// by <paramref name="heightScaleMeters"/> and written as R8. Uses a raw
    /// byte buffer rather than per-pixel SetPixel to avoid main-thread stalls.
    /// </summary>
    public static ImageTexture BuildHeightmapTexture(
        float[,] heights, int sizeX, int sizeZ, float heightScaleMeters)
    {
        var buf = new byte[sizeX * sizeZ];
        var inv = heightScaleMeters > 0f ? 255f / heightScaleMeters : 0f;
        for (var z = 0; z < sizeZ; z++)
        for (var x = 0; x < sizeX; x++)
        {
            var v = (int)(heights[x, z] * inv);
            if (v < 0) v = 0;
            else if (v > 255) v = 255;
            buf[z * sizeX + x] = (byte)v;
        }
        var img = Image.CreateFromData(sizeX, sizeZ, false, Image.Format.R8, buf);
        return ImageTexture.CreateFromImage(img);
    }
}
