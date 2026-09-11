// Code Created by Asplin Tech, Will@AsplinTech.com. Do not reuse without permission.
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SkyrimVersionManager.Services;

/// <summary>
/// Detects a Steam install that has never been launched. Steam finishes a fresh install on the
/// first Play: it runs installscript.vdf (writes the Bethesda "Installed Path" registry entry and
/// seeds the Documents INIs), records LastPlayed, and the launcher then downloads the Anniversary
/// Edition creations. Replacing files or locking the appmanifest before that leaves Steam with a
/// half-registered install that it may try to repair or re-download.
/// </summary>
public static class FirstRunService
{
    // installscript.vdf targets HKLM\SOFTWARE\Bethesda Softworks\... in the 32-bit view.
    private const string BethesdaKey = @"SOFTWARE\Bethesda Softworks\Skyrim Special Edition";

    /// <summary>
    /// Evidence that the install has not been launched yet; empty when it has been run or the
    /// markers cannot be read (unknown is treated as "fine" to avoid false alarms).
    /// </summary>
    public static List<string> PendingFirstRunMarkers(string gameDir)
    {
        var markers = new List<string>();

        var acf = SteamLocator.FindAppManifest(gameDir);
        if (acf != null)
        {
            try
            {
                var m = Regex.Match(File.ReadAllText(acf), "\"LastPlayed\"\\s+\"(\\d+)\"");
                if (m.Success && m.Groups[1].Value == "0")
                    markers.Add("Steam has never recorded a launch (LastPlayed is 0 in appmanifest_489830.acf)");
            }
            catch
            {
                // Unreadable manifest: nothing to conclude.
            }
        }

        if (!InstallScriptHasRun())
            markers.Add("Steam's first-launch install script has not run (no 'Installed Path' registry entry)");

        return markers;
    }

    private static bool InstallScriptHasRun()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32);
            using var key = hklm.OpenSubKey(BethesdaKey);
            return key?.GetValue("Installed Path") is string p && !string.IsNullOrWhiteSpace(p);
        }
        catch
        {
            return true;
        }
    }
}
