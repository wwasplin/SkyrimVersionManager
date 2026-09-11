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

    /// <summary>
    /// Depot id -> manifest id. May be null or partial; the "latest" entry falls back to whatever
    /// Steam serves for any depot without a pinned manifest.
    /// </summary>
    [JsonPropertyName("manifests")]
    public Dictionary<string, string>? Manifests { get; set; }

    /// <summary>Pinned manifest for a depot, or null when none is known.</summary>
    public string? ManifestFor(string depot) =>
        Manifests != null && Manifests.TryGetValue(depot, out var manifest) ? manifest : null;

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
