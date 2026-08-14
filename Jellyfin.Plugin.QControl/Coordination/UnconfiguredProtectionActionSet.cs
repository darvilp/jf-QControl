using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Coordination;

/// <summary>
/// Refuses mutations and recovery claims until a validated qBittorrent action set is configured.
/// </summary>
public sealed class UnconfiguredProtectionActionSet : IProtectionActionSet
{
    /// <inheritdoc />
    public Task<ProtectionActionSetResult> ReconcileProtectionAsync(
        ActivationJournalDocument journal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProtectionActionSetResult(
            journal,
            false,
            JournalFailureCode.Credential));
    }

    /// <inheritdoc />
    public Task<ProtectionActionSetResult> ReconcileRestorationAsync(
        ActivationJournalDocument journal,
        ActivationJournalAuthority authority,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(journal);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new ProtectionActionSetResult(
            journal,
            false,
            JournalFailureCode.Credential));
    }
}
