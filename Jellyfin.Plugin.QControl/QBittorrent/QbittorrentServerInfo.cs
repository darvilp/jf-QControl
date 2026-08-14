using System;

namespace Jellyfin.Plugin.QControl.QBittorrent;

/// <summary>
/// Compatible qBittorrent application and Web API versions.
/// </summary>
/// <param name="ApplicationVersion">The parsed qBittorrent application version.</param>
/// <param name="WebApiVersion">The parsed Web API version.</param>
public sealed record QbittorrentServerInfo(
    Version ApplicationVersion,
    Version WebApiVersion);
