using System.Text.Json.Serialization;

namespace SkyrimVersionManager.Models;

public class GameVersion
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("displayVersion")]
    public string DisplayVersion { get; set; } = "";

    [JsonPropertyName("releaseDate")]
    public string ReleaseDate { get; set; } = "";

    [JsonPropertyName("expectedSkse")]
    public string ExpectedSkse { get; set; } = "";

    [JsonPropertyName("isLatest")]
    public bool IsLatest { get; set; }

    /// <summary>Depot id -> manifest id. Null for the "latest" entry (download whatever Steam serves).</summary>
    [JsonPropertyName("manifests")]
    public Dictionary<string, string>? Manifests { get; set; }

    [JsonPropertyName("notes")]
    public string Notes { get; set; } = "";

    public override string ToString() => string.IsNullOrEmpty(DisplayVersion) ? Version : DisplayVersion;
}

public class VersionCatalogFile
{
    [JsonPropertyName("appId")]
    public string AppId { get; set; } = "489830";

    [JsonPropertyName("depots")]
    public Dictionary<string, string> Depots { get; set; } = new();

    [JsonPropertyName("versions")]
    public List<GameVersion> Versions { get; set; } = new();
}
