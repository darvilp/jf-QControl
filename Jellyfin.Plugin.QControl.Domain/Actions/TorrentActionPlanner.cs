using System;
using System.Collections.Generic;
using System.Linq;
using Jellyfin.Plugin.QControl.Domain.Torrents;

namespace Jellyfin.Plugin.QControl.Domain.Actions;

/// <summary>
/// Plans idempotent torrent mutations from neutral qBittorrent snapshots.
/// </summary>
public static class TorrentActionPlanner
{
    /// <summary>
    /// Plans marker acquisition followed by stopping for eligible running torrents.
    /// </summary>
    /// <param name="torrents">The complete current torrent snapshots.</param>
    /// <param name="policy">The immutable activation selection policy.</param>
    /// <returns>A deterministic explicit-hash mutation plan.</returns>
    public static TorrentMutationPlan PlanProtection(
        IEnumerable<TorrentSnapshot> torrents,
        TorrentSelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(torrents);
        ArgumentNullException.ThrowIfNull(policy);

        var snapshots = torrents.ToArray();
        var selectedHashes = TorrentSelector.SelectForAcquisition(snapshots, policy);
        var selectedHashSet = selectedHashes.ToHashSet(StringComparer.Ordinal);
        var markerHashes = snapshots
            .Where(torrent => selectedHashSet.Contains(torrent.Hash))
            .Where(torrent => !torrent.Tags.Contains(policy.MarkerTag))
            .Select(torrent => torrent.Hash)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new TorrentMutationPlan(markerHashes, selectedHashes, [], []);
    }

    /// <summary>
    /// Plans start or marker removal for marker-owned torrents.
    /// </summary>
    /// <param name="torrents">The complete current torrent snapshots.</param>
    /// <param name="policy">The immutable activation selection policy.</param>
    /// <returns>A deterministic explicit-hash mutation plan.</returns>
    public static TorrentMutationPlan PlanRestoration(
        IEnumerable<TorrentSnapshot> torrents,
        TorrentSelectionPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(torrents);
        ArgumentNullException.ThrowIfNull(policy);

        var owned = torrents
            .Where(torrent => torrent.Tags.Contains(policy.MarkerTag))
            .Where(torrent => !torrent.Tags.Contains(policy.NeverTouchTag))
            .ToArray();
        var startHashes = owned
            .Where(torrent => torrent.IsStopped)
            .Select(torrent => torrent.Hash)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        var removeMarkerHashes = owned
            .Where(torrent => !torrent.IsStopped)
            .Select(torrent => torrent.Hash)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return new TorrentMutationPlan([], [], startHashes, removeMarkerHashes);
    }
}
