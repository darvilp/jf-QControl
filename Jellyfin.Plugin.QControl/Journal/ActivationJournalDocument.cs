using System;
using System.Collections.Immutable;
using Jellyfin.Plugin.QControl.Domain.Activation;

namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Versioned immutable durable state for one active QControl activation.
/// </summary>
/// <param name="SchemaVersion">The journal schema version.</param>
/// <param name="ProcessInstanceId">The uninterrupted process owner.</param>
/// <param name="ActivationId">The activation identity.</param>
/// <param name="StartedAt">The activation start instant.</param>
/// <param name="SessionIds">Participating Jellyfin session identifiers.</param>
/// <param name="Configuration">The complete behavior snapshot.</param>
/// <param name="Endpoint">The credential-free qBittorrent endpoint identity.</param>
/// <param name="AlternativeLimits">Alternative Limits ownership and progress.</param>
/// <param name="Torrents">Per-hash torrent progress.</param>
/// <param name="Phase">The active lifecycle phase.</param>
/// <param name="ReleaseDueAt">The pending-release deadline, if any.</param>
/// <param name="LastSuccessfulReconciliation">The last successful reconciliation, if any.</param>
/// <param name="LastFailure">The most recent bounded failure category, if any.</param>
public sealed record ActivationJournalDocument(
    int SchemaVersion,
    Guid ProcessInstanceId,
    Guid ActivationId,
    DateTimeOffset StartedAt,
    ImmutableArray<string> SessionIds,
    JournalConfigurationSnapshot Configuration,
    QbittorrentEndpointIdentity Endpoint,
    AlternativeLimitsJournalState AlternativeLimits,
    ImmutableArray<TorrentMutationJournalEntry> Torrents,
    ProtectionPhase Phase,
    DateTimeOffset? ReleaseDueAt,
    DateTimeOffset? LastSuccessfulReconciliation,
    JournalFailureCode? LastFailure);
