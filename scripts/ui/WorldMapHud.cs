using Godot;
using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Grid;

namespace CowColonySim.UI;

/// <summary>
/// Toggleable overlay (M key) showing the 400×400 overworld map as a
/// biome-colored grid. Hover a cell to see its coordinate, biome, temp,
/// rainfall. The currently-loaded pocket is marked with a yellow outline.
///
/// First-pass view-only. Click-to-travel lands when the playable region
/// can swap its <see cref="WorldMapCoord"/> and regen in place.
/// </summary>
public sealed partial class WorldMapHud : CanvasLayer
{
    // 2 px per cell keeps the 400×400 map within 800×800 screen pixels.
    private const int PixelsPerCell = 2;
    private const int MapWidthPx = WorldMap.Width * PixelsPerCell;
    private const int MapHeightPx = WorldMap.Height * PixelsPerCell;
    private const int Margin = 16;

    private SimHost _sim = null!;
    private Panel _panel = null!;
    private TextureRect _mapImage = null!;
    private ColorRect _marker = null!;
    private Label _hoverLabel = null!;
    private ImageTexture? _texture;
    private bool _shown;

    public override void _Ready()
    {
        Layer = 95;
        _sim = GetNode<SimHost>("/root/SimHost");
        _sim.WorldRegenerated += RebuildTexture;

        _panel = new Panel
        {
            AnchorLeft = 0.5f, AnchorTop = 0.5f, AnchorRight = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -(MapWidthPx / 2 + Margin),
            OffsetTop = -(MapHeightPx / 2 + Margin + 24),
            OffsetRight = MapWidthPx / 2 + Margin,
            OffsetBottom = MapHeightPx / 2 + Margin + 24,
            Visible = false,
        };
        AddChild(_panel);

        _mapImage = new TextureRect
        {
            OffsetLeft = Margin, OffsetTop = Margin,
            OffsetRight = Margin + MapWidthPx, OffsetBottom = Margin + MapHeightPx,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _panel.AddChild(_mapImage);

        // Floor at 4px so a single-cell marker stays visible when
        // PixelsPerCell drops below that on large maps.
        var markerSize = Mathf.Max(PixelsPerCell, 4);
        _marker = new ColorRect
        {
            Color = new Color(1f, 0.92f, 0.10f, 1f),
            Size = new Vector2(markerSize, markerSize),
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _mapImage.AddChild(_marker);

        _hoverLabel = new Label
        {
            OffsetLeft = Margin,
            OffsetTop = Margin + MapHeightPx + 4,
            OffsetRight = Margin + MapWidthPx,
            OffsetBottom = Margin + MapHeightPx + 24,
            LabelSettings = new LabelSettings { FontSize = 12, FontColor = new Color(1, 1, 1) },
            Text = "hover a cell",
        };
        _panel.AddChild(_hoverLabel);

        RebuildTexture();
    }

    public override void _ExitTree()
    {
        if (_sim != null) _sim.WorldRegenerated -= RebuildTexture;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.M)
        {
            _shown = !_shown;
            _panel.Visible = _shown;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!_shown) return;

        var mx = _mapImage.GetLocalMousePosition();
        var cx = (int)(mx.X / PixelsPerCell);
        var cz = (int)(mx.Y / PixelsPerCell);
        if (WorldMap.InBounds(cx, cz))
        {
            var cell = _sim.Overworld.Get(cx, cz);
            var biome = BiomeRegistry.Get(cell.BiomeId);
            var label = cell.IsOcean ? "Ocean"
                : cell.IsLake ? "Lake"
                : cell.HasRiver ? $"{biome.Name} (river)"
                : biome.Name;
            _hoverLabel.Text = $"({cx},{cz}) {label}  {cell.TemperatureC:0.0}°C  {cell.RainfallMm:0}mm";
        }

        var here = _sim.CurrentMapCoord;
        _marker.Position = new Vector2(here.X * PixelsPerCell, here.Z * PixelsPerCell);
    }

    // Ocean depth ramp: shallow shelf (cyan-teal) → deep water (near-black
    // navy). Elevation is negative under ocean; the deeper it goes, the
    // darker the pixel reads on the overworld.
    private static readonly Color OceanShallow = new(0.25f, 0.55f, 0.78f);
    private static readonly Color OceanDeep    = new(0.05f, 0.12f, 0.28f);
    private static readonly Color LakeColor    = new(0.30f, 0.60f, 0.85f);
    private static readonly Color RiverColor   = new(0.40f, 0.70f, 0.95f);

    private void RebuildTexture()
    {
        var img = Image.CreateEmpty(WorldMap.Width, WorldMap.Height, false, Image.Format.Rgb8);
        for (var z = 0; z < WorldMap.Height; z++)
        for (var x = 0; x < WorldMap.Width; x++)
        {
            var cell = _sim.Overworld.Get(x, z);
            Color px;
            if (cell.IsOcean)
            {
                // Elevation is the raw continent-plus-bias value; under
                // threshold it goes negative. Clamp to [-1, 0] and map to
                // shallow→deep so the ramp saturates before runaway lows.
                var t = Mathf.Clamp(-cell.Elevation, 0f, 1f);
                px = OceanShallow.Lerp(OceanDeep, t);
            }
            else if (cell.IsLake)
            {
                px = LakeColor;
            }
            else if (cell.HasRiver)
            {
                px = RiverColor;
            }
            else
            {
                var b = BiomeRegistry.Get(cell.BiomeId);
                px = new Color(b.DebugR, b.DebugG, b.DebugB);
            }
            img.SetPixel(x, z, px);
        }
        _texture = ImageTexture.CreateFromImage(img);
        _mapImage.Texture = _texture;
    }
}
