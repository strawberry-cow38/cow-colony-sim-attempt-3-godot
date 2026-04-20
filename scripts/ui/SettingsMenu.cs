using System;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;
using Godot;

namespace CowColonySim.UI;

public sealed partial class SettingsMenu : CanvasLayer
{
    private const string ConfigPath = "user://settings.cfg";
    private const string RepoOwner = "strawberry-cow38";
    private const string RepoName = "cow-colony-sim-attempt-3-godot";

    private static readonly (int W, int H)[] Resolutions =
    {
        (1280, 720), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160),
    };

    private static readonly string[] WindowModes = { "Windowed", "Borderless", "Fullscreen" };

    private Panel _panel = null!;
    private OptionButton _resolutionDropdown = null!;
    private OptionButton _windowModeDropdown = null!;
    private Button _checkUpdateButton = null!;
    private Label _updateStatus = null!;
    private Label _currentResolutionLabel = null!;

    private int _resolutionIndex = 2;
    private int _windowModeIndex = 0;

    public override void _Ready()
    {
        Layer = 100;
        Load();
        ApplyWindow();
        BuildUi();
        _panel.Visible = false;
    }

    public override void _UnhandledInput(InputEvent ev)
    {
        if (ev is InputEventKey k && k.Pressed && !k.Echo && k.Keycode == Key.Escape)
        {
            _panel.Visible = !_panel.Visible;
            if (_panel.Visible) UpdateCurrentResolutionLabel();
            GetViewport().SetInputAsHandled();
        }
    }

    private void BuildUi()
    {
        _panel = new Panel
        {
            AnchorLeft = 0.5f, AnchorTop = 0.5f, AnchorRight = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -220, OffsetTop = -180, OffsetRight = 220, OffsetBottom = 180,
        };
        AddChild(_panel);

        var vb = new VBoxContainer
        {
            AnchorLeft = 0, AnchorTop = 0, AnchorRight = 1, AnchorBottom = 1,
            OffsetLeft = 16, OffsetTop = 16, OffsetRight = -16, OffsetBottom = -16,
        };
        _panel.AddChild(vb);

        vb.AddChild(new Label { Text = "Settings", HorizontalAlignment = HorizontalAlignment.Center });
        vb.AddChild(new HSeparator());

        vb.AddChild(new Label { Text = "Resolution" });
        _resolutionDropdown = new OptionButton();
        foreach (var r in Resolutions) _resolutionDropdown.AddItem($"{r.W} x {r.H}");
        _resolutionDropdown.Selected = _resolutionIndex;
        _resolutionDropdown.ItemSelected += idx => { _resolutionIndex = (int)idx; ApplyWindow(); Save(); UpdateCurrentResolutionLabel(); };
        vb.AddChild(_resolutionDropdown);

        vb.AddChild(new Label { Text = "Window Mode" });
        _windowModeDropdown = new OptionButton();
        foreach (var m in WindowModes) _windowModeDropdown.AddItem(m);
        _windowModeDropdown.Selected = _windowModeIndex;
        _windowModeDropdown.ItemSelected += idx => { _windowModeIndex = (int)idx; ApplyWindow(); Save(); UpdateCurrentResolutionLabel(); };
        vb.AddChild(_windowModeDropdown);

        _currentResolutionLabel = new Label { Text = "" };
        vb.AddChild(_currentResolutionLabel);

        vb.AddChild(new HSeparator());
        _checkUpdateButton = new Button { Text = "Check for Updates" };
        _checkUpdateButton.Pressed += OnCheckUpdatePressed;
        vb.AddChild(_checkUpdateButton);

        _updateStatus = new Label { Text = "" };
        vb.AddChild(_updateStatus);

        vb.AddChild(new HSeparator());
        var closeBtn = new Button { Text = "Close (Esc)" };
        closeBtn.Pressed += () => _panel.Visible = false;
        vb.AddChild(closeBtn);
    }

    private void UpdateCurrentResolutionLabel()
    {
        var size = DisplayServer.WindowGetSize();
        _currentResolutionLabel.Text = $"Current: {size.X} x {size.Y}";
    }

    private void ApplyWindow()
    {
        var (w, h) = Resolutions[_resolutionIndex];
        switch (_windowModeIndex)
        {
            case 0:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, false);
                DisplayServer.WindowSetSize(new Vector2I(w, h));
                break;
            case 1:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
                DisplayServer.WindowSetFlag(DisplayServer.WindowFlags.Borderless, true);
                DisplayServer.WindowSetSize(new Vector2I(w, h));
                break;
            case 2:
                DisplayServer.WindowSetMode(DisplayServer.WindowMode.Fullscreen);
                break;
        }
    }

    private void Load()
    {
        var cfg = new ConfigFile();
        if (cfg.Load(ConfigPath) != Error.Ok) return;
        _resolutionIndex = Mathf.Clamp((int)cfg.GetValue("display", "resolution_index", 2), 0, Resolutions.Length - 1);
        _windowModeIndex = Mathf.Clamp((int)cfg.GetValue("display", "window_mode", 0), 0, WindowModes.Length - 1);
    }

    private void Save()
    {
        var cfg = new ConfigFile();
        cfg.SetValue("display", "resolution_index", _resolutionIndex);
        cfg.SetValue("display", "window_mode", _windowModeIndex);
        cfg.Save(ConfigPath);
    }

    private async void OnCheckUpdatePressed()
    {
        _checkUpdateButton.Disabled = true;
        _updateStatus.Text = "Checking...";
        try
        {
            var local = ReadLocalVersion();
            var remote = await FetchLatestTagAsync();
            if (remote == null)
            {
                _updateStatus.Text = $"Local: {local}\nRemote: (unreachable)";
            }
            else if (string.Equals(local, remote, StringComparison.OrdinalIgnoreCase))
            {
                _updateStatus.Text = $"Up to date ({local})";
            }
            else
            {
                _updateStatus.Text = $"Update available!\nLocal: {local}\nRemote: {remote}\nRun launcher to update.";
            }
        }
        catch (Exception ex)
        {
            _updateStatus.Text = $"Check failed: {ex.Message}";
        }
        finally
        {
            _checkUpdateButton.Disabled = false;
        }
    }

    private static string ReadLocalVersion()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(OS.GetExecutablePath()) ?? "";
            var path = Path.Combine(exeDir, "version.txt");
            if (!File.Exists(path)) return "(unknown)";
            foreach (var line in File.ReadAllLines(path))
            {
                var eq = line.IndexOf('=');
                if (eq <= 0) continue;
                if (line[..eq].Trim() == "tag") return line[(eq + 1)..].Trim();
            }
        }
        catch { }
        return "(unknown)";
    }

    private static async Task<string?> FetchLatestTagAsync()
    {
        using var http = new System.Net.Http.HttpClient();
        http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CowColonySim", "1.0"));
        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        using var res = await http.GetAsync(url);
        if (!res.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("tag_name", out var t) ? t.GetString() : null;
    }
}
