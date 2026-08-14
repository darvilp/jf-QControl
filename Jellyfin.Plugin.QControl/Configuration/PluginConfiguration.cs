using System.Text.Json.Serialization;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Persisted QControl configuration.
/// </summary>
public sealed class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Gets or sets the persisted configuration schema version.
    /// </summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>
    /// Gets or sets the host-protected stored qBittorrent API key.
    /// </summary>
    /// <remarks>
    /// This value is XML-persisted by Jellyfin but excluded from configuration API JSON.
    /// </remarks>
    [JsonIgnore]
    public string QbittorrentApiKey { get; set; } = string.Empty;
}
