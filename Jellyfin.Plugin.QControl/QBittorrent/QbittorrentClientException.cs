using System;
using System.Diagnostics.CodeAnalysis;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// A bounded qBittorrent failure that never embeds response or credential content.
/// </summary>
[SuppressMessage(
    "Design",
    "CA1032:Implement standard exception constructors",
    Justification = "Arbitrary messages and inner exceptions could expose credentials or response bodies.")]
public sealed class QbittorrentClientException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="QbittorrentClientException"/> class.
    /// </summary>
    /// <param name="error">The stable failure category.</param>
    /// <param name="message">The bounded secret-safe message.</param>
    public QbittorrentClientException(QbittorrentClientError error, string message)
        : base(message)
    {
        Error = error;
    }

    /// <summary>
    /// Gets the stable failure category.
    /// </summary>
    public QbittorrentClientError Error { get; }
}
