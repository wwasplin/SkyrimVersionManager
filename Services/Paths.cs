using System.IO;

namespace SkyrimVersionManager.Services;

/// <summary>
/// Portable data layout: everything lives in a "data" folder next to the exe.
/// Falls back to %LOCALAPPDATA%\SkyrimVersionManager if the exe folder is not writable
/// (e.g. the exe was dropped into Program Files).
/// </summary>
public static class Paths
{
    private static string? _dataDir;

    public static string DataDir
    {
        get
        {
            if (_dataDir != null) return _dataDir;

            var portable = Path.Combine(AppContext.BaseDirectory, "data");
            _dataDir = IsWritable(portable)
                ? portable
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SkyrimVersionManager");
            Directory.CreateDirectory(_dataDir);
            return _dataDir;
        }
    }

    public static string CacheDir => Ensure(Path.Combine(DataDir, "cache"));
    public static string ToolsDir => Ensure(Path.Combine(DataDir, "tools"));
    public static string BackupsDir => Ensure(Path.Combine(DataDir, "backups"));
    public static string SettingsFile => Path.Combine(DataDir, "settings.json");
    public static string LogFile => Path.Combine(DataDir, "log.txt");
    public static string VersionsOverrideFile => Path.Combine(DataDir, "versions.json");

    private static string Ensure(string dir)
    {
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static bool IsWritable(string dir)
    {
        try
        {
            Directory.CreateDirectory(dir);
            var probe = Path.Combine(dir, ".write-probe");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
