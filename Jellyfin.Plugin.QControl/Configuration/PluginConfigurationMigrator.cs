using System;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Normalizes the pre-alpha schema into the complete inert schema-1 configuration.
/// </summary>
public static class PluginConfigurationMigrator
{
    /// <summary>Normalizes supported configuration and reports whether persistence is needed.</summary>
    /// <param name="configuration">The deserialized Jellyfin configuration.</param>
    /// <param name="changed">Whether the returned value should replace the current value.</param>
    /// <returns>The normalized configuration.</returns>
    public static PluginConfiguration Normalize(
        PluginConfiguration configuration,
        out bool changed)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        changed = configuration.SchemaVersion == 0 || configuration.SelectedCategories is null;
        if (!changed)
        {
            return configuration;
        }

        return new PluginConfiguration
        {
            SchemaVersion = 1,
            Revision = Math.Max(0, configuration.Revision),
            QbittorrentBaseAddress = configuration.QbittorrentBaseAddress ?? string.Empty,
            CredentialMode = Enum.IsDefined(configuration.CredentialMode)
                ? configuration.CredentialMode
                : QbittorrentCredentialMode.StoredApiKey,
            QbittorrentApiKey = configuration.QbittorrentApiKey ?? string.Empty,
            SecretFilePath = configuration.SecretFilePath ?? string.Empty,
            ConnectionValidated = false,
            AlternativeLimitsEnabled = false,
            StopTorrentsEnabled = false,
            StopScope = Domain.Torrents.TorrentScope.All,
            SelectedCategories = configuration.SelectedCategories ?? [],
            IncludeIncomplete = true,
            IncludeCompleted = true,
            MarkerTag = string.IsNullOrWhiteSpace(configuration.MarkerTag)
                ? "jfStopped"
                : configuration.MarkerTag,
            NeverTouchTag = string.IsNullOrWhiteSpace(configuration.NeverTouchTag)
                ? "jfNeverTouch"
                : configuration.NeverTouchTag,
            ReleaseGraceSeconds = configuration.ReleaseGraceSeconds is >= 0 and <= 86400
                ? configuration.ReleaseGraceSeconds
                : 60,
        };
    }
}
