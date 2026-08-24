using System.Diagnostics;
using System.IO;

namespace SkyrimVersionManager.Services;

public static class VersionDetector
{
    /// <summary>
    /// Reads the installed game version from SkyrimSE.exe's version resource.
    /// Returns e.g. "1.6.1170" (trailing ".0" build component trimmed), or null.
    /// </summary>
    public static string? DetectGameVersion(string gameDir)
    {
        var exe = Path.Combine(gameDir, "SkyrimSE.exe");
        if (!File.Exists(exe)) return null;
        return ReadVersion(exe);
    }

    /// <summary>Reads the SKSE loader version, normalized (e.g. "0.2.2.6" -> "2.2.6"), or null if absent.</summary>
    public static string? DetectSkseVersion(string gameDir)
    {
        var exe = Path.Combine(gameDir, "skse64_loader.exe");
        if (!File.Exists(exe)) return null;
        var raw = ReadVersion(exe);
        if (raw == null) return null;

        var parts = raw.Split('.').ToList();
        while (parts.Count > 1 && parts[0] == "0") parts.RemoveAt(0);
        return string.Join(".", parts);
    }

    /// <summary>Normalized file version of any exe (e.g. "1.6.1170"), or null.</summary>
    public static string? DetectExeFileVersion(string exePath) =>
        File.Exists(exePath) ? ReadVersion(exePath) : null;

    private static string? ReadVersion(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            var raw = info.FileVersion ?? info.ProductVersion;
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // Normalize "1, 6, 1170, 0" or "1.6.1170.0" -> "1.6.1170"
            var parts = raw.Replace(",", ".").Split('.', StringSplitOptions.TrimEntries)
                           .Where(p => p.Length > 0).ToList();
            while (parts.Count > 3 && parts[^1] == "0") parts.RemoveAt(parts.Count - 1);
            return string.Join(".", parts);
        }
        catch
        {
            return null;
        }
    }
}
