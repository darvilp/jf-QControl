using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Playback;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.Playback;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Serializes session truth, lifecycle timing, journal state, and protection actions.
/// </summary>
public sealed class ProtectionCoordinator : IProtectionCoordinator, IDisposable
{
    private readonly IPlaybackSessionSource _sessionSource;
    private readonly IActivationJournalFactory _journalFactory;
    private readonly IProtectionActionSet _actions;
    private readonly IActivationJournalStore _journalStore;
    private readonly TimeProvider _timeProvider;
    private readonly Guid _processInstanceId;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private bool _loaded;
    private bool _recoveryRequired;
    private ActivationJournalAuthority _authority;
    private ActivationJournalDocument? _journal;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProtectionCoordinator"/> class.
    /// </summary>
    /// <param name="sessionSource">The authoritative neutral playback source.</param>
    /// <param name="journalFactory">The new-activation configuration snapshot factory.</param>
    /// <param name="actions">The complete independent protection action set.</param>
    /// <param name="journalStore">The durable activation state boundary.</param>
    /// <param name="timeProvider">The explicit lifecycle clock.</param>
    /// <param name="processInstanceId">The uninterrupted process identity.</param>
    public ProtectionCoordinator(
        IPlaybackSessionSource sessionSource,
        IActivationJournalFactory journalFactory,
        IProtectionActionSet actions,
        IActivationJournalStore journalStore,
        TimeProvider timeProvider,
        Guid processInstanceId)
    {
        ArgumentNullException.ThrowIfNull(sessionSource);
        ArgumentNullException.ThrowIfNull(journalFactory);
        ArgumentNullException.ThrowIfNull(actions);
        ArgumentNullException.ThrowIfNull(journalStore);
        ArgumentNullException.ThrowIfNull(timeProvider);
        if (processInstanceId == Guid.Empty)
        {
            throw new ArgumentException("A process identity is required.", nameof(processInstanceId));
        }

        _sessionSource = sessionSource;
        _journalFactory = journalFactory;
        _actions = actions;
        _journalStore = journalStore;
        _timeProvider = timeProvider;
        _processInstanceId = processInstanceId;
    }

    /// <summary>
    /// Performs one complete serialized reconciliation from authoritative sessions.
    /// </summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The resulting privacy-safe lifecycle snapshot.</returns>
    public async Task<ProtectionCoordinatorSnapshot> ReconcileAsync(
        CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync(cancellationToken).ConfigureAwait(false);
            var sessions = await _sessionSource.ReadAsync(cancellationToken).ConfigureAwait(false);
            var presence = PlaybackPresence.Evaluate(sessions);
            var now = _timeProvider.GetUtcNow();

            if (_journal is null)
            {
                if (_recoveryRequired || !presence.IsPresent)
                {
                    return Snapshot();
                }

                var created = _journalFactory.Create(presence, _processInstanceId, now);
                if (created is null)
                {
                    return Snapshot();
                }

                await _journalStore.WriteAsync(created, cancellationToken).ConfigureAwait(false);
                _journal = created;
                _authority = ActivationJournalAuthority.Full;
            }

            if (_authority == ActivationJournalAuthority.ProtectOnly)
            {
                if (presence.IsPresent)
                {
                    await ContinueInterruptedProtectionAsync(presence, now, cancellationToken)
                        .ConfigureAwait(false);
                }

                return Snapshot();
            }

            return await ReconcileOwnedActivationAsync(presence, now, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _gate.Dispose();
        GC.SuppressFinalize(this);
    }

    private async Task EnsureLoadedAsync(CancellationToken cancellationToken)
    {
        if (_loaded)
        {
            return;
        }

        var loaded = await _journalStore
            .LoadAsync(_processInstanceId, cancellationToken)
            .ConfigureAwait(false);
        _loaded = true;
        _journal = loaded.Document;
        _authority = loaded.Authority;
        _recoveryRequired = loaded.Status is ActivationJournalLoadStatus.Interrupted
            or ActivationJournalLoadStatus.Corrupt
            or ActivationJournalLoadStatus.UnsupportedSchema;
    }

    private async Task ContinueInterruptedProtectionAsync(
        PlaybackPresenceSnapshot presence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var next = UpdateProtecting(_journal!, presence);
        await PersistTransitionIfChangedAsync(next, cancellationToken).ConfigureAwait(false);
        var outcome = await _actions
            .ReconcileProtectionAsync(_journal!, cancellationToken)
            .ConfigureAwait(false);
        await PersistOutcomeAsync(outcome, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task<ProtectionCoordinatorSnapshot> ReconcileOwnedActivationAsync(
        PlaybackPresenceSnapshot presence,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        switch (_journal!.Phase)
        {
            case ProtectionPhase.Protecting:
                if (presence.IsPresent)
                {
                    await PersistTransitionIfChangedAsync(
                            UpdateProtecting(_journal, presence),
                            cancellationToken)
                        .ConfigureAwait(false);
                    await ProtectAsync(now, cancellationToken).ConfigureAwait(false);
                    break;
                }

                var releaseDueAt = now + _journal.Configuration.ReleaseGrace;
                await PersistTransitionIfChangedAsync(
                        _journal with
                        {
                            Phase = ProtectionPhase.ReleasePending,
                            ReleaseDueAt = releaseDueAt,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                if (now < releaseDueAt)
                {
                    await ProtectAsync(now, cancellationToken).ConfigureAwait(false);
                    break;
                }

                await PersistTransitionIfChangedAsync(
                        _journal with
                        {
                            Phase = ProtectionPhase.Restoring,
                            ReleaseDueAt = null,
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
                await RestoreAsync(now, cancellationToken).ConfigureAwait(false);
                break;

            case ProtectionPhase.ReleasePending:
                if (presence.IsPresent)
                {
                    await PersistTransitionIfChangedAsync(
                            UpdateProtecting(_journal, presence),
                            cancellationToken)
                        .ConfigureAwait(false);
                    await ProtectAsync(now, cancellationToken).ConfigureAwait(false);
                }
                else if (now < _journal.ReleaseDueAt!.Value)
                {
                    await ProtectAsync(now, cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await PersistTransitionIfChangedAsync(
                            _journal with
                            {
                                Phase = ProtectionPhase.Restoring,
                                ReleaseDueAt = null,
                            },
                            cancellationToken)
                        .ConfigureAwait(false);
                    await RestoreAsync(now, cancellationToken).ConfigureAwait(false);
                }

                break;

            case ProtectionPhase.Restoring:
                await RestoreAsync(now, cancellationToken).ConfigureAwait(false);
                break;

            default:
                throw new InvalidOperationException("An active journal has an invalid lifecycle phase.");
        }

        return Snapshot();
    }

    private async Task ProtectAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var outcome = await _actions
            .ReconcileProtectionAsync(_journal!, cancellationToken)
            .ConfigureAwait(false);
        await PersistOutcomeAsync(outcome, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task RestoreAsync(DateTimeOffset now, CancellationToken cancellationToken)
    {
        var outcome = await _actions
            .ReconcileRestorationAsync(_journal!, _authority, cancellationToken)
            .ConfigureAwait(false);
        _journal = outcome.Journal;
        if (outcome.RestorationSettled && outcome.Failure is null)
        {
            await _journalStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
            _journal = null;
            _authority = ActivationJournalAuthority.None;
            return;
        }

        await PersistOutcomeAsync(outcome, now, cancellationToken).ConfigureAwait(false);
    }

    private async Task PersistOutcomeAsync(
        ProtectionActionSetResult outcome,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var next = outcome.Journal with
        {
            LastSuccessfulReconciliation = outcome.Failure is null
                ? now
                : outcome.Journal.LastSuccessfulReconciliation,
            LastFailure = outcome.Failure,
        };
        await _journalStore.WriteAsync(next, cancellationToken).ConfigureAwait(false);
        _journal = next;
    }

    private async Task PersistTransitionIfChangedAsync(
        ActivationJournalDocument next,
        CancellationToken cancellationToken)
    {
        if (_journal == next)
        {
            return;
        }

        await _journalStore.WriteAsync(next, cancellationToken).ConfigureAwait(false);
        _journal = next;
    }

    private ProtectionCoordinatorSnapshot Snapshot()
    {
        return _journal is null
            ? new ProtectionCoordinatorSnapshot(
                ProtectionPhase.Inactive,
                [],
                null,
                _recoveryRequired)
            : new ProtectionCoordinatorSnapshot(
                _journal.Phase,
                _journal.SessionIds,
                _journal.ReleaseDueAt,
                _recoveryRequired);
    }

    private static ActivationJournalDocument UpdateProtecting(
        ActivationJournalDocument journal,
        PlaybackPresenceSnapshot presence)
    {
        var sessionIds = journal.SessionIds
            .Concat(presence.SessionIds)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToImmutableArray();
        if (journal.Phase == ProtectionPhase.Protecting
            && journal.ReleaseDueAt is null
            && journal.SessionIds.SequenceEqual(sessionIds))
        {
            return journal;
        }

        return journal with
        {
            Phase = ProtectionPhase.Protecting,
            ReleaseDueAt = null,
            SessionIds = sessionIds,
        };
    }
}
