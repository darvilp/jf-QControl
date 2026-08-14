using System.Collections.Generic;
using Jellyfin.Plugin.QControl.Domain.Torrents;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Administrator-readable configuration with credential presence, never content.
/// </summary>
/// <param name="Revision">The current accepted revision.</param>
/// <param name="QbittorrentBaseAddress">The qBittorrent Web UI base URL.</param>
/// <param name="CredentialMode">The active credential source.</param>
/// <param name="HasStoredApiKey">Whether a stored key exists.</param>
/// <param name="SecretFilePath">The configured platform-native secret path.</param>
/// <param name="ConnectionValidated">Whether this connection was successfully probed.</param>
/// <param name="AlternativeLimitsEnabled">Whether Alternative Limits is enabled.</param>
/// <param name="StopTorrentsEnabled">Whether Stop Torrents is enabled.</param>
/// <param name="StopScope">The category scope.</param>
/// <param name="SelectedCategories">Exact selected category names.</param>
/// <param name="IncludeIncomplete">Whether incomplete torrents qualify.</param>
/// <param name="IncludeCompleted">Whether completed torrents qualify.</param>
/// <param name="MarkerTag">The restart marker tag.</param>
/// <param name="NeverTouchTag">The exclusion tag.</param>
/// <param name="ReleaseGraceSeconds">The release grace.</param>
public sealed record ConfigurationView(
    long Revision,
    string QbittorrentBaseAddress,
    QbittorrentCredentialMode CredentialMode,
    bool HasStoredApiKey,
    string SecretFilePath,
    bool ConnectionValidated,
    bool AlternativeLimitsEnabled,
    bool StopTorrentsEnabled,
    TorrentScope StopScope,
    IReadOnlyList<string> SelectedCategories,
    bool IncludeIncomplete,
    bool IncludeCompleted,
    string MarkerTag,
    string NeverTouchTag,
    int ReleaseGraceSeconds);
