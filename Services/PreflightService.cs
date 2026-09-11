using System.IO;
using SkyrimVersionManager.Models;

namespace SkyrimVersionManager.Services;

public enum PreflightSeverity { Info, Warning, Blocker }

public record PreflightIssue(PreflightSeverity Severity, string Title, string Detail);

/// <summary>
/// Pre-switch compatibility checks. Blockers require an explicit acknowledgement;
/// warnings and infos are shown for the user to weigh.
/// </summary>
public static class PreflightService
{
    // Plugins with header version >= 1.71 (introduced in game version 1.6.1130) are misread by
    // older executables: form IDs load wrong, causing crashes and corrupted saves.
    private const string NewHeaderIntroducedIn = "1.6.1130";
    private const float NewHeaderVersion = 1.705f;

    private static readonly string[] BaseMasters =
        { "Skyrim.esm", "Update.esm", "Dawnguard.esm", "HearthFires.esm", "Dragonborn.esm" };

    public static List<PreflightIssue> Run(
        string gameDir,
        string? installedVersion,
        GameVersion target,
        bool fullGame,
        bool manageSaves)
    {
        var issues = new List<PreflightIssue>();
        if (installedVersion == null || installedVersion == target.Version)
            return issues;

        bool downgrading = VersionMath.Compare(installedVersion, target.Version) > 0;
        var dataDir = Path.Combine(gameDir, "Data");

        // --- Fresh install that Steam hasn't finished setting up ---------------------------
        var firstRun = FirstRunService.PendingFirstRunMarkers(gameDir);
        if (firstRun.Count > 0)
        {
            issues.Add(new PreflightIssue(PreflightSeverity.Blocker,
                "The game has not been launched since Steam installed it",
                "Steam finishes a fresh install on the first Play: it runs the install script, registers the " +
                "game, and the launcher downloads the Anniversary Edition creations. Switching versions or " +
                "locking updates before that leaves Steam with a half-registered install it may try to repair " +
                "or re-download. Launch Skyrim once from Steam, reach the main menu, quit, then come back. " +
                "Detected: " + string.Join("; ", firstRun) + "."));
        }

        // --- Era jump: DLL mods + saves are era-specific ---------------------------------
        var eraFrom = VersionMath.Era(installedVersion);
        var eraTo = VersionMath.Era(target.Version);
        if (eraFrom != eraTo)
        {
            int dllCount = 0;
            var skseDir = Path.Combine(dataDir, "SKSE", "Plugins");
            if (Directory.Exists(skseDir))
                dllCount = Directory.EnumerateFiles(skseDir, "*.dll", SearchOption.TopDirectoryOnly).Count();

            issues.Add(new PreflightIssue(PreflightSeverity.Warning,
                $"Crossing a compatibility era ({eraFrom}.x -> {eraTo}.x)",
                "Native SKSE plugin DLLs are built per era (separate Address Library databases), so " +
                (dllCount > 0 ? $"the {dllCount} DLL mod(s) in Data\\SKSE\\Plugins " : "any DLL mods ") +
                "will not work until each has a build matching the target version. " +
                "SKSE itself also needs the matching era build."));
        }

        // --- Creations catalog written by a newer build -----------------------------------
        if (eraFrom != eraTo)
        {
            var catalog = CreationsCatalogService.LiveFile();
            if (CreationsCatalogService.HasStash(target.Version))
            {
                issues.Add(new PreflightIssue(PreflightSeverity.Info,
                    $"Creations catalog for {eraTo}.x will be restored",
                    "ContentCatalog.txt (the game's record of Creations-menu downloads) is kept per version era. " +
                    $"The current one is stashed and the copy from when {eraTo}.x last ran is put back."));
            }
            else if (CreationsCatalogService.IsIncompatible(catalog, target.Version))
            {
                issues.Add(new PreflightIssue(PreflightSeverity.Warning,
                    $"Creations catalog from {eraFrom}.x would crash {target.Version} at startup",
                    "Builds from 1.7.99 on write ContentCatalog.txt with GUID entries that older executables cannot " +
                    "parse (crash to desktop ~30 s into loading). The file will be stashed and set aside; the game " +
                    "rebuilds it on the next run and every installed creation still loads through Skyrim.ccc. " +
                    "Only the Creations menu's download history is affected."));
            }
        }

        // --- New plugin format vs old executable (the dangerous one) ---------------------
        if (VersionMath.Compare(target.Version, NewHeaderIntroducedIn) < 0 && Directory.Exists(dataDir))
        {
            var flagged = FindNewFormatPlugins(dataDir, excludeBaseMasters: fullGame);
            if (flagged.Count > 0)
            {
                var shown = string.Join(", ", flagged.Take(8));
                if (flagged.Count > 8) shown += $", ... ({flagged.Count} total)";
                issues.Add(new PreflightIssue(PreflightSeverity.Blocker,
                    $"{flagged.Count} plugin(s) use the 1.71 header format that {target.Version} cannot read",
                    $"Executables older than {NewHeaderIntroducedIn} misread these plugins' form IDs, causing " +
                    $"crashes and silently corrupted saves: {shown}. Remove or downgrade these plugins first, " +
                    "or pick 1.6.1130+ as the target."));
            }
        }

        // --- Orphaned post-AE Creations content on classic SE ----------------------------
        if (target.Version == "1.5.97" && Directory.Exists(dataDir))
        {
            var ccFiles = Directory.EnumerateFiles(dataDir, "cc*.es*", SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName).Where(f => f != null).Cast<string>().ToList();
            if (ccFiles.Count > 0)
            {
                issues.Add(new PreflightIssue(PreflightSeverity.Warning,
                    $"{ccFiles.Count} Creation Club file(s) will be left behind",
                    "1.5.97 predates the Anniversary Edition and the downgrade only copies old files over - " +
                    "it does not remove content newer versions added. The game auto-loads everything in Data, " +
                    "so leftover cc* files can crash the classic executable. Consider moving them out of Data."));
            }
        }

        // --- Save games are a one-way door ------------------------------------------------
        if (downgrading)
        {
            var savesDir = SavesService.LiveSavesDir();
            int saveCount = savesDir != null && Directory.Exists(savesDir)
                ? Directory.EnumerateFiles(savesDir, "*.ess", SearchOption.TopDirectoryOnly).Count()
                : 0;
            if (saveCount > 0)
            {
                issues.Add(manageSaves
                    ? new PreflightIssue(PreflightSeverity.Info,
                        $"{saveCount} save(s) will be stashed with version {installedVersion}",
                        "Saves load forward but not backward. Because 'Manage saves with game version' is on, " +
                        $"the current saves are moved to this app's stash for {installedVersion} and any saves " +
                        $"previously made on {target.Version} are restored - each version only sees its own saves.")
                    : new PreflightIssue(PreflightSeverity.Warning,
                        $"{saveCount} save(s) may not load on {target.Version}",
                        "Saves made on a newer version can fail to load (or corrupt) on an older one, and " +
                        "'Manage saves with game version' is OFF, so they stay visible in-game. Back them up " +
                        "or enable save management before switching."));
            }
        }

        // --- Mild note about the hybrid exe-only install ---------------------------------
        if (!fullGame)
        {
            issues.Add(new PreflightIssue(PreflightSeverity.Info,
                "Executables-only switch creates a hybrid install",
                $"Only the game binaries change to {target.Version}; data files stay from {installedVersion}. " +
                "This is the standard approach for SKSE compatibility and normally works fine - if the game " +
                "misbehaves afterwards, re-apply with the 'Full game' scope."));
        }

        return issues;
    }

    /// <summary>
    /// Reads each plugin's HEDR version (a float right after the "HEDR" subrecord marker in the
    /// TES4 header) and returns those using the post-1.6.1130 format.
    /// </summary>
    private static List<string> FindNewFormatPlugins(string dataDir, bool excludeBaseMasters)
    {
        var flagged = new List<string>();
        var extensions = new[] { ".esm", ".esp", ".esl" };

        foreach (var file in Directory.EnumerateFiles(dataDir, "*.*", SearchOption.TopDirectoryOnly))
        {
            if (!extensions.Contains(Path.GetExtension(file), StringComparer.OrdinalIgnoreCase)) continue;
            var name = Path.GetFileName(file);
            // A full downgrade replaces the base masters with old-format copies, so skip them then.
            if (excludeBaseMasters && BaseMasters.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;

            try
            {
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                var buf = new byte[256];
                int read = fs.Read(buf, 0, buf.Length);
                for (int i = 0; i + 10 <= read; i++)
                {
                    if (buf[i] == 'H' && buf[i + 1] == 'E' && buf[i + 2] == 'D' && buf[i + 3] == 'R')
                    {
                        float version = BitConverter.ToSingle(buf, i + 6); // "HEDR" + u16 size, then float
                        if (version >= NewHeaderVersion) flagged.Add(name);
                        break;
                    }
                }
            }
            catch
            {
                // Unreadable file: not our call to make - skip.
            }
        }
        return flagged;
    }
}
