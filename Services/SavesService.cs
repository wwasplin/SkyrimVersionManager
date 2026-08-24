using System.IO;

namespace SkyrimVersionManager.Services;

/// <summary>
/// "Manage saves with game version": each game version gets its own stash under data\saves\.
/// On a version switch the live saves folder is moved into the outgoing version's stash and the
/// incoming version's stash (if any) is moved back in - so in-game, only saves made on the
/// currently installed version are ever visible. Files are moved, never copied or deleted.
/// </summary>
public static class SavesService
{
    /// <summary>The game's live saves folder (Documents\My Games\Skyrim Special Edition\Saves), or null.</summary>
    public static string? LiveSavesDir()
    {
        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        if (string.IsNullOrEmpty(docs)) return null;
        return Path.Combine(docs, "My Games", "Skyrim Special Edition", "Saves");
    }

    public static string StashRoot => Path.Combine(Paths.DataDir, "saves");

    public static string StashDir(string version) => Path.Combine(StashRoot, version);

    public static void SwitchSaves(string fromVersion, string toVersion, Action<string> log)
    {
        var live = LiveSavesDir();
        if (live == null)
        {
            log("Save management: Documents folder not found - skipping.");
            return;
        }
        Directory.CreateDirectory(live);

        int stashed = MoveContents(live, StashDir(fromVersion));
        int restored = MoveContents(StashDir(toVersion), live);

        log($"Save management: stashed {stashed} file(s) with version {fromVersion}, " +
            $"restored {restored} file(s) for {toVersion}.");
        if (restored == 0)
            log($"No previous saves exist for {toVersion} - the in-game save list will start empty. " +
                "Nothing is lost: the other saves return when you switch back.");
    }

    /// <summary>Moves everything (recursively) from one folder into another; returns files moved.</summary>
    private static int MoveContents(string fromDir, string toDir)
    {
        if (!Directory.Exists(fromDir)) return 0;

        int moved = 0;
        foreach (var file in Directory.EnumerateFiles(fromDir, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(fromDir, file);
            var dst = Path.Combine(toDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(dst)!);
            File.Move(file, dst, true);
            moved++;
        }

        // Tidy now-empty subfolders left behind by the move (keep the root folder itself).
        foreach (var dir in Directory.EnumerateDirectories(fromDir, "*", SearchOption.AllDirectories)
                                     .OrderByDescending(d => d.Length))
        {
            try { if (!Directory.EnumerateFileSystemEntries(dir).Any()) Directory.Delete(dir); } catch { }
        }
        return moved;
    }
}
