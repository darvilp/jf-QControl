using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Configuration;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Journal;
using Jellyfin.Plugin.QControl.Playback;
using Xunit;

namespace Jellyfin.Plugin.QControl.Tests.Configuration;

public sealed class ConfigurationServiceTests
{
    private const string FirstKey = "qbt_1234567890123456789012345678";
    private const string SecondKey = "qbt_abcdefghijklmnopqrstuvwxyz12";

    [Fact]
    public async Task InvalidCandidateDoesNotReplaceCurrentConfiguration()
    {
        var persistence = new RecordingPersistence(ValidCurrent());
        var probe = new RecordingConnectionProbe();
        using var service = CreateService(persistence, probe);
        var candidate = CandidateFrom(persistence.Current);
        candidate.MarkerTag = "same";
        candidate.NeverTouchTag = "same";

        var result = await service.SaveAsync(candidate, CancellationToken.None);

        Assert.Equal(ConfigurationSaveOutcome.Invalid, result.Outcome);
        Assert.Equal(0, persistence.SaveCalls);
        Assert.Equal(0, probe.Calls);
        Assert.Equal("jfStopped", persistence.Current.MarkerTag);
    }

    [Fact]
    public async Task BehaviorChangeDuringActivationSavesNextRevisionWithoutRetestingConnection()
    {
        var persistence = new RecordingPersistence(ValidCurrent());
        var probe = new RecordingConnectionProbe();
        var activation = new RecordingActivationStateReader
        {
            Current = JournalFor(persistence.Current),
        };
        var wake = new RecordingWakeSignal();
        using var service = new ConfigurationService(
            persistence,
            probe,
            activation,
            wake,
            new RecordingExecutionGate());
        var candidate = CandidateFrom(persistence.Current);
        candidate.ReleaseGraceSeconds = 90;

        var result = await service.SaveAsync(candidate, CancellationToken.None);

        Assert.Equal(ConfigurationSaveOutcome.Accepted, result.Outcome);
        Assert.Equal(2, persistence.Current.Revision);
        Assert.Equal(90, persistence.Current.ReleaseGraceSeconds);
        Assert.Equal(0, probe.Calls);
        Assert.Equal(1, wake.Count);
    }

    [Fact]
    public async Task NewEnabledConnectionMustPassProbeBeforeItCanReplaceCurrent()
    {
        var current = ValidCurrent();
        current.ConnectionValidated = false;
        current.AlternativeLimitsEnabled = false;
        current.StopTorrentsEnabled = false;
        var persistence = new RecordingPersistence(current);
        var probe = new RecordingConnectionProbe
        {
            Result = QbittorrentConnectionProbeResult.Failed(JournalFailureCode.Authentication),
        };
        using var service = CreateService(persistence, probe);
        var candidate = CandidateFrom(current);
        candidate.AlternativeLimitsEnabled = true;

        var rejected = await service.SaveAsync(candidate, CancellationToken.None);

        Assert.Equal(ConfigurationSaveOutcome.ConnectionFailed, rejected.Outcome);
        Assert.Equal(0, persistence.SaveCalls);
        Assert.False(persistence.Current.AlternativeLimitsEnabled);

        probe.Result = QbittorrentConnectionProbeResult.Connected(
            new Version(5, 2, 3),
            new Version(2, 15, 1),
            ["radarr", "sonarr"]);
        var accepted = await service.SaveAsync(candidate, CancellationToken.None);

        Assert.Equal(ConfigurationSaveOutcome.Accepted, accepted.Outcome);
        Assert.True(persistence.Current.AlternativeLimitsEnabled);
        Assert.True(persistence.Current.ConnectionValidated);
    }

    [Fact]
    public async Task EnabledActionsAcceptExplicitUnauthenticatedModeAfterConnectionProbe()
    {
        var persistence = new RecordingPersistence(ValidCurrent());
        var probe = new RecordingConnectionProbe();
        using var service = CreateService(persistence, probe);
        var candidate = CandidateFrom(persistence.Current);
        candidate.CredentialMode = QbittorrentCredentialMode.Unauthenticated;

        var result = await service.SaveAsync(candidate, CancellationToken.None);

        Assert.Equal(ConfigurationSaveOutcome.Accepted, result.Outcome);
        Assert.Equal(QbittorrentCredentialMode.Unauthenticated, persistence.Current.CredentialMode);
        Assert.Equal(FirstKey, persistence.Current.QbittorrentApiKey);
        Assert.True(persistence.Current.ConnectionValidated);
        Assert.Equal(1, probe.Calls);
        Assert.Equal(QbittorrentCredentialMode.Unauthenticated, probe.LastCandidate?.CredentialMode);
    }

    [Fact]
    public async Task BlankReplacementRetainsStoredKeyAndExplicitReplacementReconnects()
    {
        var persistence = new RecordingPersistence(ValidCurrent());
        var probe = new RecordingConnectionProbe();
        using var service = CreateService(persistence, probe);
        var retainedCandidate = CandidateFrom(persistence.Current);
        retainedCandidate.ApiKeyReplacement = "";

        var retained = await service.SaveAsync(retainedCandidate, CancellationToken.None);

        Assert.Equal(ConfigurationSaveOutcome.Accepted, retained.Outcome);
        Assert.Equal(FirstKey, persistence.Current.QbittorrentApiKey);
        Assert.Equal(0, probe.Calls);

        var replacement = CandidateFrom(persistence.Current);
        replacement.ApiKeyReplacement = SecondKey;
        var replaced = await service.SaveAsync(replacement, CancellationToken.None);

        Assert.Equal(ConfigurationSaveOutcome.Accepted, replaced.Outcome);
        Assert.Equal(SecondKey, persistence.Current.QbittorrentApiKey);
        Assert.Equal(1, probe.Calls);
        Assert.Equal(SecondKey, probe.LastCandidate?.QbittorrentApiKey);
    }

    [Fact]
    public async Task ActiveConnectionTopologyCannotChangeButCredentialCan()
    {
        var persistence = new RecordingPersistence(ValidCurrent());
        var activation = new RecordingActivationStateReader
        {
            Current = JournalFor(persistence.Current),
        };
        var probe = new RecordingConnectionProbe();
        using var service = new ConfigurationService(
            persistence,
            probe,
            activation,
            new RecordingWakeSignal(),
            new RecordingExecutionGate());
        var candidate = CandidateFrom(persistence.Current);
        candidate.QbittorrentBaseAddress = "http://another-host:8080";

        var topologyChange = await service.SaveAsync(candidate, CancellationToken.None);

        Assert.Equal(ConfigurationSaveOutcome.ActiveConnectionConflict, topologyChange.Outcome);
        Assert.Equal(0, persistence.SaveCalls);

        candidate = CandidateFrom(persistence.Current);
        candidate.ApiKeyReplacement = SecondKey;
        var credentialChange = await service.SaveAsync(candidate, CancellationToken.None);

        Assert.Equal(ConfigurationSaveOutcome.Accepted, credentialChange.Outcome);
        Assert.Equal(SecondKey, persistence.Current.QbittorrentApiKey);
    }

    [Fact]
    public void ConfigurationViewContainsOnlyCredentialPresence()
    {
        var persistence = new RecordingPersistence(ValidCurrent());
        using var service = CreateService(persistence, new RecordingConnectionProbe());

        var view = service.Get();
        var serializedShape = view.ToString();

        Assert.True(view.HasStoredApiKey);
        Assert.DoesNotContain(FirstKey, serializedShape, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ActiveStateCheckAndAcceptedSaveShareCoordinatorExecutionGate()
    {
        var gate = new RecordingExecutionGate();
        var persistence = new RecordingPersistence(ValidCurrent())
        {
            BeforeSave = () => Assert.True(gate.IsExecuting),
        };
        var activation = new RecordingActivationStateReader
        {
            BeforeRead = () => Assert.True(gate.IsExecuting),
        };
        using var service = new ConfigurationService(
            persistence,
            new RecordingConnectionProbe(),
            activation,
            new RecordingWakeSignal(),
            gate);
        var candidate = CandidateFrom(persistence.Current);
        candidate.ReleaseGraceSeconds = 61;

        var result = await service.SaveAsync(candidate, CancellationToken.None);

        Assert.Equal(ConfigurationSaveOutcome.Accepted, result.Outcome);
        Assert.Equal(1, gate.Calls);
    }

    private static ConfigurationService CreateService(
        IPluginConfigurationPersistence persistence,
        IQbittorrentConnectionProbe probe)
    {
        return new ConfigurationService(
            persistence,
            probe,
            new RecordingActivationStateReader(),
            new RecordingWakeSignal(),
            new RecordingExecutionGate());
    }

    private static PluginConfiguration ValidCurrent()
    {
        return new PluginConfiguration
        {
            Revision = 1,
            QbittorrentBaseAddress = "http://qbittorrent:8080",
            CredentialMode = QbittorrentCredentialMode.StoredApiKey,
            QbittorrentApiKey = FirstKey,
            ConnectionValidated = true,
            AlternativeLimitsEnabled = true,
            StopTorrentsEnabled = true,
            StopScope = TorrentScope.All,
            SelectedCategories = [],
            IncludeIncomplete = true,
            IncludeCompleted = true,
            MarkerTag = "jfStopped",
            NeverTouchTag = "jfNeverTouch",
            ReleaseGraceSeconds = 60,
        };
    }

    private static ConfigurationCandidate CandidateFrom(PluginConfiguration configuration)
    {
        return new ConfigurationCandidate
        {
            ExpectedRevision = configuration.Revision,
            QbittorrentBaseAddress = configuration.QbittorrentBaseAddress,
            CredentialMode = configuration.CredentialMode,
            SecretFilePath = configuration.SecretFilePath,
            AlternativeLimitsEnabled = configuration.AlternativeLimitsEnabled,
            StopTorrentsEnabled = configuration.StopTorrentsEnabled,
            StopScope = configuration.StopScope,
            SelectedCategories = configuration.SelectedCategories,
            IncludeIncomplete = configuration.IncludeIncomplete,
            IncludeCompleted = configuration.IncludeCompleted,
            MarkerTag = configuration.MarkerTag,
            NeverTouchTag = configuration.NeverTouchTag,
            ReleaseGraceSeconds = configuration.ReleaseGraceSeconds,
        };
    }

    private static ActivationJournalDocument JournalFor(PluginConfiguration configuration)
    {
        return new ActivationJournalDocument(
            SchemaVersion: 1,
            ProcessInstanceId: Guid.NewGuid(),
            ActivationId: Guid.NewGuid(),
            StartedAt: new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero),
            SessionIds: System.Collections.Immutable.ImmutableArray.Create("session-a"),
            Configuration: new JournalConfigurationSnapshot(
                configuration.Revision,
                configuration.AlternativeLimitsEnabled,
                configuration.StopTorrentsEnabled,
                configuration.StopScope,
                System.Collections.Immutable.ImmutableArray<string>.Empty,
                configuration.IncludeIncomplete,
                configuration.IncludeCompleted,
                configuration.MarkerTag,
                configuration.NeverTouchTag,
                TimeSpan.FromSeconds(configuration.ReleaseGraceSeconds)),
            Endpoint: new QbittorrentEndpointIdentity("http", "qbittorrent", 8080, "/"),
            AlternativeLimits: new AlternativeLimitsJournalState(
                null,
                false,
                JournalMutationStage.None,
                JournalMutationStage.None),
            Torrents: [],
            Phase: Jellyfin.Plugin.QControl.Domain.Activation.ProtectionPhase.Protecting,
            ReleaseDueAt: null,
            LastSuccessfulReconciliation: null,
            LastFailure: null);
    }

    private sealed class RecordingPersistence(PluginConfiguration current)
        : IPluginConfigurationPersistence
    {
        public PluginConfiguration Current { get; private set; } = current;

        public int SaveCalls { get; private set; }

        public Action? BeforeSave { get; init; }

        public void Save(PluginConfiguration configuration)
        {
            BeforeSave?.Invoke();
            Current = configuration;
            SaveCalls++;
        }
    }

    private sealed class RecordingConnectionProbe : IQbittorrentConnectionProbe
    {
        public QbittorrentConnectionProbeResult Result { get; set; } =
            QbittorrentConnectionProbeResult.Connected(
                new Version(5, 2, 3),
                new Version(2, 15, 1),
                []);

        public int Calls { get; private set; }

        public PluginConfiguration? LastCandidate { get; private set; }

        public Task<QbittorrentConnectionProbeResult> ProbeAsync(
            PluginConfiguration candidate,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastCandidate = candidate;
            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingActivationStateReader : IActivationStateReader
    {
        public ActivationJournalDocument? Current { get; set; }

        public Action? BeforeRead { get; init; }

        public Task<ActivationJournalDocument?> ReadAsync(CancellationToken cancellationToken)
        {
            BeforeRead?.Invoke();
            return Task.FromResult(Current);
        }
    }

    private sealed class RecordingExecutionGate : IProtectionExecutionGate
    {
        public int Calls { get; private set; }

        public bool IsExecuting { get; private set; }

        public async Task<T> ExecuteAsync<T>(
            Func<CancellationToken, Task<T>> operation,
            CancellationToken cancellationToken)
        {
            Calls++;
            IsExecuting = true;
            try
            {
                return await operation(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                IsExecuting = false;
            }
        }
    }

    private sealed class RecordingWakeSignal : IProtectionWakeSignal
    {
        public int Count { get; private set; }

        public void Wake()
        {
            Count++;
        }
    }
}
