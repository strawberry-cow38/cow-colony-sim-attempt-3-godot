using Godot;
using CowColonySim.Render;
using CowColonySim.Sim.Grid;

namespace CowColonySim.UI;

public partial class TileInfoHud : CanvasLayer
{
    private Label? _label;
    private SimHost? _sim;

    public override void _Ready()
    {
        Layer = 10;
        _sim = GetNode<SimHost>("/root/SimHost");
        _label = new Label
        {
            Text = "",
            AnchorLeft = 0f,
            AnchorTop = 1f,
            AnchorRight = 0f,
            AnchorBottom = 1f,
            OffsetLeft = 8f,
            // Three lines × 18pt font with some breathing room.
            OffsetTop = -70f,
            OffsetBottom = -8f,
            OffsetRight = 320f,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
        };
        _label.AddThemeColorOverride("font_color", new Color(1.0f, 0.92f, 0.10f));
        _label.AddThemeColorOverride("font_outline_color", new Color(0, 0, 0, 1));
        _label.AddThemeConstantOverride("outline_size", 4);
        _label.AddThemeFontSizeOverride("font_size", 16);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        if (_label == null || _sim == null) return;
        var cam = GetViewport()?.GetCamera3D();
        if (cam == null) { _label.Text = ""; return; }

        // Y=0 ground-plane intersection for XZ pick; column surface lookup for Y.
        // Doesn't match cliff-face picks (ray would hit the face before the
        // ground plane behind it), but gives a meaningful readout for flat
        // terrain without a full ray-vs-voxel march.
        var mouse = GetViewport()!.GetMousePosition();
        var origin = cam.ProjectRayOrigin(mouse);
        var dir = cam.ProjectRayNormal(mouse);
        if (Mathf.Abs(dir.Y) < 1e-5f) { _label.Text = "tile: (ray parallel)"; return; }
        var t = -origin.Y / dir.Y;
        if (t < 0f) { _label.Text = "tile: (behind cam)"; return; }
        var hit = origin + dir * t;

        var tile = TileCoord.WorldToTile(hit);
        var surfaceY = WorldGen.SurfaceY(_sim.Tiles, tile.X, tile.Z);
        var tileWithY = new TilePos(tile.X, surfaceY, tile.Z);
        var cell = Cell.FromTile(tileWithY);
        var cellState = _sim.Tiles.GetCellState(cell);
        var cellChunks = _sim.Tiles.GetChunksInCell(cell)?.Count ?? 0;

        _label.Text =
            $"tile: ({tileWithY.X}, {tileWithY.Y}, {tileWithY.Z})\n" +
            $"cell: ({cell.X}, {cell.Z})  {cellState}  chunks:{cellChunks}";
    }
}
