namespace Jellyfin.Plugin.QControl.Journal;

/// <summary>
/// Credential-free qBittorrent endpoint identity captured by an activation.
/// </summary>
/// <param name="Scheme">The HTTP or HTTPS scheme.</param>
/// <param name="Host">The host name or address.</param>
/// <param name="Port">The explicit resolved port.</param>
/// <param name="BasePath">The Web UI base path.</param>
public sealed record QbittorrentEndpointIdentity(
    string Scheme,
    string Host,
    int Port,
    string BasePath);
