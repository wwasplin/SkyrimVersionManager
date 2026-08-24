using System.Text.Json.Serialization;

namespace SkyrimVersionManager.Models;

public class AppSettings
{
    [JsonPropertyName("gamePathOverride")]
    public string? GamePathOverride { get; set; }

    [JsonPropertyName("desiredVersion")]
    public string? DesiredVersion { get; set; }

    [JsonPropertyName("fullGameScope")]
    public bool FullGameScope { get; set; } = false;

    [JsonPropertyName("lockUpdates")]
    public bool LockUpdates { get; set; } = true;

    [JsonPropertyName("backupBeforeSwitch")]
    public bool BackupBeforeSwitch { get; set; } = true;

    [JsonPropertyName("skseCheck")]
    public bool SkseCheck { get; set; } = true;

    [JsonPropertyName("manageSaves")]
    public bool ManageSaves { get; set; } = true;

    [JsonPropertyName("steamUsername")]
    public string? SteamUsername { get; set; }

    /// <summary>"console" (drive the logged-in desktop Steam client) or "account" (DepotDownloader login).</summary>
    [JsonPropertyName("authMethod")]
    public string AuthMethod { get; set; } = "console";
}
