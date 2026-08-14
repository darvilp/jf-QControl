using System.Diagnostics.CodeAnalysis;
using Jellyfin.Plugin.QControl.Domain.Torrents;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Complete administrator-supplied candidate with write-only credential controls.
/// </summary>
public sealed class ConfigurationCandidate
{
    /// <summary>Gets or sets the revision the administrator edited.</summary>
    public long ExpectedRevision { get; set; }

    /// <summary>Gets or sets the qBittorrent Web UI base URL.</summary>
    public string QbittorrentBaseAddress { get; set; } = string.Empty;

    /// <summary>Gets or sets the selected authentication mode.</summary>
    public QbittorrentCredentialMode CredentialMode { get; set; }

    /// <summary>Gets or sets the secret-file path.</summary>
    public string SecretFilePath { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a replacement stored key. Null, empty, or whitespace retains the current key.
    /// </summary>
    public string? ApiKeyReplacement { get; set; }

    /// <summary>Gets or sets a value indicating whether the stored key is explicitly cleared.</summary>
    public bool ClearStoredApiKey { get; set; }

    /// <summary>Gets or sets a value indicating whether Alternative Limits is enabled.</summary>
    public bool AlternativeLimitsEnabled { get; set; }

    /// <summary>Gets or sets a value indicating whether Stop Torrents is enabled.</summary>
    public bool StopTorrentsEnabled { get; set; }

    /// <summary>Gets or sets the category scope.</summary>
    public TorrentScope StopScope { get; set; }

    /// <summary>Gets or sets selected exact category names.</summary>
    [SuppressMessage(
        "Performance",
        "CA1819:Properties should not return arrays",
        Justification = "ASP.NET request binding requires a simple settable DTO.")]
    public string[] SelectedCategories { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether incomplete torrents qualify.</summary>
    public bool IncludeIncomplete { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether completed torrents qualify.</summary>
    public bool IncludeCompleted { get; set; } = true;

    /// <summary>Gets or sets the restart marker tag.</summary>
    public string MarkerTag { get; set; } = "jfStopped";

    /// <summary>Gets or sets the dominant exclusion tag.</summary>
    public string NeverTouchTag { get; set; } = "jfNeverTouch";

    /// <summary>Gets or sets release grace in seconds.</summary>
    public int ReleaseGraceSeconds { get; set; } = 60;

    /// <inheritdoc />
    public override string ToString() => nameof(ConfigurationCandidate);
}
