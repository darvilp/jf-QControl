using System;
using System.Collections.Frozen;
using System.Collections.Generic;

namespace Jellyfin.Plugin.QControl.Domain.Torrents;

/// <summary>
/// Framework-neutral state for one qBittorrent torrent.
/// </summary>
public sealed record TorrentSnapshot
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TorrentSnapshot"/> class.
    /// </summary>
    /// <param name="hash">The explicit torrent hash.</param>
    /// <param name="category">The category name, or <see langword="null"/> when uncategorized.</param>
    /// <param name="remainingBytes">The selected content bytes remaining.</param>
    /// <param name="isStopped">Whether qBittorrent reports a deliberate stopped state.</param>
    /// <param name="tags">The exact tag names.</param>
    public TorrentSnapshot(
        string hash,
        string? category,
        long remainingBytes,
        bool isStopped,
        IEnumerable<string> tags)
    {
        if (string.IsNullOrWhiteSpace(hash)
            || string.Equals(hash, "all", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("A torrent requires an explicit non-special hash.", nameof(hash));
        }

        if (remainingBytes < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(remainingBytes),
                remainingBytes,
                "Remaining bytes cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(tags);
        Hash = hash;
        Category = category;
        RemainingBytes = remainingBytes;
        IsStopped = isStopped;
        Tags = tags.ToFrozenSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Gets the explicit torrent hash.
    /// </summary>
    public string Hash { get; }

    /// <summary>
    /// Gets the category name, or <see langword="null"/> when uncategorized.
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// Gets the selected content bytes remaining.
    /// </summary>
    public long RemainingBytes { get; }

    /// <summary>
    /// Gets a value indicating whether the torrent is deliberately stopped.
    /// </summary>
    public bool IsStopped { get; }

    /// <summary>
    /// Gets the exact tag names.
    /// </summary>
    public IReadOnlySet<string> Tags { get; }

    /// <summary>
    /// Gets a value indicating whether all selected content is complete.
    /// </summary>
    public bool IsCompleted => RemainingBytes == 0;
}
