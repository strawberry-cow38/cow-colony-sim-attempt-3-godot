using System;
using Godot;
using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Grid;

namespace CowColonySim.UI;

/// <summary>
/// World-map HUD in two roles: (1) boot-time fullscreen selector — on launch
/// the sim sits in <see cref="SimHost.AwaitingWorldSelection"/> with no tiles
/// generated, and this panel blocks input until the player pins a land cell
/// and hits Settle; (2) in-game overlay toggled via the M key so the player
/// can re-settle or regen a new world from inside the sim.
///
/// Click to pin a cell; Settle commits the pinned cell (disabled on ocean
/// per the "no pocket on ocean" rule). Regenerate World rolls a new seed
/// and returns to selection.
/// </summary>
public sealed partial class WorldMapHud : CanvasLayer
{
    // 2 px per cell keeps the 400×400 map within 800×800 screen pixels.
    private const int PixelsPerCell = 2;
    private const int MapWidthPx = WorldMap.Width * PixelsPerCell;
    private const int MapHeightPx = WorldMap.Height * PixelsPerCell;
    private const int Margin = 16;
    private const int ButtonStripH = 40;

    private SimHost _sim = null!;
    private Panel _panel = null!;
    private TextureRect _mapImage = null!;
    private ColorRect _currentMarker = null!;
    private ColorRect _pinMarker = null!;
    private Label _hoverLabel = null!;
    private Label _pinLabel = null!;
    private Button _settleButton = null!;
    private Button _regenButton = null!;
    private Label _titleLabel = null!;
    private ImageTexture? _texture;
    private bool _shown;
    private WorldMapCoord? _pin;
    private readonly Random _regenRng = new();

    public override void _Ready()
    {
        Layer = 95;
        _sim = GetNode<SimHost>("/root/SimHost");
        _sim.WorldRegenerated += OnWorldRegenerated;
        _sim.WorldSelectionChanged += OnSelectionChanged;

        var totalH = MapHeightPx + Margin * 3 + 24 + ButtonStripH + 24;
        _panel = new Panel
        {
            AnchorLeft = 0.5f, AnchorTop = 0.5f, AnchorRight = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -(MapWidthPx / 2 + Margin),
            OffsetTop = -totalH / 2,
            OffsetRight = MapWidthPx / 2 + Margin,
            OffsetBottom = totalH / 2,
            Visible = false,
        };
        AddChild(_panel);

        _titleLabel = new Label
        {
            OffsetLeft = Margin, OffsetTop = 4,
            OffsetRight = Margin + MapWidthPx, OffsetBottom = 24,
            LabelSettings = new LabelSettings { FontSize = 14, FontColor = new Color(1, 1, 1) },
            Text = "Choose a location to settle.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _panel.AddChild(_titleLabel);

        _mapImage = new TextureRect
        {
            OffsetLeft = Margin, OffsetTop = 28,
            OffsetRight = Margin + MapWidthPx, OffsetBottom = 28 + MapHeightPx,
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Pass,
        };
        _mapImage.GuiInput += OnMapGuiInput;
        _panel.AddChild(_mapImage);

        // Floor at 4px so a single-cell marker stays visible when
        // PixelsPerCell drops below that on large maps.
        var markerSize = Mathf.Max(PixelsPerCell, 4);
        _currentMarker = new ColorRect
        {
            Color = new Color(1f, 0.92f, 0.10f, 1f),
            Size = new Vector2(markerSize, markerSize),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _mapImage.AddChild(_currentMarker);

        _pinMarker = new ColorRect
        {
            Color = new Color(1f, 0.30f, 0.30f, 1f),
            Size = new Vector2(markerSize, markerSize),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _mapImage.AddChild(_pinMarker);

        _hoverLabel = new Label
        {
            OffsetLeft = Margin,
            OffsetTop = 28 + MapHeightPx + 4,
            OffsetRight = Margin + MapWidthPx,
            OffsetBottom = 28 + MapHeightPx + 24,
            LabelSettings = new LabelSettings { FontSize = 12, FontColor = new Color(1, 1, 1) },
            Text = "hover a cell",
        };
        _panel.AddChild(_hoverLabel);

        _pinLabel = new Label
        {
            OffsetLeft = Margin,
            OffsetTop = 28 + MapHeightPx + 24,
            OffsetRight = Margin + MapWidthPx,
            OffsetBottom = 28 + MapHeightPx + 44,
            LabelSettings = new LabelSettings { FontSize = 12, FontColor = new Color(0.85f, 0.85f, 0.85f) },
            Text = "click a land cell to pin",
        };
        _panel.AddChild(_pinLabel);

        var buttonY = 28 + MapHeightPx + 48;
        _regenButton = new Button
        {
            Text = "Regenerate World",
            OffsetLeft = Margin, OffsetTop = buttonY,
            OffsetRight = Margin + 170, OffsetBottom = buttonY + 28,
        };
        _regenButton.AddThemeFontSizeOverride("font_size", 13);
        _regenButton.Pressed += OnRegenPressed;
        _panel.AddChild(_regenButton);

        _settleButton = new Button
        {
            Text = "Settle Here",
            OffsetLeft = Margin + MapWidthPx - 150, OffsetTop = buttonY,
            OffsetRight = Margin + MapWidthPx, OffsetBottom = buttonY + 28,
            Disabled = true,
        };
        _settleButton.AddThemeFontSizeOverride("font_size", 13);
        _settleButton.Pressed += OnSettlePressed;
        _panel.AddChild(_settleButton);

        RebuildTexture();
        OnSelectionChanged();
    }

    public override void _ExitTree()
    {
        if (_sim != null)
        {
            _sim.WorldRegenerated -= OnWorldRegenerated;
            _sim.WorldSelectionChanged -= OnSelectionChanged;
        }
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        // M-key toggle only valid after settlement; during selection the
        // panel is modal and the key is a no-op so the player can't dismiss
        // the selector and be stranded with no pocket.
        if (_sim.AwaitingWorldSelection) return;
        if (ev is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.M)
        {
            _shown = !_shown;
            _panel.Visible = _shown;
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!_panel.Visible) return;

        var mx = _mapImage.GetLocalMousePosition();
        var cx = (int)(mx.X / PixelsPerCell);
        var cz = (int)(mx.Y / PixelsPerCell);
        if (WorldMap.InBounds(cx, cz))
        {
            var cell = _sim.Overworld.Get(cx, cz);
            _hoverLabel.Text = $"({cx},{cz}) {DescribeCell(cell)}  {cell.TemperatureC:0.0}°C  {cell.RainfallMm:0}mm";
        }

        if (!_sim.AwaitingWorldSelection)
        {
            var here = _sim.CurrentMapCoord;
            _currentMarker.Visible = true;
            _currentMarker.Position = new Vector2(here.X * PixelsPerCell, here.Z * PixelsPerCell);
        }
        else
        {
            _currentMarker.Visible = false;
        }
    }

    private static string DescribeCell(WorldMapCell cell)
    {
        if (cell.IsOcean) return "Ocean";
        if (cell.IsLake) return "Lake";
        var b = BiomeRegistry.Get(cell.BiomeId);
        return cell.HasRiver ? $"{b.Name} (river)" : b.Name;
    }

    private void OnMapGuiInput(InputEvent ev)
    {
        if (ev is not InputEventMouseButton mb) return;
        if (!mb.Pressed || mb.ButtonIndex != MouseButton.Left) return;
        var cx = (int)(mb.Position.X / PixelsPerCell);
        var cz = (int)(mb.Position.Y / PixelsPerCell);
        if (!WorldMap.InBounds(cx, cz)) return;
        _pin = new WorldMapCoord(cx, cz);
        UpdatePin();
    }

    private void UpdatePin()
    {
        if (_pin is not { } p)
        {
            _pinMarker.Visible = false;
            _pinLabel.Text = "click a land cell to pin";
            _settleButton.Disabled = true;
            return;
        }
        _pinMarker.Visible = true;
        _pinMarker.Position = new Vector2(p.X * PixelsPerCell, p.Z * PixelsPerCell);
        var cell = _sim.Overworld.Get(p);
        var desc = DescribeCell(cell);
        if (cell.IsOcean)
        {
            _pinLabel.Text = $"pinned ({p.X},{p.Z}) {desc} — can't settle on ocean";
            _settleButton.Disabled = true;
        }
        else
        {
            _pinLabel.Text = $"pinned ({p.X},{p.Z}) {desc} — Settle to begin";
            _settleButton.Disabled = false;
        }
    }

    private void OnSettlePressed()
    {
        if (_pin is not { } p) return;
        if (!_sim.SettleAt(p)) return;
        _pin = null;
    }

    private void OnRegenPressed()
    {
        _pin = null;
        _sim.Regenerate(_regenRng.Next());
    }

    private void OnWorldRegenerated()
    {
        RebuildTexture();
    }

    private void OnSelectionChanged()
    {
        if (_sim.AwaitingWorldSelection)
        {
            _shown = true;
            _panel.Visible = true;
            _titleLabel.Text = "Choose a location to settle.";
            _settleButton.Visible = true;
        }
        else
        {
            _shown = false;
            _panel.Visible = false;
            _titleLabel.Text = "World Map";
        }
        UpdatePin();
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
