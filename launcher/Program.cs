using System.Diagnostics;
using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text.Json;

namespace CowLauncher;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new LauncherForm());
    }
}

internal sealed record BuildInfo(string Tag, DateTime? Date)
{
    public static BuildInfo Unknown { get; } = new("(none)", null);

    public string FormatDate() =>
        Date is null ? "unknown" : Date.Value.ToLocalTime().ToString("yyyy-MM-dd HH:mm");
}

internal sealed record RemoteRelease(BuildInfo Info, string DownloadUrl, long SizeBytes);

internal sealed class LauncherForm : Form
{
    private const string RepoOwner = "strawberry-cow38";
    private const string RepoName = "cow-colony-sim-attempt-3-godot";
    private const string AssetName = "CowColonySim-windows-x86_64.zip";
    private const string GameExe = "CowColonySim.exe";

    private static readonly string InstallRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CowColonySim", "install");

    private readonly Label _statusLabel = new()
    {
        Dock = DockStyle.Top,
        Height = 32,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 12, 0),
        Text = "Checking for updates...",
    };

    private readonly Label _versionsLabel = new()
    {
        Dock = DockStyle.Top,
        Height = 60,
        TextAlign = ContentAlignment.MiddleLeft,
        Padding = new Padding(12, 0, 12, 0),
        Font = new Font(FontFamily.GenericMonospace, 9f),
    };

    private readonly ProgressBar _progress = new()
    {
        Dock = DockStyle.Top,
        Height = 20,
        Style = ProgressBarStyle.Continuous,
        Minimum = 0,
        Maximum = 100,
        Visible = false,
    };

    private readonly FlowLayoutPanel _buttonRow = new()
    {
        Dock = DockStyle.Bottom,
        Height = 48,
        FlowDirection = FlowDirection.RightToLeft,
        Padding = new Padding(8),
    };

    private readonly Button _updateButton = new()
    {
        Text = "Update",
        Width = 110,
        Height = 32,
        Enabled = false,
        Visible = false,
    };

    private readonly Button _playButton = new()
    {
        Text = "Play",
        Width = 110,
        Height = 32,
        Enabled = false,
    };

    private BuildInfo _installed = BuildInfo.Unknown;
    private RemoteRelease? _remote;

    public LauncherForm()
    {
        Text = "Cow Colony Sim — Launcher";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        MinimizeBox = true;
        ClientSize = new Size(460, 220);

        Controls.Add(_progress);
        Controls.Add(_versionsLabel);
        Controls.Add(_statusLabel);
        Controls.Add(_buttonRow);
        _buttonRow.Controls.Add(_playButton);
        _buttonRow.Controls.Add(_updateButton);

        _playButton.Click += async (_, _) => await LaunchGameAsync();
        _updateButton.Click += async (_, _) => await RunUpdateAsync();

        Load += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        _installed = ReadInstalledVersion();
        RenderVersions(remote: null);

        try
        {
            _remote = await FetchLatestReleaseAsync();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Could not reach GitHub: {ex.Message}";
            _playButton.Enabled = IsInstalled();
            return;
        }

        RenderVersions(_remote);
        var installed = IsInstalled();

        if (_remote is null)
        {
            _statusLabel.Text = installed
                ? "No releases published yet. Playing currently-installed build."
                : "No releases published yet and nothing installed. Nothing to launch.";
            _playButton.Enabled = installed;
            return;
        }

        if (!installed)
        {
            _statusLabel.Text = "No local install found — update required.";
            _updateButton.Text = "Install";
            _updateButton.Enabled = true;
            _updateButton.Visible = true;
            _playButton.Enabled = false;
            return;
        }

        if (!string.Equals(_installed.Tag, _remote.Info.Tag, StringComparison.Ordinal))
        {
            _statusLabel.Text = "Update available.";
            _updateButton.Enabled = true;
            _updateButton.Visible = true;
            _playButton.Enabled = true;
        }
        else
        {
            _statusLabel.Text = "Up to date.";
            _playButton.Enabled = true;
        }
    }

    private void RenderVersions(RemoteRelease? remote)
    {
        var installedLine = $"installed: {_installed.Tag,-28} ({_installed.FormatDate()})";
        var remoteLine = remote is null
            ? "latest:    (fetching...)"
            : $"latest:    {remote.Info.Tag,-28} ({remote.Info.FormatDate()})";
        _versionsLabel.Text = $"{installedLine}\n{remoteLine}";
    }

    private static BuildInfo ReadInstalledVersion()
    {
        var path = Path.Combine(InstallRoot, "version.txt");
        if (!File.Exists(path))
        {
            return BuildInfo.Unknown;
        }

        string? tag = null;
        DateTime? date = null;
        foreach (var line in File.ReadAllLines(path))
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) continue;
            var key = line[..eq].Trim();
            var val = line[(eq + 1)..].Trim();
            if (key == "tag") tag = val;
            else if (key == "date" && DateTime.TryParse(val, null,
                         System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
            {
                date = parsed;
            }
        }

        return new BuildInfo(tag ?? "(unknown)", date);
    }

    private static bool IsInstalled() =>
        File.Exists(Path.Combine(InstallRoot, GameExe));

    private static async Task<RemoteRelease?> FetchLatestReleaseAsync()
    {
        using var http = CreateHttpClient();
        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        using var res = await http.GetAsync(url);
        if (res.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
        res.EnsureSuccessStatusCode();

        var json = await res.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var tag = root.GetProperty("tag_name").GetString() ?? "(unknown)";
        DateTime? publishedAt = null;
        if (root.TryGetProperty("published_at", out var pub) &&
            pub.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(pub.GetString(), null,
                System.Globalization.DateTimeStyles.RoundtripKind, out var p))
        {
            publishedAt = p;
        }

        string? downloadUrl = null;
        long size = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString();
                if (string.Equals(name, AssetName, StringComparison.OrdinalIgnoreCase))
                {
                    downloadUrl = asset.GetProperty("browser_download_url").GetString();
                    if (asset.TryGetProperty("size", out var s) && s.ValueKind == JsonValueKind.Number)
                    {
                        size = s.GetInt64();
                    }
                    break;
                }
            }
        }

        if (downloadUrl is null)
        {
            return null;
        }

        return new RemoteRelease(new BuildInfo(tag, publishedAt), downloadUrl, size);
    }

    private async Task RunUpdateAsync()
    {
        if (_remote is null) return;

        _playButton.Enabled = false;
        _updateButton.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;

        var tmpZip = Path.Combine(Path.GetTempPath(), $"CowColonySim-{Guid.NewGuid():N}.zip");
        try
        {
            _statusLabel.Text = "Downloading...";
            await DownloadWithProgressAsync(_remote.DownloadUrl, tmpZip, _remote.SizeBytes);

            _statusLabel.Text = "Clearing old install...";
            if (Directory.Exists(InstallRoot))
            {
                Directory.Delete(InstallRoot, recursive: true);
            }
            Directory.CreateDirectory(InstallRoot);

            _statusLabel.Text = "Extracting...";
            await Task.Run(() => ZipFile.ExtractToDirectory(tmpZip, InstallRoot, overwriteFiles: true));

            _installed = ReadInstalledVersion();
            RenderVersions(_remote);
            _statusLabel.Text = "Up to date.";
            _updateButton.Visible = false;
            _playButton.Enabled = true;
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Update failed: {ex.Message}";
            _updateButton.Enabled = true;
            _playButton.Enabled = IsInstalled();
        }
        finally
        {
            _progress.Visible = false;
            if (File.Exists(tmpZip))
            {
                try { File.Delete(tmpZip); } catch { /* leave it */ }
            }
        }
    }

    private async Task DownloadWithProgressAsync(string url, string dest, long expectedSize)
    {
        using var http = CreateHttpClient();
        using var res = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();

        var total = res.Content.Headers.ContentLength ?? expectedSize;
        _progress.Style = total > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;

        await using var src = await res.Content.ReadAsStreamAsync();
        await using var dst = File.Create(dest);
        var buffer = new byte[81920];
        long read = 0;
        int n;
        while ((n = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n));
            read += n;
            if (total > 0)
            {
                var pct = (int)Math.Clamp(read * 100 / total, 0, 100);
                _progress.Value = pct;
                _statusLabel.Text = $"Downloading... {read / 1024 / 1024} / {total / 1024 / 1024} MB";
            }
        }
    }

    private async Task LaunchGameAsync()
    {
        var exe = Path.Combine(InstallRoot, GameExe);
        if (!File.Exists(exe))
        {
            _statusLabel.Text = $"{GameExe} not found in install dir.";
            return;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = InstallRoot,
                UseShellExecute = false,
            };
            Process.Start(psi);
            await Task.Delay(400);
            Close();
        }
        catch (Exception ex)
        {
            _statusLabel.Text = $"Launch failed: {ex.Message}";
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("CowLauncher", "0.1"));
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }
}
