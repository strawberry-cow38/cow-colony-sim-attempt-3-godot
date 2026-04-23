using System;
using Godot;
using CowColonySim.Sim.Biomes;
using CowColonySim.Sim.Grid;

namespace CowColonySim.UI;

/// <summary>
/// Fullscreen world-map HUD in two roles: (1) boot-time selector — on launch
/// the sim sits in <see cref="SimHost.AwaitingWorldSelection"/> with no tiles
/// generated, and this panel blocks input until the player pins a land cell
/// and hits Settle; (2) in-game overlay toggled via M so the player can
/// re-settle or regen a new world from inside the sim.
///
/// Left-click a cell to pin (Settle button grays on ocean per the no-pocket-
/// on-ocean rule). Mouse wheel zooms toward cursor. Middle- or right-click
/// drag pans. Regenerate World rolls a new seed and returns to selection.
/// </summary>
public sealed partial class WorldMapHud : CanvasLayer
{
    private const float MinZoom = 1.0f;   // 1 px per cell (400×400 px)
    private const float MaxZoom = 16.0f;  // 16 px per cell (chunky look at max)
    private const float WheelZoomStep = 1.25f;
    private const int   TopBarH = 40;
    private const int   BottomBarH = 56;

    private SimHost _sim = null!;
    private Panel _panel = null!;
    private Control _viewport = null!;
    private Control _mapRoot = null!;
    private TextureRect _mapImage = null!;
    private ColorRect _currentMarker = null!;
    private ColorRect _pinMarker = null!;
    private Label _hoverLabel = null!;
    private Label _pinLabel = null!;
    private Button _settleButton = null!;
    private Button _regenButton = null!;
    private Label _titleLabel = null!;
    private Button _closeButton = null!;
    private ImageTexture? _texture;

    private float _zoom = 2.0f;
    private Vector2 _pan = Vector2.Zero;       // top-left of map in viewport space
    private bool _dragging;
    private Vector2 _dragLastMouse;
    private WorldMapCoord? _pin;
    private readonly Random _regenRng = new();

    public override void _Ready()
    {
        Layer = 95;
        _sim = GetNode<SimHost>("/root/SimHost");
        _sim.WorldRegenerated += OnWorldRegenerated;
        _sim.WorldSelectionChanged += OnSelectionChanged;

        _panel = new Panel
        {
            AnchorLeft = 0f, AnchorTop = 0f, AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 0, OffsetTop = 0, OffsetRight = 0, OffsetBottom = 0,
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        AddChild(_panel);

        _titleLabel = new Label
        {
            AnchorLeft = 0f, AnchorRight = 1f,
            OffsetLeft = 0, OffsetTop = 8,
            OffsetRight = 0, OffsetBottom = 32,
            LabelSettings = new LabelSettings { FontSize = 18, FontColor = new Color(1, 1, 1) },
            Text = "Choose a location to settle.",
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        _panel.AddChild(_titleLabel);

        _closeButton = new Button
        {
            Text = "Close (M)",
            AnchorLeft = 1f, AnchorRight = 1f,
            OffsetLeft = -110, OffsetTop = 8,
            OffsetRight = -10, OffsetBottom = 36,
            Visible = false,
        };
        _closeButton.AddThemeFontSizeOverride("font_size", 13);
        _closeButton.Pressed += HideOverlay;
        _panel.AddChild(_closeButton);

        _viewport = new Control
        {
            AnchorLeft = 0f, AnchorTop = 0f, AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 0, OffsetTop = TopBarH,
            OffsetRight = 0, OffsetBottom = -BottomBarH,
            ClipContents = true,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        _viewport.GuiInput += OnViewportInput;
        _viewport.Resized += CenterMap;
        _panel.AddChild(_viewport);

        _mapRoot = new Control
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _viewport.AddChild(_mapRoot);

        _mapImage = new TextureRect
        {
            StretchMode = TextureRect.StretchModeEnum.Scale,
            TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        };
        _mapRoot.AddChild(_mapImage);

        _currentMarker = new ColorRect
        {
            Color = new Color(1f, 0.92f, 0.10f, 1f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _mapRoot.AddChild(_currentMarker);

        _pinMarker = new ColorRect
        {
            Color = new Color(1f, 0.30f, 0.30f, 1f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false,
        };
        _mapRoot.AddChild(_pinMarker);

        _hoverLabel = new Label
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 16, OffsetTop = -BottomBarH + 4,
            OffsetRight = -16, OffsetBottom = -BottomBarH + 24,
            LabelSettings = new LabelSettings { FontSize = 13, FontColor = new Color(1, 1, 1) },
            Text = "hover a cell",
        };
        _panel.AddChild(_hoverLabel);

        _pinLabel = new Label
        {
            AnchorLeft = 0f, AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 16, OffsetTop = -BottomBarH + 26,
            OffsetRight = -16, OffsetBottom = -BottomBarH + 46,
            LabelSettings = new LabelSettings { FontSize = 12, FontColor = new Color(0.85f, 0.85f, 0.85f) },
            Text = "left-click to pin · wheel to zoom · right/middle-drag to pan",
        };
        _panel.AddChild(_pinLabel);

        _regenButton = new Button
        {
            AnchorLeft = 0f, AnchorBottom = 1f,
            OffsetLeft = 16, OffsetTop = -32,
            OffsetRight = 186, OffsetBottom = -8,
            Text = "Regenerate World",
        };
        _regenButton.AddThemeFontSizeOverride("font_size", 13);
        _regenButton.Pressed += OnRegenPressed;
        _panel.AddChild(_regenButton);

        _settleButton = new Button
        {
            AnchorLeft = 1f, AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = -166, OffsetTop = -32,
            OffsetRight = -16, OffsetBottom = -8,
            Text = "Settle Here",
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
        if (_sim.AwaitingWorldSelection) return;
        if (ev is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.M)
        {
            if (_panel.Visible) HideOverlay();
            else ShowOverlay();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        if (!_panel.Visible) return;

        ApplyMapTransform();

        var local = _viewport.GetLocalMousePosition();
        if (TryViewportToCell(local, out var cx, out var cz))
        {
            var cell = _sim.Overworld.Get(cx, cz);
            _hoverLabel.Text = $"({cx},{cz}) {DescribeCell(cell)}  {cell.TemperatureC:0.0}°C  {cell.RainfallMm:0}mm";
        }
        else
        {
            _hoverLabel.Text = "hover a cell";
        }
    }

    private void ApplyMapTransform()
    {
        var w = WorldMap.Width * _zoom;
        var h = WorldMap.Height * _zoom;
        _mapRoot.Position = _pan;
        _mapImage.Position = Vector2.Zero;
        _mapImage.Size = new Vector2(w, h);

        var markerPx = Mathf.Max(_zoom, 4f);
        _pinMarker.Size = new Vector2(markerPx, markerPx);
        _currentMarker.Size = new Vector2(markerPx, markerPx);

        if (_pin is { } p)
        {
            _pinMarker.Position = new Vector2(p.X * _zoom, p.Z * _zoom);
        }
        if (!_sim.AwaitingWorldSelection)
        {
            var here = _sim.CurrentMapCoord;
            _currentMarker.Visible = true;
            _currentMarker.Position = new Vector2(here.X * _zoom, here.Z * _zoom);
        }
        else
        {
            _currentMarker.Visible = false;
        }
    }

    private bool TryViewportToCell(Vector2 viewportLocal, out int cx, out int cz)
    {
        var local = viewportLocal - _pan;
        cx = (int)Math.Floor(local.X / _zoom);
        cz = (int)Math.Floor(local.Y / _zoom);
        return WorldMap.InBounds(cx, cz);
    }

    private static string DescribeCell(WorldMapCell cell)
    {
        if (cell.IsOcean) return "Ocean";
        if (cell.IsLake) return "Lake";
        var b = BiomeRegistry.Get(cell.BiomeId);
        return cell.HasRiver ? $"{b.Name} (river)" : b.Name;
    }

    private void OnViewportInput(InputEvent ev)
    {
        switch (ev)
        {
            case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp:
                ZoomAt(mb.Position, _zoom * WheelZoomStep);
                _viewport.AcceptEvent();
                break;
            case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown:
                ZoomAt(mb.Position, _zoom / WheelZoomStep);
                _viewport.AcceptEvent();
                break;
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Middle
                                             || mb.ButtonIndex == MouseButton.Right:
                _dragging = mb.Pressed;
                _dragLastMouse = mb.Position;
                _viewport.AcceptEvent();
                break;
            case InputEventMouseButton mb when mb.Pressed && mb.ButtonIndex == MouseButton.Left:
                if (TryViewportToCell(mb.Position, out var cx, out var cz))
                {
                    _pin = new WorldMapCoord(cx, cz);
                    UpdatePin();
                }
                _viewport.AcceptEvent();
                break;
            case InputEventMouseMotion mm when _dragging:
                _pan += mm.Position - _dragLastMouse;
                _dragLastMouse = mm.Position;
                _viewport.AcceptEvent();
                break;
        }
    }

    private void ZoomAt(Vector2 viewportPos, float targetZoom)
    {
        targetZoom = Mathf.Clamp(targetZoom, MinZoom, MaxZoom);
        if (Mathf.IsEqualApprox(targetZoom, _zoom)) return;
        // Keep the map point under the cursor stationary. Cursor in map
        // space = (viewportPos - pan) / zoom → invariant under zoom change.
        var mapPt = (viewportPos - _pan) / _zoom;
        _zoom = targetZoom;
        _pan = viewportPos - mapPt * _zoom;
    }

    private void CenterMap()
    {
        var vs = _viewport.Size;
        if (vs.X <= 0 || vs.Y <= 0) return;
        // Default zoom = fit-to-viewport with a little breathing room.
        var fit = Math.Min(vs.X / WorldMap.Width, vs.Y / WorldMap.Height) * 0.95f;
        _zoom = Mathf.Clamp(fit, MinZoom, MaxZoom);
        _pan = new Vector2(
            (vs.X - WorldMap.Width * _zoom) * 0.5f,
            (vs.Y - WorldMap.Height * _zoom) * 0.5f);
    }

    private void UpdatePin()
    {
        if (_pin is not { } p)
        {
            _pinMarker.Visible = false;
            _pinLabel.Text = "left-click to pin · wheel to zoom · right/middle-drag to pan";
            _settleButton.Disabled = true;
            return;
        }
        _pinMarker.Visible = true;
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

    private void ShowOverlay()
    {
        _panel.Visible = true;
        CenterMap();
    }

    private void HideOverlay()
    {
        if (_sim.AwaitingWorldSelection) return;
        _panel.Visible = false;
    }

    private void OnSelectionChanged()
    {
        if (_sim.AwaitingWorldSelection)
        {
            ShowOverlay();
            _titleLabel.Text = "Choose a location to settle.";
            _settleButton.Text = "Settle Here";
            _closeButton.Visible = false;
        }
        else
        {
            _panel.Visible = false;
            _titleLabel.Text = "World Map";
            _settleButton.Text = "Settle Here";
            _closeButton.Visible = true;
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
