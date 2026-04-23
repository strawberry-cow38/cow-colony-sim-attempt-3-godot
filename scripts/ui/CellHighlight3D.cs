using Godot;
using CowColonySim.Sim;
using CowColonySim.Sim.Grid;

namespace CowColonySim.UI;

/// <summary>
/// Translucent border walls around the playable pocket — four thin,
/// semi-transparent quads on the north/south/east/west edges of the
/// 256×256 tile center cell, plus a yellow wireframe outline so the
/// edge stays visible when the wall blends into terrain.
///
/// Unshaded so it reads in fog or at night. Walls are drawn double-
/// sided (no backface cull) so they're visible whether the camera is
/// inside or outside the pocket.
/// </summary>
public partial class CellHighlight3D : Node3D
{
    // Vertical span in meters. Tall enough to clear the highest peaks
    // WorldGen can generate (~77m) with headroom for the camera pan.
    private const float BoxHeightMeters = 300f;

    // Wall translucency. Low alpha keeps the interior readable; a
    // slightly warm yellow matches the wireframe accent so the wall +
    // outline read as one object.
    private static readonly Color WallColor = new(1.0f, 0.92f, 0.10f, 0.12f);
    private static readonly Color WireColor = new(1.0f, 0.92f, 0.10f, 1.0f);

    public override void _Ready()
    {
        var tileW = SimConstants.TileWidthMeters;
        var pocketMeters = Cell.SizeTiles * tileW;
        var half = pocketMeters * 0.5f;

        var wireMesh = new MeshInstance3D
        {
            Mesh = BuildWireBox(),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = WireColor,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = new Vector3(-half, 0f, -half),
            Scale = new Vector3(pocketMeters, BoxHeightMeters, pocketMeters),
        };
        AddChild(wireMesh);

        var wallMat = new StandardMaterial3D
        {
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            AlbedoColor = WallColor,
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        AddChild(BuildWall(pocketMeters, new Vector3(0,  BoxHeightMeters * 0.5f, -half), new Vector3(0, 0, 0),           wallMat));
        AddChild(BuildWall(pocketMeters, new Vector3(0,  BoxHeightMeters * 0.5f,  half), new Vector3(0, Mathf.Pi, 0),    wallMat));
        AddChild(BuildWall(pocketMeters, new Vector3(-half, BoxHeightMeters * 0.5f, 0), new Vector3(0,  Mathf.Pi / 2, 0), wallMat));
        AddChild(BuildWall(pocketMeters, new Vector3( half, BoxHeightMeters * 0.5f, 0), new Vector3(0, -Mathf.Pi / 2, 0), wallMat));
    }

    private static MeshInstance3D BuildWall(float width, Vector3 pos, Vector3 rot, StandardMaterial3D mat)
    {
        return new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(width, BoxHeightMeters) },
            MaterialOverride = mat,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Position = pos,
            RotationOrder = EulerOrder.Yxz,
            Rotation = rot,
        };
    }

    private static ArrayMesh BuildWireBox()
    {
        var verts = new Vector3[]
        {
            new(0,0,0), new(1,0,0),
            new(1,0,0), new(1,0,1),
            new(1,0,1), new(0,0,1),
            new(0,0,1), new(0,0,0),

            new(0,1,0), new(1,1,0),
            new(1,1,0), new(1,1,1),
            new(1,1,1), new(0,1,1),
            new(0,1,1), new(0,1,0),

            new(0,0,0), new(0,1,0),
            new(1,0,0), new(1,1,0),
            new(1,0,1), new(1,1,1),
            new(0,0,1), new(0,1,1),
        };
        var arrays = new Godot.Collections.Array();
        arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = verts;
        var mesh = new ArrayMesh();
        mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
        mesh.CustomAabb = new Aabb(Vector3.Zero, Vector3.One);
        return mesh;
    }
}
