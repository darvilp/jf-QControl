using Jellyfin.Plugin.QControl.Configuration;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Configuration;

public sealed class PluginConfigurationMigrationTests
{
    [Fact]
    public void PreAlphaConfigurationMigratesInertAndRetainsStoredCredential()
    {
        const string apiKey = "qbt_1234567890123456789012345678";
        var legacy = new PluginConfiguration
        {
            SchemaVersion = 0,
            QbittorrentApiKey = apiKey,
            ConnectionValidated = true,
            AlternativeLimitsEnabled = true,
            StopTorrentsEnabled = true,
            MarkerTag = string.Empty,
            ExclusionTags = null!,
        };

        var migrated = PluginConfigurationMigrator.Normalize(legacy, out var changed);

        Assert.True(changed);
        Assert.Equal(1, migrated.SchemaVersion);
        Assert.Equal(apiKey, migrated.QbittorrentApiKey);
        Assert.False(migrated.ConnectionValidated);
        Assert.False(migrated.AlternativeLimitsEnabled);
        Assert.False(migrated.StopTorrentsEnabled);
        Assert.Equal("qcontrol-resume", migrated.MarkerTag);
        Assert.Equal(["qcontrol-ignore"], migrated.ExclusionTags);
        Assert.Equal(60, migrated.ReleaseGraceSeconds);
    }

    [Fact]
    public void CurrentSchemaNeedsNoMigration()
    {
        var current = new PluginConfiguration();

        var normalized = PluginConfigurationMigrator.Normalize(current, out var changed);

        Assert.False(changed);
        Assert.Same(current, normalized);
    }
}
