using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.QControl.Coordination;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Reads the current durable activation without changing coordinator authority.
/// </summary>
public sealed class JournalActivationStateReader : IActivationStateReader
{
    private readonly IActivationJournalStore _journalStore;
    private readonly ProcessInstanceIdentity _processIdentity;

    /// <summary>
    /// Initializes a new instance of the <see cref="JournalActivationStateReader"/> class.
    /// </summary>
    /// <param name="journalStore">The durable activation boundary.</param>
    /// <param name="processIdentity">The current process identity.</param>
    public JournalActivationStateReader(
        IActivationJournalStore journalStore,
        ProcessInstanceIdentity processIdentity)
    {
        ArgumentNullException.ThrowIfNull(journalStore);
        ArgumentNullException.ThrowIfNull(processIdentity);
        _journalStore = journalStore;
        _processIdentity = processIdentity;
    }

    /// <inheritdoc />
    public async Task<ActivationJournalDocument?> ReadAsync(CancellationToken cancellationToken)
    {
        var loaded = await _journalStore
            .LoadAsync(_processIdentity.Value, cancellationToken)
            .ConfigureAwait(false);
        return loaded.Document;
    }
}
