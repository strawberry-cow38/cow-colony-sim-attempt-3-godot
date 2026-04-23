using Godot;
using CowColonySim.Sim;
using CowColonySim.Sim.Grid;

namespace CowColonySim.UI;

/// <summary>
/// Wireframe box outlining the playable pocket — a single 256×256 tile
/// region centered on the world origin, representing the overworld cell
/// the player is currently inside. With streaming gone, there is exactly
/// one pocket per session; the box is fixed geometry, no per-frame
/// re-hit-testing.
///
/// Uses an unshaded material so it stays readable in fog or at night.
/// </summary>
public partial class CellHighlight3D : Node3D
{
    // Vertical span of the box in meters. The pocket covers full world
    // height; a fixed span that comfortably clears any generated terrain
    // keeps the outline in view without scanning for a max elevation.
    private const float BoxHeightMeters = 300f;

    private MeshInstance3D? _mesh;

    public override void _Ready()
    {
        _mesh = new MeshInstance3D
        {
            Mesh = BuildWireBox(),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = new Color(1.0f, 0.92f, 0.10f),
                VertexColorUseAsAlbedo = false,
                NoDepthTest = false,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = true,
        };
        AddChild(_mesh);

        var tileW = SimConstants.TileWidthMeters;
        var pocketMeters = Cell.SizeTiles * tileW;
        // WorldGen centers the pocket on origin: tiles span [-half, half),
        // so world-space extent is [-half*tileW, +half*tileW).
        var origin = -(Cell.SizeTiles / 2) * tileW;
        _mesh.Position = new Vector3(origin, 0f, origin);
        _mesh.Scale = new Vector3(pocketMeters, BoxHeightMeters, pocketMeters);
    }

    // 12-edge wireframe on a [0,1]³ unit cube. Scaling the MeshInstance3D
    // stretches it to the cell footprint without rebuilding geometry.
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
