using System.IO;

namespace SkyrimVersionManager.Services;

public record StorageItem(string Kind, string Name, string Path, long SizeBytes)
{
    public string SizeText => FormatSize(SizeBytes);
    public string Description => $"{Kind}: {Name} ({SizeText})";

    public static string FormatSize(long bytes)
    {
        if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F1} GB";
        if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):F0} MB";
        return $"{bytes / 1024.0:F0} KB";
    }
}

/// <summary>Enumerates and deletes the app's stored data: cached version downloads, backups, tools.</summary>
public static class StorageService
{
    public static List<StorageItem> GetItems()
    {
        var items = new List<StorageItem>();

        if (Directory.Exists(Paths.CacheDir))
        {
            foreach (var dir in Directory.GetDirectories(Paths.CacheDir).OrderBy(d => d))
                items.Add(new StorageItem("Downloaded version", System.IO.Path.GetFileName(dir), dir, DirSize(dir)));
        }

        if (Directory.Exists(Paths.BackupsDir))
        {
            foreach (var dir in Directory.GetDirectories(Paths.BackupsDir).OrderBy(d => d))
                items.Add(new StorageItem("Backup", System.IO.Path.GetFileName(dir), dir, DirSize(dir)));
        }

        if (Directory.Exists(SavesService.StashRoot))
        {
            foreach (var dir in Directory.GetDirectories(SavesService.StashRoot).OrderBy(d => d))
                items.Add(new StorageItem("Stashed saves", System.IO.Path.GetFileName(dir), dir, DirSize(dir)));
        }

        var toolDir = System.IO.Path.Combine(Paths.ToolsDir, "DepotDownloader");
        if (Directory.Exists(toolDir))
            items.Add(new StorageItem("Tool", "DepotDownloader (re-downloaded automatically if needed)", toolDir, DirSize(toolDir)));

        return items;
    }

    public static void Delete(StorageItem item)
    {
        if (Directory.Exists(item.Path))
            Directory.Delete(item.Path, true);

        // Cached versions keep "depot_<id>.complete" markers next to the depot folders inside the
        // version directory, so they vanish with it. Nothing else to clean up.
    }

    private static long DirSize(string dir)
    {
        long size = 0;
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            try { size += new FileInfo(f).Length; } catch { }
        }
        return size;
    }
}
