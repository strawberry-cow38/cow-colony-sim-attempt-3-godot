using System.Collections.Generic;
using Godot;
using CowColonySim.Sim;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

/// <summary>
/// Emits a single <see cref="MeshBuildResult"/> from a <see cref="TerrainSnapshot"/> —
/// two triangles per tile spanning the tile's four corner heights, producing
/// smooth slopes wherever corners step (AoE2 / Sims-style). Where the snapshot
/// flags a cliff edge, the upper tile emits a vertical wall quad and the lower
/// tile pulls its W / N corners down to its own floor height — giving true
/// vertical faces above <see cref="WorldGen.CliffMinDelta"/>. Buildings and
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

            var mask = snap.CliffMask[lx, lz];

            short h00 = snap.Heights[lx,     lz    ];
            short h10 = snap.Heights[lx + 1, lz    ];
            short h11 = snap.Heights[lx + 1, lz + 1];
            short h01 = snap.Heights[lx,     lz + 1];

            // W cliff: I am the lower side. Pull my west corners (SW, NW)
            // down from the upper shared height to my own cliff-lower.
            if ((mask & TerrainSnapshot.CliffBitW) != 0)
            {
                var lower = snap.CliffLowerW[lx, lz];
                h00 = lower;
                h01 = lower;
            }
            // N cliff: I am lower on N. Pull NW and NE down.
            if ((mask & TerrainSnapshot.CliffBitN) != 0)
            {
                var lower = snap.CliffLowerN[lx, lz];
                h01 = lower;
                h11 = lower;
            }

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

            // Flat normal per tile — right-handed cross of (SW→NW) × (SW→SE)
            // points +Y for a flat quad, tilting toward uphill corners for a
            // ramp. Both triangles share this normal so adjacent tiles with
            // matching corner heights visually blend (same Y derivatives)
            // without needing vertex sharing across tile borders.
            var nrm = (p01 - p00).Cross(p10 - p00).Normalized();
            if (nrm.LengthSquared() < 1e-6f) nrm = Vector3.Up;
            normals.Add(nrm); normals.Add(nrm); normals.Add(nrm); normals.Add(nrm);

            colors.Add(tint); colors.Add(tint); colors.Add(tint); colors.Add(tint);

            uvs.Add(new Vector2(u0, v0));
            uvs.Add(new Vector2(u1, v0));
            uvs.Add(new Vector2(u1, v1));
            uvs.Add(new Vector2(u0, v1));

            // Two tris: SW→SE→NE, SW→NE→NW. Matches NaiveChunkMesher's top-
            // face winding so back-face cull shows the upward surface.
            indices.Add(vi + 0); indices.Add(vi + 1); indices.Add(vi + 2);
            indices.Add(vi + 0); indices.Add(vi + 2); indices.Add(vi + 3);

            // Cliff faces. Upper tile owns its +X / +Z flagged edges — draws
            // a single vertical quad from stored upper corners down to
            // CliffLowerE / S. Lower side's W / N bit only pulls its own
            // corners down; it never emits a face.
            if ((mask & TerrainSnapshot.CliffBitE) != 0)
            {
                var upperSE = snap.Heights[lx + 1, lz    ] * th;
                var upperNE = snap.Heights[lx + 1, lz + 1] * th;
                var lowerY  = snap.CliffLowerE[lx, lz] * th;
                EmitCliffFace(verts, normals, colors, uvs, indices,
                    new Vector3(x1, lowerY,  z0),
                    new Vector3(x1, upperSE, z0),
                    new Vector3(x1, upperNE, z1),
                    new Vector3(x1, lowerY,  z1),
                    new Vector3(1f, 0f, 0f),
                    kind);
            }
            if ((mask & TerrainSnapshot.CliffBitS) != 0)
            {
                var upperSW = snap.Heights[lx,     lz + 1] * th;
                var upperSE = snap.Heights[lx + 1, lz + 1] * th;
                var lowerY  = snap.CliffLowerS[lx, lz] * th;
                EmitCliffFace(verts, normals, colors, uvs, indices,
                    new Vector3(x1, lowerY,  z1),
                    new Vector3(x1, upperSE, z1),
                    new Vector3(x0, upperSW, z1),
                    new Vector3(x0, lowerY,  z1),
                    new Vector3(0f, 0f, 1f),
                    kind);
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
            Revision = snap.Revision,
            LodLevel = 0,
        };
    }

    // Vertical cliff face. Four corners are passed in order:
    //   bl (lower-start) → tl (upper-start) → tr (upper-end) → br (lower-end)
    // so the two triangles (bl, tl, tr) and (bl, tr, br) wind CCW as seen
    // from <paramref name="normal"/> — which is the outward direction the
    // face should be visible from. Face is degenerate (zero-area) when the
    // two upper corners are both at or below the lower Y; skipped in that
    // case so we don't emit a flipped / zero-area quad.
    private static void EmitCliffFace(
        List<Vector3> verts, List<Vector3> normals, List<Color> colors,
        List<Vector2> uvs, List<int> indices,
        Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br,
        Vector3 normal, TileKind kind)
    {
        if (tl.Y <= bl.Y && tr.Y <= br.Y) return;

        var cell = TileAtlas.CellForSide(kind);
        var (u0, v0, u1, v1) = TileAtlas.CellUV(cell);
        var tint = TileAtlas.TintFor(kind);

        var vi = verts.Count;
        verts.Add(bl); verts.Add(tl); verts.Add(tr); verts.Add(br);
        normals.Add(normal); normals.Add(normal); normals.Add(normal); normals.Add(normal);
        colors.Add(tint); colors.Add(tint); colors.Add(tint); colors.Add(tint);
        uvs.Add(new Vector2(u0, v1));
        uvs.Add(new Vector2(u0, v0));
        uvs.Add(new Vector2(u1, v0));
        uvs.Add(new Vector2(u1, v1));
        indices.Add(vi + 0); indices.Add(vi + 1); indices.Add(vi + 2);
        indices.Add(vi + 0); indices.Add(vi + 2); indices.Add(vi + 3);
    }
}
