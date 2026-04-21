using System.Collections.Generic;
using Godot;
using CowColonySim.Sim;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

/// <summary>
/// Emits a single <see cref="MeshBuildResult"/> from a <see cref="TerrainSnapshot"/> —
/// two triangles per tile spanning the tile's four corner heights, producing
/// smooth slopes wherever corners step (AoE2 / Sims-style). Cliff wall geometry
/// is NOT emitted; a height-1 step renders as a steep slope. Buildings and
/// rock stay on the voxel mesher.
///
/// Kinds:
///  - <see cref="TileKind.Floor"/> → grass (cell 0..6, hashed by world XZ)
///  - <see cref="TileKind.Sand"/>  → sand (cell 15)
///  - <see cref="TileKind.Water"/> → white cell + water tint; top Y drops by
///    <c>WaterTopDropMeters</c> so the shore reads as a step above the waterline.
///    Matches the L0/L1 voxel mesher's shore treatment.
/// Any other kind (Solid, Empty) is skipped — the voxel mesher still owns rock
/// fills and buildings, and Empty shouldn't appear post-WorldGen.
/// </summary>
public sealed class HeightmapTerrainMesher
{
    public const float WaterTopDropMeters = 0.375f;

    public MeshBuildResult? Build(TerrainSnapshot snap)
    {
        const int s = TerrainSnapshot.Size;
        const float tw = SimConstants.TileWidthMeters;
        const float th = SimConstants.TileHeightMeters;
        var chunkBaseX = snap.ChunkX * s * tw;
        var chunkBaseZ = snap.ChunkZ * s * tw;

        var verts = new List<Vector3>();
        var normals = new List<Vector3>();
        var colors = new List<Color>();
        var uvs = new List<Vector2>();
        var indices = new List<int>();

        for (var lx = 0; lx < s; lx++)
        for (var lz = 0; lz < s; lz++)
        {
            var kind = (TileKind)snap.Kinds[lx, lz];
            if (kind != TileKind.Floor && kind != TileKind.Sand && kind != TileKind.Water)
                continue;

            var h00 = snap.Heights[lx,     lz    ];
            var h10 = snap.Heights[lx + 1, lz    ];
            var h11 = snap.Heights[lx + 1, lz + 1];
            var h01 = snap.Heights[lx,     lz + 1];

            var waterDrop = kind == TileKind.Water ? WaterTopDropMeters : 0f;
            var y00 = h00 * th - waterDrop;
            var y10 = h10 * th - waterDrop;
            var y11 = h11 * th - waterDrop;
            var y01 = h01 * th - waterDrop;

            var x0 = chunkBaseX + lx * tw;
            var z0 = chunkBaseZ + lz * tw;
            var x1 = x0 + tw;
            var z1 = z0 + tw;

            var wx = snap.ChunkX * s + lx;
            var wz = snap.ChunkZ * s + lz;
            var cell = TileAtlas.CellForTop(kind, wx, wz);
            var (u0, v0, u1, v1) = TileAtlas.CellUV(cell);
            var tint = TileAtlas.TintFor(kind);

            var vi = verts.Count;
            var p00 = new Vector3(x0, y00, z0);
            var p10 = new Vector3(x1, y10, z0);
            var p11 = new Vector3(x1, y11, z1);
            var p01 = new Vector3(x0, y01, z1);
            verts.Add(p00); verts.Add(p10); verts.Add(p11); verts.Add(p01);

            // Flat normal per tile — the cross product of quad diagonals.
            // Both triangles share this normal so adjacent tiles with
            // matching corner heights visually blend (same Y derivatives)
            // without needing vertex sharing across tile borders.
            var nrm = (p10 - p00).Cross(p01 - p00).Normalized();
            if (nrm.LengthSquared() < 1e-6f) nrm = Vector3.Up;
            normals.Add(nrm); normals.Add(nrm); normals.Add(nrm); normals.Add(nrm);

            colors.Add(tint); colors.Add(tint); colors.Add(tint); colors.Add(tint);

            uvs.Add(new Vector2(u0, v0));
            uvs.Add(new Vector2(u1, v0));
            uvs.Add(new Vector2(u1, v1));
            uvs.Add(new Vector2(u0, v1));

            // Two tris — wind CCW from above (Godot Y-up, X→right, Z→back).
            indices.Add(vi + 0); indices.Add(vi + 2); indices.Add(vi + 1);
            indices.Add(vi + 0); indices.Add(vi + 3); indices.Add(vi + 2);
        }

        if (indices.Count == 0) return null;

        return new MeshBuildResult
        {
            Verts = verts.ToArray(),
            Normals = normals.ToArray(),
            Colors = colors.ToArray(),
            Uvs = uvs.ToArray(),
            Indices = indices.ToArray(),
            Revision = snap.Revision,
            LodLevel = 0,
        };
    }
}
