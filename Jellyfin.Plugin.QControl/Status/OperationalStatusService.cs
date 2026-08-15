using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Configuration;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.Playback;
using Jellyfin.Plugin.QControl.QBittorrent;

namespace Jellyfin.Plugin.QControl.Status;

/// <summary>
/// Builds current status from neutral sessions, durable state, and read-only qBittorrent calls.
/// </summary>
public sealed class OperationalStatusService
{
    private readonly IPlaybackSessionSource _sessions;
    private readonly IActivationJournalStore _journalStore;
    private readonly ProcessInstanceIdentity _processIdentity;
    private readonly IPluginConfigurationPersistence _configuration;
    private readonly IQbittorrentClientFactory _clientFactory;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="OperationalStatusService"/> class.</summary>
    /// <param name="sessions">The neutral authoritative Jellyfin sessions.</param>
    /// <param name="journalStore">The durable activation boundary.</param>
    /// <param name="processIdentity">The uninterrupted process identity.</param>
    /// <param name="configuration">The current saved configuration.</param>
    /// <param name="clientFactory">The read-only qBittorrent client factory.</param>
    /// <param name="timeProvider">The countdown clock.</param>
    public OperationalStatusService(
        IPlaybackSessionSource sessions,
        IActivationJournalStore journalStore,
        ProcessInstanceIdentity processIdentity,
        IPluginConfigurationPersistence configuration,
        IQbittorrentClientFactory clientFactory,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(journalStore);
        ArgumentNullException.ThrowIfNull(processIdentity);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _sessions = sessions;
        _journalStore = journalStore;
        _processIdentity = processIdentity;
        _configuration = configuration;
        _clientFactory = clientFactory;
        _timeProvider = timeProvider;
    }

    /// <summary>Reads one complete privacy-safe operational status snapshot.</summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The current status.</returns>
    public async Task<OperationalStatusSnapshot> GetAsync(CancellationToken cancellationToken)
    {
        var sessionSnapshots = await _sessions.ReadAsync(cancellationToken).ConfigureAwait(false);
        var presence = PlaybackPresence.Evaluate(sessionSnapshots);
        var loaded = await _journalStore
            .LoadAsync(_processIdentity.Value, cancellationToken)
            .ConfigureAwait(false);
        var journal = loaded.Document;
        var configuration = _configuration.Current;
        var recoveryRequired = loaded.Status is ActivationJournalLoadStatus.Interrupted
            or ActivationJournalLoadStatus.Corrupt
            or ActivationJournalLoadStatus.UnsupportedSchema;
        var actionConfiguration = journal?.Configuration;
        var alternativeLimitsActionEnabled = actionConfiguration?.AlternativeLimitsEnabled
            ?? configuration.AlternativeLimitsEnabled;
        var stopTorrentsActionEnabled = actionConfiguration?.StopTorrentsEnabled
            ?? configuration.StopTorrentsEnabled;
        var markerTag = actionConfiguration?.MarkerTag ?? configuration.MarkerTag;
        IEnumerable<string> configuredExclusionTags = actionConfiguration is null
                ? configuration.ExclusionTags
                : actionConfiguration.ExclusionTags;
        var exclusionTags = configuredExclusionTags.ToHashSet(StringComparer.Ordinal);

        var connectivity = QbittorrentConnectivity.Unconfigured;
        string? applicationVersion = null;
        string? webApiVersion = null;
        bool? alternativeLimitsCurrentlyEnabled = null;
        int? eligibleCount = null;
        int? markedCount = null;
        int? resumableMarkedCount = null;
        int? stoppedMarkedCount = null;
        int? excludedCount = null;
        var currentError = journal?.LastFailure;

        if (journal is not null
            || (configuration.ConnectionValidated
                && !string.IsNullOrWhiteSpace(configuration.QbittorrentBaseAddress)))
        {
            try
            {
                var client = journal is null
                    ? _clientFactory.Create(configuration)
                    : _clientFactory.Create(journal.Endpoint);
                var server = await client.GetServerInfoAsync(cancellationToken).ConfigureAwait(false);
                var torrents = await client.GetTorrentsAsync(cancellationToken).ConfigureAwait(false);
                alternativeLimitsCurrentlyEnabled = await client
                    .GetAlternativeLimitsEnabledAsync(cancellationToken)
                    .ConfigureAwait(false);
                connectivity = QbittorrentConnectivity.Connected;
                applicationVersion = server.ApplicationVersion.ToString();
                webApiVersion = server.WebApiVersion.ToString();
                markedCount = torrents.Count(torrent => torrent.Tags.Contains(markerTag));
                resumableMarkedCount = torrents.Count(torrent =>
                    torrent.Tags.Contains(markerTag)
                    && !torrent.Tags.Any(exclusionTags.Contains));
                stoppedMarkedCount = torrents.Count(torrent =>
                    torrent.IsStopped && torrent.Tags.Contains(markerTag));
                excludedCount = torrents.Count(torrent => torrent.Tags.Any(exclusionTags.Contains));
                eligibleCount = stopTorrentsActionEnabled
                    ? TorrentSelector.SelectForAcquisition(
                        torrents,
                        CreatePolicy(journal, configuration)).Count
                    : 0;
            }
            catch (QbittorrentClientException exception)
            {
                connectivity = QbittorrentConnectivity.Failed;
                currentError = QbittorrentFailureMapper.Map(exception.Error);
            }
        }

        var releaseSeconds = journal?.ReleaseDueAt is null
            ? (int?)null
            : (int)Math.Ceiling(
                (journal.ReleaseDueAt.Value - _timeProvider.GetUtcNow()).TotalSeconds);
        var releaseRemaining = releaseSeconds.HasValue
            ? Math.Max(0, releaseSeconds.Value)
            : (int?)null;
        var canResume = resumableMarkedCount.GetValueOrDefault() > 0
            || (recoveryRequired
                && stopTorrentsActionEnabled
                && connectivity != QbittorrentConnectivity.Connected);
        return new OperationalStatusSnapshot(
            connectivity,
            applicationVersion,
            webApiVersion,
            ToOperationalState(journal?.Phase ?? ProtectionPhase.Inactive, recoveryRequired),
            presence.SessionIds.Count,
            alternativeLimitsActionEnabled,
            stopTorrentsActionEnabled,
            alternativeLimitsCurrentlyEnabled,
            journal?.AlternativeLimits.EnabledByActivation ?? false,
            eligibleCount,
            markedCount,
            stoppedMarkedCount,
            excludedCount,
            releaseRemaining,
            journal is not null && journal.Configuration.Revision != configuration.Revision,
            configuration.Revision,
            journal?.Configuration.Revision,
            journal?.LastSuccessfulReconciliation,
            currentError,
            canResume,
            recoveryRequired && journal?.AlternativeLimits.InitialEnabled is not null,
            recoveryRequired);
    }

    private static TorrentSelectionPolicy CreatePolicy(
        ActivationJournalDocument? journal,
        PluginConfiguration configuration)
    {
        var active = journal?.Configuration;
        IEnumerable<string> categories = active is null
            ? configuration.SelectedCategories
            : active.SelectedCategories;
        return new TorrentSelectionPolicy(
            active?.StopScope ?? configuration.StopScope,
            categories,
            active?.IncludeIncomplete ?? configuration.IncludeIncomplete,
            active?.IncludeCompleted ?? configuration.IncludeCompleted,
            active?.MarkerTag ?? configuration.MarkerTag,
            active is null ? configuration.ExclusionTags : active.ExclusionTags);
    }

    private static OperationalProtectionState ToOperationalState(
        ProtectionPhase phase,
        bool recoveryRequired)
    {
        if (recoveryRequired)
        {
            return OperationalProtectionState.RecoveryRequired;
        }

        return phase switch
        {
            ProtectionPhase.Inactive => OperationalProtectionState.Inactive,
            ProtectionPhase.Protecting => OperationalProtectionState.Protecting,
            ProtectionPhase.ReleasePending => OperationalProtectionState.ReleasePending,
            ProtectionPhase.Restoring => OperationalProtectionState.Restoring,
            _ => throw new ArgumentOutOfRangeException(nameof(phase), phase, "Unknown protection phase."),
        };
    }
}
