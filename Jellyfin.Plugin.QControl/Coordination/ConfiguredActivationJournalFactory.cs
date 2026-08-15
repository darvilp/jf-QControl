using System;
using System.Collections.Immutable;
using System.Linq;
using Jellyfin.Plugin.QControl.Configuration;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Captures one complete validated behavior and credential-free endpoint snapshot.
/// </summary>
public sealed class ConfiguredActivationJournalFactory : IActivationJournalFactory
{
    private readonly IPluginConfigurationPersistence _configuration;

    /// <summary>
    /// Initializes a new instance of the <see cref="ConfiguredActivationJournalFactory"/> class.
    /// </summary>
    /// <param name="configuration">The current accepted configuration boundary.</param>
    public ConfiguredActivationJournalFactory(IPluginConfigurationPersistence configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        _configuration = configuration;
    }

    /// <inheritdoc />
    public ActivationJournalDocument? Create(
        PlaybackPresenceSnapshot presence,
        Guid processInstanceId,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(presence);
        var configuration = _configuration.Current;
        if (configuration.SchemaVersion != 1
            || !configuration.ConnectionValidated
            || (!configuration.AlternativeLimitsEnabled
                && !configuration.StopTorrentsEnabled))
        {
            return null;
        }

        var endpoint = new Uri(configuration.QbittorrentBaseAddress, UriKind.Absolute);
        return new ActivationJournalDocument(
            SchemaVersion: 1,
            ProcessInstanceId: processInstanceId,
            ActivationId: Guid.NewGuid(),
            StartedAt: now,
            SessionIds: presence.SessionIds.ToImmutableArray(),
            Configuration: new JournalConfigurationSnapshot(
                configuration.Revision,
                configuration.AlternativeLimitsEnabled,
                configuration.StopTorrentsEnabled,
                configuration.StopScope,
                (configuration.SelectedCategories ?? [])
                    .Order(StringComparer.Ordinal)
                    .ToImmutableArray(),
                configuration.IncludeIncomplete,
                configuration.IncludeCompleted,
                configuration.MarkerTag,
                (configuration.ExclusionTags ?? [])
                    .Order(StringComparer.Ordinal)
                    .ToImmutableArray(),
                TimeSpan.FromSeconds(configuration.ReleaseGraceSeconds)),
            Endpoint: new QbittorrentEndpointIdentity(
                endpoint.Scheme,
                endpoint.Host,
                endpoint.Port,
                endpoint.AbsolutePath),
            AlternativeLimits: new AlternativeLimitsJournalState(
                InitialEnabled: null,
                EnabledByActivation: false,
                EnableStage: JournalMutationStage.None,
                DisableStage: JournalMutationStage.None),
            Torrents: [],
            Phase: ProtectionPhase.Protecting,
            ReleaseDueAt: null,
            LastSuccessfulReconciliation: null,
            LastFailure: null);
    }
}
