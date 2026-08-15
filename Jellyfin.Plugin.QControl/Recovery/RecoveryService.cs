using System;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Actions;
using Jellyfin.Plugin.QControl.Configuration;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Domain.Activation;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.Playback;
using Jellyfin.Plugin.QControl.QBittorrent;

namespace Jellyfin.Plugin.QControl.Recovery;

/// <summary>
/// Executes explicit recovery under the same process-wide mutation gate as automatic reconciliation.
/// </summary>
public sealed class RecoveryService
{
    private const int MaxImmediateTorrentRestorationPasses = 20;
    private static readonly TimeSpan TorrentRestorationReadbackDelay =
        TimeSpan.FromMilliseconds(250);

    private readonly IActivationJournalStore _journalStore;
    private readonly ProcessInstanceIdentity _processIdentity;
    private readonly IPluginConfigurationPersistence _configuration;
    private readonly IQbittorrentClientFactory _clientFactory;
    private readonly IProtectionExecutionGate _executionGate;
    private readonly IProtectionCoordinatorStateControl _coordinatorState;
    private readonly IProtectionWakeSignal _wakeSignal;
    private readonly IReconciliationDelay _delay;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes a new instance of the <see cref="RecoveryService"/> class.</summary>
    /// <param name="journalStore">The durable recovery state.</param>
    /// <param name="processIdentity">The current process identity.</param>
    /// <param name="configuration">The current connection and fallback tag configuration.</param>
    /// <param name="clientFactory">The activation-pinned qBittorrent client factory.</param>
    /// <param name="executionGate">The shared automatic/manual mutation gate.</param>
    /// <param name="coordinatorState">The coordinator journal-cache control.</param>
    /// <param name="wakeSignal">The post-recovery worker wake signal.</param>
    /// <param name="delay">The bounded accepted-start read-back delay.</param>
    /// <param name="timeProvider">The manual recovery journal clock.</param>
    public RecoveryService(
        IActivationJournalStore journalStore,
        ProcessInstanceIdentity processIdentity,
        IPluginConfigurationPersistence configuration,
        IQbittorrentClientFactory clientFactory,
        IProtectionExecutionGate executionGate,
        IProtectionCoordinatorStateControl coordinatorState,
        IProtectionWakeSignal wakeSignal,
        IReconciliationDelay delay,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(journalStore);
        ArgumentNullException.ThrowIfNull(processIdentity);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(clientFactory);
        ArgumentNullException.ThrowIfNull(executionGate);
        ArgumentNullException.ThrowIfNull(coordinatorState);
        ArgumentNullException.ThrowIfNull(wakeSignal);
        ArgumentNullException.ThrowIfNull(delay);
        ArgumentNullException.ThrowIfNull(timeProvider);
        _journalStore = journalStore;
        _processIdentity = processIdentity;
        _configuration = configuration;
        _clientFactory = clientFactory;
        _executionGate = executionGate;
        _coordinatorState = coordinatorState;
        _wakeSignal = wakeSignal;
        _delay = delay;
        _timeProvider = timeProvider;
    }

    /// <summary>Starts non-excluded marked torrents and removes markers after read-back.</summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The bounded recovery result.</returns>
    public Task<RecoveryResult> ResumeMarkedTorrentsAsync(CancellationToken cancellationToken)
    {
        return _executionGate.ExecuteAsync(ResumeMarkedCoreAsync, cancellationToken);
    }

    /// <summary>Restores the previously observed Alternative Limits mode when known.</summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The bounded recovery result.</returns>
    public Task<RecoveryResult> RestorePreviousSpeedSettingAsync(
        CancellationToken cancellationToken)
    {
        return _executionGate.ExecuteAsync(RestorePreviousSpeedCoreAsync, cancellationToken);
    }

    /// <summary>Clears recovery state without contacting qBittorrent.</summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The completed or bounded failed result.</returns>
    public Task<RecoveryResult> MarkResolvedAsync(CancellationToken cancellationToken)
    {
        return _executionGate.ExecuteAsync(MarkResolvedCoreAsync, cancellationToken);
    }

    private async Task<RecoveryResult> ResumeMarkedCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await _journalStore
                .LoadAsync(_processIdentity.Value, cancellationToken)
                .ConfigureAwait(false);
            var document = loaded.Document;
            if (document is null)
            {
                document = CreateManualTorrentRecoveryDocument();
                if (document is null)
                {
                    return Finish(new RecoveryResult(RecoveryOutcome.NotAvailable));
                }

                await _journalStore.WriteAsync(document, cancellationToken).ConfigureAwait(false);
            }
            else if (!document.Configuration.StopTorrentsEnabled)
            {
                document = document with
                {
                    Configuration = document.Configuration with
                    {
                        StopTorrentsEnabled = true,
                        StopScope = TorrentScope.All,
                        SelectedCategories = [],
                        IncludeIncomplete = true,
                        IncludeCompleted = true,
                    },
                };
                await _journalStore.WriteAsync(document, cancellationToken).ConfigureAwait(false);
            }

            var client = _clientFactory.Create(document.Endpoint);
            using var action = new StopTorrentsActionService(client, _journalStore);
            for (var pass = 0; pass < MaxImmediateTorrentRestorationPasses; pass++)
            {
                document = await action
                    .ReconcileRestorationAsync(
                        document,
                        ActivationJournalAuthority.Full,
                        cancellationToken)
                    .ConfigureAwait(false);
                if (!TorrentsSettled(document))
                {
                    if (pass + 1 < MaxImmediateTorrentRestorationPasses)
                    {
                        // The accepted start can remain stopped briefly while qBittorrent
                        // moves it into a queued or active state.
                        await _delay
                            .WaitAsync(TorrentRestorationReadbackDelay, cancellationToken)
                            .ConfigureAwait(false);
                    }

                    continue;
                }

                if (AlternativeRecoverySettled(document))
                {
                    await _journalStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
                }

                return Finish(new RecoveryResult(RecoveryOutcome.Completed));
            }

            return Finish(new RecoveryResult(
                RecoveryOutcome.Failed,
                JournalFailureCode.InvalidResponse));
        }
        catch (QbittorrentClientException exception)
        {
            return Finish(new RecoveryResult(
                RecoveryOutcome.Failed,
                QbittorrentFailureMapper.Map(exception.Error)));
        }
        catch (ActivationJournalException)
        {
            return Finish(new RecoveryResult(
                RecoveryOutcome.Failed,
                JournalFailureCode.JournalPersistence));
        }
    }

    private async Task<RecoveryResult> RestorePreviousSpeedCoreAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            var loaded = await _journalStore
                .LoadAsync(_processIdentity.Value, cancellationToken)
                .ConfigureAwait(false);
            var document = loaded.Document;
            if (document?.AlternativeLimits.InitialEnabled is null)
            {
                return Finish(new RecoveryResult(RecoveryOutcome.NotAvailable));
            }

            var desired = document.AlternativeLimits.InitialEnabled.Value;
            var client = _clientFactory.Create(document.Endpoint);
            var current = await client
                .GetAlternativeLimitsEnabledAsync(cancellationToken)
                .ConfigureAwait(false);
            if (current != desired)
            {
                document = document with
                {
                    AlternativeLimits = document.AlternativeLimits with
                    {
                        ManualRestoreTarget = desired,
                        ManualRestoreStage = JournalMutationStage.IntentPersisted,
                    },
                };
                await _journalStore.WriteAsync(document, cancellationToken).ConfigureAwait(false);
                await client
                    .SetAlternativeLimitsEnabledAsync(desired, cancellationToken)
                    .ConfigureAwait(false);
                current = await client
                    .GetAlternativeLimitsEnabledAsync(cancellationToken)
                    .ConfigureAwait(false);
                if (current != desired)
                {
                    throw new QbittorrentClientException(
                        QbittorrentClientError.InvalidResponse,
                        "qBittorrent did not confirm the requested Alternative Limits state.");
                }
            }

            document = document with
            {
                AlternativeLimits = document.AlternativeLimits with
                {
                    ManualRestoreTarget = desired,
                    ManualRestoreStage = JournalMutationStage.Confirmed,
                },
            };
            await _journalStore.WriteAsync(document, cancellationToken).ConfigureAwait(false);
            if (await RecoverySettledAsync(document, client, cancellationToken).ConfigureAwait(false))
            {
                await _journalStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
            }

            return Finish(new RecoveryResult(RecoveryOutcome.Completed));
        }
        catch (QbittorrentClientException exception)
        {
            return Finish(new RecoveryResult(
                RecoveryOutcome.Failed,
                QbittorrentFailureMapper.Map(exception.Error)));
        }
        catch (ActivationJournalException)
        {
            return Finish(new RecoveryResult(
                RecoveryOutcome.Failed,
                JournalFailureCode.JournalPersistence));
        }
    }

    private async Task<RecoveryResult> MarkResolvedCoreAsync(CancellationToken cancellationToken)
    {
        try
        {
            _ = await _journalStore
                .LoadAsync(_processIdentity.Value, cancellationToken)
                .ConfigureAwait(false);
            await _journalStore.DeleteAsync(cancellationToken).ConfigureAwait(false);
            return Finish(new RecoveryResult(RecoveryOutcome.Completed));
        }
        catch (ActivationJournalException)
        {
            return Finish(new RecoveryResult(
                RecoveryOutcome.Failed,
                JournalFailureCode.JournalPersistence));
        }
    }

    private static async Task<bool> RecoverySettledAsync(
        ActivationJournalDocument document,
        IQbittorrentClient client,
        CancellationToken cancellationToken)
    {
        if (!document.Configuration.StopTorrentsEnabled)
        {
            return true;
        }

        var torrents = await client.GetTorrentsAsync(cancellationToken).ConfigureAwait(false);
        return !torrents.Any(torrent =>
            torrent.Tags.Contains(document.Configuration.MarkerTag)
            && !torrent.Tags.Any(document.Configuration.ExclusionTags.Contains));
    }

    private ActivationJournalDocument? CreateManualTorrentRecoveryDocument()
    {
        var configuration = _configuration.Current;
        if (!configuration.ConnectionValidated
            || string.IsNullOrWhiteSpace(configuration.QbittorrentBaseAddress))
        {
            return null;
        }

        var endpoint = new Uri(configuration.QbittorrentBaseAddress, UriKind.Absolute);
        return new ActivationJournalDocument(
            1,
            _processIdentity.Value,
            Guid.NewGuid(),
            _timeProvider.GetUtcNow(),
            ImmutableArray.Create("manual-recovery"),
            new JournalConfigurationSnapshot(
                configuration.Revision,
                false,
                true,
                TorrentScope.All,
                [],
                true,
                true,
                configuration.MarkerTag,
                (configuration.ExclusionTags ?? []).ToImmutableArray(),
                TimeSpan.Zero),
            new QbittorrentEndpointIdentity(
                endpoint.Scheme,
                endpoint.Host,
                endpoint.Port,
                endpoint.AbsolutePath),
            new AlternativeLimitsJournalState(
                null,
                false,
                JournalMutationStage.None,
                JournalMutationStage.None),
            [],
            ProtectionPhase.Restoring,
            null,
            null,
            null);
    }

    private RecoveryResult Finish(RecoveryResult result)
    {
        _coordinatorState.InvalidateJournalCache();
        _wakeSignal.Wake();
        return result;
    }

    private static bool TorrentsSettled(ActivationJournalDocument document)
    {
        return document.Torrents.All(entry =>
            entry.StartStage != JournalMutationStage.IntentPersisted
            && entry.MarkerRemoveStage != JournalMutationStage.IntentPersisted);
    }

    private static bool AlternativeRecoverySettled(ActivationJournalDocument document)
    {
        return !document.Configuration.AlternativeLimitsEnabled
            || !document.AlternativeLimits.InitialEnabled.HasValue
            || document.AlternativeLimits.ManualRestoreStage == JournalMutationStage.Confirmed;
    }
}
