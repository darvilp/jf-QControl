using System;
using System.Linq;
using Jellyfin.Plugin.QControl.Configuration;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Configuration;

public sealed class ConfiguredActivationJournalFactoryTests
{
    [Fact]
    public void InertOrUnvalidatedConfigurationCannotCreateActivation()
    {
        var configuration = ValidConfiguration();
        configuration.AlternativeLimitsEnabled = false;
        configuration.StopTorrentsEnabled = false;
        var factory = new ConfiguredActivationJournalFactory(
            new StaticPersistence(configuration));
        var presence = new PlaybackPresenceSnapshot(true, ["session-a"]);

        Assert.Null(factory.Create(presence, Guid.NewGuid(), DateTimeOffset.UtcNow));

        configuration.AlternativeLimitsEnabled = true;
        configuration.ConnectionValidated = false;
        Assert.Null(factory.Create(presence, Guid.NewGuid(), DateTimeOffset.UtcNow));
    }

    [Fact]
    public void ActivationCapturesCompleteBehaviorAndEndpointSnapshot()
    {
        var configuration = ValidConfiguration();
        var factory = new ConfiguredActivationJournalFactory(
            new StaticPersistence(configuration));
        var process = Guid.NewGuid();
        var now = new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero);

        var journal = Assert.IsType<Jellyfin.Plugin.QControl.Journal.ActivationJournalDocument>(
            factory.Create(
                new PlaybackPresenceSnapshot(true, ["session-b", "session-a"]),
                process,
                now));
        configuration.MarkerTag = "changed-after-capture";
        configuration.SelectedCategories[0] = "changed-category";

        Assert.Equal(process, journal.ProcessInstanceId);
        Assert.Equal(["session-b", "session-a"], journal.SessionIds.ToArray());
        Assert.Equal(7, journal.Configuration.Revision);
        Assert.True(journal.Configuration.AlternativeLimitsEnabled);
        Assert.True(journal.Configuration.StopTorrentsEnabled);
        Assert.Equal(TorrentScope.SelectedCategories, journal.Configuration.StopScope);
        Assert.Equal(["radarr", "sonarr"], journal.Configuration.SelectedCategories.ToArray());
        Assert.Equal("jfStopped", journal.Configuration.MarkerTag);
        Assert.Equal(TimeSpan.FromSeconds(75), journal.Configuration.ReleaseGrace);
        Assert.Equal("https", journal.Endpoint.Scheme);
        Assert.Equal("qbit.internal", journal.Endpoint.Host);
        Assert.Equal(8443, journal.Endpoint.Port);
        Assert.Equal("/webui", journal.Endpoint.BasePath);
    }

    private static PluginConfiguration ValidConfiguration()
    {
        return new PluginConfiguration
        {
            Revision = 7,
            QbittorrentBaseAddress = "https://qbit.internal:8443/webui",
            CredentialMode = QbittorrentCredentialMode.StoredApiKey,
            QbittorrentApiKey = "qbt_1234567890123456789012345678",
            ConnectionValidated = true,
            AlternativeLimitsEnabled = true,
            StopTorrentsEnabled = true,
            StopScope = TorrentScope.SelectedCategories,
            SelectedCategories = ["radarr", "sonarr"],
            IncludeIncomplete = true,
            IncludeCompleted = false,
            MarkerTag = "jfStopped",
            NeverTouchTag = "jfNeverTouch",
            ReleaseGraceSeconds = 75,
        };
    }

    private sealed class StaticPersistence(PluginConfiguration configuration)
        : IPluginConfigurationPersistence
    {
        public PluginConfiguration Current => configuration;

        public void Save(PluginConfiguration next) => throw new NotSupportedException();
    }
}
