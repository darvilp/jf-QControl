using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Domain.Torrents;
using Jellyfin.Plugin.QControl.Playback;
using Jellyfin.Plugin.QControl.QBittorrent;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Serializes validation, connection proof, revisioning, persistence, and worker wake-up.
/// </summary>
public sealed class ConfigurationService : IDisposable
{
    private const int MaximumReleaseGraceSeconds = 24 * 60 * 60;
    private readonly IPluginConfigurationPersistence _persistence;
    private readonly IQbittorrentConnectionProbe _connectionProbe;
    private readonly IActivationStateReader _activationState;
    private readonly IProtectionWakeSignal _wakeSignal;
    private readonly IProtectionExecutionGate _executionGate;
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>Initializes a new instance of the <see cref="ConfigurationService"/> class.</summary>
    /// <param name="persistence">The accepted configuration boundary.</param>
    /// <param name="connectionProbe">The read-only connection proof boundary.</param>
    /// <param name="activationState">The active configuration-snapshot boundary.</param>
    /// <param name="wakeSignal">The worker wake signal.</param>
    /// <param name="executionGate">The process-wide protection execution gate.</param>
    public ConfigurationService(
        IPluginConfigurationPersistence persistence,
        IQbittorrentConnectionProbe connectionProbe,
        IActivationStateReader activationState,
        IProtectionWakeSignal wakeSignal,
        IProtectionExecutionGate executionGate)
    {
        ArgumentNullException.ThrowIfNull(persistence);
        ArgumentNullException.ThrowIfNull(connectionProbe);
        ArgumentNullException.ThrowIfNull(activationState);
        ArgumentNullException.ThrowIfNull(wakeSignal);
        ArgumentNullException.ThrowIfNull(executionGate);
        _persistence = persistence;
        _connectionProbe = connectionProbe;
        _activationState = activationState;
        _wakeSignal = wakeSignal;
        _executionGate = executionGate;
    }

    /// <summary>Gets the current credential-safe configuration.</summary>
    /// <returns>The complete safe configuration view.</returns>
    public ConfigurationView Get() => ToView(_persistence.Current);

    /// <summary>Tests a complete candidate without persisting it or mutating qBittorrent.</summary>
    /// <param name="candidate">The administrator-supplied complete candidate.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>Compatible versions, categories, or one bounded failure.</returns>
    public async Task<QbittorrentConnectionProbeResult> TestConnectionAsync(
        ConfigurationCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        var current = _persistence.Current;
        if (!TryBuildCandidate(current, candidate, out var next, out _)
            || string.IsNullOrWhiteSpace(next.QbittorrentBaseAddress))
        {
            return QbittorrentConnectionProbeResult.Failed(
                Journal.JournalFailureCode.Credential);
        }

        return await _connectionProbe.ProbeAsync(next, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads compatible versions and categories for the saved connection.</summary>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>Compatible versions, categories, or one bounded failure.</returns>
    public Task<QbittorrentConnectionProbeResult> GetCategoriesAsync(
        CancellationToken cancellationToken)
    {
        var current = _persistence.Current;
        return string.IsNullOrWhiteSpace(current.QbittorrentBaseAddress)
            ? Task.FromResult(QbittorrentConnectionProbeResult.Failed(
                Journal.JournalFailureCode.Credential))
            : _connectionProbe.ProbeAsync(current, cancellationToken);
    }

    /// <summary>Validates and conditionally persists one complete candidate.</summary>
    /// <param name="candidate">The administrator-supplied complete candidate.</param>
    /// <param name="cancellationToken">Caller cancellation.</param>
    /// <returns>The accepted or bounded rejected result.</returns>
    public async Task<ConfigurationSaveResult> SaveAsync(
        ConfigurationCandidate candidate,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await _executionGate
                .ExecuteAsync(
                    token => SaveCoreAsync(candidate, token),
                    cancellationToken)
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

    private async Task<ConfigurationSaveResult> SaveCoreAsync(
        ConfigurationCandidate candidate,
        CancellationToken cancellationToken)
    {
        var current = _persistence.Current;
        if (candidate.ExpectedRevision != current.Revision)
        {
            return Result(ConfigurationSaveOutcome.RevisionConflict, current);
        }

        if (!TryBuildCandidate(current, candidate, out var next, out var credentialChanged))
        {
            return Result(ConfigurationSaveOutcome.Invalid, current);
        }

        var topologyChanged = HasTopologyChanged(current, next);
        if (topologyChanged
            && await _activationState.ReadAsync(cancellationToken).ConfigureAwait(false) is not null)
        {
            return Result(ConfigurationSaveOutcome.ActiveConnectionConflict, current);
        }

        next.ConnectionValidated = current.ConnectionValidated
            && !topologyChanged
            && !credentialChanged;
        var hasEnabledAction = next.AlternativeLimitsEnabled || next.StopTorrentsEnabled;
        if (hasEnabledAction && !next.ConnectionValidated)
        {
            var probe = await _connectionProbe
                .ProbeAsync(next, cancellationToken)
                .ConfigureAwait(false);
            if (!probe.IsConnected)
            {
                return new ConfigurationSaveResult(
                    ConfigurationSaveOutcome.ConnectionFailed,
                    ToView(current),
                    probe.Failure);
            }

            next.ConnectionValidated = true;
        }

        next.Revision = checked(current.Revision + 1);
        _persistence.Save(next);
        _wakeSignal.Wake();
        return Result(ConfigurationSaveOutcome.Accepted, next);
    }

    private static bool TryBuildCandidate(
        PluginConfiguration current,
        ConfigurationCandidate candidate,
        out PluginConfiguration next,
        out bool credentialChanged)
    {
        next = new PluginConfiguration();
        credentialChanged = false;
        if (!Enum.IsDefined(candidate.CredentialMode)
            || !Enum.IsDefined(candidate.StopScope)
            || candidate.ReleaseGraceSeconds < 0
            || candidate.ReleaseGraceSeconds > MaximumReleaseGraceSeconds
            || (candidate.ClearStoredApiKey
                && !string.IsNullOrWhiteSpace(candidate.ApiKeyReplacement)))
        {
            return false;
        }

        var categories = candidate.SelectedCategories ?? [];
        try
        {
            if (candidate.StopTorrentsEnabled)
            {
                _ = new TorrentSelectionPolicy(
                    candidate.StopScope,
                    categories,
                    candidate.IncludeIncomplete,
                    candidate.IncludeCompleted,
                    candidate.MarkerTag,
                    candidate.NeverTouchTag);
            }

            var hasEnabledAction = candidate.AlternativeLimitsEnabled
                || candidate.StopTorrentsEnabled;
            if (hasEnabledAction || !string.IsNullOrWhiteSpace(candidate.QbittorrentBaseAddress))
            {
                _ = new QbittorrentConnectionOptions(
                    new Uri(candidate.QbittorrentBaseAddress, UriKind.Absolute),
                    TimeSpan.FromSeconds(15));
            }

            var storedKey = current.QbittorrentApiKey;
            if (candidate.ClearStoredApiKey)
            {
                storedKey = string.Empty;
                credentialChanged = !string.IsNullOrEmpty(current.QbittorrentApiKey);
            }
            else if (!string.IsNullOrWhiteSpace(candidate.ApiKeyReplacement))
            {
                _ = QbittorrentApiKey.Create(candidate.ApiKeyReplacement);
                storedKey = candidate.ApiKeyReplacement;
                credentialChanged = !string.Equals(
                    storedKey,
                    current.QbittorrentApiKey,
                    StringComparison.Ordinal);
            }

            if (hasEnabledAction
                && candidate.CredentialMode == QbittorrentCredentialMode.StoredApiKey)
            {
                _ = QbittorrentApiKey.Create(storedKey);
            }

            if (hasEnabledAction
                && candidate.CredentialMode == QbittorrentCredentialMode.SecretFile
                && string.IsNullOrWhiteSpace(candidate.SecretFilePath))
            {
                return false;
            }

            next = new PluginConfiguration
            {
                SchemaVersion = 1,
                Revision = current.Revision,
                QbittorrentBaseAddress = candidate.QbittorrentBaseAddress.Trim(),
                CredentialMode = candidate.CredentialMode,
                QbittorrentApiKey = storedKey,
                SecretFilePath = candidate.SecretFilePath.Trim(),
                AlternativeLimitsEnabled = candidate.AlternativeLimitsEnabled,
                StopTorrentsEnabled = candidate.StopTorrentsEnabled,
                StopScope = candidate.StopScope,
                SelectedCategories = categories
                    .Distinct(StringComparer.Ordinal)
                    .Order(StringComparer.Ordinal)
                    .ToArray(),
                IncludeIncomplete = candidate.IncludeIncomplete,
                IncludeCompleted = candidate.IncludeCompleted,
                MarkerTag = candidate.MarkerTag,
                NeverTouchTag = candidate.NeverTouchTag,
                ReleaseGraceSeconds = candidate.ReleaseGraceSeconds,
            };
            return true;
        }
        catch (Exception exception) when (exception is ArgumentException
            or UriFormatException
            or QbittorrentClientException)
        {
            return false;
        }
    }

    private static bool HasTopologyChanged(
        PluginConfiguration current,
        PluginConfiguration next)
    {
        return !string.Equals(
                current.QbittorrentBaseAddress.TrimEnd('/'),
                next.QbittorrentBaseAddress.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase)
            || current.CredentialMode != next.CredentialMode
            || !string.Equals(
                current.SecretFilePath,
                next.SecretFilePath,
                StringComparison.Ordinal);
    }

    private static ConfigurationSaveResult Result(
        ConfigurationSaveOutcome outcome,
        PluginConfiguration configuration)
    {
        return new ConfigurationSaveResult(outcome, ToView(configuration), null);
    }

    private static ConfigurationView ToView(PluginConfiguration configuration)
    {
        return new ConfigurationView(
            configuration.Revision,
            configuration.QbittorrentBaseAddress,
            configuration.CredentialMode,
            !string.IsNullOrEmpty(configuration.QbittorrentApiKey),
            configuration.SecretFilePath,
            configuration.ConnectionValidated,
            configuration.AlternativeLimitsEnabled,
            configuration.StopTorrentsEnabled,
            configuration.StopScope,
            Array.AsReadOnly((configuration.SelectedCategories ?? []).ToArray()),
            configuration.IncludeIncomplete,
            configuration.IncludeCompleted,
            configuration.MarkerTag,
            configuration.NeverTouchTag,
            configuration.ReleaseGraceSeconds);
    }
}
