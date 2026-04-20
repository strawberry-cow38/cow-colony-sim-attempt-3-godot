using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
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

internal sealed record ManifestEntry(string Name, string Sha256, long Size, string? ExtractTo);

internal sealed record RemoteManifest(
    BuildInfo Info,
    IReadOnlyList<ManifestEntry> Files,
    IReadOnlyList<ManifestEntry> Archives,
    IReadOnlyDictionary<string, string> AssetUrls);

internal sealed class LauncherForm : Form
{
    private const string RepoOwner = "strawberry-cow38";
    private const string RepoName  = "cow-colony-sim-attempt-3-godot";
    private const string GameExe   = "CowColonySim.exe";

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
    private RemoteManifest? _remote;

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

        _playButton.Click   += async (_, _) => await LaunchGameAsync();
        _updateButton.Click += async (_, _) => await RunUpdateAsync();

        Load += async (_, _) => await InitAsync();
    }

    private async Task InitAsync()
    {
        _installed = ReadInstalledVersion();
        RenderVersions(remote: null);

        try
        {
            _remote = await FetchLatestManifestAsync();
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

    private void RenderVersions(RemoteManifest? remote)
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
                         DateTimeStyles.RoundtripKind, out var parsed))
            {
                date = parsed;
            }
        }

        return new BuildInfo(tag ?? "(unknown)", date);
    }

    private static bool IsInstalled() =>
        File.Exists(Path.Combine(InstallRoot, GameExe));

    private static async Task<RemoteManifest?> FetchLatestManifestAsync()
    {
        using var http = CreateHttpClient();
        var url = $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest";
        using var res = await http.GetAsync(url);
        if (res.StatusCode == HttpStatusCode.NotFound) return null;
        res.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        var root = doc.RootElement;

        var assetUrls = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string? manifestUrl = null;
        if (root.TryGetProperty("assets", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in arr.EnumerateArray())
            {
                var n = a.GetProperty("name").GetString();
                var u = a.GetProperty("browser_download_url").GetString();
                if (n is null || u is null) continue;
                assetUrls[n] = u;
                if (string.Equals(n, "manifest.json", StringComparison.OrdinalIgnoreCase))
                {
                    manifestUrl = u;
                }
            }
        }

        if (manifestUrl is null) return null;

        var manifestJson = await http.GetStringAsync(manifestUrl);
        using var mdoc = JsonDocument.Parse(manifestJson);
        var mroot = mdoc.RootElement;

        var tag = mroot.TryGetProperty("tag", out var tagEl) ? tagEl.GetString() ?? "(unknown)" : "(unknown)";
        DateTime? date = null;
        if (mroot.TryGetProperty("date", out var dEl) && dEl.ValueKind == JsonValueKind.String &&
            DateTime.TryParse(dEl.GetString(), null, DateTimeStyles.RoundtripKind, out var parsed))
        {
            date = parsed;
        }

        var files    = ParseEntries(mroot, "files", extractTo: null);
        var archives = ParseEntries(mroot, "archives", extractTo: "");

        return new RemoteManifest(new BuildInfo(tag, date), files, archives, assetUrls);
    }

    private static List<ManifestEntry> ParseEntries(JsonElement root, string key, string? extractTo)
    {
        var list = new List<ManifestEntry>();
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var e in arr.EnumerateArray())
        {
            var name = e.GetProperty("name").GetString();
            var sha  = e.GetProperty("sha256").GetString();
            var size = e.GetProperty("size").GetInt64();
            if (name is null || sha is null) continue;
            string? ex = extractTo is null ? null
                : (e.TryGetProperty("extract_to", out var ex2) ? ex2.GetString() : null);
            list.Add(new ManifestEntry(name, sha, size, ex));
        }
        return list;
    }

    private async Task RunUpdateAsync()
    {
        if (_remote is null) return;

        _playButton.Enabled = false;
        _updateButton.Enabled = false;
        _progress.Visible = true;
        _progress.Value = 0;

        Directory.CreateDirectory(InstallRoot);

        try
        {
            var pending = new List<ManifestEntry>();
            long totalBytes = 0;

            foreach (var f in _remote.Files)
            {
                var local = Path.Combine(InstallRoot, f.Name);
                if (!File.Exists(local) || !HashMatches(local, f.Sha256))
                {
                    pending.Add(f);
                    totalBytes += f.Size;
                }
            }

            foreach (var a in _remote.Archives)
            {
                if (string.IsNullOrEmpty(a.ExtractTo)) continue;
                var sidecar   = SidecarPath(a.Name);
                var extracted = Path.Combine(InstallRoot, a.ExtractTo);
                var sidecarOk = File.Exists(sidecar) &&
                                File.ReadAllText(sidecar).Trim()
                                    .Equals(a.Sha256, StringComparison.OrdinalIgnoreCase);
                if (!sidecarOk || !Directory.Exists(extracted))
                {
                    pending.Add(a);
                    totalBytes += a.Size;
                }
            }

            if (pending.Count == 0)
            {
                _statusLabel.Text = "All files already match manifest.";
            }
            else
            {
                var mb = Math.Max(1, totalBytes / 1024 / 1024);
                _statusLabel.Text = $"Downloading {pending.Count} file(s), ~{mb} MB...";
                long doneBytes = 0;
                foreach (var entry in pending)
                {
                    if (!_remote.AssetUrls.TryGetValue(entry.Name, out var assetUrl))
                    {
                        throw new InvalidOperationException($"No release asset for {entry.Name}");
                    }

                    var tmp = Path.Combine(InstallRoot, entry.Name + ".partial");
                    await DownloadWithProgressAsync(assetUrl, tmp, entry.Size, doneBytes, totalBytes, entry.Name);

                    var actual = ComputeSha256(tmp);
                    if (!actual.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(tmp);
                        throw new InvalidOperationException($"Hash mismatch for {entry.Name}");
                    }

                    if (!string.IsNullOrEmpty(entry.ExtractTo))
                    {
                        var target = Path.Combine(InstallRoot, entry.ExtractTo);
                        if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                        ZipFile.ExtractToDirectory(tmp, InstallRoot, overwriteFiles: true);
                        File.Delete(tmp);
                        File.WriteAllText(SidecarPath(entry.Name), entry.Sha256);
                    }
                    else
                    {
                        var final = Path.Combine(InstallRoot, entry.Name);
                        if (File.Exists(final)) File.Delete(final);
                        File.Move(tmp, final);
                    }

                    doneBytes += entry.Size;
                }
            }

            CleanupStaleFiles(_remote);

            _installed = ReadInstalledVersion();
            RenderVersions(_remote);
            _statusLabel.Text = "Up to date.";
            _updateButton.Visible = false;
            _playButton.Enabled = IsInstalled();
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
        }
    }

    private static string SidecarPath(string archiveName) =>
        Path.Combine(InstallRoot, $".{archiveName}.sha256");

    private static void CleanupStaleFiles(RemoteManifest remote)
    {
        var managed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var f in remote.Files) managed.Add(f.Name);
        foreach (var a in remote.Archives)
        {
            managed.Add($".{a.Name}.sha256");
            if (string.IsNullOrEmpty(a.ExtractTo)) continue;
            var dir = Path.Combine(InstallRoot, a.ExtractTo);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            {
                managed.Add(Path.GetRelativePath(InstallRoot, file));
            }
        }

        foreach (var file in Directory.EnumerateFiles(InstallRoot, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(InstallRoot, file);
            if (rel.EndsWith(".partial", StringComparison.OrdinalIgnoreCase)) continue;
            if (managed.Contains(rel)) continue;
            try { File.Delete(file); } catch { /* ignore */ }
        }
    }

    private static bool HashMatches(string path, string expected) =>
        ComputeSha256(path).Equals(expected, StringComparison.OrdinalIgnoreCase);

    private static string ComputeSha256(string path)
    {
        using var fs = File.OpenRead(path);
        using var sha = SHA256.Create();
        return Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();
    }

    private async Task DownloadWithProgressAsync(
        string url, string dest, long expectedSize, long doneBytes, long totalBytes, string label)
    {
        using var http = CreateHttpClient();
        using var res  = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        res.EnsureSuccessStatusCode();

        var fileSize = res.Content.Headers.ContentLength ?? expectedSize;
        _progress.Style = totalBytes > 0 ? ProgressBarStyle.Continuous : ProgressBarStyle.Marquee;

        await using var src = await res.Content.ReadAsStreamAsync();
        await using var dst = File.Create(dest);
        var buffer = new byte[81920];
        long fileRead = 0;
        int n;
        while ((n = await src.ReadAsync(buffer)) > 0)
        {
            await dst.WriteAsync(buffer.AsMemory(0, n));
            fileRead += n;
            if (totalBytes > 0)
            {
                var pct = (int)Math.Clamp((doneBytes + fileRead) * 100 / totalBytes, 0, 100);
                _progress.Value = pct;
                var overallMb = (doneBytes + fileRead) / 1024 / 1024;
                var totalMb   = Math.Max(1, totalBytes / 1024 / 1024);
                var fileMb    = fileRead / 1024 / 1024;
                var fileTot   = Math.Max(1, fileSize / 1024 / 1024);
                _statusLabel.Text = $"{label}  {fileMb}/{fileTot} MB   (overall {overallMb}/{totalMb} MB)";
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
            new ProductInfoHeaderValue("CowLauncher", "0.2"));
        http.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
        return http;
    }
}
