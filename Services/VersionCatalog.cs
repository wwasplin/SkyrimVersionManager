using System.IO;
using System.Reflection;
using System.Text.Json;
using SkyrimVersionManager.Models;

namespace SkyrimVersionManager.Services;

public class VersionCatalog
{
    public string AppId { get; private set; } = "489830";
    public string CoreDepot { get; private set; } = "489831";
    public string AssetsDepot { get; private set; } = "489832";
    public string ExeDepot { get; private set; } = "489833";
    public List<GameVersion> Versions { get; private set; } = new();

    /// <summary>
    /// Loads the embedded catalog, or data\versions.json if the user has placed an
    /// override there (lets the catalog be extended without rebuilding the app).
    /// </summary>
    public static VersionCatalog Load()
    {
        string json;
        if (File.Exists(Paths.VersionsOverrideFile))
        {
            json = File.ReadAllText(Paths.VersionsOverrideFile);
        }
        else
        {
            var asm = Assembly.GetExecutingAssembly();
            using var stream = asm.GetManifestResourceStream("SkyrimVersionManager.versions.json")
                ?? throw new InvalidOperationException("Embedded versions.json not found.");
            using var reader = new StreamReader(stream);
            json = reader.ReadToEnd();
        }

        var file = JsonSerializer.Deserialize<VersionCatalogFile>(json)
            ?? throw new InvalidOperationException("versions.json could not be parsed.");

        var catalog = new VersionCatalog
        {
            AppId = file.AppId,
            Versions = file.Versions
        };
        if (file.Depots.TryGetValue("core", out var core)) catalog.CoreDepot = core;
        if (file.Depots.TryGetValue("assets", out var assets)) catalog.AssetsDepot = assets;
        if (file.Depots.TryGetValue("exe", out var exe)) catalog.ExeDepot = exe;
        return catalog;
    }

    public GameVersion? Find(string version) =>
        Versions.FirstOrDefault(v => v.Version.Equals(version, StringComparison.OrdinalIgnoreCase));

    /// <summary>Depots required for a given scope, executable depot always included.</summary>
    public string[] DepotsForScope(bool fullGame) =>
        fullGame ? new[] { CoreDepot, AssetsDepot, ExeDepot } : new[] { ExeDepot };
}
