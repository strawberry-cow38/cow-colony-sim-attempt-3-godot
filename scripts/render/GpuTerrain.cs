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
    /// per-side (4 per cell). The heightmap texture has a 1-pixel border in
    /// every direction carrying neighbor-group tile heights, so all four wall
    /// sides (not just +X/+Z) can sample true neighbor height at the patch
    /// edge; otherwise cliffs at group seams where our side is lower would
    /// have no wall and show sky through the gap.
    /// </summary>
    public static ArrayMesh BuildPatchMeshStepped(int cellsPerSide, float patchWidthMeters, int heightmapInteriorSize)
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
        // Texture is heightmapInteriorSize+2 pixels per side: own tiles at
        // [1..S], neighbor borders at 0 and S+1. Cell cx spans S/N pixels of
        // own tiles; cell-center sample lands at pixel (cx+0.5)*S/N + 1, which
        // in UV space is ((cx+0.5)*S/N + 1) / (S+2).
        float S = heightmapInteriorSize;
        float invTex = 1.0f / (S + 2f);
        float pixPerCell = S / cellsPerSide;
        // Center verts around the MeshInstance origin so Godot's
        // VisibilityRangeBegin (measured cam→origin, not cam→AABB) corresponds
        // to the patch midpoint rather than a corner. Straddling groups would
        // otherwise fade out whenever the near corner dipped below Begin,
        // hiding far-side chunks with no near-tier coverage and leaving holes.
        var half = patchWidthMeters * 0.5f;
        for (var cz = 0; cz < cellsPerSide; cz++)
        for (var cx = 0; cx < cellsPerSide; cx++)
        {
            var x0 = cx * cell - half;
            var x1 = x0 + cell;
            var z0 = cz * cell - half;
            var z1 = z0 + cell;
            var ownUv = new Vector2(
                ((cx + 0.5f) * pixPerCell + 1f) * invTex,
                ((cz + 0.5f) * pixPerCell + 1f) * invTex);

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
            // sample neighbor UV. At the group edge (cx=N-1) the UV walks past
            // the interior, clamping to pixel S+1 = +X neighbor's first tile.
            var nbrPxUv = new Vector2(
                ((cx + 1.5f) * pixPerCell + 1f) * invTex,
                ((cz + 0.5f) * pixPerCell + 1f) * invTex);
            var wPx = vi;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi++] = nbrPxUv;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi++] = nbrPxUv;
            indices[ii++] = wPx + 0; indices[ii++] = wPx + 1; indices[ii++] = wPx + 2;
            indices[ii++] = wPx + 2; indices[ii++] = wPx + 1; indices[ii++] = wPx + 3;

            // -X side: neighbor (cx-1, cz). At cx=0 the UV walks negative and
            // clamps to pixel 0 = -X neighbor's last tile, so the wall is never
            // degenerate at the patch left edge.
            var nbrNxUv = new Vector2(
                ((cx - 0.5f) * pixPerCell + 1f) * invTex,
                ((cz + 0.5f) * pixPerCell + 1f) * invTex);
            var wNx = vi;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi++] = nbrNxUv;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi++] = nbrNxUv;
            indices[ii++] = wNx + 0; indices[ii++] = wNx + 2; indices[ii++] = wNx + 1;
            indices[ii++] = wNx + 1; indices[ii++] = wNx + 2; indices[ii++] = wNx + 3;

            // +Z side: neighbor (cx, cz+1).
            var nbrPzUv = new Vector2(
                ((cx + 0.5f) * pixPerCell + 1f) * invTex,
                ((cz + 1.5f) * pixPerCell + 1f) * invTex);
            var wPz = vi;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi++] = ownUv;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi++] = nbrPzUv;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi++] = nbrPzUv;
            indices[ii++] = wPz + 0; indices[ii++] = wPz + 2; indices[ii++] = wPz + 1;
            indices[ii++] = wPz + 1; indices[ii++] = wPz + 2; indices[ii++] = wPz + 3;

            // -Z side: neighbor (cx, cz-1).
            var nbrNzUv = new Vector2(
                ((cx + 0.5f) * pixPerCell + 1f) * invTex,
                ((cz - 0.5f) * pixPerCell + 1f) * invTex);
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
            new Vector3(-half - 1f, -2f, -half - 1f),
            new Vector3(patchWidthMeters + 2f, 260f, patchWidthMeters + 2f));
        return mesh;
    }

    /// <summary>
    /// Stepped grid mesh for the corner-heightmap pipeline. Per cell, the top
    /// quad has 4 distinct corner UVs sampling SW/SE/NW/NE pixels of a 2x-
    /// density heightmap — when those four corners disagree the top becomes a
    /// ramp (smooth terrain); when adjacent cells' facing corners disagree the
    /// wall quads pick up a height delta and render as cliffs. Walls sample
    /// neighbor-cell corner pixels for bottom verts, so height disagreement
    /// across cells emerges as vertical geometry automatically.
    ///
    /// Layout of the heightmap texture (cornersSize x cornersSize, nearest filter):
    ///   pixel (2cx+1, 2cz+1) = cell(cx,cz).SW   pixel (2cx+2, 2cz+1) = .SE
    ///   pixel (2cx+1, 2cz+2) = cell(cx,cz).NW   pixel (2cx+2, 2cz+2) = .NE
    /// 1-pixel border around all four sides carries the neighbor group's
    /// edge corners so seam walls have correct bottom samples.
    ///
    /// Kindmap is 1-per-cell (kindsSize = cellsPerSide+2, 1px border) sampled
    /// via UV2 from the mesh; each vertex of a cell carries the same kind UV.
    /// </summary>
    public static ArrayMesh BuildCornerPatchMeshStepped(
        int cellsPerSide, float patchWidthMeters, int cornersSize, int kindsSize)
    {
        var cell = patchWidthMeters / cellsPerSide;
        var totalCells = cellsPerSide * cellsPerSide;
        var vertCount = totalCells * 20;
        var triCount = totalCells * 10;
        var vertexArr = new Vector3[vertCount];
        var uvArr = new Vector2[vertCount];
        var uv2Arr = new Vector2[vertCount];
        var indices = new int[triCount * 3];

        var invC = 1f / cornersSize;
        var invK = 1f / kindsSize;
        var half = patchWidthMeters * 0.5f;
        var vi = 0;
        var ii = 0;
        for (var cz = 0; cz < cellsPerSide; cz++)
        for (var cx = 0; cx < cellsPerSide; cx++)
        {
            var x0 = cx * cell - half;
            var x1 = x0 + cell;
            var z0 = cz * cell - half;
            var z1 = z0 + cell;

            var uSW = new Vector2((2f*cx + 1.5f) * invC, (2f*cz + 1.5f) * invC);
            var uSE = new Vector2((2f*cx + 2.5f) * invC, (2f*cz + 1.5f) * invC);
            var uNW = new Vector2((2f*cx + 1.5f) * invC, (2f*cz + 2.5f) * invC);
            var uNE = new Vector2((2f*cx + 2.5f) * invC, (2f*cz + 2.5f) * invC);
            var uKind = new Vector2((cx + 1.5f) * invK, (cz + 1.5f) * invK);

            var uPxSW = new Vector2((2f*cx + 3.5f) * invC, (2f*cz + 1.5f) * invC);
            var uPxNW = new Vector2((2f*cx + 3.5f) * invC, (2f*cz + 2.5f) * invC);
            var uNxSE = new Vector2((2f*cx + 0.5f) * invC, (2f*cz + 1.5f) * invC);
            var uNxNE = new Vector2((2f*cx + 0.5f) * invC, (2f*cz + 2.5f) * invC);
            var uPzSW = new Vector2((2f*cx + 1.5f) * invC, (2f*cz + 3.5f) * invC);
            var uPzSE = new Vector2((2f*cx + 2.5f) * invC, (2f*cz + 3.5f) * invC);
            var uNzNW = new Vector2((2f*cx + 1.5f) * invC, (2f*cz + 0.5f) * invC);
            var uNzNE = new Vector2((2f*cx + 2.5f) * invC, (2f*cz + 0.5f) * invC);

            var t0 = vi;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi] = uSW; uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi] = uSE; uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi] = uNW; uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi] = uNE; uv2Arr[vi++] = uKind;
            indices[ii++] = t0 + 0; indices[ii++] = t0 + 2; indices[ii++] = t0 + 1;
            indices[ii++] = t0 + 1; indices[ii++] = t0 + 2; indices[ii++] = t0 + 3;

            var wPx = vi;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi] = uSE;   uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi] = uNE;   uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi] = uPxSW; uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi] = uPxNW; uv2Arr[vi++] = uKind;
            indices[ii++] = wPx + 0; indices[ii++] = wPx + 1; indices[ii++] = wPx + 2;
            indices[ii++] = wPx + 2; indices[ii++] = wPx + 1; indices[ii++] = wPx + 3;

            var wNx = vi;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi] = uSW;   uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi] = uNW;   uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi] = uNxSE; uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi] = uNxNE; uv2Arr[vi++] = uKind;
            indices[ii++] = wNx + 0; indices[ii++] = wNx + 2; indices[ii++] = wNx + 1;
            indices[ii++] = wNx + 1; indices[ii++] = wNx + 2; indices[ii++] = wNx + 3;

            var wPz = vi;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi] = uNW;   uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi] = uNE;   uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x0, 0, z1); uvArr[vi] = uPzSW; uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x1, 0, z1); uvArr[vi] = uPzSE; uv2Arr[vi++] = uKind;
            indices[ii++] = wPz + 0; indices[ii++] = wPz + 2; indices[ii++] = wPz + 1;
            indices[ii++] = wPz + 1; indices[ii++] = wPz + 2; indices[ii++] = wPz + 3;

            var wNz = vi;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi] = uSW;   uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi] = uSE;   uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x0, 0, z0); uvArr[vi] = uNzNW; uv2Arr[vi++] = uKind;
            vertexArr[vi] = new Vector3(x1, 0, z0); uvArr[vi] = uNzNE; uv2Arr[vi++] = uKind;
            indices[ii++] = wNz + 0; indices[ii++] = wNz + 1; indices[ii++] = wNz + 2;
            indices[ii++] = wNz + 2; indices[ii++] = wNz + 1; indices[ii++] = wNz + 3;
        }

        var arrays = new GArray();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = vertexArr;
        arrays[(int)Mesh.ArrayType.TexUV] = uvArr;
        arrays[(int)Mesh.ArrayType.TexUV2] = uv2Arr;
        arrays[(int)Mesh.ArrayType.Index] = indices;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.CustomAabb = new Aabb(
            new Vector3(-half - 1f, -2f, -half - 1f),
            new Vector3(patchWidthMeters + 2f, 260f, patchWidthMeters + 2f));
        return mesh;
    }

    public static ArrayMesh BuildPatchMesh(int cellsPerSide, float patchWidthMeters)
    {
        var verts = new int[(cellsPerSide + 1) * (cellsPerSide + 1)];
        var vertexArr = new Vector3[verts.Length];
        var uvArr = new Vector2[verts.Length];
        var cell = patchWidthMeters / cellsPerSide;
        var half = patchWidthMeters * 0.5f;
        for (var z = 0; z <= cellsPerSide; z++)
        for (var x = 0; x <= cellsPerSide; x++)
        {
            var idx = z * (cellsPerSide + 1) + x;
            vertexArr[idx] = new Vector3(x * cell - half, 0f, z * cell - half);
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
            new Vector3(-half - 1f, -2f, -half - 1f),
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

    /// <summary>
    /// Upload a per-cell TileKind byte map alongside the heightmap so the
    /// terrain shader can branch on water / sand without another sampling
    /// scheme. Raw byte copy — no scaling.
    /// </summary>
    public static ImageTexture BuildKindmapTexture(byte[,] kinds, int sizeX, int sizeZ)
    {
        var buf = new byte[sizeX * sizeZ];
        for (var z = 0; z < sizeZ; z++)
        for (var x = 0; x < sizeX; x++)
            buf[z * sizeX + x] = kinds[x, z];
        var img = Image.CreateFromData(sizeX, sizeZ, false, Image.Format.R8, buf);
        return ImageTexture.CreateFromImage(img);
    }
}
