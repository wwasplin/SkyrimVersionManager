using System.IO;
using System.Text.RegularExpressions;
using SkyrimVersionManager.Models;

namespace SkyrimVersionManager.Services;

public record ApplyRequest(
    string GameDir,
    GameVersion Target,
    bool FullGame,
    bool Backup,
    bool LockUpdates,
    string? InstalledVersion);

/// <summary>
/// Cache layout: data\cache\&lt;version&gt;\depot_&lt;id&gt; plus a "depot_&lt;id&gt;.complete" marker.
/// Acquisition (Steam console or DepotDownloader) fills the cache; ApplyCachedAsync then
/// backs up, copies into the game folder, and locks Steam updates.
/// </summary>
public class DowngradeService
{
    private readonly VersionCatalog _catalog;

    public DowngradeService(VersionCatalog catalog)
    {
        _catalog = catalog;
    }

    public string CacheDirFor(GameVersion version, string depotId) =>
        Path.Combine(Paths.CacheDir, version.Version, "depot_" + depotId);

    private static string CompleteMarker(string depotDir) => depotDir + ".complete";

    public bool IsCached(GameVersion version, bool fullGame) =>
        _catalog.DepotsForScope(fullGame).All(d => File.Exists(CompleteMarker(CacheDirFor(version, d))));

    public string[] MissingDepots(GameVersion version, bool fullGame) =>
        _catalog.DepotsForScope(fullGame)
            .Where(d => !File.Exists(CompleteMarker(CacheDirFor(version, d))))
            .ToArray();

    public void MarkCached(GameVersion version, string depotId) =>
        File.WriteAllText(CompleteMarker(CacheDirFor(version, depotId)), DateTime.UtcNow.ToString("o"));

    /// <summary>Marker recording which scope a backup covers ("full" or "exe").</summary>
    private static string ScopeMarker(string backupDir) => Path.Combine(backupDir, ".scope");

    /// <summary>
    /// True when a backup of this version exists AND covers the requested scope. An
    /// executables-only backup must not satisfy a full-game restore - it would silently
    /// produce a hybrid install while claiming to be a clean full version.
    /// </summary>
    public bool HasBackup(string version, bool fullGame)
    {
        var dir = Path.Combine(Paths.BackupsDir, version);
        if (!Directory.Exists(dir) ||
            !Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                .Any(f => !string.Equals(Path.GetFileName(f), ".scope", StringComparison.OrdinalIgnoreCase)))
            return false;
        if (!fullGame) return true;
        var marker = ScopeMarker(dir);
        return File.Exists(marker) && File.ReadAllText(marker).Trim() == "full";
    }

    /// <summary>Restoring the current Steam version from a local backup beats re-downloading it.</summary>
    public bool ShouldUseBackupAsSource(GameVersion target, bool fullGame) =>
        target.IsLatest && HasBackup(target.Version, fullGame) && !IsCached(target, fullGame);

    /// <summary>Copies a finished Steam-console download into the cache and removes the transient content dir.</summary>
    public void CacheFromContentDir(GameVersion target, string depotId, string contentDepotDir, Action<string> log)
    {
        var cacheDir = CacheDirFor(target, depotId);
        if (Directory.Exists(cacheDir)) Directory.Delete(cacheDir, true);
        Directory.CreateDirectory(cacheDir);

        int copied = 0;
        foreach (var file in Directory.EnumerateFiles(contentDepotDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(contentDepotDir, file);
            var dst = Path.Combine(cacheDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Copy(file, dst, true);
            copied++;
        }
        MarkCached(target, depotId);
        log($"Cached {copied} file(s) for depot {depotId}.");

        try
        {
            Directory.Delete(contentDepotDir, true);
            log("Removed Steam's transient download folder to free disk space.");
        }
        catch
        {
            log($"Note: could not remove {contentDepotDir} - safe to delete manually.");
        }
    }

    /// <summary>
    /// Sanity check after acquisition: does the cached SkyrimSE.exe report the expected version?
    /// Returns the detected version on a mismatch (so the caller can ask before applying), else null.
    /// </summary>
    public string? VerifyCachedExe(GameVersion target, Action<string> log)
    {
        var exe = Path.Combine(CacheDirFor(target, _catalog.ExeDepot), "SkyrimSE.exe");
        var v = VersionDetector.DetectExeFileVersion(exe);
        if (v == null)
        {
            log("WARNING: cached executable depot has no readable SkyrimSE.exe version - proceed with caution.");
            return null;
        }
        if (v == target.Version)
        {
            log($"Verified: downloaded SkyrimSE.exe reports version {v}.");
            return null;
        }
        if (target.IsLatest)
        {
            log($"Downloaded SkyrimSE.exe reports version {v} (current Steam build).");
            return null;
        }
        log($"WARNING: downloaded SkyrimSE.exe reports {v}, expected {target.Version}. " +
            "The wrong manifest may have been downloaded - check the commands/manifest IDs.");
        return v;
    }

    public async Task ApplyCachedAsync(
        ApplyRequest req,
        Action<string> log,
        Action<double> progress,
        CancellationToken ct)
    {
        var sources = new List<string>();
        if (ShouldUseBackupAsSource(req.Target, req.FullGame))
        {
            log($"Using local backup of {req.Target.Version} (no download needed).");
            sources.Add(Path.Combine(Paths.BackupsDir, req.Target.Version));
        }
        else
        {
            foreach (var depot in _catalog.DepotsForScope(req.FullGame))
            {
                var depotDir = CacheDirFor(req.Target, depot);
                if (!File.Exists(CompleteMarker(depotDir)))
                    throw new InvalidOperationException($"Depot {depot} of {req.Target.Version} is not cached - download did not complete.");
                sources.Add(depotDir);
            }
        }

        // Collect (relativePath -> sourceFile); later sources win on collision (they never
        // should collide across depots, but be deterministic anyway).
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in sources)
        {
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                if (rel.StartsWith(".DepotDownloader", StringComparison.OrdinalIgnoreCase)) continue;
                if (rel.Equals(".scope", StringComparison.OrdinalIgnoreCase)) continue;
                files[rel] = file;
            }
        }
        if (files.Count == 0)
            throw new InvalidOperationException("No files found to apply - the download may have failed.");

        if (req.Backup && !string.IsNullOrEmpty(req.InstalledVersion))
        {
            var myBackupDir = Path.Combine(Paths.BackupsDir, req.InstalledVersion!);
            int backedUp = 0;
            foreach (var (rel, _) in files)
            {
                ct.ThrowIfCancellationRequested();
                var current = Path.Combine(req.GameDir, rel);
                var backup = Path.Combine(myBackupDir, rel);
                if (File.Exists(current) && !File.Exists(backup))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(backup)!);
                    File.Copy(current, backup);
                    backedUp++;
                }
            }
            // Record which scope this backup covers, so a later restore knows whether the
            // backup is a complete version or just the executables. "full" is never downgraded.
            Directory.CreateDirectory(myBackupDir);
            var marker = ScopeMarker(myBackupDir);
            if (req.FullGame)
                File.WriteAllText(marker, "full");
            else if (!File.Exists(marker))
                File.WriteAllText(marker, "exe");

            log($"Backed up {backedUp} file(s) of version {req.InstalledVersion} to {myBackupDir}");
        }

        // Point of no return: cancelling mid-copy would leave a half-updated install, so the
        // token is checked once here and then ignored until every file is in place.
        ct.ThrowIfCancellationRequested();
        log($"Applying {files.Count} file(s) to {req.GameDir} ... (cancel is disabled during this phase " +
            "to avoid a half-updated install)");
        int applied = 0;
        foreach (var (rel, src) in files)
        {
            var dst = Path.Combine(req.GameDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            if (File.Exists(dst))
                File.SetAttributes(dst, FileAttributes.Normal);
            File.Copy(src, dst, true);
            applied++;
            if (applied % 50 == 0) progress(100.0 * applied / files.Count);
        }
        progress(100);
        log($"Applied {applied} file(s).");
        if (ct.IsCancellationRequested)
            log("Note: a cancel was requested during the apply phase - it was completed anyway to keep the install consistent.");

        if (req.LockUpdates)
            LockSteamUpdates(req.GameDir, log);

        await Task.CompletedTask;
    }

    /// <summary>
    /// Sets AutoUpdateBehavior to "update only on launch" and makes the appmanifest read-only,
    /// the standard community trick to stop Steam from silently re-updating a downgraded install.
    /// </summary>
    public static void LockSteamUpdates(string gameDir, Action<string> log)
    {
        var acf = SteamLocator.FindAppManifest(gameDir);
        if (acf == null)
        {
            log("WARNING: appmanifest_489830.acf not found - could not lock Steam updates. " +
                "Set Skyrim to 'Only update this game when I launch it' in Steam properties manually.");
            return;
        }
        try
        {
            File.SetAttributes(acf, FileAttributes.Normal);
            var text = File.ReadAllText(acf);
            text = Regex.Replace(text, "(\"AutoUpdateBehavior\"\\s+\")\\d+(\")", "${1}1${2}");
            File.WriteAllText(acf, text);
            File.SetAttributes(acf, FileAttributes.ReadOnly);
            log($"Steam updates locked: {Path.GetFileName(acf)} set to update-on-launch-only and marked read-only.");
            log("NOTE: launch the game via SKSE or SkyrimSE.exe directly (not the Steam Play button) to keep Steam from updating it.");
        }
        catch (Exception ex)
        {
            log("WARNING: failed to lock Steam updates: " + ex.Message);
        }
    }

    public static void UnlockSteamUpdates(string gameDir, Action<string> log)
    {
        var acf = SteamLocator.FindAppManifest(gameDir);
        if (acf == null)
        {
            log("appmanifest_489830.acf not found - nothing to unlock.");
            return;
        }
        try
        {
            File.SetAttributes(acf, FileAttributes.Normal);
            log($"Steam updates unlocked ({Path.GetFileName(acf)} is writable again). Steam may now update the game.");
        }
        catch (Exception ex)
        {
            log("Failed to unlock: " + ex.Message);
        }
    }

    public string DepotLabel(string depot)
    {
        if (depot == _catalog.ExeDepot) return "executables";
        if (depot == _catalog.CoreDepot) return "core files";
        return "game assets";
    }
}
