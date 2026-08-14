using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Domain.Actions;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.QBittorrent;

namespace Jellyfin.Plugin.QControl.Actions;

/// <summary>
/// Serializes write-ahead, read-back-confirmed Alternative Limits reconciliation.
/// </summary>
public sealed class AlternativeLimitsActionService : IAlternativeLimitsActionService, IDisposable
{
    private readonly IQbittorrentClient _qbit;
    private readonly IActivationJournalStore _journalStore;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Initializes a new instance of the <see cref="AlternativeLimitsActionService"/> class.
    /// </summary>
    /// <param name="qbit">The allowlisted qBittorrent boundary.</param>
    /// <param name="journalStore">The durable activation journal boundary.</param>
    public AlternativeLimitsActionService(
        IQbittorrentClient qbit,
        IActivationJournalStore journalStore)
    {
        ArgumentNullException.ThrowIfNull(qbit);
        ArgumentNullException.ThrowIfNull(journalStore);
        _qbit = qbit;
        _journalStore = journalStore;
    }

    /// <inheritdoc />
    public async Task<ActivationJournalDocument> ReconcileProtectionAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!journal.Configuration.AlternativeLimitsEnabled)
        {
            return journal;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReconcileProtectionCoreAsync(journal, cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <inheritdoc />
    public async Task<ActivationJournalDocument> ReconcileRestorationAsync(
        ActivationJournalDocument journal,
        ActivationJournalAuthority authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);
        if (!journal.Configuration.AlternativeLimitsEnabled)
        {
            return journal;
        }

        if (authority != ActivationJournalAuthority.Full)
        {
            throw new InvalidOperationException(
                "Full journal authority is required for automatic restoration.");
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ReconcileRestorationCoreAsync(journal, cancellationToken)
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

    private async Task<ActivationJournalDocument> ReconcileProtectionCoreAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken)
    {
        var currentlyEnabled = await _qbit
            .GetAlternativeLimitsEnabledAsync(cancellationToken)
            .ConfigureAwait(false);
        var state = journal.AlternativeLimits;

        if (state.EnableStage == JournalMutationStage.IntentPersisted && currentlyEnabled)
        {
            var confirmed = state with
            {
                EnabledByActivation = state.EnabledByActivation || state.InitialEnabled == false,
                EnableStage = JournalMutationStage.Confirmed,
            };
            journal = await PersistStateIfChangedAsync(journal, confirmed, cancellationToken)
                .ConfigureAwait(false);
            state = journal.AlternativeLimits;
        }

        var ownership = new AlternativeLimitsOwnership(
            state.InitialEnabled.HasValue,
            state.EnabledByActivation);
        var plan = AlternativeLimitsPlanner.PlanProtection(
            actionEnabled: true,
            currentlyEnabled,
            ownership);

        if (!state.InitialEnabled.HasValue)
        {
            state = state with
            {
                InitialEnabled = currentlyEnabled,
                EnabledByActivation = false,
                EnableStage = plan.Mutation == AlternativeLimitsMutation.Enable
                    ? JournalMutationStage.IntentPersisted
                    : JournalMutationStage.None,
            };
            journal = await PersistStateIfChangedAsync(journal, state, cancellationToken)
                .ConfigureAwait(false);
        }
        else if (plan.Mutation == AlternativeLimitsMutation.Enable)
        {
            state = state with { EnableStage = JournalMutationStage.IntentPersisted };
            journal = await PersistStateIfChangedAsync(journal, state, cancellationToken)
                .ConfigureAwait(false);
        }

        if (plan.Mutation != AlternativeLimitsMutation.Enable)
        {
            return journal;
        }

        await _qbit.SetAlternativeLimitsEnabledAsync(true, cancellationToken)
            .ConfigureAwait(false);
        currentlyEnabled = await _qbit
            .GetAlternativeLimitsEnabledAsync(cancellationToken)
            .ConfigureAwait(false);
        if (!currentlyEnabled)
        {
            return journal;
        }

        state = journal.AlternativeLimits with
        {
            EnabledByActivation = journal.AlternativeLimits.EnabledByActivation
                || journal.AlternativeLimits.InitialEnabled == false,
            EnableStage = JournalMutationStage.Confirmed,
        };
        return await PersistStateIfChangedAsync(journal, state, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ActivationJournalDocument> ReconcileRestorationCoreAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken)
    {
        var currentlyEnabled = await _qbit
            .GetAlternativeLimitsEnabledAsync(cancellationToken)
            .ConfigureAwait(false);
        var state = journal.AlternativeLimits;

        if (state.EnableStage == JournalMutationStage.IntentPersisted && currentlyEnabled)
        {
            state = state with
            {
                EnabledByActivation = state.EnabledByActivation || state.InitialEnabled == false,
                EnableStage = JournalMutationStage.Confirmed,
            };
            journal = await PersistStateIfChangedAsync(journal, state, cancellationToken)
                .ConfigureAwait(false);
        }

        if (state.DisableStage == JournalMutationStage.IntentPersisted && !currentlyEnabled)
        {
            state = state with { DisableStage = JournalMutationStage.Confirmed };
            return await PersistStateIfChangedAsync(journal, state, cancellationToken)
                .ConfigureAwait(false);
        }

        var ownership = new AlternativeLimitsOwnership(
            state.InitialEnabled.HasValue,
            state.EnabledByActivation);
        var mutation = AlternativeLimitsPlanner.PlanRestoration(
            actionEnabled: true,
            currentlyEnabled,
            ownership);
        if (mutation != AlternativeLimitsMutation.Disable)
        {
            if (state.EnabledByActivation
                && !currentlyEnabled
                && state.DisableStage != JournalMutationStage.Confirmed)
            {
                state = state with { DisableStage = JournalMutationStage.Confirmed };
                return await PersistStateIfChangedAsync(journal, state, cancellationToken)
                    .ConfigureAwait(false);
            }

            return journal;
        }

        state = state with { DisableStage = JournalMutationStage.IntentPersisted };
        journal = await PersistStateIfChangedAsync(journal, state, cancellationToken)
            .ConfigureAwait(false);
        await _qbit.SetAlternativeLimitsEnabledAsync(false, cancellationToken)
            .ConfigureAwait(false);
        currentlyEnabled = await _qbit
            .GetAlternativeLimitsEnabledAsync(cancellationToken)
            .ConfigureAwait(false);
        if (currentlyEnabled)
        {
            return journal;
        }

        state = journal.AlternativeLimits with
        {
            DisableStage = JournalMutationStage.Confirmed,
        };
        return await PersistStateIfChangedAsync(journal, state, cancellationToken)
            .ConfigureAwait(false);
    }

    private async Task<ActivationJournalDocument> PersistStateIfChangedAsync(
        ActivationJournalDocument journal,
        AlternativeLimitsJournalState state,
        CancellationToken cancellationToken)
    {
        if (journal.AlternativeLimits == state)
        {
            return journal;
        }

        var next = journal with { AlternativeLimits = state };
        await _journalStore.WriteAsync(next, cancellationToken).ConfigureAwait(false);
        return next;
    }
}
