using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Jellyfin.Plugin.QControl.Journal;

namespace Jellyfin.Plugin.QControl.Configuration;

/// <summary>
/// Bounded read-only qBittorrent connection evidence.
/// </summary>
public sealed record QbittorrentConnectionProbeResult
{
    private QbittorrentConnectionProbeResult(
        bool isConnected,
        Version? applicationVersion,
        Version? webApiVersion,
        IEnumerable<string> categories,
        JournalFailureCode? failure)
    {
        IsConnected = isConnected;
        ApplicationVersion = applicationVersion?.ToString();
        WebApiVersion = webApiVersion?.ToString();
        Categories = new ReadOnlyCollection<string>(categories.Order(StringComparer.Ordinal).ToArray());
        Failure = failure;
    }

    /// <summary>Gets a value indicating whether authentication and compatibility succeeded.</summary>
    public bool IsConnected { get; }

    /// <summary>Gets the compatible qBittorrent application version.</summary>
    public string? ApplicationVersion { get; }

    /// <summary>Gets the compatible Web API version.</summary>
    public string? WebApiVersion { get; }

    /// <summary>Gets deterministic exact category names.</summary>
    public IReadOnlyList<string> Categories { get; }

    /// <summary>Gets the bounded failure, if any.</summary>
    public JournalFailureCode? Failure { get; }

    /// <summary>Creates successful connection evidence.</summary>
    /// <param name="applicationVersion">The compatible application version.</param>
    /// <param name="webApiVersion">The compatible Web API version.</param>
    /// <param name="categories">The exact discovered category names.</param>
    /// <returns>Successful bounded evidence.</returns>
    public static QbittorrentConnectionProbeResult Connected(
        Version applicationVersion,
        Version webApiVersion,
        IEnumerable<string> categories)
    {
        ArgumentNullException.ThrowIfNull(applicationVersion);
        ArgumentNullException.ThrowIfNull(webApiVersion);
        ArgumentNullException.ThrowIfNull(categories);
        return new QbittorrentConnectionProbeResult(
            true,
            applicationVersion,
            webApiVersion,
            categories,
            null);
    }

    /// <summary>Creates bounded failed connection evidence.</summary>
    /// <param name="failure">The bounded failure.</param>
    /// <returns>Failed bounded evidence.</returns>
    public static QbittorrentConnectionProbeResult Failed(JournalFailureCode failure)
    {
        return new QbittorrentConnectionProbeResult(false, null, null, [], failure);
    }
}
