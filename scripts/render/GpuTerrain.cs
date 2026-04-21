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
