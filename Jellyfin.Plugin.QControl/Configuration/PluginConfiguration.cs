using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Jellyfin.Plugin.QControl.Domain.Torrents;
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
    /// Gets or sets the monotonically increasing accepted configuration revision.
    /// </summary>
    public long Revision { get; set; }

    /// <summary>
    /// Gets or sets the qBittorrent Web UI base URL.
    /// </summary>
    public string QbittorrentBaseAddress { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the active credential source.
    /// </summary>
    public QbittorrentCredentialMode CredentialMode { get; set; } =
        QbittorrentCredentialMode.StoredApiKey;

    /// <summary>
    /// Gets or sets the host-protected stored qBittorrent API key.
    /// </summary>
    /// <remarks>
    /// This value is XML-persisted by Jellyfin but excluded from configuration API JSON.
    /// </remarks>
    [JsonIgnore]
    public string QbittorrentApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the platform-native secret-file path.
    /// </summary>
    public string SecretFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether the current connection was successfully probed.
    /// </summary>
    public bool ConnectionValidated { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Alternative Limits protection is enabled.
    /// </summary>
    public bool AlternativeLimitsEnabled { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether Stop Torrents protection is enabled.
    /// </summary>
    public bool StopTorrentsEnabled { get; set; }

    /// <summary>
    /// Gets or sets the torrent category scope.
    /// </summary>
    public TorrentScope StopScope { get; set; } = TorrentScope.All;

    /// <summary>
    /// Gets or sets exact selected qBittorrent category names.
    /// </summary>
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "Jellyfin plugin configuration requires a simple settable serializer DTO.")]
    public string[] SelectedCategories { get; set; } = [];

    /// <summary>
    /// Gets or sets a value indicating whether incomplete torrents qualify.
    /// </summary>
    public bool IncludeIncomplete { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether completed torrents qualify.
    /// </summary>
    public bool IncludeCompleted { get; set; } = true;

    /// <summary>
    /// Gets or sets the restart-intent marker tag.
    /// </summary>
    public string MarkerTag { get; set; } = "jfStopped";

    /// <summary>
    /// Gets or sets the dominant exclusion tag.
    /// </summary>
    public string NeverTouchTag { get; set; } = "jfNeverTouch";

    /// <summary>
    /// Gets or sets the complete-absence release grace in seconds.
    /// </summary>
    public int ReleaseGraceSeconds { get; set; } = 60;
}
