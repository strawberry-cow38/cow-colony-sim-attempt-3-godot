using Godot;
using CowColonySim.Render;
using CowColonySim.Sim;
using CowColonySim.Sim.Grid;

namespace CowColonySim.UI;

/// <summary>
/// Wireframe box outlining the streaming cell under the mouse cursor. A cell
/// is 16×16 chunks = 256×256 tiles = 384m on each side, so the box is useful
/// for eyeballing which cell a spot in the world falls into when tweaking
/// paging or debugging cell-scoped systems.
///
/// Geometry is one static unit-cube wireframe (12 line segments). We move /
/// scale the instance each frame; no mesh rebuild. Uses an unshaded material
/// so it stays readable in fog or at night.
/// </summary>
public partial class CellHighlight3D : Node3D
{
    // Vertical span of the box in meters. Cells cover full world height; a
    // fixed span that comfortably clears any generated terrain keeps the
    // outline in view without computing per-cell max heights.
    private const float BoxHeightMeters = 300f;

    private MeshInstance3D? _mesh;
    private SimHost? _sim;

    public override void _Ready()
    {
        _sim = GetNode<SimHost>("/root/SimHost");
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
            Visible = false,
        };
        AddChild(_mesh);
    }

    public override void _Process(double delta)
    {
        if (_mesh == null || _sim == null) return;
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null) { _mesh.Visible = false; return; }

        var mouse = GetViewport()!.GetMousePosition();
        var origin = cam.ProjectRayOrigin(mouse);
        var dir = cam.ProjectRayNormal(mouse);
        if (Mathf.Abs(dir.Y) < 1e-5f) { _mesh.Visible = false; return; }
        var t = -origin.Y / dir.Y;
        if (t < 0f) { _mesh.Visible = false; return; }
        var hit = origin + dir * t;
        var tile = TileCoord.WorldToTile(hit);
        var surfaceY = WorldGen.SurfaceY(_sim.Tiles, tile.X, tile.Z);
        var cell = Cell.FromTile(new TilePos(tile.X, surfaceY, tile.Z));

        var tileW = SimConstants.TileWidthMeters;
        var cellMeters = Cell.SizeTiles * tileW;
        var x0 = cell.X * cellMeters;
        var z0 = cell.Z * cellMeters;
        // Y floor at 0 (world origin). Height is fixed so the box clears any
        // terrain in the cell without scanning for per-cell max elevation.
        _mesh.Position = new Vector3(x0, 0f, z0);
        _mesh.Scale = new Vector3(cellMeters, BoxHeightMeters, cellMeters);
        _mesh.Visible = true;
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
