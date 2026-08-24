using System.Diagnostics;
using System.IO;

namespace SkyrimVersionManager.Services;

/// <summary>
/// Drives a depot download through the user's already-logged-in desktop Steam client:
/// the app hands the user download_depot commands for the Steam console, then watches
/// Steam's content download folder to detect when each depot has finished.
/// No credentials are involved at any point.
/// </summary>
public class SteamConsoleService
{
    /// <summary>Where the Steam console writes download_depot output for this app.</summary>
    public static string? ContentRoot()
    {
        var steam = SteamLocator.GetSteamRoot();
        return steam == null ? null : Path.Combine(steam, "steamapps", "content", "app_" + SteamLocator.SkyrimAppId);
    }

    public static string BuildCommand(string appId, string depotId, string? manifestId) =>
        manifestId == null
            ? $"download_depot {appId} {depotId}"
            : $"download_depot {appId} {depotId} {manifestId}";

    public static void OpenConsole() =>
        Process.Start(new ProcessStartInfo { FileName = "steam://open/console", UseShellExecute = true });

    /// <summary>
    /// Waits until Steam has finished writing a depot into contentDepotDir.
    /// Completion heuristic: files exist, the expected key file (when known) is present,
    /// and the total size has been stable for several consecutive polls.
    /// </summary>
    public async Task WaitForDepotAsync(
        string contentDepotDir,
        string? expectedFile,
        Action<string> log,
        CancellationToken ct)
    {
        var started = DateTime.UtcNow;
        var timeout = TimeSpan.FromMinutes(90);
        long lastSize = -1, lastLoggedSize = 0;
        int stablePolls = 0;
        bool seenFiles = false;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (DateTime.UtcNow - started > timeout)
                throw new TimeoutException($"Gave up waiting for Steam to download into {contentDepotDir}.");

            await Task.Delay(2000, ct);

            if (!Directory.Exists(contentDepotDir))
            {
                if (!seenFiles && (DateTime.UtcNow - started).TotalMinutes >= 3 && (DateTime.UtcNow - started).TotalSeconds % 60 < 2)
                    log("Still waiting - make sure the command was pasted into the Steam console and Enter was pressed.");
                continue;
            }

            long size = 0;
            int fileCount = 0;
            foreach (var f in Directory.EnumerateFiles(contentDepotDir, "*", SearchOption.AllDirectories))
            {
                size += new FileInfo(f).Length;
                fileCount++;
            }
            if (fileCount == 0) continue;

            if (!seenFiles)
            {
                seenFiles = true;
                log("Steam has started writing files ...");
            }

            if (size - lastLoggedSize >= 100 * 1024 * 1024)
            {
                log($"  {size / (1024.0 * 1024.0):F0} MB downloaded so far ...");
                lastLoggedSize = size;
            }

            if (expectedFile != null && !File.Exists(Path.Combine(contentDepotDir, expectedFile)))
            {
                stablePolls = 0;
                lastSize = size;
                continue;
            }

            if (size == lastSize)
            {
                // Steam routinely stalls downloads (throttling, disk flush), so a short window
                // would declare a half-finished depot complete. 30s of zero growth is required.
                if (++stablePolls >= 15)
                {
                    log($"Download complete: {fileCount} file(s), {size / (1024.0 * 1024.0):F0} MB.");
                    return;
                }
            }
            else
            {
                stablePolls = 0;
                lastSize = size;
            }
        }
    }
}
