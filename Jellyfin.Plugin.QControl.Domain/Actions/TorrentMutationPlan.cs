using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace Jellyfin.Plugin.QControl.Domain.Actions;

/// <summary>
/// Explicit, ordered-by-stage torrent mutations for one reconciliation pass.
/// </summary>
public sealed record TorrentMutationPlan
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TorrentMutationPlan"/> class.
    /// </summary>
    /// <param name="addMarkerTagHashes">Hashes that must receive the marker before stopping.</param>
    /// <param name="stopHashes">Hashes that must be stopped after marker assignment.</param>
    /// <param name="startHashes">Hashes that must be started before marker removal.</param>
    /// <param name="removeMarkerTagHashes">Hashes whose marker can be removed after start readback.</param>
    public TorrentMutationPlan(
        IEnumerable<string> addMarkerTagHashes,
        IEnumerable<string> stopHashes,
        IEnumerable<string> startHashes,
        IEnumerable<string> removeMarkerTagHashes)
    {
        AddMarkerTagHashes = Copy(addMarkerTagHashes);
        StopHashes = Copy(stopHashes);
        StartHashes = Copy(startHashes);
        RemoveMarkerTagHashes = Copy(removeMarkerTagHashes);
    }

    /// <summary>
    /// Gets hashes that must receive the marker before stopping.
    /// </summary>
    public IReadOnlyList<string> AddMarkerTagHashes { get; }

    /// <summary>
    /// Gets hashes that must be stopped after marker assignment.
    /// </summary>
    public IReadOnlyList<string> StopHashes { get; }

    /// <summary>
    /// Gets hashes that must be started before marker removal.
    /// </summary>
    public IReadOnlyList<string> StartHashes { get; }

    /// <summary>
    /// Gets hashes whose marker can be removed after start readback.
    /// </summary>
    public IReadOnlyList<string> RemoveMarkerTagHashes { get; }

    /// <summary>
    /// Gets a value indicating whether the plan requires no qBittorrent mutation.
    /// </summary>
    public bool IsEmpty => AddMarkerTagHashes.Count == 0
        && StopHashes.Count == 0
        && StartHashes.Count == 0
        && RemoveMarkerTagHashes.Count == 0;

    private static ReadOnlyCollection<string> Copy(IEnumerable<string> hashes)
    {
        ArgumentNullException.ThrowIfNull(hashes);
        return Array.AsReadOnly(hashes.ToArray());
    }
}
