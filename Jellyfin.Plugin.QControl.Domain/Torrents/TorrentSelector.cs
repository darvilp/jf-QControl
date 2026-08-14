using System;
using System.Collections.Generic;
using System.Linq;

namespace Jellyfin.Plugin.QControl.Domain.Torrents;

/// <summary>
/// Selects explicit torrent hashes from neutral snapshots.
/// </summary>
public static class TorrentSelector
{
    /// <summary>
    /// Selects running torrents that should be acquired by the stop action.
    /// </summary>
    /// <param name="torrents">The complete current torrent snapshots.</param>
    /// <param name="policy">The active immutable selection policy.</param>
    /// <returns>Deterministic explicit torrent hashes.</returns>
    public static IReadOnlyList<string> SelectForAcquisition(
        IEnumerable<TorrentSnapshot> torrents,
        TorrentSelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(torrents);
        ArgumentNullException.ThrowIfNull(policy);

        var selectedHashes = torrents
            .Where(torrent => IsInScope(torrent, policy))
            .Where(torrent => LifecycleQualifies(torrent, policy))
            .Where(torrent => !torrent.Tags.Contains(policy.NeverTouchTag))
            .Where(torrent => !torrent.IsStopped)
            .Select(torrent => torrent.Hash)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return Array.AsReadOnly(selectedHashes);
    }

    private static bool IsInScope(
        TorrentSnapshot torrent,
        TorrentSelectionPolicy policy)
    {
        return policy.Scope == TorrentScope.All
            || (torrent.Category is not null
                && policy.SelectedCategories.Contains(torrent.Category));
    }

    private static bool LifecycleQualifies(
        TorrentSnapshot torrent,
        TorrentSelectionPolicy policy)
    {
        return torrent.IsCompleted
            ? policy.IncludeCompleted
            : policy.IncludeIncomplete;
    }
}
