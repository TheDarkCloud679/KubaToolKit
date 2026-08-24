using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace KubaToolKit.Shared.Services;

public class UpdateInfo
{
    public Version Version { get; init; } = new(0, 0, 0);
    public string ZipDownloadUrl { get; init; } = "";
}

// Checks GitHub Releases (the repo is public, so no token is needed) for a
// newer tagged build, and -- if found -- downloads its zip asset, extracts
// it, and hands off to a small batch script that waits for this process to
// exit before overwriting it and relaunching. Every step is best-effort:
// a flaky network or a malformed release must never block the app from
// starting with whatever version is already installed.
public static class UpdateService
{
    private const string ReleasesApiUrl =
        "https://api.github.com/repos/TheDarkCloud679/KubaToolKit/releases/latest";

    private static readonly HttpClient Client = BuildClient();

    private static HttpClient
    BuildClient()
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("KubaToolKit-Updater", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        return client;
    }

    // Short timeout on purpose -- a slow/unreachable GitHub must not turn
    // into a multi-second delay before the app even shows a window.
    public static async Task<UpdateInfo?>
    CheckForUpdateAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            using var response = await Client.GetAsync(ReleasesApiUrl, cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cts.Token);

            using var doc = JsonDocument.Parse(body);

            var tag = doc.RootElement.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() : null;

            if (string.IsNullOrWhiteSpace(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out var remoteVersion))
            {
                return null;
            }

            var currentVersion =
                System.Reflection.Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);

            if (!IsNewer(remoteVersion, currentVersion))
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty("assets", out var assetsEl) || assetsEl.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var zipUrl =
                assetsEl.EnumerateArray()
                    .Select(a => new
                    {
                        Name = a.TryGetProperty("name", out var n) ? n.GetString() : null,
                        Url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null
                    })
                    .FirstOrDefault(a => a.Name?.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) == true)
                    ?.Url;

            if (string.IsNullOrWhiteSpace(zipUrl))
            {
                return null;
            }

            return new UpdateInfo { Version = remoteVersion, ZipDownloadUrl = zipUrl };
        }
        catch (Exception ex)
        {
            Logger.Error("UpdateService: update check failed.", ex);

            return null;
        }
    }

    // <Version> in the csproj is always 3-part (Major.Minor.Build) -- the
    // Revision component isn't used, and comparing it directly via
    // Version.CompareTo would be wrong anyway: a 3-part Version has an
    // implicit Revision of -1, while Assembly.GetName().Version (4-part,
    // Revision defaulted to 0) always reports 0, so the current release's
    // own tag would otherwise permanently look "newer" than what's running.
    private static bool
    IsNewer(
        Version remote,
        Version local) =>
        (remote.Major, remote.Minor, Math.Max(remote.Build, 0))
            .CompareTo((local.Major, local.Minor, Math.Max(local.Build, 0))) > 0;

    public static async Task
    DownloadAndApplyAsync(
        UpdateInfo update,
        IProgress<double> progress)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "KubaToolKit_Update_" + Guid.NewGuid().ToString("N"));

        Directory.CreateDirectory(tempDir);

        var zipPath = Path.Combine(tempDir, "update.zip");
        var extractDir = Path.Combine(tempDir, "extracted");

        using (var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5)))
        {
            using var response =
                await Client.GetAsync(update.ZipDownloadUrl, HttpCompletionOption.ResponseHeadersRead, cts.Token);

            response.EnsureSuccessStatusCode();

            var totalBytes = response.Content.Headers.ContentLength ?? -1L;

            await using var contentStream = await response.Content.ReadAsStreamAsync(cts.Token);
            await using var fileStream = File.Create(zipPath);

            var buffer = new byte[81920];
            long totalRead = 0;
            int read;

            while ((read = await contentStream.ReadAsync(buffer, cts.Token)) > 0)
            {
                await fileStream.WriteAsync(buffer.AsMemory(0, read), cts.Token);

                totalRead += read;

                if (totalBytes > 0)
                {
                    progress.Report(Math.Min(99.0, (double)totalRead / totalBytes * 100.0));
                }
            }
        }

        progress.Report(100);

        ZipFile.ExtractToDirectory(zipPath, extractDir);

        // The release zip is expected to hold the publish folder's
        // contents directly at its root -- but if it instead wraps them
        // in a single top-level folder, use that as the real source
        // rather than copying the wrapper folder itself into the install
        // directory.
        var sourceDir = extractDir;
        var topEntries = Directory.GetFileSystemEntries(extractDir);

        if (topEntries.Length == 1 && Directory.Exists(topEntries[0]))
        {
            sourceDir = topEntries[0];
        }

        var installDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        var exePath = Path.Combine(installDir, "KubaToolKit.exe");
        var scriptPath = Path.Combine(tempDir, "apply_update.bat");

        // robocopy's own default retry count (1,000,000 attempts, 30s
        // apart) would hang effectively forever on a single locked file --
        // /r:2 /w:1 makes it fail fast instead, so THIS script's own retry
        // loop is what actually waits out this process's shutdown.
        var script =
            "@echo off\r\n" +
            "setlocal\r\n" +
            "set COUNT=0\r\n" +
            ":retry\r\n" +
            "set /a COUNT+=1\r\n" +
            "timeout /t 2 /nobreak >nul\r\n" +
            $"robocopy \"{sourceDir}\" \"{installDir}\" /e /is /it /r:2 /w:1 >nul 2>&1\r\n" +
            "if errorlevel 8 if %COUNT% lss 20 goto retry\r\n" +
            $"start \"\" \"{exePath}\"\r\n";

        File.WriteAllText(scriptPath, script);

        Process.Start(
            new ProcessStartInfo
            {
                FileName = scriptPath,
                WorkingDirectory = tempDir,
                WindowStyle = ProcessWindowStyle.Hidden,
                UseShellExecute = true
            });
    }
}
