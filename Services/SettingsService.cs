using System.IO;
using System.Text.Json;
using SkyrimVersionManager.Models;

namespace SkyrimVersionManager.Services;

public class SettingsService
{
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };

    public AppSettings Load()
    {
        try
        {
            if (File.Exists(Paths.SettingsFile))
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(Paths.SettingsFile));
                if (settings != null) return settings;
            }
        }
        catch
        {
            // Corrupt settings: fall through to defaults.
        }
        return new AppSettings();
    }

    public void Save(AppSettings settings)
    {
        File.WriteAllText(Paths.SettingsFile, JsonSerializer.Serialize(settings, JsonOpts));
    }
}
