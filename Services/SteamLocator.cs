using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SkyrimVersionManager.Services;

/// <summary>Finds the Steam install and the Skyrim SE game folder across all Steam libraries.</summary>
public static class SteamLocator
{
    public const string SkyrimAppId = "489830";

    public static string? GetSteamRoot()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
            {
                if (key?.GetValue("SteamPath") is string p && !string.IsNullOrWhiteSpace(p))
                    return Path.GetFullPath(p.Replace('/', '\\'));
            }
            using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Valve\Steam"))
            {
                if (key?.GetValue("InstallPath") is string p && !string.IsNullOrWhiteSpace(p))
                    return p;
            }
        }
        catch
        {
            // Registry unavailable; give up on auto-detection.
        }
        return null;
    }

    /// <summary>All Steam library roots (including the main install), parsed from libraryfolders.vdf.</summary>
    public static List<string> GetLibraryRoots()
    {
        var roots = new List<string>();
        var steam = GetSteamRoot();
        if (steam == null) return roots;
        roots.Add(steam);

        var vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (File.Exists(vdf))
        {
            foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
            {
                var path = m.Groups[1].Value.Replace(@"\\", @"\");
                if (Directory.Exists(path) && !roots.Contains(path, StringComparer.OrdinalIgnoreCase))
                    roots.Add(path);
            }
        }
        return roots;
    }

    /// <summary>Path to the Skyrim Special Edition folder containing SkyrimSE.exe, or null.</summary>
    public static string? FindSkyrimDir()
    {
        foreach (var root in GetLibraryRoots())
        {
            var dir = Path.Combine(root, "steamapps", "common", "Skyrim Special Edition");
            if (File.Exists(Path.Combine(dir, "SkyrimSE.exe")))
                return dir;
        }
        return null;
    }

    /// <summary>The appmanifest_489830.acf for a given game dir (gameDir\..\..\appmanifest_489830.acf).</summary>
    public static string? FindAppManifest(string gameDir)
    {
        try
        {
            var steamapps = Path.GetFullPath(Path.Combine(gameDir, "..", ".."));
            var acf = Path.Combine(steamapps, $"appmanifest_{SkyrimAppId}.acf");
            if (File.Exists(acf)) return acf;
        }
        catch
        {
            // Unusual game path layout; search the libraries instead.
        }

        foreach (var root in GetLibraryRoots())
        {
            var acf = Path.Combine(root, "steamapps", $"appmanifest_{SkyrimAppId}.acf");
            if (File.Exists(acf)) return acf;
        }
        return null;
    }
}
