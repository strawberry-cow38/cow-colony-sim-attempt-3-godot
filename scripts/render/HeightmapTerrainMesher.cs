using System.Collections.Generic;
using Godot;
using CowColonySim.Sim;
using CowColonySim.Sim.Grid;

namespace CowColonySim.Render;

/// <summary>
/// Emits a single <see cref="MeshBuildResult"/> from a <see cref="TerrainSnapshot"/>.
/// One flat-shaded quad per tile uses the tile's own four render corners
/// (SW/SE/NE/NW). Cliff walls are emergent: wherever an edge-shared corner
/// pair disagrees with the adjacent tile's opposite corners, a vertical quad
/// is emitted spanning the gap. Worldgen is responsible for clamping corners
/// via the Cap rule so smooth terrain blends and cliffs produce walls.
///
/// Kinds:
///  - <see cref="TileKind.Floor"/> → grass (cell 0..6, hashed by world XZ)
///  - <see cref="TileKind.Sand"/>  → sand (cell 15)
///  - <see cref="TileKind.Water"/> → white cell + water tint; top Y drops by
///    <c>WaterTopDropMeters</c> so the shore reads as a step above the
///    waterline.
/// Any other kind (Solid, Empty) is skipped.
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

            var hSW = snap.Corners[lx, lz, TerrainChunk.SW];
            var hSE = snap.Corners[lx, lz, TerrainChunk.SE];
            var hNE = snap.Corners[lx, lz, TerrainChunk.NE];
            var hNW = snap.Corners[lx, lz, TerrainChunk.NW];

            var waterDrop = kind == TileKind.Water ? WaterTopDropMeters : 0f;
            var ySW = hSW * th - waterDrop;
            var ySE = hSE * th - waterDrop;
            var yNE = hNE * th - waterDrop;
            var yNW = hNW * th - waterDrop;

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
            var pSW = new Vector3(x0, ySW, z0);
            var pSE = new Vector3(x1, ySE, z0);
            var pNE = new Vector3(x1, yNE, z1);
            var pNW = new Vector3(x0, yNW, z1);
            verts.Add(pSW); verts.Add(pSE); verts.Add(pNE); verts.Add(pNW);

            // Flat normal per tile — right-handed cross of (SW→NW) × (SW→SE)
            // points +Y for a flat quad, tilting toward uphill corners for a
            // ramp. Shared across both triangles so adjacent tiles with
            // matching facing corners blend seamlessly.
            var nrm = (pNW - pSW).Cross(pSE - pSW).Normalized();
            if (nrm.LengthSquared() < 1e-6f) nrm = Vector3.Up;
            normals.Add(nrm); normals.Add(nrm); normals.Add(nrm); normals.Add(nrm);

            colors.Add(tint); colors.Add(tint); colors.Add(tint); colors.Add(tint);

            uvs.Add(new Vector2(u0, v0));
            uvs.Add(new Vector2(u1, v0));
            uvs.Add(new Vector2(u1, v1));
            uvs.Add(new Vector2(u0, v1));

            indices.Add(vi + 0); indices.Add(vi + 1); indices.Add(vi + 2);
            indices.Add(vi + 0); indices.Add(vi + 2); indices.Add(vi + 3);

            // Cliff walls. Each tile handles its east (+X) and north (+Z)
            // edge. The shared corner pair across the edge is compared to
            // the neighbor tile's opposite corners; any disagreement yields
            // a vertical quad. West / south walls are emitted by the
            // respective western / southern neighbor tile, so no double-up.
            short eSW, eNW;
            if (lx + 1 < s)
            {
                eSW = snap.Corners[lx + 1, lz, TerrainChunk.SW];
                eNW = snap.Corners[lx + 1, lz, TerrainChunk.NW];
            }
            else
            {
                eSW = snap.EastRim[lz, 0];
                eNW = snap.EastRim[lz, 1];
            }
            // Water tiles drop their top by WaterTopDropMeters; walls need
            // to match that drop for water-vs-water seams or match the
            // neighbor's (undropped) Y for water-vs-land seams. Simplest:
            // apply my own waterDrop to my top edge and none to the rim.
            EmitWallIfGap(
                verts, normals, colors, uvs, indices,
                topA: new Vector3(x1, hSE * th - waterDrop, z0),
                topB: new Vector3(x1, hNE * th - waterDrop, z1),
                botA: new Vector3(x1, eSW * th, z0),
                botB: new Vector3(x1, eNW * th, z1),
                faceDirPlus: new Vector3(1f, 0f, 0f),
                kind);

            short nSW, nSE;
            if (lz + 1 < s)
            {
                nSW = snap.Corners[lx, lz + 1, TerrainChunk.SW];
                nSE = snap.Corners[lx, lz + 1, TerrainChunk.SE];
            }
            else
            {
                nSW = snap.NorthRim[lx, 0];
                nSE = snap.NorthRim[lx, 1];
            }
            EmitWallIfGap(
                verts, normals, colors, uvs, indices,
                topA: new Vector3(x1, hNE * th - waterDrop, z1),
                topB: new Vector3(x0, hNW * th - waterDrop, z1),
                botA: new Vector3(x1, nSE * th, z1),
                botB: new Vector3(x0, nSW * th, z1),
                faceDirPlus: new Vector3(0f, 0f, 1f),
                kind);
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

    // Emits a vertical cliff quad between two (top, bot) corner pairs if the
    // top is strictly higher than the bottom on at least one side. Winds CCW
    // as seen from the +face direction (the outward-facing side). When the
    // "top" is actually lower than "bot" (neighbor higher than self), swaps
    // roles and flips the face normal so the outward side still points away
    // from the taller tile.
    private static void EmitWallIfGap(
        List<Vector3> verts, List<Vector3> normals, List<Color> colors,
        List<Vector2> uvs, List<int> indices,
        Vector3 topA, Vector3 topB, Vector3 botA, Vector3 botB,
        Vector3 faceDirPlus, TileKind kind)
    {
        var selfUpperA = topA.Y > botA.Y;
        var selfUpperB = topB.Y > botB.Y;
        var selfLowerA = topA.Y < botA.Y;
        var selfLowerB = topB.Y < botB.Y;

        if ((selfUpperA || selfUpperB) && !(selfLowerA || selfLowerB))
        {
            // Self is the upper tile → wall faces +faceDirPlus (outward from
            // self, toward the lower neighbor's ground where the player views
            // the cliff). B side first so CCW winding from +faceDirPlus side
            // matches the supplied normal under Godot's front-face rule.
            EmitCliffQuad(verts, normals, colors, uvs, indices,
                bl: botB, tl: topB, tr: topA, br: botA,
                normal: faceDirPlus, kind);
        }
        else if ((selfLowerA || selfLowerB) && !(selfUpperA || selfUpperB))
        {
            // Neighbor is the upper tile → wall faces -faceDirPlus (outward
            // from the cliff, toward self's lower ground). Top of wall =
            // neighbor corners (our "bot" params, higher here); bottom = own
            // edge ("top" params).
            EmitCliffQuad(verts, normals, colors, uvs, indices,
                bl: topA, tl: botA, tr: botB, br: topB,
                normal: -faceDirPlus, kind);
        }
        // else: either flat (no gap) or a twisted corner (one side self-upper,
        // other side self-lower). Skip twisted corners — pathological edge
        // case not produced by the Cap rule on well-formed worldgen.
    }

    private static void EmitCliffQuad(
        List<Vector3> verts, List<Vector3> normals, List<Color> colors,
        List<Vector2> uvs, List<int> indices,
        Vector3 bl, Vector3 tl, Vector3 tr, Vector3 br,
        Vector3 normal, TileKind kind)
    {
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
