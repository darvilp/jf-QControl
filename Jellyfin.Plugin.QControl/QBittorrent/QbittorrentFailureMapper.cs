using System;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// Maps client failures into the only categories safe for persistence and API responses.
/// </summary>
public static class QbittorrentFailureMapper
{
    /// <summary>Maps a qBittorrent client error.</summary>
    /// <param name="error">The internal bounded error.</param>
    /// <returns>The journal-safe failure.</returns>
    public static JournalFailureCode Map(QbittorrentClientError error)
    {
        return error switch
        {
            QbittorrentClientError.Timeout => JournalFailureCode.Timeout,
            QbittorrentClientError.Connection => JournalFailureCode.Connection,
            QbittorrentClientError.Authentication => JournalFailureCode.Authentication,
            QbittorrentClientError.InvalidResponse => JournalFailureCode.InvalidResponse,
            QbittorrentClientError.UnsupportedVersion => JournalFailureCode.UnsupportedVersion,
            QbittorrentClientError.Credential => JournalFailureCode.Credential,
            _ => throw new ArgumentOutOfRangeException(nameof(error), error, "Unknown qBittorrent failure."),
        };
    }
}
