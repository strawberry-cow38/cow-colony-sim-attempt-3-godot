using Godot;

namespace CowColonySim.UI.Selection;

/// <summary>
/// 3D wireframe box drawn around the current <see cref="SelectionTarget.Bounds"/>.
/// Hidden when nothing is selected. Mirrors CellHighlight3D's unshaded wire
/// material so it reads at night and through fog.
/// </summary>
public partial class SelectionGhost : Node3D
{
    private static readonly Color WireColor = new(1.0f, 0.92f, 0.35f, 1.0f);

    private SelectionController? _controller;
    private MeshInstance3D _wire = null!;

    public override void _Ready()
    {
        _wire = new MeshInstance3D
        {
            Mesh = BuildWireBox(),
            MaterialOverride = new StandardMaterial3D
            {
                ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
                AlbedoColor = WireColor,
                NoDepthTest = false,
            },
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Visible = false,
        };
        AddChild(_wire);

        _controller = GetNodeOrNull<SelectionController>("/root/Main/SelectionController");
        if (_controller != null) _controller.SelectionChanged += Refresh;
    }

    public override void _ExitTree()
    {
        if (_controller != null) _controller.SelectionChanged -= Refresh;
    }

    private void Refresh()
    {
        var sel = _controller?.Current;
        if (sel == null)
        {
            _wire.Visible = false;
            return;
        }
        _wire.Visible = true;
        _wire.Position = sel.Bounds.Position;
        _wire.Scale = sel.Bounds.Size;
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
