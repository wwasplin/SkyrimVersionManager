// Code Created by Asplin Tech, Will@AsplinTech.com. Do not reuse without permission.
using System.IO;
using System.Text.RegularExpressions;

namespace SkyrimVersionManager.Services;

/// <summary>
/// Keeps %LOCALAPPDATA%\Skyrim Special Edition\ContentCatalog.txt (the game's record of
/// Creations-menu downloads) compatible with the installed executable.
///
/// Builds from 1.7.99 on write catalog entries keyed by GUID ("CSV2_016105c0-..."); older
/// executables expect numeric keys ("CSV2_5615"), parse them with stoull, and crash to desktop
/// about half a minute into startup. Each compatibility era therefore gets its own stash under
/// data\catalogs\: on a switch the outgoing era's catalog is stashed, the incoming era's is
/// restored, and when none exists an incompatible catalog is simply set aside - the game
/// rebuilds it, and every installed creation still loads through Skyrim.ccc.
/// </summary>
public static class CreationsCatalogService
{
    /// <summary>First build that writes GUID-keyed catalog entries.</summary>
    public const string GuidFormatIntroducedIn = "1.7.99";

    private static readonly Regex KeyPattern = new("\"CSV2_([^\"]+)\"", RegexOptions.Compiled);

    public static string LiveDir(string? overrideDir = null) =>
        overrideDir ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Skyrim Special Edition");

    public static string LiveFile(string? liveDir = null) => Path.Combine(LiveDir(liveDir), "ContentCatalog.txt");

    public static string StashRoot => Path.Combine(Paths.DataDir, "catalogs");

    public static string StashFile(string version) =>
        Path.Combine(StashRoot, VersionMath.Era(version), "ContentCatalog.txt");

    /// <summary>True when the catalog holds any non-numeric (GUID) key.</summary>
    public static bool HasGuidKeys(string catalogFile)
    {
        try
        {
            return KeyPattern.Matches(File.ReadAllText(catalogFile))
                .Any(m => !m.Groups[1].Value.All(char.IsDigit));
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Executables older than 1.7.99 cannot read a GUID-keyed catalog.</summary>
    public static bool IsIncompatible(string catalogFile, string targetVersion) =>
        File.Exists(catalogFile) &&
        VersionMath.Compare(targetVersion, GuidFormatIntroducedIn) < 0 &&
        HasGuidKeys(catalogFile);

    public static bool HasStash(string version) => File.Exists(StashFile(version));

    /// <summary>
    /// Runs after the game files have been switched from one version to another.
    /// Files are copied into the stash and only removed from the live folder when they would
    /// crash the target executable.
    /// </summary>
    public static void Switch(string fromVersion, string toVersion, Action<string> log, string? liveDir = null)
    {
        if (VersionMath.Era(fromVersion) == VersionMath.Era(toVersion)) return;

        var live = LiveFile(liveDir);
        var outgoing = StashFile(fromVersion);
        var incoming = StashFile(toVersion);

        if (File.Exists(live))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(outgoing)!);
            File.Copy(live, outgoing, true);
            log($"Creations catalog: stashed the {VersionMath.Era(fromVersion)}.x catalog to {outgoing}");
        }

        if (File.Exists(incoming))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(live)!);
            File.Copy(incoming, live, true);
            log($"Creations catalog: restored the {VersionMath.Era(toVersion)}.x catalog written when that version last ran.");
            return;
        }

        if (IsIncompatible(live, toVersion))
        {
            File.Delete(live);
            log($"Creations catalog: set aside - it was written by a {VersionMath.Era(fromVersion)}.x build with GUID entries " +
                $"that {toVersion} cannot parse (startup crash). The game rebuilds it; installed creations still load via Skyrim.ccc.");
        }
    }
}
