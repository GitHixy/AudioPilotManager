using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace AudioPilotManager.Services;

public sealed record UpdateInfo(Version Version, string Tag, string PageUrl, string AssetName, string AssetUrl, string ChecksumsUrl, long Size);

/// <summary>
/// Looks for new releases on GitHub and installs them. The only network traffic the app makes:
/// one request to the GitHub releases API per check, plus the download when the user asks for it.
/// Every download is verified against the release's SHA256SUMS.txt before it is run.
/// </summary>
public static class UpdateService
{
    private const string Owner = "GitHixy";
    private const string Repo = "AudioPilotManager";
    private const string LatestApi = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
    private const string DownloadPrefix = $"https://github.com/{Owner}/{Repo}/releases/download/";
    private const string InstallerAppId = "{6E4B7C1A-3F2D-4B8E-9A51-2C7D9F0E4A6B}_is1";
    private const string OldSuffix = ".old";

    private static readonly HttpClient Http = CreateClient();
    private static ProcessStartInfo? _pendingLaunch;

    public static Version CurrentVersion { get; } =
        Normalize(Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0));

    /// <summary>True when this copy was installed by Setup (so updates go through the installer).</summary>
    public static bool IsInstalled { get; } = DetectInstalled();

    private static string Rid => RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "win-arm64" : "win-x64";

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(Repo, CurrentVersion.ToString(3)));
        return client;
    }

    /// <summary>Returns the latest release if it's newer than this build, otherwise null.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, LatestApi);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(TimeSpan.FromSeconds(20));
        using var response = await Http.SendAsync(request, cts.Token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no published release yet
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cts.Token).ConfigureAwait(false);
        var root = doc.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

        var tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        latest = Normalize(latest);
        if (latest <= CurrentVersion) return null;

        var wanted = IsInstalled
            ? $"AudioPilotManager-{latest.ToString(3)}-Setup.exe"
            : $"AudioPilotManager-{latest.ToString(3)}-{Rid}-portable.zip";

        string? assetUrl = null, sumsUrl = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            var name = asset.GetProperty("name").GetString();
            var url = asset.GetProperty("browser_download_url").GetString();
            if (string.Equals(name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                assetUrl = url;
                size = asset.TryGetProperty("size", out var s) ? s.GetInt64() : 0;
            }
            else if (string.Equals(name, "SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase))
            {
                sumsUrl = url;
            }
        }

        var page = root.TryGetProperty("html_url", out var html) ? html.GetString() : null;
        page = page is not null && page.StartsWith($"https://github.com/{Owner}/{Repo}/", StringComparison.OrdinalIgnoreCase)
            ? page
            : $"https://github.com/{Owner}/{Repo}/releases/latest";

        // Only ever download from this repository's own release files.
        if (assetUrl is null || sumsUrl is null || !IsOurs(assetUrl) || !IsOurs(sumsUrl))
        {
            Log.Warn($"Release {tag} has no downloadable {wanted} with checksums");
            return new UpdateInfo(latest, tag, page, wanted, "", "", 0);
        }

        return new UpdateInfo(latest, tag, page, wanted, assetUrl, sumsUrl, size);
    }

    private static bool IsOurs(string url) => url.StartsWith(DownloadPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Downloads and verifies the update, then prepares it so it runs as soon as the app exits.
    /// The caller should shut the app down afterwards.
    /// </summary>
    public static async Task PrepareAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
    {
        if (update.AssetUrl.Length == 0) throw new InvalidOperationException("This release has no package for this PC.");

        var folder = Path.Combine(Path.GetTempPath(), "AudioPilotManager-update");
        if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        Directory.CreateDirectory(folder);

        var expected = await GetExpectedHashAsync(update, ct).ConfigureAwait(false);
        var file = Path.Combine(folder, update.AssetName);
        await DownloadAsync(update.AssetUrl, file, update.Size, progress, ct).ConfigureAwait(false);

        string actual;
        await using (var fs = File.OpenRead(file))
            actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(file);
            throw new InvalidDataException("The download doesn't match the published checksum, so it was discarded.");
        }

        if (IsInstalled)
        {
            // /relaunch=1 makes the installer start the app again when it's done (see AudioPilotManager.iss).
            _pendingLaunch = new ProcessStartInfo(file)
            {
                Arguments = "/SILENT /SP- /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /relaunch=1",
                UseShellExecute = true,
            };
        }
        else
        {
            _pendingLaunch = SwapPortableExe(file, folder);
        }

        Log.Info($"Update {update.Tag} downloaded and verified; it will be applied on exit");
    }

    /// <summary>Runs the prepared installer or the new portable exe. Call after the single-instance mutex is released.</summary>
    public static void LaunchPending()
    {
        if (_pendingLaunch is null) return;
        try
        {
            Process.Start(_pendingLaunch);
        }
        catch (Exception ex)
        {
            Log.Error("Could not start the update", ex);
        }
    }

    /// <summary>Removes what a previous portable update left behind.</summary>
    public static void CleanUpLeftovers()
    {
        try
        {
            var old = Environment.ProcessPath + OldSuffix;
            if (File.Exists(old)) File.Delete(old);
            var folder = Path.Combine(Path.GetTempPath(), "AudioPilotManager-update");
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch
        {
            // Still locked by the exiting old copy; next start tries again.
        }
    }

    private static async Task<string> GetExpectedHashAsync(UpdateInfo update, CancellationToken ct)
    {
        var sums = await Http.GetStringAsync(update.ChecksumsUrl, ct).ConfigureAwait(false);
        foreach (var line in sums.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && parts[0].Length == 64
                && string.Equals(parts[1].TrimStart('*'), update.AssetName, StringComparison.OrdinalIgnoreCase))
                return parts[0];
        }

        throw new InvalidDataException("The release has no checksum for " + update.AssetName + ".");
    }

    private static async Task DownloadAsync(string url, string path, long size, IProgress<double>? progress, CancellationToken ct)
    {
        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var total = response.Content.Headers.ContentLength ?? size;

        await using var input = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        await using var output = File.Create(path);
        var buffer = new byte[81920];
        long done = 0;
        int read;
        while ((read = await input.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            await output.WriteAsync(buffer.AsMemory(0, read), ct).ConfigureAwait(false);
            done += read;
            if (total > 0) progress?.Report((double)done / total);
        }
    }

    /// <summary>
    /// Windows lets a running exe be renamed, not overwritten: move ourselves aside and put the new
    /// exe in our place. The old one is deleted by the next start.
    /// </summary>
    private static ProcessStartInfo SwapPortableExe(string zip, string folder)
    {
        var current = Environment.ProcessPath ?? throw new InvalidOperationException("Unknown executable path.");
        var extracted = Path.Combine(folder, "new");
        ZipFile.ExtractToDirectory(zip, extracted);
        var newExe = Path.Combine(extracted, Path.GetFileName(current));
        if (!File.Exists(newExe)) newExe = Path.Combine(extracted, "AudioPilotManager.exe");
        if (!File.Exists(newExe)) throw new InvalidDataException("The update package doesn't contain the app.");

        var old = current + OldSuffix;
        if (File.Exists(old)) File.Delete(old);
        File.Move(current, old);
        try
        {
            File.Copy(newExe, current);
        }
        catch
        {
            File.Move(old, current); // put things back the way they were
            throw;
        }

        return new ProcessStartInfo(current) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(current)! };
    }

    private static bool DetectInstalled()
    {
        try
        {
            var dir = Path.GetDirectoryName(Environment.ProcessPath);
            if (dir is null) return false;
            foreach (var hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
            {
                using var key = hive.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\{InstallerAppId}");
                if (key?.GetValue("InstallLocation") is string location
                    && string.Equals(Path.TrimEndingDirectorySeparator(location), Path.TrimEndingDirectorySeparator(dir), StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch
        {
            // Treat as portable.
        }

        return false;
    }

    private static Version Normalize(Version v) => new(v.Major, Math.Max(v.Minor, 0), Math.Max(v.Build, 0));
}
